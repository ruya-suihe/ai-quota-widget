using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Web.Script.Serialization;

partial class QuotaWidget {
    // Claude Code (CLI and Desktop Code) appends each response's token usage to
    // .claude/projects/**/*.jsonl within seconds. The online 5-hour percentage is
    // rate limited, so estimate the burn rate locally between online reads.
    // JSON is parsed transiently; only usage numbers and dedupe identifiers are
    // retained. Conversation content is never persisted by this feature.
    const double ClaudeRateAlarmPercent=3, ClaudeDefaultTokensPerPercent=300000;
    const int ClaudeRateWindowSeconds=60;
    sealed class TokenEvent {internal DateTimeOffset Time;internal double Weighted;}
    sealed class CountedMessage {internal double Weighted;internal DateTimeOffset Time;}
    // Worker-only state; one scan runs at a time.
    sealed class ClaudeRateReader {
        internal readonly Dictionary<string,long> Offsets=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string,CountedMessage> Messages=new Dictionary<string,CountedMessage>();
    }
    static ClaudeRateReader claudeRateReader=new ClaudeRateReader();
    static Dictionary<string,long> claudeLogOffsets {get{return claudeRateReader.Offsets;}}
    static Dictionary<string,CountedMessage> claudeCountedMessages {get{return claudeRateReader.Messages;}}
    static string claudeRateContext="";
    // UI-thread state.
    static readonly List<TokenEvent> claudeTokenEvents=new List<TokenEvent>();
    static double claudeTokensTotal,claudeTokensPerPercent=ClaudeDefaultTokensPerPercent,claudeRatePercent,claudeBaselineUsed=-1,claudeBaselineTokens;
    static bool claudeRateAlarm,claudeRateScanning;
    static DateTimeOffset claudeAlarmNotified=DateTimeOffset.MinValue;
    static bool claudeRateEnabled,claudeRateAlerts,claudeRateSound;
    static DateTimeOffset claudeRateContextSince=DateTimeOffset.UtcNow;
    sealed class ClaudeRateOptions {public bool Enabled,Alerts,Sound;}
    static void LoadClaudeRateOptions(){
        try{
            string path=Path.Combine(ProviderFolder(),"claude-rate-options.json");
            if(!File.Exists(path) || new FileInfo(path).Length>4096)return;
            var options=new JavaScriptSerializer().Deserialize<ClaudeRateOptions>(File.ReadAllText(path));
            if(options!=null){claudeRateEnabled=options.Enabled;claudeRateAlerts=options.Alerts;claudeRateSound=options.Sound;}
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
    }
    static string ClaudeRateContextKey(){
        return providerGeneration+"|"+ProviderSource("claude")+"|"+ClientRoots.Current().ClaudeHome+"|"+(snapshot==null?"":snapshot.LoginVersion)+"|"+(snapshot==null || snapshot.Hour==null?0:snapshot.Hour.Reset);
    }
    static void ResetClaudeRateContext(){
        // Replace worker-owned state instead of clearing dictionaries during a scan.
        claudeRateReader=new ClaudeRateReader();claudeRateContext="";claudeRateContextSince=DateTimeOffset.UtcNow;
        claudeTokenEvents.Clear();claudeTokensTotal=claudeRatePercent=claudeBaselineTokens=0;
        claudeTokensPerPercent=ClaudeDefaultTokensPerPercent;claudeBaselineUsed=-1;claudeRateAlarm=false;
    }
    static void EnsureClaudeRateContext(){
        string key=ClaudeRateContextKey();if(key==claudeRateContext)return;
        ResetClaudeRateContext();claudeRateContext=key;
    }
    static StackPanel CreateClaudeRateSettings(out Action labels){
        var panel=new StackPanel {Margin=new System.Windows.Thickness(0,6,0,0)};
        var enabled=new CheckBox {Margin=new System.Windows.Thickness(0,2,0,2)};
        var alerts=new CheckBox {Margin=new System.Windows.Thickness(0,2,0,2)};
        var sound=new CheckBox {Margin=new System.Windows.Thickness(0,2,0,2)};
        var note=new TextBlock {TextWrapping=System.Windows.TextWrapping.Wrap,FontSize=11,Margin=new System.Windows.Thickness(0,3,0,0)};
        panel.Children.Add(enabled);panel.Children.Add(alerts);panel.Children.Add(sound);panel.Children.Add(note);bool syncing=false;
        Action sync=()=>{
            syncing=true;panel.Visibility=currentProvider.Id=="claude"?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
            enabled.Content=Text("本机 Code 消耗速率估算","Estimate local Code usage rate");
            alerts.Content=Text("消耗速率达到 3%/分时提醒","Warn at an estimated 3%/min");
            sound.Content=Text("提醒时播放声音","Play sound with rate alerts");
            enabled.IsChecked=claudeRateEnabled;alerts.IsChecked=claudeRateAlerts;sound.IsChecked=claudeRateSound;
            alerts.IsEnabled=claudeRateEnabled;sound.IsEnabled=claudeRateEnabled&&claudeRateAlerts;
            note.Text=Text("仅提取本机 Code 日志的用量，不保存正文；启用或切换来源后统计新记录。≈ 为估算，不代表账户实时额度。","Extracts usage from local Code logs without retaining conversation text. Counts new records after enabling or changing source. ≈ is an estimate, not live account quota.");syncing=false;
        };
        System.Windows.RoutedEventHandler changed=(s,e)=>{
            if(syncing)return;
            var options=new ClaudeRateOptions {Enabled=enabled.IsChecked==true,Alerts=alerts.IsChecked==true,Sound=sound.IsChecked==true};
            try{
                if(!checking){
                    Directory.CreateDirectory(ProviderFolder());string path=Path.Combine(ProviderFolder(),"claude-rate-options.json"),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                    try{File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(options));if(!MoveFileEx(temporary,path,0x1|0x8))throw new IOException();}
                    finally{if(File.Exists(temporary))try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
                }
                claudeRateEnabled=options.Enabled;claudeRateAlerts=options.Alerts;claudeRateSound=options.Sound;ResetClaudeRateContext();Render();
            }catch(IOException){System.Windows.MessageBox.Show(settingsWindow,Text("速率选项未能保存。","Could not save rate options."));}
            catch(UnauthorizedAccessException){System.Windows.MessageBox.Show(settingsWindow,Text("没有权限保存速率选项。","Cannot save rate options: access denied."));}
            sync();
        };
        enabled.Checked+=changed;enabled.Unchecked+=changed;alerts.Checked+=changed;alerts.Unchecked+=changed;sound.Checked+=changed;sound.Unchecked+=changed;
        labels=sync;sync();return panel;
    }
    // Input-token equivalents, following relative API prices: cache writes 1.25x,
    // cache reads 0.1x, output 5x. Plan limits are not published; this is a proxy.
    static double WeightedTokens(Dictionary<string,object> usage) {
        Func<string,double> read=key=>{object value=Value(usage,key);double number=IsJsonNumber(value)?Convert.ToDouble(value,CultureInfo.InvariantCulture):0;return double.IsNaN(number)||double.IsInfinity(number)?0:Math.Max(0,Math.Min(1e12,number));};
        return read("input_tokens")+read("cache_creation_input_tokens")*1.25+read("cache_read_input_tokens")*0.1+read("output_tokens")*5;
    }
    static List<TokenEvent> ScanClaudeLogs(string root,DateTimeOffset now,ClaudeRateReader state=null) {
        state=state??claudeRateReader;var offsets=state.Offsets;var messages=state.Messages;
        var found=new List<TokenEvent>();
        string[] files;
        try {files=Directory.Exists(root)?Directory.EnumerateFiles(root,"*.jsonl",SearchOption.AllDirectories).Take(4096).ToArray():new string[0];}
        catch(IOException){return found;}catch(UnauthorizedAccessException){return found;}
        var serializer=new JavaScriptSerializer {MaxJsonLength=8388608};
        foreach(string file in files) {
            try {
                var info=new FileInfo(file);
                if(info.LastWriteTimeUtc<now.UtcDateTime.AddMinutes(-10)){offsets.Remove(file);continue;}
                long offset;bool partial=false;
                // First sight: only the recent tail. Old entries are filtered by time below.
                if(!offsets.TryGetValue(file,out offset)){offset=Math.Max(0,info.Length-2097152);partial=offset>0;}
                if(info.Length<offset){offset=0;partial=false;}
                if(info.Length==offset)continue;
                byte[] bytes;
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    if(stream.Length-offset>8388608){offset=stream.Length-2097152;partial=true;}
                    stream.Seek(offset,SeekOrigin.Begin);bytes=new byte[stream.Length-offset];
                    int count=0,read;while(count<bytes.Length && (read=stream.Read(bytes,count,bytes.Length-count))>0)count+=read;
                    if(count<bytes.Length)Array.Resize(ref bytes,count);
                }
                // Stop at the last complete line; a line still being written is read next time.
                int end=Array.LastIndexOf(bytes,(byte)'\n');if(end<0)continue;
                offsets[file]=offset+end+1;
                int start=0;
                if(partial){int first=Array.IndexOf(bytes,(byte)'\n');start=first+1;}
                while(start<=end) {
                    int stop=Array.IndexOf(bytes,(byte)'\n',start);if(stop<0)stop=end;
                    string line=Encoding.UTF8.GetString(bytes,start,stop-start);start=stop+1;
                    if(line.IndexOf("\"usage\"",StringComparison.Ordinal)<0 || line.IndexOf("\"assistant\"",StringComparison.Ordinal)<0)continue;
                    var item=ReadUsageLine(line,serializer,now,state);if(item!=null)found.Add(item);
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        foreach(string id in messages.Where(pair=>pair.Value.Time<now.AddMinutes(-15)).Select(pair=>pair.Key).ToArray())messages.Remove(id);
        var present=new HashSet<string>(files,StringComparer.OrdinalIgnoreCase);foreach(string path in offsets.Keys.Where(path=>!present.Contains(path)).ToArray())offsets.Remove(path);
        return found;
    }
    static TokenEvent ReadUsageLine(string line,JavaScriptSerializer serializer,DateTimeOffset now,ClaudeRateReader state) {
        var messages=state.Messages;
        Dictionary<string,object> entry;
        try {entry=Map(serializer.DeserializeObject(line));}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
        if(Value(entry,"type") as string!="assistant")return null;
        var message=Map(Value(entry,"message"));var usage=Map(Value(message,"usage"));if(usage==null)return null;
        DateTimeOffset time;
        if(!DateTimeOffset.TryParse(Value(entry,"timestamp") as string,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out time) || time<now.AddMinutes(-2) || time>now.AddMinutes(1))return null;
        string id=Value(message,"id") as string??Value(entry,"requestId") as string;if(string.IsNullOrEmpty(id))return null;
        // One response is logged once per content block with growing usage; count only the increase.
        double weighted=WeightedTokens(usage);CountedMessage counted;
        if(!messages.TryGetValue(id,out counted))messages[id]=counted=new CountedMessage {Time=time};
        double added=weighted-counted.Weighted;if(added<=0)return null;
        counted.Weighted=weighted;counted.Time=time;
        return new TokenEvent {Time=time,Weighted=added};
    }
    static void UpdateClaudeRate(List<TokenEvent> found,DateTimeOffset now) {
        foreach(var item in found){claudeTokenEvents.Add(item);claudeTokensTotal+=item.Weighted;}
        claudeTokenEvents.RemoveAll(item=>item.Time<now.AddSeconds(-ClaudeRateWindowSeconds));
        double previous=claudeRatePercent;bool wasAlarm=claudeRateAlarm;
        claudeRatePercent=claudeTokenEvents.Sum(item=>item.Weighted)/claudeTokensPerPercent;
        claudeRateAlarm=claudeRateAlerts && claudeRatePercent>=ClaudeRateAlarmPercent;
        if(claudeRateAlarm && !wasAlarm && now>=claudeAlarmNotified.AddMinutes(2)) {
            claudeAlarmNotified=now;
            if(!checking) {
                if(claudeRateSound)System.Media.SystemSounds.Exclamation.Play();
                // Confirm with the account's own number when Claude allows a query.
                if(!reading && now>=providerRetryNotBefore && (snapshot==null || now-snapshot.Time>=TimeSpan.FromSeconds(ClaudeFastestSeconds)))Refresh(true);
            }
        }
        if(Math.Abs(previous-claudeRatePercent)>=0.05 || wasAlarm!=claudeRateAlarm)Render();
    }
    // Learn tokens per 1% from successive online readings of the 5-hour window.
    static void CalibrateClaudeRate(Snapshot data) {
        if(!claudeRateEnabled || data==null || data.Hour==null)return;
        EnsureClaudeRateContext();
        double used=100-data.Hour.Remaining;
        if(claudeBaselineUsed<0 || used<claudeBaselineUsed){claudeBaselineUsed=used;claudeBaselineTokens=claudeTokensTotal;return;}
        double delta=used-claudeBaselineUsed,tokens=claudeTokensTotal-claudeBaselineTokens;
        if(delta<2)return;
        // Usage from other devices also moves the percentage; that only makes the
        // estimate more cautious. Limit each step so one odd reading cannot dominate.
        if(tokens>0)claudeTokensPerPercent=Math.Max(claudeTokensPerPercent/2,Math.Min(claudeTokensPerPercent*2,(claudeTokensPerPercent+tokens/delta)/2));
        claudeBaselineUsed=used;claudeBaselineTokens=claudeTokensTotal;
    }
    static string ClaudeRateText() {
        if(!claudeRateEnabled || currentProvider.Id!="claude" || claudeRatePercent<0.05)return "";
        return (claudeRateAlarm?"⚠ ":"")+"≈"+claudeRatePercent.ToString("0.0",CultureInfo.InvariantCulture)+Text("%/分 · ","%/min · ");
    }
    static string ClaudeRateHelp() {
        return Text("本机估算速率：从 Claude Code 日志中提取令牌用量，不保留对话正文，按近 60 秒估算每分钟消耗的 5 小时额度百分比；开启提醒后达到 ","Local estimate: extracts token usage from Claude Code logs without retaining conversation content, estimated as 5-hour quota percent per minute over the last 60 s; when alerts are enabled, alarm at ")
            +ClaudeRateAlarmPercent.ToString("0",CultureInfo.InvariantCulture)+Text("%/分 时报警。估算值，以账户查询为准。","%/min. An estimate; the account query is authoritative.");
    }
    static async Task ScanClaudeRateNow() {
        if(!claudeRateEnabled || currentProvider.Id!="claude" || claudeRateScanning || refreshStopped)return;
        EnsureClaudeRateContext();int generation=providerGeneration;var reader=claudeRateReader;string context=claudeRateContext;
        claudeRateScanning=true;string root=Path.Combine(ClientRoots.Current().ClaudeHome,"projects");
        try {
            var found=await Task.Run(()=>ScanClaudeLogs(root,DateTimeOffset.UtcNow,reader));
            if(claudeRateEnabled && generation==providerGeneration && object.ReferenceEquals(reader,claudeRateReader) && context==ClaudeRateContextKey() && currentProvider.Id=="claude" && !refreshStopped)UpdateClaudeRate(found.Where(item=>item.Time>=claudeRateContextSince).ToList(),DateTimeOffset.UtcNow);
        }catch(Exception){}  // A rate hint must never disturb quota reads.
        finally{claudeRateScanning=false;}
    }
    static void StartClaudeRateWatch() {
        var timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(3)};
        timer.Tick+=async (s,e)=>await ScanClaudeRateNow();
        window.Closed+=(s,e)=>timer.Stop();
        timer.Start();
    }
    // Claude refuses a usage query a few seconds after the previous one and then
    // pauses all queries. A reading under 15 s old only gets a local rate update.
    static async void ManualRefresh() {
        if(currentProvider.Id=="claude" && snapshot!=null && snapshot.Live && !ShouldDropSnapshotForLogin(snapshot,"claude",ProviderSource("claude"),ClientRoots.Current()) && DateTimeOffset.UtcNow-snapshot.Time<TimeSpan.FromSeconds(ClaudeFastestSeconds)) {
            int generation=providerGeneration;await ScanClaudeRateNow();if(generation!=providerGeneration || refreshStopped)return;Render();
            if(!reading)Control<TextBlock>("Updated").Text+=Text(" · 刚查询过"," · just checked");
            return;
        }
        Refresh(true);
    }
    static void CheckClaudeRate(string report) {
        claudeRateEnabled=claudeRateAlerts=true;EnsureClaudeRateContext();
        string folder=Path.Combine(Path.GetTempPath(),"CodexQuotaRateCheck-"+Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(Path.Combine(folder,"project"));
            string file=Path.Combine(folder,"project","session.jsonl");var now=DateTimeOffset.UtcNow;
            Func<string,DateTimeOffset,int,int,string> entry=(id,time,cacheRead,output)=>"{\"type\":\"assistant\",\"timestamp\":\""+time.ToString("o",CultureInfo.InvariantCulture)+"\",\"message\":{\"id\":\""+id+"\",\"content\":[{\"type\":\"text\",\"text\":\"\\\"usage\\\": input_tokens 999999\"}],\"usage\":{\"input_tokens\":10,\"cache_creation_input_tokens\":100,\"cache_read_input_tokens\":"+cacheRead+",\"output_tokens\":"+output+"}}}";
            File.WriteAllText(file,"{\"type\":\"user\",\"message\":{\"content\":\"\\\"usage\\\" assistant\"}}\n"+entry("a",now,1000,20)+"\n"+entry("a",now,1000,200)+"\n"+entry("old",now.AddMinutes(-5),1000,2000)+"\n"+entry("b",now,0,0).Substring(0,40));
            claudeLogOffsets.Clear();claudeCountedMessages.Clear();
            var first=ScanClaudeLogs(folder,now);
            // a: 10 + 100*1.25 + 1000*0.1 + 200*5 = 1235, counted once despite two blocks.
            if(first.Count!=2 || Math.Abs(first.Sum(item=>item.Weighted)-1235)>0.001)throw new Exception("Usage dedupe or weighting failed.");
            File.AppendAllText(file,entry("b",now,0,0).Substring(40)+"\n"+entry("a",now,1000,200)+"\n");
            var second=ScanClaudeLogs(folder,now);
            if(second.Count!=1 || Math.Abs(second[0].Weighted-135)>0.001)throw new Exception("Partial line or repeat handling failed.");
            claudeTokenEvents.Clear();claudeTokensTotal=0;claudeTokensPerPercent=400;claudeRateAlarm=false;
            UpdateClaudeRate(first.Concat(second).ToList(),now);
            // 1370 / 400 = 3.425%/min, above the 3%/min alarm.
            if(!claudeRateAlarm || Math.Abs(claudeRatePercent-3.425)>0.001)throw new Exception("Rate percent or alarm failed.");
            UpdateClaudeRate(new List<TokenEvent>(),now.AddSeconds(ClaudeRateWindowSeconds+1));
            if(claudeRateAlarm || claudeRatePercent!=0)throw new Exception("Rate window did not expire.");
            claudeBaselineUsed=-1;claudeTokensTotal=0;claudeTokensPerPercent=1000;
            CalibrateClaudeRate(new Snapshot {Hour=new Limit {Remaining=90}});claudeTokensTotal=3000;
            CalibrateClaudeRate(new Snapshot {Hour=new Limit {Remaining=89}});
            if(claudeTokensPerPercent!=1000)throw new Exception("Calibrated on a 1-point step.");
            CalibrateClaudeRate(new Snapshot {Hour=new Limit {Remaining=88}});
            if(Math.Abs(claudeTokensPerPercent-1250)>0.001)throw new Exception("Calibration failed.");
            var surfaces=ParseClaudeOnline("{\"five_hour\":{\"utilization\":7,\"resets_at\":null},\"seven_day\":null,\"seven_day_oauth_apps\":{\"utilization\":12,\"resets_at\":null},\"seven_day_cowork\":{\"utilization\":30,\"resets_at\":null}}",now,"Claude Desktop account");
            if(surfaces.Week==null || surfaces.Week.Remaining!=70 || surfaces.Week.Minutes!=10080 || surfaces.ClaudeWeeklyWindow!="seven_day_cowork")throw new Exception("Per-surface weekly fallback failed.");
            var overall=ParseClaudeOnline("{\"five_hour\":{\"utilization\":7,\"resets_at\":null},\"seven_day\":{\"utilization\":5,\"resets_at\":null},\"seven_day_cowork\":{\"utilization\":30,\"resets_at\":null}}",now,"Claude Desktop account");
            if(overall.Week==null || overall.Week.Remaining!=95 || overall.ClaudeWeeklyWindow!="seven_day")throw new Exception("Overall weekly window lost priority.");
            File.WriteAllText(report,"PASS: weighted token usage deduped per response; message text containing \"usage\" ignored; old entries skipped; partial trailing line read once complete; 60-second rate, alarm and expiry; calibration needs a 2-point change; weekly bar uses seven_day, else the most used per-surface weekly window.\r\nSynthetic log only. No account queries or user file reads.\r\n");
        } finally {try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
