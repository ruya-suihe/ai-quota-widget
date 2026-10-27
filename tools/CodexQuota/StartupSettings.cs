using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

partial class QuotaWidget {
    static DispatcherTimer updateTimer;
    static bool updateNoticeShown;
    static StackPanel CreateStartupSettings(bool guide,out Action labels) {
        var foreground=guide ? SettingsBrush("Guide") : SettingsBrush("Text");
        var panel=new StackPanel();
        var auto=new CheckBox {IsChecked=AppServices.AutoCheck,Margin=new Thickness(0,0,0,8)};
        var startup=new CheckBox {Margin=new Thickness(0,0,0,8)};
        try {startup.IsChecked=AppServices.IsStartupEnabled();} catch(Exception) {startup.IsEnabled=false;}
        var check=new Button {Padding=new Thickness(10,5,10,5),HorizontalAlignment=HorizontalAlignment.Left};
        var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
        var open=new Button {Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,8,0,0),HorizontalAlignment=HorizontalAlignment.Left};
        foreach(var element in new UIElement[]{auto,startup,check,status,open}) {var control=element as System.Windows.Controls.Control;if(control!=null) control.Foreground=foreground;var text=element as TextBlock;if(text!=null) text.Foreground=foreground;}
        var startupHeading=new TextBlock();var updateHeading=new TextBlock();
        panel.Children.Add(SettingsGroup(startupHeading,new UIElement[]{startup}));
        panel.Children.Add(SettingsGroup(updateHeading,new UIElement[]{auto,status,check,open}));
        Action sync=()=>{
            startupHeading.Text=Text("启动","Startup");updateHeading.Text=Text("更新","Updates");
            auto.Content=Text("自动检查更新（启动及每6小时）","Check updates on launch and every 6 hours");startup.Content=Text("开机启动","Start with Windows");check.Content=Text("立即检查更新","Check now");open.Content=Text("打开发布页面","Open release page");
            check.IsEnabled=!AppServices.Checking;open.Visibility=AppServices.LatestUrl.Length>0 ? Visibility.Visible : Visibility.Collapsed;
            status.Text=AppServices.Checking ? Text("正在检查…","Checking…") : AppServices.Error.Length>0 ? UpdateErrorText(AppServices.Error) : AppServices.NoRelease ? Text("已连接，尚无公开发布版本。","Connected. No public release yet.") : AppServices.LatestUrl.Length>0 ? Text("发现新版本：","New version: ")+AppServices.LatestVersion : AppServices.LatestVersion.Length>0 ? Text("当前已是最新版本。","You are up to date.") : Text("点击检查，获取最新版本信息。","Check for the latest version.");
        };
        labels=sync;sync();AppServices.Changed+=sync;
        settingsWindow.Closed+=(s,e)=>AppServices.Changed-=sync;
        auto.Click+=(s,e)=>{
            try {AppServices.SetAutoCheck(auto.IsChecked==true);if(AppServices.AutoCheck) AppServices.CheckUpdates();}
            catch(Exception) {auto.IsChecked=AppServices.AutoCheck;MessageBox.Show(Text("无法保存更新设置。","Could not save update settings."));}
        };
        startup.Click+=(s,e)=>{
            try {AppServices.SetStartup(startup.IsChecked==true);}
            catch(Exception) {startup.IsChecked=AppServices.IsStartupEnabled();MessageBox.Show(Text("无法修改开机启动设置。","Could not change startup settings."));}
        };
        check.Click+=(s,e)=>AppServices.CheckUpdates();open.Click+=(s,e)=>AppServices.OpenRelease();return panel;
    }
    static string UpdateErrorText(string error) {
        switch(error) {
            case "dns":return Text("无法解析 GitHub 地址，请检查网络或代理。","Cannot resolve GitHub. Check network or proxy.");
            case "timeout":return Text("连接超时，请稍后重试。","Connection timed out. Try again later.");
            case "tls":return Text("安全连接失败，请检查系统时间、证书或代理。","Secure connection failed. Check clock, certificates or proxy.");
            case "limited":return Text("GitHub 拒绝请求或请求过多，请稍后重试。","GitHub denied the request or rate limit reached. Retry later.");
            case "unavailable":return Text("发行仓库不存在或访问受限。","Release repository unavailable or access restricted.");
            case "network":return Text("无法连接 GitHub，请检查网络或代理。","Cannot connect to GitHub. Check network or proxy.");
            case "invalid":return Text("GitHub 返回的版本信息无效。","GitHub returned invalid release information.");
            default:return Text("GitHub 服务返回错误：","GitHub returned an error: ")+error;
        }
    }
    static void InitializeUpdates() {
        AppServices.Load(System.IO.Path.GetDirectoryName(PreferenceFile()));
        AppServices.Changed+=()=>{
            if(AppServices.LatestUrl.Length>0 && !updateNoticeShown) {
                updateNoticeShown=true;
                if(MessageBox.Show(Text("发现新版，打开 GitHub 发布页面？","A new version is available. Open the GitHub release page?"),"AI Quota Widget",MessageBoxButton.YesNo)==MessageBoxResult.Yes) AppServices.OpenRelease();
            }
        };
        updateTimer=new DispatcherTimer {Interval=TimeSpan.FromHours(6)};
        updateTimer.Tick+=(s,e)=>{if(AppServices.AutoCheck) AppServices.CheckUpdates();};updateTimer.Start();
        window.Closed+=(s,e)=>updateTimer.Stop();if(AppServices.AutoCheck) AppServices.CheckUpdates();
    }
}
