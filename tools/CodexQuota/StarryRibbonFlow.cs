using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Approved bandflow preview v2 promoted to production. Move only brush detail inside
// fixed ribbons; never advect the whole sky. Includes both lower small vortices.
internal static class StarryRibbonFlow {
    public const double CycleSeconds=52;
    const double MinimumPlaybackSpeed=1.2,SpeedInterval=60,SpeedTransition=3;
    static readonly Random speedRandom=new Random();
    static readonly List<double> speedTargets=new List<double>{1.5};
    static readonly List<double> speedOrigins=new List<double>{0};
    // Integrate speed, rather than multiplying wall-clock time by a changing value:
    // minute changes and resize/repaint calls must never jump the ribbon phase.
    static void EnsureSpeed(int segment){
        while(speedTargets.Count<=segment){
            int previous=speedTargets.Count-1;
            double from=previous==0?speedTargets[0]:speedTargets[previous-1],to=speedTargets[previous];
            speedOrigins.Add(speedOrigins[previous]+SpeedInterval*to-(to-from)*SpeedTransition/2);
            speedTargets.Add(1+speedRandom.NextDouble());
        }
    }
    internal static double SpeedAt(double seconds){
        seconds=Math.Max(0,seconds);int segment=(int)(seconds/SpeedInterval);EnsureSpeed(segment);
        double from=segment==0?speedTargets[0]:speedTargets[segment-1];
        return from+(speedTargets[segment]-from)*Smooth((seconds-segment*SpeedInterval)/SpeedTransition);
    }
    internal static double TravelTime(double seconds){
        seconds=Math.Max(0,seconds);int segment=(int)(seconds/SpeedInterval);EnsureSpeed(segment);
        double local=seconds-segment*SpeedInterval,from=segment==0?speedTargets[0]:speedTargets[segment-1],to=speedTargets[segment];
        double u=Math.Min(1,local/SpeedTransition);
        double integral=from*Math.Min(local,SpeedTransition)+(to-from)*SpeedTransition*(u*u*u-.5*u*u*u*u);
        integral+=to*Math.Max(0,local-SpeedTransition);
        return MinimumPlaybackSpeed*(speedOrigins[segment]+integral);
    }

