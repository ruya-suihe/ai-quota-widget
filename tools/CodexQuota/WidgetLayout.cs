using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal sealed class LayoutRowInput {
    public readonly string Prefix, Label, Value, Reset, CompactLabel, Caption, FallbackCaption;
    public readonly bool Present;
    public LayoutRowInput(string prefix,bool present,string label,string value,string reset,string compactLabel,string caption,string fallback) {
        Prefix=prefix;Present=present;Label=label;Value=value;Reset=reset;
        CompactLabel=compactLabel;Caption=caption;FallbackCaption=fallback;
    }
}
internal sealed class LayoutInput {
    public readonly double Width, Scale, TextThreshold, Opacity, OutlineWidth, WaveTravel;
    public readonly double HourBarHeight,WeekBarHeight;
    public readonly bool Expanded, NoExpand, IconsPinned, ToolbarTop, WaveEnabled, WaveOverText, Minimal;
    public readonly string Title;
    public readonly string[] ToolbarOrder;
    public readonly LayoutRowInput[] Rows;
    public LayoutInput(double width,double scale,double threshold,double opacity,double outline,bool expanded,bool noExpand,
        bool pinned,bool top,string title,string[] order,LayoutRowInput[] rows,bool wave,bool overText,double travel,bool minimal=false,
        double hourBarHeight=LayoutMetrics.DefaultHourBarHeight,double weekBarHeight=LayoutMetrics.DefaultWeekBarHeight) {
        Width=width;Scale=scale;TextThreshold=threshold;Opacity=opacity;OutlineWidth=outline;
        Expanded=expanded;NoExpand=noExpand;IconsPinned=pinned;ToolbarTop=top;Title=title;
        ToolbarOrder=(string[])order.Clone();Rows=(LayoutRowInput[])rows.Clone();
        WaveEnabled=wave;WaveOverText=overText;WaveTravel=travel;
        Minimal=minimal;
        HourBarHeight=LayoutMetrics.NormalizeBarHeight(hourBarHeight,LayoutMetrics.DefaultHourBarHeight);
        WeekBarHeight=LayoutMetrics.NormalizeBarHeight(weekBarHeight,LayoutMetrics.DefaultWeekBarHeight);
    }
}
internal sealed class LayoutRowResult {
    public readonly LayoutRowInput Input;
    public readonly string Caption, InlineLabel, Kind;
    public LayoutRowResult(LayoutRowInput input,string caption,string label,string kind) {
        Input=input;Caption=caption;InlineLabel=label;Kind=kind;
    }
}
internal sealed class LayoutResult {
    public readonly bool Compact, HiddenText, ShowIcons, Narrow, ShowTitle, ShowDetailLabel, ShowTime;
    public readonly double TitleSize, IconSize, ButtonSlot, CompactTextSize, LabelColumn, ValueColumn, TimeColumn;
    public readonly Thickness Padding, QuotaMargin;
    public readonly LayoutRowResult[] Rows;
    public LayoutResult(bool compact,bool hidden,bool icons,bool narrow,bool title,bool label,bool time,
        double titleSize,double icon,double slot,double compactSize,double labelWidth,double valueWidth,double timeWidth,
        Thickness padding,Thickness quotaMargin,LayoutRowResult[] rows) {
        Compact=compact;HiddenText=hidden;ShowIcons=icons;Narrow=narrow;ShowTitle=title;ShowDetailLabel=label;ShowTime=time;
        TitleSize=titleSize;IconSize=icon;ButtonSlot=slot;CompactTextSize=compactSize;
        LabelColumn=labelWidth;ValueColumn=valueWidth;TimeColumn=timeWidth;
        Padding=padding;QuotaMargin=quotaMargin;Rows=rows;
    }
}

