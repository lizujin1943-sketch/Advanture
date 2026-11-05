using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

public readonly struct BossPoint3
{
    public BossPoint3(int x,int y,int z){X=x;Y=y;Z=z;}
    public int X {get;} public int Y {get;} public int Z {get;}
}
public readonly struct BossProxy
{
    public BossProxy(int limbId,int x,int y,int z,int radiusX,int radiusY,int radiusZ,bool eye=false)
    {LimbId=limbId;X=x;Y=y;Z=z;RadiusX=radiusX;RadiusY=radiusY;RadiusZ=radiusZ;Eye=eye;}
    public int LimbId {get;} public int X {get;} public int Y {get;} public int Z {get;}
    public int RadiusX {get;} public int RadiusY {get;} public int RadiusZ {get;} public bool Eye {get;}
}
/// <summary>权威三维射线和固定朝向警示区域；只使用整数，不查询 Unity 碰撞体。</summary>
public static class BossGeometry
{
    private static readonly Point[] Directions={new Point(-906,-423),new Point(-407,-914),new Point(407,-914),new Point(906,-423)};
    private static readonly Point[] TownDirections={new Point(-866,-500),new Point(-342,-940),new Point(342,-940),new Point(866,-500)};
    private static readonly Point[] InkDirections={new Point(-643,-766),new Point(0,-1000),new Point(643,-766)};
    public static Point AttackDirection(int id,bool town=false)=>(town?TownDirections:Directions)[id>=0 && id<4?id:throw new ArgumentOutOfRangeException(nameof(id))];
    public static bool SlamContainsXZ(int id,int x,int z,int margin=0,bool town=false,Point? direction=null)
    {
        var d=direction??AttackDirection(id,town);long rx=x-(town?TownLayout.CenterX:BossConfig.CenterX),rz=z-(town?TownLayout.CenterZ:BossConfig.CenterZ);
        long along=(rx*d.X+rz*d.Z)/1000,across=(rx*d.Z-rz*d.X)/1000;
        return along>=(town?8500:12000)-margin && along<=(town?22000:20000)+margin && Math.Abs(across)<=1000+margin;
    }
    public static bool SweepContainsXZ(int id,int attackTicks,int x,int z,int margin=0,bool town=false,Point? direction=null)
    {
        if(attackTicks<BossConfig.SweepStartTicks || attackTicks>BossConfig.SweepEndTicks)return false;
        var d=direction??AttackDirection(id,town);long rx=x-(town?TownLayout.CenterX:BossConfig.CenterX),rz=z-(town?TownLayout.CenterZ:BossConfig.CenterZ);
        long along=(rx*d.X+rz*d.Z)/1000,across=(rx*d.Z-rz*d.X)/1000;
        // 横向扫掠在固定扇道中经过玩家；命中位图保证一轮只击退一次。
        int center=-5000+(attackTicks-BossConfig.SweepStartTicks)*10000/(BossConfig.SweepEndTicks-BossConfig.SweepStartTicks);
        long radius=rx*rx+rz*rz;
        return town?along>0 && radius>=(12000L-margin)*(12000-margin) && radius<=(19500L+margin)*(19500+margin) && Math.Abs(across-center)<=1600+margin:
            along>=11000-margin && along<=20000+margin && Math.Abs(across-center)<=1600+margin;
    }
    public static bool InkContainsXZ(int x,int z,int margin=0)
    {
        long dx=x-BossConfig.CenterX,dz=BossConfig.CenterZ-z;
        return dz>=3000-margin && dz<=22000+margin && Math.Abs(dx)<=dz*7/10+margin && dx*dx+dz*dz<=(22000L+margin)*(22000+margin);
    }
    public static Point InkDirection(BossState state)=>state.TownArena?InkDirections[state.InkSector]:new Point(0,-1000);
    public static bool InkContainsXZ(BossState state,int x,int z,int margin=0)
    {
        if(!state.TownArena)return InkContainsXZ(x,z,margin);
        var d=InkDirection(state);long rx=x-TownLayout.CenterX,rz=z-TownLayout.CenterZ;
        long along=(rx*d.X+rz*d.Z)/1000,across=(rx*d.Z-rz*d.X)/1000,r2=rx*rx+rz*rz;
        return along>=8500-margin && r2<=(19500L+margin)*(19500+margin) && Math.Abs(across)<=along*414/1000+margin;
    }
    public static Point AttackDirection(BossState state,int id)
    {
        var l=state.Limbs[id];return state.TownArena && (l.AttackX!=0 || l.AttackZ!=0)?new Point(l.AttackX,l.AttackZ):AttackDirection(id,state.TownArena);
    }
    public static bool SlamContainsXZ(BossState state,int id,int x,int z,int margin=0)=>SlamContainsXZ(id,x,z,margin,state.TownArena,AttackDirection(state,id));
    public static bool SweepContainsXZ(BossState state,int id,int attackTicks,int x,int z,int margin=0)=>SweepContainsXZ(id,attackTicks,x,z,margin,state.TownArena,AttackDirection(state,id));
    public static int MuzzleHeight(Fisher p)=>p.Swimming.InWater?350:1480;
    public static void Direction3(int horizontalX,int horizontalZ,short vertical,out int dx,out int dy,out int dz)
    {
        dy=vertical*1000;int horizontal=HookGame.Sqrt(1000000000000L-(long)dy*dy);
        dx=(int)((long)horizontalX*horizontal/EnemyGeometry.DirectionScale);dz=(int)((long)horizontalZ*horizontal/EnemyGeometry.DirectionScale);
    }
    // 凸椭球的隐式函数沿射线是凸函数。先求最近点，再二分首次接触，避免浮点和大整数判别式溢出。
    public static int RayEllipsoid(int ox,int oy,int oz,int dx,int dy,int dz,BossProxy proxy,int range)
    {
        long Evaluate(int distance)
        {
            long x=((long)ox+(long)dx*distance/1000000-proxy.X)*10000/proxy.RadiusX;
            long y=((long)oy+(long)dy*distance/1000000-proxy.Y)*10000/proxy.RadiusY;
            long z=((long)oz+(long)dz*distance/1000000-proxy.Z)*10000/proxy.RadiusZ;
            return x*x+y*y+z*z;
        }
        if(Evaluate(0)<=100000000)return 0;
        int low=0,high=range;
        while(high-low>4)
        {int a=low+(high-low)/3,b=high-(high-low)/3;if(Evaluate(a)<=Evaluate(b))high=b;else low=a;}
        int closest=low;long best=Evaluate(low);
        for(int i=low+1;i<=high;i++){long value=Evaluate(i);if(value<best){best=value;closest=i;}}
        if(best>100000000)return int.MaxValue;
        low=0;high=closest;
        while(low<high){int mid=low+(high-low)/2;if(Evaluate(mid)<=100000000)high=mid;else low=mid+1;}
        return low;
    }
    /// <summary>用于血条、调试绘制和近战查询的关节代理；枪械额外连接相邻关节为胶囊。</summary>
    public static IReadOnlyList<BossProxy> Proxies(BossState state)
    {
        var list=new List<BossProxy>{BossPoseData.Head(state,false),BossPoseData.Head(state,true)};
        foreach(var limb in state.Limbs)
            if(limb.Health>0)for(int joint=0;joint<BossPoseData.JointCount(limb.Id);joint++)list.Add(BossPoseData.Joint(state,limb,joint));
        return list;
    }
    private static int RayCapsule(int ox,int oy,int oz,int dx,int dy,int dz,BossProxy a,BossProxy b,int range)
    {
        long vx=b.X-a.X,vy=b.Y-a.Y,vz=b.Z-a.Z,length=vx*vx+vy*vy+vz*vz;
        long radius=Math.Max(a.RadiusX,b.RadiusX),limit=radius*radius;
        long Evaluate(int distance)
        {
            long x=(long)ox+(long)dx*distance/1000000-a.X,y=(long)oy+(long)dy*distance/1000000-a.Y,z=(long)oz+(long)dz*distance/1000000-a.Z;
            long dot=length==0?0:Math.Max(0,Math.Min(length,x*vx+y*vy+z*vz));
            if(length!=0){x-=vx*dot/length;y-=vy*dot/length;z-=vz*dot/length;}return x*x+y*y+z*z;
        }
        if(Evaluate(0)<=limit)return 0;
        int low=0,high=range;
        while(high-low>4){int a0=low+(high-low)/3,b0=high-(high-low)/3;if(Evaluate(a0)<=Evaluate(b0))high=b0;else low=a0;}
        int closest=low;long best=Evaluate(low);for(int t=low+1;t<=high;t++){long d=Evaluate(t);if(d<best){best=d;closest=t;}}
        if(best>limit)return int.MaxValue;low=0;high=closest;
        while(low<high){int middle=low+(high-low)/2;if(Evaluate(middle)<=limit)high=middle;else low=middle+1;}return low;
    }
    public static int RayHit(BossState state,int ox,int oy,int oz,int dx,int dy,int dz,int range,out int limbId,out bool eye)
    {
        int best=int.MaxValue;limbId=-1;eye=false;if(!state.Damageable)return best;
        var head=BossPoseData.Head(state,false);best=RayEllipsoid(ox,oy,oz,dx,dy,dz,head,range);
        int eyeEntry=RayEllipsoid(ox,oy,oz,dx,dy,dz,BossPoseData.Head(state,true),range);
        if(eyeEntry<=best && eyeEntry!=int.MaxValue){best=eyeEntry;eye=true;}
        foreach(var limb in state.Limbs)
        {
            if(limb.Health==0)continue;
            var previous=BossPoseData.Joint(state,limb,0);
            for(int joint=1;joint<BossPoseData.JointCount(limb.Id);joint++)
            {
                var current=BossPoseData.Joint(state,limb,joint);int entry=RayCapsule(ox,oy,oz,dx,dy,dz,previous,current,Math.Min(range,best));
                if(entry<best){best=entry;limbId=limb.Id;eye=false;}previous=current;
            }
        }
        return best;
    }
}
