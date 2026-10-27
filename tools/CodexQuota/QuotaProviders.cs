using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.InteropServices;

partial class QuotaWidget {
    // Account providers own data semantics. CLI/Desktop are sources, not extra quotas.
    interface IQuotaProvider {
        string Id {get;}
        bool SupportsQuery {get;}
        ProviderRead Read(bool active,string source);
    }
    sealed class ProviderRead {
        public Snapshot Data;
        public string Warning="", Detail="";
        public DateTimeOffset RetryAt;
    }
    sealed class CodexProvider : IQuotaProvider {
        public string Id {get{return "codex";}}
        public bool SupportsQuery {get{return true;}}
        public ProviderRead Read(bool active,string source) {
            string warning="",detail="";
            var data=active ? QueryCodexSnapshot() : ReadCodexSnapshotFrom(null,out warning,out detail);
            if(data!=null) {data.Provider=Id;data.Source=active ? "Codex app-server" : "Codex sessions";}
            return new ProviderRead {Data=data,Warning=warning,Detail=detail};
        }
    }
    sealed class ClaudeProvider : IQuotaProvider {
        public string Id {get{return "claude";}}
        public bool SupportsQuery {get{return ProviderSource("claude")!="local";}}
        public ProviderRead Read(bool active,string source) {
            if(active)return new ProviderRead {Data=QueryClaudeOnline(source,ClientRoots.Current(),SendQuotaRequest)};
            if(source!="local")return new ProviderRead();
            if(ClaudeUsageFile.Length>0)return new ProviderRead {Data=ReadClaudeUsageFile(ClaudeUsageFile,DateTimeOffset.UtcNow),Detail="Claude Code usage file"};
            string path=Path.Combine(ProviderFolder(),"claude-statusline.json");
            return new ProviderRead {Data=ReadProviderCache(path,Id),Detail="Claude Code statusLine"};
        }
    }
    sealed class CursorProvider : IQuotaProvider {
        public string Id {get{return "cursor";}}
        public bool SupportsQuery {get{return true;}}
        public ProviderRead Read(bool active,string source) {
            return active ? QueryCursorOnlineRead(source,ClientRoots.Current(),SendQuotaRequest) : new ProviderRead();
        }
    }
    static readonly IQuotaProvider[] quotaProviders={new CodexProvider(),new ClaudeProvider(),new CursorProvider()};
    static IQuotaProvider currentProvider=quotaProviders[0];
    static int providerGeneration;
    static bool providerInitialized;
    static string providerCacheWarning="";
    static string providerSelectionWarning="";
    sealed class ProviderState {
        public Snapshot Data;
        public bool Failed;
        public string Error="",Warning="",Detail="",CacheWarning="";
        public int Failures;
        public DateTimeOffset Next=DateTimeOffset.MinValue,LocalFailure=DateTimeOffset.MinValue,Completed,Started;
        public DateTimeOffset RetryNotBefore=DateTimeOffset.MinValue;
    }
    static readonly Dictionary<string,ProviderState> providerStates=new Dictionary<string,ProviderState>();
    static readonly Dictionary<string,string> providerSources=new Dictionary<string,string>{{"codex","auto"},{"claude","auto"},{"cursor","auto"}};
    static string ProviderSource(string id){return providerSources[id];}
    static void SetProviderSource(string source,bool force=false) {
        if(!new[]{"auto","cli","desktop","local"}.Contains(source) || currentProvider.Id=="codex" && source!="auto" || currentProvider.Id=="cursor" && source=="local")throw new ArgumentException("Invalid source.");
        if(ProviderSource(currentProvider.Id)==source && !force)return;
        providerSources[currentProvider.Id]=source;providerGeneration++;
        ForgetProviderCredentials(currentProvider.Id);
        // An explicit source change can select a different account. Do not carry
        // over that source's quota/error, and do not bypass a server cooldown.
        snapshot=null;queryFailed=false;queryError=localReadWarning="";activeQueryFailures=0;
        nextAutoQuery=providerRetryNotBefore;reading=activeReading=false;
        localFailureSince=DateTimeOffset.MinValue;providerCacheWarning="";
        Control<Button>("RefreshButton").IsEnabled=true;
        if(!checking) {
            try {Directory.CreateDirectory(ProviderFolder());File.WriteAllText(Path.Combine(ProviderFolder(),currentProvider.Id+"-source.txt"),source);providerSelectionWarning="";}
            catch(IOException){providerSelectionWarning=Text("登录来源未能保存。","Could not save login source.");}
            catch(UnauthorizedAccessException){providerSelectionWarning=Text("登录来源未能保存。","Could not save login source.");}
        }
        Render();if(!checking)Refresh(currentProvider.SupportsQuery);
    }
    static string ProviderFolder() {return Path.Combine(Path.GetDirectoryName(PreferenceFile()),"providers");}
    static string ProviderCacheFile(string id) {return Path.Combine(ProviderFolder(),id+".json");}
    static void RememberProvider() {
        providerStates[currentProvider.Id]=new ProviderState {Data=snapshot,Failed=queryFailed,Error=queryError,
            Warning=localReadWarning,Detail=diagnostic,CacheWarning=providerCacheWarning,Failures=activeQueryFailures,Next=nextAutoQuery,
            LocalFailure=localFailureSince,Completed=lastActiveCompleted,Started=activeQueryStarted};
        providerStates[currentProvider.Id].RetryNotBefore=providerRetryNotBefore;
    }
    static void SelectProvider(string id,bool persist) {
        var provider=quotaProviders.FirstOrDefault(item=>item.Id==id);
        if(provider==null) throw new ArgumentException("Unknown quota provider.");
        if(provider==currentProvider && persist) return;
        if(providerInitialized) RememberProvider();currentProvider=provider;providerGeneration++;providerInitialized=true;
        ProviderState state;
        if(!providerStates.TryGetValue(id,out state)) {
            state=new ProviderState();
            try {state.Data=ReadProviderCache(ProviderCacheFile(id),id);}
            catch(Exception ex) {whenCacheRead(ex,()=>state.CacheWarning=Text("上次额度记录损坏，等待重新读取。","Saved quota data is invalid; waiting for a fresh read."));}
            if(!checking)try {
                string path=Path.Combine(ProviderFolder(),id+"-retry.txt");DateTimeOffset until;
                if(File.Exists(path) && new FileInfo(path).Length<256 && DateTimeOffset.TryParse(File.ReadAllText(path),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out until) && until>DateTimeOffset.UtcNow)state.RetryNotBefore=state.Next=until;
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        string sourceMode=ProviderSource(id);
        if(state.Data!=null && !CacheMatchesLogin(state.Data,id,sourceMode,ClientRoots.Current()))state.Data=null;
        snapshot=state.Data;queryFailed=state.Failed;queryError=state.Error;localReadWarning=state.Warning;diagnostic=state.Detail;
        providerCacheWarning=state.CacheWarning;activeQueryFailures=state.Failures;nextAutoQuery=state.Next;
        localFailureSince=state.LocalFailure;lastActiveCompleted=state.Completed;activeQueryStarted=state.Started;
        providerRetryNotBefore=state.RetryNotBefore;
        reading=activeReading=false;
        if(persist && !checking) {
            try {Directory.CreateDirectory(ProviderFolder());File.WriteAllText(Path.Combine(ProviderFolder(),"selected.txt"),id);providerSelectionWarning="";}
            catch(IOException) {providerSelectionWarning=Text("平台选择未能保存。","Could not save platform selection.");}
            catch(UnauthorizedAccessException) {providerSelectionWarning=Text("平台选择未能保存。","Could not save platform selection.");}
        }
        if(window!=null) {Control<Button>("RefreshButton").IsEnabled=true;ApplyLanguage();}
        if(persist && !checking) Refresh(provider.SupportsQuery);
    }
    // Keep malformed cache handling narrow; unexpected programming errors must surface.
    static void whenCacheRead(Exception ex,Action handler) {
        if(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException) handler();
        else throw ex;
    }
    static void LoadProviderSelection() {
        LoadClaudeConnectionOptions();
        string id="codex";
        foreach(string provider in new[]{"claude","cursor"}) {
            try {string sourcePath=Path.Combine(ProviderFolder(),provider+"-source.txt");string mode=File.Exists(sourcePath)?File.ReadAllText(sourcePath).Trim():"auto";
                if(new[]{"auto","cli","desktop"}.Contains(mode) || provider=="claude" && mode=="local")providerSources[provider]=mode;
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        try {string path=Path.Combine(ProviderFolder(),"selected.txt");if(File.Exists(path)) id=File.ReadAllText(path).Trim();}
        catch(IOException) {} catch(UnauthorizedAccessException) {}
        if(!quotaProviders.Any(item=>item.Id==id)) id="codex";
        SelectProvider(id,false);
    }
    static Dictionary<string,object> CacheLimit(Limit limit) {
        return limit==null ? null : new Dictionary<string,object>{{"remaining",limit.Remaining},{"reset",limit.Reset},{"minutes",limit.Minutes},{"label",limit.Label}};
    }
    static string SerializeProviderCache(Snapshot data) {
        var baseline=data.Live ? data : data.LiveBaseline;
        return new JavaScriptSerializer().Serialize(new Dictionary<string,object> {
            {"schema",1},{"provider",data.Provider},{"time",data.Time.ToString("o",CultureInfo.InvariantCulture)},
            {"hourTime",(data.HourTime==default(DateTimeOffset)?data.Time:data.HourTime).ToString("o",CultureInfo.InvariantCulture)},
            {"weekTime",(data.WeekTime==default(DateTimeOffset)?data.Time:data.WeekTime).ToString("o",CultureInfo.InvariantCulture)},
            {"source",data.Source},{"loginVersion",data.LoginVersion},{"hour",CacheLimit(data.Hour)},{"week",CacheLimit(data.Week)},
            {"authority",baseline==null ? null : new Dictionary<string,object>{{"time",baseline.Time.ToString("o",CultureInfo.InvariantCulture)},{"hour",baseline.Hour!=null},{"week",baseline.Week!=null}}}
        });
    }
    static string CacheString(Dictionary<string,object> map,string key) {
        var value=Value(map,key) as string;if(value==null)throw new FormatException("Invalid cache field: "+key);return value;
    }
    static Limit DecodeCacheLimit(object value) {
        if(value==null) return null;
        var map=Map(value);if(map==null || Value(map,"remaining")==null || Value(map,"reset")==null || Value(map,"minutes")==null) throw new FormatException("Invalid cached window.");
        foreach(string key in new[]{"remaining","minutes","reset"}) if(!IsJsonNumber(Value(map,key)))throw new FormatException("Invalid cache numeric field.");
        double remaining=Convert.ToDouble(Value(map,"remaining"),CultureInfo.InvariantCulture),minutes=Convert.ToDouble(Value(map,"minutes"),CultureInfo.InvariantCulture);
        long reset=Convert.ToInt64(Value(map,"reset"),CultureInfo.InvariantCulture);
        if(double.IsNaN(remaining) || double.IsInfinity(remaining) || remaining<0 || remaining>100 || double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes<0 || minutes>527040) throw new FormatException("Invalid cached quota.");
        if(reset!=0) DateTimeOffset.FromUnixTimeSeconds(reset);
        string label=Value(map,"label") as string??"";if(label!="" && label!="cycle")throw new FormatException("Invalid quota label.");
        return new Limit {Remaining=remaining,Reset=reset,Minutes=minutes,Label=label};
    }
    static bool IsJsonNumber(object value) {return value is int || value is long || value is double || value is decimal;}
    static Snapshot DecodeProviderCache(string text,string provider) {
        var map=Map(new JavaScriptSerializer {MaxJsonLength=16384}.DeserializeObject(text));
        if(map==null || !IsJsonNumber(Value(map,"schema")) || Convert.ToDouble(Value(map,"schema"),CultureInfo.InvariantCulture)!=1 || CacheString(map,"provider")!=provider) throw new FormatException("Cache provider/schema mismatch.");
        var time=DateTimeOffset.Parse(CacheString(map,"time"),CultureInfo.InvariantCulture);
        var hourTime=DateTimeOffset.Parse(CacheString(map,"hourTime"),CultureInfo.InvariantCulture);
        var weekTime=DateTimeOffset.Parse(CacheString(map,"weekTime"),CultureInfo.InvariantCulture);
        if(time>DateTimeOffset.UtcNow.AddMinutes(1) || hourTime>time || weekTime>time) throw new FormatException("Invalid cache timestamp.");
        var source=CacheString(map,"source");
        if(!new[]{"","Codex app-server","Codex sessions","Claude Code statusLine","Claude Code Mods","Claude Code usage file","Claude CLI account","Claude Desktop account","Cursor CLI account","Cursor Desktop account"}.Contains(source)) throw new FormatException("Invalid quota source.");
        var result=new Snapshot {Provider=provider,Time=time,HourTime=hourTime,WeekTime=weekTime,
            Source=source,Hour=DecodeCacheLimit(Value(map,"hour")),Week=DecodeCacheLimit(Value(map,"week"))};
        string loginVersion=Value(map,"loginVersion") as string??"";
        if(loginVersion.Length!=0 && (loginVersion.Length!=64 || loginVersion.Any(c=>!Uri.IsHexDigit(c))))throw new FormatException("Invalid cached login version.");
        result.LoginVersion=loginVersion;
        var authority=Map(Value(map,"authority"));
        if(Value(map,"authority")!=null && authority==null)throw new FormatException("Invalid cache authority.");
        if(authority!=null) {
            var observed=DateTimeOffset.Parse(CacheString(authority,"time"),CultureInfo.InvariantCulture);
            if(observed>time || !(Value(authority,"hour") is bool) || !(Value(authority,"week") is bool))throw new FormatException("Invalid cache authority.");
            result.LiveBaseline=new Snapshot {Provider=provider,Time=observed,Hour=(bool)Value(authority,"hour")?new Limit():null,Week=(bool)Value(authority,"week")?new Limit():null};
        }
        if(provider=="claude" && ((result.Hour!=null && result.Hour.Minutes!=300) || (result.Week!=null && result.Week.Minutes!=10080))) throw new FormatException("Unexpected Claude window.");
        return result;
    }
    static Snapshot SelectProviderSnapshot(string provider,Snapshot current,Snapshot candidate) {
        if(candidate!=null && candidate.Provider!=provider || current!=null && current.Provider!=provider) throw new FormatException("Quota provider mismatch.");
        if(provider=="codex") return NewerSnapshot(current,candidate);
        if(candidate!=null && candidate.Live)return candidate;
        // A locally collected event must not resurrect a missing window or a
        // higher cached balance after an authoritative network observation.
        if(provider=="claude" && current!=null && (current.Live || current.LiveBaseline!=null))return NewerSnapshot(current,candidate);
        return candidate!=null && (current==null || candidate.Time>current.Time) ? candidate : current;
    }
    static Snapshot ReadProviderCache(string path,string provider) {
        if(!File.Exists(path)) return null;
        if(new FileInfo(path).Length>16384) throw new FormatException("Quota cache exceeds size limit.");
        return DecodeProviderCache(File.ReadAllText(path),provider);
    }
    static void WriteProviderCache(string path,Snapshot data) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.WriteAllText(temporary,SerializeProviderCache(data));
            // Rename in the same directory; unlike ReplaceFile this does not need
            // permission to copy security metadata from the old file.
            if(!MoveFileEx(temporary,path,0x1|0x8)) throw new IOException("Could not atomically update quota cache.",new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        } finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool MoveFileEx(string existing,string destination,int flags);
    static void SaveProviderSnapshot() {
        if(checking || snapshot==null) return;
        try {WriteProviderCache(ProviderCacheFile(currentProvider.Id),snapshot);providerCacheWarning="";}
        catch(IOException) {providerCacheWarning=Text("额度已读取，但缓存未能保存。","Usage read, but its cache could not be saved.");}
        catch(UnauthorizedAccessException) {providerCacheWarning=Text("额度已读取，但缓存未能保存。","Usage read, but its cache could not be saved.");}
    }
    static Snapshot ParseClaudeStatus(string text,DateTimeOffset now) {
        var root=Map(new JavaScriptSerializer {MaxJsonLength=1048576}.DeserializeObject(text));
        var limits=Map(Value(root,"rate_limits"));
        // The upstream payload may carry prompts/paths; keep only quota fields.
        var result=new Snapshot {Provider="claude",Time=now,Source="Claude Code statusLine"};
        foreach(string name in new[]{"five_hour","seven_day"}) {
            var data=Map(Value(limits,name));if(data==null)continue;
            object used=Value(data,"used_percentage"),reset=Value(data,"resets_at");if(used==null || reset==null)continue;
            if(!IsJsonNumber(used) || !IsJsonNumber(reset))throw new FormatException("Invalid Claude quota field type.");
            double percent=Convert.ToDouble(used,CultureInfo.InvariantCulture);long seconds=Convert.ToInt64(reset,CultureInfo.InvariantCulture);
            if(double.IsNaN(percent) || double.IsInfinity(percent) || percent<0 || percent>100) throw new FormatException("Invalid Claude quota.");
            DateTimeOffset.FromUnixTimeSeconds(seconds);if(seconds<=now.ToUnixTimeSeconds())continue;
            var limit=new Limit {Remaining=100-percent,Reset=seconds,Minutes=name=="five_hour"?300:10080};
            if(name=="five_hour") result.Hour=limit;else result.Week=limit;
        }
        return result;
    }
    static int CollectClaudeStatus() {
        return CollectClaudeStatus(Console.In,Path.Combine(ProviderFolder(),"claude-statusline.json"));
    }
    static int CollectClaudeStatus(TextReader input,string path) {
        return CollectClaudeStatus(input,path,"Claude Code statusLine");
    }
    static int CollectClaudeStatus(TextReader input,string path,string source) {
        try {
            // Bound stdin; never store the original statusLine JSON or log secrets.
            var buffer=new char[1048577];int size=0,read;
            while(size<buffer.Length && (read=input.Read(buffer,size,buffer.Length-size))>0) size+=read;
            if(size>=buffer.Length) return 2;
            var data=ParseClaudeStatus(new string(buffer,0,size),DateTimeOffset.UtcNow);
            data.Source=source;
            // Missing rate_limits is not 100% free. Keep the previous timestamp.
            if(data.Hour!=null || data.Week!=null) {
                // Several Claude sessions may report concurrently. Serialize writes
                // so a slower older collector cannot overwrite a newer observation.
                string name;
                using(var hash=System.Security.Cryptography.SHA256.Create())name=BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-","");
                using(var mutex=new System.Threading.Mutex(false,"Local\\CodexQuotaClaude-"+name)) {
                    bool held=false;
                    try {
                        try {held=mutex.WaitOne(1500);}catch(System.Threading.AbandonedMutexException){held=true;}
                        if(!held)return 2;
                        Snapshot previous=null;
                        try {previous=ReadProviderCache(path,"claude");}catch(Exception ex){whenCacheRead(ex,()=>{});}
                        if(previous==null || data.Time>previous.Time)WriteProviderCache(path,data);
                    } finally {if(held)mutex.ReleaseMutex();}
                }
            }
            return 0;
        } catch(Exception) {return 2;}
    }
    static string ClaudeStatusLineCommand(string executable) {
        return ForwardCollectorCommand(executable,"--claude-statusline");
    }
    static string ForwardCollectorCommand(string executable,string arguments) {
        // EncodedCommand avoids different quoting rules in Git Bash/PowerShell,
        // and explicitly forwards stdin to the GUI-subsystem executable.
        string script="$p=New-Object System.Diagnostics.Process; $p.StartInfo=New-Object System.Diagnostics.ProcessStartInfo; "+
            "$p.StartInfo.FileName='"+executable.Replace("'","''")+"'; $p.StartInfo.Arguments='"+arguments.Replace("'","''")+"'; "+
            "$p.StartInfo.UseShellExecute=$false; $p.StartInfo.CreateNoWindow=$true; $p.StartInfo.RedirectStandardInput=$true; "+
            "$p.StartInfo.RedirectStandardOutput=$true; $p.StartInfo.RedirectStandardError=$true; "+
            "$raw=[Console]::In.ReadToEnd(); $p.Start() | Out-Null; $p.StandardInput.Write($raw); $p.StandardInput.Close(); "+
            "if(!$p.WaitForExit(5000)){$p.Kill();exit 2}; $code=$p.ExitCode; $p.Dispose(); if($code -eq 0){Write-Output 'Claude quota collector'}; exit $code";
        return "powershell -NoProfile -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
    }
    static string ProviderHelp() {
        if(currentProvider.Id=="codex") return Text("读取 Codex CLI／桌面端使用的账户额度。CLI 与桌面端使用不同账户时，以查询程序当前登录的账户为准。", "Reads account limits through Codex CLI/app-server. If CLI and desktop use different accounts, the query runtime's signed-in account is used.");
        if(currentProvider.Id=="claude" && ProviderSource("claude")=="local" && ClaudeUsageFile.Length>0)return Text("每15秒只读所选 Claude Code 用量文件；保留文件原始时间，不把旧数据当作刚更新。刷新不会触发 Claude 请求。","Reads the selected usage file every 15s. Keeps its original time and does not send Claude requests.");
        if(currentProvider.Id=="claude" && ProviderSource("claude")=="local")return Text("每15秒读取状态栏／Mods 的最近采集结果，不额外联网；长任务期间客户端可能没有提供新额度，旧数据不会视为实时数据。", "Reads statusLine/Mods every 15s without network calls. Older observations keep their original time.");
        if(currentProvider.Id=="claude" && ProviderSource("claude")=="desktop")return Text("只读 Claude 桌面端 Code 的已有授权，每15秒查询额度；无需安装 CLI、插件或修改 Claude 配置。", "Reads existing Desktop Code authorization and queries quota every 15s. No CLI, plugin or Claude configuration changes required.");
        if(currentProvider.Id=="claude") return Text("只读所选客户端登录态，每15秒联网查询订阅额度。自动优先 CLI；可指定桌面端。本地采集可另选为备用来源。登录过期时请在客户端重新登录。", "Queries the selected client every 15s. Auto prefers CLI. A custom config folder or local usage file can be selected below. Renew expired sign-in in Claude.");
        return Text("只读所选 Cursor 登录态，每15秒联网查询个人套餐额度；按真实账期显示，不把额外支出上限当套餐额度。自动优先 CLI；可指定桌面端。", "Read the selected Cursor sign-in and query personal plan usage every 15s. Uses the reported billing cycle, not on-demand spending caps. Auto prefers CLI; Desktop can be selected.");
    }
    static bool CommandExists(params string[] names) {
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) {
            if(string.IsNullOrWhiteSpace(folder))continue;
            foreach(string name in names)foreach(string suffix in new[]{".exe",".cmd",".ps1"}) {
                try {if(File.Exists(Path.Combine(folder.Trim('"'),name+suffix)))return true;}catch(ArgumentException) {}
            }
        }
        return false;
    }
    static string DetectProviderSources() {
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        bool cli=currentProvider.Id=="codex" ? CommandExists("codex") : currentProvider.Id=="claude" ? CommandExists("claude") : CommandExists("agent","cursor-agent");
        bool desktop=false;
        if(currentProvider.Id=="codex") desktop=Directory.Exists(Path.Combine(local,"OpenAI","Codex","bin"));
        if(currentProvider.Id=="cursor") desktop=File.Exists(Path.Combine(local,"Programs","cursor","Cursor.exe")) || File.Exists(Path.Combine(local,"cursor","Cursor.exe"));
        if(currentProvider.Id=="claude") {
            string folder=Path.Combine(local,"AnthropicClaude");
            if(Directory.Exists(folder)) {
                try {desktop=File.Exists(Path.Combine(folder,"claude.exe")) || Directory.EnumerateDirectories(folder,"app-*").Any(path=>File.Exists(Path.Combine(path,"claude.exe")));}
                catch(IOException) {} catch(UnauthorizedAccessException) {}
            }
            desktop=desktop || File.Exists(Path.Combine(local,"Programs","Claude","Claude.exe"));
        }
        return Text("常用路径检测：CLI ","Common-path detection: CLI ")+(cli?"✓":"—")+Text(" · 桌面端 "," · Desktop ")+(desktop?"✓":"—")+Text("（— 不代表未安装）"," (— does not prove absence)");
    }
    static string ProviderEmptyText() {
        return currentProvider.Id=="cursor" ? Text("暂无 Cursor 套餐额度","No Cursor plan quota available") : currentProvider.Id=="claude" ? Text("暂无 Claude 额度","No Claude quota available") : Text("暂无额度数据","No usage limits available");
    }
    static double QuotaHours(string prefix,Limit limit) {return limit!=null && limit.Minutes>0 ? limit.Minutes/60 : prefix=="Hour" ? 5 : 168;}
    static string QuotaRowLabel(string prefix,Limit limit,bool compact) {
        if(limit!=null && limit.Label=="cycle")return compact ? Text("周期","Cycle") : Text("套餐剩余","Plan remaining");
        double hours=QuotaHours(prefix,limit);
        if(hours==5) return compact ? Text("五","5h") : Text("5 小时剩余","5-hour remaining");
        if(hours==168) return compact ? Text("周","Wk") : Text("本周剩余","Weekly remaining");
        string duration=hours>=24 && hours%24==0 ? (hours/24).ToString("0.#",CultureInfo.InvariantCulture)+Text("天","d") : hours>=1 ? hours.ToString("0.#",CultureInfo.InvariantCulture)+Text("小时","h") : (hours*60).ToString("0.#",CultureInfo.InvariantCulture)+Text("分","m");
        return compact ? duration : duration+Text("剩余"," remaining");
    }
    static double PeriodReferenceMarker(Limit limit,double hours) {
        double seconds=limit.Reset-DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return limit.Reset<=0 || seconds<=0 || seconds>hours*3600 ? -1 : seconds/(hours*3600);
    }
    static string ProviderSourceLabel(string source){
        switch(source){
            case "Claude Desktop account":return Text("Claude 桌面端（含 Code）","Claude Desktop (incl. Code)");
            case "Claude CLI account":return "Claude Code CLI";
            case "Claude Code usage file":return Text("Claude Code 用量文件","Claude Code usage file");
            case "Claude Code statusLine":case "Claude Code Mods":return Text("Claude Code 本地数据","Claude Code local data");
            case "Cursor Desktop account":return Text("Cursor 桌面端","Cursor Desktop");
            case "Cursor CLI account":return "Cursor CLI";
            case "Codex app-server":return Text("Codex 账户","Codex account");
            case "Codex sessions":return Text("Codex 本地记录","Codex local records");
            default:return Text("平台数据","Provider data");
        }
    }
    static StackPanel CreateProviderSettings(out Action labels) {
        var panel=new StackPanel();var heading=new TextBlock();
        var selector=new ComboBox {Margin=new Thickness(0,0,0,6)};
        foreach(var provider in quotaProviders)selector.Items.Add(provider.Id=="codex"?"Codex":provider.Id=="claude"?"Claude":"Cursor");
        selector.SelectedIndex=Array.IndexOf(quotaProviders,currentProvider);
        var sourceRow=new Grid {Margin=new Thickness(0,0,0,6)};sourceRow.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(78)});sourceRow.ColumnDefinitions.Add(new ColumnDefinition());
        var sourceLabel=new TextBlock {VerticalAlignment=VerticalAlignment.Center};var sourceChoice=new ComboBox();
        sourceRow.Children.Add(sourceLabel);sourceRow.Children.Add(sourceChoice);Grid.SetColumn(sourceChoice,1);bool syncing=false;
        var claudeActions=new WrapPanel {Margin=new Thickness(0,5,0,0)};
        var directoryButton=new Button {Padding=new Thickness(6,4,6,4),Margin=new Thickness(0,0,5,4)};
        var usageButton=new Button {Padding=new Thickness(6,4,6,4),Margin=new Thickness(0,0,5,4)};
        var collectorButton=new Button {Padding=new Thickness(6,4,6,4),Margin=new Thickness(0,0,0,4)};
        claudeActions.Children.Add(directoryButton);claudeActions.Children.Add(usageButton);claudeActions.Children.Add(collectorButton);
        var description=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=SettingsBrush("Secondary")};
        var help=new Button {HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,4),Padding=new Thickness(6,4,6,4)};
        claudeActions.Children.Add(help);
        var statusHeading=new TextBlock {FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,2,0,3),FontSize=11};
        var status=new TextBlock {Name="ProviderConnectionState",TextWrapping=TextWrapping.Wrap,FontSize=11};
        var actualSource=new TextBlock {Name="ProviderConnectionSource",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=SettingsBrush("Secondary"),Margin=new Thickness(0,3,0,0)};
        var lastSuccess=new TextBlock {Name="ProviderConnectionTime",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=SettingsBrush("Secondary"),Margin=new Thickness(0,2,0,0)};
        var error=new TextBlock {Name="ProviderConnectionError",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=SettingsBrush("Danger"),Margin=new Thickness(0,4,0,0),Visibility=Visibility.Collapsed};
        var retry=new Button {Content=Text("重试","Retry"),Padding=new Thickness(7,3,7,3),Margin=new Thickness(0,0,5,0)};
        var failureDetails=new Button {Padding=new Thickness(7,3,7,3)};
        var failureActions=new WrapPanel {Margin=new Thickness(0,5,0,0),Visibility=Visibility.Collapsed};failureActions.Children.Add(retry);failureActions.Children.Add(failureDetails);
        var advancedBody=new StackPanel {Margin=new Thickness(0,7,0,0)};
        advancedBody.Children.Add(sourceRow);advancedBody.Children.Add(description);advancedBody.Children.Add(claudeActions);
        var advanced=new Expander {Name="ProviderAdvancedSettings",IsExpanded=false,Content=advancedBody,FontSize=11,Foreground=SettingsBrush("Secondary"),Margin=new Thickness(0,8,0,0)};
        panel.Children.Add(SettingsGroup(heading,new UIElement[]{selector,statusHeading,status,actualSource,lastSuccess,error,failureActions,advanced}));
        Action updateStatus=()=>{
            bool hasData=snapshot!=null && (snapshot.Hour!=null || snapshot.Week!=null),failed=queryFailed || !hasData && localReadWarning.Length>0;
            var now=DateTimeOffset.UtcNow;
            statusHeading.Text=Text("连接状态","Connection status");
            status.Text=now<providerRetryNotBefore ? Text("等待服务端允许后重试，","Waiting for the server; retry after ")+providerRetryNotBefore.ToLocalTime().ToString("HH:mm:ss") :
                reading ? Text("正在更新额度…","Updating quota…") : failed ? (hasData ? Text("暂未连接，保留上次读数。","Connection unavailable; keeping the previous reading.") : Text("暂未读取到额度。","Quota unavailable.")) :
                hasData ? (snapshot.Live ? Text("已连接","Connected") : Text("已读取额度","Quota loaded")) : snapshot!=null ? Text("已连接，暂未返回额度窗口。","Connected; no quota windows reported.") : Text("等待首次读取…","Waiting for the first reading…");
            status.Foreground=failed?SettingsBrush("Danger"):SettingsBrush("Text");
            actualSource.Text=Text("来源：","Source: ")+(snapshot==null || snapshot.Source.Length==0 ? Text("尚未确认","Not confirmed yet") : ProviderSourceLabel(snapshot.Source));
            lastSuccess.Text=Text("最近成功：","Last successful reading: ")+(snapshot==null ? Text("尚无","None yet") : snapshot.Time.ToLocalTime().ToString("MM-dd HH:mm:ss"));
            string warning=queryFailed?queryError:localReadWarning;
            error.Text=warning;error.Visibility=warning.Length>0?Visibility.Visible:Visibility.Collapsed;
            failureActions.Visibility=failed?Visibility.Visible:Visibility.Collapsed;
            retry.Content=Text("重试","Retry");retry.IsEnabled=!reading && now>=providerRetryNotBefore;
            failureDetails.Content=Text("原因与处理…","Troubleshooting…");
        };
        // Read UI state only. Stop on unload/close; never create another quota poller.
        var statusClock=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
        statusClock.Tick+=(s,e)=>updateStatus();panel.Loaded+=(s,e)=>{updateStatus();statusClock.Start();};panel.Unloaded+=(s,e)=>statusClock.Stop();
        if(settingsWindow!=null)settingsWindow.Closed+=(s,e)=>statusClock.Stop();
        retry.Click+=(s,e)=>{Refresh(true);updateStatus();};
        Action sync=()=>{
            syncing=true;heading.Text=Text("额度平台","Usage provider");sourceLabel.Text=Text("登录来源","Sign-in");
            sourceChoice.Items.Clear();foreach(string label in new[]{Text("自动 · 优先 CLI","Auto · prefer CLI"),"CLI",Text("桌面端（含 Code）","Desktop (incl. Code)")})sourceChoice.Items.Add(label);
            if(currentProvider.Id=="claude")sourceChoice.Items.Add(Text("Claude Code 本地数据","Claude Code local data"));
            sourceChoice.SelectedIndex=Array.IndexOf(new[]{"auto","cli","desktop","local"},ProviderSource(currentProvider.Id));sourceChoice.IsEnabled=currentProvider.Id!="codex";
            directoryButton.Visibility=usageButton.Visibility=collectorButton.Visibility=currentProvider.Id=="claude" && ProviderSource("claude")!="desktop"?Visibility.Visible:Visibility.Collapsed;
            directoryButton.Content=Text("配置目录…","Config folder…");directoryButton.ToolTip=Text("选择 Claude Code 的 .claude 目录；不读取或改写 Claude 设置。","Choose the Claude Code .claude folder; no Claude settings are changed.");
            usageButton.Content=Text("用量文件…","Usage file…");usageButton.ToolTip=Text("选择 Claude Code 已生成的 usage.json。","Choose usage.json already generated by Claude Code.");
            collectorButton.Content=Text("内置采集","Collector");
            advanced.Header=Text("高级设置","Advanced settings");
            description.Text=ProviderHelp();description.ToolTip=DetectProviderSources();help.Content=Text("诊断详情…","Diagnostics…");syncing=false;updateStatus();
        };
        directoryButton.Click+=(s,e)=>{using(var picker=new System.Windows.Forms.FolderBrowserDialog {Description=Text("选择包含 .credentials.json 的 Claude Code 配置目录","Choose the Claude Code folder containing .credentials.json")}){
            if(picker.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
            if(!File.Exists(Path.Combine(picker.SelectedPath,".credentials.json"))){MessageBox.Show(settingsWindow,Text("该目录没有 .credentials.json，请选择 Claude Code 实际使用的配置目录。","No .credentials.json found in this folder. Choose the directory used by Claude Code."));return;}
            try{ApplyClaudeConnection(picker.SelectedPath,null,"cli");sync();}catch(QuotaFetchException ex){MessageBox.Show(settingsWindow,ex.Message);}
        }};
        usageButton.Click+=(s,e)=>{var picker=new Microsoft.Win32.OpenFileDialog {Filter="JSON (*.json)|*.json|All files (*.*)|*.*",CheckFileExists=true};
            if(picker.ShowDialog(settingsWindow)!=true)return;
            try {ReadClaudeUsageFile(picker.FileName,DateTimeOffset.UtcNow);ApplyClaudeConnection(null,picker.FileName,"local");sync();}
            catch(Exception ex){MessageBox.Show(settingsWindow,ex is QuotaFetchException?ex.Message:Text("无法读取有效的 Claude Code 用量文件。","Cannot read a valid Claude Code usage file."));}
        };
        collectorButton.Click+=(s,e)=>{try{ApplyClaudeConnection(null,"","local");sync();}catch(QuotaFetchException ex){MessageBox.Show(settingsWindow,ex.Message);}};
        sourceChoice.SelectionChanged+=(s,e)=>{if(!syncing && sourceChoice.SelectedIndex>=0){SetProviderSource(new[]{"auto","cli","desktop","local"}[sourceChoice.SelectedIndex]);sync();}};
        selector.SelectionChanged+=(s,e)=>{if(selector.SelectedIndex>=0){SelectProvider(quotaProviders[selector.SelectedIndex].Id,true);sync();}};
        RoutedEventHandler showDetails=(s,e)=>{
            string command=ClaudeStatusLineCommand(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string content=ProviderHelp()+"\n\n"+DetectProviderSources();
            if(currentProvider.Id=="claude" && ProviderSource("claude")=="desktop")content+="\n\n"+ClaudeDesktopDiscoveryInfo(ClientRoots.Current());
            if(currentProvider.Id=="claude" && ProviderSource("claude")!="desktop")content+="\n\n"+Text("Claude Code 配置目录：","Claude Code config folder: ")+ClientRoots.Current().ClaudeHome+
                "\n"+Text("CLI 登录文件：","CLI sign-in file: ")+(File.Exists(Path.Combine(ClientRoots.Current().ClaudeHome,".credentials.json"))?Text("已找到","Found"):Text("未找到；可用“配置目录”指定实际目录。","Not found; use Config folder to select the actual directory."))+
                (ClaudeUsageFile.Length>0?"\n"+Text("用量文件：","Usage file: ")+ClaudeUsageFile:"");
            if(currentProvider.Id=="claude" && ProviderSource("claude")=="local") content+="\n\n"+Text("以下配置只把 Claude 提供的额度送入本程序。命令使用编码来兼容 Windows 两种命令环境，不包含账号或密钥。已有状态栏时应先合并采集逻辑，避免覆盖原输出。此按钮不会修改 Claude 配置。\n", "This configuration forwards Claude's quota fields to the widget. Encoding handles both Windows shells; it contains no credentials. Merge with an existing status line to preserve its output. This button does not change Claude settings.\n")+new JavaScriptSerializer().Serialize(new {statusLine=new {type="command",command=command,refreshInterval=15}});
            var detail=new Window {Owner=settingsWindow,Title=Text("额度接入","Quota connection"),Width=390,Height=420,ResizeMode=ResizeMode.CanResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=SettingsBrush("Background"),Foreground=SettingsBrush("Text"),Resources=SettingsStyles(),ShowInTaskbar=false};
            detail.Content=new TextBox {Text=content,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(16),BorderThickness=new Thickness(0),Background=Brushes.Transparent,Foreground=SettingsBrush("Text")};detail.ShowDialog();
        };
        help.Click+=showDetails;failureDetails.Click+=showDetails;
        labels=sync;sync();return panel;
    }
}
