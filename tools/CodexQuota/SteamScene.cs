using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Independent artwork and a simple left-to-right carrying pass, drawn in root coordinates below the live text.
internal static class SteamScene {
    internal const int PresetIndex=10;
    internal const double CycleSeconds=24;
    static readonly Int32Rect[] cuts={new Int32Rect(40,13,446,436),new Int32Rect(588,14,212,420),new Int32Rect(968,14,229,434),new Int32Rect(1275,80,462,350),new Int32Rect(129,452,249,420),new Int32Rect(563,452,213,415),new Int32Rect(918,597,376,227),new Int32Rect(1352,485,357,361)};
    static readonly Dictionary<int,BitmapSource> parts=new Dictionary<int,BitmapSource>();
    internal static double RobotX,CrateX,CrateY;
    static double Clamp(double x){return Math.Max(0,Math.Min(1,x));}
    static double E(double t,double a,double b){double x=Clamp((t-a)/(b-a));return x*x*(3-2*x);}
    static double Lerp(double a,double b,double t){return a+(b-a)*t;}
    static double Rand(int i){double x=Math.Sin(i*127.1+91.7)*41358.54;return x-Math.Floor(x);}
    static Brush B(string hex,double opacity){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)){Opacity=Clamp(opacity)};}
    static Pen P(string hex,double opacity,double width){return new Pen(B(hex,opacity),width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};}
    static BitmapSource Part(int index){
        BitmapSource image;if(parts.TryGetValue(index,out image))return image;
        image=new CroppedBitmap(ScifiAssets.Load("steam-parts.png"),cuts[index]);image.Freeze();parts[index]=image;return image;
    }
    internal static Dictionary<string,string> Palette(){
        var p=Theme.Defaults();
        string[] keys={"background","track","barBorder","windowBorder","title","label","valueText","timeText","caption","status","icons","ticks","weekMarker","alert","failure","quotaGreen","timeGreen","quotaYellow","timeYellow","quotaOrange","timeOrange","quotaRed","timeRed"};
        string[] values={"#25231E","#3E3A30","#9D8662","#9D8662","#F3E8CE","#F3E8CE","#F3E8CE","#F3E8CE","#F3E8CE","#D2B78F","#D2B78F","#BCA177","#EFD28D","#F1C078","#F3B6A7","#3F5C4B","#3F5C4B","#66552D","#66552D","#70482C","#70482C","#633F3F","#633F3F"};
        for(int i=0;i<keys.Length;i++)p[keys[i]]=values[i];return p;
    }
    static void Gear(DrawingContext c,double x,double y,double radius,double angle){
        c.PushTransform(new RotateTransform(angle,x,y));c.DrawImage(Part(7),new Rect(x-radius,y-radius,radius*2,radius*2));c.Pop();
    }
    internal static void DrawBackdrop(DrawingContext c,double w,double h,double seconds){
        seconds%=CycleSeconds;
        var image=ScifiAssets.Load("steam-workshop.png");double scale=Math.Max(w/image.PixelWidth,h/image.PixelHeight);
        double width=image.PixelWidth*scale,height=image.PixelHeight*scale;
        c.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));
        c.DrawImage(image,new Rect((w-width)/2,h*.82-height*.82,width,height));
        double u=Math.Min(w/330,h/240);
        Gear(c,w*.59,h*.15,10*u,seconds*30);Gear(c,w*.59+15*u,h*.15,5*u,-seconds*60);
        // Slow furnace breathing, not a full-scene flashing overlay.
        var glow=new RadialGradientBrush();glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(18+6*Math.Sin(seconds*Math.PI*2/12)),239,140,47),0));glow.GradientStops.Add(new GradientStop(Colors.Transparent,1));
        c.DrawEllipse(glow,null,new Point(w*.18,h*.59),w*.16,h*.25);c.Pop();
    }
    static Point Rotate(Point p,Point pivot,double angle){double a=angle*Math.PI/180,x=p.X-pivot.X,y=p.Y-pivot.Y;return new Point(pivot.X+x*Math.Cos(a)-y*Math.Sin(a),pivot.Y+x*Math.Sin(a)+y*Math.Cos(a));}
    static Point Joint(Point a,Point b,double first,double second,double bend){
        Vector delta=b-a;double d=Math.Max(.001,delta.Length);Vector direction=delta/d;
        double clamped=Math.Min(first+second-.01,Math.Max(Math.Abs(first-second)+.01,d));
        double along=(first*first-second*second+clamped*clamped)/(2*clamped),side=Math.Sqrt(Math.Max(0,first*first-along*along));
        return a+direction*along+new Vector(-direction.Y,direction.X)*side*bend;
    }
    static void Bone(DrawingContext c,int part,Point start,Point end,Point sourceStart,Point sourceEnd,int layer=0){
        Vector src=sourceEnd-sourceStart,dst=end-start;double scale=dst.Length/src.Length;
        double angle=(Math.Atan2(dst.Y,dst.X)-Math.Atan2(src.Y,src.X))*180/Math.PI;
        c.PushTransform(new TranslateTransform(start.X,start.Y));c.PushTransform(new RotateTransform(angle));c.PushTransform(new ScaleTransform(scale,scale));c.PushTransform(new TranslateTransform(-sourceStart.X,-sourceStart.Y));
        var image=Part(part);
        if(layer==1)c.PushClip(new RectangleGeometry(new Rect(0,0,image.PixelWidth,284)));
        else if(layer==2)c.PushClip(new RectangleGeometry(new Rect(0,270,image.PixelWidth,image.PixelHeight-270)));
        c.DrawImage(image,new Rect(0,0,image.PixelWidth,image.PixelHeight));
        if(layer!=0)c.Pop();c.Pop();c.Pop();c.Pop();c.Pop();
    }
    static void Arm(DrawingContext c,Point shoulder,Point hand,bool far,bool handOnly=false){
        Point elbow=Joint(shoulder,hand,37,39,1);
        c.PushOpacity(far?.65:1);
        if(!handOnly)Bone(c,1,shoulder,elbow,new Point(74,66),new Point(143,359));
        Bone(c,2,elbow,hand,new Point(68,56),new Point(144,348),handOnly?2:far?0:1);c.Pop();
    }
    static void Leg(DrawingContext c,double x,double squat,double walk,double offset,double bodyX,double u,bool far){
        double phase=bodyX/(40*u)+offset;phase-=Math.Floor(phase);
        double stride=phase<.65?13-40*phase:Lerp(-13,13,E(phase,.65,1));
        double swing=phase<.65?0:Math.Sin((phase-.65)/.35*Math.PI);
        double footX=Lerp(x,stride,walk),footY=144-swing*9*walk;
        Point hip=new Point(x,82+squat),ankle=new Point(footX,footY),knee=Joint(hip,ankle,38,39,-1);
        c.PushOpacity(far?.62:1);
        Bone(c,4,hip,knee,new Point(77,59),new Point(146,347));
        Bone(c,5,knee,ankle,new Point(78,59),new Point(135,356));
        c.PushTransform(new RotateTransform(-swing*6*walk,ankle.X,ankle.Y));c.DrawImage(Part(6),new Rect(ankle.X-10,ankle.Y-5,32,21));c.Pop();c.Pop();
    }
    static void Box(DrawingContext c,double x,double y,double u,double angle){
        c.PushTransform(new RotateTransform(angle,x,y));
        c.DrawImage(Part(3),new Rect(x-31*u,y-23.5*u,62*u,47*u));c.Pop();
    }
    static Rect BoxBounds(double x,double y,double u,double angle){
        return new RotateTransform(angle,x,y).TransformBounds(new Rect(x-31*u,y-23.5*u,62*u,47*u));
    }
    static void Steam(DrawingContext c,Point spout,double u,double t,double power){
        for(int i=0;i<15;i++){
            double age=(t/3.0+i/15.0)%1;
            double dx=Math.Sin(age*4+i)*4*age*u,dy=-age*28*u;
            c.DrawEllipse(B("#DBCBB0",(1-age)*.12*power),null,new Point(spout.X+dx,spout.Y+dy),(1+age*5)*u,(1+age*3.5)*u);
        }
    }
    internal static void DrawActors(DrawingContext c,double w,double h,double seconds){
        double t=seconds%CycleSeconds,u=Math.Min(w/330,h/240),floor=h*.84;
        // Enter already carrying cargo, cross at a steady pace, then leave completely before looping.
        double progress=Clamp(t/21),x=w*(-.30+1.60*progress),rootY=floor-160*u;
        double walk=E(t,0,.8)*(1-E(t,20.2,21)),lean=-5;
        RobotX=x;CrateX=x+44*u;CrateY=rootY+65*u;
        var bounds=new Rect(x-44*u,rootY,100*u,162*u);
        bounds.Union(BoxBounds(CrateX,CrateY,u,0));
        c.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));
        // The independent data layer occludes actual glyphs; a large actor must not
        // disappear merely because its bounding box intersects a caption or toolbar.
        c.PushOpacity(1);
        c.DrawEllipse(B("#100F0C",.28),null,new Point(x+5*u,floor+u),35*u,3*u);
        c.PushTransform(new TranslateTransform(x,rootY));c.PushTransform(new ScaleTransform(u,u));
        Point hip=new Point(0,82),nearShoulder=Rotate(new Point(-20,35),hip,lean),farShoulder=Rotate(new Point(19,35),hip,lean);
        Point nearHand=new Point(31,65),farHand=new Point(53,65);
        Leg(c,11,0,walk,.5,x,u,true);Arm(c,farShoulder,farHand,true);
        Leg(c,-11,0,walk,0,x,u,false);
        c.PushTransform(new RotateTransform(lean,hip.X,hip.Y));
        c.DrawImage(Part(0),new Rect(-35,8,73,74));
        Point dial=new Point(11,42);double needle=48+Math.Sin(t*Math.PI*2/6)*3;
        c.PushTransform(new RotateTransform(needle,dial.X,dial.Y));
        c.DrawLine(P("#48351E",1,.55),dial,new Point(dial.X+2.6,dial.Y-1.1));c.Pop();c.Pop();
        // The same immutable depth order for the entire pass: forearm, crate, gripping palm.
        Arm(c,nearShoulder,nearHand,false);Box(c,44,65,1,0);Arm(c,nearShoulder,nearHand,false,true);
        c.Pop();c.Pop();
        Point spout=Rotate(new Point(-6,8),hip,lean);
        Steam(c,new Point(x+spout.X*u,rootY+spout.Y*u),u,seconds,1);
        c.Pop();c.Pop();
    }
}
