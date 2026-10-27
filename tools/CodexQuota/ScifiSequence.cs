using System;
using System.Windows;
using System.Windows.Media;

// Camera-relative travel continues throughout charging and firing. Shared frame clock is supplied by ScifiTheme.
public static class ScifiSequence {
    public static int Stage;
    public static double ApproachScale,PortalPower,WarpVelocity,PortalSweep,PortalRadius,LockBlend;
    public static bool GunOnly,Destroyed;
    static double U=1;
    static double Clamp(double x){return Math.Max(0,Math.Min(1,x));}
    static double Ease(double x){x=Clamp(x);return x*x*(3-2*x);}
    static double Rand(int i){double x=Math.Sin(i*127.1+311.7)*43758.5453;return x-Math.Floor(x);}
    static Brush B(string s,double a){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));b.Opacity=Clamp(a);return b;}
    static Pen P(string s,double a,double width){return new Pen(B(s,a),width*U){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};}
    static Point Mix(Point a,Point b,double p){return new Point(a.X+(b.X-a.X)*p,a.Y+(b.Y-a.Y)*p);}
    static void Glow(DrawingContext c,Point p,double r,double a,string s){
        Color v=(Color)ColorConverter.ConvertFromString(s);var b=new RadialGradientBrush();b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(Clamp(a)*255),v.R,v.G,v.B),0));b.GradientStops.Add(new GradientStop(Colors.Transparent,1));c.DrawEllipse(b,null,p,r*U,r*U);
    }
    static void Arc(DrawingContext c,Point p,double radius,double start,double length,Pen pen){
        var g=new StreamGeometry();using(var d=g.Open()){int n=Math.Max(2,(int)(Math.Abs(length)*24));for(int j=0;j<=n;j++){double a=start+length*j/n;var q=new Point(p.X+Math.Cos(a)*radius,p.Y+Math.Sin(a)*radius);if(j==0)d.BeginFigure(q,false,false);else d.LineTo(q,true,false);}}c.DrawGeometry(null,pen,g);
    }
    static void Bolt(DrawingContext c,Point from,Point to,double alpha,double t,int seed){
        Vector direction=to-from;double len=Math.Max(1,direction.Length);var normal=new Vector(-direction.Y/len,direction.X/len);
        var g=new StreamGeometry();using(var d=g.Open()){d.BeginFigure(from,false,false);for(int j=1;j<=9;j++){double noise=j==9?0:Math.Sin(t*23+j*4+seed)*1.7*U;d.LineTo(Mix(from,to,j/9.0)+normal*noise,true,false);}}c.DrawGeometry(null,P("#9DDFF7",alpha,.55),g);
    }
    static void SpeedLines(DrawingContext c,Point focus,double w,double h,double power,double t){
        for(int i=0;i<34;i++){double angle=i*2.399,phase=(t*.84+Rand(i))%1,near=.05+phase*phase*.65,far=near+.02+power*.24;
            Point a=new Point(focus.X+Math.Cos(angle)*w*near,focus.Y+Math.Sin(angle)*h*near),b=new Point(focus.X+Math.Cos(angle)*w*far,focus.Y+Math.Sin(angle)*h*far);c.DrawLine(P(i%6==0?"#D0BA94":"#7ABFD9",power*(.11+.20*Rand(i+20)),.38),a,b);}
    }
    static void FlightField(DrawingContext c,double w,double h,double t,Point focus){
        double travel=t<10.4?t*2:20.8+ScifiGeometry.CameraDistance+(t-10.4)*.45,fade=Ease(t/.7)*(1-Ease((t-25)/2.8));
        for(int i=0;i<88;i++){
            double z=1+((Rand(i+500)*85-travel)%85+85)%85,oldZ=z+.55,a=Rand(i+100)*6.283,r=3+Rand(i+200)*25,x=Math.Cos(a)*r,y=Math.Sin(a)*r*.65;
            Point p=new Point(focus.X+w*.70*x/z,focus.Y-w*.70*y/z),tail=new Point(focus.X+w*.70*x/oldZ,focus.Y-w*.70*y/oldZ);double alpha=fade*(.15+.5*(1-z/86));
            if(i%9==0){double rr=Math.Min(8,w*.70*(.08+Rand(i)*.18)/z);var rock=new StreamGeometry();using(var d=rock.Open()){for(int j=0;j<6;j++){double k=j*6.283/6+t*.1,rad=rr*(.65+Rand(i+j)*.35);var q=new Point(p.X+Math.Cos(k)*rad,p.Y+Math.Sin(k)*rad);if(j==0)d.BeginFigure(q,true,true);else d.LineTo(q,true,false);}}c.DrawGeometry(B("#516C79",alpha*.7),P("#8EABB4",alpha*.28,.2),rock);}
            else c.DrawLine(P("#92BCC9",alpha,.24),tail,p);
        }
        double moving=Ease((t-10.4)/2)*(1-Ease((t-24.7)/1.2));SpeedLines(c,focus,w,h,moving*.72,t);
    }
    static void ChargeFX(DrawingContext c,Point muzzle,Point breech,double q,double t,int seed){
        if(q<.002)return;Glow(c,muzzle,6+q*14,q*.65,"#46ABEF");
        // Illuminate the full barrel, with a soft energy shell around the projected axis.
        c.DrawLine(P("#24A8F1",q*.30,5.0),breech,muzzle);c.DrawLine(P("#8AE5FF",q*.46,2.0),breech,muzzle);
        for(int i=0;i<48;i++){
            double phase=(t*(.62+Rand(i+seed)*.35)+Rand(i+100+seed))%1,a=i*2.399+phase*1.3+t*.25,radius=(2+Math.Pow(1-phase,1.5)*(20+Rand(i)*14))*U;
            Point p=new Point(muzzle.X+Math.Cos(a)*radius,muzzle.Y+Math.Sin(a)*radius*.72),tail=new Point(muzzle.X+Math.Cos(a-.10)*(radius+2*U),muzzle.Y+Math.Sin(a-.10)*(radius+2*U)*.72);double alpha=q*Ease(phase/.25)*(.35+.55*phase);
            c.DrawLine(P("#5CBFEA",alpha,.38),tail,p);c.DrawEllipse(B("#BCEBFF",alpha),null,p,(.22+phase*.30)*U,(.22+phase*.30)*U);
        }
        for(int i=0;i<7;i++){double p=(t*1.7+Rand(i+seed))%1,a=Rand(i+seed+80)*6.283+t*.4;Point end=new Point(muzzle.X+Math.Cos(a)*(3+p*10)*U,muzzle.Y+Math.Sin(a)*(3+p*8)*U);if(q>.4)Bolt(c,muzzle,end,(q-.4)*(1-p)*.65,t,i+seed);}
        for(int j=0;j<10;j++){double f=j/9.0,lit=Ease((q-f*.55)/.30);Point p=Mix(breech,muzzle,f);Glow(c,p,2.5,lit*q*.17,"#61C4F4");if(j>0)c.DrawLine(P("#A3E4FF",lit*q*.52,.75),Mix(breech,muzzle,(j-1)/9.0),p);}
        Arc(c,muzzle,(2+q*3)*U,t*2,Math.PI*1.4,P("#B6E5F5",q*.7,.5));c.DrawEllipse(B("#F0FAFF",q*q*.9),null,muzzle,(.6+q*.9)*U,(.6+q*.9)*U);
    }
    static void Beam(DrawingContext c,Point muzzle,Point target,double power,double t,int seed){
        Glow(c,muzzle,12,power*.8,"#70D7FF");c.DrawLine(P("#2978D0",power*.24,8),muzzle,target);c.DrawLine(P("#55CFF5",power*.65,3.4),muzzle,target);c.DrawLine(P("#D6F9FF",power*.95,1.25),muzzle,target);c.DrawLine(P("#FFFFFF",power,.42),muzzle,target);
        for(int i=0;i<24;i++){double f=(t*2.8+Rand(i+seed))%1;Point p=Mix(muzzle,target,f);Vector d=target-muzzle;d.Normalize();Vector normal=new Vector(-d.Y,d.X);p+=normal*(Math.Sin(i*3.1+t*11)*(1+Rand(i)*2)*U);c.DrawLine(P("#A9E9FF",power*(1-f)*.7,.4),p-d*(2*U),p+d*(1*U));}
    }
    static void Impact(DrawingContext c,Point target,double age,double t){ScifiImpact.Draw(c,target,U,age,t);}
    static void Portal(DrawingContext c,Point p,double w,double h,double t){
        double close=Ease((t-26)/2),open=Ease((t-20.5)/1.9),full=Math.Min(w*.24,Math.Max(h*.38,14*U));
        PortalPower=open*(1-close);PortalRadius=(.7*U+full*open)*(1-.82*close);PortalSweep=6.283*Ease((t-20.5)/1.5);
        ScifiPortal.Draw(c,p,w,h,t,U);
    }
    public static void Draw(DrawingContext c,Window window,double dx,double dy,double seconds){
        var root=(System.Windows.Controls.Border)window.FindName("Root");
        c.PushTransform(new TranslateTransform(dx,dy));
        Draw(c,root.ActualWidth,root.ActualHeight,seconds);c.Pop();
    }
    internal static void Draw(DrawingContext c,double w,double h,double seconds){
        double t=seconds%30;U=w/180;
        Stage=t<3?0:t<8.7?1:t<10.4?2:t<15?3:t<17?4:t<19.5?5:t<20.5?6:t<22.5?7:t<26?8:t<28?9:10;PortalPower=PortalSweep=PortalRadius=WarpVelocity=0;GunOnly=t>=10.4&&t<17.8;Destroyed=t>=17.5;
        var mesh=ScifiGeometry.Render(w,h,t);ApproachScale=ScifiGeometry.PassScale;LockBlend=t<10.4?0:Ease((t-10.4)/2.6)*(1-ScifiGeometry.Pullback);
        c.PushTransform(Transform.Identity);c.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));// The impact shakes only the scene. Window bounds, controls and captions stay fixed.
        double impactAge=t-17.5,shake=impactAge>=0 ? Math.Exp(-impactAge*8)*1.15*U : 0;
        c.PushTransform(new TranslateTransform(Math.Sin(t*103)*shake,Math.Cos(t*89)*shake*.55));
        var focus=new Point(w*.5,h*.38);FlightField(c,w,h,t,focus);
        if(t>=20.5 && t<28)Portal(c,focus,w,h,t);c.PushOpacity(.92);c.DrawImage(mesh,new Rect(0,0,w,h));c.Pop();
        Point a=ScifiGeometry.MuzzleA,b=ScifiGeometry.MuzzleB,target=ScifiGeometry.Target;
        // The same detailed mesh remains visible in every phase; no whole-ship picture swaps.
        if(t>=15 && t<18.2){double q=t<17?Ease((t-15)/2):Math.Exp(-(t-17)*5);ChargeFX(c,a,ScifiGeometry.BreechA,q,t,20);ChargeFX(c,b,ScifiGeometry.BreechB,q,t,120);}
        if(t>=17 && t<17.55){double fire=Clamp((t-17)/.55),power=Math.Pow(Math.Sin(fire*Math.PI),.4);Beam(c,a,target,power,t,10);Beam(c,b,target,power,t,80);}
        if(t>=17.5 && t<20.5)Impact(c,target,t-17.5,t);
        if(t>=22.5 && t<24.7){double u=Clamp((t-22.5)/2.2);WarpVelocity=260*3.5*Math.Pow(u,2.5)/2.2;Glow(c,ScifiGeometry.Engine,6+u*10,u*.7,"#7FD5FF");SpeedLines(c,focus,w,h,u*.85,t*2);}
        if(t>=24.7 && t<25.3)Glow(c,focus,12,(1-Ease((t-24.7)/.6))*.7,"#A6DFFF");c.Pop();c.Pop();c.Pop();
    }
}
