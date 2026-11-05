using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>同一份整数轨迹供模拟与本地预览使用；不依赖浮点物理或 Unity 碰撞回调。</summary>
public static class GrenadeRules
{
    public const int MaxActive=128,BodyRadius=70;
    public static int SurfaceHeight(IslandMap map,IReadOnlyList<PlatformTile> tiles,int x,int z)=>
        map.IsTown?(TownLayout.OnGround(tiles,x,z)?450:0):map.Platform!=null?(PlatformRules.OnPlatform(tiles,x,z)?450:0):(map.OnLand(x,z)?450:0);

    /// <summary>固定力度高抛。每逻辑帧细分采样，连同手雷半径检查共享实体障碍。</summary>
    public static GrenadeResult TryPlan(IslandMap map,IReadOnlyList<PlatformTile> tiles,int startX,int startZ,int targetX,int targetZ,out GrenadeState plan)
    {
        plan=default;var c=GrenadeConfig.Default;
        if(!map.InWorldBounds(targetX,targetZ))return GrenadeResult.OutOfRange;
        long distanceSquared=IslandMap.DistanceSquared(startX,startZ,targetX,targetZ);
        if(distanceSquared<(long)c.MinRange*c.MinRange || distanceSquared>(long)c.MaxRange*c.MaxRange)return GrenadeResult.OutOfRange;
        int distance=HookGame.Sqrt(distanceSquared),fraction=(distance-c.MinRange)*1000/(c.MaxRange-c.MinRange);
        int floor=SurfaceHeight(map,tiles,targetX,targetZ);
        plan=new GrenadeState{StartX=startX,StartZ=startZ,StartY=SurfaceHeight(map,tiles,startX,startZ)+1000,
            TargetX=targetX,TargetZ=targetZ,TargetY=floor+BodyRadius,FlightTicks=c.MinFlightTicks+(c.MaxFlightTicks-c.MinFlightTicks)*fraction/1000,
            ArcHeight=c.MinArc+(c.MaxArc-c.MinArc)*fraction/1000,Water=floor==0};
        int samples=plan.FlightTicks*8;
        for(int i=0;i<=samples;i++)
        {
            Position(plan,i,samples,out int x,out int y,out int z);
            if(Solid(map,x,y,z,BodyRadius))return GrenadeResult.Blocked;
            if(i>0 && i<samples && y<SurfaceHeight(map,tiles,x,z)+BodyRadius)return GrenadeResult.Blocked;
        }
        return GrenadeResult.Success;
    }
    public static void Position(GrenadeState plan,int age,int duration,out int x,out int y,out int z)
    {
        age=Math.Max(0,Math.Min(duration,age));
        x=plan.StartX+(int)((long)(plan.TargetX-plan.StartX)*age/duration);
        z=plan.StartZ+(int)((long)(plan.TargetZ-plan.StartZ)*age/duration);
        y=plan.StartY+(int)((long)(plan.TargetY-plan.StartY)*age/duration)+
            (int)(4L*plan.ArcHeight*age*(duration-age)/((long)duration*duration));
    }
    /// <summary>地图现有实体的共享高度：商人台 2 米，初始岛的大岩体 7 米、小岩体 3 米。</summary>
    public static bool Solid(IslandMap map,int x,int y,int z,int margin=0)
    {
        if(map.IsTown)return System.Linq.Enumerable.Any(TownLayout.Obstacles,o=>y>=-margin && y<=o.Height+margin && o.Contains(x,z,margin));
        if(map.Platform==null && (y<=7000+margin && y>=-margin && IslandMap.DistanceSquared(x,z,0,1050)<(long)(2050+margin)*(2050+margin) ||
            y<=3000+margin && y>=-margin && IslandMap.DistanceSquared(x,z,-2150,1550)<(long)(390+margin)*(390+margin)))return true;
        if(map.Fishing!=null && y<=2000+margin && y>=-margin)
            for(int i=0;i<=map.Fishing.Merchants.Definitions.Count;i++)
            {var p=MerchantRules.Position(map.Fishing,i);int half=MerchantRules.BodyHalfSize+margin;if(Math.Abs((long)x-p.X)<=half && Math.Abs((long)z-p.Z)<=half)return true;}
        return false;
    }
    /// <summary>实体顶面也能承接飞行中被挡停、竖直下落的手雷。</summary>
    public static int LandingHeight(IslandMap map,IReadOnlyList<PlatformTile> tiles,int x,int z)
    {
        int height=SurfaceHeight(map,tiles,x,z);
        if(map.IsTown){foreach(var o in TownLayout.Obstacles)if(o.Contains(x,z))height=Math.Max(height,o.Height);return height;}
        if(map.Platform==null)
        {if(IslandMap.DistanceSquared(x,z,0,1050)<2050L*2050)height=Math.Max(height,7000);if(IslandMap.DistanceSquared(x,z,-2150,1550)<390L*390)height=Math.Max(height,3000);}
        if(map.Fishing!=null)for(int i=0;i<=map.Fishing.Merchants.Definitions.Count;i++)
        {var p=MerchantRules.Position(map.Fishing,i);if(Math.Abs((long)x-p.X)<=MerchantRules.BodyHalfSize && Math.Abs((long)z-p.Z)<=MerchantRules.BodyHalfSize)height=Math.Max(height,2000);}
        return height;
    }
    public static bool Visible(IslandMap map,int ax,int ay,int az,int bx,int by,int bz)
    {
        int steps=Math.Max(1,HookGame.Sqrt(IslandMap.DistanceSquared(ax,az,bx,bz))/50);
        for(int i=1;i<steps;i++)if(Solid(map,ax+(int)((long)(bx-ax)*i/steps),ay+(int)((long)(by-ay)*i/steps),az+(int)((long)(bz-az)*i/steps)))return false;
        return true;
    }
    public static int DamageAt(int distance)
    {
        var c=GrenadeConfig.Default;if(distance>c.Radius)return 0;if(distance<=c.FullDamageRadius)return c.Damage;
        return c.Damage-(c.Damage-c.EdgeDamage)*(distance-c.FullDamageRadius)/(c.Radius-c.FullDamageRadius);
    }
    /// <summary>无平台随机状态：世界种子、玩家与成功序号决定圆盘内偏移，回滚不会重新抽签。</summary>
    public static Point Scatter(int seed,int owner,uint sequence)
    {
        uint value=unchecked((uint)seed^((uint)owner+1)*0x9e3779b9u^sequence*0x85ebca6bu);
        uint Next(){value^=value<<13;value^=value>>17;value^=value<<5;return value;}
        int r=GrenadeConfig.Default.Scatter;
        for(int i=0;i<32;i++){int x=(int)(Next()%(uint)(2*r+1))-r,z=(int)(Next()%(uint)(2*r+1))-r;if((long)x*x+(long)z*z<=(long)r*r)return new Point(x,z);}
        return default;
    }
}
