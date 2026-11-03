using System;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Shoreline attackers. Times are simulation ticks, distances integer millimetres.</summary>
public sealed class CrabConfig
{
    public static CrabConfig Disabled { get; } = new CrabConfig(false);
    public CrabConfig(bool enabled=true,int initialDelayTicks=2700,int respawnTicks=1800,int health=100,
        int speed=2000,int windupTicks=18,int recoveryTicks=30,int damage=12,int knockback=500,
        int climbTicks=45,int idleTicks=90,int interruptProtectionTicks=90,int count=1,int spawnStaggerTicks=90)
    {
        if(initialDelayTicks<1 || initialDelayTicks>108000 || respawnTicks<30 || respawnTicks>108000 ||
            health<1 || health>10000 || speed<100 || speed>6000 || windupTicks<1 || windupTicks>300 ||
            recoveryTicks<1 || recoveryTicks>300 || damage<1 || damage>1000 || knockback<1 || knockback>46000 ||
            climbTicks<1 || climbTicks>300 || idleTicks<1 || idleTicks>1800 || interruptProtectionTicks<1 || interruptProtectionTicks>1800 ||
            count<1 || count>8 || spawnStaggerTicks<0 || spawnStaggerTicks>1800)
            throw new InvalidDataException("Invalid crab configuration.");
        Enabled=enabled;InitialDelayTicks=initialDelayTicks;RespawnTicks=respawnTicks;Health=health;Speed=speed;
        WindupTicks=windupTicks;RecoveryTicks=recoveryTicks;Damage=damage;Knockback=knockback;
        ClimbTicks=climbTicks;IdleTicks=idleTicks;InterruptProtectionTicks=interruptProtectionTicks;
        Count=count;SpawnStaggerTicks=spawnStaggerTicks;
    }
    public bool Enabled {get;}
    public int InitialDelayTicks {get;} public int RespawnTicks {get;} public int Health {get;}
    public int Speed {get;} public int WindupTicks {get;} public int RecoveryTicks {get;}
    public int Damage {get;} public int Knockback {get;} public int ClimbTicks {get;}
    public int IdleTicks {get;} public int InterruptProtectionTicks {get;}
    public int Count {get;} public int SpawnStaggerTicks {get;}
    public const int AggroRange=12000,AttackDistance=1200,AttackRange=1400,PinchTicks=6,BodyRadius=400;
    // round(100 / n^0.6), fixed for the supported 1..4 participants. No platform pow/round in Step.
    public static int CoinsPerAttacker(int count)=>count switch {1=>100,2=>66,3=>52,4=>44,_=>throw new ArgumentOutOfRangeException(nameof(count))};
    public const int BonesPerAttacker=2;
    internal void Write(BinaryWriter w)
    {w.Write(Enabled);w.Write(InitialDelayTicks);w.Write(RespawnTicks);w.Write(Health);w.Write(Speed);w.Write(WindupTicks);w.Write(RecoveryTicks);w.Write(Damage);w.Write(Knockback);w.Write(ClimbTicks);w.Write(IdleTicks);w.Write(InterruptProtectionTicks);w.Write(Count);w.Write(SpawnStaggerTicks);}
    internal static CrabConfig Read(BinaryReader r)
    {byte flag=r.ReadByte();if(flag>1)throw new InvalidDataException("Invalid crab flag.");return new CrabConfig(flag==1,r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());}
}
