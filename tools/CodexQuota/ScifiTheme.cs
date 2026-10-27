using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class ScifiAssets {
    static readonly Dictionary<string,BitmapSource> softened=new Dictionary<string,BitmapSource>();
    static readonly Dictionary<string,BitmapSource> images=new Dictionary<string,BitmapSource>();
    internal static BitmapSource Load(string name) {
        BitmapSource result;if(images.TryGetValue(name,out result))return result;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Scifi."+name)) {
            if(stream==null)throw new InvalidOperationException("Missing embedded scene asset: "+name);
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();result=image;
        }
        images[name]=result;return result;
    }
    internal static BitmapSource Softened(string name){
        BitmapSource cached;if(softened.TryGetValue(name,out cached))return cached;
        var image=Load(name);int w=192,h=Math.Max(1,(int)Math.Round(192.0*image.PixelHeight/image.PixelWidth));
        var scaled=new TransformedBitmap(image,new ScaleTransform((double)w/image.PixelWidth,(double)h/image.PixelHeight));
        var converted=new FormatConvertedBitmap(scaled,PixelFormats.Pbgra32,null,0);
        var pixels=new byte[w*h*4];converted.CopyPixels(pixels,w*4,0);
        // Cache a small, three-pass box blur once per image; no live blur effect per frame.
        for(int pass=0;pass<3;pass++)for(int axis=0;axis<2;axis++){
            var result=new byte[pixels.Length];int radius=6;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)for(int channel=0;channel<4;channel++){
                int total=0;for(int k=-radius;k<=radius;k++){
                    int sx=axis==0?Math.Max(0,Math.Min(w-1,x+k)):x,sy=axis==1?Math.Max(0,Math.Min(h-1,y+k)):y;
                    total+=pixels[(sy*w+sx)*4+channel];
                }
                result[(y*w+x)*4+channel]=(byte)(total/(radius*2+1));
            }
            pixels=result;
        }
        cached=BitmapSource.Create(w,h,96,96,PixelFormats.Pbgra32,null,pixels,w*4);cached.Freeze();softened[name]=cached;return cached;
    }
}

