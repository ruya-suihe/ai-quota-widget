using System;
using System.Windows;
using System.Windows.Media;

// Destruction lives in world artwork, below all captions. It never moves the actual window/HUD.
internal static class ScifiImpact {
    internal const int FragmentCount=248;
    static double unit;
    static double Clamp(double v){return Math.Max(0,Math.Min(1,v));}
    static double Ease(double v){v=Clamp(v);return v*v*(3-2*v);}
    static double Rand(int i){double v=Math.Sin(i*127.1+311.7)*43758.5453;return v-Math.Floor(v);}
    static Brush B(string value,double alpha){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));b.Opacity=Clamp(alpha);return b;}
    static Pen P(string value,double alpha,double width){return new Pen(B(value,alpha),width*unit){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};}
    static void Heat(DrawingContext c,Point p,double rx,double ry,string color,double alpha){
        var value=(Color)ColorConverter.ConvertFromString(color);var b=new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(Clamp(alpha)*255),value.R,value.G,value.B),0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(Clamp(alpha*.55)*255),value.R,value.G,value.B),.30));
        b.GradientStops.Add(new GradientStop(Colors.Transparent,1));c.DrawEllipse(b,null,p,rx*unit,ry*unit);
    }
    static void Star(DrawingContext c,Point p,double radius,double time,double alpha,int seed){
        var g=new StreamGeometry();using(var d=g.Open())for(int j=0;j<64;j++){
            double a=j*Math.PI/32+seed*.1,r=radius*(j%2==0?.65+Rand(j+seed)*.7:.09+Rand(j+seed)*.12);
            var q=new Point(p.X+Math.Cos(a)*r*unit,p.Y+Math.Sin(a)*r*.72*unit);
            if(j==0)d.BeginFigure(q,true,true);else d.LineTo(q,true,false);
        }
        c.DrawGeometry(B("#FFD7A3",alpha*.78),null,g);Heat(c,p,radius*.25,radius*.18,"#FFF5DD",alpha);
    }
    static void Shock(DrawingContext c,Point p,double radius,double flatten,double age,double alpha){
        c.PushTransform(new RotateTransform(-17+age*7,p.X,p.Y));
        c.DrawEllipse(null,P("#79CDED",alpha*.30,3.5),p,radius*unit,radius*flatten*unit);
        c.DrawEllipse(null,P("#E0F5FF",alpha*.67,.65),p,radius*unit,radius*flatten*unit);c.Pop();
    }
    internal static void Draw(DrawingContext c,Point center,double scale,double age,double time){
        unit=scale;double expand=1-Math.Exp(-age*5.5),hot=1-Ease(age/.46),life=1-Ease(age/3);
        // White-hot breach, an irregular starburst, then a blue pressure front and orange fire.
        Heat(c,center,9+expand*27,6+expand*20,"#EF7E32",hot*.83);
        if(age<.68)Star(c,center,3+expand*43,time,(1-Ease(age/.68)),800);
        Heat(c,center,2+expand*5,2+expand*4,"#FFFFFF",hot*.95);
        Shock(c,center,3+age*47,.34,age,(1-Ease(age/1.30)));
        if(age>.12)Shock(c,center,2+(age-.12)*35,.67,age,(1-Ease((age-.12)/1.65))*.65);
        // Delayed breaches travel along the hull rather than one uniform circular pop.
        for(int i=0;i<11;i++){
            double start=.07+i*.055,local=age-start;if(local<0 || local>.92)continue;
            double a=Rand(i+23)*6.283,spread=2+i*.48;
            var p=new Point(center.X+Math.Cos(a)*spread*unit+(i-5)*1.5*unit,center.Y+Math.Sin(a)*spread*.6*unit);
            double energy=1-Ease(local/.92),r=3+(1-Math.Exp(-local*7))*(4+Rand(i+10)*7);
            Heat(c,p,r,r*.72,"#E9742E",energy*.74);Heat(c,p,r*.38,r*.32,"#FFE7B5",energy*.82);
            if(local<.2)Star(c,p,r*1.3,time,(1-Ease(local/.2))*.55,i+60);
        }
        // Distinct armor plates, glowing splinters and fast needle-like ejecta; nearby fragments
        // grow and cross the camera plane, while distant pieces keep the explosion's depth.
        for(int i=0;i<FragmentCount;i++){
            double launch=Rand(i+210)*.25,local=age-launch;if(local<0)continue;
            double a=Rand(i+701)*6.283,speed=7+Rand(i+305)*34,depth=1-local*(.08+Rand(i+810)*.33);
            if(depth<=.10)continue;
            double flight=(local+.14*local*local)/depth,alpha=life*(.40+Rand(i+42)*.55);
            double x=center.X+Math.Cos(a)*flight*speed*unit,y=center.Y+Math.Sin(a)*flight*speed*.7*unit+local*local*1.8*unit;
            double tail=(1.8+local*7)*unit/depth;var p=new Point(x,y);
            var trail=new Point(x-Math.Cos(a)*tail,y-Math.Sin(a)*tail*.7);
            if(i<44){
                double r=(.55+Rand(i+75)*1.4)*unit/depth,spin=local*(1.5+Rand(i)*7)+i;
                c.PushTransform(new RotateTransform(spin*180/Math.PI,x,y));
                var plate=new StreamGeometry();using(var d=plate.Open()){
                    d.BeginFigure(new Point(x-r,y-r*.45),true,true);d.LineTo(new Point(x+r*.65,y-r*.7),true,false);d.LineTo(new Point(x+r,y+r*.2),true,false);d.LineTo(new Point(x-r*.4,y+r*.6),true,false);
                }
                c.DrawGeometry(B(i%3==0?"#8F7659":"#273B45",alpha),P("#F4B079",alpha*.9,.32),plate);c.Pop();
                c.DrawLine(P("#D37F42",alpha*.32,.45),trail,p);
            }else{
                c.DrawLine(P(i%5==0?"#C6F1FF":i%3==0?"#FFE7BD":"#FF9E50",alpha,.25+Rand(i)*.45),trail,p);
                if(i%16==0)Heat(c,p,1.6,1.6,"#FFD09A",alpha*.7);
            }
        }
        // Rising, slowly cooling smoke makes the end of the blast drift naturally into departure.
        if(age>.20)for(int i=0;i<16;i++){
            double local=age-.20-i*.025;if(local<0)continue;double a=i*2.399,r=(4+local*6)*(0.5+Rand(i+6));
            var p=new Point(center.X+Math.Cos(a)*local*8*unit,center.Y+Math.Sin(a)*local*4*unit-local*3*unit);
            Heat(c,p,r,r*.77,"#463F40",life*.27);Heat(c,p,r*.48,r*.39,"#C47945",life*Math.Exp(-local*1.6)*.25);
        }
    }
}
