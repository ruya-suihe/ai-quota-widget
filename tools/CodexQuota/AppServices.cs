using System;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Microsoft.Win32;

[assembly: AssemblyVersion("0.1.12.2")]
// Update metadata comes from the public project repository.
internal static class AppServices {
    public static readonly Version CurrentVersion=new Version(0,1,12,2);
    public const string AuthorUrl="https://github.com/ruya-suihe";
    public const string ProjectUrl="https://github.com/ruya-suihe/ai-quota-widget";
    const string RepositoryApi="https://api.github.com/repositories/1421835446";
    const string Api=RepositoryApi+"/releases/latest";
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName="CodexUsageWidget";
    static string optionsFile;
    public static bool AutoCheck, Checking;
    public static string LatestUrl="", LatestVersion="", Error="";
    public static bool NoRelease;
    public static event Action Changed;
    class Options { public bool AutoCheck; }
    public static void Load(string folder) {
        optionsFile=Path.Combine(folder,"startup-update.json");
        try {if(File.Exists(optionsFile)) AutoCheck=new JavaScriptSerializer().Deserialize<Options>(File.ReadAllText(optionsFile)).AutoCheck;} catch(Exception) {AutoCheck=false;}
    }
    public static void SetAutoCheck(bool value) {
        Directory.CreateDirectory(Path.GetDirectoryName(optionsFile));
        File.WriteAllText(optionsFile,new JavaScriptSerializer().Serialize(new Options {AutoCheck=value}));AutoCheck=value;
    }
    public static bool IsStartupEnabled() {
        using(var key=Registry.CurrentUser.OpenSubKey(RunKey)) return key!=null && (string)key.GetValue(RunName)==StartupCommand();
    }
    static string StartupCommand() {return "\""+Assembly.GetExecutingAssembly().Location+"\"";}
    public static void SetStartup(bool enabled) {
        using(var key=Registry.CurrentUser.CreateSubKey(RunKey)) {
            if(enabled) key.SetValue(RunName,StartupCommand());else key.DeleteValue(RunName,false);
        }
    }
    public static bool IsNewer(string tag) {
        Version version;return Version.TryParse((tag ?? "").TrimStart('v','V'),out version) && version>CurrentVersion;
    }
    public static bool ValidReleaseUrl(string value) {
        Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri) && uri.Scheme=="https" && uri.Host=="github.com" && uri.IsDefaultPort && uri.UserInfo.Length==0
            && uri.AbsolutePath.StartsWith("/ruya-suihe/ai-quota-widget/releases/tag/",StringComparison.Ordinal) && uri.Segments.Length>5;
    }
    internal class UpdateRequestException:Exception {
        public readonly int Status;public readonly WebExceptionStatus NetworkStatus;
        public UpdateRequestException(int status,WebExceptionStatus networkStatus) {Status=status;NetworkStatus=networkStatus;}
    }
    internal class UpdateResult {public string Version="",Url="",Error="";public bool NoRelease;}
    static Dictionary<string,object> GetUpdateJson(string url) {
        // Older .NET Framework defaults may omit TLS 1.2, which GitHub requires.
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="CodexUsageWidget/"+CurrentVersion;request.Accept="application/vnd.github+json";request.Timeout=15000;request.ReadWriteTimeout=15000;
        try {
            using(var response=request.GetResponse()) using(var reader=new StreamReader(response.GetResponseStream()))
                return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
        } catch(WebException ex) {
            var response=ex.Response as HttpWebResponse;int status=response==null ? 0 : (int)response.StatusCode;
            if(response!=null) response.Dispose();throw new UpdateRequestException(status,ex.Status);
        }
    }
    static string UpdateError(Exception error) {
        var request=error as UpdateRequestException;
        if(request==null) return "invalid";
        if(request.Status==403 || request.Status==429) return "limited";
        if(request.Status==401 || request.Status==404) return "unavailable";
        if(request.Status!=0) return "http:"+request.Status;
        if(request.NetworkStatus==WebExceptionStatus.Timeout) return "timeout";
        if(request.NetworkStatus==WebExceptionStatus.NameResolutionFailure || request.NetworkStatus==WebExceptionStatus.ProxyNameResolutionFailure) return "dns";
        if(request.NetworkStatus==WebExceptionStatus.TrustFailure || request.NetworkStatus==WebExceptionStatus.SecureChannelFailure) return "tls";
        return "network";
    }
    internal static UpdateResult ReadUpdate(Func<string,Dictionary<string,object>> get) {
        try {
            Dictionary<string,object> result;
            try {result=get(Api);} catch(UpdateRequestException ex) {
                if(ex.Status!=404) throw;
                var repository=get(RepositoryApi);object id;
                if(repository==null || !repository.TryGetValue("id",out id) || Convert.ToString(id)!="1421835446") throw new InvalidDataException();
                return new UpdateResult {NoRelease=true};
            }
            object tagValue,urlValue;
            if(result==null || !result.TryGetValue("tag_name",out tagValue) || !result.TryGetValue("html_url",out urlValue)) throw new InvalidDataException();
            string tag=Convert.ToString(tagValue),url=Convert.ToString(urlValue);Version version;
            if(!Version.TryParse(tag.TrimStart('v','V'),out version) || !ValidReleaseUrl(url)) throw new InvalidDataException();
            return new UpdateResult {Version=tag,Url=IsNewer(tag) ? url : ""};
        } catch(Exception ex) {return new UpdateResult {Error=UpdateError(ex)};}
    }
    public static async void CheckUpdates() {
        if(Checking) return;Checking=true;Error="";Notify();
        try {
            var result=await Task.Run(()=>ReadUpdate(GetUpdateJson));
            LatestVersion=result.Version;LatestUrl=result.Url;Error=result.Error;NoRelease=result.NoRelease;
        } catch(Exception) {LatestVersion=LatestUrl="";NoRelease=false;Error="invalid";}
        finally {Checking=false;Notify();}
    }
    static void Notify() {if(Changed!=null) Changed();}
    public static void OpenRelease() {if(ValidReleaseUrl(LatestUrl)) Process.Start(new ProcessStartInfo(LatestUrl) {UseShellExecute=true});}
    public static void OpenProject() {Process.Start(new ProcessStartInfo(ProjectUrl) {UseShellExecute=true});}
    public static void OpenAuthor() {Process.Start(new ProcessStartInfo(AuthorUrl) {UseShellExecute=true});}
}
