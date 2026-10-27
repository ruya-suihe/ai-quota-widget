using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Media.Animation;

public class SegmentedVisual : FrameworkElement
{
    public static readonly DependencyProperty WaveEnabledProperty=DependencyProperty.RegisterAttached("WaveEnabled",typeof(bool),typeof(SegmentedVisual),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WaveOverTextProperty=DependencyProperty.RegisterAttached("WaveOverText",typeof(bool),typeof(SegmentedVisual),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WaveTravelProperty=DependencyProperty.RegisterAttached("WaveTravel",typeof(double),typeof(SegmentedVisual),new FrameworkPropertyMetadata(2.0,FrameworkPropertyMetadataOptions.AffectsRender));
    static readonly Stopwatch waveClock=Stopwatch.StartNew();
    DispatcherTimer waveTimer;
    double transitionTop, transitionBottom, transitionTarget, transitionStarted=-1;
    const double WaveTransitionSeconds=0.5, WaveBottomDelaySeconds=0.1;
    public SegmentedVisual() {
        Loaded+=(s,e)=>UpdateWaveTimer();
        Unloaded+=(s,e)=>{transitionStarted=-1;if(waveTimer!=null) waveTimer.Stop();};
        IsVisibleChanged+=(s,e)=>{UpdateWaveTimer();InvalidateVisual();};
    }
    bool WaveAllowed() {
        double scale=Math.Max(LayoutMetrics.MinimumScale,(double)GetValue(MarkerScaleProperty));
        return (bool)GetValue(WaveEnabledProperty) && Kind.StartsWith("quota")
            && (double)GetValue(WaveTravelProperty)>0
            && scale>=0.6 && ActualWidth*scale>=120 && ActualHeight*scale>=(Kind.Contains("minimal") && Kind.Contains("week") ? 5 : 10);
    }
    bool TransitionRunning() {return transitionStarted>=0 && waveClock.Elapsed.TotalSeconds-transitionStarted<WaveTransitionSeconds;}
    bool CanWave() {return WaveAllowed() && (Value>0 && Value<100 || TransitionRunning());}
    static double EaseQuotaEndpoint(double from,double target,double elapsed,double delay) {
        double t=Math.Max(0,Math.Min(1,(elapsed-delay)/(WaveTransitionSeconds-delay)));
        return from+(target-from)*(1-Math.Pow(1-t,3));
    }
    double WaveEndValue(bool bottom) {
        if(transitionStarted<0 || !WaveAllowed()) return Math.Max(0,Math.Min(100,Value));
        return EaseQuotaEndpoint(bottom ? transitionBottom : transitionTop,transitionTarget,
            waveClock.Elapsed.TotalSeconds-transitionStarted,bottom ? WaveBottomDelaySeconds : 0);
    }
    static void QuotaValueChanged(DependencyObject sender,DependencyPropertyChangedEventArgs args) {
        var visual=(SegmentedVisual)sender;
        // The first realization and disabled/small/hidden wave modes keep their
        // original immediate rendering. Retarget from the visible curve, not an
        // obsolete previous quota, if a new sample arrives during the transition.
        if(!visual.IsLoaded || !visual.IsVisible || !visual.WaveAllowed()) {visual.transitionStarted=-1;return;}
        double elapsed=waveClock.Elapsed.TotalSeconds-visual.transitionStarted;
        double oldValue=Math.Max(0,Math.Min(100,(double)args.OldValue));
        visual.transitionTop=visual.transitionStarted<0 ? oldValue : EaseQuotaEndpoint(visual.transitionTop,visual.transitionTarget,elapsed,0);
        visual.transitionBottom=visual.transitionStarted<0 ? oldValue : EaseQuotaEndpoint(visual.transitionBottom,visual.transitionTarget,elapsed,WaveBottomDelaySeconds);
        visual.transitionTarget=Math.Max(0,Math.Min(100,(double)args.NewValue));
        visual.transitionStarted=waveClock.Elapsed.TotalSeconds;
        visual.UpdateWaveTimer();
    }
    void UpdateWaveTimer() {
        if(!IsLoaded || !IsVisible || !WaveAllowed()) {transitionStarted=-1;if(waveTimer!=null) waveTimer.Stop();return;}
        if(!CanWave()) {if(waveTimer!=null) waveTimer.Stop();return;}
        if(waveTimer==null) {
            waveTimer=new DispatcherTimer(DispatcherPriority.Render) {Interval=TimeSpan.FromMilliseconds(33)};
            waveTimer.Tick+=(s,e)=>{
                UpdateWaveTimer();
                // Repaint the exact endpoint once, even when reaching 0/100%
                // stops the standing-wave timer on this tick.
                InvalidateVisual();
            };
        }
        if(!waveTimer.IsEnabled) waveTimer.Start();
    }
    static double WaveOffset(double y,double height,double end,double amplitude,double phase,Rect[] textAreas,double fade) {
        double weight=1;
        foreach(var area in textAreas) {
            if(end+amplitude<area.Left || end-amplitude>area.Right) continue;
            double distance=y<area.Top ? area.Top-y : y>area.Bottom ? y-area.Bottom : 0;
            double t=Math.Min(1,distance/fade);
            weight=Math.Min(weight,t*t*(3-2*t));
        }
        return amplitude*weight*Math.Sin(2*Math.PI*y/height+phase);
    }
    static Geometry WaveGeometry(double width,double height,double end,double amplitude,double phase,bool fill,Rect[] textAreas,double fade,double topOffset,double bottomOffset) {
        var geometry=new StreamGeometry();
        var rows=Enumerable.Range(0,25).Select(i=>height*i/24).ToList();
        foreach(var area in textAreas) {
            foreach(double y in new[]{area.Top-fade,area.Top,area.Bottom,area.Bottom+fade}) if(y>0 && y<height) rows.Add(y);
        }
        using(var path=geometry.Open()) {
            if(fill) {path.BeginFigure(new Point(0,0),true,true);}
            bool first=true;
            foreach(double y in rows.Distinct().OrderBy(row=>row)) {
                double mix=y/height;mix=mix*mix*(3-2*mix);
                double rowEnd=Math.Max(0,Math.Min(width,end+topOffset+(bottomOffset-topOffset)*mix));
                double rowAmplitude=Math.Min(amplitude,Math.Min(rowEnd,width-rowEnd)*0.5);
                double x=rowEnd+WaveOffset(y,height,rowEnd,rowAmplitude,phase,textAreas,fade);
                if(first && !fill) path.BeginFigure(new Point(x,y),false,false);
                else path.LineTo(new Point(x,y),true,false);
                first=false;
            }
            if(fill) path.LineTo(new Point(0,height),true,false);
        }
        geometry.Freeze();return geometry;
    }
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(SegmentedVisual), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender,QuotaValueChanged));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(SegmentedVisual), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register("Kind", typeof(string), typeof(SegmentedVisual), new FrameworkPropertyMetadata("quota", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register("Caption", typeof(string), typeof(SegmentedVisual), new FrameworkPropertyMetadata("—", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.RegisterAttached("Label",typeof(string),typeof(SegmentedVisual),new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.AffectsRender));
    public string Label {get{return (string)GetValue(LabelProperty);}set{SetValue(LabelProperty,value);}}
    public static readonly DependencyProperty DayMarkerProperty = DependencyProperty.Register("DayMarker", typeof(double), typeof(SegmentedVisual), new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double DayMarker {get {return (double)GetValue(DayMarkerProperty);} set {SetValue(DayMarkerProperty,value);} }
    public static readonly DependencyProperty MarkerWidthProperty=DependencyProperty.RegisterAttached("MarkerWidth",typeof(double),typeof(SegmentedVisual),new FrameworkPropertyMetadata(2.0,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MarkerScaleProperty=DependencyProperty.RegisterAttached("MarkerScale",typeof(double),typeof(SegmentedVisual),new FrameworkPropertyMetadata(1.0,FrameworkPropertyMetadataOptions.AffectsRender));
    public string Caption { get { return (string)GetValue(CaptionProperty); } set { SetValue(CaptionProperty, value); } }
    public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
    public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
    public string Kind { get { return (string)GetValue(KindProperty); } set { SetValue(KindProperty, value); } }
    static Geometry Outline(double width, double height, double slope, double inset) {
        double radius = Math.Min(3, Math.Max(0,(height-2*inset)/2));
        var geometry = new RectangleGeometry(new Rect(inset,inset,Math.Max(0,width-2*inset),Math.Max(0,height-2*inset)),radius,radius);
        geometry.Freeze(); return geometry;
    }
    protected override void OnRender(DrawingContext context) {
        if(Kind=="marker-gap") return;
        if(Kind=="week-marker-overlay") return;
        bool time = Kind.StartsWith("time"), labels = Kind.EndsWith("center");
        bool fullTicks = time || Kind.Contains("full");
        bool detail = Kind.Contains("detail");
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        var empty = Theme.Brush("track");
        double[] boundaries = time && Kind.Contains("week") ? new double[] {0,100.0/7,200.0/7,300.0/7,400.0/7,500.0/7,600.0/7,100} : new double[] {0,20,40,60,80,100};
        bool wave=CanWave();UpdateWaveTimer();
        double topEnd=width*(wave ? WaveEndValue(false) : Math.Max(0,Math.Min(100,Value)))/100;
        double bottomEnd=width*(wave ? WaveEndValue(true) : Math.Max(0,Math.Min(100,Value)))/100;
        double end=(topEnd+bottomEnd)/2;
        double scale=Math.Max(LayoutMetrics.MinimumScale,(double)GetValue(MarkerScaleProperty));
        double pixelScale=scale*VisualTreeHelper.GetDpi(this).DpiScaleX;
        // The preference is total physical-pixel travel, shared equally between both sides.
        double amplitude=wave ? Math.Min((double)GetValue(WaveTravelProperty)/2/pixelScale,Math.Min(end,width-end)*0.5) : 0;
        double phase=2*Math.PI*(waveClock.Elapsed.TotalSeconds%3)/3;
        FormattedText caption = labels && !string.IsNullOrEmpty(Caption) ? new FormattedText(Caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(LayoutMetrics.FontFamily), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), Kind.Contains("narrow") ? LayoutMetrics.NarrowQuotaFontSize : LayoutMetrics.QuotaFontSize, Brushes.WhiteSmoke, VisualTreeHelper.GetDpi(this).PixelsPerDip) : null;
        double textLeft=caption==null ? -1 : Kind.Contains("right") ? width-caption.Width-LayoutMetrics.BarTextInset : (width-caption.Width)/2;
        Point origin=caption==null ? new Point() : new Point(textLeft,(height-caption.Height)/2);
        FormattedText label=caption!=null && !string.IsNullOrEmpty(Label) ? new FormattedText(Label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
            new Typeface(new FontFamily(LayoutMetrics.FontFamily),FontStyles.Normal,FontWeights.Bold,FontStretches.Normal),Kind.Contains("narrow") ? LayoutMetrics.NarrowQuotaFontSize : LayoutMetrics.QuotaFontSize,Theme.Brush("label"),VisualTreeHelper.GetDpi(this).PixelsPerDip) : null;
        Point labelOrigin=label==null ? new Point() : new Point(LayoutMetrics.BarTextInset,origin.Y+caption.Baseline-label.Baseline);
        var textAreas=new List<Rect>();
        if(caption!=null) textAreas.Add(new Rect(origin,new Size(caption.Width,caption.Height)));
        if(label!=null) textAreas.Add(new Rect(labelOrigin,new Size(label.Width,label.Height)));
        // Protect text plus a small margin; taper back into the wave above and below it.
        var protectedAreas=(bool)GetValue(WaveOverTextProperty) ? new Rect[0] : textAreas.Select(area=>{area.Inflate(2/pixelScale,1/pixelScale);return area;}).ToArray();
        double slope = width * 0.30;
        var shape = Outline(width,height,slope,0);
        context.PushOpacity(ScifiTheme.Enabled ? .25 : 1);context.DrawGeometry(empty, null, shape);context.Pop();
        context.PushClip(shape);
        if(end>0) {
            context.PushOpacity(ScifiTheme.Enabled ? .38 : 1);
            if(wave) context.DrawGeometry(Fill,null,WaveGeometry(width,height,end,amplitude,phase,true,protectedAreas,2/pixelScale,topEnd-end,bottomEnd-end));
            else context.DrawRectangle(Fill,null,new Rect(0,0,end,height));
            context.Pop();
        }
        var bigMark = new Pen(Theme.Brush("ticks"),2);
        for (int i = 1; !Kind.Contains("minimal") && i < boundaries.Length - 1; i++) {
            double x = width * boundaries[i] / 100;
            context.DrawLine(bigMark,new Point(x,fullTicks ? 0 : detail ? height*0.5 : height*0.75),new Point(x,height));
        }
        if(!time && !Kind.Contains("minimal")) {
            var smallMark=new Pen(Theme.Brush("ticks"),2);
            for(int percent=10;percent<100;percent+=20) {
                double x=width*percent/100;
                context.DrawLine(smallMark,new Point(x,detail ? height*0.75 : height*0.875),new Point(x,height));
            }
        }
        // Quota-only, one physical pixel; preserve the time bars and the rounded frame.
        if(!time && end>0) {
            var solid=Fill as SolidColorBrush;
            if(solid!=null) {
                var front=new SolidColorBrush(QuotaFrontColor(solid.Color,((SolidColorBrush)empty).Color));
                double thickness=1/Math.Max(LayoutMetrics.MinimumScale,(double)GetValue(MarkerScaleProperty));
                double frontX=Math.Max(thickness/2,Math.Min(width-thickness/2,end-thickness/2));
                if(wave) context.DrawGeometry(null,new Pen(front,thickness),WaveGeometry(width,height,frontX,amplitude,phase,false,protectedAreas,2/pixelScale,topEnd-end,bottomEnd-end));
                else context.DrawLine(new Pen(front,thickness),new Point(frontX,0),new Point(frontX,height));
            }
        }
        context.Pop();
        var borderBrush = Theme.Brush("barBorder");
        context.DrawGeometry(null, new Pen(borderBrush,1.2) { LineJoin = PenLineJoin.Bevel }, Outline(width,height,slope,0.6));
        if(!time && (Kind.Contains("week") || Kind.Contains("minimal"))) DrawQuotaMarker(context,width,height);
        if (caption != null) {
            // Outline only the glyphs, keeping the bar visible behind and between characters.
            caption.SetForegroundBrush(Theme.Brush("caption"));
            context.DrawText(caption,origin);
            if(label!=null) context.DrawText(label,labelOrigin);
        }
    }
    static double FrontLuminance(Color color) {
        Func<byte,double> linear=channel=>{double value=channel/255.0;return value<=0.04045 ? value/12.92 : Math.Pow((value+0.055)/1.055,2.4);};
        return 0.2126*linear(color.R)+0.7152*linear(color.G)+0.0722*linear(color.B);
    }
    static double FrontContrast(Color first,Color second) {
        double a=FrontLuminance(first),b=FrontLuminance(second);return (Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05);
    }
    static Color QuotaFrontColor(Color fill,Color track) {
        // Retain the fill's hue. Use the smallest tint/shade that contrasts with both sides.
        Color best=fill;double bestScore=0;
        for(int step=1;step<=20;step++) {
            double amount=step/20.0;
            foreach(int target in new[]{0,255}) {
                var candidate=Color.FromArgb(fill.A,
                    (byte)Math.Round(fill.R+(target-fill.R)*amount),
                    (byte)Math.Round(fill.G+(target-fill.G)*amount),
                    (byte)Math.Round(fill.B+(target-fill.B)*amount));
                double score=Math.Min(FrontContrast(candidate,fill),FrontContrast(candidate,track));
                double fillContrast=FrontContrast(candidate,fill),trackContrast=FrontContrast(candidate,track);
                if(fillContrast>=3 && trackContrast>bestScore) {best=candidate;bestScore=trackContrast;}
                if(score>=3) return candidate;
            }
        }
        return best;
    }
    void DrawQuotaMarker(DrawingContext context,double width,double height) {
        if(DayMarker<0 || DayMarker>1 || width<=0 || height<=0) return;
        double scale=Math.Max(LayoutMetrics.MinimumScale,(double)GetValue(MarkerScaleProperty));
        double markerFactor=Kind.Contains("minimal") ? 0.8 : 1;
        double half=Math.Min(Math.Max(5,3.5/scale)*0.75*markerFactor,width/4), x=Math.Max(half+0.75/scale,Math.Min(width-half-0.75/scale,width*DayMarker));
        // Value is remaining quota; falling below the daily remaining reference exceeds the suggested usage.
        // Disabled by user preference; retain the reminder drawing for possible future use.
        bool reminderEnabled=false;
        bool overSuggestion=reminderEnabled && Value < DayMarker*100;
        var color=Theme.Brush("weekMarker");
        bool outside=Kind.EndsWith("center") && (!string.IsNullOrEmpty(Caption) || !string.IsNullOrEmpty(Label));
        double markerHeight=Math.Max(5,4.5/scale)*0.75*markerFactor;
        // Text stays unobstructed: outside with text, inside without text.
        // Account for half the marker stroke so its visible edge touches the frame without a gap.
        double tip=outside ? -0.5/scale : Math.Min(height-0.5/scale,1.2+0.5/scale+markerHeight);
        double top=outside ? tip-markerHeight : 1.2+0.5/scale;
        // Match the landscape theme: minimal weekly marker sits on the lower inner edge, pointing up.
        if(Kind.Contains("minimal") && Kind.Contains("week")){tip=height-tip;top=height-top;}
        if(!outside) context.PushClip(new RectangleGeometry(new Rect(0,0,width,height)));
        var fillColor=((SolidColorBrush)color).Color;
        bool light=fillColor.R*0.2126+fillColor.G*0.7152+fillColor.B*0.0722>160;
        double target=light ? 0 : 255;
        // A subtle shade of the fill replaces the high-contrast black/white edge.
        var outline=new SolidColorBrush(Color.FromArgb(fillColor.A,
            (byte)Math.Round(fillColor.R+(target-fillColor.R)*0.15),
            (byte)Math.Round(fillColor.G+(target-fillColor.G)*0.15),
            (byte)Math.Round(fillColor.B+(target-fillColor.B)*0.15)));
        {
            var geometry=new StreamGeometry();
            using(var path=geometry.Open()) {path.BeginFigure(new Point(x-half,top),true,true);path.LineTo(new Point(x+half,top),true,false);path.LineTo(new Point(x,tip),true,false);}
            context.DrawGeometry(color,new Pen(outline,1/scale) {LineJoin=PenLineJoin.Round},geometry);
            if(overSuggestion) {
                // Highlight only the sloping sides; keep the fill and horizontal edge unchanged.
                var reminder=new Pen(Theme.Brush("alert"),1.5/scale);
                context.DrawLine(reminder,new Point(x-half,top),new Point(x,tip));
                context.DrawLine(reminder,new Point(x+half,top),new Point(x,tip));
            }
        }
        if(!outside) context.Pop();
    }
}

partial class QuotaWidget
{
    class Limit { public double Remaining; public long Reset; public double Minutes; public string Label=""; }
    class Snapshot {
        public DateTimeOffset Time, HourTime, WeekTime;
        public Limit Hour, Week;
        public bool Live;
        public Snapshot LiveBaseline;
        public string Provider = "codex", Source = "", LoginVersion="";
    }
    static Window window;
    static Snapshot snapshot;
    static bool reading;
    static bool activeReading;
    static string diagnostic = "";
    static bool queryFailed;
    static string queryError = "";
    static string localReadWarning = "";
    static bool english, checking, expanded = true, dragging;
    static bool locked, appearanceOpen;
    static DispatcherTimer collapseTimer;
    static DispatcherTimer zoomPaintTimer;
    static DateTime zoomPaintUntil;
    static bool settingsOpen;
    static bool pinIcons = true, noExpand, minimalMode=true;
    static bool toolbarTop;
    static readonly string[] DefaultToolbarOrder = { "SettingsButton", "LockButton", "RefreshButton", "CloseButton" };
    static string[] toolbarOrder = (string[])DefaultToolbarOrder.Clone();
    static Window settingsWindow;
    static int settingsVisits;
    static void ConfigureSegments() {
        foreach (string name in new[] { "HourBar", "WeekBar", "CompactHourBar", "CompactWeekBar", "HourTimeBar", "WeekTimeBar", "CompactHourTimeBar", "CompactWeekTimeBar", "WeekGap", "CompactWeekGap", "WeekMarker", "CompactWeekMarker" }) {
            var bar = Control<ProgressBar>(name);
            bar.Uid = "—";
            bar.Tag = name.Contains("Time") ? "time" : "quota";
            if (name.Contains("Week")) bar.Tag += "-week";
            if (name.StartsWith("Compact") && !name.Contains("Time")) bar.Tag += "-center";
            else if (!name.Contains("Time")) bar.Tag += "-detail";
            if(name.EndsWith("Gap")) bar.Tag="marker-gap";
            if(name.EndsWith("Marker")) bar.Tag="week-marker-overlay";
            var visual = new FrameworkElementFactory(typeof(SegmentedVisual));
            visual.SetBinding(SegmentedVisual.ValueProperty, new Binding("Value") { RelativeSource = RelativeSource.TemplatedParent });
            visual.SetBinding(SegmentedVisual.FillProperty, new Binding("Foreground") { RelativeSource = RelativeSource.TemplatedParent });
            visual.SetBinding(SegmentedVisual.KindProperty, new Binding("Tag") { RelativeSource = RelativeSource.TemplatedParent });
            visual.SetBinding(SegmentedVisual.CaptionProperty, new Binding("Uid") { RelativeSource = RelativeSource.TemplatedParent });
            visual.SetBinding(SegmentedVisual.LabelProperty,new Binding {Path=new PropertyPath("(0)",SegmentedVisual.LabelProperty),RelativeSource=RelativeSource.TemplatedParent});
            visual.SetBinding(SegmentedVisual.DayMarkerProperty, new Binding("DataContext") { RelativeSource = RelativeSource.TemplatedParent });
            visual.SetBinding(SegmentedVisual.MarkerWidthProperty,new Binding {Path=new PropertyPath("(0)",SegmentedVisual.MarkerWidthProperty),RelativeSource=RelativeSource.TemplatedParent});
            visual.SetBinding(SegmentedVisual.MarkerScaleProperty,new Binding {Path=new PropertyPath("(0)",SegmentedVisual.MarkerScaleProperty),RelativeSource=RelativeSource.TemplatedParent});
            visual.SetBinding(SegmentedVisual.WaveEnabledProperty,new Binding {Path=new PropertyPath("(0)",SegmentedVisual.WaveEnabledProperty),RelativeSource=RelativeSource.TemplatedParent});
            visual.SetBinding(SegmentedVisual.WaveOverTextProperty,new Binding {Path=new PropertyPath("(0)",SegmentedVisual.WaveOverTextProperty),RelativeSource=RelativeSource.TemplatedParent});
            visual.SetBinding(SegmentedVisual.WaveTravelProperty,new Binding {Path=new PropertyPath("(0)",SegmentedVisual.WaveTravelProperty),RelativeSource=RelativeSource.TemplatedParent});
            bar.DataContext=-1.0;
            bar.Template = new ControlTemplate(typeof(ProgressBar)) { VisualTree = visual };
        }
    }
    static double zoom = 1;
    static double textThreshold = LayoutMetrics.DefaultTextThreshold, baseWidth = LayoutMetrics.DefaultWidth;
    static DateTimeOffset lastActiveCompleted = DateTimeOffset.MinValue, localFailureSince = DateTimeOffset.MinValue;
    static double widgetOpacity=1;
    static bool waveEnabled;
    static bool waveOverText;
    static double waveTravel=2;
    static double hourBarHeight=LayoutMetrics.DefaultHourBarHeight,weekBarHeight=LayoutMetrics.DefaultWeekBarHeight;
    static int backgroundRatio=1;
    class DisplayOptions { public double TextThreshold = LayoutMetrics.DefaultTextThreshold, Width = LayoutMetrics.DefaultWidth, Opacity = 1, WaveTravel = 2;
        public double HourBarHeight=LayoutMetrics.DefaultHourBarHeight,WeekBarHeight=LayoutMetrics.DefaultWeekBarHeight;
        public int BackgroundRatio=1;
        public bool WaveEnabled, WaveOverText; }
    static string DisplayOptionsFile() { return Path.Combine(Path.GetDirectoryName(PreferenceFile()),"display.json"); }
    static void ApplyDisplayOptions(DisplayOptions options) {
        if (options == null) return;
        waveEnabled=options.WaveEnabled;
        // Retired 4:3 (stored value 3) migrates to 16:9 without rewriting preferences on load.
        backgroundRatio=options.BackgroundRatio==3 ? 2 : options.BackgroundRatio>=0 && options.BackgroundRatio<=2 ? options.BackgroundRatio : 1;
        waveOverText=options.WaveOverText;
        hourBarHeight=LayoutMetrics.NormalizeBarHeight(options.HourBarHeight,LayoutMetrics.DefaultHourBarHeight);
        weekBarHeight=LayoutMetrics.NormalizeBarHeight(options.WeekBarHeight,LayoutMetrics.DefaultWeekBarHeight);
        if(!double.IsNaN(options.WaveTravel) && !double.IsInfinity(options.WaveTravel)) waveTravel=Math.Max(0,Math.Min(5,options.WaveTravel));
        if (!double.IsNaN(options.Opacity) && !double.IsInfinity(options.Opacity)) widgetOpacity=Math.Max(0.3,Math.Min(1,options.Opacity));
        if (!double.IsNaN(options.TextThreshold) && !double.IsInfinity(options.TextThreshold)) textThreshold=Math.Max(LayoutMetrics.MinimumScale,Math.Min(LayoutMetrics.MaximumScale,options.TextThreshold));
        if (!double.IsNaN(options.Width) && !double.IsInfinity(options.Width)) baseWidth=Math.Max(LayoutMetrics.MinimumWidth,Math.Min(LayoutMetrics.MaximumWidth,options.Width));
    }
    static void LoadDisplayOptions() {
        try { if (File.Exists(DisplayOptionsFile())) ApplyDisplayOptions(new JavaScriptSerializer().Deserialize<DisplayOptions>(File.ReadAllText(DisplayOptionsFile()))); }
        catch (IOException) {} catch (UnauthorizedAccessException) {} catch (ArgumentException) {} catch (InvalidOperationException) {}
    }
    static void SaveDisplayOptions() {
        if(checking) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(DisplayOptionsFile())); File.WriteAllText(DisplayOptionsFile(),new JavaScriptSerializer().Serialize(new DisplayOptions {TextThreshold=textThreshold,Width=baseWidth,Opacity=widgetOpacity,WaveEnabled=waveEnabled,WaveOverText=waveOverText,WaveTravel=waveTravel,HourBarHeight=hourBarHeight,WeekBarHeight=weekBarHeight,BackgroundRatio=backgroundRatio})); }
        catch(IOException) {} catch(UnauthorizedAccessException) {}
    }
    static double TextWidth(string text, double size, FontWeight weight) {
        return new FormattedText(text ?? "",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
            new Typeface(new FontFamily(LayoutMetrics.FontFamily),FontStyles.Normal,weight,FontStretches.Normal),size,Brushes.White,VisualTreeHelper.GetDpi(window).PixelsPerDip).Width;
    }
    static bool DetailsFit() {
        foreach(string prefix in new[]{"Hour","Week"}) {
            var limit=snapshot==null ? null : prefix=="Hour" ? snapshot.Hour : snapshot.Week;
            if(limit==null) continue;
            double titleWidth=toolbarTop ? Math.Max(80,TextWidth(Control<TextBlock>(prefix+"Label").Text,13,FontWeights.Bold)+2) : 140;
            double needed=titleWidth+12+48+12+TextWidth(Control<TextBlock>(prefix+"Reset").Text,13,FontWeights.Bold)+6;
            if(toolbarTop) needed+=TextWidth("Updated 00:00:00",10,FontWeights.Normal)+6;
            if(needed>baseWidth-18) return false;
        }
        return true;
    }
    static void LoadZoom() {
        try {
            string path = Path.Combine(Path.GetDirectoryName(PreferenceFile()), "scale.txt");
            double stored;
            if (File.Exists(path) && double.TryParse(File.ReadAllText(path), NumberStyles.Float, CultureInfo.InvariantCulture, out stored)
                && !double.IsNaN(stored) && !double.IsInfinity(stored)) zoom = Math.Max(LayoutMetrics.MinimumScale, Math.Min(LayoutMetrics.MaximumScale, stored));
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    static void SaveZoom() {
        if (checking) return;
        try {
            string folder = Path.GetDirectoryName(PreferenceFile()); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "scale.txt"), zoom.ToString("0.00", CultureInfo.InvariantCulture));
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    static void ResizeWidget() {
        LayoutInput layoutInput;var layoutResult=ApplyScaleLayout(out layoutInput);
        var root = Control<Border>("Root");
        double right = (double.IsNaN(window.Left) ? 0 : window.Left) + window.Width;
        double bottom = (double.IsNaN(window.Top) ? 0 : window.Top) + window.Height;
        double top = double.IsNaN(window.Top) ? 0 : window.Top;
        var scale=root.LayoutTransform as ScaleTransform;
        if(scale==null || scale.ScaleX!=zoom || scale.ScaleY!=zoom)root.LayoutTransform = new ScaleTransform(zoom, zoom);
        window.Width = baseWidth * zoom;
        // Measure manual/reference/fitted heights without arranging intermediate sizes.
        // UpdateLayout here exposes a temporary manual height on every hover/refresh.
        foreach (var child in Children(root)) { var element = child as UIElement; if (element != null) element.InvalidateMeasure(); }
        root.InvalidateMeasure();
        root.Measure(new Size(baseWidth * zoom, double.PositiveInfinity));
        ScifiTheme.LockArtworkAspect=backgroundRatio!=0;
        double aspect=backgroundRatio==0 ? 0 : backgroundRatio==2 ? 16.0/9 : ScifiTheme.ArtworkAspect(SelectedBuiltin());
        WidgetLayout.FitBackgroundRatio(window,layoutInput,layoutResult,aspect);
        window.Width = baseWidth * zoom;
        window.Height = Math.Ceiling(root.DesiredSize.Height);
        window.Left = right - window.Width;
        window.Top = toolbarTop ? top : bottom - window.Height;
        window.UpdateLayout();
        ScifiTheme.LayoutCommitted();
    }
    static void ChangeZoom(int wheelDelta) {
        if (wheelDelta == 0) return;
        SetZoom(zoom + wheelDelta / 120.0 * 0.05);
    }
    static void SetZoom(double value) {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        zoom = Math.Max(LayoutMetrics.MinimumScale, Math.Min(LayoutMetrics.MaximumScale, Math.Round(value, 2)));
        Render(); SaveZoom();
    }
    static LayoutInput CaptureLayoutInput() {
        var rows=new List<LayoutRowInput>();
        foreach(string prefix in new[]{"Hour","Week"}) {
            var limit=snapshot==null ? null : prefix=="Hour" ? snapshot.Hour : snapshot.Week;
            string label=QuotaRowLabel(prefix,limit,true);
            rows.Add(new LayoutRowInput(prefix,limit!=null,Control<TextBlock>(prefix+"Label").Text,
                Control<TextBlock>(prefix+"Value").Text,Control<TextBlock>(prefix+"Reset").Text,label,
                limit==null ? "—" : limit.Remaining.ToString("0.#")+"% · "+CompactReset(limit,prefix=="Week"),limit==null ? "—" : limit.Remaining.ToString("0.#")+"%"));
        }
        return new LayoutInput(baseWidth,zoom,textThreshold,widgetOpacity,Control<Border>("Root").BorderThickness.Left,
            expanded,noExpand,pinIcons,toolbarTop,Control<TextBlock>("TitleLabel").Text,toolbarOrder,rows.ToArray(),
            waveEnabled,waveOverText,waveTravel,minimalMode,hourBarHeight,weekBarHeight);
    }
    static LayoutResult ApplyScaleLayout(out LayoutInput input) {
        input=CaptureLayoutInput();
        var result=WidgetLayout.Calculate(input,(value,size)=>TextWidth(value,size,FontWeights.Bold));
        WidgetLayout.Apply(window,input,result);
        return result;
    }
    static int SelectedBuiltin(){string id=ResolveSelectedColorCard();var card=AvailableColorCards().FirstOrDefault(item=>item.Id==id);return card==null?-1:card.Builtin;}
    static string Text(string chinese, string en) { return english ? en : chinese; }
    static string ToolbarPreferenceFile() { return Path.Combine(Path.GetDirectoryName(PreferenceFile()), "toolbar.txt"); }
    static void LoadToolbarPreference() {
        try { pinIcons = !File.Exists(ToolbarPreferenceFile()) || File.ReadAllText(ToolbarPreferenceFile()).Trim() != "hide"; }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        try {
            string path = ToolbarLayoutFile();
            if (File.Exists(path)) ApplyToolbarLayout(new JavaScriptSerializer().Deserialize<ToolbarLayout>(File.ReadAllText(path)));
        } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
    }
    class ToolbarLayout { public bool Top, NoExpand, Minimal; public string[] Order; }
    static string ToolbarLayoutFile() { return Path.Combine(Path.GetDirectoryName(PreferenceFile()),"toolbar-layout.json"); }
    static bool ApplyToolbarLayout(ToolbarLayout saved) {
        if (saved == null || saved.Order == null || saved.Order.Length != 4 || saved.Order.Distinct().Count() != 4
            || saved.Order.Any(name => !DefaultToolbarOrder.Contains(name))) return false;
        noExpand = saved.NoExpand;minimalMode=saved.Minimal; toolbarTop = saved.Top; toolbarOrder = (string[])saved.Order.Clone(); return true;
    }
    static void SaveToolbarLayout() {
        if (checking) return;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(ToolbarLayoutFile()));
            File.WriteAllText(ToolbarLayoutFile(),new JavaScriptSerializer().Serialize(new ToolbarLayout {Top=toolbarTop,Order=toolbarOrder,NoExpand=noExpand,Minimal=minimalMode}));
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    static void SwapToolbarPosition(int slot, string button) {
        int previous = Array.IndexOf(toolbarOrder,button);
        if (previous < 0 || slot < 0 || slot >= 4) return;
        string displaced = toolbarOrder[slot]; toolbarOrder[slot] = button; toolbarOrder[previous] = displaced;
        Render(); SaveToolbarLayout();
    }
    static void SetPinnedIcons(bool value) {
        pinIcons = value; Render();
        if (checking) return;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(ToolbarPreferenceFile()));
            File.WriteAllText(ToolbarPreferenceFile(),pinIcons ? "pin" : "hide");
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    static void SelectLanguage(bool value) { english = value; ApplyLanguage(); SaveLanguage(); }
    static string GuideFile() { return Path.Combine(Path.GetDirectoryName(PreferenceFile()), "settings-guide-v2.txt"); }
    static void LoadSettingsGuide() {
        try {int value;if(File.Exists(GuideFile()) && int.TryParse(File.ReadAllText(GuideFile()),out value)) settingsVisits=Math.Max(0,Math.Min(2,value));}
        catch(IOException) {} catch(UnauthorizedAccessException) {}
    }
    static bool BeginSettingsVisit() {
        bool highlight=settingsVisits<2;
        settingsVisits=Math.Min(2,settingsVisits+1);
        UpdateSettingsHint();
        if(checking) return highlight;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(GuideFile()));
            File.WriteAllText(GuideFile(),settingsVisits.ToString(CultureInfo.InvariantCulture));
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return highlight;
    }
    static bool settingsPulseActive;
    static void UpdateSettingsHint() {
        var button=Control<Button>("SettingsButton");
        var glyph=(Viewbox)button.Content;
        if(settingsVisits==0 && !settingsPulseActive) {glyph.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,0.4,TimeSpan.FromSeconds(1.5)) {AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});settingsPulseActive=true;}
        else if(settingsVisits>0 && settingsPulseActive) {glyph.BeginAnimation(UIElement.OpacityProperty,null);glyph.Opacity=1;settingsPulseActive=false;}
        if(settingsVisits==0) {
            var red=(Color)ColorConverter.ConvertFromString("#E5484D");
            button.Resources["ToolbarNormal"]=new SolidColorBrush(red);
            button.Resources["ToolbarHover"]=LightenIcon(red,0.25);
            button.Resources["ToolbarPressed"]=LightenIcon(red,0.45);
        } else foreach(string key in new[]{"ToolbarNormal","ToolbarHover","ToolbarPressed"}) button.Resources.Remove(key);
        button.ToolTip=settingsVisits==0 ? Text("打开设置了解常用功能","Open settings to discover useful controls") : Text("系统设置 · 语言、配色、缩放（也可滚轮缩放）", "Settings · language, colors, scale (or use the mouse wheel)");
    }
    static Action AddNumberSetting(StackPanel panel, TextBlock label, double minimum, double maximum, double initial, string suffix, Action<double> apply,double step=1) {
        string format=step<1 ? "0.#" : "0";
        label.Margin=new Thickness(0,0,0,6); panel.Children.Add(label);
        var row=new Grid {Margin=new Thickness(0,0,0,12)};
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(66)}); row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(24)});
        var slider=new Slider {Minimum=minimum,Maximum=maximum,Value=initial,TickFrequency=step,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
        var input=new TextBox {Text=initial.ToString(format,CultureInfo.InvariantCulture),Padding=new Thickness(7,5,7,5),Background=SettingsBrush("Input"),Foreground=SettingsBrush("Text"),BorderThickness=new Thickness(0)};
        var unit=new TextBlock {Text=suffix,VerticalAlignment=VerticalAlignment.Center};
        row.Children.Add(slider); Grid.SetColumn(input,1);row.Children.Add(input);Grid.SetColumn(unit,2);row.Children.Add(unit);panel.Children.Add(row);
        label.Tag=row;
        slider.ValueChanged+=(s,e)=>{apply(slider.Value);input.Text=slider.Value.ToString(format,CultureInfo.InvariantCulture);input.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);};
        Action commit=()=>{double value; if(!double.TryParse(input.Text.Trim().TrimEnd('%'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.IsNaN(value)||double.IsInfinity(value)){input.BorderBrush=Brushes.OrangeRed;return;}
            value=Math.Round(Math.Max(minimum,Math.Min(maximum,value))/step)*step;slider.Value=value;apply(value);input.Text=value.ToString(format,CultureInfo.InvariantCulture);input.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);};
        input.LostKeyboardFocus+=(s,e)=>commit();input.KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter){commit();e.Handled=true;}};
        return commit;
    }
    static Brush PaletteBrush(Dictionary<string,string> palette,string key) {return new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[key]));}
    static TextBox BarHeightInput(double initial,Action<double> apply,out Action commit) {
        var input=new TextBox {Text=initial.ToString("0.#",CultureInfo.InvariantCulture),Padding=new Thickness(5,2,5,2),Background=SettingsBrush("Input"),Foreground=SettingsBrush("Text"),BorderThickness=new Thickness(0)};
        Action save=()=>{double value;if(!double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.IsNaN(value)||double.IsInfinity(value)){input.BorderBrush=Brushes.OrangeRed;return;}
            value=Math.Round(LayoutMetrics.NormalizeBarHeight(value,initial)*2)/2;input.Text=value.ToString("0.#",CultureInfo.InvariantCulture);apply(value);input.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);};
        input.LostKeyboardFocus+=(s,e)=>save();input.KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter){save();e.Handled=true;}
            else if(e.Key==System.Windows.Input.Key.Up || e.Key==System.Windows.Input.Key.Down){double value;if(!double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value))value=initial;input.Text=(value+(e.Key==System.Windows.Input.Key.Up?.5:-.5)).ToString("0.#",CultureInfo.InvariantCulture);save();e.Handled=true;}};
        commit=save;return input;
    }
    class ColorCard {public string Id,Name;public int Builtin=-1;public Dictionary<string,string> Colors;}
    static List<ColorCard> customColorCards=new List<ColorCard>();
    static string selectedColorCardId="";
    static bool SameColorSettings(Dictionary<string,string> first,Dictionary<string,string> second) {
        return Theme.Keys.All(key=>first.ContainsKey(key) && second.ContainsKey(key) &&
            (Color)ColorConverter.ConvertFromString(first[key])==(Color)ColorConverter.ConvertFromString(second[key]));
    }
    static ColorCard MatchingColorCard(Dictionary<string,string> colors) {return AvailableColorCards().FirstOrDefault(card=>SameColorSettings(card.Colors,colors));}
    static string ResolveSelectedColorCard() {
        var cards=AvailableColorCards();
        var match=cards.FirstOrDefault(card=>card.Id==selectedColorCardId && SameColorSettings(card.Colors,Theme.Colors));
        if(match==null) match=cards.FirstOrDefault(card=>SameColorSettings(card.Colors,Theme.Colors));
        return selectedColorCardId=match==null ? "" : match.Id;
    }
    static string ColorCardsFile() {return Path.Combine(Path.GetDirectoryName(PreferenceFile()),"color-cards.json");}
    static List<ColorCard> AvailableColorCards() {
        var cards=new List<ColorCard> {new ColorCard {Id="builtin-default",Name="默认配色",Colors=Theme.Defaults()}};
        for(int i=0;i<Theme.PresetCount;i++) cards.Add(new ColorCard {Id="builtin-"+i,Builtin=i,Colors=Theme.Preset(i)});
        cards.AddRange(customColorCards);return cards;
    }
    static string ColorCardName(ColorCard card) {return card.Id=="builtin-default" ? Text("默认配色","Default colors") : card.Builtin>=0 ? Theme.PresetName(card.Builtin,english) : card.Name;}
    static bool ColorCardNameExists(string name) {
        if(string.Equals(name,"默认配色",StringComparison.OrdinalIgnoreCase) || string.Equals(name,"Default colors",StringComparison.OrdinalIgnoreCase)) return true;
        return AvailableColorCards().Any(card=>card.Builtin>=0
            ? string.Equals(Theme.PresetName(card.Builtin,false),name,StringComparison.OrdinalIgnoreCase) || string.Equals(Theme.PresetName(card.Builtin,true),name,StringComparison.OrdinalIgnoreCase)
            : string.Equals(card.Name,name,StringComparison.OrdinalIgnoreCase));
    }
    static void LoadColorCards() {
        if(checking) return;
        try {
            if(!File.Exists(ColorCardsFile())) {customColorCards.Clear();return;}
            var saved=new JavaScriptSerializer().Deserialize<List<ColorCard>>(File.ReadAllText(ColorCardsFile()));
            var valid=new List<ColorCard>();
            foreach(var card in saved ?? new List<ColorCard>()) {
                if(card==null || string.IsNullOrWhiteSpace(card.Name) || card.Colors==null || card.Name.Length>48) continue;
                if(card.Colors.Any(pair=>Theme.Keys.Contains(pair.Key) && !Theme.Valid(pair.Value))) continue;
                var colors=Theme.Defaults();foreach(string key in Theme.Keys) if(card.Colors.ContainsKey(key)) colors[key]=card.Colors[key];
                if(string.IsNullOrWhiteSpace(card.Id) || card.Id.StartsWith("builtin-",StringComparison.Ordinal) || valid.Any(item=>item.Id==card.Id)) card.Id=Guid.NewGuid().ToString("N");
                card.Builtin=-1;card.Colors=colors;valid.Add(card);
            }
            customColorCards=valid;
        } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(ArgumentException) {} catch(InvalidOperationException) {}
    }
    static void AddColorCard(string name,Dictionary<string,string> colors) {
        name=(name ?? "").Trim();
        if(name.Length==0 || name.Length>48) throw new ArgumentException(Text("名称需要 1–48 个字符。","Use a name between 1 and 48 characters."));
        if(ColorCardNameExists(name)) throw new ArgumentException(Text("名称已存在，请使用其他名称。","That name already exists. Choose another."));
        if(Theme.Keys.Any(key=>!colors.ContainsKey(key) || !Theme.Valid(colors[key]))) throw new ArgumentException(Text("请先修正无效颜色。","Correct invalid colors first."));
        var duplicate=MatchingColorCard(colors);
        if(duplicate!=null) throw new ArgumentException(Text("该配色已存在：","These colors already exist: ")+ColorCardName(duplicate));
        var next=new List<ColorCard>(customColorCards);next.Add(new ColorCard {Id=Guid.NewGuid().ToString("N"),Name=name,Colors=new Dictionary<string,string>(colors)});
        SaveColorCardList(next);
        selectedColorCardId=next.Last().Id;
    }
    static void SaveColorCardList(List<ColorCard> next) {
        if(!checking) {
            string path=ColorCardsFile(),temporary=path+".tmp";Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(next));
            if(File.Exists(path)) File.Replace(temporary,path,null);else File.Move(temporary,path);
        }
        customColorCards=next;
    }
    static void DeleteColorCard(ColorCard card) {
        if(card==null || !customColorCards.Any(item=>item.Id==card.Id)) throw new ArgumentException(Text("只能删除自定义色卡。","Only custom color cards can be deleted."));
        SaveColorCardList(customColorCards.Where(item=>item.Id!=card.Id).ToList());
        if(selectedColorCardId==card.Id) selectedColorCardId="";
    }
    static bool ApplyColorCard(ColorCard card) {
        var previous=new Dictionary<string,string>(Theme.Colors);
        string previousCard=selectedColorCardId;
        try {Theme.Colors=new Dictionary<string,string>(card.Colors);selectedColorCardId=card.Id;SaveColors(previous,true);ApplyTheme();return true;}
        catch(Exception ex) {Theme.Colors=previous;selectedColorCardId=previousCard;ApplyTheme();if(checking) throw;MessageBox.Show(settingsWindow ?? window,Text("配色无法保存：","Could not save colors: ")+ex.Message);return false;}
    }
    static void EnableCardDragging(ScrollViewer viewer) {
        Point origin=new Point();double offset=0;bool pressed=false,dragged=false;
        Action snap=()=>viewer.ScrollToVerticalOffset(Math.Round(viewer.VerticalOffset/98)*98);
        viewer.PreviewMouseLeftButtonDown+=(s,e)=>{
            var current=e.OriginalSource as DependencyObject;
            while(current!=null) {if(current is System.Windows.Controls.Primitives.ScrollBar){pressed=false;return;}current=current is Visual ? VisualTreeHelper.GetParent(current) : null;}
            origin=e.GetPosition(viewer);offset=viewer.VerticalOffset;pressed=true;dragged=false;
        };
        viewer.PreviewMouseMove+=(s,e)=>{
            if(!pressed || e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed) return;
            double delta=e.GetPosition(viewer).Y-origin.Y;
            if(!dragged && Math.Abs(delta)>=6) {dragged=true;viewer.CaptureMouse();}
            if(dragged) {viewer.ScrollToVerticalOffset(offset-delta);e.Handled=true;}
        };
        // Only take capture after a drag starts: normal clicks still select cards,
        // while taking capture cancels the button's pending click during a drag.
        viewer.PreviewMouseLeftButtonUp+=(s,e)=>{if(dragged){e.Handled=true;snap();viewer.ReleaseMouseCapture();}pressed=dragged=false;};
        viewer.LostMouseCapture+=(s,e)=>{if(e.OriginalSource==viewer){pressed=dragged=false;}};
        viewer.PreviewMouseWheel+=(s,e)=>{viewer.ScrollToVerticalOffset(viewer.VerticalOffset-(e.Delta>0 ? 196 : -196));e.Handled=true;};
    }
    static bool IsAnimatedColorCard(ColorCard card) {return card!=null && card.Builtin>=0 && Theme.IsAnimatedPreset(card.Builtin);}
    static Grid CreatePresetCards(Action<int> select,out Button[] buttons,Action<ColorCard> delete=null,bool? animated=null) {
        var cards=AvailableColorCards().Where(card=>!animated.HasValue || IsAnimatedColorCard(card)==animated.Value).ToList();var grid=new Grid();
        var template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='Card' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1.5' CornerRadius='5'><ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Stretch'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Card' Property='BorderBrush' Value='#AECFFF'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        for(int i=0;i<2;i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for(int i=0;i<(cards.Count+1)/2;i++) grid.RowDefinitions.Add(new RowDefinition {Height=new GridLength(98)});
        buttons=new Button[cards.Count];
        for(int i=0;i<cards.Count;i++) {
            int index=i;var card=cards[i];var palette=card.Colors;
            var content=new StackPanel();
            content.Children.Add(new TextBlock {Text=ColorCardName(card),Tag="PresetName",Foreground=PaletteBrush(palette,"title"),FontSize=12,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,8),TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis});
            var bar=new Grid {Height=20,Background=PaletteBrush(palette,"track"),ClipToBounds=true};
            bar.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(7,GridUnitType.Star)});bar.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(3,GridUnitType.Star)});
            bar.Children.Add(new Border {Background=PaletteBrush(palette,"quotaGreen")});
            var caption=new TextBlock {Text=Text("72% · 6天","72% · 6d"),Foreground=PaletteBrush(palette,"caption"),FontSize=11,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumnSpan(caption,2);bar.Children.Add(caption);
            var arrow=new System.Windows.Shapes.Path {Tag="PresetWeekArrow",Data=Geometry.Parse("M 0,0 L 8,0 L 4,5 Z"),Fill=PaletteBrush(palette,"weekMarker"),Stroke=PaletteBrush(palette,"caption"),StrokeThickness=0.7,Width=9,Height=6,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,0,14,0)};
            Grid.SetColumnSpan(arrow,2);bar.Children.Add(arrow);
            content.Children.Add(new Border {Child=bar,BorderBrush=PaletteBrush(palette,"barBorder"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3)});
            var swatches=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,7,0,0)};
            foreach(string key in new[]{"quotaYellow","quotaOrange","quotaRed","weekMarker"}) swatches.Children.Add(new Border {Width=22,Height=4,Background=PaletteBrush(palette,key),CornerRadius=new CornerRadius(2),Margin=new Thickness(0,0,4,0)});
            content.Children.Add(swatches);
            var button=new Button {Padding=new Thickness(0),Margin=new Thickness(i%2==0 ? 0 : 4,0,i%2==0 ? 4 : 0,8),HorizontalContentAlignment=HorizontalAlignment.Stretch,Tag=card,
                Template=template,Content=new Border {Background=PaletteBrush(palette,"background"),Padding=new Thickness(10),CornerRadius=new CornerRadius(5),Child=content}};
            button.ToolTip=Text("点击应用并保存","Click to apply and save");button.Click+=(s,e)=>select(index);
            Grid.SetColumn(button,i%2);Grid.SetRow(button,i/2);grid.Children.Add(button);buttons[i]=button;
            if(customColorCards.Any(item=>item.Id==card.Id) && delete!=null) {
                ((TextBlock)content.Children[0]).Margin=new Thickness(0,0,18,8);
                var remove=new Button {Content="×",FontSize=14,Width=20,Height=20,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,
                    Margin=new Thickness(0,3,i%2==0 ? 8 : 4,0),ToolTip=Text("删除色卡：","Delete color card: ")+ColorCardName(card)};
                remove.Click+=(s,e)=>{e.Handled=true;delete(card);};Grid.SetColumn(remove,i%2);Grid.SetRow(remove,i/2);grid.Children.Add(remove);
            }
        }
        return grid;
    }
    static void UpdatePresetCards(Button[] cards) {
        string selectedId=ResolveSelectedColorCard();
        for(int i=0;i<cards.Length;i++) {
            var card=(ColorCard)cards[i].Tag;var palette=card.Colors;bool selected=card.Id==selectedId;
            cards[i].BorderBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(selected ? SettingsBrush("Accent").ToString() : SettingsBrush("Line").ToString()));
            var content=(StackPanel)((Border)cards[i].Content).Child;
            ((TextBlock)content.Children[0]).Text=ColorCardName(card)+(selected ? " ✓" : "");
            ((Grid)((Border)content.Children[1]).Child).Children.OfType<TextBlock>().Single().Text=Text("72% · 6天","72% · 6d");
            cards[i].ToolTip=ColorCardName(card)+"\n"+Text("点击应用并保存","Click to apply and save");
        }
    }
    static void SaveColors(Dictionary<string,string> previous,bool preservePrevious) {
        if(checking) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ThemeFile()));
        if(preservePrevious) File.WriteAllText(ThemeBackupFile(),new JavaScriptSerializer().Serialize(previous));
        SaveActiveColorSettings(json=>File.WriteAllText(ThemeFile(),json));
    }
    static void SaveActiveColorSettings(Action<string> write) {
        string previousCard=selectedColorCardId;
        try {
            var saved=new Dictionary<string,string>(Theme.Colors);saved["_selectedCardId"]=ResolveSelectedColorCard();
            write(new JavaScriptSerializer().Serialize(saved));
        } catch {selectedColorCardId=previousCard;throw;}
    }
    static void OpenSettings() {
        if (settingsOpen) return;
        settingsOpen = true; collapseTimer.Stop(); bool guide=BeginSettingsVisit();
        var dialog = new Window { Width = 410, Height=590, SizeToContent=SizeToContent.Manual, ResizeMode = ResizeMode.NoResize,
            Owner = checking ? null : window, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = true,
            Background = SettingsBrush("Background"), Foreground = SettingsBrush("Text"),
            FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13, ShowInTaskbar = false };
        settingsWindow = dialog;
        dialog.Resources=SettingsStyles();
        if(checking && settingsFocusedCheck) {dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-10000;dialog.ShowActivated=false;dialog.Topmost=false;}
        var panel = new StackPanel { Margin = new Thickness(18) };
        dialog.Content=panel;
        var languageLabel = new TextBlock { Margin = new Thickness(0,0,0,6) }; panel.Children.Add(languageLabel);
        var language = new ComboBox { SelectedIndex = english ? 1 : 0, Margin = new Thickness(0,0,0,16) };
        language.Items.Add("中文"); language.Items.Add("English"); panel.Children.Add(language);
        var scaleLabel = new TextBlock { Margin = new Thickness(0,0,0,6) }; panel.Children.Add(scaleLabel);
        var scaleRow = new Grid {Margin=new Thickness(0,0,0,12)}; scaleRow.ColumnDefinitions.Add(new ColumnDefinition()); scaleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) }); scaleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        var slider = new Slider { Minimum = LayoutMetrics.MinimumScale*100, Maximum = LayoutMetrics.MaximumScale*100, Value = zoom * 100, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) };
        var input = new TextBox { Text = (zoom * 100).ToString("0",CultureInfo.InvariantCulture), Padding = new Thickness(7,5,7,5), VerticalContentAlignment = VerticalAlignment.Center,Background=SettingsBrush("Input"),Foreground=SettingsBrush("Text"),BorderThickness=new Thickness(0) };
        var percent = new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4,0,0,0) };
        scaleRow.Children.Add(slider); Grid.SetColumn(input,1); scaleRow.Children.Add(input); Grid.SetColumn(percent,2); scaleRow.Children.Add(percent); panel.Children.Add(scaleRow);
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,14), Foreground = SettingsBrush("Secondary") }; panel.Children.Add(note);
        var thresholdLabel=new TextBlock();
        var commitThreshold=AddNumberSetting(panel,thresholdLabel,LayoutMetrics.MinimumScale*100,LayoutMetrics.MaximumScale*100,textThreshold*100,"%",value=>{textThreshold=value/100;Render();SaveDisplayOptions();});
        var opacityLabel=new TextBlock();
        AddNumberSetting(panel,opacityLabel,30,100,widgetOpacity*100,"%",value=>{widgetOpacity=value/100;window.Opacity=widgetOpacity;SaveDisplayOptions();});
        var widthLabel=new TextBlock();
        var commitWidth=AddNumberSetting(panel,widthLabel,LayoutMetrics.MinimumWidth,LayoutMetrics.MaximumWidth,baseWidth,"px",value=>{baseWidth=value;Render();SaveDisplayOptions();});
        var colors = new Button { Padding = new Thickness(10,5,10,5), Margin = new Thickness(0) }; panel.Children.Add(colors);
        LoadColorCards();
        Button[] presetCards=new Button[0],animatedCards=new Button[0];Grid presetGrid=null;
        var colorNote=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,0,0,4),Foreground=SettingsBrush("Secondary")};
        var cardViewport=new ScrollViewer {Height=208,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false,PanningMode=PanningMode.VerticalOnly,Margin=new Thickness(0,0,0,10)};
        cardViewport.Padding=new Thickness(8,0,8,0);cardViewport.Resources=SettingsStyles();cardViewport.Template=(ControlTemplate)cardViewport.Resources["OfficeCardScroll"];EnableCardDragging(cardViewport);
        var animatedViewport=new ScrollViewer {Height=208,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false,PanningMode=PanningMode.VerticalOnly,Margin=new Thickness(0,0,0,10),Padding=new Thickness(8,0,8,0)};
        animatedViewport.Resources=SettingsStyles();animatedViewport.Template=(ControlTemplate)animatedViewport.Resources["OfficeCardScroll"];EnableCardDragging(animatedViewport);
        Action syncColors=()=>{UpdatePresetCards(presetCards);UpdatePresetCards(animatedCards);};
        Action reloadCards=null;reloadCards=()=>{
            double previousOffset=cardViewport.VerticalOffset,previousAnimatedOffset=animatedViewport.VerticalOffset;
            Action<ColorCard> deleteCard=card=>{
                if(MessageBox.Show(dialog,Text("删除色卡“","Delete color card “")+ColorCardName(card)+Text("”？当前窗口配色会保留。","”? Current window colors will be kept."),Text("删除色卡","Delete color card"),MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                try {DeleteColorCard(card);reloadCards();} catch(Exception ex) {MessageBox.Show(dialog,Text("无法删除色卡：","Could not delete color card: ")+ex.Message);}
            };
            presetGrid=CreatePresetCards(index=>{ApplyColorCard((ColorCard)presetCards[index].Tag);syncColors();},out presetCards,deleteCard,false);
            var animatedGrid=CreatePresetCards(index=>{ApplyColorCard((ColorCard)animatedCards[index].Tag);syncColors();},out animatedCards,deleteCard,true);
            cardViewport.Content=presetGrid;animatedViewport.Content=animatedGrid;syncColors();
            cardViewport.ScrollToVerticalOffset(previousOffset);animatedViewport.ScrollToVerticalOffset(previousAnimatedOffset);
        };
        reloadCards();
        var toolbarOption = new CheckBox { IsChecked = pinIcons, Foreground = SettingsBrush("Text"), Margin = new Thickness(0,0,0,16) }; panel.Children.Add(toolbarOption);
        var noExpandOption = new CheckBox { IsChecked=noExpand, Foreground=SettingsBrush("Text"), Margin=new Thickness(0,0,0,12) }; panel.Children.Add(noExpandOption);
        var minimalOption=new CheckBox {IsChecked=minimalMode,Foreground=SettingsBrush("Text"),Margin=new Thickness(0,0,0,12)};panel.Children.Add(minimalOption);
        var minimalNote=new TextBlock {Foreground=SettingsBrush("Secondary"),FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};panel.Children.Add(minimalNote);
        var placementLabel = new TextBlock { Margin = new Thickness(0,0,0,6) }; panel.Children.Add(placementLabel);
        var placement = new ComboBox { Margin = new Thickness(0,0,0,12) };
        placement.Items.Add(new ComboBoxItem()); placement.Items.Add(new ComboBoxItem());
        placement.SelectedIndex = toolbarTop ? 1 : 0; panel.Children.Add(placement);
        var orderLabel = new TextBlock { Margin = new Thickness(0,0,0,6) }; panel.Children.Add(orderLabel);
        var orderEditors = new ComboBox[4]; var positionLabels = new TextBlock[4];
        for (int i=0;i<4;i++) {
            var row = new Grid { Margin = new Thickness(0,0,0,6) };
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(70)}); row.ColumnDefinitions.Add(new ColumnDefinition());
            positionLabels[i] = new TextBlock {VerticalAlignment=VerticalAlignment.Center}; row.Children.Add(positionLabels[i]);
            orderEditors[i] = new ComboBox();
            foreach (string name in DefaultToolbarOrder) orderEditors[i].Items.Add(new ComboBoxItem {Tag=name});
            orderEditors[i].SelectedIndex = Array.IndexOf(DefaultToolbarOrder,toolbarOrder[i]);
            Grid.SetColumn(orderEditors[i],1); row.Children.Add(orderEditors[i]); panel.Children.Add(row);
        }
        var resetToolbar = new Button {Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,0,16)}; panel.Children.Add(resetToolbar);
        bool syncingOrder = false;
        Action syncOrder = () => {
            syncingOrder = true;
            try { for (int i=0;i<4;i++) orderEditors[i].SelectedIndex = Array.IndexOf(DefaultToolbarOrder,toolbarOrder[i]); placement.SelectedIndex=toolbarTop ? 1 : 0; }
            finally { syncingOrder = false; }
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var done = new Button { Padding = new Thickness(12,5,12,5), IsCancel = true,Tag="OfficePrimary" };
        actions.Children.Add(done); panel.Children.Add(actions);
        var sizeHeading=new TextBlock();var toolbarHeading=new TextBlock();var languageHeading=new TextBlock();
        var toolbarElements=panel.Children.Cast<UIElement>().SkipWhile(element=>element!=toolbarOption).TakeWhile(element=>element!=actions).ToArray();
        panel.Children.Clear();
        var settingsHeading=new TextBlock {FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,16)};panel.Children.Add(settingsHeading);
        var generalPanel=new StackPanel {Margin=new Thickness(0,2,0,0)};var toolbarPanel=new StackPanel {Margin=new Thickness(0,10,0,0)};var colorPanel=new StackPanel {Margin=new Thickness(0,10,0,0)};
        var startupTabPanel=new StackPanel {Margin=new Thickness(0,10,0,0)};
        var startupPage=new TabItem {Content=new ScrollViewer {Content=startupTabPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},Padding=new Thickness(8)};
        var pages=new TabControl {Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
        // A plain header row avoids native TabPanel selection offsets/clipping.
        pages.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TabControl'><Grid ClipToBounds='False' SnapsToDevicePixels='True'><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions><Border Grid.Row='0' BorderBrush='{DynamicResource OfficeLine}' BorderThickness='0,0,0,1' Padding='0,0,0,8' Margin='0,0,0,10'><ItemsPresenter Margin='0,0,2,0' ClipToBounds='False'/></Border><ContentPresenter x:Name='PART_SelectedContentHost' Grid.Row='1' ContentSource='SelectedContent' Margin='0' /></Grid></ControlTemplate>");
        var headerPanel=new FrameworkElementFactory(typeof(StackPanel));headerPanel.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);pages.ItemsPanel=new ItemsPanelTemplate(headerPanel);
        var generalPage=new TabItem {Content=new ScrollViewer {Content=generalPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        var toolbarPage=new TabItem {Content=new ScrollViewer {Content=toolbarPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        var colorOuter=new ScrollViewer {Content=colorPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var colorPage=new TabItem {Content=colorOuter};
        Action aboutLabels;var aboutPanel=CreateAboutSettings(out aboutLabels);
        var aboutPage=new TabItem {Content=new ScrollViewer {Content=aboutPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        dialog.SizeChanged+=(s,e)=>colorOuter.VerticalScrollBarVisibility=dialog.ActualHeight+1<dialog.Height ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        pages.Items.Add(generalPage);pages.Items.Add(toolbarPage);pages.Items.Add(colorPage);pages.Items.Add(startupPage);pages.Items.Add(aboutPage);
        foreach(TabItem page in pages.Items) {
            page.Padding=new Thickness(4,6,4,6);page.Margin=new Thickness(0,0,3,0);page.FontSize=12;((Control)page.Content).Foreground=SettingsBrush("Text");
            page.PreviewMouseLeftButtonDown+=(s,e)=>{var point=e.GetPosition(page);if(point.X>=0 && point.X<=page.ActualWidth && point.Y>=0 && point.Y<=page.ActualHeight) AcknowledgeTabGuide(page);};
        }
        pages.SelectedIndex=0;panel.Children.Add(pages);
        language.Margin=new Thickness(0);scaleRow.Margin=new Thickness(0,0,0,2);note.Margin=new Thickness(0,2,0,4);
        foreach(var box in scaleRow.Children.OfType<TextBox>())box.Padding=new Thickness(5,2,5,2);
        foreach(var label in new[]{scaleLabel,thresholdLabel,widthLabel,opacityLabel,languageLabel}) label.Margin=new Thickness(0,0,0,3);
        foreach(var label in new[]{thresholdLabel,widthLabel,opacityLabel}) {var row=(Grid)label.Tag;row.Margin=new Thickness(0,0,0,2);foreach(var inputBox in row.Children.OfType<TextBox>()) inputBox.Padding=new Thickness(5,2,5,2);}
        ((Grid)thresholdLabel.Tag).Margin=new Thickness(0,0,0,8);((Grid)widthLabel.Tag).Margin=new Thickness(0);
        StackPanel groupTarget=generalPanel;
        Action<TextBlock,UIElement[]> group=(heading,elements)=>{
            groupTarget.Children.Add(SettingsGroup(heading,elements));
        };
        var languageRow=new Grid();languageRow.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(130)});languageRow.ColumnDefinitions.Add(new ColumnDefinition());
        languageLabel.VerticalAlignment=VerticalAlignment.Center;languageLabel.Margin=new Thickness(0);languageRow.Children.Add(languageLabel);Grid.SetColumn(language,1);languageRow.Children.Add(language);
        languageHeading.Visibility=Visibility.Collapsed;group(languageHeading,new UIElement[]{languageRow});
        var thresholdNote=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,4)};
        var waveOption=new CheckBox {IsChecked=waveEnabled,Margin=new Thickness(0,4,0,0),Foreground=SettingsBrush("Text")};
        waveOption.Checked+=(s,e)=>{waveEnabled=true;Render();SaveDisplayOptions();};
        waveOption.Unchecked+=(s,e)=>{waveEnabled=false;Render();SaveDisplayOptions();};
        var waveTextOption=new CheckBox {IsChecked=waveOverText,Margin=new Thickness(12,4,0,0),Foreground=SettingsBrush("Text")};
        waveTextOption.Checked+=(s,e)=>{waveOverText=true;Render();SaveDisplayOptions();};
        waveTextOption.Unchecked+=(s,e)=>{waveOverText=false;Render();SaveDisplayOptions();};
        var waveOptions=new StackPanel {Orientation=Orientation.Horizontal};waveOptions.Children.Add(waveOption);waveOptions.Children.Add(waveTextOption);
        var waveTravelLabel=new TextBlock {VerticalAlignment=VerticalAlignment.Center};
        var waveTravelPanel=new StackPanel();var commitWaveTravel=AddNumberSetting(waveTravelPanel,waveTravelLabel,0,5,waveTravel,"px",value=>{waveTravel=value;Render();SaveDisplayOptions();});
        var waveTravelRow=(Grid)waveTravelLabel.Tag;waveTravelPanel.Children.Clear();
        waveTravelRow.ColumnDefinitions.Insert(0,new ColumnDefinition {Width=new GridLength(68)});
        foreach(UIElement child in waveTravelRow.Children) Grid.SetColumn(child,Grid.GetColumn(child)+1);
        waveTravelLabel.Margin=new Thickness(0);waveTravelRow.Children.Add(waveTravelLabel);waveTravelRow.Margin=new Thickness(0,4,0,0);
        foreach(var box in waveTravelRow.Children.OfType<TextBox>()) box.Padding=new Thickness(5,3,5,3);
        group(sizeHeading,new UIElement[]{InlineSettingsRow(scaleLabel,scaleRow),note,InlineSettingsRow(thresholdLabel,(Grid)thresholdLabel.Tag),thresholdNote,InlineSettingsRow(widthLabel,(Grid)widthLabel.Tag),InlineSettingsRow(opacityLabel,(Grid)opacityLabel.Tag)});
        var waveHeading=new TextBlock {Visibility=Visibility.Collapsed};
        waveTravelRow.ColumnDefinitions[0].Width=new GridLength(104);waveTravelRow.ColumnDefinitions[2].Width=new GridLength(44);waveTravelRow.ColumnDefinitions[3].Width=new GridLength(20);
        group(waveHeading,new UIElement[]{waveOptions,waveTravelRow});
        Action startupLabels;var startupPanel=CreateStartupSettings(guide,out startupLabels);
        Action providerLabels;var providerPanel=CreateProviderSettings(out providerLabels);
        startupTabPanel.Children.Add(providerPanel);
        startupTabPanel.Children.Add(startupPanel);
        toolbarOption.Margin=noExpandOption.Margin=minimalOption.Margin=new Thickness(0,0,0,6);placement.Margin=new Thickness(0,0,0,8);resetToolbar.Margin=new Thickness(0,6,0,0);groupTarget=toolbarPanel;group(toolbarHeading,toolbarElements);
        var heightHeading=new TextBlock {VerticalAlignment=VerticalAlignment.Center};
        var hourHeightLabel=new TextBlock {FontSize=11,VerticalAlignment=VerticalAlignment.Center};
        var weekHeightLabel=new TextBlock {FontSize=11,VerticalAlignment=VerticalAlignment.Center};
        Action commitHourHeight,commitWeekHeight;
        var hourHeightInput=BarHeightInput(hourBarHeight,value=>{hourBarHeight=value;Render();SaveDisplayOptions();},out commitHourHeight);
        var weekHeightInput=BarHeightInput(weekBarHeight,value=>{weekBarHeight=value;Render();SaveDisplayOptions();},out commitWeekHeight);
        var heightRow=new Grid {Margin=new Thickness(0,1,0,0)};
        foreach(double width in new[]{104.0,44,44,8,44,44,20})heightRow.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(width)});
        UIElement[] heightItems={heightHeading,hourHeightLabel,hourHeightInput,weekHeightLabel,weekHeightInput,new TextBlock {Text="px",VerticalAlignment=VerticalAlignment.Center}};
        int[] heightColumns={0,1,2,4,5,6};for(int i=0;i<heightItems.Length;i++){Grid.SetColumn(heightItems[i],heightColumns[i]);heightRow.Children.Add(heightItems[i]);}
        var heightNote=new TextBlock();
        ((StackPanel)((Border)generalPanel.Children[generalPanel.Children.Count-1]).Child).Children.Add(heightRow);
        var ratioLabel=new TextBlock {Width=104,VerticalAlignment=VerticalAlignment.Center};
        var ratioSelect=new ComboBox {MinWidth=190,SelectedIndex=-1};
        for(int i=0;i<3;i++)ratioSelect.Items.Add(new ComboBoxItem());
        ratioSelect.SelectedIndex=backgroundRatio;
        hourHeightInput.IsEnabled=weekHeightInput.IsEnabled=backgroundRatio==0;
        ratioSelect.SelectionChanged+=(s,e)=>{if(ratioSelect.SelectedIndex<0)return;backgroundRatio=ratioSelect.SelectedIndex;hourHeightInput.IsEnabled=weekHeightInput.IsEnabled=backgroundRatio==0;Render();SaveDisplayOptions();};
        var ratioRow=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0)};ratioRow.Children.Add(ratioLabel);ratioRow.Children.Add(ratioSelect);
        ((StackPanel)((Border)generalPanel.Children[generalPanel.Children.Count-1]).Child).Children.Add(ratioRow);
        ((StackPanel)((Border)generalPanel.Children[generalPanel.Children.Count-1]).Child).Margin=new Thickness(8,7,8,7);
        var arrowNote=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10),Foreground=guide?SettingsBrush("Guide"):SettingsBrush("Text")};
        Button[] exampleCards;var exampleGrid=CreatePresetCards(index=>{},out exampleCards);var exampleCard=exampleCards[3];exampleGrid.Children.Remove(exampleCard);exampleCard.Width=140;exampleCard.Height=88;exampleCard.Margin=new Thickness(0);exampleCard.IsHitTestVisible=false;
        var example=new Grid {Width=140,Height=88};example.Children.Add(exampleCard);
        var annotationLayer=new Canvas {IsHitTestVisible=false};var ring=new System.Windows.Shapes.Ellipse {Width=20,Height=20,Stroke=Brushes.OrangeRed,StrokeThickness=1.5};annotationLayer.Children.Add(ring);example.Children.Add(annotationLayer);
        // Follow the rendered triangle, rather than assuming card padding or font metrics.
        example.LayoutUpdated+=(s,e)=>{var triangle=Children(exampleCard).OfType<System.Windows.Shapes.Path>().FirstOrDefault(item=>(string)item.Tag=="PresetWeekArrow");if(triangle==null || !triangle.IsArrangeValid) return;var bounds=triangle.TransformToAncestor(example).TransformBounds(triangle.RenderedGeometry.Bounds);double left=bounds.Left+bounds.Width/2-ring.Width/2,top=bounds.Top+bounds.Height/2-ring.Height/2;if(Canvas.GetLeft(ring)!=left) Canvas.SetLeft(ring,left);if(Canvas.GetTop(ring)!=top) Canvas.SetTop(ring,top);};
        var compactExample=new Viewbox {Width=112,Height=71,Stretch=Stretch.Uniform,Child=example,HorizontalAlignment=HorizontalAlignment.Left};
        var arrowExplanation=new Grid {Margin=new Thickness(0,0,0,6)};arrowExplanation.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(120)});arrowExplanation.ColumnDefinitions.Add(new ColumnDefinition());arrowExplanation.Children.Add(compactExample);Grid.SetColumn(arrowNote,1);arrowNote.VerticalAlignment=VerticalAlignment.Center;arrowExplanation.Children.Add(arrowNote);
        var presetCategory=new TabItem {Content=cardViewport,Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,0,4,0),FontSize=12};
        var animatedCategory=new TabItem {Content=animatedViewport,Padding=new Thickness(8,5,8,5),FontSize=12};
        var colorCategories=new TabControl {Background=Brushes.Transparent,BorderThickness=new Thickness(0),Margin=new Thickness(0),Template=pages.Template};
        var colorHeaderPanel=new FrameworkElementFactory(typeof(StackPanel));colorHeaderPanel.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);colorCategories.ItemsPanel=new ItemsPanelTemplate(colorHeaderPanel);
        colorCategories.Items.Add(presetCategory);colorCategories.Items.Add(animatedCategory);
        string selectedCategoryCard=ResolveSelectedColorCard();colorCategories.SelectedIndex=IsAnimatedColorCard(AvailableColorCards().FirstOrDefault(card=>card.Id==selectedCategoryCard))?1:0;
        foreach(var item in new UIElement[]{colorCategories,colorNote,arrowExplanation,colors}) colorPanel.Children.Add(item);panel.Children.Add(actions);
        // Let the current page shrink within the screen instead of creating one
        // long settings document. Each page owns its own overflow scrolling.
        var settingsRoot=new DockPanel {Margin=new Thickness(18)};
        panel.Children.Remove(settingsHeading);panel.Children.Remove(pages);panel.Children.Remove(actions);
        var footer=new Border {BorderBrush=SettingsBrush("Line"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,10,0,0),Child=actions};DockPanel.SetDock(settingsHeading,Dock.Top);DockPanel.SetDock(footer,Dock.Bottom);
        settingsRoot.Children.Add(settingsHeading);settingsRoot.Children.Add(footer);settingsRoot.Children.Add(pages);dialog.Content=settingsRoot;
        foreach(var item in new TextBlock[]{languageLabel,note,thresholdLabel,thresholdNote,placementLabel,orderLabel}) if(guide) item.Foreground=SettingsBrush("Guide");
        if(guide) noExpandOption.Foreground=SettingsBrush("Guide");
        if(guide) minimalOption.Foreground=SettingsBrush("Guide");
        if(guide) toolbarOption.Foreground=SettingsBrush("Guide");
        if(guide) waveOption.Foreground=SettingsBrush("Guide");
        if(guide) waveTextOption.Foreground=waveTravelLabel.Foreground=SettingsBrush("Guide");
        if(guide) {
            foreach(var page in new[]{generalPage,toolbarPage,colorPage,startupPage,aboutPage}) page.Tag="guide";
        }
        Action labels = () => {
            arrowNote.Text=Text("周参考箭头：当天的本周建议用量","Weekly reference arrow: suggested weekly usage for the current day");
            dialog.Title = Text("系统设置", "Settings"); languageLabel.Text = Text("语言（Language）", "Language");
            startupLabels();
            providerLabels();
            aboutLabels();
            settingsHeading.Text=dialog.Title;languageHeading.Text=Text("语言（Language）","Language");sizeHeading.Text=Text("大小与显示","Size and display");waveHeading.Text=Text("波浪与高度","Waves and height");toolbarHeading.Text=Text("工具栏","Toolbar");
            presetCategory.Header=Text("预设配色","Color presets");animatedCategory.Header=Text("动画配色","Animated presets");
            scaleLabel.Text = Text("缩放比例", "Scale");
            note.Text = Text("鼠标置于悬浮窗上，用滚轮缩放。", "Point at the widget and scroll to resize.");note.FontSize=11;thresholdNote.FontSize=11;
            thresholdLabel.Text=Text("文字隐藏阈值", "Text threshold");thresholdLabel.ToolTip=Text("文字隐藏阈值","Text hiding threshold");
            thresholdNote.Text=Text("收起时，缩放比例 ≤ 此阈值则隐藏条内文字；展开时显示文字。","When collapsed, hide in-bar text at or below this scale. Expanded text remains visible.");
            opacityLabel.Text=Text("不透明度","Opacity");
            waveOption.Content=Text("额度前端波浪","Animate quota front");
            waveOption.ToolTip=Text("可调总摆幅；极小尺寸时停用。","Adjustable total travel; stops at very small sizes.");
            waveTextOption.Content=Text("文字处也波动","Animate over text");
            waveTextOption.ToolTip=Text("开启后不再避让文字；文字始终绘制在最上层。","When enabled, the front also moves behind text; text stays on top.");
            waveTravelLabel.Text=Text("总摆幅","Travel");
            heightHeading.Text=Text("额度条高度","Quota bar height");
            hourHeightLabel.Text=Text("五小时","5-hour");weekHeightLabel.Text=Text("周额度","Weekly");
            heightNote.Text=Text("展开、收起和极简共用；条内有字时至少保留 19 px，避免裁字。","Shared by all modes; bars with captions stay at least 19 px tall.");
            heightRow.ToolTip=heightNote.Text;
            ratioLabel.Text=Text("背景比例","Backdrop ratio");
            string[] ratioNames=english?new[]{"Free / manual heights","Original artwork","16:9"}:new[]{"自由 · 手动条高","原图比例 · 自动条高","16:9 · 自动条高"};
            for(int i=0;i<3;i++)((ComboBoxItem)ratioSelect.Items[i]).Content=ratioNames[i];
            ratioRow.ToolTip=Text("锁定时条高随窗口宽度及展开模式调整；完整原画等比显示，空余区域柔化补边。窄窗口优先容纳文字，自由模式恢复手动高度。","Locked ratios link bar heights to width and mode. Full artwork fits without stretching; softened artwork fills padding. Text minimums take priority in narrow windows. Free restores manual heights.");
            waveTravelLabel.ToolTip=Text("0–5 px，左右合计；0 为直线。","0–5 px total horizontal travel; 0 stays straight.");
            widthLabel.Text=Text("窗口宽度", "Window width");widthLabel.ToolTip=Text("100% 缩放时的窗口宽度。","Window width at 100% scale.");
            colors.Content = Text("自定义配色…", "Custom colors…"); done.Content = Text("完成", "Done");
            startupPage.Header=Text("更新／平台","Update / Provider");startupPage.ToolTip=Text("额度平台、开机启动与更新检查","Usage provider, Windows startup and update checks");aboutPage.Header=Text("关于","About");
            generalPage.Header=Text("常规","General");toolbarPage.Header=Text("工具栏","Toolbar");colorPage.Header=Text("配色","Colors");
            colorNote.Text=Text("点击色卡应用并保存；拖动或滚轮查看更多。自定义配色可另存色卡。","Click to apply and save. Drag or scroll for more. Save custom colors as cards.");syncColors();
            noExpandOption.Content=Text("不展开模式","No expansion mode"); noExpandOption.ToolTip=Text("鼠标进入时只显示工具栏，额度条保持收起布局。","Hover shows only the toolbar; usage bars retain their compact layout.");
            minimalOption.Content=Text("极简模式","Minimal mode");minimalOption.ToolTip=Text("只保留额度条和参考三角；隐藏文字、刻度和倒计时条。悬停也保持极简。","Only quota bars and reference arrows; no text, ticks or countdown bars, even on hover.");
            minimalNote.Text=Text("极简模式保留两条额度条和参考三角。","Minimal mode keeps both quota bars and reference arrows.");
            toolbarOption.Content = Text("收起时保留工具栏", "Keep toolbar when collapsed");
            toolbarOption.ToolTip=Text("关闭后，只有展开窗口时才显示工具栏。","When disabled, the toolbar appears only while expanded.");
            placementLabel.ToolTip=Text("上方工具栏向下展开，下方工具栏向上展开。","Top toolbar expands downward; bottom toolbar expands upward.");
            placementLabel.Text = Text("按钮栏位置", "Toolbar placement");
            ((ComboBoxItem)placement.Items[0]).Content = Text("下方 · 向上展开", "Bottom · expand upward");
            ((ComboBoxItem)placement.Items[1]).Content = Text("上方 · 向下展开", "Top · expand downward");
            orderLabel.Text = Text("按钮顺序（从左到右）", "Button order (left to right)");
            string[] names = english ? new[] {"Settings","Lock","Refresh","Close"} : new[] {"设置","锁","刷新","关闭"};
            for (int i=0;i<4;i++) {
                positionLabels[i].Text = Text("位置 ","Slot ")+(i+1);
                for (int j=0;j<4;j++) ((ComboBoxItem)orderEditors[i].Items[j]).Content=names[j];
            }
            resetToolbar.Content = Text("恢复默认位置与顺序", "Reset placement and order");
        };
        labels();
        language.SelectionChanged += (s,e) => { SelectLanguage(language.SelectedIndex == 1); labels(); };
        noExpandOption.Checked += (s,e) => {noExpand=true; Render(); SaveToolbarLayout();};
        noExpandOption.Unchecked += (s,e) => {noExpand=false; Render(); SaveToolbarLayout();};
        minimalOption.Checked+=(s,e)=>{minimalMode=true;Render();SaveToolbarLayout();};
        minimalOption.Unchecked+=(s,e)=>{minimalMode=false;Render();SaveToolbarLayout();};
        toolbarOption.Checked += (s,e) => SetPinnedIcons(true);
        toolbarOption.Unchecked += (s,e) => SetPinnedIcons(false);
        placement.SelectionChanged += (s,e) => { if (!syncingOrder && placement.SelectedIndex >= 0) { toolbarTop=placement.SelectedIndex==1; Render(); SaveToolbarLayout(); } };
        for (int i=0;i<4;i++) {
            int slot=i;
            orderEditors[i].SelectionChanged += (s,e) => {
                if (syncingOrder || orderEditors[slot].SelectedIndex < 0) return;
                SwapToolbarPosition(slot,DefaultToolbarOrder[orderEditors[slot].SelectedIndex]); syncOrder();
            };
        }
        resetToolbar.Click += (s,e) => { toolbarTop=false; toolbarOrder=(string[])DefaultToolbarOrder.Clone(); syncOrder(); Render(); SaveToolbarLayout(); };
        slider.ValueChanged += (s,e) => { SetZoom(slider.Value / 100); input.Text = (zoom * 100).ToString("0",CultureInfo.InvariantCulture); input.ClearValue(System.Windows.Controls.Control.BorderBrushProperty); };
        Action commit = () => {
            double value;
            if (!double.TryParse(input.Text.Trim().TrimEnd('%'),NumberStyles.Float,CultureInfo.InvariantCulture,out value) || double.IsNaN(value) || double.IsInfinity(value)) {
                input.BorderBrush = Brushes.OrangeRed; return;
            }
            SetZoom(value / 100); slider.Value = zoom * 100;
            input.Text = (zoom * 100).ToString("0",CultureInfo.InvariantCulture); input.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        };
        input.LostKeyboardFocus += (s,e) => commit();
        input.KeyDown += (s,e) => { if (e.Key == System.Windows.Input.Key.Enter) { commit(); e.Handled = true; } };
        colors.Click += (s,e) => {OpenAppearance(null,guide);reloadCards();};
        done.Click += (s,e) => { commit(); commitThreshold(); commitWidth(); commitWaveTravel(); commitHourHeight(); commitWeekHeight(); dialog.Close(); };
        try {
            if (checking && !settingsFocusedCheck && !settingsVisualOnlyCheck) {
                var initialColors=new Dictionary<string,string>(Theme.Colors);
                for(int i=0;i<4;i++) {
                    presetCards[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(Theme.Keys.Any(key=>Theme.Colors[key]!=Theme.Preset(i)[key])) throw new Exception("Immediate card selection failed.");
                }
                Theme.Colors=initialColors;ApplyTheme();syncColors();
                if(pages.Items.Count!=3 || generalPanel.Children.OfType<Border>().Count()!=2 || toolbarPanel.Children.OfType<Border>().Count()!=1 || colorPanel.Children.OfType<Border>().Count()!=1 || cardViewport.Height!=208
                    || languageLabel.Text!=Text("语言（Language）","Language")) throw new Exception("Settings pages / card viewport failed.");
                bool guidanceShown=note.Foreground.ToString()==SettingsBrush("Guide").ToString();
                if(guidanceShown!=guide || ((SolidColorBrush)toolbarOption.Foreground).Color!=((SolidColorBrush)(guide ? SettingsBrush("Guide") : SettingsBrush("Text"))).Color) throw new Exception("Settings guide visibility failed.");
                if(new[]{generalPage,toolbarPage,colorPage}.Any(page=>((string)page.Tag=="guide")!=guide) || (colors.Foreground.ToString()==SettingsBrush("Guide").ToString())!=guide) throw new Exception("Three-page guide / custom-color emphasis failed.");
                double before = zoom; bool wasEnglish = english;
                bool beforeTop=toolbarTop;var beforeOrder=(string[])toolbarOrder.Clone();
                double oldThreshold=textThreshold,oldWidth=baseWidth;
                var numberRows=new[]{scaleRow,(Grid)thresholdLabel.Tag,(Grid)widthLabel.Tag};
                var thresholdInput=numberRows[1].Children.OfType<TextBox>().Single();
                var widthInput=numberRows[2].Children.OfType<TextBox>().Single();
                numberRows[1].Children.OfType<Slider>().Single().Value=60;
                numberRows[2].Children.OfType<Slider>().Single().Value=480;
                if(textThreshold!=0.6 || baseWidth!=480) throw new Exception("Display sliders failed.");
                thresholdInput.Text="not a number";commitThreshold();
                if(textThreshold!=0.6 || thresholdInput.BorderBrush!=Brushes.OrangeRed) throw new Exception("Invalid threshold accepted.");
                thresholdInput.Text="75%";commitThreshold();widthInput.Text="300";commitWidth();
                if(textThreshold!=0.75 || baseWidth!=300) throw new Exception("Display numeric input failed.");
                thresholdInput.Text=(oldThreshold*100).ToString("0",CultureInfo.InvariantCulture);commitThreshold();
                widthInput.Text=oldWidth.ToString("0",CultureInfo.InvariantCulture);commitWidth();
                bool previousPin = pinIcons;
                toolbarOption.IsChecked = !previousPin;
                if (pinIcons == previousPin) throw new Exception("Toolbar setting did not update.");
                toolbarOption.IsChecked = previousPin;
                orderEditors[0].SelectedIndex = 3;
                if (toolbarOrder[0] != "CloseButton" || toolbarOrder[3] != "SettingsButton" || orderEditors[3].SelectedIndex != 0) throw new Exception("Settings order exchange failed.");
                placement.SelectedIndex = 1;
                if (!toolbarTop || Grid.GetRow(Control<Grid>("Toolbar")) != 0) throw new Exception("Settings placement failed.");
                resetToolbar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (toolbarTop || !toolbarOrder.SequenceEqual(DefaultToolbarOrder)) throw new Exception("Toolbar defaults failed.");
                slider.Value = 40;
                if (zoom != 0.4 || Control<StackPanel>("Details").Visibility != Visibility.Collapsed || Control<ProgressBar>("CompactHourBar").Uid != "") throw new Exception("Settings slider / mini layout failed.");
                input.Text = "125"; commit();
                if (zoom != 1.25 || slider.Value != 125) throw new Exception("Scale input failed.");
                input.Text = "NaN"; commit(); if (zoom != 1.25) throw new Exception("Invalid scale accepted.");
                input.Text = "999"; commit(); if (zoom != 1.5) throw new Exception("Scale input upper bound failed.");
                input.Text = "10"; commit(); if (zoom != 0.4) throw new Exception("Scale input lower bound failed.");
                language.SelectedIndex = wasEnglish ? 0 : 1;
                if (english == wasEnglish || dialog.Title != Text("系统设置", "Settings")) throw new Exception("Settings language selection failed.");
                language.SelectedIndex = wasEnglish ? 1 : 0;
                colors.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                toolbarTop=beforeTop;toolbarOrder=beforeOrder;syncOrder();
                SetZoom(before);
                slider.Value=before*100;input.Text=(before*100).ToString("0",CultureInfo.InvariantCulture);
                settingsRoot.Measure(new Size(dialog.Width,dialog.Height-32));settingsRoot.Arrange(new Rect(0,0,dialog.Width,dialog.Height-32));settingsRoot.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)dialog.Width,(int)dialog.Height-32,96,96,PixelFormats.Pbgra32);bitmap.Render(settingsRoot);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(settingsPreviewPath))encoder.Save(stream);
                SavePanelPreview(presetGrid,dialog.Background,338,Path.Combine(Path.GetDirectoryName(settingsPreviewPath),english ? "preset-cards-en.png" : "preset-cards-zh.png"));
            } else if(settingsFocusedCheck) {
                Exception failure=null;
                dialog.ContentRendered+=(s,e)=>{
                    try {
                        dialog.UpdateLayout();double originalWidth=dialog.ActualWidth,originalHeight=dialog.ActualHeight;
                        foreach(TabItem page in pages.Items) {
                            pages.SelectedItem=page;dialog.UpdateLayout();page.ApplyTemplate();
                            var dot=(System.Windows.Shapes.Ellipse)page.Template.FindName("GuideDot",page);
                            var tabBorder=(Border)page.Template.FindName("Tab",page);if(tabBorder.CornerRadius!=new CornerRadius(5) || page.Background.ToString()!="#00FFFFFF") throw new Exception("Rounded tab template mismatch");
                            if(dot==null || (dot.Visibility==Visibility.Visible)!=guide) throw new Exception("Tab red-dot guide mismatch");
                            if(page.Foreground.ToString()==SettingsBrush("Guide").ToString()) throw new Exception("Inherited tab highlight remained");
                            var header=(ContentPresenter)Children(page).OfType<ContentPresenter>().First(item=>item.ContentSource=="Header");
                            if(header.ActualWidth+header.Margin.Left+header.Margin.Right+0.5<header.DesiredSize.Width) throw new Exception("Tab header clipped");
                            if(dialog.ActualWidth!=originalWidth || dialog.ActualHeight!=originalHeight) throw new Exception("Tab changes window size");
                            if(((ScrollViewer)page.Content).ScrollableHeight>1) throw new Exception("Settings page requires scrolling: "+page.Header+" "+((ScrollViewer)page.Content).ScrollableHeight);
                            if(page==aboutPage) {
                                var qr=Children(aboutPanel).OfType<Image>().Single(item=>item.Name=="ProjectQr");
                                if(qr.ActualWidth!=164 || qr.ActualHeight!=164 || (string)qr.ToolTip!=AppServices.ProjectUrl || ((BitmapSource)qr.Source).PixelWidth!=164) throw new Exception("About QR target or size mismatch");
                            }
                            if(page==colorPage) {var buttonBounds=colors.TransformToAncestor(colorOuter).TransformBounds(new Rect(0,0,colors.ActualWidth,colors.ActualHeight));if(buttonBounds.Bottom>colorOuter.ActualHeight+0.5) throw new Exception("Custom color button clipped: "+buttonBounds.Bottom+" > "+colorOuter.ActualHeight);}
                            var bounds=tabBorder.TransformToAncestor(pages).TransformBounds(new Rect(0,0,tabBorder.ActualWidth,tabBorder.ActualHeight));if(bounds.Left<0 || bounds.Right>pages.ActualWidth+0.5) throw new Exception("Tab border clipped by header container");
                            if(page==startupPage && settingsVisits==1) {var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(Path.GetDirectoryName(settingsFocusedReport),"tabs-actual.png"))) encoder.Save(output);}
                            if(settingsVisits==1 || !guide) SaveSettingsSnapshot(dialog,"page-"+pages.Items.IndexOf(page)+(guide ? "-guide" : "-normal")+".png");
                        }
                        pages.SelectedItem=generalPage;dialog.UpdateLayout();
                        pages.SelectedItem=colorPage;dialog.UpdateLayout();
                        var fifth=presetCards[4];var fifthTop=fifth.TransformToAncestor(cardViewport).Transform(new Point(0,0)).Y;if(cardViewport.ViewportHeight-196<11 || fifthTop>=cardViewport.ActualHeight) throw new Exception("Fifth card not partially visible");
                        var scrollStyle=(Style)cardViewport.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)];if((double)scrollStyle.Setters.OfType<Setter>().First(item=>item.Property==FrameworkElement.WidthProperty).Value!=2) throw new Exception("Scrollbar width mismatch");
                        var actualScroll=(System.Windows.Controls.Primitives.ScrollBar)cardViewport.Template.FindName("PART_VerticalScrollBar",cardViewport);if(actualScroll.ActualWidth!=2) throw new Exception("Rendered scrollbar width="+actualScroll.ActualWidth+", minimum="+actualScroll.MinWidth+", DPI="+VisualTreeHelper.GetDpi(dialog).DpiScaleX);
                        cardViewport.ScrollToBottom();dialog.UpdateLayout();if(cardViewport.VerticalOffset<=0) throw new Exception("Color cards cannot scroll");
                        if(settingsVisits==1) SaveSettingsSnapshot(dialog,"color-cards-bottom.png");
                        cardViewport.ScrollToTop();dialog.UpdateLayout();
                        if(((ScrollViewer)colorPage.Content).ScrollableHeight>1) throw new Exception("Colors page requires outer scrolling");pages.SelectedItem=generalPage;dialog.UpdateLayout();
                        if(((ScrollViewer)generalPage.Content).ScrollableHeight>1) throw new Exception("General page requires scrolling: "+((ScrollViewer)generalPage.Content).ScrollableHeight);
                        if((waveOption.Foreground.ToString()==SettingsBrush("Guide").ToString())!=guide || (waveTextOption.Foreground.ToString()==SettingsBrush("Guide").ToString())!=guide || (waveTravelLabel.Foreground.ToString()==SettingsBrush("Guide").ToString())!=guide) throw new Exception("Wave guide mismatch");
                        if(waveOptions.DesiredSize.Width>waveOptions.ActualWidth+0.5) throw new Exception("Wave options clipped");
                        if(settingsVisits==1) {var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(Path.GetDirectoryName(settingsFocusedReport),"general-actual.png"))) encoder.Save(output);}
                        foreach(var label in new[]{widthLabel,opacityLabel}) if(label.Foreground.ToString()==SettingsBrush("Guide").ToString()) throw new Exception("Unrequested label highlighted");
                        if(colors.Foreground.ToString()==SettingsBrush("Guide").ToString()) throw new Exception("Custom colors highlighted");
                        if((thresholdNote.Foreground.ToString()==SettingsBrush("Guide").ToString())!=guide || (note.Foreground.ToString()==SettingsBrush("Guide").ToString())!=guide) throw new Exception("Guide fields mismatch");
                        var defaults=AvailableColorCards()[0];if(defaults.Id!="builtin-default" || Theme.Keys.Any(key=>defaults.Colors[key]!=Theme.Defaults()[key])) throw new Exception("Default card mismatch");
                        if(AvailableColorCards().Count!=Theme.PresetCount+1+customColorCards.Count) throw new Exception("Builtin palette count mismatch");
                        for(int i=0;i<Theme.PresetCount;i++) {var card=AvailableColorCards()[i+1];if(!ApplyColorCard(card) || Theme.Keys.Any(key=>Theme.Colors[key]!=Theme.Preset(i)[key])) throw new Exception("Preset apply mismatch: "+i);}
                        if(!ApplyColorCard(defaults) || Theme.Keys.Any(key=>Theme.Colors[key]!=Theme.Defaults()[key])) throw new Exception("Default card apply failed");
                    } catch(Exception ex) {failure=ex;} finally {dialog.Close();}
                };
                dialog.ShowDialog();if(failure!=null) throw failure;
            } else {
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                PositionSettings(dialog,dialog.Height);
                dialog.ContentRendered += (s,e) => PositionSettings(dialog);
                dialog.ShowDialog();
            }
        } finally { settingsOpen = false; settingsWindow = null; if (!window.IsMouseOver) collapseTimer.Start(); }
    }
    static string settingsPreviewPath;
    static bool settingsFocusedCheck;
    static bool settingsVisualOnlyCheck=false; // Offline rendering without the legacy embedded settings assertions.
    static string settingsFocusedReport;
    static void AcknowledgeTabGuide(TabItem page) {page.Tag=null;}
    static void CheckToolbarLayout(string report) {
        window.Opacity=0; window.ShowActivated=false; window.ShowInTaskbar=false; window.Left=-10000; window.Top=-10000;
        long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        snapshot=new Snapshot {Time=DateTimeOffset.UtcNow,Live=true,Hour=new Limit {Remaining=65,Reset=now+18000},Week=new Limit {Remaining=72,Reset=now+604800}};
        window.Show(); window.UpdateLayout();
        SwapToolbarPosition(0,"CloseButton");
        if (toolbarOrder[0] != "CloseButton" || toolbarOrder[3] != "SettingsButton") throw new Exception("Duplicate choice did not swap.");
        var serializer=new JavaScriptSerializer();
        var saved=new ToolbarLayout {Top=true,Order=toolbarOrder};
        if (!ApplyToolbarLayout(serializer.Deserialize<ToolbarLayout>(serializer.Serialize(saved)))) throw new Exception("Toolbar preference roundtrip failed.");
        if (ApplyToolbarLayout(new ToolbarLayout {Top=false,Order=new[] {"CloseButton","CloseButton","LockButton","RefreshButton"}}) || !toolbarTop) throw new Exception("Invalid order changed layout.");
        toolbarOrder=(string[])DefaultToolbarOrder.Clone();
        var folder=Path.GetDirectoryName(report);
        var referenceOffsets=new Dictionary<string,double>();
        foreach (bool en in new[] {false,true}) {
            SelectLanguage(en);
            foreach (bool above in new[] {false,true}) {
                toolbarTop=above;
                foreach (bool pinned in new[] {false,true}) {
                    SetPinnedIcons(pinned);
                    foreach (double scale in new[] {0.4,1.0,1.5}) {
                        SetZoom(scale); SetExpanded(false); window.UpdateLayout();
                        if (!pinned) {
                            var padding=Control<Border>("Root").Padding;
                            if (padding.Top!=8 || padding.Bottom!=8) throw new Exception("Hidden toolbar padding is asymmetric.");
                            var first=Control<ProgressBar>("CompactHourBar"); var last=Control<ProgressBar>("CompactWeekTimeBar");
                            double firstY=first.TranslatePoint(new Point(0,0),window).Y;
                            double lastY=last.TranslatePoint(new Point(0,last.ActualHeight),window).Y;
                            if (Math.Abs(firstY-(window.ActualHeight-lastY))>1.5) throw new Exception("Scaled hidden-toolbar visual whitespace is asymmetric.");
                        }
                        double fixedEdge=above ? window.Top : window.Top+window.Height;
                        SetExpanded(true); window.UpdateLayout();
                        if (Math.Abs((above ? window.Top : window.Top+window.Height)-fixedEdge)>0.1) throw new Exception("Expansion moved fixed edge.");
                        if (scale>=0.8) {
                            double infoOffset=Control<Grid>("HourInfo").TranslatePoint(new Point(0,0),window).Y;
                            string key=en+"/"+pinned+"/"+scale.ToString(CultureInfo.InvariantCulture);
                            if (!above) referenceOffsets[key]=infoOffset;
                            else if (Math.Abs(infoOffset-referenceOffsets[key])>1.5) throw new Exception("Top and bottom expanded header spacing differs.");
                        }
                        var toolbar=Control<Grid>("Toolbar"); var quota=Control<Grid>("QuotaArea");
                        double toolbarY=toolbar.TranslatePoint(new Point(0,0),window).Y;
                        double quotaY=quota.TranslatePoint(new Point(0,0),window).Y;
                        if ((toolbarY<quotaY)!=above) throw new Exception("Toolbar row placement incorrect.");
                        if (above && scale>=0.8) {
                            if (Control<TextBlock>("TitleLabel").Parent!=toolbar || Control<TextBlock>("TopUpdated").Parent!=Control<Grid>("HourInfo")) throw new Exception("Top title / update row incorrect.");
                            if (Control<TextBlock>("TopUpdated").Visibility!=Visibility.Visible || string.IsNullOrEmpty(Control<TextBlock>("TopUpdated").Text)) throw new Exception("Update time missing in top layout.");
                            for (int i=1;i<5;i++) if (toolbar.ColumnDefinitions[i].Width.Value!=24 || toolbar.ColumnDefinitions[i].Width.GridUnitType!=GridUnitType.Pixel) throw new Exception("Top buttons not compact.");
                        }
                        for (int i=0;i<4;i++) if (Grid.GetColumn(Control<Button>(toolbarOrder[i]))!=i+1) throw new Exception("Order not applied to columns.");
                        double iconY=Control<Button>("SettingsButton").TranslatePoint(new Point(0,0),window).Y+window.Top;
                        SetExpanded(false); window.UpdateLayout();
                        if (above && pinned && Math.Abs(Control<Button>("SettingsButton").TranslatePoint(new Point(0,0),window).Y+window.Top-iconY)>0.1) throw new Exception("Top buttons moved on collapse.");
                        if ((toolbar.Visibility==Visibility.Visible)!=pinned || Math.Abs((above ? window.Top : window.Top+window.Height)-fixedEdge)>0.1) throw new Exception("Collapse preference or edge changed.");
                        SetExpanded(true); window.UpdateLayout();
                        if (Math.Abs(Control<Button>("SettingsButton").TranslatePoint(new Point(0,0),window).Y+window.Top-iconY)>0.1) throw new Exception("Toolbar shifted between expansions.");
                        if (scale==1 && pinned) {
                            SavePreview(Path.Combine(folder,(en ? "en" : "zh")+(above ? "-top" : "-bottom")+"-expanded.png"));
                            SetExpanded(false); SavePreview(Path.Combine(folder,(en ? "en" : "zh")+(above ? "-top" : "-bottom")+"-compact.png"));
                        }
                    }
                }
            }
        }
        toolbarTop=true; SetZoom(1); SetExpanded(true); snapshot.Hour=null; Render(); window.UpdateLayout();
        if (Control<TextBlock>("TopUpdated").Parent!=Control<Grid>("WeekInfo") || Control<TextBlock>("TopUpdated").Visibility!=Visibility.Visible)
            throw new Exception("Weekly-only update time not moved to the visible row.");
        SavePreview(Path.Combine(folder,"weekly-only-top.png"));
        window.Top=450;
        var tallDialog=new Window {Width=360,Height=700,Opacity=0,ShowActivated=false,ShowInTaskbar=false,Content=new ScrollViewer {Content=new TextBlock {Text="Scrollable settings",Height=900}}};
        try {
            PositionSettings(tallDialog,700); tallDialog.Show(); tallDialog.UpdateLayout(); PositionSettings(tallDialog); tallDialog.UpdateLayout();
            if (tallDialog.ActualHeight>tallDialog.MaxHeight+1 || (tallDialog.Top<window.Top+window.Height && tallDialog.Top+tallDialog.ActualHeight>window.Top))
                throw new Exception("Tall settings overlaps the widget or ignores available space.");
        } finally { tallDialog.Close(); }
        File.WriteAllText(report,"PASS: dropdown swap semantics, order validity and JSON roundtrip; top/bottom rows and fixed expansion edges; repeated expand/collapse with pinned/unpinned buttons in both languages at 40/100/150 percent; tall settings fit available space without widget overlap. No queries or preference writes. Physical pointer movement and saved-file restart not exercised.\r\n");
    }
    static void WaitForHoverCheck() {
        var frame = new DispatcherFrame();
        var finish = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        finish.Tick += (s,e) => frame.Continue = false;
        finish.Start(); Dispatcher.PushFrame(frame); finish.Stop();
    }
    static void RaiseHover(UIElement target, RoutedEvent routedEvent) {
        target.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent = routedEvent });
    }
    static void CheckHoverBoundary(string report) {
        window.Opacity = 0; window.ShowActivated = false; window.ShowInTaskbar = false;
        window.Left = -10000; window.Top = -10000;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        snapshot = new Snapshot { Time = DateTimeOffset.UtcNow, Live = true,
            Hour = new Limit { Remaining = 65, Reset = now+18000 },
            Week = new Limit { Remaining = 72, Reset = now+604800 } };
        window.Show(); window.UpdateLayout();
        foreach (string name in new[] {"SettingsButton","LockButton","RefreshButton","CloseButton"}) {
            var button = Control<Button>(name); button.ApplyTemplate();
            var templateRoot = VisualTreeHelper.GetChild(button,0) as Grid;
            if (templateRoot == null || ((SolidColorBrush)templateRoot.Background).Color.A != 0)
                throw new Exception("Toolbar button background is not transparent.");
            foreach (Trigger trigger in button.Style.Triggers) {
                foreach (Setter setter in trigger.Setters)
                    if (setter.Property == System.Windows.Controls.Control.BackgroundProperty)
                        throw new Exception("Button state changes its background.");
            }
            var canvas = (Canvas)((Viewbox)button.Content).Child;
            foreach (System.Windows.Shapes.Shape shape in canvas.Children)
                if (!BindingOperations.IsDataBound(shape,System.Windows.Shapes.Shape.StrokeProperty)) throw new Exception("Icon stroke is not bound to button highlight.");
        }
        ToggleLock(); window.UpdateLayout();
        if (Control<Button>("LockButton").Foreground.ToString() != ((Brush)window.Resources["ToolbarHover"]).ToString()
            || Control<System.Windows.Shapes.Path>("LockGlyph").Stroke.ToString() != Control<Button>("LockButton").Foreground.ToString())
            throw new Exception("Persistent lock icon highlight failed.");
        ToggleLock(); collapseTimer.Stop();
        foreach (bool pinned in new[] {false,true}) {
            SetPinnedIcons(pinned);
            foreach (double scale in new[] {0.4,1.0}) {
                SetZoom(scale); SetExpanded(false); collapseTimer.Stop();
                RaiseHover(window,System.Windows.Input.Mouse.MouseEnterEvent);
                RaiseHover(Control<Grid>("Toolbar"),System.Windows.Input.Mouse.MouseEnterEvent);
                if (expanded) throw new Exception("Toolbar hover expanded collapsed window.");
                RaiseHover(Control<Grid>("QuotaArea"),System.Windows.Input.Mouse.MouseEnterEvent);
                RaiseHover(Control<Grid>("QuotaArea"),System.Windows.Input.Mouse.MouseLeaveEvent);
                RaiseHover(Control<Grid>("Toolbar"),System.Windows.Input.Mouse.MouseEnterEvent);
                if (collapseTimer.IsEnabled) throw new Exception("Content-to-toolbar transition armed collapse.");
                WaitForHoverCheck();
                if (!expanded || Control<Grid>("Toolbar").Visibility != Visibility.Visible) throw new Exception("Toolbar disappeared while staying inside window.");
                RaiseHover(window,System.Windows.Input.Mouse.MouseLeaveEvent);
                if (!collapseTimer.IsEnabled) throw new Exception("Window exit did not arm collapse.");
                RaiseHover(window,System.Windows.Input.Mouse.MouseEnterEvent);
                if (collapseTimer.IsEnabled) throw new Exception("Window reentry did not cancel collapse.");
                RaiseHover(window,System.Windows.Input.Mouse.MouseLeaveEvent); WaitForHoverCheck();
                if (expanded || (Control<Grid>("Toolbar").Visibility == Visibility.Visible) != pinned) throw new Exception("Window exit collapse / toolbar preference failed.");
            }
        }
        SetZoom(1); SetExpanded(false);
        SavePreview(Path.Combine(Path.GetDirectoryName(report),"buttons-normal.png"));
        ToggleLock(); SavePreview(Path.Combine(Path.GetDirectoryName(report),"buttons-locked.png"));
        File.WriteAllText(report,"PASS: transparent toolbar templates, icon foreground bindings and persistent lock highlight; routed content-to-toolbar transitions remain expanded beyond the collapse delay; whole-window exit collapses; reentry cancels collapse; collapsed toolbar hover does not expand. Checked pinned and unpinned at 40% and 100% in a realized transparent window. No account queries or preference writes. Physical pointer / pressed-state interaction not exercised.\r\n");
    }
    static void PositionSettings(Window dialog, double estimatedHeight = 0) {
        const double gap = 8;
        var area = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(window).Handle).WorkingArea;
        var source = PresentationSource.FromVisual(window);
        var transform = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(area.Left,area.Top));
        var bottomRight = transform.Transform(new Point(area.Right,area.Bottom));
        double height = dialog.ActualHeight > 0 ? dialog.ActualHeight : estimatedHeight;
        dialog.Left = Math.Max(topLeft.X,Math.Min(bottomRight.X-dialog.Width,window.Left+(window.Width-dialog.Width)/2));
        double above = Math.Max(0,window.Top-gap-topLeft.Y);
        double below = Math.Max(0,bottomRight.Y-window.Top-window.Height-gap);
        bool putAbove = height <= above || (height > below && above >= below);
        // Keep the configured height when the screen can fit it; widget position must not shrink settings.
        dialog.MaxHeight = Math.Max(80,bottomRight.Y-topLeft.Y-2*gap);
        height = Math.Min(height,dialog.MaxHeight);
        double preferred=putAbove ? window.Top-height-gap : window.Top+window.Height+gap;
        dialog.Top=Math.Max(topLeft.Y+gap,Math.Min(bottomRight.Y-height-gap,preferred));
    }
    static void CheckRealizedLayout(string report) {
        // Realize the native window without displaying it or querying an account.
        window.Opacity = 0; window.ShowActivated = false; window.ShowInTaskbar = false;
        window.Left = 100; window.Top = 100;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        snapshot = new Snapshot { Time = DateTimeOffset.UtcNow, Live = true,
            Hour = new Limit { Remaining = 65, Reset = now + 18000 },
            Week = new Limit { Remaining = 72, Reset = now + 604800 } };
        window.Show(); window.UpdateLayout();
        SetZoom(1); SetExpanded(true); window.UpdateLayout(); Render(); window.UpdateLayout();
        double detailHeight = window.Height;
        SetExpanded(false); window.UpdateLayout(); Render(); window.UpdateLayout();
        double compactHeight = window.Height;
        foreach (bool details in new[] {false,true}) {
            SetExpanded(details); window.UpdateLayout();
            var lastBar = Control<ProgressBar>(details ? "WeekTimeBar" : "CompactWeekTimeBar");
            double barBottom = lastBar.TranslatePoint(new Point(0,lastBar.ActualHeight),window).Y;
            foreach (string name in new[] {"SettingsButton","LockButton","RefreshButton","CloseButton"}) {
                var icon = (Viewbox)Control<Button>(name).Content;
                double iconTop = icon.TranslatePoint(new Point(0,0),window).Y;
                double iconBottom = icon.TranslatePoint(new Point(0,icon.ActualHeight),window).Y;
                double topGap = iconTop-barBottom, bottomGap = window.ActualHeight-1-iconBottom;
                if (topGap < -0.1 || topGap > 2.2 || bottomGap < -0.1 || bottomGap > 2.2)
                    throw new Exception(string.Format(CultureInfo.InvariantCulture,"Icon spacing incorrect: {0}, details={1}, top={2}, bottom={3}",name,details,topGap,bottomGap));
            }
        }
        var observations = new List<string>();
        foreach (bool en in new[] {false,true}) {
            SelectLanguage(en);
            foreach (bool pin in new[] {false,true}) {
                locked = false; SetExpanded(false); window.UpdateLayout();
                if (pin) ToggleLock(); else SetExpanded(true);
                foreach (double scale in new[] {0.4,0.75,0.8,1.0,1.5,0.4,0.8,1.0}) {
                    SetZoom(scale);
                    double expected = (scale < 0.8 ? compactHeight : detailHeight) * scale;
                    observations.Add(string.Format(CultureInfo.InvariantCulture,"{0}, lock={1}, scale={2}, height={3}, expected={4}",en,pin,scale,window.Height,expected));
                    if (Math.Abs(window.Height - expected) > 2)
                        throw new Exception("Immediate height incorrect: " + observations.Last());
                    window.UpdateLayout();
                    if (Math.Abs(window.ActualHeight - expected) > 2)
                        throw new Exception("Realized height incorrect: " + observations.Last());
                    foreach (string name in new[] {"SettingsButton","LockButton","RefreshButton","CloseButton"}) {
                        var icon = (Viewbox)Control<Button>(name).Content;
                        Rect visualBounds = icon.TransformToAncestor(window).TransformBounds(new Rect(0,0,icon.ActualWidth,icon.ActualHeight));
                        double expectedIcon = 13*Math.Min(1,scale);
                        if (Math.Abs(visualBounds.Width-expectedIcon) > 1 || Math.Abs(visualBounds.Height-expectedIcon) > 1)
                            throw new Exception("Icon scale cap failed: " + name + " at " + scale);
                    }
                }
                locked = false; SetExpanded(false); window.UpdateLayout();
            }
        }
        locked = false; SetZoom(1); SetExpanded(false);
        double stableBottom = window.Top+window.Height;
        SetPinnedIcons(false); window.UpdateLayout();
        if (Control<Grid>("Toolbar").Visibility != Visibility.Collapsed || Control<Grid>("LayoutRoot").RowDefinitions[1].Height.Value != 0)
            throw new Exception("Unpinned collapsed toolbar still occupies space.");
        if (Math.Abs(window.Top+window.Height-stableBottom) > 0.1) throw new Exception("Unpinning moved the bottom anchor.");
        double collapsedWithoutIcons = window.Height;
        SetExpanded(true); window.UpdateLayout();
        if (Control<Grid>("Toolbar").Visibility != Visibility.Visible || window.Height <= collapsedWithoutIcons || Math.Abs(window.Top+window.Height-stableBottom) > 0.1)
            throw new Exception("Unpinned toolbar did not expand upward with a fixed bottom.");
        double anchoredIconBottom = Control<Button>("SettingsButton").TranslatePoint(new Point(0,17),window).Y+window.Top;
        SetExpanded(false); window.UpdateLayout(); SetExpanded(true); window.UpdateLayout();
        if (Math.Abs(Control<Button>("SettingsButton").TranslatePoint(new Point(0,17),window).Y+window.Top-anchoredIconBottom) > 0.1)
            throw new Exception("Icon position changed between expansions.");
        SetZoom(0.4); SetExpanded(false); window.UpdateLayout();
        if (Control<Grid>("Toolbar").Visibility != Visibility.Collapsed) throw new Exception("Mini collapsed toolbar not hidden.");
        SetExpanded(true); window.UpdateLayout();
        if (Control<Grid>("Toolbar").Visibility != Visibility.Visible) throw new Exception("Mini hover cannot restore toolbar.");
        SetPinnedIcons(true); SetZoom(1); SetExpanded(false);
        window.Top = 450;
        var settingsTest = new Window { Width = 360, Height = 240, Opacity = 0, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        settingsWindow = settingsTest;
        try {
            settingsTest.Show(); settingsTest.UpdateLayout(); PositionSettings(settingsTest);
            if (settingsTest.Top+settingsTest.ActualHeight > window.Top-7) throw new Exception("Settings overlaps owner above.");
            SetExpanded(true); SetZoom(1.5);
            if (settingsTest.Top+settingsTest.ActualHeight > window.Top-7) throw new Exception("Settings does not follow owner resizing.");
            window.Top = 0; PositionSettings(settingsTest);
            if (settingsTest.Top < window.Top+window.Height+7) throw new Exception("Settings below fallback overlaps owner.");
        } finally { settingsWindow = null; settingsTest.Close(); }
        window.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,-120) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
        if (!zoomPaintTimer.IsEnabled) throw new Exception("Wheel redraw burst did not start.");
        var frame = new DispatcherFrame();
        var finish = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        finish.Tick += (s,e) => frame.Continue = false;
        finish.Start(); Dispatcher.PushFrame(frame); finish.Stop();
        if (zoomPaintTimer.IsEnabled) throw new Exception("Wheel redraw burst did not stop.");
        File.WriteAllText(report,"PASS: realized-window lock expansion and scale transitions update height immediately in both languages; settings above owner, resize following, below fallback; wheel redraw burst starts and stops. No network calls or preference writes. Physical wheel gestures not exercised.\r\n"+string.Join("\r\n",observations));
    }
    static string ThemeFile() { return Path.Combine(Path.GetDirectoryName(PreferenceFile()), "colors.json"); }
    static string ThemeBackupFile() { return Path.Combine(Path.GetDirectoryName(PreferenceFile()),"colors-before-preset.json"); }
    static void LoadTheme() {
        try {
            if (!File.Exists(ThemeFile())) {
                Theme.Colors=Theme.Preset(InkScene.PresetIndex);
                selectedColorCardId="builtin-"+InkScene.PresetIndex;return;
            }
            var saved = new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(ThemeFile()));
            if(saved!=null && saved.ContainsKey("_selectedCardId")) selectedColorCardId=saved["_selectedCardId"] ?? "";
            foreach (string key in Theme.Keys) if (saved != null && saved.ContainsKey(key) && Theme.Valid(saved[key])) Theme.Colors[key] = saved[key];
            UpgradeLegacyContrast();
        } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
    }
    static void UpgradeLegacyContrast() {
        if(string.Equals(Theme.Colors["track"],"#303640",StringComparison.OrdinalIgnoreCase)) Theme.Colors["track"]="#414B5B";
        if(string.Equals(Theme.Colors["barBorder"],"#15181E",StringComparison.OrdinalIgnoreCase)) Theme.Colors["barBorder"]="#77859B";
    }
    static IEnumerable<DependencyObject> Children(DependencyObject root) {
        for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {
            var child = VisualTreeHelper.GetChild(root,i); yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
    static void ApplyTheme() {
        var iconColor = ((SolidColorBrush)Theme.Brush("icons")).Color;
        window.Resources["ToolbarNormal"] = new SolidColorBrush(iconColor);
        window.Resources["ToolbarHover"] = LightenIcon(iconColor,0.55);
        window.Resources["ToolbarPressed"] = LightenIcon(iconColor,0.85);
        var root = Control<Border>("Root");
        root.Background = Theme.Brush("background"); root.BorderBrush = Theme.Brush("windowBorder");
        foreach (var child in Children(root)) {
            var text = child as TextBlock;
            if (text != null) text.Foreground = Theme.Brush(text.Name == "TitleLabel" ? "title" : "label");
            var shape = child as System.Windows.Shapes.Shape;
            if (shape != null && !BindingOperations.IsDataBound(shape,System.Windows.Shapes.Shape.StrokeProperty)) shape.Stroke = Theme.Brush("icons");
            var segmented = child as SegmentedVisual;
            if (segmented != null) segmented.InvalidateVisual();
        }
        foreach (string name in new[] { "HourValue", "WeekValue" }) Control<TextBlock>(name).Foreground = Theme.Brush("valueText");
        foreach (string name in new[] { "HourReset", "WeekReset" }) Control<TextBlock>(name).Foreground = Theme.Brush("timeText");

        UpdateSettingsHint();
        Render();
        string selected=ResolveSelectedColorCard();var selectedCard=AvailableColorCards().FirstOrDefault(card=>card.Id==selected);
        ScifiTheme.ApplyPreset(window,selectedCard==null?-1:selectedCard.Builtin,checking);
    }
    static string PromptColorCardName(Window owner) {
        var dialog=new Window {Owner=owner,Title=Text("保存为色卡","Save as color card"),Width=320,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,Background=owner.Background,Foreground=SettingsBrush("Text"),FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=12,Resources=SettingsStyles()};
        var body=new StackPanel {Margin=new Thickness(16)};dialog.Content=body;
        body.Children.Add(new TextBlock {Text=Text("色卡名称","Card name"),Margin=new Thickness(0,0,0,8)});
        var input=new TextBox {MaxLength=48,Padding=new Thickness(8,6,8,6)};body.Children.Add(input);
        var error=new TextBlock {Foreground=Brushes.LightSalmon,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};body.Children.Add(error);
        var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};body.Children.Add(actions);
        var cancel=new Button {Content=Text("取消","Cancel"),IsCancel=true,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,0)};
        var save=new Button {Content=Text("保存","Save"),IsDefault=true,Padding=new Thickness(10,5,10,5)};actions.Children.Add(cancel);actions.Children.Add(save);
        string result=null;cancel.Click+=(s,e)=>dialog.Close();
        save.Click+=(s,e)=>{
            string name=input.Text.Trim();
            if(name.Length==0){error.Text=Text("请输入名称。","Enter a name.");return;}
            if(ColorCardNameExists(name)){error.Text=Text("名称已存在，请换一个名称。","That name already exists. Choose another.");return;}
            result=name;dialog.Close();
        };
        dialog.ContentRendered+=(s,e)=>input.Focus();dialog.ShowDialog();return result;
    }
    static bool OpenAppearance(Dictionary<string,string> presetBackup=null, bool guide=false) {
        if (appearanceOpen) return false;
        appearanceOpen = true; collapseTimer.Stop(); SetExpanded(true);
        var original = new Dictionary<string,string>(Theme.Colors);
        string originalCardId=selectedColorCardId;
        bool accepted = false;
        bool presetApplied=presetBackup!=null;
        var dialog = new Window { Owner = checking ? null : (settingsWindow ?? window), Title = Text("外观设置", "Appearance"), Width = 470, Height = 610,
            MinWidth = 430, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = true,
            Background = SettingsBrush("Background"), Foreground = SettingsBrush("Text"), FontSize = 13 };
        dialog.Resources=SettingsStyles();
        var panel = new DockPanel { Margin = new Thickness(16) }; dialog.Content = panel;
        var help = new TextBlock { Text = Text("点击色块选色，或输入 #RRGGBB / #AARRGGBB。修改即时预览。", "Click a swatch or enter #RRGGBB / #AARRGGBB. Changes preview immediately."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) };
        DockPanel.SetDock(help,Dock.Top); panel.Children.Add(help);
        var presetsRow=new Grid {Margin=new Thickness(0,0,0,12)};
        presetsRow.ColumnDefinitions.Add(new ColumnDefinition());presetsRow.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var presets=new ComboBox {Margin=new Thickness(0,0,8,0)};
        presets.Items.Add(Text("选择配色预设…","Choose a color preset…"));
        for(int i=0;i<Theme.PresetCount;i++) presets.Items.Add(Theme.PresetName(i,english));
        presets.SelectedIndex=0;presetsRow.Children.Add(presets);
        var restore=new Button {Content=Text("恢复上次配色","Restore previous"),Padding=new Thickness(8,5,8,5)};
        Grid.SetColumn(restore,1);presetsRow.Children.Add(restore);DockPanel.SetDock(presetsRow,Dock.Top);panel.Children.Add(presetsRow);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        DockPanel.SetDock(actions,Dock.Bottom); panel.Children.Add(actions);
        var reset = new Button { Content = Text("恢复默认", "Defaults"), Padding = new Thickness(10,5,10,5), Margin = new Thickness(0,0,8,0) };
        var cancel = new Button { Content = Text("取消", "Cancel"), IsCancel = true, Padding = new Thickness(10,5,10,5), Margin = new Thickness(0,0,8,0) };
        var save = new Button { Content = Text("保存", "Save"), IsDefault = true, Padding = new Thickness(10,5,10,5) };
        var saveAsCard=new Button {Content=Text("另存为色卡…","Save as card…"),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,0)};
        actions.Children.Add(reset); actions.Children.Add(cancel);actions.Children.Add(saveAsCard); actions.Children.Add(save);
        var list = new StackPanel(); panel.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var editors = new Dictionary<string,TextBox>();
        string[][] groups={new[]{"quotaGreen","quotaYellow","quotaOrange","quotaRed"},new[]{"timeGreen","timeYellow","timeOrange","timeRed"},new[]{"title","label","valueText","timeText","caption"},new[]{"background","windowBorder","barBorder","track"},new[]{"ticks","weekMarker","icons","status","alert","failure"}};
        string[] headings=english ? new[]{"Quota bar","Countdown bar","Text","Borders and background","Ticks and indicators"} : new[]{"额度条","倒计时条","文字","边框与背景","刻度与提示"};
        for(int groupIndex=0;groupIndex<groups.Length;groupIndex++) {
            list.Children.Add(new TextBlock {Text=headings[groupIndex],FontWeight=FontWeights.Bold,FontSize=14,Foreground=SettingsBrush("Secondary"),Margin=new Thickness(0,groupIndex==0 ? 0 : 14,0,8)});
            foreach(string key in groups[groupIndex]) {
            int i=Array.IndexOf(Theme.Keys,key);
            var row = new Grid { Margin = new Thickness(0,0,0,8) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
            var label = new TextBlock { Text = english ? Theme.English[i] : Theme.Chinese[i], VerticalAlignment = VerticalAlignment.Center }; row.Children.Add(label);
            var swatch = new Button { Background = Theme.Brush(key),Tag="OfficeSwatch", Margin = new Thickness(0,0,8,0), ToolTip = Text("选择颜色", "Choose color") }; Grid.SetColumn(swatch,1); row.Children.Add(swatch);
            var edit = new TextBox { Text = Theme.Colors[key], Padding = new Thickness(5), VerticalContentAlignment = VerticalAlignment.Center }; Grid.SetColumn(edit,2); row.Children.Add(edit); editors[key] = edit;
            edit.TextChanged += (s,e) => {
                bool valid = Theme.Valid(edit.Text); edit.BorderBrush = valid ? Brushes.Gray : Brushes.OrangeRed;
                save.IsEnabled=saveAsCard.IsEnabled=editors.Values.All(t => Theme.Valid(t.Text));
                if (valid) { Theme.Colors[key] = edit.Text.ToUpperInvariant(); swatch.Background = Theme.Brush(key); ApplyTheme(); }
            };
            swatch.Click += (s,e) => {
                var color = (Color)ColorConverter.ConvertFromString(Theme.Colors[key]);
                using (var picker = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(color.R,color.G,color.B) }) {
                    if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK) edit.Text = string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}",color.A,picker.Color.R,picker.Color.G,picker.Color.B);
                }
            };
            list.Children.Add(row);
            }
        }
        reset.Click += (s,e) => { var defaults = Theme.Defaults(); foreach (string key in Theme.Keys) editors[key].Text = defaults[key]; };
        presets.SelectionChanged+=(s,e)=>{
            if(presets.SelectedIndex<1) return;
            var colors=Theme.Preset(presets.SelectedIndex-1);
            foreach(string key in Theme.Keys) editors[key].Text=colors[key];
            presetApplied=true;
        };
        restore.Click+=(s,e)=>{
            var previous=new Dictionary<string,string>(original);
            try {
                if(!checking && File.Exists(ThemeBackupFile())) {
                    var saved=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(ThemeBackupFile()));
                    foreach(string key in Theme.Keys) if(saved!=null && saved.ContainsKey(key) && Theme.Valid(saved[key])) previous[key]=saved[key];
                }
            } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(ArgumentException) {}
            foreach(string key in Theme.Keys) editors[key].Text=previous[key];
            presets.SelectedIndex=0;presetApplied=false;
        };
        cancel.Click += (s,e) => dialog.Close();
        save.Click += (s,e) => {
            try {
                if (!checking) {
                    SaveColors(presetBackup ?? original,presetApplied);
                }
                accepted = true; dialog.Close();
            } catch (Exception ex) { MessageBox.Show(dialog,Text("颜色设置无法保存：", "Could not save colors: ") + ex.Message); }
        };
        saveAsCard.Click+=(s,e)=>{
            var duplicate=MatchingColorCard(Theme.Colors);
            if(duplicate!=null) {MessageBox.Show(dialog,Text("该配色已存在：","These colors already exist: ")+ColorCardName(duplicate));return;}
            string name=PromptColorCardName(dialog);if(name==null) return;
            try {AddColorCard(name,Theme.Colors);}
            catch(Exception ex){MessageBox.Show(dialog,Text("色卡无法保存：","Could not save color card: ")+ex.Message);return;}
            try {SaveColors(presetBackup ?? original,presetApplied);accepted=true;dialog.Close();}
            catch(Exception ex){MessageBox.Show(dialog,Text("色卡已保存，但当前配色未能保存。可再次点击保存：","Card saved, but active colors could not be saved. You can try Save again: ")+ex.Message);}
        };
        try {
            if (checking) {
                if(list.Children.OfType<TextBlock>().Count()!=5 || editors.Count!=Theme.Keys.Length) throw new Exception("Color groups incomplete.");
                for(int i=1;i<=4;i++) {
                    presets.SelectedIndex=i;
                    if(Theme.Keys.Any(key=>Theme.Colors[key]!=Theme.Preset(i-1)[key])) throw new Exception("Preset incomplete.");
                }
                restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(Theme.Keys.Any(key=>Theme.Colors[key]!=original[key])) throw new Exception("Custom theme restore failed.");
                editors["quotaGreen"].Text = "#123456";
                if (Theme.Colors["quotaGreen"] != "#123456") throw new Exception("Color preview failed.");
                editors["quotaGreen"].Text = "invalid";
                if (save.IsEnabled || Theme.Colors["quotaGreen"] != "#123456") throw new Exception("Invalid color handling failed.");
                reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!save.IsEnabled || Theme.Colors["quotaGreen"] != Theme.Defaults()["quotaGreen"]) throw new Exception("Color defaults failed.");
                var decoded = new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(new JavaScriptSerializer().Serialize(Theme.Colors));
                if (Theme.Keys.Any(key => decoded[key] != Theme.Colors[key])) throw new Exception("Color serialization failed.");
                editors["background"].Text = "#FF223344";
                if (Control<Border>("Root").Background.ToString() != "#FF223344") throw new Exception("Background preview failed.");
                if(settingsPreviewPath!=null) SavePanelPreview(list,dialog.Background,438,Path.Combine(Path.GetDirectoryName(settingsPreviewPath),english ? "color-groups-en.png" : "color-groups-zh.png"));
            } else dialog.ShowDialog();
        }
        finally { if (!accepted) {Theme.Colors = original;selectedColorCardId=originalCardId;} appearanceOpen = false; ApplyTheme(); if (!window.IsMouseOver) collapseTimer.Start(); }
        return accepted;
    }
    static void SavePanelPreview(FrameworkElement panel,Brush background,double width,string path) {
        var parent=panel.Parent as Panel;int index=parent==null ? -1 : parent.Children.IndexOf(panel);
        var owner=panel.Parent as ContentControl;
        if(parent!=null) parent.Children.Remove(panel);else if(owner!=null) owner.Content=null;
        double previousWidth=panel.Width;panel.Width=width;panel.InvalidateMeasure();
        try {
        panel.Measure(new Size(width,double.PositiveInfinity));panel.Arrange(new Rect(0,0,width,panel.DesiredSize.Height));panel.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width+32),(int)Math.Ceiling(panel.DesiredSize.Height+32),96,96,PixelFormats.Pbgra32);
        var drawing=new DrawingVisual();using(var context=drawing.RenderOpen()){context.DrawRectangle(background,null,new Rect(0,0,bitmap.PixelWidth,bitmap.PixelHeight));context.PushTransform(new TranslateTransform(16,16));context.DrawRectangle(new VisualBrush(panel){AutoLayoutContent=false,Stretch=Stretch.None,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top},null,new Rect(panel.RenderSize));}
        bitmap.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path)) encoder.Save(stream);
        } finally {panel.Width=previousWidth;if(parent!=null) parent.Children.Insert(index,panel);else if(owner!=null) owner.Content=panel;}
    }
    static Brush LightenIcon(Color color, double amount) {
        var background=((SolidColorBrush)Theme.Brush("background")).Color;
        double target=(background.R*0.2126+background.G*0.7152+background.B*0.0722)>160 ? 0 : 255;
        return new SolidColorBrush(Color.FromArgb(color.A,
            (byte)Math.Round(color.R+(target-color.R)*amount),
            (byte)Math.Round(color.G+(target-color.G)*amount),
            (byte)Math.Round(color.B+(target-color.B)*amount)));
    }
    static string PreferenceFile() {
        string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrEmpty(local)) local = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), "AppData", "Local");
        return Path.Combine(local, "CodexQuota", "language.txt");
    }
    static void LoadLanguage() {
        try { english = File.Exists(PreferenceFile()) && File.ReadAllText(PreferenceFile()).Trim() == "en"; }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    static void SaveLanguage() {
        if (checking) return;
        try {
            string path = PreferenceFile(); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, english ? "en" : "zh");
        } catch (IOException) { Control<Button>("SettingsButton").ToolTip = Text("语言偏好无法保存", "Could not save language preference"); }
        catch (UnauthorizedAccessException) { Control<Button>("SettingsButton").ToolTip = Text("语言偏好无法保存", "Could not save language preference"); }
    }
    static void ApplyLanguage() {
        window.Title = Text("额度", "Usage");
        Control<TextBlock>("TitleLabel").Text = window.Title;
        Control<TextBlock>("HourLabel").Text = Text("5 小时剩余", "5-hour remaining");
        Control<TextBlock>("WeekLabel").Text = Text("本周剩余", "Weekly remaining");
        UpdateSettingsHint();

        Control<TextBlock>("CompactHourLabel").Text = Text("五", "5h");
        Control<TextBlock>("CompactWeekLabel").Text = Text("周", "Wk");

        Control<Button>("RefreshButton").ToolTip = Text("主动查询最新账户额度", "Query current account usage");
        Control<Button>("CloseButton").ToolTip = Text("关闭悬浮窗", "Close widget");
        UpdateLock();
        Render();
    }
    static void UpdateLock() {
        Control<Button>("LockButton").ToolTip = locked ? Text("解除锁定", "Unlock expanded view") : Text("锁定展开", "Lock expanded view");
        Control<Button>("LockButton").Tag = locked ? "locked" : "";
        Control<System.Windows.Shapes.Path>("LockGlyph").Data = Geometry.Parse(locked
            ? "M 6,10 V 7 A 4,4 0 0 1 14,7 V 10 M 3,10 H 17 V 17 H 3 Z"
            : "M 6,10 V 7 A 4,4 0 0 1 14,7 M 3,10 H 17 V 17 H 3 Z");
    }
    static void ToggleLock() {
        locked = !locked; UpdateLock();
        if (locked) { collapseTimer.Stop(); SetExpanded(true); }
        else if (Control<Grid>("QuotaArea").IsMouseOver) { collapseTimer.Stop(); SetExpanded(true); }
        else collapseTimer.Start();
    }
    static void SetExpanded(bool value) {
        if (!value && locked) return;
        if (expanded == value) return;
        expanded = value;
        Render();
    }
    static void NormalizeToolbarIcons() {
        foreach (string name in new[] { "SettingsButton", "LockButton", "RefreshButton", "CloseButton" }) {
            var button = Control<Button>(name);
            var view = (Viewbox)button.Content;
            var canvas = (Canvas)view.Child;
            Rect bounds = Rect.Empty;
            foreach (System.Windows.Shapes.Shape shape in canvas.Children) {
                Geometry geometry;
                var path = shape as System.Windows.Shapes.Path;
                if (path != null) geometry = path.Data;
                else geometry = new EllipseGeometry(new Rect(Canvas.GetLeft(shape),Canvas.GetTop(shape),shape.Width,shape.Height));
                bounds.Union(geometry.GetRenderBounds(new Pen(Brushes.White,shape.StrokeThickness)));
            }
            canvas.Width = bounds.Width; canvas.Height = bounds.Height;
            foreach (System.Windows.Shapes.Shape shape in canvas.Children)
                shape.RenderTransform = new TranslateTransform(-bounds.Left,-bounds.Top);
            button.Height = 17; button.VerticalContentAlignment = VerticalAlignment.Center;
            view.Width = view.Height = 13; view.Stretch = Stretch.Fill;
        }
    }
    static void ConfigureInteraction() {
        NormalizeToolbarIcons();
        zoomPaintTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        zoomPaintTimer.Tick += (s,e) => {
            if (DateTime.UtcNow >= zoomPaintUntil) { zoomPaintTimer.Stop(); return; }
            Render();
        };
        collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        collapseTimer.Tick += (s, e) => {
            collapseTimer.Stop();
            if (!locked && !appearanceOpen && !settingsOpen && !dragging && !window.IsMouseOver) SetExpanded(false);
        };
        // Expansion belongs to the quota area; collapse belongs to leaving the whole window.
        // Keeping these boundaries separate prevents hidden buttons moving a stationary
        // pointer back into the quota area and repeatedly reopening the widget.
        window.MouseEnter += (s,e) => collapseTimer.Stop();
        window.MouseLeave += (s,e) => { if (!dragging) collapseTimer.Start(); };
        Control<Grid>("QuotaArea").MouseEnter += (s, e) => { collapseTimer.Stop(); SetExpanded(true); };
        window.PreviewMouseWheel += (s, e) => {
            ChangeZoom(e.Delta);
            zoomPaintUntil = DateTime.UtcNow.AddMilliseconds(250); zoomPaintTimer.Start();
            e.Handled = true;
        };
        Control<Button>("LockButton").Click += (s, e) => ToggleLock();
        Control<Button>("SettingsButton").Click += (s, e) => OpenSettings();
        Control<Button>("CloseButton").Click += (s,e) => window.Close();
        window.MouseLeftButtonDown += (s, e) => {
            var source = e.OriginalSource as DependencyObject;
            while (source != null) {
                if (source is Button || source is MenuItem) return;
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            dragging = true; collapseTimer.Stop();
            try { window.DragMove(); }
            finally { dragging = false; if (!window.IsMouseOver) collapseTimer.Start(); }
        };
        window.Closed += (s, e) => { collapseTimer.Stop(); zoomPaintTimer.Stop(); };
        ApplyLanguage(); ResizeWidget();
    }
    static string FindCodex() {
        string arch=Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432")??Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")??"AMD64";
        return FindCodexAt(Environment.GetEnvironmentVariable("LOCALAPPDATA"),Environment.GetEnvironmentVariable("PATH"),arch,File.Exists);
    }
    static string FindCodexAt(string local,string paths,string architecture,Func<string,bool> exists) {
        if (!string.IsNullOrEmpty(local)) {
            string root = Path.Combine(local, "OpenAI", "Codex", "bin");
            if (Directory.Exists(root)) {
                try {
                    string found = Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories)
                        .OrderByDescending(p => File.GetLastWriteTimeUtc(p)).FirstOrDefault();
                    if (found != null) return found;
                } catch(IOException) {} catch(UnauthorizedAccessException) {}
            }
        }
        foreach (string folder in (paths ?? "").Split(';')) {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            try {
                foreach(string file in CodexCommandCandidates(folder.Trim('"'),architecture)) if(exists(file))return file;
            } catch(ArgumentException) {}
        }
        throw new Exception("找不到 Codex 查询程序");
    }
    static IEnumerable<string> CodexCommandCandidates(string folder,string architecture) {
        yield return Path.Combine(folder,"codex.exe");
        string arch=architecture.IndexOf("ARM64",StringComparison.OrdinalIgnoreCase)>=0 ? "arm64" : "x64";
        string triple=arch=="arm64" ? "aarch64-pc-windows-msvc" : "x86_64-pc-windows-msvc";
        string modules=Path.Combine(folder,"node_modules","@openai"),package="codex-win32-"+arch;
        foreach(string vendor in new[]{Path.Combine(modules,package,"vendor"),Path.Combine(modules,"codex","node_modules","@openai",package,"vendor"),Path.Combine(modules,"codex","vendor")}) {
            yield return Path.Combine(vendor,triple,"bin","codex.exe");
            yield return Path.Combine(vendor,triple,"codex","codex.exe");
        }
    }
    static Dictionary<string, object> RpcReply(Process process, JavaScriptSerializer json, int id, DateTime deadline) {
        while (DateTime.UtcNow < deadline) {
            var read = process.StandardOutput.ReadLineAsync();
            if (!read.Wait(Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds)))
                throw new TimeoutException("额度查询超时");
            if (read.Result == null) throw new Exception("Codex 查询连接已结束");
            var reply = Map(json.DeserializeObject(read.Result));
            object replyId = Value(reply, "id");
            if (replyId == null || Convert.ToInt32(replyId) != id) continue;
            if (Value(reply, "error") != null) throw new Exception("Codex 拒绝额度查询，错误代码：" + Value(Map(Value(reply, "error")), "code"));
            return Map(Value(reply, "result"));
        }
        throw new TimeoutException("额度查询超时");
    }
    static Snapshot QueryCodexSnapshot() {
        var info = new ProcessStartInfo(FindCodex(), "app-server --stdio") {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
        };
        using (var process = new Process { StartInfo = info }) {
            process.Start();
            // Drain diagnostics without recording authentication details or server output.
            process.ErrorDataReceived += (s, e) => { };
            process.BeginErrorReadLine();
            try {
                DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                var json = new JavaScriptSerializer { MaxJsonLength = 1048576 };
                process.StandardInput.WriteLine("{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"codex_quota_widget\",\"title\":\"Codex Quota Widget\",\"version\":\"1.2\"}}}");
                process.StandardInput.Flush();
                RpcReply(process, json, 1, deadline);
                process.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
                process.StandardInput.WriteLine("{\"method\":\"account/rateLimits/read\",\"id\":2}");
                process.StandardInput.Flush();
                var result = RpcReply(process, json, 2, deadline);
                var buckets = Map(Value(result, "rateLimitsByLimitId"));
                var limits = Map(Value(buckets, "codex")) ?? Map(Value(result, "rateLimits"));
                if (limits == null) throw new Exception("账户未返回额度信息");
                return ParseWindows(limits, DateTimeOffset.UtcNow, true);
            } finally {
                try { process.StandardInput.Close(); } catch (IOException) { }
                if (!process.WaitForExit(2000)) { process.Kill(); process.WaitForExit(2000); }
            }
        }
    }
    static Snapshot NewerSnapshot(Snapshot current, Snapshot candidate) {
        if (candidate == null) return current;
        if(current!=null && current.Provider!=candidate.Provider) throw new FormatException("Quota provider mismatch.");
        // A successful account read is authoritative, including absent quota windows.
        if (current == null || candidate.Live) return candidate;
        var baseline = current.Live ? current : current.LiveBaseline;
        if (baseline != null && candidate.Time <= baseline.Time) return current;
        DateTimeOffset hourTime = current.HourTime == default(DateTimeOffset) ? current.Time : current.HourTime;
        DateTimeOffset weekTime = current.WeekTime == default(DateTimeOffset) ? current.Time : current.WeekTime;
        DateTimeOffset candidateHourTime = candidate.HourTime == default(DateTimeOffset) ? candidate.Time : candidate.HourTime;
        DateTimeOffset candidateWeekTime = candidate.WeekTime == default(DateTimeOffset) ? candidate.Time : candidate.WeekTime;
        if (baseline != null) {
            if (hourTime < baseline.Time) hourTime = baseline.Time;
            if (weekTime < baseline.Time) weekTime = baseline.Time;
        }
        bool hour = AcceptLocalLimit(current.Hour, candidate.Hour, hourTime, candidateHourTime,
            baseline == null || baseline.Hour != null);
        bool week = AcceptLocalLimit(current.Week, candidate.Week, weekTime, candidateWeekTime,
            baseline == null || baseline.Week != null);
        if (!hour && !week) return current;
        return new Snapshot {
            Time = candidate.Time, Live = false, LiveBaseline = baseline,
            Provider = candidate.Provider, Source = candidate.Source,
            LoginVersion=current.LoginVersion,
            Hour = hour ? candidate.Hour : current.Hour, Week = week ? candidate.Week : current.Week,
            HourTime = hour ? candidateHourTime : hourTime, WeekTime = week ? candidateWeekTime : weekTime
        };
    }
    static bool AcceptLocalLimit(Limit current, Limit candidate, DateTimeOffset observed,
        DateTimeOffset timestamp, bool available) {
        if (!available || candidate == null || timestamp <= observed) return false;
        if (current == null) return true;
        // Log timestamps can advance while their embedded rate-limit data is cached.
        // Account and cached records can round the same reset boundary differently.
        // A one-second drift is not a new 5-hour/week cycle.
        if (current.Reset > 0 && candidate.Reset == 0) return false;
        if (current.Reset > 0 && candidate.Reset < current.Reset - 2) return false;
        if (candidate.Reset > current.Reset + 2) return true;
        return candidate.Remaining <= current.Remaining;
    }
    static void CheckSnapshotSelection(string report) {
        var start = DateTimeOffset.FromUnixTimeSeconds(1800000000);
        var live = new Snapshot { Time = start, Live = true,
            Hour = new Limit { Remaining = 100, Reset = 1800018000 },
            Week = new Limit { Remaining = 65, Reset = 1800604800 } };
        var stale = new Snapshot { Time = start.AddSeconds(30),
            Hour = new Limit { Remaining = 65, Reset = 1800000000 },
            Week = new Limit { Remaining = 65, Reset = 1800604800 } };
        var selected = NewerSnapshot(live, stale);
        if (selected.Hour != live.Hour || selected.Hour.Remaining != 100)
            throw new Exception("Old 5-hour cycle replaced active query.");
        stale.Time = start.AddSeconds(60); stale.Week.Remaining = 64;
        selected = NewerSnapshot(selected, stale);
        if (selected.Hour != live.Hour || selected.Week.Remaining != 64)
            throw new Exception("Independent quota window selection failed.");
        var fresh = new Snapshot { Time = start.AddSeconds(45),
            Hour = new Limit { Remaining = 99, Reset = live.Hour.Reset },
            Week = new Limit { Remaining = 63, Reset = live.Week.Reset } };
        selected = NewerSnapshot(selected, fresh);
        if (selected.Hour.Remaining != 99 || selected.Week.Remaining != 64)
            throw new Exception("Per-window timestamps lost during partial update.");
        var cached = new Snapshot { Time = start.AddSeconds(90),
            Hour = new Limit { Remaining = 100, Reset = live.Hour.Reset } };
        if (NewerSnapshot(selected, cached) != selected)
            throw new Exception("Cached same-cycle balance replaced newer usage.");
        fresh.Time = start.AddSeconds(-1); fresh.Hour.Remaining = 98;
        if (NewerSnapshot(selected, fresh) != selected)
            throw new Exception("Pre-query local record accepted.");
        fresh.Time = start.AddSeconds(120); fresh.Hour.Remaining = 98;
        selected = NewerSnapshot(selected, fresh);
        if (selected.Hour.Remaining != 98 || selected.Week.Remaining != 63)
            throw new Exception("Current local data did not resume updates.");
        var nextCycle = new Snapshot { Time = start.AddHours(5),
            Hour = new Limit { Remaining = 100, Reset = live.Hour.Reset + 18000 } };
        selected = NewerSnapshot(selected, nextCycle);
        if (selected.Hour.Remaining != 100 || selected.Week.Remaining != 63)
            throw new Exception("Next cycle or missing-window preservation failed.");
        var missingReset = new Snapshot { Time = start.AddHours(6),
            Hour = new Limit { Remaining = 50, Reset = 0 } };
        if (NewerSnapshot(selected, missingReset) != selected || NewerSnapshot(selected, null) != selected)
            throw new Exception("Incomplete fallback replaced current data.");
        var weekOnly = new Snapshot { Time = start.AddHours(6), Live = true, Week = live.Week };
        selected = NewerSnapshot(selected, weekOnly);
        if (selected != weekOnly || NewerSnapshot(selected, nextCycle).Hour != null)
            throw new Exception("Live missing-window authority failed.");
        nextCycle.Time = start.AddHours(7);
        if (NewerSnapshot(selected, nextCycle).Hour != null)
            throw new Exception("Local record resurrected unavailable live window.");
        File.WriteAllText(report, "PASS: old-cycle rollback (100% to 65%) blocked; independent windows and per-window timestamps; cached same-cycle increase and pre-query records rejected; valid local updates and next-cycle resets accepted; missing/reset-less fallback preserved; authoritative live reads and unavailable quota windows preserved.\r\nDeterministic data-only check. No account queries or preference writes.\r\n");
    }
    static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
    static object Value(Dictionary<string, object> map, string key) {
        object value; return map != null && map.TryGetValue(key, out value) ? value : null;
    }
    static Limit ParseLimit(object value) {
        var map = Map(value);
        object used = Value(map, "used_percent") ?? Value(map, "usedPercent");
        object reset = Value(map, "resets_at") ?? Value(map, "resetsAt");
        if (used == null) return null;
        double usedPercent = Convert.ToDouble(used);
        if (double.IsNaN(usedPercent) || double.IsInfinity(usedPercent)) throw new FormatException("Invalid usage percentage.");
        long resetSeconds = reset == null ? 0 : Convert.ToInt64(reset);
        if (resetSeconds != 0) DateTimeOffset.FromUnixTimeSeconds(resetSeconds);
        return new Limit { Remaining = Math.Max(0, Math.Min(100, 100 - usedPercent)), Reset = resetSeconds };
    }
    static Snapshot ParseWindows(Dictionary<string,object> limits, DateTimeOffset timestamp, bool live) {
        var result = new Snapshot { Time = timestamp, Live = live };
        var parsed=new List<Limit>();
        foreach (string slot in new[] { "primary", "secondary" }) {
            var data = Map(Value(limits,slot));
            var limit = ParseLimit(data); if (limit == null) continue;
            object duration = Value(data,"windowDurationMins") ?? Value(data,"window_minutes");
            if (duration == null) limit.Minutes=slot=="primary" ? 300 : 10080;
            else {
                double minutes=Convert.ToDouble(duration,CultureInfo.InvariantCulture);
                if(double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes<=0 || minutes>527040) throw new FormatException("Invalid quota window duration.");
                limit.Minutes=minutes;
            }
            parsed.Add(limit);
        }
        result.Hour=parsed.FirstOrDefault(item=>item.Minutes==300);
        result.Week=parsed.FirstOrDefault(item=>item.Minutes==10080);
        foreach(var limit in parsed) {
            if(limit==result.Hour || limit==result.Week)continue;
            if(result.Hour==null) result.Hour=limit;else if(result.Week==null)result.Week=limit;
        }
        return result;
    }
    static Snapshot ReadSnapshot() {
        return ReadSnapshotFrom(Environment.GetEnvironmentVariable("USERPROFILE"));
    }
    static Snapshot ReadSnapshotFrom(string profile) {
        string warning, details;
        var result=ReadCodexSnapshotFrom(profile,out warning,out details);
        localReadWarning=warning;diagnostic=details;return result;
    }
    static Snapshot ReadCodexSnapshotFrom(string profile,out string warning,out string details) {
        if (string.IsNullOrEmpty(profile)) profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string home = Path.Combine(profile, ".codex");
        if(profile==Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_HOME"))) home=Environment.GetEnvironmentVariable("CODEX_HOME");
        details = "Profile: " + home;
        var files = new List<FileInfo>();
        int errors = 0;
        warning = "";
        foreach (string folder in new[] { "sessions", "archived_sessions" }) {
            string root = Path.Combine(home, folder);
            if (!Directory.Exists(root)) continue;
            try { files.AddRange(Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).Select(p => new FileInfo(p))); }
            catch (IOException) { errors++; } catch (UnauthorizedAccessException) { errors++; }
        }
        Snapshot latest = null;
        var records = new List<Snapshot>();
        details += "\r\nFiles: " + files.Count;
        var json = new JavaScriptSerializer { MaxJsonLength = 1048576 };
        foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(16)) {
            try {
                using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    long offset = Math.Max(0, stream.Length - 524288);
                    stream.Seek(offset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream)) {
                        if (offset > 0) reader.ReadLine();
                        string line;
                        while ((line = reader.ReadLine()) != null) {
                            if (!line.Contains("\"rate_limits\"")) continue;
                            try {
                                var record = Map(json.DeserializeObject(line));
                                var limits = Map(Value(Map(Value(record, "payload")), "rate_limits"));
                                var id = Value(limits, "limit_id") as string;
                                if (id != null && id != "codex") continue;
                                if (limits == null) continue;
                                var time = DateTimeOffset.Parse((string)Value(record, "timestamp"), CultureInfo.InvariantCulture);
                                var candidate = ParseWindows(limits, time, false);
                                if (candidate.Hour != null || candidate.Week != null) records.Add(candidate);
                            } catch (ArgumentException) { errors++; } catch (FormatException) { errors++; }
                            catch (InvalidCastException) { errors++; } catch (InvalidOperationException) { errors++; }
                            catch (OverflowException) { errors++; }
                        }
                    }
                }
            } catch (IOException) { errors++; } catch (UnauthorizedAccessException) { errors++; }
        }
        // Select each quota window independently; an incomplete newer event must not hide
        // a valid older hourly record in a different file.
        foreach (var record in records.OrderBy(r => r.Time)) latest = NewerSnapshot(latest, record);
        if (errors > 0) warning = "Local read skipped " + errors + " unreadable file(s)/record(s).";
        details += "\r\nValid quota records: " + records.Count + "\r\n" + warning;
        if (latest == null && errors > 0) throw new InvalidDataException(warning);
        return latest;
    }
    static string ResetText(Limit limit) {
        if (limit.Reset == 0) return Text("重置时间未知", "Reset time unavailable");
        var span = DateTimeOffset.FromUnixTimeSeconds(limit.Reset) - DateTimeOffset.UtcNow;
        if (span.TotalSeconds <= 0) return Text("已到重置时间 · 等待更新", "Reset reached · awaiting update");
        return span.TotalDays >= 1 ? string.Format(Text("{0}天 {1}小时后重置", "Resets in {0}d {1}h"), (int)span.TotalDays, span.Hours)
            : string.Format(Text("{0}小时 {1}分后重置", "Resets in {0}h {1}m"), (int)span.TotalHours, span.Minutes);
    }
    static string CompactReset(Limit limit, bool weekly=false) {
        if (limit == null || limit.Reset == 0) return "—";
        return FormatCompactReset(limit.Reset,DateTimeOffset.UtcNow,weekly || limit.Label=="cycle");
    }
    static string FormatCompactReset(long reset,DateTimeOffset now,bool weekly) {
        var span = DateTimeOffset.FromUnixTimeSeconds(reset) - now;
        // Weekly text stays day-sized until the last day; the thin timer uses exact
        // time independently, while the reference arrow advances once per day.
        if(weekly && span.TotalHours>=24) return Math.Ceiling(span.TotalDays).ToString("0",CultureInfo.InvariantCulture)+Text("天","d");
        long minutes = Math.Max(0, (long)Math.Ceiling(span.TotalMinutes));
        if(weekly) return (Math.Min(1439,minutes)/60).ToString()+":"+(Math.Min(1439,minutes)%60).ToString("00");
        return (minutes >= 1440 ? (minutes / 1440).ToString() + "d " : "")
            + ((minutes / 60) % 24).ToString() + ":" + (minutes % 60).ToString("00");
    }
    static readonly Brush Green = new SolidColorBrush(Color.FromRgb(35,122,87));
    static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(246,211,101));
    static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(255,160,85));
    static readonly Brush Red = new SolidColorBrush(Color.FromRgb(255,107,120));
    static Brush QuotaColor(double remaining) {
        return remaining < 10 ? Red : remaining < 20 ? Orange : remaining < 30 ? Yellow : Green;
    }
    static Brush TimeColor(double remaining) {
        return remaining > 60 ? Red : remaining > 40 ? Orange : remaining > 20 ? Yellow : Green;
    }
    static double TimePercent(Limit limit, double hours) {
        return TimePercentAt(limit,hours,DateTimeOffset.UtcNow);
    }
    static double TimePercentAt(Limit limit,double hours,DateTimeOffset now) {
        return limit.Reset == 0 ? 0 : Math.Max(0, Math.Min(100,
            (DateTimeOffset.FromUnixTimeSeconds(limit.Reset) - now).TotalHours / hours * 100));
    }
    static void StyleQuota(string prefix, Limit limit, double hours) {
        Brush quota = QuotaColor(limit.Remaining);
        double time = TimePercent(limit, hours);
        Brush countdown = TimeColor(time);
        quota = Theme.Brush("quota" + (limit.Remaining < 10 ? "Red" : limit.Remaining < 20 ? "Orange" : limit.Remaining < 30 ? "Yellow" : "Green"));
        countdown = Theme.Brush("time" + (time > 60 ? "Red" : time > 40 ? "Orange" : time > 20 ? "Yellow" : "Green"));
        Control<ProgressBar>(prefix + "Bar").Foreground = quota;
        Control<ProgressBar>("Compact" + prefix + "Bar").Foreground = quota;
        Control<TextBlock>(prefix + "Value").Foreground = Theme.Brush("valueText");
        foreach (string name in new[] { prefix + "TimeBar", "Compact" + prefix + "TimeBar" }) {
            Control<ProgressBar>(name).Value = time;
            Control<ProgressBar>(name).Foreground = countdown;
            Control<ProgressBar>(name).ToolTip = Text("距重置剩余时间", "Time remaining until reset");
        }
        Control<TextBlock>(prefix + "Reset").Foreground = Theme.Brush("timeText");
        Control<TextBlock>("Compact" + prefix + "Reset").Foreground = Theme.Brush("timeText");
        // CaptureLayoutInput builds the raw caption from the snapshot; WidgetLayout
        // writes the final visible caption once. Never insert then clear text in a hidden/minimal bar.
        Control<ProgressBar>("Compact" + prefix + "TimeBar").Uid = CompactReset(limit,prefix=="Week");
    }
    static T Control<T>(string name) where T : class { return window.FindName(name) as T; }
    static void RenderLimit(string prefix, Limit limit, double hours) {
        if (limit == null) return;
        hours=QuotaHours(prefix,limit);
        Control<TextBlock>(prefix+"Label").Text=QuotaRowLabel(prefix,limit,false);
        Control<TextBlock>(prefix+"Value").Text = limit.Remaining.ToString("0.#") + "%";
        Control<ProgressBar>(prefix+"Bar").Value = Control<ProgressBar>("Compact"+prefix+"Bar").Value = limit.Remaining;
        Control<TextBlock>(prefix+"Reset").Text = Control<TextBlock>("Compact"+prefix+"Reset").Text = CompactReset(limit,prefix=="Week");
        StyleQuota(prefix,limit,hours);
        if(prefix=="Hour") foreach(string name in new[]{"HourBar","CompactHourBar"}) Control<ProgressBar>(name).DataContext=hours==5 ? HourlyReferenceMarker(limit.Reset,DateTimeOffset.UtcNow) : PeriodReferenceMarker(limit,hours);
        if(prefix=="Week") foreach(string name in new[]{"WeekBar","CompactWeekBar","WeekTimeBar","CompactWeekTimeBar","WeekGap","CompactWeekGap","WeekMarker","CompactWeekMarker"}) {
            double marker=hours==168 ? WeeklyDayMarker(limit.Reset,DateTimeOffset.UtcNow) : PeriodReferenceMarker(limit,hours);
            var bar=Control<ProgressBar>(name);if(!Equals(bar.DataContext,marker))bar.DataContext=marker;
        }
    }
    static double WeeklyDayMarker(long reset, DateTimeOffset now) {
        if(reset<=0) return -1;
        double days=(DateTimeOffset.FromUnixTimeSeconds(reset)-now).TotalDays;
        if(days<=0 || days>7) return -1;
        return Math.Max(0,Math.Min(6,Math.Ceiling(days)-1))/7;
    }
    static double HourlyReferenceMarker(long reset,DateTimeOffset now) {
        if(reset<=0) return -1;
        double seconds=(DateTimeOffset.FromUnixTimeSeconds(reset)-now).TotalSeconds;
        return seconds<=0 || seconds>18000 ? -1 : seconds/18000;
    }
    static void UpdateRefreshStatus() {
        string state=activeReading ? "querying" : queryFailed || localReadWarning.Length>0 ? "failed" : snapshot==null && !currentProvider.SupportsQuery ? "unavailable" : snapshot==null || DataIsStale() ? "stale" : "normal";
        if(!activeReading && DateTimeOffset.UtcNow<providerRetryNotBefore)state="limited";
        var button=Control<Button>("RefreshButton");
        string hex=Theme.Colors[state=="querying" ? "weekMarker" : state=="failed" ? "failure" : state=="stale" || state=="limited" ? "alert" : "icons"];
        var color=(Color)ColorConverter.ConvertFromString(hex);
        button.Resources["ToolbarNormal"]=new SolidColorBrush(color);
        button.Resources["ToolbarHover"]=LightenIcon(color,0.35);
        button.Resources["ToolbarPressed"]=LightenIcon(color,0.65);
        button.Tag=state;
        button.ToolTip=Text(state=="querying" ? "正在查询账户额度" : state=="failed" ? "查询或读取失败，点击重试" : state=="stale" ? "额度可能过期，点击查询" : "主动查询最新账户额度",
            state=="querying" ? "Querying account usage" : state=="failed" ? "Query/read failed; click to retry" : state=="stale" ? "Usage may be stale; click to query" : "Query current account usage")
            +(snapshot==null ? "" : "\n"+Text("数据时间：","Data timestamp: ")+snapshot.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        if(!currentProvider.SupportsQuery) button.ToolTip=ProviderHelp()+(snapshot==null?"":"\n"+Text("数据时间：","Data timestamp: ")+snapshot.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        if(state=="limited")button.ToolTip=Text("服务端要求等待，恢复查询时间：","Server cooldown; queries resume at: ")+providerRetryNotBefore.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }
    static void Render() {
        UpdateRefreshStatus();
        Control<TextBlock>("Updated").ToolTip=ProviderHelp()+"\n"+queryError;
        Control<TextBlock>("EmptyUsage").Text = ProviderEmptyText();
        window.Title=currentProvider.Id=="codex" ? Text("额度","Usage") : (currentProvider.Id=="claude" ? "Claude" : "Cursor")+Text(" 额度"," usage");
        Control<TextBlock>("TitleLabel").Text=window.Title;
        bool warning = queryFailed;
        Control<TextBlock>("Updated").Foreground = Theme.Brush(warning ? "alert" : "status");
        // Keep the warning visible even when zoom hides captions and the toolbar.
        Control<Border>("Root").BorderBrush = Theme.Brush(warning ? "alert" : "windowBorder");
        Control<Border>("Root").BorderThickness = new Thickness(warning ? 1+1/zoom : 1);
        Control<Border>("Root").ToolTip = window.Title+"\n"+ProviderHelp()+"\n"+providerCacheWarning+"\n"+providerSelectionWarning+(warning ? "\n"+queryError+"\n"+localReadWarning : "");
        if (reading) { Control<TextBlock>("Updated").Text = Text("正在读取额度…", "Reading usage…"); ResizeWidget(); return; }
        if (snapshot == null) {
            Control<TextBlock>("HourValue").Text = "—";
            Control<TextBlock>("WeekValue").Text = "—";
            Control<TextBlock>("Updated").Text = queryFailed ? Text("查询失败 · 暂无本地记录", "Query failed · no local data") : ProviderEmptyText();
            Control<TextBlock>("CompactHourReset").Text = "—";
            Control<TextBlock>("CompactWeekReset").Text = "—";
            UpdateProviderTiming();
            ResizeWidget();
            return;
        }
        RenderLimit("Hour",snapshot.Hour,5);
        RenderLimit("Week",snapshot.Week,168);
        Control<TextBlock>("Updated").Text = (queryFailed ? Text("查询失败 · ", "Query failed · ") : localReadWarning.Length > 0 ? Text("本地读取异常 · ", "Local read warning · ") : snapshot.Live ? Text("账户查询 ", "Account query ") : Text("记录更新 ", "Local record ")) + snapshot.Time.ToLocalTime().ToString("HH:mm:ss")
            + (DataIsStale() ? Text(" · 可能已过期", " · May be stale") : "");
        Control<TextBlock>("Updated").ToolTip = (queryFailed ? Text("主动查询失败，保留上次数据。请确认 Codex 已登录且网络可用。\n", "Query failed; keeping previous data. Check Codex sign-in and network.\n") : "")
            + Text("打开、点击刷新及后台每 30 秒主动查询；每 15 秒读取本地记录。连续失败时自动重试间隔延长至最多 5 分钟，手动刷新可立即查询。\n数据时间：", "Queries on launch, manual refresh and every 30 seconds; local reads every 15 seconds. Consecutive failures extend retries up to 5 minutes; manual refresh queries immediately.\nData timestamp: ") + snapshot.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            + "\n" + localReadWarning;
        if(currentProvider.Id!="codex") Control<TextBlock>("Updated").ToolTip=ProviderHelp()+"\n"+snapshot.Source+"\n"+snapshot.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+"\n"+queryError;
        if(providerCacheWarning.Length>0) Control<TextBlock>("Updated").ToolTip+="\n"+providerCacheWarning;
        UpdateProviderTiming();
        ResizeWidget();
    }
    static void UpdateProviderTiming() {
        if(DateTimeOffset.UtcNow<providerRetryNotBefore)Control<TextBlock>("Updated").Text=Text("限流等待至 ","Waiting until ")+providerRetryNotBefore.ToLocalTime().ToString("HH:mm:ss");
        else if(queryFailed && currentProvider.Id!="codex")Control<TextBlock>("Updated").Text+=Text(" · 重试 "," · Retry ")+nextAutoQuery.ToLocalTime().ToString("HH:mm:ss");
    }
    static void SavePreview(string path) {
        window.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.Width),(int)Math.Ceiling(window.Height),96,96,PixelFormats.Pbgra32);
        double opacity=window.Opacity;window.Opacity=1;
        try {bitmap.Render(window);} finally {window.Opacity=opacity;}
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var output=File.Create(path)) encoder.Save(output);
    }
    static void CheckUi(string report) {
        var originalColors = new Dictionary<string,string>(Theme.Colors);
        OpenAppearance();
        if (appearanceOpen || Theme.Keys.Any(key => Theme.Colors[key] != originalColors[key])) throw new Exception("Color cancellation failed.");
        Control<Button>("LockButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SetExpanded(false);
        if (!locked || !expanded) throw new Exception("Lock did not preserve expanded view.");
        Control<Button>("LockButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (locked) throw new Exception("Unlock failed.");
        if (!collapseTimer.IsEnabled) throw new Exception("Unlock did not schedule collapse without another hover event.");
        var unlockFrame = new DispatcherFrame();
        var unlockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        unlockTimer.Tick += (s,e) => unlockFrame.Continue = false;
        unlockTimer.Start(); Dispatcher.PushFrame(unlockFrame); unlockTimer.Stop();
        if (expanded) throw new Exception("Unlock did not collapse without another hover event.");
        collapseTimer.Stop();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        snapshot = new Snapshot { Time = DateTimeOffset.UtcNow, Live = true,
            Hour = new Limit { Remaining = 72, Reset = now + 16200 },
            Week = new Limit { Remaining = 42, Reset = now + 3 * 86400 + 12 * 3600 + 29 * 60 } };
        window.Left = 200; window.Top = 200;
        string folder = Path.GetDirectoryName(Path.GetFullPath(report));
        foreach (int language in new[] { 0, 1 }) {
            SelectLanguage(language == 1);
            if (english != (language == 1)) throw new Exception("Language selection failed.");
            Control<Grid>("QuotaArea").RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            if (!expanded || Control<StackPanel>("Details").Visibility != Visibility.Visible) throw new Exception("Hover expansion failed.");
            double bottom = window.Top + window.Height;
            SavePreview(Path.Combine(folder, english ? "english-details.png" : "chinese-details.png"));
            Control<Grid>("QuotaArea").RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            if (collapseTimer.IsEnabled) throw new Exception("Leaving quota area armed collapse while inside the widget.");
            RaiseHover(window,System.Windows.Input.Mouse.MouseLeaveEvent);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += (s, e) => frame.Continue = false;
            timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
            if (expanded || Control<Grid>("Compact").Visibility != Visibility.Visible) throw new Exception("Delayed collapse failed.");
            Control<Grid>("Toolbar").RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            if (expanded || Control<Grid>("Toolbar").Visibility != Visibility.Visible) throw new Exception("Toolbar hover expanded quota area.");
            if (Math.Abs(window.Top + window.Height - bottom) > 0.1) throw new Exception("Window anchor moved.");
            if (Control<TextBlock>("CompactWeekLabel").Text != Text("周","Wk") || Control<TextBlock>("CompactHourLabel").Text != Text("五","5h")) throw new Exception("Compact labels not localized.");
            if (!Control<TextBlock>("CompactWeekReset").Text.Contains("d ")) throw new Exception("Countdown formatting failed.");
            SavePreview(Path.Combine(folder, english ? "english-compact.png" : "chinese-compact.png"));
        }
        queryFailed = true; snapshot.Time = snapshot.Time.AddMinutes(-6); Render();
        if (!Control<TextBlock>("Updated").Text.Contains("Query failed") || !Control<TextBlock>("Updated").Text.Contains("May be stale")) throw new Exception("Failure translation failed.");
        foreach (double value in new[] { 0.0, 9.99, 10, 19.99, 20, 29.99, 30, 100 }) {
            Brush expected = value < 10 ? Red : value < 20 ? Orange : value < 30 ? Yellow : Green;
            if (QuotaColor(value) != expected) throw new Exception("Quota color boundary failed.");
        }
        foreach (double value in new[] { 0.0, 20, 20.01, 40, 40.01, 60, 60.01, 100 }) {
            Brush expected = value > 60 ? Red : value > 40 ? Orange : value > 20 ? Yellow : Green;
            if (TimeColor(value) != expected) throw new Exception("Countdown color boundary failed.");
        }
        snapshot.Hour.Remaining = 25; snapshot.Week.Remaining = 15;
        snapshot.Hour.Reset = now + 1800; snapshot.Week.Reset = now + 6 * 86400;
        queryFailed = false; Render();
        if (Control<ProgressBar>("CompactHourBar").Foreground.ToString() != Theme.Brush("quotaYellow").ToString() || Control<ProgressBar>("WeekBar").Foreground.ToString() != Theme.Brush("quotaOrange").ToString()) throw new Exception("Applied color failed.");
        SavePreview(Path.Combine(folder, "color-compact.png")); SetExpanded(true);
        SavePreview(Path.Combine(folder, "color-details.png"));
        foreach (int language in new[] { 0, 1 }) {
            SelectLanguage(language == 1);
            foreach (bool details in new[] { false, true }) {
                SetExpanded(details);
                foreach (double target in new[] { 0.4, 0.75, 0.8, 1.0, 1.5 }) {
                    zoom = 1; ResizeWidget();
                    double right = window.Left + window.Width, bottom = window.Top + window.Height;
                    int delta = (int)Math.Round((target - 1) / 0.05) * 120;
                    window.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, delta) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
                    if (Math.Abs(zoom - target) > 0.001 || Math.Abs(window.Width - 360 * target) > 0.1) throw new Exception("Wheel scaling failed.");
                    if (Math.Abs(window.Left + window.Width - right) > 0.1 || Math.Abs(window.Top + window.Height - bottom) > 0.1) throw new Exception("Scale anchor moved.");
                    Control<Border>("Root").Measure(new Size(window.Width, double.PositiveInfinity));
                    if (Control<Border>("Root").DesiredSize.Height > window.Height + 1) throw new Exception("Scaled content clipped.");
                    if (target < 0.8 && (Control<StackPanel>("Details").Visibility != Visibility.Collapsed || Control<TextBlock>("CompactHourLabel").Visibility != Visibility.Collapsed || Control<ProgressBar>("CompactHourBar").Uid != "")) throw new Exception("Mini text not hidden.");
                    if (target >= 0.8 && (Control<TextBlock>("CompactHourLabel").Visibility != Visibility.Visible || string.IsNullOrEmpty(Control<ProgressBar>("CompactHourBar").Uid))) throw new Exception("Text not restored after scaling.");
                    if (((string)Control<ProgressBar>("CompactHourBar").Tag).Contains("detail") != (target < 0.8) || (string)Control<ProgressBar>("HourBar").Tag != "quota-detail") throw new Exception("Quota tick height mode failed.");
                    if (language == 1 && target != 1) SavePreview(Path.Combine(folder, "scale-" + (int)(target * 100) + (details ? "-details.png" : "-compact.png")));
                }
            }
        }
        ChangeZoom(12000); if (zoom != 1.5) throw new Exception("Upper zoom bound failed.");
        ChangeZoom(-12000); if (zoom != 0.4) throw new Exception("Lower zoom bound failed.");
        SetZoom(1);
        foreach (bool en in new[] {false,true}) {
            SelectLanguage(en); settingsPreviewPath = Path.Combine(folder,en ? "settings-en.png" : "settings-zh.png");
            Control<Button>("SettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateSettingsHint();
        }
        foreach (double amount in new[] { 79.5, 85, 90.5, 99.5, 100 }) {
            snapshot.Hour.Remaining = snapshot.Week.Remaining = amount;
            snapshot.Hour.Reset = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)(18000 * amount / 100);
            snapshot.Week.Reset = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)(604800 * amount / 100);
            snapshot.Time = DateTimeOffset.UtcNow;
            Render(); SetExpanded(false);
            SavePreview(Path.Combine(folder, "boundary-" + amount.ToString("0.#", CultureInfo.InvariantCulture) + ".png"));
        }
        var parser = new JavaScriptSerializer();
        var weekOnly = Map(parser.DeserializeObject("{\"primary\":{\"usedPercent\":28,\"windowDurationMins\":10080},\"secondary\":null}"));
        snapshot = ParseWindows(weekOnly,DateTimeOffset.UtcNow,true);
        if (snapshot.Hour != null || snapshot.Week == null || snapshot.Week.Remaining != 72) throw new Exception("Primary weekly window misclassified.");
        snapshot.Week.Reset = now + 86400;
        Render();
        foreach (bool details in new[] {false,true}) {
            SetExpanded(details);
            if (Control<FrameworkElement>("HourBar").Visibility != Visibility.Collapsed || Control<FrameworkElement>("WeekBar").Visibility != Visibility.Visible) throw new Exception("Weekly-only visibility failed.");
            SavePreview(Path.Combine(folder,details ? "weekly-only-details.png" : "weekly-only-compact.png"));
        }
        snapshot = ParseWindows(Map(parser.DeserializeObject("{\"primary\":null,\"secondary\":null}")),DateTimeOffset.UtcNow,true); Render();
        if (Control<TextBlock>("EmptyUsage").Visibility != Visibility.Visible) throw new Exception("Missing quotas not indicated.");
        SetExpanded(false); SavePreview(Path.Combine(folder,"no-limits.png"));
        snapshot = ParseWindows(Map(parser.DeserializeObject("{\"primary\":{\"used_percent\":10,\"window_minutes\":300},\"secondary\":null}")),DateTimeOffset.UtcNow,false); Render();
        if (snapshot.Hour == null || snapshot.Week != null) throw new Exception("Hourly-only parse failed.");
        File.WriteAllText(report, "PASS: nullable quotas, primary weekly-window classification, hourly-only local format, weekly-only layout, no-limits message; color preview, invalid-color rejection, defaults, JSON roundtrip, cancellation; settings language selection, slider and numeric scale input, invalid input, welcome bubble construction; hover collapse/expand, colors, wheel events at 40/75/80/100/150 percent in both languages and both modes, scale limits, anchored resizing and no vertical clipping.\r\nOffline previews generated. No network calls or preference writes.\r\nLive Pro account query, physical color picker, mouse dragging, welcome popup placement and saved-preference reload not exercised by this offline check.\r\n");
    }
    static void CheckLocalReader(string report) {
        string fixture = Path.Combine(Path.GetDirectoryName(report), "reader-fixture");
        string sessions = Path.Combine(fixture,".codex","sessions");
        Directory.CreateDirectory(sessions);
        var now = DateTimeOffset.UtcNow;
        long reset = now.ToUnixTimeSeconds()+18000;
        Func<int,string,string> record = (seconds,limits) => "{\"timestamp\":\""+now.AddSeconds(seconds).ToString("o")+"\",\"payload\":{\"rate_limits\":"+limits+"}}";
        string hourly90 = "{\"primary\":{\"used_percent\":10,\"window_minutes\":300,\"resets_at\":"+reset+"}}";
        string hourly5 = "{\"primary\":{\"used_percent\":95,\"window_minutes\":300,\"resets_at\":"+reset+"}}";
        string weekly = "{\"secondary\":{\"used_percent\":38,\"window_minutes\":10080,\"resets_at\":"+(reset+604800)+"}}";
        File.WriteAllLines(Path.Combine(sessions,"hour.jsonl"),new[] {
            record(-30,hourly90), "{\"rate_limits\": broken", record(-10,hourly5),
            record(-5,"{\"primary\":{\"used_percent\":80,\"resets_at\":9223372036854775807}}") });
        File.WriteAllLines(Path.Combine(sessions,"week.jsonl"),new[] {
            record(-3,weekly), record(-1,"{\"primary\":null,\"secondary\":null}") });
        snapshot = ReadSnapshotFrom(fixture);
        if (snapshot == null || snapshot.Hour == null || snapshot.Hour.Remaining != 5 || snapshot.Week == null || snapshot.Week.Remaining != 62)
            throw new Exception("Malformed/incomplete records blocked independent quota updates.");
        if (localReadWarning.Length == 0) throw new Exception("Skipped records were not reported.");
        if (snapshot.HourTime != now.AddSeconds(-10) || snapshot.WeekTime != now.AddSeconds(-3))
            throw new Exception("Independent record timestamps were replaced by newest overall time.");
        var roundedLive = new Snapshot {Time=now.AddSeconds(-20),Live=true,
            Hour=new Limit {Remaining=90,Reset=reset+1}};
        var roundedLocal = new Snapshot {Time=now.AddSeconds(-10),
            Hour=new Limit {Remaining=5,Reset=reset}};
        var roundedSelected = NewerSnapshot(roundedLive,roundedLocal);
        if (roundedSelected.Hour.Remaining != 5) throw new Exception("One-second earlier reset froze usage updates.");
        roundedLocal = new Snapshot {Time=now,Hour=new Limit {Remaining=92,Reset=reset+1}};
        if (NewerSnapshot(roundedSelected,roundedLocal).Hour.Remaining != 5)
            throw new Exception("One-second later reset resurrected cached balance.");
        var live = new Snapshot { Time=now.AddSeconds(-8), Live=true,
            Hour=new Limit {Remaining=90,Reset=reset}, Week=new Limit {Remaining=70,Reset=reset+604800} };
        var merged = NewerSnapshot(live,snapshot);
        if (merged.Hour.Remaining != 90 || merged.Week.Remaining != 62)
            throw new Exception("Weekly timestamp allowed pre-query hourly data to overwrite a live read.");
        foreach (double scale in new[] {0.4,0.75,1.0}) {
            SetZoom(scale); SetExpanded(false); Render();
            if (Control<Border>("Root").ToolTip == null || !Control<TextBlock>("Updated").Text.Contains("warning"))
                throw new Exception("Collapsed read warning missing.");
            SavePreview(Path.Combine(Path.GetDirectoryName(report),"read-warning-"+(int)(scale*100)+".png"));
        }
        localReadWarning="";
        snapshot.HourTime = now.AddMinutes(-6); snapshot.WeekTime = now;
        Render();
        if (!DataIsStale() || Control<Border>("Root").ToolTip == null) throw new Exception("Recent weekly data hid stale hourly data.");
        snapshot.HourTime = now; Render();
        if (DataIsStale() || Control<Border>("Root").ToolTip != null) throw new Exception("Healthy data did not clear warning.");
        File.WriteAllLines(Path.Combine(sessions,"hour.jsonl"),new[]{"{\"rate_limits\": broken"});
        File.WriteAllText(Path.Combine(sessions,"week.jsonl"),"");
        bool failed=false;
        try { ReadSnapshotFrom(fixture); } catch (InvalidDataException) { failed=true; }
        if (!failed) throw new Exception("All-bad records silently returned no data.");
        File.WriteAllText(report,"PASS: one-second reset drift accepts 90% to 5% and rejects cached 92%; malformed JSON and invalid reset isolation; newer weekly-only/empty events preserve hourly data and per-window timestamps; warnings visible at 40/75/100% collapsed; independent window freshness and warning recovery; all-invalid read reported.\r\nSynthetic records only. No account queries or preference writes. Physical long-running widget not exercised.\r\n");
    }
    static void CheckDisplayRefresh(string report) {
        var now=DateTimeOffset.UtcNow;
        long weeklyReset=now.ToUnixTimeSeconds()+604800;
        for(int day=0;day<7;day++) {
            double marker=WeeklyDayMarker(weeklyReset,DateTimeOffset.FromUnixTimeSeconds(weeklyReset-604800+day*86400+3600));
            if(Math.Abs(marker-(6-day)/7.0)>0.00001) throw new Exception("Weekly day marker boundary failed.");
        }
        if(WeeklyDayMarker(0,now)!=-1 || WeeklyDayMarker(now.ToUnixTimeSeconds()-1,now)!=-1) throw new Exception("Unknown/expired reset marker shown.");
        snapshot=new Snapshot {Time=now,Live=true,Hour=new Limit {Remaining=38,Reset=now.ToUnixTimeSeconds()+18000},Week=new Limit {Remaining=100,Reset=now.ToUnixTimeSeconds()+604800}};
        activeQueryStarted=now;CompleteActiveQuery(true,now);
        if(ShouldAutoQuery(now.AddSeconds(29.99)) || !ShouldAutoQuery(now.AddSeconds(30))) throw new Exception("Independent 30-second account schedule failed.");
        snapshot.Time=now.AddSeconds(29);snapshot.HourTime=snapshot.WeekTime=snapshot.Time;
        if(!ShouldAutoQuery(now.AddSeconds(30))) throw new Exception("Fresh local records postponed account query.");
        reading=true;if(ShouldAutoQuery(now.AddSeconds(30))) throw new Exception("Concurrent automatic query allowed.");reading=false;
        snapshot=new Snapshot {Time=now};
        if(!ShouldAutoQuery(now.AddSeconds(30))) throw new Exception("Unavailable quotas stopped scheduled reads.");
        for(int failure=0;failure<5;failure++) {
            CompleteActiveQuery(false,now);
            double seconds=new[]{60,120,240,300,300}[failure];
            if(ShouldAutoQuery(now.AddSeconds(seconds-0.01)) || !ShouldAutoQuery(now.AddSeconds(seconds))) throw new Exception("Account query retry backoff failed.");
        }
        queryFailed=true;queryError="synthetic";
        RegisterLocalResult(new Snapshot {Time=now,Hour=new Limit {Remaining=38}},now);
        if(!queryFailed) throw new Exception("Local fallback hid account query failure.");
        activeQueryStarted=now;CompleteActiveQuery(true,now);queryFailed=false;queryError="";
        if(!ShouldAutoQuery(now.AddSeconds(30))) throw new Exception("Successful manual read did not restore normal cadence.");
        refreshStopped=true;if(ShouldAutoQuery(now.AddMinutes(10))) throw new Exception("Closed window scheduled an account query.");refreshStopped=false;
        var serializer=new JavaScriptSerializer();
        ApplyDisplayOptions(serializer.Deserialize<DisplayOptions>("{}"));
        if(textThreshold!=0.8 || baseWidth!=180) throw new Exception("Old display settings compatibility failed.");
        ApplyDisplayOptions(serializer.Deserialize<DisplayOptions>(serializer.Serialize(new DisplayOptions {TextThreshold=0.6,Width=480})));
        if(textThreshold!=0.6 || baseWidth!=480) throw new Exception("Display preference roundtrip failed.");
        ApplyDisplayOptions(new DisplayOptions {TextThreshold=2,Width=1});
        if(textThreshold!=1.5 || baseWidth!=100) throw new Exception("Invalid preference bounds failed.");
        snapshot=new Snapshot {Time=now,Live=true,Hour=new Limit {Remaining=38,Reset=now.ToUnixTimeSeconds()+1800},Week=new Limit {Remaining=72,Reset=now.ToUnixTimeSeconds()+388800}};
        window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=-10000;window.Top=-10000;window.Show();
        foreach(bool en in new[]{false,true}) {
            SelectLanguage(en);
            foreach(double width in new[]{100.0,165.0,180.0,280.0,360.0,480.0}) foreach(double threshold in new[]{0.4,0.6,0.8,1.0,1.5}) foreach(double scale in new[]{0.4,0.59,0.6,0.79,0.8,1.0,1.5}) foreach(bool top in new[]{false,true}) foreach(bool details in new[]{false,true}) {
                baseWidth=width;textThreshold=threshold;toolbarTop=top;locked=details;expanded=details;SetZoom(scale);Render();window.UpdateLayout();
                if(Math.Abs(window.Width-width*scale)>0.1) throw new Exception("Independent width failed.");
                if(Control<TextBlock>("TitleLabel").Text!=(en ? "Usage" : "额度")) throw new Exception("Title rename failed.");
                var root=Control<Border>("Root");root.Measure(new Size(window.Width,double.PositiveInfinity));
                if(root.DesiredSize.Height>window.Height+1) throw new Exception("Display vertically clipped.");
                if(scale<=threshold && (Control<StackPanel>("Details").Visibility!=Visibility.Collapsed || Control<ProgressBar>("CompactWeekBar").Uid!="")) throw new Exception("Threshold not honored while locked.");
                if(details && scale>threshold && !DetailsFit()) {
                    var header=Control<Grid>("HeaderGrid");
                    if(header.Parent!=Control<Grid>("QuotaArea") || header.Visibility!=Visibility.Visible || Control<TextBlock>("TitleLabel").Visibility!=Visibility.Visible)
                        throw new Exception("Narrow expansion lost title/time.");
                    if(Control<TextBlock>("Updated").Text!=snapshot.Time.ToLocalTime().ToString("HH:mm:ss")) throw new Exception("Narrow timestamp missing.");
                    if(width>=165 && Grid.GetRow(Control<TextBlock>("Updated"))!=0) throw new Exception("Narrow header not aligned on one line.");
                }
                foreach(string buttonName in DefaultToolbarOrder) {
                    var button=Control<Button>(buttonName);var toolbar=Control<Grid>("Toolbar");
                    var origin=button.TranslatePoint(new Point(0,0),toolbar);
                    if(origin.X < -1 || origin.X+button.ActualWidth>toolbar.ActualWidth+1) throw new Exception("Narrow toolbar overflow.");
                }
                if(((string)Control<ProgressBar>("CompactWeekBar").Tag).Contains("detail")!=(Control<ProgressBar>("CompactWeekBar").Uid.Length==0)) throw new Exception("Hidden text tick rules failed.");
                foreach(string prefix in new[]{"Hour","Week"}) {
                    var label=Control<TextBlock>("Compact"+prefix+"Label");var bar=Control<ProgressBar>("Compact"+prefix+"Bar");
                    double captionSize=width<=200 ? 11 : 13;
                    double captionStart=width<=200 ? width-18-6-TextWidth(bar.Uid,captionSize,FontWeights.Bold) : (width-18-TextWidth(bar.Uid,captionSize,FontWeights.Bold))/2;
                    if(label.Visibility==Visibility.Visible && captionStart < 6+TextWidth(label.Text,captionSize,FontWeights.Bold)+(width<=200 ? 8 : 6)) throw new Exception("Label/caption overlap.");
                    if(scale>threshold && bar.Uid.Length==0 && TextWidth("72%",13,FontWeights.Bold)<=width-30) throw new Exception("Percentage hidden despite fitting.");
                }
                if(scale>threshold && width==100 && Control<ProgressBar>("CompactWeekBar").Uid!="72%") throw new Exception("Percentage-only fallback failed.");
                if(scale>threshold && (width==165 || width==180) && !Control<ProgressBar>("CompactWeekBar").Uid.Contains("·")) throw new Exception("Percent/time fallback failed.");
                foreach(string name in new[]{"WeekBar","CompactWeekBar","WeekTimeBar","CompactWeekTimeBar"}) {
                    var bar=Control<ProgressBar>(name);bar.ApplyTemplate();
                    var visual=Children(bar).OfType<SegmentedVisual>().Single();
                    if(Math.Abs(visual.DayMarker-4.0/7)>0.001 || !visual.Kind.Contains("week")) throw new Exception("Weekly marker template binding failed.");
                }
                foreach(string name in new[]{"WeekGap","CompactWeekGap"}) {
                    var gap=Control<ProgressBar>(name);gap.ApplyTemplate();var visual=Children(gap).OfType<SegmentedVisual>().Single();
                    if(visual.Kind!="marker-gap" || Math.Abs(visual.DayMarker-4.0/7)>0.001) throw new Exception("Connected week marker gap missing.");
                }
                if(threshold==0.6 && scale==1 && !top) SavePreview(Path.Combine(Path.GetDirectoryName(report),(en?"en":"zh")+"-"+(int)width+(details?"-expanded":"-compact")+".png"));
            }
        }
        baseWidth=360;textThreshold=0.8;toolbarTop=false;locked=false;SetZoom(1);SetExpanded(true);
        var fixedDialog=new Window {Width=410,Height=300,Opacity=0,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-9000,Top=-9000};
        settingsWindow=fixedDialog;fixedDialog.Show();fixedDialog.UpdateLayout();
        double fixedLeft=fixedDialog.Left,fixedTop=fixedDialog.Top;
        foreach(double width in new[]{100.0,180.0,480.0}) {baseWidth=width;SetZoom(1);window.UpdateLayout();if(fixedDialog.Left!=fixedLeft || fixedDialog.Top!=fixedTop) throw new Exception("Settings followed widget resize.");}
        fixedDialog.Close();settingsWindow=null;baseWidth=360;SetZoom(1);
        snapshot=new Snapshot {Time=now,Live=true,Hour=new Limit {Remaining=69,Reset=now.ToUnixTimeSeconds()+15000},Week=new Limit {Remaining=93,Reset=now.ToUnixTimeSeconds()+496200}};
        SelectLanguage(false);baseWidth=180;SetZoom(1);SetExpanded(false);window.UpdateLayout();
        if(!Control<ProgressBar>("CompactWeekBar").Uid.Contains("·") || Control<TextBlock>("CompactWeekLabel").FontSize!=11) throw new Exception("Long weekly countdown compact case failed.");
        SavePreview(Path.Combine(Path.GetDirectoryName(report),"zh-180-long-countdown.png"));
        baseWidth=360;SetZoom(1);SetExpanded(true);
        foreach(bool en in new[]{false,true}) {SelectLanguage(en);settingsPreviewPath=Path.Combine(Path.GetDirectoryName(report),en?"settings-en.png":"settings-zh.png");Control<Button>("SettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        File.WriteAllText(report,"PASS: simulated stale/fresh/missing/unavailable data, completion cooldown, single-flight guard; display preference compatibility; bilingual widths100/165/180/280/360/480, thresholds40/60/80/100/150%, zoom40/59/60/79/80/100/150%, toolbar top/bottom and compact/locked-expanded; narrow 11px captions right-aligned with minimum 8px label gap, long weekly countdown; connected weekly marker gap bindings and day boundaries; visible settings remain fixed during widget resize; styled settings numeric controls/language/order construction.\r\nNo network queries or user preference writes. Physical mouse interaction, live failures and restart persistence not exercised.\r\n");
    }
    static void CheckAlignedLayout(string report) {
        var now=DateTimeOffset.UtcNow;var folder=Path.GetDirectoryName(report);
        CheckTickDrawing();
        snapshot=new Snapshot {Time=now,Live=true,Hour=new Limit {Remaining=49,Reset=now.ToUnixTimeSeconds()+14340},Week=new Limit {Remaining=90,Reset=now.ToUnixTimeSeconds()+495540}};
        window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=-10000;window.Top=-10000;window.Show();
        foreach(bool en in new[]{false,true}) foreach(bool top in new[]{false,true}) foreach(double width in new[]{100.0,150.0,165.0,180.0,200.0,201.0,280.0,360.0,480.0}) foreach(double scale in new[]{0.4,0.8,1.0,1.5}) foreach(bool details in new[]{false,true}) {
            SelectLanguage(en);toolbarTop=top;baseWidth=width;expanded=details;textThreshold=0.8;SetZoom(scale);window.UpdateLayout();
            if(Math.Abs(window.Width-width*scale)>1) throw new Exception("Widget widened: actual="+window.Width+" expected="+(width*scale)+" base="+width+" zoom="+scale+" top="+top+" details="+details);
            bool full=details;
            if((Control<StackPanel>("Details").Visibility==Visibility.Visible)!=full) throw new Exception("Expanded view fell back to compact.");
            if(Control<TextBlock>("Updated").Visibility!=Visibility.Collapsed || Control<TextBlock>("TopUpdated").Visibility!=Visibility.Collapsed) throw new Exception("Record time still visible.");
            for(int col=0;col<6;col++) if(Control<Grid>("HourInfo").ColumnDefinitions[col].Width!=Control<Grid>("WeekInfo").ColumnDefinitions[col].Width) throw new Exception("Detail columns misaligned.");
            foreach(string prefix in new[]{"Hour","Week"}) {
                var row=Control<Grid>(prefix+"Info");
                double visibleWidth=row.ColumnDefinitions.Take(5).Sum(column=>column.Width.Value);
                if(visibleWidth>width-18+0.1) throw new Exception("Detail values overflow.");
                var value=Control<TextBlock>(prefix+"Value");
                if(full && value.ActualWidth+0.1<TextWidth(value.Text,13,FontWeights.Bold)) throw new Exception("Percentage clipped.");
            }
            var toolbar=Control<Grid>("Toolbar");
            if(Control<TextBlock>("TitleLabel").FontSize>13 || Control<TextBlock>("TitleLabel").FontWeight!=FontWeights.Bold) throw new Exception("Title does not match percentage typography.");
            foreach(string name in DefaultToolbarOrder) {
                var button=Control<Button>(name);var origin=button.TranslatePoint(new Point(0,0),toolbar);
                if(origin.X < -1 || origin.X+button.ActualWidth>toolbar.ActualWidth+1) throw new Exception("Toolbar overflow.");
                if(full && Control<TextBlock>("TitleLabel").Visibility==Visibility.Visible) {
                    var title=Control<TextBlock>("TitleLabel");var titleOrigin=title.TranslatePoint(new Point(0,0),toolbar);
                    if(title.Parent!=toolbar || Math.Abs(titleOrigin.Y+title.ActualHeight-origin.Y-button.ActualHeight)>1) throw new Exception("Title/buttons bottom alignment failed.");
                    if(titleOrigin.X+title.ActualWidth>origin.X+1) throw new Exception("Title/buttons overlap.");
                }
            }
            var ordered=toolbarOrder.Select(name=>Control<Button>(name)).ToArray();
            for(int i=0;i<ordered.Length-1;i++) {
                var first=(Viewbox)ordered[i].Content;var next=(Viewbox)ordered[i+1].Content;
                double gap=(next.TranslatePoint(new Point(0,0),toolbar).X-first.TranslatePoint(new Point(first.ActualWidth,0),toolbar).X)*scale;
                if(gap>7.1 || gap<0) throw new Exception("Icon visible gaps not compact.");
            }
            foreach(string prefix in new[]{"Hour","Week"}) {
                var bar=Control<ProgressBar>("Compact"+prefix+"Bar");bar.ApplyTemplate();var visual=Children(bar).OfType<SegmentedVisual>().Single();
                if(visual.Label!=(string)bar.GetValue(SegmentedVisual.LabelProperty)) throw new Exception("Inline label binding failed.");
                if(Control<TextBlock>("Compact"+prefix+"Label").Visibility!=Visibility.Collapsed) throw new Exception("Old label control still visible.");
                if(!full && scale<=textThreshold && visual.Label.Length>0) throw new Exception("Label ignores text threshold.");
            }
            var root=Control<Border>("Root");root.Measure(new Size(window.Width,double.PositiveInfinity));
            if(root.DesiredSize.Height>window.Height+1) throw new Exception("Vertical clipping.");
            if(scale==1 && (width==100 || width==180 || width==360)) SavePreview(Path.Combine(folder,(en?"en":"zh")+"-"+(int)width+(top?"-top":"-bottom")+(details?"-expanded":"-compact")+".png"));
            if(width==150 && scale==1.5 && top) SavePreview(Path.Combine(folder,(en?"en":"zh")+"-150-zoom150"+(details?"-expanded":"-compact")+".png"));
        }
        SelectLanguage(false);toolbarTop=true;baseWidth=180;SetZoom(1);SetExpanded(true);window.UpdateLayout();
        var marker=Control<ProgressBar>("WeekMarker");marker.ApplyTemplate();
        var markerVisual=Children(marker).OfType<SegmentedVisual>().Single();
        if(markerVisual.Kind!="week-marker-overlay" || Math.Abs(markerVisual.DayMarker-5.0/7)>0.001) throw new Exception("Single weekly overlay binding failed.");
        Control<ProgressBar>("WeekBar").ApplyTemplate();CheckQuotaArrow(Children(Control<ProgressBar>("WeekBar")).OfType<SegmentedVisual>().Single(),zoom);
        foreach(string state in new[]{"normal","stale","failed","querying"}) {
            activeReading=state=="querying";queryFailed=state=="failed";snapshot.Time=state=="stale" ? now.AddMinutes(-6) : now;localReadWarning="";Render();window.UpdateLayout();
            var refresh=Control<Button>("RefreshButton");if((string)refresh.Tag!=state) throw new Exception("Refresh status color state failed.");
            string expected=Theme.Colors[state=="normal" ? "icons" : state=="stale" ? "alert" : state=="failed" ? "failure" : "weekMarker"];
            if(((SolidColorBrush)refresh.Foreground).Color!=(Color)ColorConverter.ConvertFromString(expected)) throw new Exception("Refresh status brush failed.");
            SavePreview(Path.Combine(folder,"refresh-"+state+".png"));
        }
        activeReading=queryFailed=false;snapshot.Time=now;Render();
        var oldColors=new Dictionary<string,string>(Theme.Colors);Theme.Colors["track"]="#303640";Theme.Colors["barBorder"]="#15181E";UpgradeLegacyContrast();
        if(Theme.Colors["track"]!="#414B5B" || Theme.Colors["barBorder"]!="#77859B") throw new Exception("Legacy default contrast migration failed.");
        Theme.Colors["track"]="#123456";UpgradeLegacyContrast();if(Theme.Colors["track"]!="#123456") throw new Exception("Custom color overwritten.");Theme.Colors=oldColors;ApplyTheme();
        foreach(bool en in new[]{false,true}) {
            english=en;
            var boundaryNow=DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
            if(FormatCompactReset(now.ToUnixTimeSeconds()+86400,boundaryNow,true)!=Text("1天","1d") || FormatCompactReset(now.ToUnixTimeSeconds()+86401,boundaryNow,true)!=Text("2天","2d")
                || FormatCompactReset(now.ToUnixTimeSeconds()+86399,boundaryNow,true)!="23:59" || FormatCompactReset(now.ToUnixTimeSeconds()+3660,boundaryNow,true)!="1:01" || FormatCompactReset(now.ToUnixTimeSeconds()-1,boundaryNow,true)!="0:00") throw new Exception("Weekly countdown boundary failed.");
        }
        SelectLanguage(false);toolbarTop=false;baseWidth=280;SetZoom(1);SetExpanded(true);settingsVisits=0;UpdateSettingsHint();window.UpdateLayout();
        if(Control<Button>("SettingsButton").Foreground.ToString()!="#FFE5484D") throw new Exception("First-run settings highlight failed.");
        SavePreview(Path.Combine(folder,"first-run-bottom-expanded.png"));
        for(int visit=1;visit<=3;visit++) {
            if(visit==3) SelectLanguage(true);
            settingsPreviewPath=Path.Combine(folder,"settings-visit-"+visit+".png");OpenSettings();
            if(settingsVisits!=Math.Min(2,visit) || Control<Button>("SettingsButton").Resources.Contains("ToolbarNormal")) throw new Exception("Settings visit / icon acknowledgment failed.");
        }
        SelectLanguage(false);
        for(int i=0;i<4;i++) {
            Theme.Colors=Theme.Preset(i);ApplyTheme();SetExpanded(false);window.UpdateLayout();
            var weekly=Control<ProgressBar>("CompactWeekMarker");weekly.ApplyTemplate();var visual=Children(weekly).OfType<SegmentedVisual>().Single();
            if((double)visual.GetValue(SegmentedVisual.MarkerWidthProperty)!=3.5) throw new Exception("Compact marker width mismatch.");
            SavePreview(Path.Combine(folder,"preset-"+i+"-compact.png"));
            SetExpanded(true);window.UpdateLayout();
            if((double)Children(Control<ProgressBar>("WeekMarker")).OfType<SegmentedVisual>().Single().GetValue(SegmentedVisual.MarkerWidthProperty)!=2.0) throw new Exception("Expanded marker width mismatch.");
            SavePreview(Path.Combine(folder,"preset-"+i+"-expanded.png"));
            SetZoom(0.4);window.UpdateLayout();
            if((double)visual.GetValue(SegmentedVisual.MarkerWidthProperty)!=2.0) throw new Exception("Hidden-text marker width mismatch.");
            SetZoom(1);
        }
        Theme.Colors=oldColors;ApplyTheme();
        snapshot.Hour=null;Render();window.UpdateLayout();if(Control<Grid>("WeekPair").Visibility!=Visibility.Visible) throw new Exception("Weekly-only pair hidden.");
        snapshot.Week=null;Render();window.UpdateLayout();if(Control<Grid>("WeekPair").Visibility!=Visibility.Collapsed || Control<ProgressBar>("CompactWeekMarker").Visibility!=Visibility.Collapsed) throw new Exception("Absent week marker not hidden.");
        File.WriteAllText(report,"PASS: bilingual, both toolbar placements, widths100/150/165/180/200/201/280/360/480 at40/80/100/150%; fixed widths, aligned detail columns, bottom/top title and icons on one row, narrow title fallback/hiding, compact icon gaps, text threshold; weekly marker continuity and widths2/3.5, customizable marker, four complete presets and custom restore/cancel; four settings groups, bilingual language heading, first-run red gear, guidance only on first two settings visits; weekly day rounding and last-day hour/minute boundaries; refresh states, legacy theme migration, nullable visibility.\r\nRealized offline WPF layout and previews. No live queries or user preference writes. Physical mouse gestures, actual saved-preference reload and live failures not exercised.\r\n");
    }
    static void CheckTickDrawing() {
        foreach(string kind in new[]{"quota-detail","quota-center","quota-week-detail","quota-week-center","time","time-week"}) {
            var ticks=new SegmentedVisual {Width=200,Height=20,Kind=kind,Caption="",Value=50,Fill=Brushes.Green};
            ticks.Measure(new Size(200,20));ticks.Arrange(new Rect(0,0,200,20));ticks.UpdateLayout();
            var bitmap=new RenderTargetBitmap(200,20,96,96,PixelFormats.Pbgra32);bitmap.Render(ticks);
            var lines=FlattenDrawings(VisualTreeHelper.GetDrawing(ticks)).OfType<GeometryDrawing>().Where(d=>d.Geometry is LineGeometry).ToArray();
            bool time=kind.StartsWith("time"),week=kind.Contains("week"),detail=kind.Contains("detail");
            int majorCount=time && week ? 6 : 4;
            if(lines.Length!=(!time ? 9 : majorCount)) throw new Exception("Tick count incorrect: "+kind);
            double majorWidth=2;
            for(int i=0;i<lines.Length;i++) {
                var line=(LineGeometry)lines[i].Geometry;bool minor=i>=majorCount;
                double expectedX=minor ? (10+(i-majorCount)*20)*2 : (time && week ? (i+1)*200.0/7 : (i+1)*40);
                double expectedY=minor ? (detail ? 15 : 17.5) : time ? 0 : detail ? 10 : 15;
                double minorWidth=2;
                if(Math.Abs(lines[i].Pen.Thickness-(minor ? minorWidth : majorWidth))>0.001 || Math.Abs(line.StartPoint.X-expectedX)>0.001 || Math.Abs(line.StartPoint.Y-expectedY)>0.001 || line.EndPoint.Y!=20) throw new Exception("Tick geometry incorrect: "+kind);
            }
        }
    }
    static void CheckWeeklyTicks(string report) {
        CheckTickDrawing();
        var now=DateTimeOffset.UtcNow;snapshot=new Snapshot {Time=now,Live=true,Hour=new Limit {Remaining=72,Reset=now.ToUnixTimeSeconds()+14340},Week=new Limit {Remaining=49,Reset=now.ToUnixTimeSeconds()+495540}};
        window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=-10000;window.Top=-10000;window.Show();
        baseWidth=280;toolbarTop=false;Theme.Colors=Theme.Preset(0);ApplyTheme();
        foreach(double scale in new[]{0.6,1.0}) foreach(bool full in new[]{false,true}) {
            SelectLanguage(false);SetZoom(scale);SetExpanded(full);window.UpdateLayout();
            SavePreview(Path.Combine(Path.GetDirectoryName(report),"weekly-"+(int)(scale*100)+(full ? "-expanded" : "-compact")+".png"));
        }
        File.WriteAllText(report,"PASS: hourly and weekly quota four major ticks at20/40/60/80 and five minor ticks at10/30/50/70/90; major/minor heights and widths in compact, expanded and hidden-text styles; hourly countdown four equal ticks and weekly countdown six equal ticks unchanged. WPF previews at60/100%. No network queries or preference writes.\r\n");
    }
    static double ColorLuminance(string hex) {
        var color=(Color)ColorConverter.ConvertFromString(hex);
        Func<byte,double> channel=value=>{double c=value/255.0;return c<=0.04045 ? c/12.92 : Math.Pow((c+0.055)/1.055,2.4);};
        return channel(color.R)*0.2126+channel(color.G)*0.7152+channel(color.B)*0.0722;
    }
    static double ColorContrast(string first,string second) {
        double a=ColorLuminance(first),b=ColorLuminance(second);return (Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05);
    }
    static void CheckColorSettings(string report) {
        var folder=Path.GetDirectoryName(report);var original=new Dictionary<string,string>(Theme.Colors);var notes=new List<string>();
        var now=DateTimeOffset.UtcNow;snapshot=new Snapshot {Time=now,Live=true,Hour=new Limit {Remaining=72,Reset=now.ToUnixTimeSeconds()+14340},Week=new Limit {Remaining=49,Reset=now.ToUnixTimeSeconds()+495540}};
        double before=TimePercentAt(snapshot.Week,168,now),after=TimePercentAt(snapshot.Week,168,now.AddMinutes(1));
        if(Math.Abs((before-after)-100.0/(7*24*60))>0.000001 || WeeklyDayMarker(snapshot.Week.Reset,now)!=WeeklyDayMarker(snapshot.Week.Reset,now.AddMinutes(1))
            || FormatCompactReset(snapshot.Week.Reset,now,true)!=FormatCompactReset(snapshot.Week.Reset,now.AddMinutes(1),true)) throw new Exception("Precise weekly timer coupled to day-sized text/arrow.");
        window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=-10000;window.Top=-10000;window.Show();
        baseWidth=280;toolbarTop=false;settingsVisits=2;SetZoom(1);SetExpanded(true);
        foreach(bool en in new[]{false,true}) {SelectLanguage(en);settingsPreviewPath=Path.Combine(folder,en ? "settings-en.png" : "settings-zh.png");OpenSettings();}
        for(int i=0;i<4;i++) {
            var palette=Theme.Preset(i);
            double minimum=palette.Where(pair=>pair.Key.StartsWith("quota") || pair.Key.StartsWith("time") && pair.Key!="timeText" || pair.Key=="track").Min(pair=>ColorContrast(palette["caption"],pair.Value));
            if(minimum<4.5) throw new Exception("Preset text contrast below 4.5: "+Theme.PresetName(i,true)+" "+minimum);
            if(ColorContrast(palette["icons"],palette["background"])<3 || ColorContrast(palette["failure"],palette["background"])<3 || ColorContrast(palette["weekMarker"],palette["track"])<3) throw new Exception("Preset indicator contrast below 3.");
            notes.Add(Theme.PresetName(i,true)+": minimum text contrast "+minimum.ToString("0.00",CultureInfo.InvariantCulture));
            Theme.Colors=palette;ApplyTheme();
            foreach(bool en in new[]{false,true}) foreach(double scale in new[]{0.6,1.0,1.5}) foreach(bool full in new[]{false,true}) {
                SelectLanguage(en);SetZoom(scale);SetExpanded(full);window.UpdateLayout();
                var marker=Control<ProgressBar>(full && scale>textThreshold ? "WeekBar" : "CompactWeekBar");marker.ApplyTemplate();
                var visual=Children(marker).OfType<SegmentedVisual>().Single();
                CheckQuotaArrow(visual,scale);
                SavePreview(Path.Combine(folder,"preset-"+i+(en ? "-en" : "-zh")+"-"+(int)(scale*100)+(full ? "-expanded" : "-compact")+".png"));
            }
        }
        Theme.Colors=Theme.Preset(0);SelectLanguage(false);baseWidth=280;SetExpanded(false);ApplyTheme();
        foreach(double scale in new[]{0.4,1.0,1.5}) {
            snapshot.Time=now;SetZoom(scale);window.UpdateLayout();
            var root=Control<Border>("Root");var bar=Control<ProgressBar>("CompactWeekBar");
            var originalPoint=bar.TranslatePoint(new Point(0,0),window);double originalWidth=bar.ActualWidth;
            if(root.BorderThickness.Left!=1) throw new Exception("Normal outline not restored.");
            if(scale==1) SavePreview(Path.Combine(folder,"fresh-100.png"));
            snapshot.Time=now.AddMinutes(-6);Render();window.UpdateLayout();
            var warningPoint=bar.TranslatePoint(new Point(0,0),window);
            if(Math.Abs((root.BorderThickness.Left-1)*scale-1)>0.001 || Math.Abs(warningPoint.X-originalPoint.X)>1 || Math.Abs(warningPoint.Y-originalPoint.Y)>1 || Math.Abs(bar.ActualWidth-originalWidth)>1) throw new Exception("Warning outline size/layout regression.");
            SavePreview(Path.Combine(folder,"warning-"+(int)(scale*100)+".png"));
            snapshot.Time=now;Render();window.UpdateLayout();if(root.BorderThickness.Left!=1) throw new Exception("Warning outline persisted after fresh data.");
        }
        Theme.Colors=original;ApplyTheme();
        File.WriteAllText(report,"PASS: two light, one medium and one dark color presets; bilingual cards/custom groups and preview/save/cancel behavior; text contrast >=4.5, icon/marker >=3; independent weekly timer/day text/reference; outlined down-facing top arrow with minimum size; stale outline adds1 physical px at40/100/150%, preserves quota placement/width within1 layout pixel and restores normal thickness when fresh.\r\n"+string.Join("\r\n",notes)+"\r\nNo network queries or user preference writes. Physical mouse clicks and persisted-preference reload not exercised. Previous layout/refresh checks reused.\r\n");
    }
    static void CheckQuotaArrow(SegmentedVisual visual,double scale) {
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var drawings=FlattenDrawings(VisualTreeHelper.GetDrawing(visual)).ToArray();
        var arrow=drawings.OfType<GeometryDrawing>().Single(item=>item.Geometry is StreamGeometry);
        var bounds=arrow.Geometry.GetRenderBounds(arrow.Pen);var body=arrow.Geometry.Bounds;
        if(bounds.Top < -0.001 || bounds.Top*scale>1.001 || bounds.Bottom>visual.ActualHeight+0.001 || body.Width*scale<6.99 || body.Height*scale<4.49) throw new Exception("Arrow top/minimum-size boundary failed.");
        var figure=arrow.Geometry.GetFlattenedPathGeometry().Figures.Single();var points=((PolyLineSegment)figure.Segments.Single()).Points;
        if(points[0].Y!=figure.StartPoint.Y || points[1].Y<=figure.StartPoint.Y) throw new Exception("Arrow is not pointing down.");
        int textIndex=Array.FindIndex(drawings,item=>item is GlyphRunDrawing);
        if(textIndex>=0 && Array.IndexOf(drawings,arrow)>textIndex) throw new Exception("Arrow drawn over captions.");
    }
    static void CheckArrowLayout(string report) {
        var now=DateTimeOffset.UtcNow;snapshot=new Snapshot {Time=now,Live=true,Hour=new Limit {Remaining=72,Reset=now.ToUnixTimeSeconds()+14340},Week=new Limit {Remaining=49,Reset=now.ToUnixTimeSeconds()+495540}};
        window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=-10000;window.Top=-10000;window.Show();toolbarTop=false;settingsVisits=2;
        foreach(bool light in new[]{false,true}) foreach(bool en in new[]{false,true}) foreach(double width in new[]{100.0,280.0}) foreach(double scale in new[]{0.4,0.6,1.0,1.5}) foreach(bool full in new[]{false,true}) {
            Theme.Colors=Theme.Preset(light ? 3 : 0);baseWidth=width;SelectLanguage(en);SetZoom(scale);SetExpanded(full);window.UpdateLayout();
            var bar=Control<ProgressBar>(full && scale>textThreshold ? "WeekBar" : "CompactWeekBar");bar.ApplyTemplate();var visual=Children(bar).OfType<SegmentedVisual>().Single();
            foreach(double position in new[]{0.0,3.0/7,6.0/7}) {bar.DataContext=position;window.UpdateLayout();CheckQuotaArrow(visual,scale);}
            if(width==280 && !en) SavePreview(Path.Combine(Path.GetDirectoryName(report),(light ? "light" : "dark")+"-"+(int)(scale*100)+(full ? "-expanded" : "-compact")+".png"));
        }
        File.WriteAllText(report,"PASS: down-facing weekly arrow at quota top, no upward protrusion; physical size at least7px wide/4.5px tall at40/60/100/150%, widths100/280, both modes/languages and light/dark themes; day-reference endpoints clamped inside bar; outline visible and captions rendered above arrow. Existing daily positions, quota ticks and precise countdown remain unchanged.\r\nOffline WPF checks/previews; no network queries or user preference writes. Physical mouse use not exercised.\r\n");
    }
    static IEnumerable<Drawing> FlattenDrawings(Drawing drawing) {
        if(drawing==null) yield break;
        var group=drawing as DrawingGroup;
        if(group!=null) {foreach(var child in group.Children) foreach(var nested in FlattenDrawings(child)) yield return nested;}
        else yield return drawing;
    }
    [STAThread]
    static int Main(string[] args) {
        try {
            if(args.Length==1 && args[0]=="--claude-statusline") return CollectClaudeStatus();
            if(args.Length==1 && args[0]=="--claude-mods") return CollectClaudeStatus(Console.In,Path.Combine(ProviderFolder(),"claude-statusline.json"),"Claude Code Mods");
            if (args.Length == 2 && args[0] == "--snapshot-check") {
                CheckSnapshotSelection(args[1]); return 0;
            }
            checking = args.Length == 2 && (args[0] == "--cards-update-check" || args[0] == "--minimal-check" || args[0] == "--layout-contract-check" || args[0] == "--check" || args[0] == "--ui-check" || args[0] == "--layout-check" || args[0] == "--hover-check" || args[0] == "--toolbar-check" || args[0] == "--reader-check" || args[0] == "--display-check" || args[0]=="--aligned-check" || args[0]=="--colors-check" || args[0]=="--ticks-check" || args[0]=="--arrow-check" || args[0]=="--settings-guide-check");
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Widget.xaml"))
                window = (Window)XamlReader.Load(source);
            ConfigureSegments();
            using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Widget.ico")) {
                var decoder = BitmapDecoder.Create(icon, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                window.Icon = decoder.Frames[0];
            }
            Control<Button>("RefreshButton").Click += (s, e) => Refresh(true);
            if (!checking) { LoadLanguage(); LoadZoom(); LoadDisplayOptions(); LoadTheme(); LoadToolbarPreference(); LoadSettingsGuide(); LoadProviderSelection(); }
            ConfigureInteraction();
            ApplyTheme();
            if(args.Length==2 && args[0]=="--cards-update-check") return CheckCardsAndUpdates(args[1]);
            if(args.Length==2 && args[0]=="--minimal-check") return CheckMinimalMode(args[1]);
            if(args.Length==2 && args[0]=="--settings-guide-check") {settingsFocusedCheck=true;settingsFocusedReport=args[1];settingsVisits=0;for(int visit=0;visit<3;visit++) OpenSettings();File.WriteAllText(args[1],"PASS: five fixed-size pages including About and QR; no overflow or clipped headers; first two visits show five red dots and scoped guides; third clears guides; default card applies exact default palette. No user preference writes or account queries.");window.Close();return 0;}
            if(args.Length==2 && args[0]=="--layout-contract-check") {int result=CheckLayoutContract(args[1]);window.Close();return result;}
            if(args.Length==2 && args[0]=="--arrow-check") {CheckArrowLayout(args[1]);window.Close();return 0;}
            if(args.Length==2 && args[0]=="--ticks-check") {CheckWeeklyTicks(args[1]);window.Close();return 0;}
            if(args.Length==2 && args[0]=="--colors-check") {CheckColorSettings(args[1]);window.Close();return 0;}
            if(args.Length==2 && args[0]=="--aligned-check") {CheckAlignedLayout(args[1]);window.Close();return 0;}
            if(args.Length==2 && args[0]=="--display-check") {CheckDisplayRefresh(args[1]);window.Close();return 0;}
            if (args.Length == 2 && args[0] == "--reader-check") {
                english=true; ApplyLanguage(); CheckLocalReader(args[1]); window.Close(); return 0;
            }
            if (args.Length == 2 && args[0] == "--toolbar-check") {
                CheckToolbarLayout(args[1]); window.Close(); return 0;
            }
            if (args.Length == 2 && args[0] == "--hover-check") {
                CheckHoverBoundary(args[1]); window.Close(); return 0;
            }
            if (args.Length == 2 && args[0] == "--layout-check") {
                CheckRealizedLayout(args[1]); window.Close(); return 0;
            }
            if (args.Length == 2 && args[0] == "--ui-check") {
                CheckUi(args[1]); window.Close(); return 0;
            }
            if (args.Length == 2 && args[0] == "--check") {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                Control<Button>("RefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var frame = new DispatcherFrame();
                var deadline = DateTime.UtcNow.AddSeconds(25);
                var checkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                checkTimer.Tick += (s, e) => { if (!reading || DateTime.UtcNow > deadline) frame.Continue = false; };
                checkTimer.Start(); Dispatcher.PushFrame(frame); checkTimer.Stop();
                if (reading || !Control<Button>("RefreshButton").IsEnabled) throw new Exception("Refresh did not finish.");
                if (snapshot == null) throw new Exception("No local quota snapshot found.\r\n" + diagnostic);
                if (queryFailed || !snapshot.Live) throw new Exception("Active account query failed: " + queryError);
                var old = new Snapshot { Time = snapshot.Time.AddMinutes(-1), Hour = snapshot.Hour, Week = snapshot.Week };
                if (NewerSnapshot(snapshot, old) != snapshot) throw new Exception("Stale local record overwrote live query.");
                File.WriteAllText(args[1], string.Format("UI and embedded icon loaded successfully\r\nManual refresh completed a live account query\r\nStale local snapshot protection passed\r\nUpdated: {0:o}\r\n5-hour remaining: {1}%\r\nWeekly remaining: {2}%\r\n", snapshot.Time, snapshot.Hour.Remaining, snapshot.Week.Remaining));
                window.Close(); return 0;
            }
            window.Left = SystemParameters.WorkArea.Right - window.Width - 20;
            window.Top = SystemParameters.WorkArea.Bottom - window.Height - 20;
            StartRefreshTimers();
            window.Loaded += (s, e) => { if(!checking) InitializeUpdates(); Refresh(true); UpdateSettingsHint(); if (!window.IsMouseOver) collapseTimer.Start(); };
            Render();
            new Application().Run(window);
            return 0;
        } catch (Exception ex) {
            if (checking) File.WriteAllText(args[1], ex.ToString());
            else MessageBox.Show(Text("无法启动额度窗口：", "Could not start usage widget: ") + ex.Message, Text("Codex 额度启动失败", "Codex Usage startup failed"));
            return 1;
        }
    }
}
