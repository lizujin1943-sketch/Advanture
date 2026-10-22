using System;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Immutable room rules. Distances are millimetres, weights grams, durations 30 Hz ticks.</summary>
public sealed class EnemyConfig
{
    public static EnemyConfig Disabled { get; }=new EnemyConfig(piranhaCount:0,monsterEnabled:false);
    public EnemyConfig(int piranhaCount=3,bool monsterEnabled=true,int biteDamagePerKg=3,int biteIntervalTicks=45,
        int piranhaSpeed=2400,int aggroRange=20000,int biteRange=800,int minWeightGrams=1000,int maxWeightGrams=4000,
        int monsterWeightGrams=50000,int waterballDamage=20,int windupTicks=60,int attackIntervalTicks=180,
        int initialDelayTicks=240,int interruptDelayTicks=90,int waterballSpeed=8000,int waterballRadius=300,
        int waterballRange=24000,int monsterX=0,int monsterZ=-6000,int monsterLevel=1,int baseKnockback=4000,int knockbackPerLevel=2000,
        int piranhaRespawnTicks=300,int piranhaCoinsPerKg=6,int piranhaEscapeMilli=1200,int patrolSpeed=1200)
    {
        if(piranhaCount<0 || piranhaCount>16 || biteDamagePerKg<1 || biteDamagePerKg>1000 || biteIntervalTicks<15 || biteIntervalTicks>1800 ||
            piranhaSpeed<30 || piranhaSpeed>10000 || aggroRange<1000 || aggroRange>40000 || biteRange<100 || biteRange>2000 ||
            minWeightGrams<100 || maxWeightGrams<minWeightGrams || maxWeightGrams>20000 || monsterWeightGrams<20001 || monsterWeightGrams>1000000 ||
            waterballDamage<1 || waterballDamage>100000 || windupTicks<1 || windupTicks>300 || attackIntervalTicks<15 || attackIntervalTicks>1800 ||
            initialDelayTicks<0 || initialDelayTicks>18000 || interruptDelayTicks<1 || interruptDelayTicks>1800 ||
            waterballSpeed<1000 || waterballSpeed>30000 || waterballRadius<50 || waterballRadius>1000 || waterballRange<1000 || waterballRange>40000 ||
            Math.Abs((long)monsterX)>20000 || Math.Abs((long)monsterZ)>20000)throw new InvalidDataException("Invalid enemy configuration.");
        PiranhaCount=piranhaCount;MonsterEnabled=monsterEnabled;BiteDamagePerKg=biteDamagePerKg;BiteIntervalTicks=biteIntervalTicks;
        PiranhaSpeed=piranhaSpeed;AggroRange=aggroRange;BiteRange=biteRange;MinWeightGrams=minWeightGrams;MaxWeightGrams=maxWeightGrams;
        MonsterWeightGrams=monsterWeightGrams;WaterballDamage=waterballDamage;WindupTicks=windupTicks;AttackIntervalTicks=attackIntervalTicks;
        InitialDelayTicks=initialDelayTicks;InterruptDelayTicks=interruptDelayTicks;WaterballSpeed=waterballSpeed;WaterballRadius=waterballRadius;
        WaterballRange=waterballRange;MonsterX=monsterX;MonsterZ=monsterZ;
        if(monsterLevel<1 || monsterLevel>10 || baseKnockback<1000 || baseKnockback>10000 || knockbackPerLevel<1 || knockbackPerLevel>4000 ||
            piranhaRespawnTicks<30 || piranhaRespawnTicks>18000 || piranhaCoinsPerKg<1 || piranhaCoinsPerKg>1000 ||
            piranhaEscapeMilli<100 || piranhaEscapeMilli>10000 || patrolSpeed<30 || patrolSpeed>10000)
            throw new InvalidDataException("Invalid enemy progression/capture configuration.");
        MonsterLevel=monsterLevel;BaseKnockback=baseKnockback;KnockbackPerLevel=knockbackPerLevel;PiranhaRespawnTicks=piranhaRespawnTicks;
        PiranhaCoinsPerKg=piranhaCoinsPerKg;PiranhaEscapeMilli=piranhaEscapeMilli;PatrolSpeed=patrolSpeed;
    }
    public bool Enabled=>PiranhaCount>0 || MonsterEnabled;
    public int PiranhaCount { get; }
    public bool MonsterEnabled { get; }
    public int BiteDamagePerKg { get; }
    public int BiteIntervalTicks { get; }
    public int PiranhaSpeed { get; }
    public int AggroRange { get; }
    public int BiteRange { get; }
    public int MinWeightGrams { get; }
    public int MaxWeightGrams { get; }
    public int MonsterWeightGrams { get; }
    public int WaterballDamage { get; }
    public int WindupTicks { get; }
    public int AttackIntervalTicks { get; }
    public int InitialDelayTicks { get; }
    public int InterruptDelayTicks { get; }
    public int WaterballSpeed { get; }
    public int WaterballRadius { get; }
    public int WaterballRange { get; }
    public int MonsterX { get; }
    public int MonsterZ { get; }
    public int MonsterLevel { get; }
    public int BaseKnockback { get; }
    public int KnockbackPerLevel { get; }
    public int KnockbackDistance=>BaseKnockback+(MonsterLevel-1)*KnockbackPerLevel;
    public int PiranhaRespawnTicks { get; }
    public int PiranhaCoinsPerKg { get; }
    public int PiranhaEscapeMilli { get; }
    public int PatrolSpeed { get; }
    public int BiteDamage(int grams)
    {
        if(grams<MinWeightGrams || grams>MaxWeightGrams)throw new ArgumentOutOfRangeException(nameof(grams));
        return (int)(((long)grams*BiteDamagePerKg+999)/1000);
    }
    internal void Write(BinaryWriter w)
    {
        w.Write(PiranhaCount);w.Write(MonsterEnabled?1:0);w.Write(BiteDamagePerKg);w.Write(BiteIntervalTicks);w.Write(PiranhaSpeed);
        w.Write(AggroRange);w.Write(BiteRange);w.Write(MinWeightGrams);w.Write(MaxWeightGrams);w.Write(MonsterWeightGrams);
        w.Write(WaterballDamage);w.Write(WindupTicks);w.Write(AttackIntervalTicks);w.Write(InitialDelayTicks);w.Write(InterruptDelayTicks);
        w.Write(WaterballSpeed);w.Write(WaterballRadius);w.Write(WaterballRange);w.Write(MonsterX);w.Write(MonsterZ);
        w.Write(MonsterLevel);w.Write(BaseKnockback);w.Write(KnockbackPerLevel);w.Write(PiranhaRespawnTicks);
        w.Write(PiranhaCoinsPerKg);w.Write(PiranhaEscapeMilli);w.Write(PatrolSpeed);
    }
    internal static EnemyConfig Read(BinaryReader r)
    {
        int count=r.ReadInt32(),enabled=r.ReadInt32();if(enabled<0 || enabled>1)throw new InvalidDataException("Invalid monster flag.");
        return new EnemyConfig(count,enabled==1,r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),
            r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),
            r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
    }
}