// This renderer owns only theme drawings and its clock. It never queries accounts or writes preferences.
// Background, animated subjects, and data are independent sibling visuals; data is always on top.
internal static class ScifiTheme {
    sealed class PictureLayer:FrameworkElement {
        internal Border Owner;
        internal Drawing Frame;
        internal void SetFrame(Drawing frame){Frame=frame;InvalidateVisual();}
        protected override void OnRender(DrawingContext c){
            if(Frame==null || Owner==null)return;
            Point p=TranslatePoint(new Point(),Owner);c.PushTransform(new TranslateTransform(-p.X,-p.Y));
            c.PushClip(new RectangleGeometry(new Rect(0,0,Owner.ActualWidth,Owner.ActualHeight),Owner.CornerRadius.TopLeft,Owner.CornerRadius.TopLeft));
            c.DrawDrawing(Frame);c.Pop();c.Pop();
        }
    }
    internal const int PresetIndex=7;
    internal static bool Enabled {get;private set;}
    internal static bool IsTidal {get{return Enabled && activePreset==TidalScene.PresetIndex;}}
    internal static bool IsInk {get{return Enabled && activePreset==InkScene.PresetIndex;}}
    internal static bool IsSteam {get{return Enabled && activePreset==SteamScene.PresetIndex;}}
    internal static bool IsStarry {get{return Enabled && activePreset==StarryScene.PresetIndex;}}
    internal static bool Painted {get{return IsTidal || IsInk || IsSteam || IsStarry;}}
    internal static bool DrawsAcrossTimeBars {get{return Painted;}}
    internal static bool TimerRunning {get{return timer!=null && timer.IsEnabled;}}
    internal static double Seconds {get;private set;}
    internal static bool LockArtworkAspect;
    internal static string ArtworkName(int index){
        return index==StarryScene.PresetIndex?"starry-night.png":index==InkScene.PresetIndex?"ink-landscape.png":index==SteamScene.PresetIndex?"steam-workshop.png":index==TidalScene.PresetIndex?"tidal-day.png":index==PresetIndex?"scifi-chamber.png":null;
    }
    internal static double ArtworkAspect(int index){
        string name=ArtworkName(index);if(name==null)return 2;
        var image=ScifiAssets.Load(name);return (double)image.PixelWidth/image.PixelHeight;
    }
    // All themes keep the full original image and share actor coordinates. Ratio locking
    // controls bar-height linkage only; even free/manual heights must not distort artwork.
    internal static Rect ArtworkViewport(Rect bounds,int index,bool locked){
        double aspect=ArtworkAspect(index),width=Math.Min(bounds.Width,bounds.Height*aspect),height=width/aspect;
        return new Rect(bounds.X+(bounds.Width-width)/2,bounds.Y+(bounds.Height-height)/2,width,height);
    }
    static ImageBrush EdgeSample(BitmapSource image,Rect sample){
        return new ImageBrush(image){ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Viewbox=sample,Stretch=Stretch.Fill};
    }
    static void ExtendEdges(DrawingContext c,BitmapSource soft,Rect bounds,Rect view){
        c.DrawImage(soft,view);
        if(view.Top>bounds.Top)c.DrawRectangle(EdgeSample(soft,new Rect(0,0,1,.025)),null,new Rect(bounds.Left,bounds.Top,bounds.Width,view.Top-bounds.Top+1));
        if(view.Bottom<bounds.Bottom)c.DrawRectangle(EdgeSample(soft,new Rect(0,.975,1,.025)),null,new Rect(bounds.Left,view.Bottom-1,bounds.Width,bounds.Bottom-view.Bottom+1));
        if(view.Left>bounds.Left)c.DrawRectangle(EdgeSample(soft,new Rect(0,0,.025,1)),null,new Rect(bounds.Left,bounds.Top,view.Left-bounds.Left+1,bounds.Height));
        if(view.Right<bounds.Right)c.DrawRectangle(EdgeSample(soft,new Rect(.975,0,.025,1)),null,new Rect(view.Right-1,bounds.Top,bounds.Right-view.Right+1,bounds.Height));
    }
    static Brush EdgeFade(double extent,bool horizontal){
        double edge=Math.Min(.12,9/extent);var mask=new LinearGradientBrush();
        mask.StartPoint=new Point(0,0);mask.EndPoint=horizontal?new Point(1,0):new Point(0,1);
        mask.GradientStops.Add(new GradientStop(Colors.Transparent,0));mask.GradientStops.Add(new GradientStop(Colors.White,edge));
        mask.GradientStops.Add(new GradientStop(Colors.White,1-edge));mask.GradientStops.Add(new GradientStop(Colors.Transparent,1));
        mask.Freeze();return mask;
    }
    static void DrawArtwork(DrawingContext c,Rect bounds,Rect viewport){
        bool vertical=viewport.Top>bounds.Top+.01 || viewport.Bottom<bounds.Bottom-.01;
        bool horizontal=viewport.Left>bounds.Left+.01 || viewport.Right<bounds.Right-.01;
        if(!vertical && !horizontal){c.DrawDrawing(scene);return;}
        string name=ArtworkName(activePreset);if(name==null){c.DrawDrawing(scene);return;}
        // Extend the corresponding edges, not a differently cropped full image. Fade
        // only the outer nine DIP of the crisp artwork to eliminate hard pad seams.
        ExtendEdges(c,ScifiAssets.Softened(IsTidal?"tidal-night.png":name),bounds,viewport);
        if(IsTidal){c.PushOpacity(TidalScene.Daylight);ExtendEdges(c,ScifiAssets.Softened(name),bounds,viewport);c.Pop();}
        var crisp=new DrawingGroup();using(var d=crisp.Open())d.DrawDrawing(scene);
        if(vertical)crisp.OpacityMask=EdgeFade(viewport.Height,false);
        if(horizontal){var wrapped=new DrawingGroup();wrapped.Children.Add(crisp);wrapped.OpacityMask=EdgeFade(viewport.Width,true);crisp=wrapped;}
        c.DrawDrawing(crisp);
    }
    static Window window;
    static Border root;
    static DrawingGroup scene;
    static DrawingGroup foreground;
    static PictureLayer backdropLayer,animationLayer;
    static DispatcherTimer timer;
    static bool testing,painting;
    static int activePreset=PresetIndex;
    static readonly Stopwatch clock=new Stopwatch();
    static IEnumerable<DependencyObject> Children(DependencyObject parent) {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {
            var child=VisualTreeHelper.GetChild(parent,i);yield return child;
            foreach(var nested in Children(child))yield return nested;
        }
    }
    internal static Dictionary<string,string> Palette() {
        var result=Theme.Defaults();
        string[] keys={"background","track","barBorder","windowBorder","title","label","valueText","timeText","caption","status","icons","ticks","weekMarker","alert","failure","quotaGreen","timeGreen","quotaYellow","timeYellow","quotaOrange","timeOrange","quotaRed","timeRed"};
        string[] values={"#0D1F26","#1E343B","#4F8F9F","#4F8F9F","#C1E2E7","#C1E2E7","#C1E2E7","#C1E2E7","#C1E2E7","#A3DCEB","#A4C7CF","#81B7C1","#D6B481","#FFBB68","#FFB2C5","#285A68","#285A68","#5D4B12","#5D4B12","#683817","#683817","#632843","#632843"};
        // Failure is foreground text/outline, unlike the dark red quota fill in the design palette.
        for(int i=0;i<keys.Length;i++)result[keys[i]]=values[i];return result;
    }
    internal static void ApplyPreset(Window owner,int index,bool offline) {
        if(activePreset!=index){activePreset=index;Enabled=false;clock.Reset();}
        Apply(owner,Theme.IsAnimatedPreset(index),offline);
    }
    internal static void Apply(Window owner,bool enabled,bool offline) {
        if(window!=owner) {
            if(timer!=null)timer.Stop();window=owner;root=(Border)owner.FindName("Root");
            // These siblings share one coordinate system, but independent invalidation.
            var data=root.Child;root.Child=null;
            var host=new Grid();backdropLayer=new PictureLayer {Owner=root,IsHitTestVisible=false};animationLayer=new PictureLayer {Owner=root,IsHitTestVisible=false};
            owner.RegisterName("ThemeBackgroundLayer",backdropLayer);owner.RegisterName("ThemeAnimationLayer",animationLayer);
            Panel.SetZIndex(backdropLayer,0);Panel.SetZIndex(animationLayer,1);Panel.SetZIndex(data,2);
            host.Children.Add(backdropLayer);host.Children.Add(animationLayer);host.Children.Add(data);root.Child=host;
            root.SizeChanged+=(s,e)=>{if(Enabled)RenderAt(testing?Seconds:clock.Elapsed.TotalSeconds);};
            owner.IsVisibleChanged+=(s,e)=>UpdateTimer();owner.StateChanged+=(s,e)=>UpdateTimer();
            owner.Closed+=(s,e)=>{if(timer!=null)timer.Stop();clock.Stop();Enabled=false;scene=null;};
        }
        testing=offline;bool changed=Enabled!=enabled;Enabled=enabled;
        if(enabled) {if(changed)clock.Restart();RenderAt(testing?Seconds:clock.Elapsed.TotalSeconds);}
        else {clock.Reset();scene=null;foreground=null;backdropLayer.SetFrame(null);animationLayer.SetFrame(null);root.Background=Theme.Brush("background");InvalidateBars();}
        if(changed)InvalidateBars();UpdateTimer();
    }
    static void UpdateTimer() {
        if(timer==null) {
            timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(1000.0/16)};
            timer.Tick+=(s,e)=>RenderAt(clock.Elapsed.TotalSeconds);
        }
        if(Enabled && !testing && window.IsVisible && window.WindowState!=WindowState.Minimized)timer.Start();
        else timer.Stop();
    }
    static void InvalidateBars() {
        if(root==null)return;
        foreach(var child in Children(root)){var visual=child as SegmentedVisual;if(visual!=null && visual.IsVisible)visual.InvalidateVisual();}
    }
    internal static void LayoutCommitted(){
        // A locked ratio can keep the outer size unchanged while the text/toolbar move.
        // Refresh the shared picture and obstacle coordinates only after the final layout.
        if(Enabled)RenderAt(testing?Seconds:clock.Elapsed.TotalSeconds);
    }
    internal static void RenderAt(double seconds) {
        if(!Enabled || painting || root.ActualWidth<=0 || root.ActualHeight<=0)return;
        painting=true;
        try {
            Seconds=seconds;var bounds=new Rect(0,0,root.ActualWidth,root.ActualHeight);
            Rect viewport=ArtworkViewport(bounds,activePreset,LockArtworkAspect);
            double width=viewport.Width,height=viewport.Height,offsetX=viewport.X,offsetY=viewport.Y;
            var next=new DrawingGroup();using(var c=next.Open()) {
                c.PushClip(new RectangleGeometry(bounds));c.PushTransform(new TranslateTransform(offsetX,offsetY));
                if(IsTidal)TidalScene.Draw(c,width,height,seconds);
                else if(IsInk)InkScene.DrawBackdrop(c,width,height);
                else if(IsSteam)SteamScene.DrawBackdrop(c,width,height,seconds);
                else if(IsStarry)StarryScene.DrawBackdrop(c,width,height,seconds);
                else c.DrawImage(ScifiAssets.Load("scifi-chamber.png"),new Rect(0,0,width,height));
                c.Pop();c.Pop();
            }
            if(next.CanFreeze)next.Freeze();scene=next;
            foreground=null;
            {
                var actors=new DrawingGroup();using(var c=actors.Open()){
                    c.PushClip(new RectangleGeometry(viewport));c.PushTransform(new TranslateTransform(offsetX,offsetY));
                    if(IsTidal)TidalScene.Draw(c,width,height,seconds,true);
                    else if(IsInk)InkScene.DrawActors(c,width,height,seconds);
                    else if(IsStarry)StarryScene.DrawActors(c,width,height,seconds);
                    else if(IsSteam)SteamScene.DrawActors(c,width,height,seconds);
                    else ScifiSequence.Draw(c,width,height,seconds);
                    c.Pop();c.Pop();
                }
                if(actors.CanFreeze)actors.Freeze();foreground=actors;
            }
            var drawing=new DrawingGroup();using(var c=drawing.Open()) {
                c.DrawRectangle(Theme.Brush("background"),null,bounds);
                // Painted themes use one continuous subdued picture; no text-shaped background masks.
                if(Painted)c.PushOpacity(IsStarry ? .74 : IsSteam ? .52 : IsInk ? .52 : .48-.10*TidalScene.Daylight);
                DrawArtwork(c,bounds,viewport);
                if(Painted)c.Pop();
            }
            if(drawing.CanFreeze)drawing.Freeze();
            root.Background=Theme.Brush("background");backdropLayer.SetFrame(drawing);
            var actorsDrawing=new DrawingGroup();using(var c=actorsDrawing.Open()){c.PushOpacity(IsInk?1:.90);c.DrawDrawing(foreground);c.Pop();}
            if(actorsDrawing.CanFreeze)actorsDrawing.Freeze();animationLayer.SetFrame(actorsDrawing);
        } finally {painting=false;}
    }
}
