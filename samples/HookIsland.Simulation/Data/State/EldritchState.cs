using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public enum EldritchPhase : byte { Dormant, Emerging, Hunting, Windup, Active, Recovering, Dead }
public struct EldritchCreature
{
    public int Id;
    public EldritchKind Kind;
    public EldritchPhase Phase;
    public uint Generation,AttackSerial;
    public int X,Z,DirectionX,DirectionZ,Health,Timer,Target,EffectX,EffectZ,TravelRemainder;
    public int GoalX,GoalZ,RouteTicks,NoTargetTicks,Summons,RewardOwner,Ally;
    public byte HitMask;
    public bool Rewarded,TownAdult,Paused;
    public bool IsMinion=>!TownAdult && Id>=EldritchCatalog.SpeciesCount;
    public bool Visible=>!Paused && Phase!=EldritchPhase.Dormant;
    public bool Alive=>Phase!=EldritchPhase.Dormant && Phase!=EldritchPhase.Dead;
    public bool Damageable=>!Paused && Alive;
}
public sealed class EldritchState
{
    public static EldritchState Empty {get;}=new EldritchState(Array.Empty<EldritchCreature>());
    public EldritchState(IEnumerable<EldritchCreature> creatures,int nextSpawn=0,TownDirectorState director=default)
    {Creatures=Array.AsReadOnly(creatures.ToArray());NextSpawn=nextSpawn;Director=director;}
    public IReadOnlyList<EldritchCreature> Creatures {get;}
    public int NextSpawn {get;}
    public TownDirectorState Director {get;}
}
