using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Leisurely one-way boat passage and gliding crane, composited below the data layer.
internal static class InkScene {
    internal const int PresetIndex=9;
    internal const double BoatCycleSeconds=72,CraneCycleSeconds=24,CraneFlightSeconds=9;
    static double Seconds;
    internal static double CraneOpacity,BoatOpacity,CraneY,CraneX,BoatX,BoatY;
    // All animated subjects live below the opaque data glyphs. Text intersections no
    // longer erase whole subjects, including the larger steam robot and tidal animals.
    internal static double ActorOpacity(Rect bounds){return 1;}
    internal static Dictionary<string,string> Palette(){
        var p=Theme.Defaults();
        string[] keys={"background","track","barBorder","windowBorder","title","label","valueText","timeText","caption","status","icons","ticks","weekMarker","alert","failure","quotaGreen","timeGreen","quotaYellow","timeYellow","quotaOrange","timeOrange","quotaRed","timeRed"};
        string[] values={"#F3EEE2","#DBD6CB","#8C8D82","#8C8D82","#262C29","#262C29","#262C29","#262C29","#262C29","#62665E","#62665E","#555B54","#6B2D22","#922B23","#922B23","#92988C","#92988C","#C4B08B","#C4B08B","#CEA38D","#CEA38D","#D2978C","#D2978C"};
        for(int i=0;i<keys.Length;i++)p[keys[i]]=values[i];return p;
    }
    static double Smooth(double t){t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);}
    static Brush Brush(string hex,double opacity) {var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));brush.Opacity=opacity;return brush;}
    static Pen Pen(string hex,double opacity,double width) {return new Pen(Brush(hex,opacity),width) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};}
    static Geometry Polygon(Point[] points) {
        var shape=new StreamGeometry();using(var c=shape.Open()) {c.BeginFigure(points[0],true,true);c.PolyLineTo(points.Skip(1).ToArray(),true,false);}shape.Freeze();return shape;
    }
    static void Line(DrawingContext c,Pen pen,params Point[] points) {for(int i=1;i<points.Length;i++)c.DrawLine(pen,points[i-1],points[i]);}
    internal static void DrawBackdrop(DrawingContext c,double w,double h){
        c.DrawImage(ScifiAssets.Load("ink-landscape.png"),new Rect(0,0,w,h));
        var water=new StreamGeometry();using(var path=water.Open()){
            path.BeginFigure(new Point(0,h*.82),false,false);
            path.BezierTo(new Point(w*.25,h*.59),new Point(w*.46,h*.93),new Point(w,h*.67),true,false);
        }
        c.DrawGeometry(null,Pen("#566457",.19,.55),water);
    }
    internal static void DrawActors(DrawingContext c,double w,double fullH,double seconds){
        Seconds=seconds;
        // The boat travels in one direction; strokes are deliberately unhurried.
        double t=Seconds%CraneCycleSeconds,boatTime=Seconds%BoatCycleSeconds,phase=boatTime*Math.PI*2/6;
        // One unhurried passage; the loop resets only after the boat is fully outside.
        double progressBoat=boatTime/BoatCycleSeconds;
        double bx=w*(-.15+1.3*progressBoat),by=fullH*(.86-.32*Smooth(progressBoat))+Math.Sin(boatTime*Math.PI*2/12)*.18;BoatX=bx;BoatY=by;
        BoatOpacity=1;
        c.PushOpacity(BoatOpacity);
        var ink=Pen("#343C35",.85,.72);
        c.DrawGeometry(Brush("#424C40",.84),null,Polygon(new[]{new Point(bx-7,by),new Point(bx-4,by+2),new Point(bx+5,by+2),new Point(bx+8,by-1)}));
        c.DrawEllipse(Brush("#343C35",.9),null,new Point(bx,by-5),1,1);
        Line(c,ink,new Point(bx,by-4),new Point(bx-1+Math.Sin(phase)*.5,by-.5));
        double ox=bx+5*Math.Sin(phase),oy=by+2.5;
        Line(c,ink,new Point(bx-1,by-2),new Point(bx+2,by-2),new Point(ox,oy));
        for(int i=0;i<2;i++)c.DrawEllipse(null,Pen("#526354",.12,.45),new Point(bx-2-i*2,by+3+i),7+i*2,1+i*.4);
        c.Pop();CraneOpacity=0;
        // A slow crossing with long quiet intervals; gentle wingbeats alternate with gliding.
        double flight=t-3;
        if(flight>=0 && flight<CraneFlightSeconds) {
            double progress=flight/CraneFlightSeconds,x=w*(-.08+1.16*progress),y=fullH*(.22+Math.Sin(progress*Math.PI*2)*.025);
            double fade=Smooth(flight/1.2)*Smooth((CraneFlightSeconds-flight)/1.2);CraneY=y;CraneX=x;
            double glide=.5+.5*Math.Sin(flight*Math.PI*2/6);
            double wing=Math.Sin(flight*Math.PI*2/2.2)*4*(.25+.75*glide);
            CraneOpacity=fade;
            c.PushOpacity(CraneOpacity);
            var edge=Pen("#596359",.9,.65);var white=Brush("#ECEBE1",.96);
            c.DrawGeometry(white,edge,Polygon(new[]{new Point(x-1,y),new Point(x-5,y-2-wing),new Point(x-8,y-1-wing*.65),new Point(x-3,y+1)}));
            c.DrawGeometry(white,edge,Polygon(new[]{new Point(x,y),new Point(x+4,y-3+wing),new Point(x+7,y-2+wing*.7),new Point(x+2,y+1)}));
            c.DrawEllipse(white,edge,new Point(x,y),3,.9);
            Line(c,edge,new Point(x+2,y),new Point(x+4,y-2),new Point(x+6,y-2));
            Line(c,Pen("#8A4A36",.85,.55),new Point(x+6,y-2),new Point(x+8,y-1.5));
            Line(c,edge,new Point(x-2,y+.4),new Point(x-6,y+2));c.Pop();
        }
    }
}
