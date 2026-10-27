using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

// One articulated hard-surface model per ship, used at every angle of the same flight path.
// Materials are local to armor, vents, gun housings and trim, rather than a whole-ship sprite.
public static partial class ScifiGeometry {
    const int Armor=1,Brass=2,Vent=3,GunHousing=4,Copper=5;
    static readonly Dictionary<int,BitmapSource> materials=new Dictionary<int,BitmapSource>();
    static BitmapSource Material(int id) {
        BitmapSource value;if(materials.TryGetValue(id,out value))return value;
        BitmapSource source;System.Windows.Int32Rect crop;
        if(id==GunHousing){source=ScifiAssets.Load("rider-cannon.png");crop=new System.Windows.Int32Rect(380,450,190,130);}
        else if(id==Copper){source=ScifiAssets.Load("scout-rear.png");crop=new System.Windows.Int32Rect(880,270,155,115);}
        else {source=ScifiAssets.Load("frigate-side.png");crop=id==Brass ? new System.Windows.Int32Rect(770,350,85,120) : id==Vent ? new System.Windows.Int32Rect(580,390,190,90) : new System.Windows.Int32Rect(1450,320,235,100);}
        value=new CroppedBitmap(source,crop);value.Freeze();materials[id]=value;return value;
    }
    static void FinishPart(List<Face> m,int start,int material){for(int i=start;i<m.Count;i++)m[i].Material=material;}
    static void Panel(List<Face> m,V p,double x,double y,double z,string color,int material){int start=m.Count;Box(m,p,x,y,z,color);FinishPart(m,start,material);}
    static V[] Section(double z,double width,double bottom,double top) {
        double b=Math.Min(.14,width*.2);
        return new[]{new V(-width+b,bottom,z),new V(width-b,bottom,z),new V(width,bottom+b,z),new V(width,top-b,z),new V(width-b,top,z),new V(-width+b,top,z),new V(-width,top-b,z),new V(-width,bottom+b,z)};
    }
    static void ArmoredHull(List<Face> m,double[] z,double[] width,double bottom,double[] top,string color,int material){
        for(int i=0;i<z.Length-1;i++){
            V[] a=Section(z[i],width[i],bottom,top[i]),b=Section(z[i+1],width[i+1],bottom,top[i+1]);
            for(int j=0;j<8;j++){
                string tint=j%2==1?"#9E937B":color;Surface(m,tint,false,false,a[j],a[(j+1)%8],b[(j+1)%8],b[j]);
                m[m.Count-1].Material=j%2==1?Brass:material;
            }
        }
        Surface(m,color,false,false,Section(z[0],width[0],bottom,top[0]).Reverse().ToArray());m[m.Count-1].Material=material;
        Surface(m,color,false,false,Section(z[z.Length-1],width[width.Length-1],bottom,top[top.Length-1]));m[m.Count-1].Material=material;
    }
    static void Profile(List<Face> m,double x,double y,double[] z,double[] radius,string color,int material,int sides){
        var rings=new V[z.Length][];
        for(int i=0;i<z.Length;i++){rings[i]=new V[sides];for(int j=0;j<sides;j++){double a=j*2*Math.PI/sides;rings[i][j]=new V(x+radius[i]*Math.Cos(a),y+radius[i]*Math.Sin(a),z[i]);}}
        for(int i=0;i<z.Length-1;i++)for(int j=0;j<sides;j++){int n=(j+1)%sides;Surface(m,color,false,false,rings[i][j],rings[i][n],rings[i+1][n],rings[i+1][j]);m[m.Count-1].Material=material;}
        Surface(m,color,false,false,rings[0].Reverse().ToArray());m[m.Count-1].Material=material;
        Surface(m,color,false,false,rings[rings.Length-1]);m[m.Count-1].Material=material;
    }
    static void Fin(List<Face> m,V[] outline,V thickness,string color,int material){
        var a=outline.Select(v=>v-thickness*.5).ToArray();var b=outline.Select(v=>v+thickness*.5).ToArray();
        // Fin caps remain double-sided: all thin silhouettes survive the camera turn.
        Add(m,color,false,a);m[m.Count-1].Material=material;Add(m,color,false,b.Reverse().ToArray());m[m.Count-1].Material=material;
        for(int i=0;i<a.Length;i++){int j=(i+1)%a.Length;Add(m,"#B8A582",false,a[i],a[j],b[j],b[i]);m[m.Count-1].Material=Brass;}
    }
    static void WindowStrip(List<Face> m,double x,double y,double z,double width,string color){
        for(int i=0;i<4;i++){double px=x+(i-1.5)*width/4;Add(m,color,true,new V(px-width*.075,y,z),new V(px+width*.075,y,z),new V(px+width*.075,y+.075,z),new V(px-width*.075,y+.075,z));}
    }
    static void TurretBearing(List<Face> m){
        const int n=16;for(int i=0;i<n;i++){double a=i*2*Math.PI/n,b=(i+1)*2*Math.PI/n;
            Surface(m,"#927F5B",false,true,new V(Math.Cos(a)*.76,.91,Math.Sin(a)*.76-.25),new V(Math.Cos(b)*.76,.91,Math.Sin(b)*.76-.25),new V(Math.Cos(b)*.76,1.08,Math.Sin(b)*.76-.25),new V(Math.Cos(a)*.76,1.08,Math.Sin(a)*.76-.25));m[m.Count-1].Material=Brass;
        }
        Panel(m,new V(0,1.11,-.25),1.22,.24,1.3,"#35505B",Armor);
    }
    static List<Face> BuildFrigateModel(){
        var m=new List<Face>();
        ArmoredHull(m,new[]{-2.65,-1.8,-.6,1.0,2.4,3.6,4.55,5.65},new[]{.72,1.02,1.13,1.13,.98,.74,.42,.045},-.48,new[]{.32,.45,.54,.54,.43,.34,.20,-.04},"#42616A",Armor);
        // Separate overlapping shoulder plates, undercut vents and machined bronze seams.
        foreach(int sign in new[]{-1,1}){
            for(int i=0;i<6;i++){
                double z=-1.35+i*.88,width=z<2.5?1.04:z<3.5?.83:.52;
                Panel(m,new V(sign*width,.44-i*.022,z),.29,.10,.68,"#58717A",Armor);
                Panel(m,new V(sign*(width+.07),.10,z),.065,.23,.43,"#172D38",Vent);
                Panel(m,new V(sign*width,.51-i*.022,z-.35),.35,.035,.04,"#B8A582",Brass);
            }
            Profile(m,sign*1.48,-.15,new[]{-2.83,-2.66,-2.42,-1.98,-.8,.35,.9},new[]{.35,.49,.60,.56,.56,.47,.23},"#263C47",Armor,16);
            foreach(double z in new[]{-2.45,-1.65,-.55,.35})Profile(m,sign*1.48,-.15,new[]{z-.07,z,z+.07},new[]{.57,.62,.57},"#B39D76",Brass,16);
            // Recessed exhaust with a dark lip, nested blue core and a white-hot center.
            Cylinder(m,sign*1.48,-.15,-2.83,.36,.08,"#182D38");
            Cylinder(m,sign*1.48,-.15,-2.88,.28,.025,"#3699C4",true);
            Cylinder(m,sign*1.48,-.15,-2.90,.17,.018,"#A4E9FF",true);
            for(int j=0;j<6;j++){
                double a=j*Math.PI/3;
                Panel(m,new V(sign*1.48+Math.Cos(a)*.47,-.15+Math.Sin(a)*.47,-2.68),.06,.075,.30,"#A99570",Brass);
            }
            Fin(m,new[]{new V(sign*1.05,.19,-2.25),new V(sign*2.27,.17,-2.2),new V(sign*1.87,.17,-1.35),new V(sign*1.05,.19,-.4)},new V(0,.10,0),"#4E6973",Armor);
            Fin(m,new[]{new V(sign*1.17,.32,-2.1),new V(sign*1.17,1.58,-2.38),new V(sign*1.17,1.39,-1.62),new V(sign*1.17,.35,-.75)},new V(.09,0,0),"#4E6973",Armor);
            // Functional recoil sleds and stepped cannon sleeves retain the existing muzzle axis.
            Panel(m,new V(sign*.5,1.40,-.38),.48,.40,1.00,"#2F4954",GunHousing);
            Panel(m,new V(sign*.5,1.63,-.38),.34,.045,.78,"#AD9A75",Brass);
            int barrelStart=m.Count;
            Profile(m,sign*.5,1.4,new[]{.08,.35,.55,1.1,1.95,2.7,3.35,3.47,3.55},new[]{.22,.22,.18,.18,.145,.145,.12,.18,.12},"#344F5B",GunHousing,12);
            foreach(double z in new[]{.45,1.2,2.05,2.85,3.44})Cylinder(m,sign*.5,1.4,z,.19,.055,"#C0AA80");
            Cylinder(m,sign*.5,1.4,3.56,.095,.02,"#9CDDF4",true);
            Panel(m,new V(sign*.5,1.585,1.55),.035,.035,2.65,"#76D2F0",0);
            Add(m,"#9DE9FF",true,new V(sign*.5-.015,1.606,.23),new V(sign*.5+.015,1.606,.23),new V(sign*.5+.015,1.606,2.88),new V(sign*.5-.015,1.606,2.88));
            for(int i=barrelStart;i<m.Count;i++)m[i].ChargeSurface=true;
        }
        Panel(m,new V(0,.75,-.65),1.35,.41,1.46,"#385663",Armor);
        Panel(m,new V(0,1.04,-1.07),.73,.25,.64,"#1B303C",Vent);
        WindowStrip(m,0,.67,.091,1.10,"#F0C68B");
        TurretBearing(m);
        Panel(m,new V(0,1.46,-1.92),.13,.92,.12,"#233C48",Vent);
        Panel(m,new V(0,1.77,-1.86),.39,.22,.18,"#547581",Armor);
        WindowStrip(m,0,1.72,-1.758,.27,"#D6BC86");
        return m;
    }
    static List<Face> BuildScoutModel(){
        var m=new List<Face>();
        ArmoredHull(m,new[]{-1.72,-.85,.35,1.3,2.25,2.75},new[]{.38,.59,.53,.36,.13,.025},-.22,new[]{.25,.40,.35,.21,.07,-.04},"#987147",Copper);
        Panel(m,new V(0,.48,.35),.49,.20,.95,"#1D3645",Vent);
        foreach(int sign in new[]{-1,1}){
            Fin(m,new[]{new V(sign*.35,.02,.68),new V(sign*2.05,-.02,-1.62),new V(sign*1.28,-.02,-1.45),new V(sign*.39,.02,-.56)},new V(0,.065,0),"#A98151",Copper);
            Panel(m,new V(sign*.48,.13,-.85),.22,.20,1.13,"#71543B",Brass);
            Fin(m,new[]{new V(sign*.36,.28,-1.36),new V(sign*.36,.88,-1.54),new V(sign*.36,.73,-.85),new V(sign*.36,.23,-.47)},new V(.055,0,0),"#A58053",Copper);
            for(int i=0;i<3;i++)Panel(m,new V(sign*.52,.38,-.75+i*.36),.12,.045,.04,"#D2AE72",Brass);
        }
        Profile(m,0,0,new[]{-1.98,-1.84,-1.6,-.8},new[]{.26,.36,.36,.25},"#5C4D3C",Vent,16);
        Cylinder(m,0,0,-2.0,.245,.025,"#EAA251",true);Cylinder(m,0,0,-2.02,.14,.02,"#FFF1B2",true);
        return m;
    }
}
