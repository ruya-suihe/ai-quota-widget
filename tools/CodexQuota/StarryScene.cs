using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

// Keep the approved painting composition fixed: only the ribbon brush detail flows.
// The previous full-sky SpiralFlow warped the entire painting and must not be used here.
internal static class StarryScene {
    internal const int PresetIndex=11;
    internal const double CycleSeconds=52;
    const double Tau=Math.PI*2;
    static int textureWidth,textureHeight;
    static readonly Point[] stars={new Point(.037,.075),new Point(.144,.155),new Point(.193,.017),new Point(.224,.026),new Point(.254,.045),new Point(.401,.096),new Point(.516,.116),new Point(.539,.076),new Point(.695,.136),new Point(.793,.081),new Point(.789,.230),new Point(.160,.467),new Point(.652,.511),new Point(.463,.684),new Point(.934,.694)};
    static readonly double[] radii={.035,.046,.018,.018,.020,.049,.016,.031,.038,.016,.023,.028,.022,.012,.022};
    static double Clamp(double x){return Math.Max(0,Math.Min(1,x));}
    internal static Dictionary<string,string> Palette(){
        var p=Theme.Defaults();
        string[] keys={"background","track","barBorder","windowBorder","title","label","valueText","timeText","caption","status","icons","ticks","weekMarker","alert","failure","quotaGreen","timeGreen","quotaYellow","timeYellow","quotaOrange","timeOrange","quotaRed","timeRed"};
        string[] values={"#101F42","#24365B","#8197B4","#8298BA","#FFF2CD","#FFF2CD","#FFF2CD","#FFF2CD","#FFF2CD","#D2DCEE","#E3CC92","#ACC1D2","#FFDD82","#FFD48B","#FFC1C2","#285648","#285648","#665528","#665528","#71462D","#71462D","#633747","#633747"};
        for(int i=0;i<keys.Length;i++)p[keys[i]]=values[i];return p;
    }
    internal static void DrawBackdrop(DrawingContext c,double w,double h,double seconds){
        if(w<=0 || h<=0)return;
        double resolution=Math.Min(2,720/Math.Max(1,w));
        int width=Math.Max(1,(int)Math.Ceiling(w*resolution)),height=Math.Max(1,(int)Math.Ceiling(h*resolution));
        if(width!=textureWidth || height!=textureHeight){
            StarryRibbonFlow.Initialize(ScifiAssets.Load("starry-night.png"),width,height);
            textureWidth=width;textureHeight=height;
        }
        c.DrawImage(StarryRibbonFlow.Frame(seconds),new Rect(0,0,w,h));
    }
    static void Halo(DrawingContext c,Point center,double rx,double ry,double strength){
        var glow=new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(strength*255),255,216,114),0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(strength*135),255,207,96),.25));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(strength*25),255,216,114),.65));
        glow.GradientStops.Add(new GradientStop(Colors.Transparent,1));
        c.DrawEllipse(glow,null,center,rx,ry);
    }
    internal static void DrawActors(DrawingContext c,double w,double h,double seconds){
        c.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));
        double t=Tau*(seconds%CycleSeconds)/CycleSeconds;
        for(int i=0;i<stars.Length;i++){
            double pulse=.5+.5*Math.Sin(t*(5+i%8)+i*2.399);
            Halo(c,new Point(stars[i].X*w,stars[i].Y*h),w*radii[i],h*radii[i]*1.7,.06+.20*pulse);
        }
        StarryRibbonFlow.DrawSmallHighlights(c,w,h,seconds);
        double coreBreath=.5+.5*Math.Sin(seconds*Math.PI*2/7);
        Halo(c,new Point(w*.610,h*.678),w*.018,h*.027,.05+.09*coreBreath);
        double breath=.5-.5*Math.Cos(Tau*(seconds%CycleSeconds)/10);
        Halo(c,new Point(w*.922,h*.16),w*(.081+.009*breath),h*(.15+.018*breath),.08+.46*breath);
        c.Pop();
    }
}
