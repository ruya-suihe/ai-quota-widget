using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;

partial class QuotaWidget {
    static IEnumerable<string> ClaudeDesktopProfiles(ClientRoots roots){
        var profiles=new List<string>{Path.Combine(roots.Roaming,"Claude"),Path.Combine(roots.Local,"Claude")};
        string packages=Path.Combine(roots.Local,"Packages");
        if(Directory.Exists(packages))foreach(string pattern in new[]{"Claude_*","AnthropicPBC.Claude_*"})
            foreach(string package in Directory.EnumerateDirectories(packages,pattern).Take(32))profiles.Add(Path.Combine(package,"LocalCache","Roaming","Claude"));
        return profiles.Distinct(StringComparer.OrdinalIgnoreCase);
    }
    static string ClaudeDesktopLoginPath(ClientRoots roots){
        var candidates=new List<string>();
        foreach(string profile in ClaudeDesktopProfiles(roots)){
            string config=Path.Combine(profile,"config.json");bool oauth=false;
            if(File.Exists(config)){
                try{var data=QuotaJson(ReadSmallClientFile(config));oauth=data.ContainsKey("oauth:tokenCacheV2") || data.ContainsKey("oauth:tokenCache");}
                catch(IOException){oauth=true;}catch(UnauthorizedAccessException){oauth=true;}catch(FormatException){oauth=true;}catch(ArgumentException){oauth=true;}
            }
            if(oauth){candidates.Add(config);continue;}
            foreach(string path in new[]{Path.Combine(profile,"Network","Cookies"),Path.Combine(profile,"Cookies")})if(File.Exists(path)){candidates.Add(path);break;}
        }
        return candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }
    sealed class DesktopGrant {internal string Token,Account;internal DateTimeOffset Expires;internal int Rank;}
    static DateTimeOffset DesktopGrantExpiry(object value){
        if(IsJsonNumber(value)){
            double number=Convert.ToDouble(value,CultureInfo.InvariantCulture);
            if(double.IsNaN(number) || double.IsInfinity(number) || number<=0)throw new FormatException();
            return number<100000000000?DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(number)):DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(number));
        }
        DateTimeOffset parsed;
        if(value is string && DateTimeOffset.TryParse((string)value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out parsed))return parsed;
        throw new FormatException("Missing Desktop grant expiry.");
    }
    static ClientCredential ReadClaudeDesktopOAuth(string path,string profile){
        byte[] key=null,encrypted=null,plain=null;
        try{
            var config=QuotaJson(ReadSmallClientFile(path));
            // A present V2 is authoritative. Never revive an older V1 grant after
            // logout, corruption, expiration or a server rejection of the live grant.
            string cacheKey=config.ContainsKey("oauth:tokenCacheV2")?"oauth:tokenCacheV2":"oauth:tokenCache";
            string encoded=Value(config,cacheKey) as string;
            if(string.IsNullOrWhiteSpace(encoded))throw new QuotaFetchException("Claude 桌面授权缓存为空，请确认桌面端登录状态。","Claude Desktop authorization cache is empty. Check Desktop sign-in.");
            encrypted=Convert.FromBase64String(encoded);
            if(encrypted.Length>=3 && encrypted[0]=='v'){
                var state=QuotaJson(ReadSmallClientFile(Path.Combine(profile,"Local State")));
                key=ClientCrypto.UnwrapKey(CacheString(Map(Value(state,"os_crypt")),"encrypted_key"));
            }
            plain=ClientCrypto.DecryptCookie(encrypted,key);
            var cache=QuotaJson(new UTF8Encoding(false,true).GetString(plain));var grants=new List<DesktopGrant>();
            string active=Value(config,"lastKnownAccountUuid") as string;
            foreach(var entry in cache){
                string name=entry.Key;if(name.Length>4096)continue;
                string host=":https://api.anthropic.com:";int separator=name.IndexOf(host,StringComparison.Ordinal);
                if(separator<0){host=":https://api.anthropic.com/:";separator=name.IndexOf(host,StringComparison.Ordinal);}
                if(separator<0)continue;
                string prefix=name.Substring(0,separator);int split=prefix.LastIndexOf(':');if(split<0)continue;
                string account=prefix.Substring(split+1);if(account.Length==0 || account.Length>256)continue;
                if(!string.IsNullOrEmpty(active)){
                    // Current Desktop builds can qualify the account identifier
                    // inside a composite prefix. Match the complete active value at
                    // field boundaries instead of assuming the last colon field.
                    string pattern="(^|[^A-Za-z0-9_-])"+System.Text.RegularExpressions.Regex.Escape(active)+"($|[^A-Za-z0-9_-])";
                    if(!System.Text.RegularExpressions.Regex.IsMatch(prefix,pattern,System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant))continue;
                    account=active;
                }
                var scopes=name.Substring(separator+host.Length).Split(new[]{' ',','},StringSplitOptions.RemoveEmptyEntries);
                bool inference=scopes.Contains("user:inference"),identity=scopes.Contains("user:profile");if(!inference && !identity)continue;
                var data=Map(entry.Value);string token=Value(data,"token") as string;if(string.IsNullOrWhiteSpace(token))continue;
                DateTimeOffset expiry;try{expiry=DesktopGrantExpiry(Value(data,"expiresAt"));}catch(ArgumentException){continue;}catch(FormatException){continue;}catch(OverflowException){continue;}
                if(expiry<=DateTimeOffset.UtcNow.AddSeconds(15))continue;
                grants.Add(new DesktopGrant {Token=ClientSecret(token),Account=account,Expires=expiry,Rank=(identity?2:0)+(inference?1:0)});
            }
            if(grants.Select(item=>item.Account).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1)throw new QuotaFetchException("Claude 桌面端有多个账户且无法确认当前账户，未自动选择。","Claude Desktop has multiple accounts without a confirmed active account; none was selected.");
            var grant=grants.OrderByDescending(item=>item.Rank).ThenByDescending(item=>item.Expires).FirstOrDefault();
            if(grant==null)throw new QuotaFetchException("Claude 桌面端没有有效的 Code 授权，请在桌面端确认登录。","No valid Claude Desktop Code authorization. Check sign-in in Desktop.");
            // Only the access token is used, in memory. Never redeem refreshToken
            // or write config/Local State, nor open a browser or run Claude.
            return new ClientCredential {Header=grant.Token,Source="Claude Desktop account",OAuth=true,Expires=grant.Expires};
        }catch(CryptographicException){throw new QuotaFetchException("Windows 未能解密 Claude 桌面授权；请确认悬浮窗与 Claude 由同一 Windows 用户运行，或该保护格式尚未支持。","Windows could not decrypt Claude Desktop authorization. Run both apps under the same Windows user; this protection format may also be unsupported.");}
        catch(FormatException){throw new QuotaFetchException("Claude 桌面授权格式暂不支持，未改动登录文件。","Claude Desktop authorization format is unsupported; sign-in files were not changed.");}
        finally{
            if(plain!=null)Array.Clear(plain,0,plain.Length);if(key!=null)Array.Clear(key,0,key.Length);if(encrypted!=null)Array.Clear(encrypted,0,encrypted.Length);
        }
    }
    static string ClaudeDesktopDiscoveryInfo(ClientRoots roots){
        string path=ClaudeDesktopLoginPath(roots);
        return path==null?Text("桌面登录存储：未发现；已检查标准及商店版位置。","Desktop sign-in store: not found in standard or Store locations."):
            Text("桌面登录存储：","Desktop sign-in store: ")+path+"\n"+(Path.GetFileName(path)=="config.json"?
            Text("方式：桌面端 Code 加密 OAuth，只读；无需 CLI 或插件。","Method: read-only Desktop Code encrypted OAuth; no CLI or plugin required."):
            Text("方式：旧版桌面会话，只读。","Method: read-only legacy Desktop session."));
    }
}
