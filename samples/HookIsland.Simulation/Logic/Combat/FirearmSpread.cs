using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Millidegree spread driven by accepted voluntary speed, with a separate deterministic shot random domain.</summary>
public static class FirearmSpread
{
    public static int Target(int speed)=>Target(speed,FirearmCatalog.Get(0).Spread);
    public static int Target(int speed,FirearmSpreadDefinition definition)
    {
        speed=Math.Max(0,speed);
        var nodes=definition.MovementSpreadCurve;
        if(speed<=definition.StationarySpeedThreshold)return nodes[0].HalfAngle;
        for(int i=1;i<nodes.Count;i++)if(speed<=nodes[i].Speed)
        {
            var a=nodes[i-1];var b=nodes[i];
            return a.HalfAngle+(int)((long)(speed-a.Speed)*(b.HalfAngle-a.HalfAngle)/(b.Speed-a.Speed));
        }
        return definition.MaximumConfiguredHalfAngle;
    }
    public static FirearmSpreadState Advance(FirearmSpreadState state,int speed,FirearmSpreadDefinition definition)
    {
        int target=Target(speed,definition),angle=state.Angle,remainder=state.Remainder;
        // Positive remainder belongs to recovery, negative to widening. Reversing direction
        // discards the old fraction so one profile's rate never biases the opposite rate.
        if(angle<target)
        {
            int total=definition.IncreasePerSecond+Math.Max(0,-remainder);
            angle=Math.Min(target,angle+total/HookGame.Rate);remainder=angle==target?0:-(total%HookGame.Rate);
        }
        else if(angle>target)
        {
            int total=definition.RecoveryPerSecond+Math.Max(0,remainder);
            angle=Math.Max(target,angle-total/HookGame.Rate);remainder=angle==target?0:total%HookGame.Rate;
        }
        else remainder=0;
        return new FirearmSpreadState(state.Type,angle,remainder);
    }
    public static void Advance(ref FirearmState state,int speed,IEnumerable<int> ownedTypes)
    {
        state.MoveSpeed=Math.Max(0,speed);
        var records=state.Spreads.ToDictionary(s=>s.Type);
        // Retain learned spread even after transferring a gun: reacquiring cannot reset accuracy.
        foreach(int type in ownedTypes.Distinct())if(!records.ContainsKey(type))records.Add(type,new FirearmSpreadState(type,Target(state.MoveSpeed,FirearmCatalog.Get(type).Spread)));
        var result=new List<FirearmSpreadState>();
        foreach(var s in records.Values.OrderBy(s=>s.Type))
            result.Add(Advance(s,state.MoveSpeed,FirearmCatalog.Get(s.Type).Spread));
        state.SetSpreads(result);
    }
    public static int Sample(int seed,int player,uint sequence,int weaponId,int halfAngle,FirearmSpreadSampling sampling=FirearmSpreadSampling.Triangular)
    {
        if(halfAngle<0 || halfAngle>FirearmSpreadDefinition.MaximumHalfAngle || sampling!=FirearmSpreadSampling.Triangular)throw new ArgumentOutOfRangeException(nameof(halfAngle),"Unsupported spread angle or sampling type.");
        uint h=Mix(unchecked((uint)seed)^0xD35E4711u);h=Mix(h^unchecked((uint)player));h=Mix(h^sequence);h=Mix(h^unchecked((uint)weaponId));
        int a=(int)(h%2000001)-1000000,b=(int)(Mix(h^0xA341316Cu)%2000001)-1000000;
        return (int)((long)halfAngle*(a+b)/2000000);
    }
    private static uint Mix(uint x){unchecked{x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;return x^(x>>16);}}
    public static void Direction(int aimX,int aimZ,int angle,out int x,out int z)
    {
        if(angle< -FirearmSpreadDefinition.MaximumHalfAngle || angle>FirearmSpreadDefinition.MaximumHalfAngle)throw new ArgumentOutOfRangeException(nameof(angle));
        EnemyGeometry.Direction(aimX,aimZ,out int ax,out int az);
        // |angle| <= 10 degrees: fixed-point fifth-order sine/fourth-order cosine, no platform math library.
        long r=(long)angle*3141593/180000,r2=r*r/1000000,r3=r2*r/1000000,r4=r2*r2/1000000,r5=r4*r/1000000;
        long sin=r-r3/6+r5/120,cos=1000000-r2/2+r4/24;
        int dx=(int)((ax*cos-az*sin)/1000000),dz=(int)((ax*sin+az*cos)/1000000);
        EnemyGeometry.Direction(dx,dz,out x,out z);
    }
}