    sealed class Band {
        internal Point[] Points,Normals;
        internal double[] Lengths;
        internal double Total,Width,Period;
    }
    struct PixelBand {internal int Band;internal double S,Q,Weight;}
    static readonly List<Band> bands=new List<Band>();
    static byte[] original;
    static short[] detail;
    static PixelBand[] map;
    static int width,height;
    static double Clamp(double x){return Math.Max(0,Math.Min(1,x));}
    static double Smooth(double x){x=Clamp(x);return x*x*(3-2*x);}
    static Point P(double x,double y){return new Point(x*width,y*height);}
    static Point Cubic(Point a,Point b,Point c,Point d,double t){double v=1-t;return new Point(a.X*v*v*v+3*b.X*v*v*t+3*c.X*v*t*t+d.X*t*t*t,a.Y*v*v*v+3*b.Y*v*v*t+3*c.Y*v*t*t+d.Y*t*t*t);}
    static void AddBand(double normalizedWidth,double period,params double[] xy){
        var controls=new Point[xy.Length/2];for(int i=0;i<controls.Length;i++)controls[i]=P(xy[i*2],xy[i*2+1]);
        var points=new List<Point>();points.Add(controls[0]);
        for(int i=0;i+3<controls.Length;i+=3)for(int j=1;j<=70;j++)points.Add(Cubic(controls[i],controls[i+1],controls[i+2],controls[i+3],j/70.0));
        var b=new Band {Points=points.ToArray(),Normals=new Point[points.Count],Lengths=new double[points.Count],Width=width*normalizedWidth,Period=period};
        for(int i=0;i<points.Count;i++){
            if(i>0)b.Lengths[i]=b.Lengths[i-1]+(points[i]-points[i-1]).Length;
            Vector tangent=points[Math.Min(points.Count-1,i+1)]-points[Math.Max(0,i-1)];tangent.Normalize();b.Normals[i]=new Point(-tangent.Y,tangent.X);
        }
        b.Total=b.Lengths[b.Lengths.Length-1];bands.Add(b);
    }
    static double HalfWidth(Band band,double s){return band.Width*(.24+.76*Math.Sqrt(Math.Max(0,1-s)));}
    internal static void Initialize(BitmapSource image,int outputWidth,int outputHeight){
        width=outputWidth;height=outputHeight;
        var master=new FormatConvertedBitmap(image,PixelFormats.Pbgra32,null,0);master.Freeze();
        var scaled=new TransformedBitmap(master,new ScaleTransform((double)width/master.PixelWidth,(double)height/master.PixelHeight));scaled.Freeze();
        original=new byte[width*height*4];scaled.CopyPixels(original,width*4,0);
        // Remove only small brush detail. The original broad color shapes stay at their
        // original pixels; moving detail never drags a ribbon silhouette or a mountain.
        int[] kernel={1,10,45,120,210,252,210,120,45,10,1};var horizontal=new double[width*height*3];detail=new short[width*height*3];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)for(int ch=0;ch<3;ch++){
            double total=0;for(int k=-5;k<=5;k++)total+=original[(y*width+Math.Max(0,Math.Min(width-1,x+k)))*4+ch]*kernel[k+5];horizontal[(y*width+x)*3+ch]=total/1024;
        }
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)for(int ch=0;ch<3;ch++){
            double total=0;for(int k=-5;k<=5;k++)total+=horizontal[(Math.Max(0,Math.Min(height-1,y+k))*width+x)*3+ch]*kernel[k+5];
            detail[(y*width+x)*3+ch]=(short)Math.Round(original[(y*width+x)*4+ch]-total/1024);
        }
        bands.Clear();
        // Each arm is ordered OUTSIDE -> CENTER, in the painting's own curved direction.
        AddBand(.028,52, 0,.38, .08,.41,.18,.22,.28,.22, .36,.23,.40,.42,.36,.49, .32,.57,.27,.49,.275,.43, .277,.40,.31,.42,.295,.455);
        AddBand(.026,52, .58,.43, .48,.49,.40,.65,.29,.65, .20,.65,.20,.47,.24,.405, .27,.36,.33,.38,.326,.44, .325,.475,.297,.483,.295,.455);
        AddBand(.028,52, .40,.59, .50,.48,.61,.26,.725,.29, .81,.31,.84,.53,.765,.585, .70,.62,.68,.49,.72,.465, .75,.44,.765,.50,.735,.495);
        AddBand(.026,52, 1,.54, .90,.47,.85,.69,.755,.665, .665,.64,.655,.43,.715,.39, .78,.35,.80,.52,.75,.535, .72,.545,.711,.505,.735,.495);
        // Small blue vortex above the ridge, NOT the bright star halo above the big bands.
        AddBand(.0100,52, .49,.692, .53,.674,.56,.608,.608,.616, .652,.621,.659,.691,.622,.708, .586,.716,.578,.660,.604,.648, .625,.637,.635,.682,.610,.678);
        AddBand(.0092,52, .69,.691, .66,.708,.632,.737,.598,.717, .562,.694,.563,.641,.596,.630, .630,.615,.653,.666,.628,.691, .617,.705,.591,.691,.610,.678);
        // Far lower-right painted vortex: distinct from the lower middle blue vortex.
        // Follow its two short arms, with a tighter mask above the adjacent ridge.
        AddBand(.0092,52, .84,.616, .89,.583,.98,.596,.982,.658, .987,.703,.952,.730,.925,.710, .899,.689,.921,.658,.940,.675, .953,.688,.945,.705,.934,.694);
        AddBand(.0086,52, .998,.694, .984,.668,.969,.634,.938,.642, .898,.652,.893,.704,.928,.717, .957,.730,.978,.694,.954,.681, .940,.669,.922,.681,.934,.694);
        map=new PixelBand[width*height];
        for(int i=0;i<map.Length;i++)map[i].Band=-1;
        for(int id=0;id<bands.Count;id++){
            Band b=bands[id];
            for(int j=0;j<b.Points.Length-1;j++){
                Point a=b.Points[j],z=b.Points[j+1];Vector d=z-a;double length=d.Length;
                if(length<.0001)continue;
                double radius=HalfWidth(b,b.Lengths[j]/b.Total);
                int left=Math.Max(0,(int)Math.Floor(Math.Min(a.X,z.X)-radius)),right=Math.Min(width-1,(int)Math.Ceiling(Math.Max(a.X,z.X)+radius));
                int top=Math.Max(0,(int)Math.Floor(Math.Min(a.Y,z.Y)-radius)),bottom=Math.Min(height-1,(int)Math.Ceiling(Math.Max(a.Y,z.Y)+radius));
                for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++){
                    double u=Clamp(((x+.5-a.X)*d.X+(y+.5-a.Y)*d.Y)/(length*length));
                    Point nearest=a+d*u;double s=(b.Lengths[j]+length*u)/b.Total;
                    double q=((x+.5-nearest.X)*(-d.Y)+(y+.5-nearest.Y)*d.X)/length/HalfWidth(b,s);
                    if(Math.Abs(q)>=1)continue;
                    double weight=Math.Pow(1-q*q,2)*Smooth((1-s)/.035);
                    // The silhouettes and the village remain completely untouched.
                    double nx=(x+.5)/width,ny=(y+.5)/height;
                    weight*=1-Smooth((.17-nx)/.06)*Smooth((ny-.29)/.11);
                    weight*=id<4 ? 1-Smooth((ny-.67)/.035) : id<6 ? 1-Smooth((ny-.725)/.015) : 1-Smooth((ny-.706)/.018);
                    int pixel=y*width+x;
                    if(weight>map[pixel].Weight)map[pixel]=new PixelBand {Band=id,S=s,Q=q,Weight=weight};
                }
            }
        }
    }
    static Point At(Band b,double s,double q){
        double distance=Clamp(s)*b.Total;int low=0,high=b.Lengths.Length-1;
        while(high-low>1){int m=(low+high)/2;if(b.Lengths[m]<distance)low=m;else high=m;}
        double span=b.Lengths[high]-b.Lengths[low],t=span<=0?0:(distance-b.Lengths[low])/span;
        Point p=new Point(b.Points[low].X+(b.Points[high].X-b.Points[low].X)*t,b.Points[low].Y+(b.Points[high].Y-b.Points[low].Y)*t);
        Point n=new Point(b.Normals[low].X+(b.Normals[high].X-b.Normals[low].X)*t,b.Normals[low].Y+(b.Normals[high].Y-b.Normals[low].Y)*t);
        return new Point(p.X+n.X*q*HalfWidth(b,s),p.Y+n.Y*q*HalfWidth(b,s));
    }
    static double Sample(Point p,int ch){
        double x=Math.Max(0,Math.Min(width-1,p.X-.5)),y=Math.Max(0,Math.Min(height-1,p.Y-.5));
        int ix=(int)x,iy=(int)y,rx=Math.Min(width-1,ix+1),by=Math.Min(height-1,iy+1);double fx=x-ix,fy=y-iy;
        return detail[(iy*width+ix)*3+ch]*(1-fx)*(1-fy)+detail[(iy*width+rx)*3+ch]*fx*(1-fy)+detail[(by*width+ix)*3+ch]*(1-fx)*fy+detail[(by*width+rx)*3+ch]*fx*fy;
    }
    internal static void DrawSmallHighlights(DrawingContext c,double w,double h,double seconds){
        double travel=TravelTime(seconds)*1.3;
        for(int id=4;id<=5;id++)for(int i=0;i<3;i++){
            double s=(travel/CycleSeconds+i/3.0+id*.23)%1;
            double fade=Smooth(s/.08)*Smooth((1-s)/.10);
            Point a=At(bands[id],s,0),z=At(bands[id],Math.Min(1,s+.015),0);
            a=new Point(a.X*w/width,a.Y*h/height);z=new Point(z.X*w/width,z.Y*h/height);
            var pen=new Pen(new SolidColorBrush(Color.FromArgb((byte)(fade*105),173,220,245)),Math.Max(.45,w*.0017));
            pen.StartLineCap=pen.EndLineCap=PenLineCap.Round;c.DrawLine(pen,a,z);
        }
    }
    public static BitmapSource Frame(double seconds){
        seconds=TravelTime(seconds);
        var pixels=(byte[])original.Clone();
        for(int i=0;i<map.Length;i++){
            PixelBand m=map[i];if(m.Band<0 || m.Weight<.001)continue;Band b=bands[m.Band];
            bool smallBlue=m.Band==4 || m.Band==5;
            double age=(seconds*(smallBlue?1.3:1)+m.Band*b.Period*.23)%b.Period;
            double sourceS=(m.S-age/b.Period+1)%1;
            Point source=At(b,sourceS,m.Q);
            double seam=Smooth(Math.Min(sourceS,1-sourceS)/.035);
            Point seamA=new Point(),seamB=new Point();
            if(seam<1){seamA=At(b,0,m.Q);seamB=At(b,1,m.Q);}
            for(int ch=0;ch<3;ch++){
                double moving=Sample(source,ch);
                if(seam<1){double border=(Sample(seamA,ch)+Sample(seamB,ch))*.5;moving=border+(moving-border)*seam;}
                double value=original[i*4+ch]+m.Weight*(moving*(smallBlue?1.3:1)-detail[i*3+ch])*(smallBlue?1.10:.96);
                pixels[i*4+ch]=(byte)Math.Max(0,Math.Min(255,Math.Round(value)));
            }
        }
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,pixels,width*4);bitmap.Freeze();return bitmap;
    }
}
