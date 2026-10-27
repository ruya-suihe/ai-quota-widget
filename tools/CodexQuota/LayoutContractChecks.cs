using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Web.Script.Serialization;

// Synthetic, offline checks run through Render and the real WPF visual tree.
// The oracle is L1-L8, not WidgetLayout's calculations or its constants.
partial class QuotaWidget {
    static int CheckCardsAndUpdates(string report) {
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        var newer=AppServices.ReadUpdate(url=>new Dictionary<string,object>{{"tag_name","v99.0.0"},{"html_url",AppServices.ProjectUrl+"/releases/tag/v99.0.0"}});
        ContractRequire(newer.Url.Length>0,"New release not detected");
        var current=AppServices.ReadUpdate(url=>new Dictionary<string,object>{{"tag_name","v0.1.10"},{"html_url",AppServices.ProjectUrl+"/releases/tag/v0.1.10"}});
        ContractRequire(current.Error=="" && current.Url=="" && current.Version=="v0.1.10","Current version mishandled");
        var noRelease=AppServices.ReadUpdate(url=>{if(url.EndsWith("/latest")) throw new AppServices.UpdateRequestException(404,System.Net.WebExceptionStatus.ProtocolError);return new Dictionary<string,object>{{"id",1421835446}};});
        ContractRequire(noRelease.NoRelease && noRelease.Error=="","Empty public releases reported as failure");
        foreach(var sample in new[]{Tuple.Create(404,System.Net.WebExceptionStatus.ProtocolError,"unavailable"),Tuple.Create(403,System.Net.WebExceptionStatus.ProtocolError,"limited"),Tuple.Create(429,System.Net.WebExceptionStatus.ProtocolError,"limited"),Tuple.Create(500,System.Net.WebExceptionStatus.ProtocolError,"http:500"),Tuple.Create(0,System.Net.WebExceptionStatus.Timeout,"timeout"),Tuple.Create(0,System.Net.WebExceptionStatus.NameResolutionFailure,"dns"),Tuple.Create(0,System.Net.WebExceptionStatus.SecureChannelFailure,"tls")}) {
            var result=AppServices.ReadUpdate(url=>{throw new AppServices.UpdateRequestException(sample.Item1,sample.Item2);});
            ContractRequire(result.Error==sample.Item3 && result.Url=="","Update failure misclassified: "+sample.Item3);
        }
        ContractRequire(AppServices.ReadUpdate(url=>new Dictionary<string,object>()).Error=="invalid","Invalid payload accepted");
        ContractRequire(!AppServices.ValidReleaseUrl("https://github.com/another/project/releases/tag/v99") && !AppServices.ValidReleaseUrl(AppServices.ProjectUrl+"/releases/tag/"),"Foreign or empty release URL accepted");
        customColorCards.Clear();selectedColorCardId="";Theme.Colors=Theme.Defaults();
        var first=new ColorCard {Id="legacy-a",Name="旧卡 A",Colors=Theme.Defaults()};
        var second=new ColorCard {Id="legacy-b",Name="旧卡 B",Colors=Theme.Defaults()};
        customColorCards.Add(first);customColorCards.Add(second);selectedColorCardId=second.Id;
        Button[] cards;CreatePresetCards(index=>{},out cards);UpdatePresetCards(cards);
        Func<Button,bool> marked=button=>((TextBlock)((StackPanel)((Border)button.Content).Child).Children[0]).Text.EndsWith(" ✓");
        ContractRequire(cards.Count(marked)==1 && marked(cards.Single(card=>((ColorCard)card.Tag).Id==second.Id)),"Duplicate cards selected together");
        try {AddColorCard("重复配色",Theme.Defaults());throw new InvalidOperationException("Duplicate palette accepted");}
        catch(ArgumentException ex) {ContractRequire(ex.Message.Contains("默认配色") || ex.Message.Contains("Default colors"),"Duplicate warning omits existing name");}
        var equivalent=Theme.Defaults();equivalent["caption"]="#FFF5F5F5";
        var explicitAlpha=new Dictionary<string,string>(equivalent);explicitAlpha["caption"]="#F5F5F5";
        ContractRequire(SameColorSettings(equivalent,explicitAlpha),"Equivalent color spellings differ");
        var unique=Theme.Defaults();unique["background"]="#123457";
        AddColorCard("测试自定义",unique);string addedId=selectedColorCardId;
        ContractRequire(customColorCards.Count==3 && customColorCards.Last().Id==addedId,"Custom card not saved or selected");
        Theme.Colors=new Dictionary<string,string>(unique);DeleteColorCard(customColorCards.Last());
        ContractRequire(customColorCards.Count==2 && SameColorSettings(Theme.Colors,unique) && ResolveSelectedColorCard()=="","Deleting active card changed colors");
        try {DeleteColorCard(AvailableColorCards()[0]);throw new InvalidOperationException("Builtin card deleted");}catch(ArgumentException) {}
        Theme.Colors=Theme.Defaults();selectedColorCardId=second.Id;
        string persisted="";SaveActiveColorSettings(json=>persisted=json);
        selectedColorCardId=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(persisted)["_selectedCardId"];
        ContractRequire(ResolveSelectedColorCard()==second.Id,"Persisted selection loses duplicate card identity");
        Theme.Colors=unique;
        try {SaveActiveColorSettings(json=>{throw new IOException("Synthetic write failure");});throw new InvalidOperationException("Write failure swallowed");}catch(IOException) {}
        ContractRequire(selectedColorCardId==second.Id,"Failed persistence loses original card identity");
        Theme.Colors=Theme.Defaults();
        OpenAppearance();ContractRequire(selectedColorCardId==second.Id && SameColorSettings(Theme.Colors,Theme.Defaults()),"Cancel loses original color card identity");
        settingsFocusedCheck=true;settingsFocusedReport=report;settingsVisits=0;snapshot=LayoutFixture(true,true);
        for(int visit=0;visit<3;visit++) OpenSettings();
        File.WriteAllText(report,"PASS: update newer/current/empty releases, HTTP errors, DNS/timeout/TLS errors, malformed/foreign URLs; single selection for legacy duplicate cards; duplicate names in warnings; equivalent colors; add/delete with active colors preserved; builtin deletion refused; four-page sizing and guidance. Synthetic offline data; no user settings changed.");window.Close();return 0;
    }
    static IEnumerable<Drawing> MinimalDrawings(Drawing drawing) {
        if(drawing==null) yield break;
        yield return drawing;
        var group=drawing as DrawingGroup;
        if(group!=null) foreach(var child in group.Children) foreach(var nested in MinimalDrawings(child)) yield return nested;
    }
    static int CheckMinimalMode(string report) {
        var folder=Path.GetDirectoryName(report);Directory.CreateDirectory(folder);
        window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=window.Top=-10000;window.Show();
        try {
            var now=DateTimeOffset.UtcNow;
            ContractRequire(Math.Abs(HourlyReferenceMarker(now.ToUnixTimeSeconds()+9000,now)-0.5)<0.0001,"Hourly midpoint");
            ContractRequire(HourlyReferenceMarker(0,now)==-1 && HourlyReferenceMarker(now.ToUnixTimeSeconds()-1,now)==-1 && HourlyReferenceMarker(now.ToUnixTimeSeconds()+18001,now)==-1,"Unknown/expired hourly marker");
            var serializer=new JavaScriptSerializer();
            var old=serializer.Deserialize<ToolbarLayout>("{\"Order\":[\"SettingsButton\",\"LockButton\",\"RefreshButton\",\"CloseButton\"]}");
            ContractRequire(!old.Minimal,"Old preferences enable minimal mode");
            old.Minimal=true;ContractRequire(serializer.Deserialize<ToolbarLayout>(serializer.Serialize(old)).Minimal,"Minimal preference roundtrip");
            minimalMode=true;waveEnabled=true;waveTravel=2;
            foreach(double width in new[]{100.0,180.0,480.0}) foreach(double scale in new[]{0.4,1.0,1.5}) foreach(bool top in new[]{false,true}) foreach(bool hover in new[]{false,true}) {
                baseWidth=width;zoom=scale;toolbarTop=top;expanded=hover;pinIcons=false;snapshot=LayoutFixture(true,true);Render();window.UpdateLayout();
                ContractRequire(Control<StackPanel>("Details").Visibility==Visibility.Collapsed && Control<Grid>("Compact").Visibility==Visibility.Visible,"Minimal expanded on hover");
                ContractRequire(Control<TextBlock>("TitleLabel").Visibility==Visibility.Collapsed,"Minimal title visible");
                ContractRequire(Control<Grid>("Toolbar").IsVisible==hover,"Minimal toolbar ignores preference");
                foreach(string prefix in new[]{"Hour","Week"}) {
                    var bar=Control<ProgressBar>("Compact"+prefix+"Bar");var visual=Children(bar).OfType<SegmentedVisual>().Single();
                    ContractRequire(bar.IsVisible && bar.Uid=="" && visual.Label=="","Minimal text visible");
                    ContractRequire(!Control<ProgressBar>("Compact"+prefix+"TimeBar").IsVisible,"Minimal countdown bar visible");
                    ContractRequire(visual.DayMarker>=0 && visual.DayMarker<=1,"Reference marker missing");
                    var drawings=MinimalDrawings(VisualTreeHelper.GetDrawing(visual)).ToArray();
                    ContractRequire(!drawings.OfType<GlyphRunDrawing>().Any(),"Minimal glyphs rendered");
                    ContractRequire(!drawings.OfType<GeometryDrawing>().Any(d=>d.Pen!=null && d.Pen.Brush.ToString()==Theme.Brush("ticks").ToString()),"Minimal ticks rendered");
                    ContractRequire(drawings.OfType<GeometryDrawing>().Count(d=>d.Brush!=null && d.Brush.ToString()==Theme.Brush("weekMarker").ToString())==1,"Reference triangle not rendered exactly once");
                    var triangle=drawings.OfType<GeometryDrawing>().Single(d=>d.Brush!=null && d.Brush.ToString()==Theme.Brush("weekMarker").ToString()).Geometry;
                    double baseY=triangle.GetFlattenedPathGeometry().Figures[0].StartPoint.Y;
                    ContractRequire(prefix=="Week" ? Math.Abs(baseY-triangle.Bounds.Bottom)<.01 && baseY>visual.ActualHeight/2
                        : Math.Abs(baseY-triangle.Bounds.Top)<.01 && baseY<visual.ActualHeight/2,"Minimal weekly arrow must point up from the bottom; hourly arrow stays at the top");
                }
                var rows=Control<Grid>("Compact").RowDefinitions;
                ContractRequire(rows[1].ActualHeight==0 && rows[4].ActualHeight==0,"Minimal timer space remains");
                if(!hover) {
                    var root=Control<Border>("Root");
                    var hour=LayoutBounds(Control<ProgressBar>("CompactHourBar"));
                    var week=LayoutBounds(Control<ProgressBar>("CompactWeekBar"));
                    var gaps=new[]{hour[0]-2/zoom,root.ActualWidth-hour[0]-hour[2]-2/zoom,hour[1],root.ActualHeight-week[1]-week[3]};
                    double tolerance=Math.Max(1,1/(zoom*VisualTreeHelper.GetDpi(window).DpiScaleY))+0.01;
                    ContractRequire(Math.Abs(gaps[0]-gaps[1])<=tolerance && Math.Abs(gaps[2]-gaps[3])<=tolerance && Math.Abs((gaps[0]+gaps[1]-gaps[2]-gaps[3])/2)<=tolerance,"Minimal outer spacing differs: width="+width+", scale="+scale+", gaps="+string.Join(",",gaps));
                }
            }
            baseWidth=180;zoom=1;toolbarTop=false;pinIcons=false;expanded=false;Render();SavePreview(Path.Combine(folder,"minimal-padding.png"));
            baseWidth=180;zoom=1;toolbarTop=false;pinIcons=true;expanded=true;Render();SavePreview(Path.Combine(folder,"minimal-actual.png"));
            minimalMode=false;noExpand=false;expanded=false;baseWidth=100;zoom=1.5;
            foreach(bool en in new[]{false,true}) foreach(double remaining in new[]{8.0,49.0,100.0}) {
                english=en;ApplyLanguage();snapshot=LayoutFixture(true,true);snapshot.Hour.Remaining=remaining;Render();window.UpdateLayout();
                var visual=Children(Control<ProgressBar>("CompactHourBar")).OfType<SegmentedVisual>().Single();
                ContractRequire(en ? visual.Label.StartsWith("5") : visual.Label=="五","Narrow hourly row loses its identity");
                double gap=visual.ActualWidth-2*LayoutMetrics.BarTextInset-TextWidth(visual.Caption,LayoutMetrics.NarrowQuotaFontSize,FontWeights.Bold)-TextWidth(visual.Label,LayoutMetrics.NarrowQuotaFontSize,FontWeights.Bold);
                ContractRequire(gap>=LayoutMetrics.NarrowLabelGap-0.5,"Narrow hourly label overlaps caption");
                if(!en && remaining==49) SavePreview(Path.Combine(folder,"hour-label-100-150.png"));
            }
            zoom=0.8;Render();ContractRequire((string)Control<ProgressBar>("CompactHourBar").GetValue(SegmentedVisual.LabelProperty)=="","Text threshold no longer hides hourly label");
            zoom=1;
            minimalMode=false;noExpand=false;expanded=true;waveEnabled=false;baseWidth=360;
            foreach(double width in new[]{180.0,360.0}) foreach(bool en in new[]{false,true}) foreach(double remaining in new[]{8.0,68.0,100.0}) {
                baseWidth=width;
                english=en;ApplyLanguage();snapshot=LayoutFixture(true,true);snapshot.Hour.Remaining=remaining;Render();window.UpdateLayout();
                foreach(string prefix in new[]{"Hour","Week"}) {
                    var value=Control<TextBlock>(prefix+"Value");var reset=Control<TextBlock>(prefix+"Reset");
                    var divider=Control<Grid>(prefix+"Info").Children.OfType<TextBlock>().Single(text=>text.Text=="|" && Grid.GetColumn(text)==3);
                    ContractRequire(value.TextAlignment==TextAlignment.Left && reset.TextAlignment==TextAlignment.Left && divider.TextAlignment==TextAlignment.Center,"Separator alignment");
                    ContractRequire(Math.Abs(value.TranslatePoint(new Point(),window).X-Control<TextBlock>("HourValue").TranslatePoint(new Point(),window).X)<0.01,"Percentages do not share a left edge");
                    double center=divider.TranslatePoint(new Point(divider.ActualWidth/2,0),window).X;
                    double left=center-value.TranslatePoint(new Point(value.ActualWidth,0),window).X;
                    double right=reset.TranslatePoint(new Point(),window).X-center;
                    ContractRequire(Math.Abs(left-right)<0.01,"Separator side spacing differs");
                }
                ContractRequire(Control<ProgressBar>("HourTimeBar").IsVisible && Control<ProgressBar>("WeekTimeBar").IsVisible,"Normal countdown bars not restored");
            }
            english=false;baseWidth=180;ApplyLanguage();Render();SavePreview(Path.Combine(folder,"expanded-spacing.png"));
            File.WriteAllText(report,"PASS: minimal mode across 100/180/480 widths, 40/100/150% scales, top/bottom toolbar and hover; no text/ticks/countdown or empty timer rows; both reference triangles; preference compatibility; normal restoration and symmetric separator spacing in both languages. Offline fixture only.");return 0;
        } finally {window.Close();}
    }
    class LayoutCase {
        public string Id;
        public double Width, Scale;
        public bool English, Top, Expanded, Hour = true, Week = true;
        public LayoutCase(string id, double width, double scale, bool en, bool top, bool detail) {
            Id=id; Width=width; Scale=scale; English=en; Top=top; Expanded=detail;
        }
    }
    static void ContractRequire(bool value, string rule) {
        if (!value) throw new InvalidOperationException(rule);
    }
    static Snapshot LayoutFixture(bool hour, bool week) {
        var now=DateTimeOffset.UtcNow;
        return new Snapshot {Time=now, Live=true,
            Hour=hour ? new Limit {Remaining=49, Reset=now.ToUnixTimeSeconds()+14310} : null,
            Week=week ? new Limit {Remaining=90, Reset=now.ToUnixTimeSeconds()+495570} : null};
    }
    static void SetLayoutCase(LayoutCase sample) {
        snapshot=LayoutFixture(sample.Hour,sample.Week);
        reading=activeReading=queryFailed=false; localReadWarning="";
        english=sample.English; toolbarTop=sample.Top; expanded=sample.Expanded;
        baseWidth=sample.Width; zoom=sample.Scale; textThreshold=0.8;
        locked=noExpand=false; pinIcons=true; widgetOpacity=1;
        waveEnabled=waveOverText=false; waveTravel=2;
        toolbarOrder=(string[])DefaultToolbarOrder.Clone();
        Control<TextBlock>("EmptyUsage").Margin=new Thickness(0,expanded ? 30 : 0,0,6);
        ApplyLanguage();
        // Freeze the first-use settings pulse only in this fixture; its animation
        // phase would otherwise create unrelated pixel differences between runs.
        var settingsIcon=(Viewbox)Control<Button>("SettingsButton").Content;
        settingsIcon.BeginAnimation(UIElement.OpacityProperty,null);settingsIcon.Opacity=1;
        // Freeze only fixture timer fill, so visual comparisons are independent of wall time.
        foreach(string prefix in new[]{"Hour","Week"}) foreach(string name in new[]{prefix+"TimeBar","Compact"+prefix+"TimeBar"})
            Control<ProgressBar>(name).Value=prefix=="Hour" ? 79 : 82;
        window.UpdateLayout();
    }
    static double[] LayoutBounds(FrameworkElement element) {
        if(!element.IsVisible) return new double[0];
        Point p=element.TranslatePoint(new Point(),Control<Border>("Root"));
        return new[]{p.X,p.Y,element.ActualWidth,element.ActualHeight};
    }
    static void TextBaselines(Drawing drawing, Matrix transform, List<double> baselines) {
        var group=drawing as DrawingGroup;
        if(group!=null) {
            Matrix combined=group.Transform==null ? Matrix.Identity : group.Transform.Value; combined.Append(transform);
            foreach(var child in group.Children) TextBaselines(child,combined,baselines);
        } else {
            var glyph=drawing as GlyphRunDrawing;
            if(glyph!=null) baselines.Add(transform.Transform(glyph.GlyphRun.BaselineOrigin).Y);
        }
    }
    static Dictionary<string,object> InspectLayout(bool detail, bool icons, bool hiddenText, bool hour, bool week) {
        double pixels=zoom*VisualTreeHelper.GetDpi(window).DpiScaleY;
        double tolerance=1/Math.Max(0.01,pixels)+0.001;
        var root=Control<Border>("Root");
        var toolbar=Control<Grid>("Toolbar");
        ContractRequire((Control<StackPanel>("Details").Visibility==Visibility.Visible)==detail,"L4: expanded/compact state changed");
        ContractRequire((toolbar.Visibility==Visibility.Visible)==icons,"L5: toolbar visibility changed");
        ContractRequire(Math.Abs(window.Width-baseWidth*zoom)<=1.001,"L3: widget width changed");
        root.Measure(new Size(window.Width,double.PositiveInfinity));
        ContractRequire(root.DesiredSize.Height<=window.Height+1.001,"L3: vertical clipping");
        var columns=Control<Grid>("HourInfo").ColumnDefinitions.Select(c=>c.Width.Value).ToArray();
        var weekColumns=Control<Grid>("WeekInfo").ColumnDefinitions.Select(c=>c.Width.Value).ToArray();
        ContractRequire(columns.Take(2).SequenceEqual(weekColumns.Take(2)),"L2: hourly/weekly label columns differ");
        ContractRequire(columns.Take(5).Sum()<=baseWidth-2*(4/zoom+1)+0.1,"L3: detail columns overflow");
        var title=Control<TextBlock>("TitleLabel");
        if(icons && detail && title.Visibility==Visibility.Visible) {
            var titlePosition=title.TranslatePoint(new Point(),toolbar);
            foreach(string name in toolbarOrder) {
                var button=Control<Button>(name);var p=button.TranslatePoint(new Point(),toolbar);
                ContractRequire(title.Parent==toolbar && Math.Abs(titlePosition.Y+title.ActualHeight-p.Y-button.ActualHeight)<=tolerance,
                    "L1: title moved without matching the icons");
                ContractRequire(titlePosition.X+title.ActualWidth<=p.X+tolerance,"L1: title/icon overlap");
            }
        }
        var buttons=new List<object>();
        foreach(string name in toolbarOrder) {
            var button=Control<Button>(name);
            if(icons) {
                Point p=button.TranslatePoint(new Point(),toolbar);
                ContractRequire(p.X>=-tolerance && p.X+button.ActualWidth<=toolbar.ActualWidth+tolerance,"L3: icon overflow");
            }
            buttons.Add(new {Name=name, Bounds=LayoutBounds(button), IconBounds=LayoutBounds((Viewbox)button.Content)});
        }
        var rows=new List<object>();
        foreach(string prefix in new[]{"Hour","Week"}) {
            bool present=prefix=="Hour" ? hour : week;
            var bar=Control<ProgressBar>("Compact"+prefix+"Bar");
            ContractRequire((bar.Visibility==Visibility.Visible)==present,"L6: quota row visibility changed");
            var info=Control<Grid>(prefix+"Info");
            if(detail && present) {
                foreach(var text in info.Children.OfType<TextBlock>().Where(t=>t.Visibility==Visibility.Visible && t.Text!="|"))
                    ContractRequire(text.DesiredSize.Width<=text.ActualWidth+tolerance,"L3: detail text clipped");
            }
            bar.ApplyTemplate();var visual=Children(bar).OfType<SegmentedVisual>().Single();
            if(present && !detail) {
                ContractRequire((visual.Caption.Length==0)==hiddenText,"L4: compact text threshold changed");
                if(hiddenText) ContractRequire(visual.Label.Length==0,"L4: compact label ignores threshold");
                ContractRequire(Control<TextBlock>("Compact"+prefix+"Label").Visibility==Visibility.Collapsed,"L2: duplicate old label is visible");
            }
            if(visual.IsVisible) {
                var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(visual.ActualWidth)),Math.Max(1,(int)Math.Ceiling(visual.ActualHeight)),96,96,PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var baselines=new List<double>();TextBaselines(VisualTreeHelper.GetDrawing(visual),Matrix.Identity,baselines);
                if(baselines.Count>1) ContractRequire(baselines.Max()-baselines.Min()<=tolerance,"L2: compact label/value baselines differ");
                if(prefix=="Week") CheckLayoutMarker(visual,tolerance);
            }
            rows.Add(new {Prefix=prefix, CompactBounds=LayoutBounds(bar), DetailBounds=LayoutBounds(info), Caption=bar.Uid,
                Label=visual.Label, Kind=bar.Tag, Marker=(double)bar.DataContext});
        }
        ContractRequire((Control<TextBlock>("EmptyUsage").Visibility==Visibility.Visible)==(!hour && !week && !hiddenText),"L6: empty state changed");
        return new Dictionary<string,object>{{"Window",new[]{window.Width,window.Height}}, {"Title",LayoutBounds(title)},
            {"Toolbar",LayoutBounds(toolbar)}, {"QuotaArea",LayoutBounds(Control<Grid>("QuotaArea"))},
            {"Columns",columns}, {"Buttons",buttons}, {"Rows",rows}};
    }
    static bool IsLayoutTriangle(Geometry geometry) {
        if(!(geometry is StreamGeometry)) return false;
        var figures=geometry.GetFlattenedPathGeometry().Figures;
        if(figures.Count!=1 || !figures[0].IsClosed) return false;
        int lines=0;
        foreach(var segment in figures[0].Segments) {
            var polyline=segment as PolyLineSegment;
            if(polyline!=null) lines+=polyline.Points.Count;
            else if(segment is LineSegment) lines++;
            else return false;
        }
        // WPF flattening can materialize the closing edge as a third line.
        return lines==2 || lines==3;
    }
    static void CheckLayoutMarker(SegmentedVisual visual,double tolerance) {
        if(visual.DayMarker<0) return;
        var drawings=FlattenDrawings(VisualTreeHelper.GetDrawing(visual)).ToArray();
        var triangle=drawings.OfType<GeometryDrawing>().Single(d=>IsLayoutTriangle(d.Geometry));
        Rect bounds=triangle.Geometry.GetRenderBounds(triangle.Pen);
        ContractRequire(bounds.Left>=-tolerance && bounds.Right<=visual.ActualWidth+tolerance,"L7: arrow exceeds horizontal bounds");
        bool outside=visual.Kind.EndsWith("center") && (visual.Caption.Length>0 || visual.Label.Length>0);
        ContractRequire(outside ? bounds.Bottom<=tolerance : bounds.Top>=-tolerance && bounds.Bottom<=visual.ActualHeight+tolerance,
            "L7: arrow changed its text-dependent placement");
        int text=Array.FindIndex(drawings,d=>d is GlyphRunDrawing);
        if(text>=0) ContractRequire(Array.IndexOf(drawings,triangle)<text,"L7: arrow draws over text");
    }
    static void CheckBarHeightOptions() {
        var serializer=new JavaScriptSerializer();var legacy=serializer.Deserialize<DisplayOptions>("{}");
        ContractRequire(legacy.HourBarHeight==19 && legacy.WeekBarHeight==9.5,"Legacy display height defaults");
        var saved=serializer.Deserialize<DisplayOptions>(serializer.Serialize(new DisplayOptions {HourBarHeight=25.5,WeekBarHeight=8.5}));
        ContractRequire(saved.HourBarHeight==25.5 && saved.WeekBarHeight==8.5,"Fractional height persistence");
        double oldHour=hourBarHeight,oldWeek=weekBarHeight,oldScale=zoom,oldThreshold=textThreshold;
        bool oldExpanded=expanded,oldMinimal=minimalMode,oldNoExpand=noExpand;var oldSnapshot=snapshot;
        try {
            snapshot=LayoutFixture(true,true);noExpand=false;textThreshold=.8;hourBarHeight=6;weekBarHeight=36;
            for(int mode=0;mode<4;mode++) {
                expanded=mode==0;minimalMode=mode==2;zoom=mode==3?.6:1;Render();window.UpdateLayout();
                string prefix=mode==0?"":"Compact";double expectedHour=mode==1?19:6;
                ContractRequire(Math.Abs(Control<ProgressBar>(prefix+"HourBar").Height-expectedHour)<.01,"Hourly height mode "+mode);
                ContractRequire(Math.Abs(Control<ProgressBar>(prefix+"WeekBar").Height-36)<.01,"Weekly height mode "+mode);
                if(mode>0) {
                    var hour=Control<ProgressBar>("CompactHourBar");var timer=Control<ProgressBar>("CompactHourTimeBar");
                    ContractRequire(timer.Margin.Top>=hour.Height+2,"Height-dependent countdown overlap");
                }
            }
            ContractRequire(LayoutMetrics.NormalizeBarHeight(-1,19)==6 && LayoutMetrics.NormalizeBarHeight(100,19)==36
                && LayoutMetrics.NormalizeBarHeight(double.NaN,19)==19,"Invalid height normalization");
        } finally {hourBarHeight=oldHour;weekBarHeight=oldWeek;zoom=oldScale;textThreshold=oldThreshold;expanded=oldExpanded;minimalMode=oldMinimal;noExpand=oldNoExpand;snapshot=oldSnapshot;Render();}
    }
    static int CheckLayoutContract(string report) {
        var folder=Path.GetDirectoryName(Path.GetFullPath(report));Directory.CreateDirectory(folder);
        window.ShowActivated=false;window.ShowInTaskbar=false;window.Left=-10000;window.Top=-10000;window.Show();
        CheckBarHeightOptions();
        var samples=new List<LayoutCase>();
        foreach(bool en in new[]{false,true}) foreach(bool top in new[]{false,true}) foreach(bool detail in new[]{false,true})
            samples.Add(new LayoutCase((en?"en":"zh")+(top?"-top":"-bottom")+(detail?"-expanded":"-compact"),180,1,en,top,detail));
        samples.Add(new LayoutCase("width-200",200,1,false,false,false));
        samples.Add(new LayoutCase("width-201",201,1,false,false,false));
        samples.Add(new LayoutCase("small-40",100,0.4,false,true,false));
        samples.Add(new LayoutCase("large-150",480,1.5,true,true,true));
        samples.Add(new LayoutCase("threshold-80",180,0.8,false,false,false));
        samples.Add(new LayoutCase("threshold-81",180,0.81,false,false,false));
        samples.Add(new LayoutCase("expanded-80",180,0.8,false,false,true));
        samples.Add(new LayoutCase("hour-only",180,1,false,false,true){Week=false});
        samples.Add(new LayoutCase("week-only",180,1,false,false,true){Hour=false});
        samples.Add(new LayoutCase("no-data",180,1,false,false,true){Hour=false,Week=false});
        var results=new List<object>();var errors=new List<string>();
        foreach(var sample in samples) {
            try {
                SetLayoutCase(sample);
                bool hidden=sample.Id=="small-40" || sample.Id=="threshold-80";
                var facts=InspectLayout(sample.Expanded,true,hidden,sample.Hour,sample.Week);
                SavePreview(Path.Combine(folder,sample.Id+".png"));
                results.Add(new {Id=sample.Id, Facts=facts});
            } catch(Exception ex) {errors.Add(sample.Id+": "+ex.Message);}
        }
        var transitions=new List<object>();
        Action<string,bool,bool,bool> record=(id,detail,icons,hidden)=>{
            try {window.UpdateLayout();transitions.Add(new {Id=id,Facts=InspectLayout(detail,icons,hidden,true,true)});}
            catch(Exception ex){errors.Add(id+": "+ex.Message);}
        };
        SetLayoutCase(new LayoutCase("transition",180,1,false,false,true));
        SelectLanguage(true);record("switch-language",true,true,false);
        toolbarTop=true;Render();record("switch-toolbar",true,true,false);
        SetZoom(0.8);record("zoom-expanded",true,true,false);
        SetExpanded(false);record("collapse-at-threshold",false,true,true);
        SetZoom(1);record("show-compact-text",false,true,false);
        noExpand=true;SetExpanded(true);Render();record("disable-expansion",false,true,false);
        noExpand=false;Render();record("restore-expansion",true,true,false);
        SetPinnedIcons(false);SetExpanded(false);record("hide-unpinned-icons",false,false,false);
        SetExpanded(true);record("show-icons-on-expand",true,true,false);
        SetPinnedIcons(true);SetExpanded(false);record("pin-icons-again",false,true,false);
        locked=true;UpdateLock();SetExpanded(true);SetExpanded(false);record("locked-stays-expanded",true,true,false);
        locked=false;UpdateLock();SetExpanded(false);record("unlock-collapse",false,true,false);
        // Exercise the preserved drawing options without starting background refresh services.
        waveEnabled=true;waveOverText=true;waveTravel=5;Render();record("wave-options",false,true,false);
        waveEnabled=false;Render();
        File.WriteAllText(report,new JavaScriptSerializer().Serialize(new {CaseCount=samples.Count,Cases=results,Transitions=transitions,Errors=errors}));
        return errors.Count==0 ? 0 : 1;
    }
}
