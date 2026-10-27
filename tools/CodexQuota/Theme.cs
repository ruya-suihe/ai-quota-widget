using System;
using System.Collections.Generic;
using System.Windows.Media;

public static class Theme
{
    public const int PresetCount = 12;
    public static bool IsAnimatedPreset(int index) {return index==ScifiTheme.PresetIndex || index==TidalScene.PresetIndex || index==InkScene.PresetIndex || index==SteamScene.PresetIndex || index==StarryScene.PresetIndex;}
    public static readonly string[] Keys = { "quotaGreen", "quotaYellow", "quotaOrange", "quotaRed", "timeGreen", "timeYellow", "timeOrange", "timeRed", "title", "label", "valueText", "timeText", "caption", "status", "alert", "ticks", "barBorder", "windowBorder", "track", "background", "icons", "weekMarker", "failure" };
    public static readonly string[] Chinese = { "额度 · ≥30%", "额度 · 20%–<30%", "额度 · 10%–<20%", "额度 · <10%", "时间 · ≤20%", "时间 · >20%–40%", "时间 · >40%–60%", "时间 · >60%", "窗口标题", "标签 / 分隔符（含 5h/Wk）", "展开 · 百分比文字", "展开 · 倒计时文字", "收起 · 条内数值文字", "状态说明", "提醒文字", "刻度", "条形外框", "窗口外框", "空条底色", "窗口背景", "工具栏图标", "周参考箭头", "刷新失败提示" };
    public static readonly string[] English = { "Quota · ≥30%", "Quota · 20%–<30%", "Quota · 10%–<20%", "Quota · <10%", "Time · ≤20%", "Time · >20%–40%", "Time · >40%–60%", "Time · >60%", "Window title", "Labels / separators (5h/Wk)", "Expanded · percentage", "Expanded · countdown", "Compact · value caption", "Status text", "Alert text", "Ticks", "Bar outline", "Window outline", "Empty track", "Window background", "Toolbar icons", "Weekly reference arrow", "Refresh failure" };
    public static Dictionary<string,string> Colors = Defaults();
    public static Dictionary<string,string> Defaults() {
        string[] values = { "#438A70", "#C3A356", "#C17D4D", "#B95F6A", "#438A70", "#C3A356", "#C17D4D", "#B95F6A", "#F2F4F7", "#F2F4F7", "#F2F4F7", "#F2F4F7", "#F2F4F7", "#AAB3C2", "#C17D4D", "#15181E", "#77859B", "#444C59", "#414B5B", "#20242C", "#AAB3C2", "#C9F5FF", "#B95F6A" };
        var result = new Dictionary<string,string>();
        for (int i=0;i<Keys.Length;i++) result[Keys[i]]=values[i];
        return result;
    }
    public static Dictionary<string,string> Preset(int index) {
        if(index==ScifiTheme.PresetIndex)return ScifiTheme.Palette();
        if(index==TidalScene.PresetIndex)return TidalScene.Palette();
        if(index==InkScene.PresetIndex)return InkScene.Palette();
        if(index==SteamScene.PresetIndex)return SteamScene.Palette();
        if(index==StarryScene.PresetIndex)return StarryScene.Palette();
        var result=Defaults();
        string[][] fills={
            new[]{"#ABC0D4","#D4B56C","#D2A07D","#C98F98"},
            new[]{"#E1BA96","#D6B769","#CE966B","#C88890"},
            new[]{"#5B7088","#94834D","#967557","#95616A"},
            new[]{"#756245","#968146","#966B44","#995661"},
            new[]{"#80B7C2","#D1B96D","#CA9B72","#BF8591"},
            new[]{"#D1BCA0","#D1B461","#C68E65","#B87980"},
            new[]{"#49748A","#9F8D53","#A47651","#A65F71"}
        };
        string[][] surfaces={
            new[]{"#F7F8FA","#DCE3E9","#A0ADB9","#A0ADB9","#233140","#526171","#687786","#9C5200","#153F67","#A52239"},
            new[]{"#FCF3E8","#E7D9CC","#AE9782","#AE9782","#43352C","#735347","#7B6350","#9A450C","#145653","#A52239"},
            new[]{"#6B7077","#4A515C","#BDC5CE","#BDC5CE","#F7F9FC","#F7F9FC","#26313F","#FFE08C","#CFF0FC","#FFBBC4"},
            new[]{"#1F2329","#39424C","#78818B","#78818B","#F4F2EC","#CFC9BE","#12191F","#FFE08C","#B8F0FA","#FFBBC4"},
            new[]{"#ECF6F6","#D0E4E7","#819EAA","#819EAA","#23414A","#526D79","#526D79","#9C5200","#0D4C5C","#A52239"},
            new[]{"#B3ABA2","#A39687","#665D53","#665D53","#342D25","#4D443A","#4D443A","#7C3E13","#352519","#8D2439"},
            new[]{"#182D3B","#344C5F","#7C9AAC","#7C9AAC","#F1F6FA","#C0D2DD","#152632","#FFE08C","#C7F0F7","#FFBBC4"}
        };
        string[] tones={"Green","Yellow","Orange","Red"};
        for(int i=0;i<4;i++) {result["quota"+tones[i]]=fills[index][i];result["time"+tones[i]]=fills[index][i];}
        string[] keys={"background","track","barBorder","windowBorder","title","icons","ticks","alert","weekMarker","failure"};
        for(int i=0;i<keys.Length;i++) result[keys[i]]=surfaces[index][i];
        foreach(string key in new[]{"label","valueText","timeText","caption"}) result[key]=result["title"];
        result["status"]=result["icons"];
        // Keep the approved hues, with enough contrast for captions at every quota level.
        foreach(string tone in tones) {
            string fill=ReadableFill(result["quota"+tone],result["caption"]);
            result["quota"+tone]=result["time"+tone]=fill;
        }
        return result;
    }
    public static string PresetName(int index,bool english) {
        if(index==ScifiTheme.PresetIndex)return english ? "Sci-fi console" : "科幻终端";
        if(index==TidalScene.PresetIndex)return english ? "Sun & moon tides" : "日月潮汐";
        if(index==InkScene.PresetIndex)return english ? "Ink landscape" : "山水墨韵";
        if(index==SteamScene.PresetIndex)return english ? "Steam workshop" : "蒸汽工坊";
        if(index==StarryScene.PresetIndex)return english ? "Starry night" : "流光星夜";
        return (english ? new[]{"Paper white","Soft apricot","Neutral stone","Charcoal brass","Sea salt","Warm grey","Deep ocean"} : new[]{"纸白 · 办公","杏色 · 温和","岩灰 · 中性","炭黑 · 黄铜","海盐 · 清爽","暖灰 · 稳重","深海 · 蓝调"})[index];
    }
    static double Luminance(Color color) {
        Func<byte,double> linear=b=>{double v=b/255.0;return v<=0.04045 ? v/12.92 : Math.Pow((v+0.055)/1.055,2.4);};
        return 0.2126*linear(color.R)+0.7152*linear(color.G)+0.0722*linear(color.B);
    }
    static string ReadableFill(string hex,string text) {
        var color=(Color)ColorConverter.ConvertFromString(hex);double foreground=Luminance((Color)ColorConverter.ConvertFromString(text));
        int target=foreground<0.5 ? 255 : 0;
        for(int step=0;step<=20;step++) {
            double amount=step/20.0;
            var candidate=Color.FromRgb((byte)Math.Round(color.R+(target-color.R)*amount),(byte)Math.Round(color.G+(target-color.G)*amount),(byte)Math.Round(color.B+(target-color.B)*amount));
            double background=Luminance(candidate);
            if((Math.Max(foreground,background)+0.05)/(Math.Min(foreground,background)+0.05)>=4.5) return "#"+candidate.R.ToString("X2")+candidate.G.ToString("X2")+candidate.B.ToString("X2");
        }
        return hex;
    }
    public static Brush Brush(string key) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Colors[key])); }
    public static bool Valid(string value) {
        if (value == null || (value.Length != 7 && value.Length != 9) || value[0] != '#') return false;
        for (int i=1;i<value.Length;i++) if (!Uri.IsHexDigit(value[i])) return false;
        return true;
    }
}