// No stored Window, quota snapshot, configuration, timers, or business state.
// Calculate depends only on explicit inputs and a supplied text measurer.
internal static class WidgetLayout {
    internal static LayoutResult Calculate(LayoutInput input,Func<string,double,double> measure) {
        bool compact=input.Minimal || input.NoExpand || !input.Expanded;
        bool hidden=input.Minimal || compact && input.Scale<=input.TextThreshold;
        bool narrow=input.Width<=LayoutMetrics.NarrowWidth;
        double verticalInset=LayoutMetrics.VerticalInsetAtUnitScale/input.Scale;
        double horizontalInset=verticalInset+LayoutMetrics.HorizontalExtraInsetAtUnitScale/input.Scale;
        double available=input.Width-2*(horizontalInset+LayoutMetrics.NormalBorder);
        double titleSize=LayoutMetrics.QuotaFontSize;
        double icon=LayoutMetrics.IconSizeAtUnitScale/Math.Max(1,input.Scale);
        double slot=icon+LayoutMetrics.IconGapAtUnitScale/input.Scale;
        if(!compact && measure(input.Title,titleSize)+slot*input.ToolbarOrder.Length+LayoutMetrics.TitleIconGap>available)
            titleSize=LayoutMetrics.SmallTitleFontSize;
        bool showTitle=!compact && measure(input.Title,titleSize)+slot*input.ToolbarOrder.Length+LayoutMetrics.TitleIconGap<=available;
        double room=available-(showTitle ? measure(input.Title,titleSize)+LayoutMetrics.TitleIconGap : 0);
        slot=Math.Min(slot,room/input.ToolbarOrder.Length);
        icon=Math.Min(icon,Math.Max(LayoutMetrics.MinimumIconSize,slot-LayoutMetrics.IconSlotInset));

        double labelWidth=0,valueWidth=0,timeWidth=0;
        foreach(var row in input.Rows.Where(row=>row.Present)) {
            labelWidth=Math.Max(labelWidth,measure(row.Label,LayoutMetrics.QuotaFontSize));
            valueWidth=Math.Max(valueWidth,measure(row.Value,LayoutMetrics.QuotaFontSize));
            timeWidth=Math.Max(timeWidth,measure(row.Reset,LayoutMetrics.QuotaFontSize));
        }
        labelWidth=Math.Ceiling(labelWidth)+LayoutMetrics.TextMeasureAllowance;
        valueWidth=Math.Ceiling(valueWidth)+LayoutMetrics.TextMeasureAllowance;
        timeWidth=Math.Ceiling(timeWidth)+LayoutMetrics.TextMeasureAllowance;
        bool showLabel=labelWidth+2*LayoutMetrics.DetailColumnGap+valueWidth+timeWidth<=available;
        bool showTime=valueWidth+LayoutMetrics.DetailColumnGap+timeWidth<=available;
        double compactSize=narrow ? LayoutMetrics.NarrowQuotaFontSize : LayoutMetrics.QuotaFontSize;
        var rows=input.Rows.Select(row=>{
            double captionWidth=measure(row.Caption,compactSize);
            string caption=row.Caption;
            if(hidden) caption="";
            else if(captionWidth>available-2*LayoutMetrics.BarTextInset) {
                caption=row.FallbackCaption;
                if(measure(caption,compactSize)>available-2*LayoutMetrics.BarTextInset) caption="";
            }
            Func<string,string,bool> fitsLabel=(label,text)=>narrow
                ? available-measure(text,compactSize)-2*LayoutMetrics.BarTextInset>=measure(label,compactSize)+LayoutMetrics.NarrowLabelGap
                : (available-measure(text,compactSize))/2>=LayoutMetrics.BarTextInset+measure(label,compactSize)+LayoutMetrics.CenteredLabelGap;
            string inlineLabel="";
            if(!hidden && caption.Length>0) {
                if(fitsLabel(row.CompactLabel,caption)) inlineLabel=row.CompactLabel;
                else if(narrow && row.Prefix=="Hour" && (row.CompactLabel=="五" || row.CompactLabel=="5h")) {
                    // Preserve the row's identity before dropping its label entirely.
                    string shortLabel=row.CompactLabel=="五" ? "五" : "5";
                    if(!fitsLabel(shortLabel,caption) && fitsLabel(shortLabel,row.FallbackCaption)) caption=row.FallbackCaption;
                    if(fitsLabel(shortLabel,caption)) inlineLabel=shortLabel;
                }
            }
            string kind=(caption.Length==0 ? "quota-detail" : "quota")+(row.Prefix=="Week" ? "-week" : "")+(input.Minimal ? "-minimal" : "")
                +(narrow ? "-narrow-right" : "")+"-center";
            return new LayoutRowResult(row,caption,inlineLabel,kind);
        }).ToArray();
        double extra=input.OutlineWidth-LayoutMetrics.NormalBorder;
        return new LayoutResult(compact,hidden,input.IconsPinned || input.Expanded,narrow,showTitle,showLabel,showTime,
            titleSize,icon,slot,compactSize,labelWidth,valueWidth,timeWidth,
            new Thickness(horizontalInset-extra,verticalInset-extra,horizontalInset-extra,verticalInset-extra),
            new Thickness(0,input.ToolbarTop && !compact ? LayoutMetrics.VerticalInsetAtUnitScale/input.Scale : 0,0,0),rows);
    }
    static T Find<T>(Window window,string name) where T:class {return window.FindName(name) as T;}
    static void DataRows(Window window,LayoutInput input,bool hiddenText,double hourHeight=-1,double weekHeight=-1) {
        var rows=Find<Grid>(window,"Compact").RowDefinitions;
        foreach(var row in input.Rows) {
            string prefix=row.Prefix; Visibility visibility=row.Present ? Visibility.Visible : Visibility.Collapsed;
            if(prefix=="Week") {
                Find<Grid>(window,"WeekPair").Visibility=visibility;
                var auxiliary=row.Present && !input.Minimal ? Visibility.Visible : Visibility.Collapsed;
                Find<ProgressBar>(window,"CompactWeekGap").Visibility=auxiliary;
                Find<ProgressBar>(window,"CompactWeekMarker").Visibility=auxiliary;
            }
            foreach(string suffix in new[]{"Info","Bar","TimeBar"}) Find<FrameworkElement>(window,prefix+suffix).Visibility=visibility;
            foreach(string suffix in new[]{"Bar","TimeBar"}) Find<FrameworkElement>(window,"Compact"+prefix+suffix).Visibility=input.Minimal && suffix=="TimeBar" ? Visibility.Collapsed : visibility;
            int start=prefix=="Hour" ? 0 : 3;
            double requested=prefix=="Week" ? input.WeekBarHeight : input.HourBarHeight;
            if(prefix=="Hour" && hourHeight>=0)requested=hourHeight;
            if(prefix=="Week" && weekHeight>=0)requested=weekHeight;
            // Share the requested height in every mode; preserve the caption's readable minimum.
            double compactHeight=hiddenText ? requested : System.Math.Max(LayoutMetrics.CompactQuotaHeight,requested);
            Find<ProgressBar>(window,"Compact"+prefix+"Bar").Height=compactHeight;
            rows[start].Height=new GridLength(row.Present ? (input.Minimal ? compactHeight : compactHeight+2) : 0);
            Find<ProgressBar>(window,"Compact"+prefix+"TimeBar").Margin=new Thickness(0,compactHeight+3,0,0);
            Find<ProgressBar>(window,prefix+"Bar").Height=requested;
            if(prefix=="Week") {
                Find<Grid>(window,"WeekPair").RowDefinitions[0].Height=new GridLength(requested);
                Find<ProgressBar>(window,"WeekMarker").Height=requested+7;
                Find<ProgressBar>(window,"CompactWeekGap").Margin=new Thickness(0,compactHeight+1,0,0);
                Find<ProgressBar>(window,"CompactWeekMarker").Height=compactHeight+7;
            }
            rows[start+1].Height=new GridLength(row.Present && !input.Minimal ? LayoutMetrics.CompactTimerRowHeight : 0);
        }
        // Minimal rows fit the bars exactly; this spacer is the visible edge-to-edge gap.
        rows[2].Height=new GridLength(input.Rows.All(row=>row.Present) ? (input.Minimal ? LayoutMetrics.MinimalRowsGap/(input.Scale*VisualTreeHelper.GetDpi(window).DpiScaleY) : LayoutMetrics.CompactRowsGap) : 0);
    }
    internal static void FitBackgroundRatio(Window window,LayoutInput input,LayoutResult result,double aspect) {
        int count=input.Rows.Count(row=>row.Present);if(aspect<=0 || count==0)return;
        var root=Find<Border>(window,"Root");
        // Measure the actual text, toolbar and countdown overhead in this mode. A 36px
        // reference keeps QuotaArea.MinHeight from biasing the overhead for tiny manual bars.
        DataRows(window,input,result.HiddenText,36,36);InvalidateMeasures(root);
        root.Measure(new Size(input.Width*input.Scale,double.PositiveInfinity));
        double overhead=root.DesiredSize.Height/input.Scale-36*count;
        double minimum=result.Compact && !result.HiddenText ? LayoutMetrics.CompactQuotaHeight : LayoutMetrics.MinimumBarHeight;
        double remaining=Math.Max(0,input.Width/aspect-overhead-minimum*count);
        double weights=input.Rows.Where(row=>row.Present).Sum(row=>row.Prefix=="Hour"?input.HourBarHeight:input.WeekBarHeight);
        double hour=minimum+remaining*input.HourBarHeight/weights,week=minimum+remaining*input.WeekBarHeight/weights;
        // Derived heights may exceed the manual 36px ceiling; saved manual values are untouched.
        DataRows(window,input,result.HiddenText,hour,week);InvalidateMeasures(root);
        root.Measure(new Size(input.Width*input.Scale,double.PositiveInfinity));
    }
    static void InvalidateMeasures(DependencyObject node){
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)InvalidateMeasures(VisualTreeHelper.GetChild(node,i));
        var element=node as UIElement;if(element!=null)element.InvalidateMeasure();
    }
    internal static void Apply(Window window,LayoutInput input,LayoutResult result) {
        window.Opacity=input.Opacity;
        DataRows(window,input,result.HiddenText);
        var toolbar=Find<Grid>(window,"Toolbar");var details=Find<StackPanel>(window,"Details");
        var header=Find<Grid>(window,"HeaderGrid");var title=Find<TextBlock>(window,"TitleLabel");
        if(header.Parent!=details) {((Panel)header.Parent).Children.Remove(header);details.Children.Insert(0,header);}
        if(title.Parent!=toolbar) {((Panel)title.Parent).Children.Remove(title);toolbar.Children.Add(title);}
        header.Visibility=Visibility.Collapsed;
        Find<TextBlock>(window,"Updated").Visibility=Visibility.Collapsed;
        Find<TextBlock>(window,"TopUpdated").Visibility=Visibility.Collapsed;
        toolbar.Visibility=result.ShowIcons ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(toolbar,input.ToolbarTop ? 0 : 1);
        Grid.SetRow(Find<Grid>(window,"QuotaArea"),input.ToolbarTop ? 1 : 0);
        var layout=Find<Grid>(window,"LayoutRoot");
        layout.RowDefinitions[input.ToolbarTop ? 0 : 1].Height=new GridLength(result.ShowIcons
            ? (result.Compact ? LayoutMetrics.CompactToolbarHeight : LayoutMetrics.ExpandedToolbarHeight) : 0);
        layout.RowDefinitions[input.ToolbarTop ? 1 : 0].Height=GridLength.Auto;
        Find<Border>(window,"Root").Padding=result.Padding;
        Find<Grid>(window,"QuotaArea").Margin=result.QuotaMargin;
        toolbar.Margin=new Thickness(0);
        Find<Grid>(window,"Compact").Margin=new Thickness(0);
        title.Visibility=result.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        title.TextTrimming=TextTrimming.CharacterEllipsis;title.ToolTip=input.Title;
        title.FontFamily=new FontFamily(LayoutMetrics.FontFamily);
        title.FontSize=result.TitleSize;title.FontWeight=FontWeights.Bold;
        title.VerticalAlignment=VerticalAlignment.Bottom;
        title.Margin=new Thickness(0,0,LayoutMetrics.TitleIconGap,0);
        Grid.SetColumn(title,0);Grid.SetColumnSpan(title,1);
        toolbar.ColumnDefinitions[0].Width=new GridLength(1,GridUnitType.Star);
        for(int i=0;i<input.ToolbarOrder.Length;i++) {
            var button=Find<Button>(window,input.ToolbarOrder[i]);Grid.SetColumn(button,i+1);button.TabIndex=i;
            button.Width=result.ButtonSlot;button.VerticalAlignment=VerticalAlignment.Bottom;
            var icon=(Viewbox)button.Content;icon.Width=icon.Height=result.IconSize;
            toolbar.ColumnDefinitions[i+1].Width=new GridLength(result.ButtonSlot);
        }
        details.Visibility=result.Compact ? Visibility.Collapsed : Visibility.Visible;
        Find<Grid>(window,"Compact").Visibility=result.Compact ? Visibility.Visible : Visibility.Collapsed;
        foreach(var row in result.Rows) {
            string prefix=row.Input.Prefix;var info=Find<Grid>(window,prefix+"Info");
            // XAML values are startup defaults; runtime typography and its
            // measurements share these metrics so a font change stays coherent.
            foreach(var text in info.Children.OfType<TextBlock>()) text.FontFamily=new FontFamily(LayoutMetrics.FontFamily);
            foreach(string suffix in new[]{"Label","Value","Reset"})
                Find<TextBlock>(window,prefix+suffix).FontSize=LayoutMetrics.QuotaFontSize;
            info.ColumnDefinitions[0].Width=new GridLength(result.ShowDetailLabel ? result.LabelColumn : 0);
            info.ColumnDefinitions[1].Width=new GridLength(result.ShowDetailLabel ? LayoutMetrics.DetailColumnGap : 0);
            var valueText=Find<TextBlock>(window,prefix+"Value");
            valueText.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            info.ColumnDefinitions[2].Width=new GridLength(Math.Ceiling(valueText.DesiredSize.Width));
            info.ColumnDefinitions[3].Width=new GridLength(result.ShowTime ? LayoutMetrics.DetailColumnGap : 0);
            info.ColumnDefinitions[4].Width=new GridLength(result.ShowTime ? result.TimeColumn : 0);
            info.ColumnDefinitions[5].Width=new GridLength(1,GridUnitType.Star);
            Find<TextBlock>(window,prefix+"Label").Visibility=result.ShowDetailLabel ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>(window,prefix+"Reset").Visibility=result.ShowTime ? Visibility.Visible : Visibility.Collapsed;
            valueText.TextAlignment=TextAlignment.Left;
            Find<TextBlock>(window,prefix+"Reset").TextAlignment=TextAlignment.Left;
            // Start-align percentages; each separator follows its own value with equal side gaps.
            foreach(var text in info.Children.OfType<TextBlock>().Where(text=>text.Text=="|")) {
                text.TextAlignment=TextAlignment.Center;text.FontSize=LayoutMetrics.QuotaFontSize;
                text.VerticalAlignment=VerticalAlignment.Center;
                text.Visibility=(Grid.GetColumn(text)==1 ? result.ShowDetailLabel : result.ShowTime) ? Visibility.Visible : Visibility.Collapsed;
            }
            var oldLabel=Find<TextBlock>(window,"Compact"+prefix+"Label");
            oldLabel.FontSize=result.CompactTextSize;oldLabel.Text=row.Input.CompactLabel;oldLabel.Visibility=Visibility.Collapsed;
            var bar=Find<ProgressBar>(window,"Compact"+prefix+"Bar");
            bar.SetValue(SegmentedVisual.LabelProperty,row.InlineLabel);bar.Uid=row.Caption;bar.Tag=row.Kind;
        }
        Find<ProgressBar>(window,"WeekMarker").SetValue(SegmentedVisual.MarkerWidthProperty,LayoutMetrics.NormalMarkerWidth);
        Find<ProgressBar>(window,"CompactWeekMarker").SetValue(SegmentedVisual.MarkerWidthProperty,
            result.Rows.Single(row=>row.Input.Prefix=="Week").Caption.Length==0 ? LayoutMetrics.NormalMarkerWidth : LayoutMetrics.CaptionMarkerWidth);
        foreach(var row in input.Rows) foreach(string name in new[]{row.Prefix+"Bar","Compact"+row.Prefix+"Bar"}) {
            var bar=Find<ProgressBar>(window,name);bar.SetValue(SegmentedVisual.MarkerScaleProperty,input.Scale);
            bar.SetValue(SegmentedVisual.WaveEnabledProperty,input.WaveEnabled);
            bar.SetValue(SegmentedVisual.WaveOverTextProperty,input.WaveOverText);
            bar.SetValue(SegmentedVisual.WaveTravelProperty,input.WaveTravel);
        }
        var empty=Find<TextBlock>(window,"EmptyUsage");
        empty.FontFamily=new FontFamily(LayoutMetrics.FontFamily);
        empty.Margin=new Thickness(0,input.Expanded ? LayoutMetrics.EmptyExpandedMargin : 0,0,LayoutMetrics.EmptyBottomMargin);
        empty.Visibility=!input.Rows.Any(row=>row.Present) && !result.HiddenText ? Visibility.Visible : Visibility.Collapsed;
    }
}
