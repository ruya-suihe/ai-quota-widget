using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Web.Script.Serialization;

partial class QuotaWidget {
    sealed class QuotaFetchException:Exception {
        public readonly DateTimeOffset RetryAt;
        public readonly bool AuthRejected,Incomplete;
        public readonly int HttpStatus;
        public QuotaFetchException(string zh,string en,DateTimeOffset retry=default(DateTimeOffset),bool auth=false,bool incomplete=false,int httpStatus=0):base(Text(zh,en)){RetryAt=retry;AuthRejected=auth;Incomplete=incomplete;HttpStatus=httpStatus;}
    }
    sealed class QuotaHttpResult {
        public int Status;
        public string Body="",RetryAfter="";
    }
    delegate QuotaHttpResult QuotaTransport(Uri url,Dictionary<string,string> headers,string method);
    static DateTimeOffset QuotaRetryAfter(string value,DateTimeOffset now) {
        double seconds;DateTimeOffset date;
        if(double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out seconds) && !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds>=0) {
            if(seconds>(DateTimeOffset.MaxValue-now).TotalSeconds) return DateTimeOffset.MaxValue;
            return now.AddSeconds(seconds);
        }
        return DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out date) && date>now ? date : now.AddSeconds(60);
    }
    static HttpWebRequest CreateQuotaRequest(Uri url,Dictionary<string,string> headers,string method="GET") {
        if(url.Scheme!="https" || !url.IsDefaultPort || !(url.Host=="cursor.com" || url.Host=="claude.ai" || url.Host=="api.anthropic.com")) throw new InvalidOperationException("Untrusted quota endpoint.");
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        var request=(HttpWebRequest)WebRequest.Create(url);
        if(method!="GET" && !(method=="POST" && url.AbsoluteUri=="https://cursor.com/api/dashboard/get-sand-usage-status"))throw new InvalidOperationException("Unsupported quota method.");
        request.Method=method;request.AllowAutoRedirect=false;request.UseDefaultCredentials=false;
        request.Timeout=10000;request.ReadWriteTimeout=10000;
        request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
        request.CachePolicy=new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
        request.Accept="application/json";request.UserAgent="UsageWindow/"+AppServices.CurrentVersion;
        if(method=="POST"){request.ContentType="application/json";request.ContentLength=2;request.Headers["Origin"]="https://cursor.com";}
        foreach(var pair in headers) {
            if(pair.Key!="Authorization" && pair.Key!="Cookie" && pair.Key!="anthropic-beta")throw new InvalidOperationException("Unsupported quota header.");
            request.Headers[pair.Key]=pair.Value;
        }
        return request;
    }
    static QuotaHttpResult SendQuotaRequest(Uri url,Dictionary<string,string> headers,string method) {
        try {
            var request=CreateQuotaRequest(url,headers,method);HttpWebResponse response;
            if(method=="POST")using(var body=request.GetRequestStream()){var bytes=Encoding.ASCII.GetBytes("{}");body.Write(bytes,0,bytes.Length);}
            try {response=(HttpWebResponse)request.GetResponse();}
            catch(WebException ex) {response=ex.Response as HttpWebResponse;if(response==null)throw new QuotaFetchException("额度查询连接失败，请检查网络或代理。","Quota connection failed. Check network or proxy.");}
            using(response) {
                var result=new QuotaHttpResult {Status=(int)response.StatusCode,RetryAfter=response.Headers["Retry-After"]??""};
                // Do not read or retain error pages, cookies, or redirect locations.
                if(result.Status!=200)return result;
                using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)) {
                    var buffer=new char[1048577];int count=0,read;
                    while(count<buffer.Length && (read=reader.Read(buffer,count,buffer.Length-count))>0)count+=read;
                    if(count==buffer.Length)throw new QuotaFetchException("额度响应过大，无法读取。","Quota response exceeds the size limit.");
                    result.Body=new string(buffer,0,count);
                }
                return result;
            }
        } catch(QuotaFetchException){throw;}
        catch(Exception) {throw new QuotaFetchException("额度查询失败，请检查网络或登录状态。","Quota request failed. Check network and sign-in.");}
    }
    static string ReadQuotaEndpoint(string address,Dictionary<string,string> headers,QuotaTransport send,string method="GET") {
        var url=new Uri(address);
        // Validate destinations even when using an offline transport.
        if(url.Scheme!="https" || !url.IsDefaultPort || !(url.Host=="cursor.com" || url.Host=="claude.ai" || url.Host=="api.anthropic.com"))throw new InvalidOperationException("Untrusted quota endpoint.");
        QuotaHttpResult result;
        try {result=send(url,headers,method);}catch(QuotaFetchException){throw;}
        catch(Exception){throw new QuotaFetchException("额度连接失败，保留上次数据。","Quota connection failed; keeping previous data.");}
        if(result.Status==429 || result.Status==503 && result.RetryAfter.Length>0) {
            // Claude can send Retry-After: 0; the refresh policy adds its own backoff.
            string code=" (HTTP "+result.Status.ToString(CultureInfo.InvariantCulture)+")";
            throw new QuotaFetchException("服务端限流或暂不可用，等待后重试。"+code,"Service limited or unavailable; waiting to retry."+code,QuotaRetryAfter(result.RetryAfter,DateTimeOffset.UtcNow),httpStatus:result.Status);
        }
        if(result.Status==401 || result.Status==403) throw new QuotaFetchException("登录已失效或访问被拒绝，请在对应客户端确认登录。","Sign-in expired or access denied. Check the source client.",auth:true);
        if(result.Status>=300 && result.Status<400)throw new QuotaFetchException("额度接口要求跳转，已停止转发登录凭据。","Quota endpoint redirected; credentials were not forwarded.");
        if(result.Status!=200)throw new QuotaFetchException("额度服务返回错误，请稍后重试。","Quota service returned an error. Try again later.");
        return result.Body;
    }
    static Dictionary<string,object> QuotaJson(string text) {
        var map=Map(new JavaScriptSerializer {MaxJsonLength=1048576}.DeserializeObject(text));
        if(map==null)throw new FormatException("Invalid quota response.");return map;
    }
    static double? QuotaNumber(Dictionary<string,object> map,string key) {
        object value=Value(map,key);if(value==null)return null;
        if(!IsJsonNumber(value))throw new FormatException("Invalid quota number.");
        double number=Convert.ToDouble(value,CultureInfo.InvariantCulture);
        if(double.IsNaN(number) || double.IsInfinity(number) || number<0)throw new FormatException("Invalid quota number.");
        return number;
    }
    static long QuotaIsoReset(object value) {
        if(value==null)return 0;
        var text=value as string;DateTimeOffset reset;
        if(text==null || !DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out reset))throw new FormatException("Invalid quota reset.");
        return reset.ToUnixTimeSeconds();
    }
    // Names of usage windows the server filled in, for troubleshooting (no values).
    // Window diagnostics belong to the accepted snapshot, never shared worker state.
    static Snapshot ParseClaudeOnline(string text,DateTimeOffset now,string source) {
        var root=QuotaJson(text);var result=new Snapshot {Provider="claude",Time=now,Live=true,Source=source};
        result.ClaudeReportedWindows=string.Join(", ",root.Where(pair=>pair.Value!=null).Take(20).Select(pair=>{
            var fields=Map(pair.Value);
            return fields==null?pair.Key:pair.Key+"{"+string.Join(",",fields.Where(field=>field.Value!=null).Select(field=>field.Key).Take(12))+"}";
        }));
        // An unrelated HTML/JSON page is not a successful authoritative empty read.
        if(!new[]{"five_hour","seven_day","seven_day_oauth_apps","seven_day_cowork"}.Any(root.ContainsKey))throw new FormatException("Unknown Claude quota schema.");
        Func<string,int,Limit> window=(name,minutes)=>{
            object raw=Value(root,name);if(raw==null)return null;
            var item=Map(raw);if(item==null)throw new FormatException("Invalid Claude window.");
            var percent=QuotaNumber(item,"utilization");if(!percent.HasValue)return null;
            if(percent.Value>100)throw new FormatException("Invalid Claude percentage.");
            var limit=new Limit {Remaining=100-percent.Value,Reset=QuotaIsoReset(Value(item,"resets_at")),Minutes=minutes};
            return limit.Reset>0 && limit.Reset<=now.ToUnixTimeSeconds()?null:limit;
        };
        result.Hour=window("five_hour",300);result.Week=window("seven_day",10080);result.ClaudeWeeklyWindow=result.Week==null?"":"seven_day";
        // Some organization seats report no overall weekly window, only per-surface
        // ones. Show the most used of those, since either may stop Claude Code.
        if(result.Week==null)foreach(string name in new[]{"seven_day_oauth_apps","seven_day_cowork"}) {
            var limit=window(name,10080);
            if(limit!=null && (result.Week==null || limit.Remaining<result.Week.Remaining)){result.Week=limit;result.ClaudeWeeklyWindow=name;}
        }
        return result;
    }
    static Snapshot ParseCursorOnline(string text,DateTimeOffset now,string source) {
        var root=QuotaJson(text);
        if(!root.ContainsKey("individualUsage") && !root.ContainsKey("isUnlimited"))throw new FormatException("Unknown Cursor quota schema.");
        var result=new Snapshot {Provider="cursor",Time=now,Live=true,Source=source};
        long start=QuotaIsoReset(Value(root,"billingCycleStart")),end=QuotaIsoReset(Value(root,"billingCycleEnd"));
        var plan=Map(Value(Map(Value(root,"individualUsage")),"plan"));
        if(plan==null)return result;
        if(Value(plan,"enabled") is bool && !(bool)Value(plan,"enabled"))return result;
        var usedPercent=QuotaNumber(plan,"totalPercentUsed")??QuotaNumber(plan,"percentUsed");
        if(!usedPercent.HasValue) {
            double? used=QuotaNumber(plan,"used"),total=QuotaNumber(plan,"limit");
            if(used.HasValue && total.HasValue && total>0)usedPercent=used.Value/total.Value*100;
        }
        if(!usedPercent.HasValue)return result;
        if(double.IsInfinity(usedPercent.Value) || double.IsNaN(usedPercent.Value))throw new FormatException("Invalid Cursor percentage.");
        // Monthly included allowance, never the unrelated on-demand spending cap.
        if(start<=0 || end<=start || end-start>527040L*60 || end<=now.ToUnixTimeSeconds() || start>now.ToUnixTimeSeconds())throw new FormatException("Cursor billing cycle is not current.");
        result.Hour=new Limit {Remaining=Math.Max(0,100-usedPercent.Value),Reset=end,Minutes=(end-start)/60.0,Label="cycle"};
        return result;
    }
    static Snapshot QueryCursorOnline(string mode,ClientRoots roots,QuotaTransport send) {
        return QueryCursorOnlineRead(mode,roots,send).Data;
    }
    static ProviderRead QueryCursorOnlineRead(string mode,ClientRoots roots,QuotaTransport send) {
        ClientCredential credential=null;
        try {
            credential=ReadCursorCredential(mode,roots);
            var headers=new Dictionary<string,string>{{"Cookie",credential.Header}};
            var text=ReadQuotaEndpoint("https://cursor.com/api/usage-summary",headers,send);
            var result=new ProviderRead {Data=ParseCursorOnline(text,DateTimeOffset.UtcNow,credential.Source)};
            result.Data.LoginVersion=credential.LoginVersion;
            try {
                var shared=QuotaJson(ReadQuotaEndpoint("https://cursor.com/api/dashboard/get-sand-usage-status",headers,send,"POST"));
                if(Value(shared,"isUnified") is bool && (bool)Value(shared,"isUnified")) {
                    var percent=QuotaNumber(shared,"usagePercent");long reset=QuotaIsoReset(Value(shared,"nextResetTimestampUtc"));
                    bool hasLimit=Value(shared,"includedLimitZero") is bool ? !(bool)Value(shared,"includedLimitZero") : Value(shared,"hasNonZeroIncludedLimit") is bool && (bool)Value(shared,"hasNonZeroIncludedLimit");
                    // Unified subscriptions share the weekly allowance. Do not keep
                    // an obsolete monthly placeholder if that pool is unavailable.
                    if(hasLimit && percent.HasValue && reset>DateTimeOffset.UtcNow.ToUnixTimeSeconds()){
                        result.Data.Hour=null;result.Data.Week=new Limit {Remaining=Math.Max(0,100-percent.Value),Reset=reset,Minutes=10080};
                    }else throw new QuotaFetchException("Cursor 暂未返回完整周额度，保留上次读数。","Cursor weekly quota is temporarily incomplete; keeping the previous reading.",incomplete:true);
                }
            }catch(QuotaFetchException ex){if(ex.Incomplete)throw;result.RetryAt=ex.RetryAt;}
            catch(FormatException){}catch(ArgumentException){}
            if(result.Data.Hour==null && result.Data.Week==null){
                var raw=QuotaJson(text);var plan=Map(Value(Map(Value(raw,"individualUsage")),"plan"));
                bool explicitEmpty=Value(raw,"isUnlimited") is bool && (bool)Value(raw,"isUnlimited") || Value(plan,"enabled") is bool && !(bool)Value(plan,"enabled");
                if(!explicitEmpty)throw new QuotaFetchException("Cursor 暂未返回有效额度，保留上次读数。","Cursor quota is temporarily unavailable; keeping the previous reading.");
            }
            var confirmed=ReadCursorCredential(mode,roots);
            if(confirmed.Source!=credential.Source || confirmed.LoginVersion!=credential.LoginVersion)throw new QuotaFetchException("Cursor 账户已变化，请重新读取当前账户。","Cursor account changed. Read the current account again.");
            return result;
        } catch(QuotaFetchException ex){if(ex.AuthRejected)ForgetClientCredential(credential);throw;}
        catch(Exception){throw new QuotaFetchException("Cursor 登录态或额度格式不可用，请检查客户端登录。","Cursor sign-in or quota format is unavailable. Check client sign-in.");}
    }
    static Snapshot QueryClaudeOnline(string mode,ClientRoots roots,QuotaTransport send) {
        ClientCredential credential=null;
        try {
            credential=ReadClaudeCredential(mode,roots);
            if(credential.OAuth) {
                var headers=new Dictionary<string,string>{{"Authorization","Bearer "+credential.Header},{"anthropic-beta","oauth-2025-04-20"}};
                var result=ParseClaudeOnline(ReadQuotaEndpoint("https://api.anthropic.com/api/oauth/usage",headers,send),DateTimeOffset.UtcNow,credential.Source);result.LoginVersion=credential.LoginVersion;return result;
            }
            var cookies=new Dictionary<string,string>{{"Cookie","sessionKey="+credential.Header}};
            string org=credential.Organization;
            if(org==null) {
                var response=ReadQuotaEndpoint("https://claude.ai/api/organizations",cookies,send);
                var list=new JavaScriptSerializer {MaxJsonLength=1048576}.DeserializeObject(response) as object[];
                if(list==null || list.Length!=1)throw new QuotaFetchException("Claude 有多个组织或未返回组织，请先在桌面端选择组织。","Claude has multiple or unavailable organizations. Select one in Desktop first.");
                org=Value(Map(list[0]),"uuid") as string;
            }
            Guid identifier;if(!Guid.TryParse(org,out identifier))throw new FormatException("Invalid organization.");
            var desktop=ParseClaudeOnline(ReadQuotaEndpoint("https://claude.ai/api/organizations/"+identifier.ToString("D")+"/usage",cookies,send),DateTimeOffset.UtcNow,credential.Source);desktop.LoginVersion=credential.LoginVersion;return desktop;
        } catch(QuotaFetchException ex){if(ex.AuthRejected)ForgetClientCredential(credential);throw;}
        catch(Exception){throw new QuotaFetchException("Claude 登录态或额度格式不可用，请检查客户端登录。","Claude sign-in or quota format is unavailable. Check client sign-in.");}
    }
}
