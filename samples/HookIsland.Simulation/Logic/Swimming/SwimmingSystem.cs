using System;

namespace Advanture.HookIsland.Simulation;

/// <summary>Water is independent of health/downed state; residuals are authoritative checkpoint state.</summary>
public struct SwimmingState
{
    public bool InWater,Active;
    public int DamageRemainder,DamageTicks;
    public long MoveRemainderX,MoveRemainderZ;
}

/// <summary>Resource budgeting and movement math only; terrain, hook targets and equipment transactions stay outside.</summary>
public static class SwimmingSystem
{
    public const int FullFrame=1000000;
    public const int DamageDenominator=10000,DamageIntervalTicks=HookGame.Rate;
    public const long MovementDenominator=1000L*HookGame.Rate*FullFrame;
    public static int Speed(SwimmingConfig config,SwimRingDefinition? ring)=>config.Speed*(ring?.SpeedPermille??1000)/1000;
    public static int DrainPerTick(SwimmingConfig config,SwimRingDefinition? ring)=>
        Math.Max(1,(int)((long)config.StaminaMilliPerSecond*(ring?.StaminaPermille??1000)/1000));
    public static bool CanCast(bool inWater,bool downed,bool hasRing)=>!downed && (!inWater || hasRing);
    public static bool Active(bool inWater,bool downed,bool pulled,int x,int z)=>inWater && !downed && !pulled && (x!=0 || z!=0);
    /// <returns>Proportion of the current frame supported by the shared budget, in millionths.</returns>
    public static int Spend(ref int stamina,int swimCost,int fishCost)
    {
        if(stamina<0 || swimCost<0 || fishCost<0)throw new ArgumentOutOfRangeException(nameof(stamina));
        long total=(long)swimCost+fishCost;
        int fraction=total==0?FullFrame:(int)Math.Min(FullFrame,(long)stamina*FullFrame/total);
        stamina=(int)Math.Max(0,stamina-total);return fraction;
    }
    public static int Delta(int direction,int speed,int fraction,ref long remainder)
    {
        if(direction< -1000 || direction>1000 || speed<0 || speed>30000 || fraction<0 || fraction>FullFrame ||
            remainder<=-MovementDenominator || remainder>=MovementDenominator)throw new ArgumentOutOfRangeException(nameof(direction));
        long total=(long)direction*speed*fraction+remainder;
        remainder=total%MovementDenominator;return (int)(total/MovementDenominator);
    }
    public static void Damage(ref SwimmingState water,ref CharacterVitals vitals,SwimmingConfig config,bool hasRing,int healthMax)
    {
        if(healthMax<1 || healthMax>CharacterConfig.MaximumPoints)throw new ArgumentOutOfRangeException(nameof(healthMax));
        if(!water.InWater || hasRing || vitals.Downed){water.DamageTicks=0;return;}
        if(++water.DamageTicks<DamageIntervalTicks)return;
        water.DamageTicks=0;
        // One whole-second pulse; retain sub-HP fractions instead of over-damaging low/odd HP maxima.
        long amount=water.DamageRemainder+(long)healthMax*config.HealthLossBasisPointsPerSecond;
        water.DamageRemainder=(int)(amount%DamageDenominator);
        CharacterSystem.Damage(ref vitals,(int)(amount/DamageDenominator),DamageKind.Direct,0);
    }
}
