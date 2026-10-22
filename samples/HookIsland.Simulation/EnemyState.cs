using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public enum MonsterPhase : byte { Cooldown, Windup }
public struct Piranha
{
    public CatchEscapeState Escape;
    public int Id,X,Z,WeightGrams,Target,Cooldown,TravelRemainder,MovementRemainderX,MovementRemainderZ;
    public int Owner,RespawnTicks,PatrolPoint;
}
public struct WaterMonster
{
    public MonsterPhase Phase;
    public int Timer,Target,DirectionX,DirectionZ,X,Z,Level,DiveTicks;
    public uint Cycle; // Surface attempts, including blocked retries.
}
public struct Waterball
{
    public uint Id;
    public int X,Z,OriginX,OriginZ,DirectionX,DirectionZ,Distance,TravelRemainder;
    public int KnockbackDistance;
}
public struct KnockbackState
{
    public int OriginX,OriginZ,DirectionX,DirectionZ,Distance,TravelRemainder;
    public int TotalDistance;
    public bool Active;
}
public readonly struct EnemyHit
{
    public EnemyHit(int slot,int damage,bool knockback=false,int directionX=0,int directionZ=0,int knockbackDistance=4000)
    {Slot=slot;Damage=damage;Knockback=knockback;DirectionX=directionX;DirectionZ=directionZ;KnockbackDistance=knockbackDistance;}
    public int Slot { get; }
    public int Damage { get; }
    public bool Knockback { get; }
    public int DirectionX { get; }
    public int DirectionZ { get; }
    public int KnockbackDistance { get; }
}
public sealed class EnemyState
{
    public static EnemyState Empty { get; }=new EnemyState(Array.Empty<Piranha>(),default,Array.Empty<Waterball>());
    public EnemyState(IEnumerable<Piranha> piranhas,WaterMonster monster,IEnumerable<Waterball> waterballs,
        uint serial=0,int bites=0,int shots=0,int interruptions=0,int waterHits=0,int captures=0)
    {
        Piranhas=Array.AsReadOnly(piranhas.ToArray());Monster=monster;Waterballs=Array.AsReadOnly(waterballs.ToArray());
        Serial=serial;Bites=bites;Shots=shots;Interruptions=interruptions;WaterHits=waterHits;Captures=captures;
    }
    public IReadOnlyList<Piranha> Piranhas { get; }
    public WaterMonster Monster { get; }
    public IReadOnlyList<Waterball> Waterballs { get; }
    public uint Serial { get; }
    public int Bites { get; }
    public int Shots { get; }
    public int Interruptions { get; }
    public int WaterHits { get; }
    public int Captures { get; }
}
