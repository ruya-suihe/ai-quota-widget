using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// The painted coast is fixed. Moving water has advancing crests, breaking foam and backwash.
// Sun and moon pass behind the horizon; animals keep continuous poses and water occlusion.
internal static class TidalScene {
    internal const int PresetIndex=8;
    // Water/dolphin periods divide the daylight cycle; randomized gull flights finish before its boundary.
    internal const double CycleSeconds=120;
    internal static double Daylight,DolphinActivity,GullActivity;
    static double unit,skyLight,seaLight,elevation;
    static double Clamp(double v){return Math.Max(0,Math.Min(1,v));}
    static double Ease(double v){v=Clamp(v);return v*v*(3-2*v);}
    static double Rand(int i){double v=Math.Sin(i*127.1+311.7)*43758.5453;return v-Math.Floor(v);}
    static Color C(string s){return (Color)ColorConverter.ConvertFromString(s);}
    static Brush B(string s,double opacity){return new SolidColorBrush(C(s)){Opacity=Clamp(opacity)};}
    static Pen P(string s,double opacity,double width){return new Pen(B(s,opacity),width*unit){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};}
    static readonly Dictionary<int,BitmapSource> sprites=new Dictionary<int,BitmapSource>();
    static readonly Int32Rect[] cuts={new Int32Rect(596,119,556,261),new Int32Rect(568,590,594,230)};
    static BitmapSource Sprite(int index){
        BitmapSource image;if(sprites.TryGetValue(index,out image))return image;
        image=new CroppedBitmap(ScifiAssets.Load("tidal-animals.png"),cuts[index]);image.Freeze();sprites[index]=image;return image;
    }
    static readonly Dictionary<bool,BitmapSource> celestialSprites=new Dictionary<bool,BitmapSource>();
    static BitmapSource CelestialSprite(bool sun){
        BitmapSource image;if(celestialSprites.TryGetValue(sun,out image))return image;
        var crop=sun?new Int32Rect(64,55,773,773):new Int32Rect(938,57,778,778);
        image=new CroppedBitmap(ScifiAssets.Load("tidal-celestials-v2.png"),crop);image.Freeze();celestialSprites[sun]=image;return image;
    }
    static Geometry Polygon(params Point[] points){
        var g=new StreamGeometry();using(var d=g.Open()){
            d.BeginFigure(points[0],true,true);for(int i=1;i<points.Length;i++)d.LineTo(points[i],true,false);
        }g.Freeze();return g;
    }
    static Rect PlateBounds(double w,double h){
        var image=ScifiAssets.Load("tidal-day.png");double scale=Math.Max(w/image.PixelWidth,h/image.PixelHeight);
        double width=image.PixelWidth*scale,height=image.PixelHeight*scale;
        double top=(h-height)*.45;
        // In a wide minimal window, retain a strip of sky instead of cropping the horizon away.
        if(top+height*.37<h*.36)top=h*.36-height*.37;
        return new Rect((w-width)/2,top,width,height);
    }
    static void Plates(DrawingContext c,Rect plate,double horizon,double w,double h){
        c.DrawImage(ScifiAssets.Load("tidal-night.png"),plate);
        // Dawn reaches the sky first; water and land respond more slowly.
        double stop=Clamp(horizon/h);
        var light=new LinearGradientBrush();light.StartPoint=new Point(0,0);light.EndPoint=new Point(0,1);
        light.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(skyLight*255),255,255,255),0));
        light.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(skyLight*255),255,255,255),Clamp(stop-.04)));
        light.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(seaLight*255),255,255,255),Clamp(stop+.12)));
        light.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(Daylight*255),255,255,255),1));
        var group=new DrawingGroup();using(var d=group.Open()){d.DrawImage(ScifiAssets.Load("tidal-day.png"),plate);}
        var brush=new DrawingBrush(group){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,w,h),ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,w,h),Stretch=Stretch.Fill};
        c.PushOpacityMask(light);c.DrawRectangle(brush,null,new Rect(0,0,w,h));c.Pop();
        double twilight=Math.Exp(-elevation*elevation/.045);
        if(horizon>0){
            var dusk=new LinearGradientBrush();dusk.StartPoint=new Point(0,0);dusk.EndPoint=new Point(0,1);
            dusk.GradientStops.Add(new GradientStop(Colors.Transparent,0));
            dusk.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(twilight*55),245,142,91),1));
            c.DrawRectangle(dusk,null,new Rect(0,0,w,Math.Min(h,horizon)));
        }
    }
    internal static Dictionary<string,string> Palette(){
        var p=Theme.Defaults();string[] keys={"background","track","barBorder","windowBorder","title","label","valueText","timeText","caption","status","icons","ticks","weekMarker","alert","failure","quotaGreen","timeGreen","quotaYellow","timeYellow","quotaOrange","timeOrange","quotaRed","timeRed"};
        string[] values={"#102D3B","#193D4A","#638E98","#638E98","#EAF5F5","#EAF5F5","#EAF5F5","#EAF5F5","#EAF5F5","#BCD5D9","#CEE4E6","#7DA6AF","#F2D4A0","#F1C58C","#FFB8B9","#285A68","#285A68","#665422","#665422","#713F25","#713F25","#723846","#723846"};
        for(int i=0;i<keys.Length;i++)p[keys[i]]=values[i];return p;
    }
    static void Glow(DrawingContext c,Point center,double rx,double ry,string color,double opacity){
        Color colorValue=C(color);var brush=new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(Clamp(opacity)*255),colorValue.R,colorValue.G,colorValue.B),0));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent,1));c.DrawEllipse(brush,null,center,rx,ry);
    }
    static double ShoreY(double x){
        double[] xs={0,.18,.32,.48,.65,.82,1},ys={.46,.50,.63,.77,.77,.69,.63};
        x=Clamp(x);for(int i=1;i<xs.Length;i++)if(x<=xs[i])return ys[i-1]+(ys[i]-ys[i-1])*Ease((x-xs[i-1])/(xs[i]-xs[i-1]));
        return ys[ys.Length-1];
    }
    static Point WaterPoint(Rect plate,double x,double depth,double seconds){
        double y=.37+(ShoreY(x)-.37)*depth;
        y+=Math.Sin(x*24+seconds*Math.PI*2/15)*.003*depth+Math.Sin(x*49-seconds*Math.PI*2/20)*.0015*depth;
        return new Point(plate.Left+plate.Width*x,plate.Top+plate.Height*y);
    }
    static Geometry SeaArea(Rect plate){
        var g=new StreamGeometry();using(var d=g.Open()){
            d.BeginFigure(new Point(plate.Left,plate.Top+plate.Height*.37),true,true);
            d.LineTo(new Point(plate.Right,plate.Top+plate.Height*.37),true,false);
            for(int i=72;i>=0;i--)d.LineTo(WaterPoint(plate,i/72.0,1,0),true,false);
        }return g;
    }
    static StreamGeometry Crest(Rect plate,double depth,double seconds,double thickness,bool filled){
        var g=new StreamGeometry();using(var d=g.Open()){
            for(int i=0;i<=72;i++){
                var point=WaterPoint(plate,i/72.0,depth,seconds);
                if(i==0)d.BeginFigure(point,filled,filled);else d.LineTo(point,true,false);
            }
            if(filled)for(int i=72;i>=0;i--){var point=WaterPoint(plate,i/72.0,depth,seconds);point.Y+=thickness;d.LineTo(point,true,false);}
        }return g;
    }
    static void MovingWater(DrawingContext c,double w,double h,Rect plate,double seconds){
        double horizon=plate.Top+plate.Height*.37;
        // Displace the painted water itself, not just highlights drawn over a still photograph.
        // Adjacent strips share sampling boundaries, so the water bends without opening seams.
        double step=Math.Max(2,unit*1.25);
        for(double top=Math.Max(0,horizon);top<h;top+=step){
            double bottom=Math.Min(h,top+step),depth=Clamp((top-horizon)/(plate.Height*.37));
            double a0=(.18+depth*1.25)*unit,a1=(.18+Clamp((bottom-horizon)/(plate.Height*.37))*1.25)*unit;
            double sourceTop=top+Math.Sin((top-horizon)/(unit*7)-seconds*Math.PI*2/8)*a0;
            double sourceBottom=bottom+Math.Sin((bottom-horizon)/(unit*7)-seconds*Math.PI*2/8)*a1;
            var sample=new Rect((0-plate.Left)/plate.Width,(sourceTop-plate.Top)/plate.Height,w/plate.Width,(sourceBottom-sourceTop)/plate.Height);
            var target=new Rect(0,top,w,bottom-top);
            c.PushOpacity(.78*Ease(depth/.12));
            var night=new ImageBrush(ScifiAssets.Load("tidal-night.png")){ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Viewbox=sample,Stretch=Stretch.Fill};
            c.DrawRectangle(night,null,target);
            c.PushOpacity(seaLight);
            var day=new ImageBrush(ScifiAssets.Load("tidal-day.png")){ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Viewbox=sample,Stretch=Stretch.Fill};
            c.DrawRectangle(day,null,target);c.Pop();c.Pop();
        }
    }
    static void Surf(DrawingContext c,double w,double h,Rect plate,double seconds){
        c.PushClip(SeaArea(plate));
        MovingWater(c,w,h,plate,seconds);
        for(int wave=0;wave<5;wave++){
            double progress=(seconds/12.0+wave/5.0)%1,depth=Math.Pow(progress,1.5);
            double envelope=Math.Pow(Math.Sin(progress*Math.PI),.7),breaking=Ease((progress-.50)/.30);
            double thickness=(.18+depth*1.5)*unit;
            // A translucent raised face and a darker trough move toward the beach together.
            c.DrawGeometry(B("#346D79",envelope*.12),null,Crest(plate,depth,seconds,thickness*3,true));
            c.PushTransform(new TranslateTransform(0,thickness*1.8));
            c.DrawGeometry(null,P("#183E51",envelope*.17,.18+depth*.5),Crest(plate,depth,seconds,0,false));c.Pop();
            c.DrawGeometry(null,P("#D5EDF0",envelope*(.12+breaking*.26),.12+depth*.34),Crest(plate,depth,seconds,0,false));
            // Break into separate foam curls and spray instead of one solid white stripe.
            for(int i=0;i<74;i++){
                double x=(i+.25*Rand(i+wave*71))/73,local=breaking*(.35+.65*Rand(i+80));
                if(local<.10)continue;
                var point=WaterPoint(plate,x,depth,seconds);
                double curl=Math.Sin(seconds*Math.PI*2/8+i*1.7),radius=(.18+depth*.5)*unit;
                c.DrawEllipse(B("#E4F1EE",envelope*local*.50),null,new Point(point.X,point.Y+curl*radius),radius*(1.3+Rand(i)),radius*.42);
                if(progress>.74){
                    double trail=(progress-.74)/.26;
                    c.DrawEllipse(B("#D0E5E3",envelope*local*.19),null,new Point(point.X-unit*Rand(i+21),point.Y+trail*4*unit),radius*1.5,radius*.48);
                }
            }
        }
        c.Pop();
        // Swash advances, pauses and drains back along the shore; its foam fragments linger.
        double cycle=(seconds/10)%1,advance=cycle<.57?Ease(cycle/.57):1-Ease((cycle-.57)/.43);
        double depthShore=.90+advance*.17;
        var swash=new DrawingGroup();using(var d=swash.Open()){
            d.DrawGeometry(B("#D2E5DF",.07+.07*advance),null,Crest(plate,depthShore,seconds,Math.Max(.8,unit*2.4),true));
            for(int i=0;i<95;i++){
                double x=.30+.70*i/94,alpha=(.12+.25*advance)*(.25+.75*Rand(i+14));
                var point=WaterPoint(plate,x,depthShore,seconds);
                d.DrawEllipse(B("#E0ECE5",alpha),null,point,unit*(.3+Rand(i)*.9),unit*(.12+Rand(i+3)*.20));
            }
        }
        // Fade the left edge before the foreground rocks, rather than animating the rocks.
        var edge=new LinearGradientBrush(Colors.Transparent,Colors.White,0);edge.GradientStops.Clear();
        edge.GradientStops.Add(new GradientStop(Colors.Transparent,0));edge.GradientStops.Add(new GradientStop(Colors.Transparent,.31));
        edge.GradientStops.Add(new GradientStop(Colors.White,.47));edge.GradientStops.Add(new GradientStop(Colors.White,1));
        c.PushOpacityMask(edge);c.DrawDrawing(swash);c.Pop();
    }
    static void Celestial(DrawingContext c,double w,double h,Rect plate,double horizon,double phase,bool sun,bool reflection){
        double orbit=sun?phase*2:(phase-.5)*2;
        if(orbit<0 || orbit>1)return;
        double radius=Math.Min((sun?6.0:6.7)*unit,h*.10),altitude=Math.Sin(orbit*Math.PI);
        double x=plate.Left+plate.Width*(.14+.72*orbit);
        double y=horizon+radius*1.25-(Math.Min(h*.24,plate.Height*.24)+radius*1.25)*altitude;
        double visible=Clamp((horizon-y+radius)/(radius*2));
        if(reflection){
            c.PushClip(SeaArea(plate));
            for(int i=0;i<34;i++){
                double depth=i/33.0,py=horizon+(h-horizon)*depth*.80;
                double ripple=Math.Sin(phase*90+i*2.3)*unit*(.6+depth*2),half=unit*(.3+depth*7);
                c.DrawLine(P(sun?"#FFE7B1":"#C0DCEA",visible*(1-depth)*(.06+.10*Rand(i)),.18+depth*.4),new Point(x+ripple-half,py),new Point(x+ripple+half,py));
            }c.Pop();return;
        }
        if(horizon<=0)return;
        double avoid=InkScene.ActorOpacity(new Rect(x-radius,y-radius,radius*2,radius*2));
        c.PushOpacity(avoid);c.PushClip(new RectangleGeometry(new Rect(0,0,w,Math.Min(h,horizon))));
        // The sea horizon physically masks the disc. There is no appearance/disappearance fade.
        Glow(c,new Point(x,y),radius*(sun?4.6:3.2),radius*(sun?3.0:2.8),sun?"#FFD39D":"#B9D4EC",(sun?.24:.14)*visible);
        var body=new Rect(x-radius,y-radius,radius*2,radius*2);
        var limb=new RadialGradientBrush();limb.RadiusX=.5;limb.RadiusY=.5;
        limb.GradientStops.Add(new GradientStop(Colors.White,0));
        limb.GradientStops.Add(new GradientStop(Colors.White,sun?.80:.95));
        limb.GradientStops.Add(new GradientStop(Colors.Transparent,1));
        c.PushOpacityMask(limb);
        c.DrawImage(CelestialSprite(sun),body);
        if(sun)c.DrawEllipse(B("#FFF1C9",.28),null,new Point(x,y),radius*.98,radius*.98);
        c.Pop();
        c.PushClip(new EllipseGeometry(new Point(x,y),radius,radius));
        if(sun){
            // Surface detail survives under a bright optical core; the near-horizon disc warms.
            Glow(c,new Point(x-radius*.10,y-radius*.10),radius*.96,radius*.96,"#FFF8DC",.60);
            var atmosphere=new LinearGradientBrush();atmosphere.StartPoint=new Point(0,0);atmosphere.EndPoint=new Point(0,1);
            atmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0,255,169,106),0));
            atmosphere.GradientStops.Add(new GradientStop(Color.FromArgb((byte)((1-Ease(altitude/.35))*105),239,119,63),1));
            c.DrawRectangle(atmosphere,null,body);
        }else{
            var shade=new LinearGradientBrush();shade.StartPoint=new Point(0,0);shade.EndPoint=new Point(1,1);
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0,23,45,70),.15));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(45,23,45,70),1));c.DrawRectangle(shade,null,body);
        }
        c.Pop();c.Pop();c.Pop();
    }
    static void Spray(DrawingContext c,Point point,double age,double power,int seed){
        if(age<0 || age>2.2)return;double life=Math.Pow(1-age/2.2,1.4);
        c.DrawEllipse(null,P("#C3DFE2",life*.40,.42),point,(1.5+age*8)*unit*power,(.35+age*1.3)*unit*power);
        for(int i=0;i<24;i++){
            double vx=(Rand(i+seed)-.5)*8*power,vy=-(2+Rand(i+seed+20)*7)*power;
            var q=new Point(point.X+vx*age*unit,point.Y+(vy*age+5*age*age)*unit);
            if(q.Y>point.Y+.5*unit)continue;
            c.DrawEllipse(B("#D2E9E9",life*(.25+.3*Rand(i))),null,q,.17*unit,.28*unit);
        }
    }
    static Geometry AboveWater(double w,double waterY,double seconds){
        var g=new StreamGeometry();using(var d=g.Open()){
            d.BeginFigure(new Point(-w,-w),true,true);d.LineTo(new Point(w*2,-w),true,false);
            for(int i=36;i>=0;i--)d.LineTo(new Point(w*2*i/36.0,waterY+Math.Sin(i*.8+seconds*Math.PI*2/6)*unit*.18),true,false);
            d.LineTo(new Point(-w,waterY),true,false);
        }return g;
    }
    static void Dolphins(DrawingContext c,double w,double h,double horizon,double seconds){
        double local=seconds%30-8;DolphinActivity=0;
        for(int i=0;i<2;i++){
            double age=local-i*1.25;if(age<0 || age>12.8)continue;
            double p=Clamp(age/10),length=23*unit*(i==0?1:.76),waterY=horizon+(h-horizon)*.28+i*unit*1.2;
            double start=w*.35-i*5*unit,travel=w*.43,x=start+travel*p;
            double depth=length*.65,jump=h*.15+depth,y=waterY+depth-jump*Math.Sin(p*Math.PI);
            double angle=Math.Atan2(-jump*Math.PI*Math.Cos(p*Math.PI),travel)*180/Math.PI;
            if(age<=10){
                double alpha=InkScene.ActorOpacity(new Rect(x-length*.65,y-length*.65,length*1.3,length*1.3));
                DolphinActivity=Math.Max(DolphinActivity,alpha*Clamp((waterY-y+length*.4)/length));
                c.PushClip(AboveWater(w,waterY,seconds));c.PushOpacity(alpha*(.70+.30*Daylight));
                c.PushTransform(new TranslateTransform(x,y));c.PushTransform(new RotateTransform(angle));
                var image=Sprite(0);double height=length*image.PixelHeight/image.PixelWidth;
                c.DrawImage(image,new Rect(-length*.5,-height*.5,length,height));c.Pop();c.Pop();c.Pop();c.Pop();
            }
            // Entry and exit splashes meet the body at the waterline, followed by expanding rings.
            Spray(c,new Point(start+travel*.08,waterY),age-.8,.62,20+i);
            Spray(c,new Point(start+travel*.92,waterY),age-9.2,.86,40+i);
        }
    }
    static readonly Geometry GullBody=Polygon(new Point(160,163),new Point(230,143),new Point(310,128),new Point(380,115),new Point(450,112),new Point(473,169),new Point(405,187),new Point(288,213),new Point(180,229),new Point(150,202));
    static readonly Geometry GullNearWing=Polygon(new Point(0,0),new Point(185,42),new Point(285,97),new Point(337,143),new Point(294,167),new Point(148,105),new Point(50,56));
    static readonly Geometry GullFarWing=Polygon(new Point(290,96),new Point(400,66),new Point(594,44),new Point(548,113),new Point(418,156),new Point(310,157));
    static void GullPart(DrawingContext c,Geometry clip){c.PushClip(clip);c.DrawImage(Sprite(1),new Rect(0,0,594,230));c.Pop();}
    static void GullRig(DrawingContext c,double length,double beat,double glide,double fold){
        double near=(1-glide)*Math.Sin(beat)+glide*.74,far=(1-glide)*Math.Sin(beat+.40)+glide*.72;
        double scale=length/594;c.PushTransform(new ScaleTransform(scale,scale));c.PushTransform(new TranslateTransform(-405,-144));
        // One illustrated body with independently hinged wings, rather than ghosted whole poses.
        c.PushTransform(new ScaleTransform(1,.18+.98*far*fold,332,142));
        c.PushTransform(new RotateTransform(far*7,332,142));GullPart(c,GullFarWing);c.Pop();c.Pop();
        c.PushTransform(new ScaleTransform(1,.16+1.05*near*fold,300,145));
        c.PushTransform(new RotateTransform(-near*6,300,145));GullPart(c,GullNearWing);c.Pop();c.Pop();
        GullPart(c,GullBody);c.Pop();c.Pop();
    }
    sealed class GullFlight {
        internal double Start,Duration,Sky,Size,Beat,BeatPhase,Curve,DiveStart,DiveSpan;
        internal bool Hunt,Caught,Reverse,Arc;
    }
    // One session seed, independent randomized flights, and a bounded one-cycle cache.
    // The preview can fix the seed; production varies on startup and on every daylight cycle.
    internal static int GullSeed=Environment.TickCount;
    static long gullCycle=long.MinValue;
    static int cachedGullSeed;
    static readonly List<GullFlight> gullFlights=new List<GullFlight>();
    static void PlanGulls(long cycle){
        if(cycle==gullCycle && cachedGullSeed==GullSeed)return;
        gullCycle=cycle;cachedGullSeed=GullSeed;gullFlights.Clear();
        var random=new Random(unchecked(GullSeed^((int)cycle*1664525+1013904223)));
        double start=7+random.NextDouble()*7;
        while(start<99){
            bool hunt=random.NextDouble()<.36;
            gullFlights.Add(new GullFlight {
                Start=start,Duration=13+random.NextDouble()*6,Sky=.13+random.NextDouble()*.16,
                Size=9.5+random.NextDouble()*4,Beat=1.8+random.NextDouble()*1.2,BeatPhase=random.NextDouble()*Math.PI*2,
                Curve=(random.NextDouble()-.5)*2,DiveStart=.24+random.NextDouble()*.14,DiveSpan=.32+random.NextDouble()*.13,
                Hunt=hunt,Caught=hunt && random.NextDouble()<.65,Reverse=random.NextDouble()<.5,Arc=random.NextDouble()<.35
            });
            start+=10+random.NextDouble()*12;
        }
    }
    static Point GullPosition(GullFlight flight,double p,double w,double h,double horizon,out double dive){
        p=Clamp(p);double window=Math.Pow(Math.Sin(p*Math.PI),2);
        double horizontal=-.18+1.36*p;
        if(flight.Arc)horizontal+=.045*window*Math.Sin(p*Math.PI*2);
        double x=w*(flight.Reverse?1-horizontal:horizontal);
        double sky=h*flight.Sky;
        double y=sky+h*.035*flight.Curve*window*Math.Sin(p*Math.PI*(flight.Arc?4:2));
        double q=(p-flight.DiveStart)/flight.DiveSpan;
        dive=flight.Hunt && q>0 && q<1?Math.Pow(Math.Sin(q*Math.PI),2):0;
        double water=horizon+(h-horizon)*(.17+.06*(flight.Curve+1)/2);
        y+=(water-sky)*dive;return new Point(x,y);
    }
    static void Gulls(DrawingContext c,double w,double h,double horizon,double seconds){
        long cycle=(long)Math.Floor(seconds/CycleSeconds);PlanGulls(cycle);
        double local=seconds-cycle*CycleSeconds;GullActivity=0;
        int id=0;
        foreach(var flight in gullFlights){
            int splashId=id++;double age=local-flight.Start;
            if(age<0 || age>flight.Duration+2.2)continue;
            double p=Clamp(age/flight.Duration),dive;
            Point point=GullPosition(flight,p,w,h,horizon,out dive);
            if(age<flight.Duration){
                double ignored;Point before=GullPosition(flight,p-.001,w,h,horizon,out ignored),after=GullPosition(flight,p+.001,w,h,horizon,out ignored);
                double dx=after.X-before.X,dy=after.Y-before.Y;
                double angle=Math.Atan2(dy,Math.Abs(dx))*180/Math.PI*(dx<0?-1:1);
                double length=flight.Size*unit,alpha=InkScene.ActorOpacity(new Rect(point.X-length*.85,point.Y-length*.85,length*1.7,length*1.7));
                GullActivity=Math.Max(GullActivity,alpha);
                // A different glide rhythm per flight; diving folds the wings toward the body.
                double glide=Ease((Math.Sin(age*Math.PI*2/7+flight.BeatPhase)-.25)/.65)*(1-dive);
                c.PushOpacity(alpha*(.72+.28*Daylight));c.PushTransform(new TranslateTransform(point.X,point.Y));
                c.PushTransform(new RotateTransform(angle));if(dx<0)c.PushTransform(new ScaleTransform(-1,1));
                GullRig(c,length,age*Math.PI*2/flight.Beat+flight.BeatPhase,glide,1-dive*.62);
                double catchPoint=flight.DiveStart+flight.DiveSpan*.5;
                if(flight.Caught && p>catchPoint)c.DrawLine(P("#8BBBC2",.85,.45),new Point(0,unit),new Point(unit,unit*2.7));
                if(dx<0)c.Pop();c.Pop();c.Pop();c.Pop();
            }
            if(flight.Hunt){
                double splashP=flight.DiveStart+flight.DiveSpan*.5,ignored;
                Point splash=GullPosition(flight,splashP,w,h,horizon,out ignored);
                Spray(c,splash,age-flight.Duration*splashP,.30,400+splashId);
            }
        }
    }
    internal static void Draw(DrawingContext c,double w,double h,double seconds,bool foreground=false){
        double phase=(seconds%CycleSeconds)/CycleSeconds;elevation=Math.Sin(phase*Math.PI*2);
        unit=Math.Min(w/180,Math.Max(.45,h/110));
        skyLight=Ease((elevation+.16)/.91);seaLight=Ease((elevation+.04)/1.04);Daylight=Ease((elevation+.09)/1.04);
        Rect plate=PlateBounds(w,h);double horizon=plate.Top+plate.Height*.37;
        c.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));
        if(foreground){
            Celestial(c,w,h,plate,horizon,phase,true,false);Celestial(c,w,h,plate,horizon,phase,false,false);
            Dolphins(c,w,h,horizon,seconds);Gulls(c,w,h,horizon,seconds);
        }else{
            Plates(c,plate,horizon,w,h);Surf(c,w,h,plate,seconds);
            Celestial(c,w,h,plate,horizon,phase,true,true);Celestial(c,w,h,plate,horizon,phase,false,true);
            for(int i=0;i<22;i++){
                double opacity=(1-skyLight)*(.12+.12*Math.Pow(Math.Sin(seconds*Math.PI*2/10+i),2));
                c.DrawEllipse(B("#DDEBF2",opacity),null,new Point(w*Rand(i+50),Math.Max(0,horizon*.88)*Rand(i+110)),.30*unit,.30*unit);
            }
        }c.Pop();
    }
}
