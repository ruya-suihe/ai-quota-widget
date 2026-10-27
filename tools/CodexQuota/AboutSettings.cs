using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

partial class QuotaWidget {
    static StackPanel CreateAboutSettings(out Action labels) {
        var panel=new StackPanel {Margin=new Thickness(0,10,0,0)};
        var name=new TextBlock {Text="AI Quota Widget",FontSize=18,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center};
        var description=new TextBlock {Foreground=SettingsBrush("Secondary"),TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};
        var version=new TextBlock {Foreground=SettingsBrush("Secondary"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,6,0,16)};
        panel.Children.Add(name);panel.Children.Add(description);panel.Children.Add(version);
        var bitmap=new BitmapImage();
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ProjectQr.png")) {
            bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
        }
        var image=new Image {Source=bitmap,Width=164,Height=164,ToolTip=AppServices.ProjectUrl,Name="ProjectQr"};
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        panel.Children.Add(new Border {Background=Brushes.White,Padding=new Thickness(8),CornerRadius=new CornerRadius(5),HorizontalAlignment=HorizontalAlignment.Center,Child=image});
        var hint=new TextBlock {Foreground=SettingsBrush("Secondary"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,10,0,10)};panel.Children.Add(hint);
        var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
        var project=new Button {Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0),ToolTip=AppServices.ProjectUrl};
        var author=new Button {Padding=new Thickness(12,6,12,6),ToolTip=AppServices.AuthorUrl};
        project.Click+=(s,e)=>AppServices.OpenProject();author.Click+=(s,e)=>AppServices.OpenAuthor();
        actions.Children.Add(project);actions.Children.Add(author);panel.Children.Add(actions);
        var url=new TextBlock {Text=AppServices.ProjectUrl,Foreground=SettingsBrush("Secondary"),FontSize=11,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,10,12,0)};
        panel.Children.Add(url);
        var credit=new TextBlock {Foreground=SettingsBrush("Secondary"),TextAlignment=TextAlignment.Center,Margin=new Thickness(0,12,0,0)};panel.Children.Add(credit);
        labels=()=>{
            description.Text=Text("AI 编程工具额度桌面悬浮窗","A desktop quota widget for AI coding tools");
            version.Text=Text("版本 ","Version ")+AppServices.CurrentVersion;
            hint.Text=Text("扫码查看项目、使用指南与更新","Scan for the project, guide and updates");
            project.Content=Text("打开项目主页 ↗","Open project ↗");author.Content=Text("作者 GitHub ↗","Author GitHub ↗");
            credit.Text=Text("作者：儒雅随和","By 儒雅随和");
        };
        labels();return panel;
    }
}
