using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Continuous ship projection, cached for the current theme frame and window dimensions.
// Every view of a ship uses the same mesh, including the near-camera passages.
public static partial class ScifiGeometry {
    struct V {
        public double X,Y,Z;
        public V(double x,double y,double z){X=x;Y=y;Z=z;}
        public static V operator +(V a,V b){return new V(a.X+b.X,a.Y+b.Y,a.Z+b.Z);}
        public static V operator -(V a,V b){return new V(a.X-b.X,a.Y-b.Y,a.Z-b.Z);}
        public static V operator *(V a,double b){return new V(a.X*b,a.Y*b,a.Z*b);}
        public double Length {get{return Math.Sqrt(X*X+Y*Y+Z*Z);}}
        public V Unit {get{return this*(1/Math.Max(.0001,Length));}}
    }
    class Face {public V[] Points;public Color Color;public bool Emissive,Cull,ReverseCull,ChargeSurface;public int Material;}
    class Projected {public Point[] Points;public Rect MaterialBounds;public double Depth;public Color Color;public bool Emissive;public double Opacity,ChargeLight;public int Material;}
    static List<Face> pursuer,prey;
    static Dictionary<int,Brush> brushes=new Dictionary<int,Brush>();
    static double cachedTime=-1,cachedW,cachedH;
    static BitmapSource cached;
    sealed class EngineGlow {internal Point Center;internal double Radius;}
    static readonly List<EngineGlow> engineGlows=new List<EngineGlow>();
    public static Point Target,MuzzleA,MuzzleB,Engine;
    public static double PassScale,PreyDepth,PursuerDepth,Pullback,CameraDistance,Charge;
    public static Point BreechA,BreechB;

