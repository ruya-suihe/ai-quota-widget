using System;
using System.IO;
using System.Globalization;
using System.Web.Script.Serialization;

partial class QuotaWidget {
    static string ClaudeDirectoryOverride="",ClaudeUsageFile="";
    sealed class ClaudeConnectionOptions {public string Directory="",UsageFile="";}
    static string ClaudeConnectionFile(){return Path.Combine(ProviderFolder(),"claude-connection.json");}
    static void LoadClaudeConnectionOptions(){
        try{
            string path=ClaudeConnectionFile();if(!File.Exists(path) || new FileInfo(path).Length>16384)return;
            var options=new JavaScriptSerializer().Deserialize<ClaudeConnectionOptions>(File.ReadAllText(path));
            if(options==null)return;
            if(!string.IsNullOrWhiteSpace(options.Directory) && Path.IsPathRooted(options.Directory))ClaudeDirectoryOverride=Path.GetFullPath(options.Directory);
            if(!string.IsNullOrWhiteSpace(options.UsageFile) && Path.IsPathRooted(options.UsageFile))ClaudeUsageFile=Path.GetFullPath(options.UsageFile);
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
    }
    static void ApplyClaudeConnection(string directory,string usageFile,string source){
        string selectedDirectory=directory==null?ClaudeDirectoryOverride:Path.GetFullPath(directory);
        string selectedFile=usageFile==null?ClaudeUsageFile:usageFile.Length==0?"":Path.GetFullPath(usageFile);
        if(!checking){
            string temporary=ClaudeConnectionFile()+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{
                Directory.CreateDirectory(ProviderFolder());
                File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(new ClaudeConnectionOptions {Directory=selectedDirectory,UsageFile=selectedFile}));
                if(!MoveFileEx(temporary,ClaudeConnectionFile(),0x1|0x8))throw new IOException();
            }catch(IOException){throw new QuotaFetchException("Claude 接入选择未能保存，请重试。","Could not save the Claude connection selection. Retry.");}
            catch(UnauthorizedAccessException){throw new QuotaFetchException("没有权限保存 Claude 接入选择。","Cannot save the Claude connection selection: access denied.");}
            finally{if(File.Exists(temporary))try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        ClaudeDirectoryOverride=selectedDirectory;ClaudeUsageFile=selectedFile;
        // Explicit directory/file selection can represent another account. Invalidate
        // pending work and old readings even when the mode name did not change.
        SetProviderSource(source,true);Render();if(!checking)Refresh(currentProvider.SupportsQuery);
    }
    static Snapshot ReadClaudeUsageFile(string path,DateTimeOffset now){
        try{
            var raw=QuotaJson(ReadSmallClientFile(path));var updated=QuotaNumber(raw,"updated_at");
            if(!updated.HasValue)throw new FormatException("Missing observation time.");
            var observed=DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(updated.Value));
            if(observed>now.AddMinutes(1))throw new FormatException("Future observation.");
            var limits=Map(Value(raw,"rate_limits"))??raw;
            var sanitized=new {rate_limits=new {five_hour=Value(limits,"five_hour"),seven_day=Value(limits,"seven_day")}};
            var data=ParseClaudeStatus(new JavaScriptSerializer().Serialize(sanitized),now);
            if(data.Hour==null && data.Week==null)throw new QuotaFetchException("文件没有有效的 Claude Code 额度；请先让 Claude Code 产生一次用量数据。","The file has no valid Claude Code quota. Let Claude Code produce a usage reading first.");
            data.Time=data.HourTime=data.WeekTime=observed;data.Source="Claude Code usage file";data.LoginVersion=PublicBinding(Path.GetFullPath(path));return data;
        }catch(QuotaFetchException){throw;}
        catch(Exception){throw new QuotaFetchException("无法读取 Claude Code 用量文件：需要 updated_at 毫秒时间和 five_hour／seven_day 额度。","Cannot read Claude Code usage file: expected updated_at in milliseconds and five_hour/seven_day quotas.");}
    }
}
