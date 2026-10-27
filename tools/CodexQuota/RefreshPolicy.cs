using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
partial class QuotaWidget {
    const int AccountQuerySeconds = 30;
    static DateTimeOffset nextAutoQuery = DateTimeOffset.MinValue, activeQueryStarted;
    static int activeQueryFailures;
    static DateTimeOffset providerRetryNotBefore=DateTimeOffset.MinValue;
    static bool refreshStopped;
    static bool DataIsStale() { return DataIsStaleAt(DateTimeOffset.UtcNow); }
    static bool DataIsStaleAt(DateTimeOffset now) {
        if (snapshot == null) return false;
        if(currentProvider.Id!="codex" && ((snapshot.Hour!=null && snapshot.Hour.Reset>0 && snapshot.Hour.Reset<=now.ToUnixTimeSeconds()) || (snapshot.Week!=null && snapshot.Week.Reset>0 && snapshot.Week.Reset<=now.ToUnixTimeSeconds()))) return true;
        return (snapshot.Hour != null && (now - (snapshot.HourTime == default(DateTimeOffset) ? snapshot.Time : snapshot.HourTime)).TotalMinutes >= 5)
            || (snapshot.Week != null && (now - (snapshot.WeekTime == default(DateTimeOffset) ? snapshot.Time : snapshot.WeekTime)).TotalMinutes >= 5);
    }
    static bool ShouldAutoQuery(DateTimeOffset now) {
        // Session records are event-driven, not a live quota feed. Their freshness
        // must not postpone an independent account read.
        return currentProvider.SupportsQuery && !refreshStopped && !reading && now >= nextAutoQuery && now>=providerRetryNotBefore;
    }
    static void CompleteActiveQuery(bool succeeded, DateTimeOffset now) {
        lastActiveCompleted = now;
        if(succeeded) {
            activeQueryFailures = 0;
            int interval=currentProvider.Id=="codex"?AccountQuerySeconds:15;
            nextAutoQuery = activeQueryStarted.AddSeconds(interval);
            if(nextAutoQuery <= now) nextAutoQuery = now.AddSeconds(interval);
        } else {
            activeQueryFailures = Math.Min(currentProvider.Id=="codex"?4:6,activeQueryFailures+1);
            nextAutoQuery = now.AddSeconds(Math.Min(300,(currentProvider.Id=="codex"?60:15)*Math.Pow(2,activeQueryFailures-1)));
        }
        if(nextAutoQuery<providerRetryNotBefore)nextAutoQuery=providerRetryNotBefore;
    }
    static void RecordProviderRetry(string provider,DateTimeOffset until) {
        if(until==default(DateTimeOffset))return;
        DateTimeOffset effective;
        if(currentProvider.Id==provider) {if(providerRetryNotBefore<until)providerRetryNotBefore=until;if(nextAutoQuery<until)nextAutoQuery=until;effective=providerRetryNotBefore;}
        else {
            ProviderState state;if(!providerStates.TryGetValue(provider,out state))providerStates[provider]=state=new ProviderState();
            if(state.RetryNotBefore<until)state.RetryNotBefore=until;if(state.Next<until)state.Next=until;
            effective=state.RetryNotBefore;
        }
        if(!checking)try {DirectoryForRetry(provider,effective);}catch(System.IO.IOException){}catch(UnauthorizedAccessException){}
    }
    static void DirectoryForRetry(string provider,DateTimeOffset until) {
        System.IO.Directory.CreateDirectory(ProviderFolder());
        string path=System.IO.Path.Combine(ProviderFolder(),provider+"-retry.txt"),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            System.IO.File.WriteAllText(temporary,until.ToString("o",System.Globalization.CultureInfo.InvariantCulture));
            if(!MoveFileEx(temporary,path,0x1|0x8))throw new System.IO.IOException("Could not save quota cooldown.");
        }finally{if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}
    }
    static void StartRefreshTimers() {
        var scanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        scanTimer.Tick += (s,e) => Refresh(false);
        var queryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        queryTimer.Tick += (s,e) => { if(ShouldAutoQuery(DateTimeOffset.UtcNow)) Refresh(true); };
        var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        clockTimer.Tick += (s,e) => Render();
        window.Closed += (s,e) => { refreshStopped=true;scanTimer.Stop();queryTimer.Stop();clockTimer.Stop(); };
        scanTimer.Start();queryTimer.Start();clockTimer.Start();
    }
    static void RegisterLocalResult(Snapshot candidate, DateTimeOffset now) {
        bool valid=candidate!=null && (candidate.Hour!=null || candidate.Week!=null);
        if(!valid) {if(localFailureSince==DateTimeOffset.MinValue) localFailureSince=now;return;}
        localFailureSince=DateTimeOffset.MinValue;
        // A usable local record is a fallback, not evidence that account queries
        // have recovered. Keep the failure visible until an active read succeeds.
    }
    static async void Refresh(bool active) {
        if (refreshStopped || reading) return;
        var provider=currentProvider;int generation=providerGeneration;
        active=active && provider.SupportsQuery;
        string source=ProviderSource(provider.Id);
        if(provider.Id!="codex" && source!="local" && snapshot!=null && ShouldDropSnapshotForLogin(snapshot,provider.Id,source,ClientRoots.Current()))snapshot=null;
        if(active && DateTimeOffset.UtcNow<providerRetryNotBefore){Render();return;}
        reading = true;
        activeReading=active;
        bool activeSucceeded = false;
        if(active) activeQueryStarted = DateTimeOffset.UtcNow;
        Control<Button>("RefreshButton").IsEnabled = false;
        UpdateRefreshStatus();
        if (active) Control<TextBlock>("Updated").Text = Text("正在查询账户额度…", "Querying account usage…");
        try {
            bool fallback = false;
            try {
                var result = await Task.Run(() => provider.Read(active,source));
                RecordProviderRetry(provider.Id,result.RetryAt);
                // Switching platforms invalidates old work before any shared UI,
                // retry state or cache is touched. Worker reads never mutate UI state.
                if(generation!=providerGeneration || refreshStopped) return;
                var candidate=result.Data;
                if(active && provider.Id!="codex" && candidate!=null && !CacheMatchesLogin(candidate,provider.Id,source,ClientRoots.Current(),false)) {
                    if(snapshot!=null && ShouldDropSnapshotForLogin(snapshot,provider.Id,source,ClientRoots.Current()))snapshot=null;
                    throw new QuotaFetchException("登录记录已变化，请重新读取当前账户。","Sign-in changed. Read the current account again.");
                }
                localReadWarning=result.Warning;diagnostic=result.Detail;
                var selected = SelectProviderSnapshot(provider.Id,snapshot, candidate);
                if (!active && candidate != null && snapshot != null && selected == snapshot && candidate.Time > snapshot.Time
                    && ((candidate.Hour != null && snapshot.Hour != null && candidate.Hour.Remaining < snapshot.Hour.Remaining)
                    || (candidate.Week != null && snapshot.Week != null && candidate.Week.Remaining < snapshot.Week.Remaining)))
                    localReadWarning = "Newer local usage rejected by reset/time protection. Please query the account.";
                bool changed=selected!=snapshot;
                snapshot = selected;
                if(candidate!=null && (changed || providerCacheWarning.Length>0)) SaveProviderSnapshot();
                if (active) { activeSucceeded=true; queryFailed = false; queryError = ""; localReadWarning = ""; localFailureSince=DateTimeOffset.MinValue; }
                else RegisterLocalResult(candidate,DateTimeOffset.UtcNow);
            } catch (Exception ex) {
                var limited=ex as QuotaFetchException;if(limited!=null)RecordProviderRetry(provider.Id,limited.RetryAt);
                if(generation!=providerGeneration || refreshStopped) return;
                if (active) {
                    if(provider.Id=="cursor" && snapshot!=null && ShouldDropSnapshotForLogin(snapshot,provider.Id,source,ClientRoots.Current()))snapshot=null;
                    queryFailed = true; queryError = ex.Message; fallback = true;
                }
                else {localReadWarning = "Local read failed: " + ex.GetType().Name; RegisterLocalResult(null,DateTimeOffset.UtcNow);}
            }
            if (fallback) {
                try {
                    var result=await Task.Run(()=>provider.Read(false,source));
                    if(generation!=providerGeneration || refreshStopped) return;
                    localReadWarning=result.Warning;diagnostic=result.Detail;
                    snapshot=SelectProviderSnapshot(provider.Id,snapshot,result.Data);
                    RegisterLocalResult(result.Data,DateTimeOffset.UtcNow);
                    if(result.Data!=null) SaveProviderSnapshot();
                } catch (Exception) {if(generation==providerGeneration && !refreshStopped) RegisterLocalResult(null,DateTimeOffset.UtcNow);}
            }
        }
        finally {
            if(generation==providerGeneration) {
            if(active) CompleteActiveQuery(activeSucceeded,DateTimeOffset.UtcNow);
            activeReading=false;
            reading = false;
            if(!refreshStopped) {
                Control<Button>("RefreshButton").IsEnabled = true; Render();
                if(!active && ShouldAutoQuery(DateTimeOffset.UtcNow)) Refresh(true);
            }
            }
        }
    }

}
