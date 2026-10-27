using System;
using System.Windows;

// Reusable, continuous spiral attraction. Scene artwork supplies the sinks and its
// boundary mask; the field has no dependency on themes, controls, or frame clocks.
internal sealed class SpiralFlow {
    internal struct Sink {
        internal readonly Point Center;
        internal readonly double Pull,Turn;
        internal Sink(double x,double y,double pull,double turn){Center=new Point(x,y);Pull=pull;Turn=turn;}
    }
    readonly Sink[] sinks;
    readonly double aspect;
    readonly Func<Point,double> mask;
    const int FieldWidth=160,FieldHeight=96;
    readonly Vector[,] velocities=new Vector[FieldWidth+1,FieldHeight+1];
    internal SpiralFlow(double imageAspect,Sink[] sources,Func<Point,double> boundaryMask){
        aspect=imageAspect;sinks=(Sink[])sources.Clone();mask=boundaryMask;
        for(int y=0;y<=FieldHeight;y++)for(int x=0;x<=FieldWidth;x++)velocities[x,y]=Velocity(new Point((double)x/FieldWidth,(double)y/FieldHeight));
    }
    internal Vector Velocity(Point point){
        double total=0,vx=0,vy=0;
        foreach(var sink in sinks){
            double x=point.X-sink.Center.X,y=(point.Y-sink.Center.Y)/aspect;
            double radius=x*x+y*y+.0004,weight=1/(radius*radius);
            vx+=(-sink.Pull*x-sink.Turn*y)*weight;
            vy+=(sink.Turn*x-sink.Pull*y)*aspect*weight;total+=weight;
        }
        double fade=mask==null?1:Math.Max(0,Math.Min(1,mask(point)));
        return total==0?new Vector():new Vector(vx/total*fade,vy/total*fade);
    }
    internal Point Trace(Point point,double seconds,int steps){
        double dt=seconds/Math.Max(1,steps);
        for(int i=0;i<steps;i++){
            var first=SampleVelocity(point);var middle=point+first*(dt*.5);
            point+=SampleVelocity(middle)*dt;
            point=new Point(Math.Max(0,Math.Min(1,point.X)),Math.Max(0,Math.Min(1,point.Y)));
        }
        return point;
    }
    Vector SampleVelocity(Point p){
        double gx=Math.Max(0,Math.Min(FieldWidth-.000001,p.X*FieldWidth)),gy=Math.Max(0,Math.Min(FieldHeight-.000001,p.Y*FieldHeight));
        int x=(int)gx,y=(int)gy;double fx=gx-x,fy=gy-y;
        return velocities[x,y]*((1-fx)*(1-fy))+velocities[x+1,y]*(fx*(1-fy))+velocities[x,y+1]*((1-fx)*fy)+velocities[x+1,y+1]*(fx*fy);
    }
}
