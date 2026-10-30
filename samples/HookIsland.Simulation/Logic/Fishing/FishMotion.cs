using System;

namespace Advanture.HookIsland.Simulation;

/// <summary>A deterministic patrol along a short offshore lane. All lengths are millimetres.</summary>
public static class FishMotion
{
    public const int SwimTicks=75,JumpTicks=48,DiveTicks=38,HalfLane=3600;
    public static int Tier(int id)=>id%3;
    public static Fish Create(int id,Point home,int delay)
    {
        var fish=new Fish{Id=id,Owner=-1,Phase=FishPhase.Waiting,Timer=delay,Heading=id%2==0?1:-1};
        Place(ref fish,home);return fish;
    }
    public static Point Direction(Fish fish,Point home)
    {
        int length=HookGame.Sqrt((long)home.X*home.X+(long)home.Z*home.Z);
        return length==0?new Point(1000*fish.Heading,0):
            new Point(-home.Z*1000/length*fish.Heading,home.X*1000/length*fish.Heading);
    }
    public static void Advance(ref Fish fish,Point home)
    {
        if(fish.Phase==FishPhase.Hooked || fish.Phase==FishPhase.Escaping)return;
        if(fish.Phase==FishPhase.Waiting)
        {
            if(fish.Timer>0)fish.Timer--;
            else {fish.Phase=FishPhase.Swimming;fish.Timer=0;}
        }
        else
        {
            fish.Timer++;
            if(fish.Phase==FishPhase.Swimming && fish.Timer>=SwimTicks){fish.Phase=FishPhase.Jumping;fish.Timer=0;}
            else if(fish.Phase==FishPhase.Jumping && fish.Timer>=JumpTicks){fish.Phase=FishPhase.Diving;fish.Timer=0;}
            else if(fish.Phase==FishPhase.Diving && fish.Timer>=DiveTicks)
            {fish.Phase=FishPhase.Swimming;fish.Timer=0;fish.Heading=-fish.Heading;}
        }
        Place(ref fish,home);
    }
    private static void Place(ref Fish fish,Point home)
    {
        int along=-HalfLane;fish.Y=-700;
        if(fish.Phase==FishPhase.Swimming)
        {along+=2400*fish.Timer/SwimTicks;fish.Y=-700+550*fish.Timer/SwimTicks;}
        else if(fish.Phase==FishPhase.Jumping)
        {along=-1200+3600*fish.Timer/JumpTicks;fish.Y=4*1900*fish.Timer*(JumpTicks-fish.Timer)/(JumpTicks*JumpTicks);}
        else if(fish.Phase==FishPhase.Diving)
        {along=2400+1200*fish.Timer/DiveTicks;fish.Y=-700*fish.Timer/DiveTicks;}
        var direction=Direction(fish,home);
        fish.X=home.X+direction.X*along/1000;fish.Z=home.Z+direction.Z*along/1000;
    }
    /// <summary>Read-only trajectory preview for diagnostics and automated input drivers; never aims a human player's hook.</summary>
    public static Fish Predict(Fish fish,Point home,int frames)
    {
        if(frames<0 || frames>300)throw new ArgumentOutOfRangeException(nameof(frames));
        for(int i=0;i<frames;i++)Advance(ref fish,home);
        return fish;
    }
}
