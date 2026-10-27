using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;

partial class QuotaWidget {
    sealed class ClientRoots {
        public string Home,Roaming,Local,ClaudeHome;
        public static ClientRoots Current() {
            var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string claude=ClaudeDirectoryOverride; if(string.IsNullOrWhiteSpace(claude))claude=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");if(string.IsNullOrWhiteSpace(claude))claude=Path.Combine(home,".claude");
            return new ClientRoots {Home=home,Roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),Local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ClaudeHome=claude};
        }
    }
    sealed class ClientCredential {
        public string Header,Source,Organization,LoginVersion="",StoreVersion="";
        public bool OAuth;
        public DateTimeOffset Expires=DateTimeOffset.MaxValue;
    }
    static readonly object credentialLock=new object();
    // Process memory only; never serialized. Locked Desktop cookie files can use
    // the already-read session until expiry/rejection, without copying the DB.
    static readonly Dictionary<string,ClientCredential> credentialMemory=new Dictionary<string,ClientCredential>();
    static ClientCredential LoadClientCredential(string path,Func<ClientCredential> load,string extra=null) {
        lock(credentialLock) {
            if(!File.Exists(path)){credentialMemory.Remove(path);return null;}
            ClientCredential previous;credentialMemory.TryGetValue(path,out previous);
            if(previous!=null && previous.Expires<=DateTimeOffset.UtcNow) {credentialMemory.Remove(path);previous=null;}
            string stamp=LoginFileVersion(path,extra);
            try {
                var result=load();
                if(stamp!=LoginFileVersion(path,extra)){
                    // Cursor shares its auth database with unrelated live state. A
                    // second transactional identity read distinguishes a WAL update
                    // from a real account change without requiring the DB to be idle.
                    if(result==null || !result.Source.StartsWith("Cursor",StringComparison.Ordinal))throw new QuotaFetchException("登录文件正在变化，请稍后重试。","Login file changed during the read. Retry shortly.");
                    var confirmed=load();
                    if(confirmed==null || confirmed.LoginVersion!=result.LoginVersion)throw new QuotaFetchException("Cursor 账户在读取期间变化，请重新刷新。","Cursor account changed during the read. Refresh again.");
                    result=confirmed;stamp=LoginFileVersion(path,extra);
                }
                if(result!=null){result.StoreVersion=stamp;if(result.LoginVersion.Length==0)result.LoginVersion=stamp;credentialMemory[path]=result;}else credentialMemory.Remove(path);return result;
            }
            catch(IOException) {
                if(previous!=null && previous.StoreVersion==stamp && stamp==LoginFileVersion(path,extra))return previous;
                credentialMemory.Remove(path);
                throw new QuotaFetchException("客户端登录文件被锁定或已变化，请关闭客户端后刷新一次，再重新打开。","Client login file is locked or changed. Close the client, refresh once, then reopen it.");
            }
            catch {credentialMemory.Remove(path);throw;}
        }
    }
    // Read guard metadata only, never a token or token hash. Cursor quota caches
    // bind stable account identity separately; unrelated WAL updates are not logout.
    static string LoginFileVersion(string path,string extra=null) {
        var stamp=new StringBuilder();
        foreach(string file in new[]{path,path+"-wal",extra}) {
            if(file==null)continue;
            var info=new FileInfo(file);info.Refresh();
            stamp.Append(Path.GetFullPath(file)).Append('|').Append(info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture)+":"+info.LastWriteTimeUtc.Ticks+":"+info.CreationTimeUtc.Ticks : "missing").Append('\n');
        }
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(stamp.ToString()))).Replace("-","");
    }
    static IEnumerable<string> CursorCliPaths(ClientRoots roots) {return new[]{Path.Combine(roots.Roaming,"Cursor","auth.json"),Path.Combine(roots.Home,".config","cursor","auth.json"),Path.Combine(roots.Home,".cursor","auth.json")};}
    static string ClaudeCookiePath(ClientRoots roots) {
        var profiles=new List<string>{Path.Combine(roots.Roaming,"Claude")};string packages=Path.Combine(roots.Local,"Packages");
        if(Directory.Exists(packages))foreach(string package in Directory.EnumerateDirectories(packages,"Claude_*"))profiles.Add(Path.Combine(package,"LocalCache","Roaming","Claude"));
        return profiles.SelectMany(profile=>new[]{Path.Combine(profile,"Network","Cookies"),Path.Combine(profile,"Cookies")}).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }
    static string CookieProfile(string path){string profile=Path.GetDirectoryName(path);return Path.GetFileName(profile)=="Network"?Path.GetDirectoryName(profile):profile;}
    static bool CacheMatchesLogin(Snapshot data,string provider,string mode,ClientRoots roots,bool allowRead=true) {
        if(provider=="codex")return true;
        if(mode=="local")return ClaudeUsageFile.Length>0 ? data.Source=="Claude Code usage file" && data.LoginVersion==PublicBinding(Path.GetFullPath(ClaudeUsageFile)) : data.Source=="Claude Code statusLine" || data.Source=="Claude Code Mods";
        if(provider=="cursor")return CursorLoginMatch(data,mode,roots,allowRead,!allowRead)==1;
        try {
            string path=null,extra=null;bool cli=mode!="desktop";
            if(cli)path=provider=="cursor" ? CursorCliPaths(roots).FirstOrDefault(File.Exists) : Path.Combine(roots.ClaudeHome,".credentials.json");
            if(path==null || !File.Exists(path)) {
                if(mode=="cli")return false;
                cli=false;path=provider=="cursor"?Path.Combine(roots.Roaming,"Cursor","User","globalStorage","state.vscdb"):ClaudeDesktopLoginPath(roots);
                if(path==null || !File.Exists(path))return false;
                if(provider=="claude")extra=Path.Combine(CookieProfile(path),"Local State");
            }
            string source=(provider=="cursor"?"Cursor":"Claude")+(cli?" CLI account":" Desktop account");
            return data.Source==source && data.LoginVersion.Length>0 && data.LoginVersion==LoginFileVersion(path,extra);
        }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(ArgumentException){return false;}catch(NotSupportedException){return false;}
    }
    static string PublicBinding(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");}
    // 1=verified same account, 0=verified different/missing source, -1=temporarily
    // unverifiable. File/WAL metadata changes are not evidence of an account switch.
    static int CursorLoginMatch(Snapshot data,string mode,ClientRoots roots,bool allowRead,bool confirmedQuery=false){
        try{
            string path=mode=="desktop"?null:CursorCliPaths(roots).FirstOrDefault(File.Exists);
            bool cli=path!=null;
            if(path==null && mode!="cli")path=Path.Combine(roots.Roaming,"Cursor","User","globalStorage","state.vscdb");
            if(path==null || !File.Exists(path))return 0;
            string source=cli?"Cursor CLI account":"Cursor Desktop account";
            if(data.Source!=source)return 0;
            ClientCredential credential;
            lock(credentialLock){credentialMemory.TryGetValue(path,out credential);}
            if(credential!=null && credential.LoginVersion!=data.LoginVersion)return 0;
            // Query results have just re-read identity on the worker. A subsequent
            // unrelated write cannot invalidate that observation on the UI thread.
            if(confirmedQuery && credential!=null)return 1;
            if(credential==null || credential.StoreVersion!=LoginFileVersion(path)){
                if(!allowRead)return -1;
                credential=ReadCursorCredential(mode,roots);
            }
            return credential!=null && data.LoginVersion.Length>0 && credential.LoginVersion==data.LoginVersion?1:0;
        }catch(IOException){return -1;}catch(UnauthorizedAccessException){return -1;}catch(QuotaFetchException){return -1;}catch(FormatException){return -1;}catch(ArgumentException){return -1;}
    }
    static bool ShouldDropSnapshotForLogin(Snapshot data,string provider,string mode,ClientRoots roots){
        return provider=="cursor"?CursorLoginMatch(data,mode,roots,false)==0:!CacheMatchesLogin(data,provider,mode,roots);
    }
    static void ForgetClientCredentials() {lock(credentialLock)credentialMemory.Clear();}
    static void ForgetProviderCredentials(string provider) {
        string prefix=provider=="claude"?"Claude":"Cursor";
        lock(credentialLock)foreach(string path in credentialMemory.Where(pair=>pair.Value.Source.StartsWith(prefix,StringComparison.Ordinal)).Select(pair=>pair.Key).ToArray())credentialMemory.Remove(path);
    }
    static void ForgetClientCredential(ClientCredential value) {
        if(value==null)return;
        lock(credentialLock)foreach(string path in credentialMemory.Where(pair=>object.ReferenceEquals(pair.Value,value)).Select(pair=>pair.Key).ToArray())credentialMemory.Remove(path);
    }
    static string ReadSmallClientFile(string path) {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
            if(stream.Length>1048576)throw new FormatException("Client login file exceeds size limit.");
            using(var reader=new StreamReader(stream,Encoding.UTF8))return reader.ReadToEnd();
        }
    }
    static string ClientSecret(string value) {
        if(string.IsNullOrWhiteSpace(value) || value.Length>32768 || value.Any(c=>c<=32 || c>=127 || c==';'))throw new FormatException("Invalid client login data.");
        return value;
    }
    static ClientCredential CursorToken(string token,string source) {
        token=ClientSecret(token);string[] parts=token.Split('.');
        if(parts.Length!=3 || token.Any(c=>!char.IsLetterOrDigit(c) && c!='.' && c!='_' && c!='-'))throw new FormatException("Unsupported Cursor login format.");
        string encoded=parts[1].Replace('-','+').Replace('_','/');encoded=encoded.PadRight(encoded.Length+(4-encoded.Length%4)%4,'=');
        var claims=QuotaJson(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
        var subject=Value(claims,"sub") as string;
        if(subject==null)throw new FormatException("Cursor login identity is missing.");
        string id=subject.Split('|').Last();
        if(id.Length==0 || id.Any(c=>!char.IsLetterOrDigit(c) && c!='_' && c!='-' && c!='.'))throw new FormatException("Invalid Cursor login identity.");
        var expiry=QuotaNumber(claims,"exp");if(!expiry.HasValue)throw new FormatException("Cursor login expiry is missing.");
        var expires=DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(expiry.Value));
        if(expires<=DateTimeOffset.UtcNow.AddSeconds(15))throw new QuotaFetchException("Cursor 登录已过期，请在客户端重新登录。","Cursor sign-in expired. Sign in again in the client.");
        return new ClientCredential {Header="WorkosCursorSessionToken="+id+"%3A%3A"+token,Source=source,Expires=expires,LoginVersion=PublicBinding("Cursor account|"+source+"|"+id)};
    }
    static ClientCredential ReadCursorCredential(string mode,ClientRoots roots) {
        if(mode!="desktop") {
            foreach(string path in CursorCliPaths(roots)) {
                if(!File.Exists(path))continue;
                return LoadClientCredential(path,()=>{
                    string token=Value(QuotaJson(ReadSmallClientFile(path)),"accessToken") as string;
                    if(token==null)throw new FormatException("Cursor CLI login is missing.");
                    return CursorToken(token,"Cursor CLI account");
                });
            }
        }
        if(mode!="cli") {
            string path=Path.Combine(roots.Roaming,"Cursor","User","globalStorage","state.vscdb");
            var credential=LoadClientCredential(path,()=>{
                var rows=ClientSqlite.Read(path,"SELECT value FROM ItemTable WHERE key='cursorAuth/accessToken' LIMIT 1");
                if(rows.Count==0)throw new FormatException("Cursor Desktop login is missing.");
                var data=rows[0][0] as byte[];string token=data==null ? rows[0][0] as string : Encoding.UTF8.GetString(data);
                if(data!=null && token.IndexOf('\0')>=0 && data.Length%2==0)token=Encoding.Unicode.GetString(data);
                return CursorToken(token,"Cursor Desktop account");
            });
            if(credential!=null)return credential;
        }
        throw new QuotaFetchException("未找到所选 Cursor 客户端的登录态，请先登录。","No sign-in found for the selected Cursor source. Sign in first.");
    }
    static ClientCredential ReadClaudeCredential(string mode,ClientRoots roots) {
        if(mode!="desktop") {
            string path=Path.Combine(roots.ClaudeHome,".credentials.json");
            if(File.Exists(path))return LoadClientCredential(path,()=>{
                var oauth=Map(Value(QuotaJson(ReadSmallClientFile(path)),"claudeAiOauth"));
                string token=Value(oauth,"accessToken") as string;
                if(token==null)throw new QuotaFetchException("Claude CLI 未使用订阅登录，无法读取订阅额度。","Claude CLI is not signed in with a subscription.");
                var expires=QuotaNumber(oauth,"expiresAt");
                var expiry=expires.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(expires.Value)) : DateTimeOffset.MaxValue;
                if(expiry<=DateTimeOffset.UtcNow)throw new QuotaFetchException("Claude CLI 登录已过期，请在客户端重新登录。","Claude CLI sign-in expired. Sign in again in the client.");
                return new ClientCredential {Header=ClientSecret(token),Source="Claude CLI account",OAuth=true,Expires=expiry};
            });
        }
        if(mode!="cli") {
            string path=ClaudeDesktopLoginPath(roots);
            if(path!=null) {
                string profile=CookieProfile(path);
                return LoadClientCredential(path,()=>Path.GetFileName(path)=="config.json"?ReadClaudeDesktopOAuth(path,profile):ReadClaudeDesktopCookie(path,profile),Path.Combine(profile,"Local State"));
            }
        }
        throw new QuotaFetchException("未找到所选 Claude 客户端的登录态，请先登录。","No sign-in found for the selected Claude source. Sign in first.");
    }
    static ClientCredential ReadClaudeDesktopCookie(string path,string profile) {
        var rows=ClientSqlite.Read(path,"SELECT name,value,encrypted_value,expires_utc,host_key FROM cookies WHERE host_key IN ('.claude.ai','claude.ai') AND name IN ('sessionKey','lastActiveOrg') ORDER BY last_access_utc DESC LIMIT 16");
        var metadata=ClientSqlite.Read(path,"SELECT value FROM meta WHERE key='version' LIMIT 1");
        int version=0;if(metadata.Count>0)int.TryParse(Convert.ToString(metadata[0][0],CultureInfo.InvariantCulture),out version);
        byte[] key=null;string token=null,org=null;DateTimeOffset expires=DateTimeOffset.MaxValue;
        try {
            foreach(var row in rows) {
                string name=row[0] as string;if(name=="sessionKey" && token!=null || name=="lastActiveOrg" && org!=null)continue;
                long micros=row[3] is long ? (long)row[3] : 0;
                DateTimeOffset expiry=micros>0 ? new DateTimeOffset(1601,1,1,0,0,0,TimeSpan.Zero).AddTicks(checked(micros*10)) : DateTimeOffset.MaxValue;
                if(expiry<=DateTimeOffset.UtcNow)continue;
                string value=row[1] as string;
                if(string.IsNullOrEmpty(value)) {
                    byte[] encrypted=row[2] as byte[];if(encrypted==null || encrypted.Length==0)continue;
                    if(encrypted[0]=='v' && key==null) {
                        string state=Path.Combine(profile,"Local State");
                        var crypto=Map(Value(QuotaJson(ReadSmallClientFile(state)),"os_crypt"));
                        key=ClientCrypto.UnwrapKey(CacheString(crypto,"encrypted_key"));
                    }
                    byte[] plain=ClientCrypto.DecryptCookie(encrypted,key);
                    try {
                        int offset=0;
                        if(version>=24) {
                            byte[] hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(Encoding.UTF8.GetBytes((string)row[4]));
                            if(plain.Length<32 || !hash.SequenceEqual(plain.Take(32)))throw new CryptographicException("Client cookie host binding failed.");
                            offset=32;
                        }
                        value=new UTF8Encoding(false,true).GetString(plain,offset,plain.Length-offset);
                    } finally {Array.Clear(plain,0,plain.Length);}
                }
                if(name=="sessionKey"){token=ClientSecret(value);expires=expiry;}
                else {Guid id;if(Guid.TryParse(Uri.UnescapeDataString(value),out id))org=id.ToString("D");}
            }
            if(token==null)throw new QuotaFetchException("Claude 桌面端没有可用会话，请先登录。","No usable Claude Desktop session. Sign in first.");
            return new ClientCredential {Header=token,Organization=org,Source="Claude Desktop account",Expires=expires};
        } finally {if(key!=null)Array.Clear(key,0,key.Length);}
    }
}