    static double Clamp(double x){return Math.Max(0,Math.Min(1,x));}
    static double Ease(double x){x=Clamp(x);return x*x*(3-2*x);}
    static V Cross(V a,V b){return new V(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);}
    static double Dot(V a,V b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
    static Color C(string value){return (Color)ColorConverter.ConvertFromString(value);}
    static void Add(List<Face> model,string color,bool emit,params V[] points){model.Add(new Face {Points=points,Color=C(color),Emissive=emit});}
    static void Surface(List<Face> model,string color,bool emit,bool reverse,params V[] points){Add(model,color,emit,points);model[model.Count-1].Cull=true;model[model.Count-1].ReverseCull=reverse;}
    static void Box(List<Face> m,V center,double x,double y,double z,string color,bool emit=false){
        double bevel=Math.Min(.09,Math.Min(x,y)*.20);
        V[] cross={new V(-x/2+bevel,-y/2,0),new V(x/2-bevel,-y/2,0),new V(x/2,-y/2+bevel,0),new V(x/2,y/2-bevel,0),new V(x/2-bevel,y/2,0),new V(-x/2+bevel,y/2,0),new V(-x/2,y/2-bevel,0),new V(-x/2,-y/2+bevel,0)};
        V[] a=cross.Select(v=>center+v+new V(0,0,-z/2)).ToArray(),b=cross.Select(v=>center+v+new V(0,0,z/2)).ToArray();
        for(int i=0;i<8;i++)Surface(m,color,emit,false,a[i],a[(i+1)%8],b[(i+1)%8],b[i]);
        Surface(m,color,emit,false,a.Reverse().ToArray());Surface(m,color,emit,false,b);
    }
    static void Cylinder(List<Face> m,double x,double y,double z,double radius,double length,string color,bool emit=false){
        const int n=20;var front=new V[n];var back=new V[n];
        for(int i=0;i<n;i++){double a=i*Math.PI*2/n;front[i]=new V(x+radius*Math.Cos(a),y+radius*Math.Sin(a),z+length/2);back[i]=new V(front[i].X,front[i].Y,z-length/2);}
        // Cylinder side winding is opposite to the caps; only the culling normal is reversed.
        for(int i=0;i<n;i++){int j=(i+1)%n;Surface(m,color,emit,true,front[i],front[j],back[j],back[i]);}
        Surface(m,color,emit,false,front);Surface(m,color,emit,false,back.Reverse().ToArray());
    }
    static List<Face> Build(bool enemy){return enemy?BuildScoutModel():BuildFrigateModel();}
    static V Curve(double u){double a=1-u;return new V(-26,0.15,28)*(a*a*a)+new V(-10,.30,27)*(3*a*a*u)+new V(0,0,15)*(3*a*u*u)+new V(0,0,-9)*(u*u*u);}
    static V Tangent(double u){double a=1-u;return (new V(-10,.30,27)-new V(-26,.15,28))*(3*a*a)+(new V(0,0,15)-new V(-10,.30,27))*(6*a*u)+(new V(0,0,-9)-new V(0,0,15))*(3*u*u);}
    static V PursuitCurve(double u){double a=1-u;return Curve(u)+new V(-12,0,12)*(a*a);}
    static V PursuitTangent(double u){return Tangent(u)+new V(24,0,-24)*(1-u);}
    static V Transform(V local,V origin,V forward,double roll){
        forward=forward.Unit;var right=Cross(new V(0,1,0),forward).Unit;var up=Cross(forward,right).Unit;
        var r=right*Math.Cos(roll)+up*Math.Sin(roll);var u=up*Math.Cos(roll)-right*Math.Sin(roll);
        return origin+r*local.X+u*local.Y+forward*local.Z;
    }
    static Point Project(V p,double w,double h){double f=w*.70;return new Point(w/2+f*p.X/Math.Max(.60,p.Z),h*.38-f*p.Y/Math.Max(.60,p.Z));}
    static V[] Clip(V[] vertices){
        var output=new List<V>();V a=vertices[vertices.Length-1];bool insideA=a.Z>=.60;
        foreach(var b in vertices){bool insideB=b.Z>=.60;if(insideA!=insideB){double t=(.60-a.Z)/(b.Z-a.Z);output.Add(a+(b-a)*t);}if(insideB)output.Add(b);a=b;insideA=insideB;}
        return output.ToArray();
    }
    static void Ship(List<Projected> faces,List<Face> model,V origin,V forward,double roll,double w,double h,double opacity=1){
        if(opacity==0)return;
        // Transform the ship basis once, rather than recomputing trigonometry for every vertex.
        forward=forward.Unit;var right=Cross(new V(0,1,0),forward).Unit;var up=Cross(forward,right).Unit;
        double cosine=Math.Cos(roll),sine=Math.Sin(roll);var axisX=right*cosine+up*sine;var axisY=up*cosine-right*sine;
        var camera=new V(-Dot(origin,axisX),-Dot(origin,axisY),-Dot(origin,forward));
        bool inside=model==pursuer ? Math.Abs(camera.X)<2.4 && camera.Y>-.85 && camera.Y<2.1 && camera.Z>-3.05 && camera.Z<5.75
            : Math.Abs(camera.X)<2.15 && camera.Y>-.4 && camera.Y<1.6 && camera.Z>-2.1 && camera.Z<2.8;
        foreach(var face in model){var world=face.Points.Select(p=>origin+axisX*p.X+axisY*p.Y+forward*p.Z).ToArray();var clipped=Clip(world);if(clipped.Length<3)continue;
            var normal=Cross(world[1]-world[0],world[2]-world[0]).Unit;
            // Only closed solid surfaces are culled. Paper-thin fins stay double-sided, and
            // interior views remain visible during the intentional passage through the camera.
            if(!inside && face.Cull && Dot(normal,world[0])*(face.ReverseCull?-1:1)>=0)continue;
            var projected=clipped.Select(p=>Project(p,w,h)).ToArray();var materialBounds=new Rect(projected[0],projected[0]);foreach(var p in projected)materialBounds.Union(p);
            // Near-camera polygons can extend thousands of pixels beyond the widget. Clip before
            // opacity/material rasterization, while keeping the original texture coordinates.
            var visible=ClipScreen(projected,w,h);if(visible.Length<3)continue;
            double light=.30+.70*Math.Abs(Dot(normal,new V(-.35,.7,-.55).Unit));
            double spec=Math.Pow(Math.Max(0,Dot(normal,new V(-.1,.5,-.86).Unit)),12)*.18;
            var value=face.Color;double shade=face.Emissive ? 1 : light;
            var color=Color.FromRgb((byte)Math.Min(255,value.R*shade+spec*70),(byte)Math.Min(255,value.G*shade+spec*75),(byte)Math.Min(255,value.B*shade+spec*80));
            double lit=face.ChargeSurface ? Ease((Charge-.50*Clamp(face.Points.Average(p=>p.Z)/3.56))/.50) : 0;
            if(lit>0)color=Color.FromRgb((byte)(color.R+(197-color.R)*lit*.94),(byte)(color.G+(243-color.G)*lit*.94),(byte)(color.B+(255-color.B)*lit*.94));
            faces.Add(new Projected {Points=visible,MaterialBounds=materialBounds,Depth=clipped.Average(p=>p.Z),Color=color,Emissive=face.Emissive,Opacity=opacity,Material=face.Material,ChargeLight=lit});
        }
    }
    static void Exhaust(List<Projected> faces,V origin,V forward,double roll,bool enemy,double power,double w,double h,double t){
        var plume=new List<Face>();
        foreach(double x in enemy?new[]{0.0}:new[]{-1.48,1.48}){
            double y=enemy?0:-.15,z=enemy?-2.04:-2.94;
            var nozzle=Transform(new V(x,y,z),origin,forward,roll);
            if(nozzle.Z>.60)engineGlows.Add(new EngineGlow {Center=Project(nozzle,w,h),Radius=Math.Min(w*.22,w*.70*(enemy?.25:.34)*2.8/nozzle.Z)});
            double length=(enemy?2.5:3.5)*(1+power*1.7)*(.95+.05*Math.Sin(t*43+x));
            for(int layer=0;layer<3;layer++){
                double radius=(enemy?.25:.34)*(1-layer*.29),tail=length*(1-layer*.23);
                string color=layer==0?"#287EE4":layer==1?"#67DFFF":"#F1FDFF";
                for(int i=0;i<12;i++){
                    double a=i*Math.PI/6,b=(i+1)*Math.PI/6;
                    Add(plume,color,true,new V(x+Math.Cos(a)*radius,y+Math.Sin(a)*radius,z),new V(x+Math.Cos(b)*radius,y+Math.Sin(b)*radius,z),new V(x+Math.Sin(t*23+x)*radius*.15,y,z-tail));
                }
            }
        }
        Ship(faces,plume,origin,forward,roll,w,h);
    }
    static Point[] ClipScreen(Point[] polygon,double w,double h){
        var vertices=new List<Point>(polygon);
        for(int side=0;side<4 && vertices.Count>0;side++){
            var output=new List<Point>();var a=vertices[vertices.Count-1];double bound=side==0||side==2?0:side==1?w:h;
            foreach(var b in vertices){
                double av=side<2?a.X:a.Y,bv=side<2?b.X:b.Y;
                bool ai=side==0||side==2?av>=bound:av<=bound,bi=side==0||side==2?bv>=bound:bv<=bound;
                if(ai!=bi){double q=(bound-av)/(bv-av);output.Add(new Point(a.X+(b.X-a.X)*q,a.Y+(b.Y-a.Y)*q));}
                if(bi)output.Add(b);a=b;
            }vertices=output;
        }return vertices.ToArray();
    }
    static Brush Brush(Color color){int key=(color.R<<16)|(color.G<<8)|color.B;Brush value;if(!brushes.TryGetValue(key,out value)){value=new SolidColorBrush(color);value.Freeze();brushes[key]=value;}return value;}
    static double Camera(double t){
        double u=Math.Max(0,Math.Min(20.5,t)-12.5),basePosition=8*(u-.8*(1-Math.Exp(-u/.8))),r=Clamp((t-18.5)/2);
        return basePosition-16*(r*r*r-.5*r*r*r*r);
    }
    public static BitmapSource Render(double w,double h,double t){
        if(cached!=null && t==cachedTime && w==cachedW && h==cachedH)return cached;
        if(pursuer==null){pursuer=Build(false);prey=Build(true);}
        var faces=new List<Projected>();engineGlows.Clear();PassScale=0;PreyDepth=PursuerDepth=1000;Pullback=Ease((t-17.8)/3.7);CameraDistance=Camera(t);Charge=t<17?Ease((t-15)/2):Math.Exp(-(t-17)*3.5);
        if(t<6.3){double p=Clamp(t/5.8),u=Math.Pow(p,2.3);V pos=Curve(u)+Tangent(u).Unit*(Math.Max(0,t-5.8)*30);PreyDepth=pos.Z;Ship(faces,prey,pos,Tangent(u),-.14*Math.Sin(u*Math.PI),w,h);Exhaust(faces,pos,Tangent(u),-.14*Math.Sin(u*Math.PI),true,p*p,w,h,t);PassScale=Math.Max(PassScale,4.2/Math.Max(.3,pos.Z));}
        if(t>=2.5 && t<8.7){double p=Clamp((t-2.5)/5.7),u=Math.Pow(p,2.3);V pos=PursuitCurve(u)+PursuitTangent(u).Unit*(Math.Max(0,t-8.2)*30);PursuerDepth=pos.Z;Ship(faces,pursuer,pos,PursuitTangent(u),-.12*Math.Sin(u*Math.PI),w,h);Exhaust(faces,pos,PursuitTangent(u),-.12*Math.Sin(u*Math.PI),false,p*p,w,h,t);PassScale=Math.Max(PassScale,4.7/Math.Max(.3,pos.Z));}
        double returning=Math.Max(0,t-8.8),enemyZ=-7+8*returning-Camera(t),enemyX=t<12.3 ? 0 : Math.Sin((t-12.3)*.8)*Ease((t-12.3)/1.5)*.65;
        if(t>=17.5){double age=t-17.5;enemyZ=-7+8*(17.5-8.8)+4*age-Camera(t);enemyX=Math.Sin((17.5-12.3)*.8)*.65;}
        var enemyPosition=new V(enemyX,.05+(t<17.5?.13*Math.Sin(t*1.4):-.42*(t-17.5)),enemyZ);Target=Project(enemyPosition,w,h);
        if(t>=8.8 && t<17.5){PreyDepth=enemyZ;var direction=new V(t<12.3?0:.52*Math.Cos((t-12.3)*.8),0,8);Ship(faces,prey,enemyPosition,direction,Math.Sin(t)*.08,w,h);Exhaust(faces,enemyPosition,direction,Math.Sin(t)*.08,true,.7,w,h,t);}
        double bodyZ=-8+9*Ease((t-10.4)/2.6)+11*Pullback,bodyY=(-1.4-h*.26/(w*.70)*4.56)*(1-Pullback);
        if(t>=22.5){double u=Clamp((t-22.5)/2.2);bodyZ+=260*Math.Pow(u,3.5);bodyY=0;}
        var body=new V(0,bodyY,bodyZ);MuzzleA=Project(body+new V(-.5,1.4,3.56),w,h);MuzzleB=Project(body+new V(.5,1.4,3.56),w,h);BreechA=Project(body+new V(-.5,1.4,-.35),w,h);BreechB=Project(body+new V(.5,1.4,-.35),w,h);Engine=Project(body+new V(0,-.15,-2.72),w,h);
        if(t>=10.4 && t<25.3){PursuerDepth=bodyZ;Ship(faces,pursuer,body,new V(0,0,1),0,w,h);Exhaust(faces,body,new V(0,0,1),0,false,t>=22.5?1:.65,w,h,t);}
        var visual=new DrawingVisual();using(var c=visual.RenderOpen()){
            c.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));
            var edge=new Pen(new SolidColorBrush(Color.FromArgb(45,9,18,22)),.22);
            foreach(var glow in engineGlows){var brush=new RadialGradientBrush();brush.GradientStops.Add(new GradientStop(Color.FromArgb(180,145,231,255),0));brush.GradientStops.Add(new GradientStop(Color.FromArgb(65,45,161,255),.40));brush.GradientStops.Add(new GradientStop(Colors.Transparent,1));c.DrawEllipse(brush,null,glow.Center,glow.Radius,glow.Radius);}
            // Fade the completed ship, not every individual surface. Per-face opacity both
            // leaks rear faces through the hull and creates hundreds of costly opacity layers.
            double opacity=1;bool opacityLayer=false;
            foreach(var face in faces.OrderByDescending(f=>f.Depth)){
                if(face.Opacity!=opacity){if(opacityLayer)c.Pop();opacity=face.Opacity;opacityLayer=opacity!=1;if(opacityLayer)c.PushOpacity(opacity);}
                var shape=new StreamGeometry();using(var p=shape.Open()){p.BeginFigure(face.Points[0],true,true);p.PolyLineTo(face.Points.Skip(1).ToArray(),true,false);}shape.Freeze();c.DrawGeometry(Brush(face.Color),face.Emissive?null:edge,shape);
                // Every part keeps its own material during side entry, rotation, first-person view and departure.
                if(!face.Emissive && face.Material>0) {
                    var material=new ImageBrush(Material(face.Material)){Stretch=Stretch.Fill,Opacity=.62*(1-.85*face.ChargeLight),ViewportUnits=BrushMappingMode.Absolute,Viewport=face.MaterialBounds};
                    c.DrawGeometry(material,null,shape);
                }
                if(face.Emissive && t>=15 && t<18.5 && face.Color.B>face.Color.R) {
                    c.PushOpacity(Charge*.75);c.DrawGeometry(Brush(Color.FromRgb(190,237,255)),null,shape);c.Pop();
                }
            }
            if(opacityLayer)c.Pop();
            c.Pop();
        }
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(w*2),(int)Math.Ceiling(h*2),192,192,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
        cached=bitmap;cachedW=w;cachedH=h;cachedTime=t;return bitmap;
    }
}
