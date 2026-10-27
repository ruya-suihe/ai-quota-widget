using System;
using System.Windows;
using System.Windows.Media;

// Native drawing only: broken turbulent filaments replace the rigid circular outline.
public static class ScifiPortal {
    public static int ParticleCount;
    public static double EntrySurge;
    static double unit;
    static double Clamp(double v){return Math.Max(0,Math.Min(1,v));}
    static double Ease(double v){v=Clamp(v);return v*v*(3-2*v);}
    static double Rand(int i){double v=Math.Sin(i*127.1+311.7)*43758.5453;return v-Math.Floor(v);}
    static Brush B(string s,double a){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));b.Opacity=Clamp(a);return b;}
    static Pen P(string s,double a,double w){return new Pen(B(s,a),w*unit){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};}
    static Point At(Point p,double a,double r){return new Point(p.X+Math.Cos(a)*r,p.Y+Math.Sin(a)*r);}
    static void Fog(DrawingContext c,Point p,double rx,double ry,double alpha,string color){
        Color v=(Color)ColorConverter.ConvertFromString(color);var b=new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(Clamp(alpha)*255),v.R,v.G,v.B),0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(Clamp(alpha*.48)*255),v.R,v.G,v.B),.36));
        b.GradientStops.Add(new GradientStop(Colors.Transparent,1));c.DrawEllipse(b,null,p,Math.Max(.01,rx),Math.Max(.01,ry));
    }
    static void Ribbon(DrawingContext c,Point p,double r,double angle,double arc,double seed,double t,Pen pen){
        var g=new StreamGeometry();using(var d=g.Open()){
            for(int j=0;j<=16;j++){double a=angle+arc*j/16,noise=Math.Sin(a*11+t*5+seed)*.023+Math.Sin(a*23-t*8+seed)*.013;Point q=At(p,a,r*(1+noise));if(j==0)d.BeginFigure(q,false,false);else d.LineTo(q,true,false);}
        }c.DrawGeometry(null,pen,g);
    }
    public static void Draw(DrawingContext c,Point p,double w,double h,double t,double scale){
        unit=scale;double age=t-20.5,opening=Ease(age/1.9),closing=Ease((t-26)/2),alpha=Ease(age/.30)*(1-closing);
        double full=Math.Min(w*.24,Math.Max(h*.38,14*unit)),r=(.7*unit+full*opening)*(1-.82*closing),sweep=6.283*Ease(age/1.5),turn=age*5.6;
        double surge=Math.Exp(-Math.Pow((t-23.75)/.24,2));EntrySurge=surge;ParticleCount=500;
        // A breathing outer corona, stronger and wider during the ship's crossing.
        Fog(c,p,r*1.42,r*1.42,alpha*.23,"#1167D5");
        if(opening>.04){
            var depth=new RadialGradientBrush();depth.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha*245),1,3,12),0));depth.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha*238),3,9,25),.37));depth.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha*220),12,46,83),.76));depth.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha*50),51,138,191),1));
            c.DrawEllipse(depth,null,p,r*.97,r*.97);
            c.PushClip(new EllipseGeometry(p,r*.96,r*.96));
            // Drifting light clouds collect into a spiral funnel, rather than flat concentric rings.
            for(int i=0;i<24;i++){
                double phase=(age*.16+Rand(i+110))%1,a=turn*.24+i*2.399+phase*3.5,rr=r*(.23+.72*(1-phase));Point q=At(p,a,rr);
                c.PushTransform(new RotateTransform(a*180/Math.PI+65,q.X,q.Y));
                Fog(c,q,r*(.25+.12*Rand(i)),r*(.06+.045*Rand(i+70)),alpha*(.17+.12*Rand(i+15))*(.5+opening),i%3==0?"#6CCCEB":"#2A7CDD");c.Pop();
            }
            for(int i=0;i<92;i++){
                double phase=(age*(.27+Rand(i+60)*.17)+Rand(i+130))%1,a=i*2.399+age*.75+phase*3.8,rr=r*(.08+.85*Math.Pow(1-phase,1.5));
                var g=new StreamGeometry();using(var d=g.Open()){for(int j=0;j<7;j++){double f=j/6.0;Point q=At(p,a-f*.32,rr+r*f*.09);if(j==0)d.BeginFigure(q,false,false);else d.LineTo(q,true,false);}}
                c.DrawGeometry(null,P(i%5==0?"#B9F0FF":"#528FD9",alpha*(.13+.29*phase)*Math.Sin(phase*Math.PI),.25+Rand(i)*.3),g);
            }
            Fog(c,p,r*.31,r*.31,alpha*.88,"#01030D");c.Pop();
        }
        // Uneven bloom pockets broaden the energy edge without restoring a rigid outline.
        for(int i=0;i<28;i++){
            double a=turn+sweep*Rand(i+910),pulse=.3+.7*Math.Pow(.5+.5*Math.Sin(t*5+i*1.9),2);Point q=At(p,a,r);
            Fog(c,q,(4+surge*3)*unit,(4+surge*3)*unit,alpha*pulse*.47,"#4AB8FF");
            if(i%3==0)Fog(c,q,1.7*unit,1.7*unit,alpha*pulse*.65,"#DFFAFF");
        }
        // Separate irregular luminous ribbons leave gaps and fluctuate independently.
        for(int i=0;i<38;i++){
            double a=turn+sweep*Rand(i+330),arc=(.10+.35*Rand(i+90))*Math.Min(1,sweep/1.5),rr=r*(.96+Rand(i+44)*.10),pulse=.45+.55*Math.Pow(.5+.5*Math.Sin(t*8+i),2);
            Point cloud=At(p,a,rr);c.PushTransform(new RotateTransform(a*180/Math.PI+90,cloud.X,cloud.Y));
            Fog(c,cloud,(8+Rand(i)*6)*unit,(2.5+Rand(i+4)*2)*unit,alpha*pulse*.48,"#53BDFF");c.Pop();
            Ribbon(c,p,rr,a,arc,i,t,P("#59CAFF",alpha*pulse*(.52+surge*.30),4.0+Rand(i)*3));
            Ribbon(c,p,rr,a,arc*.8,i,t,P(i%4==0?"#FFE6B7":"#C5F4FF",alpha*pulse*(.65+surge*.3),.45+Rand(i)*1.1));
        }
        // Exactly 500 edge particles: orbiting embers, outward spray, and long cooling tails.
        for(int i=0;i<500;i++){
            int layer=i<260?0:i<420?1:2;
            double phase=(age*(.75+Rand(i+80)*1.15)+Rand(i+9))%1,a=turn+sweep*Rand(i+420),rad=r;
            double rr,trail,opacity;
            if(layer==0){rr=rad+(Math.Sin(t*9+i)*1.5+(Rand(i)-.5)*5)*unit;trail=(.6+Rand(i+4)*2)*unit;opacity=.45+.55*Math.Sin(phase*Math.PI);}
            else if(layer==1){rr=rad+Math.Pow(phase,1.25)*(6+Rand(i+5)*23)*(1+surge)*unit;a+=phase*.42;trail=(1+phase*5)*unit;opacity=1-phase;}
            else {rr=rad+phase*phase*(14+Rand(i+10)*28)*unit;a+=phase*.70;trail=(2+phase*7)*unit;opacity=Math.Pow(1-phase,1.5);}
            if(rr<0)continue;Point q=At(p,a,rr),tail=At(p,a-.018-phase*.03,Math.Max(.1,rr-trail));
            double flicker=.35+.65*Math.Pow(.5+.5*Math.Sin(t*(14+Rand(i)*12)+i),2),brightness=alpha*opacity*flicker*(.8+surge*.35);
            string color=i%6==0?"#FFD49A":i%3==0?"#FFFFFF":"#99DEFF";
            c.DrawLine(P(color,brightness,.22+Rand(i+3)*.6),tail,q);
            if(i%12==0)Fog(c,q,(1.5+surge)*unit,(1.5+surge)*unit,brightness*.65,"#8DD8FF");
        }
        // Opening pressure wave and the brief entry flash travel beyond the corona.
        if(age<2.8){double wave=Clamp((age-.55)/2),light=alpha*Math.Sin(wave*Math.PI)*.24;
            for(int i=0;i<12;i++)Ribbon(c,p,r*(1+wave*.7),i*6.283/12+turn*.1,.22,i,t,P("#8CD6FA",light,.65));}
        Fog(c,p,r*.85,r*.85,surge*alpha*.30,"#75CFFF");
        if(t>=23.7 && t<25.0){double burst=(t-23.7)/1.3;
            for(int i=0;i<80;i++){double a=i*2.399+turn*.08,rr=r*(1+burst*(.3+Rand(i+4)*.65));Point q=At(p,a,rr);c.DrawLine(P("#DAF8FF",alpha*(1-burst)*.65,.55),At(p,a,rr-(2+burst*8)*unit),q);}}
        if(opening<.9){Point head=At(p,turn+sweep,r);Fog(c,head,5*unit,5*unit,alpha,"#CFF6FF");}
    }
}
