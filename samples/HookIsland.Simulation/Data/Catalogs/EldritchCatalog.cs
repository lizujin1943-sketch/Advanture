using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

public enum EldritchKind : byte { DeepOne, ReefRam, ShellWarden, RiftEye, DrownedBell, BlightSac, ChoirPriest, BroodMother, RiftStalker, StarHerald }
public enum EldritchHabitat : byte { Ground, Water, Hover }
public enum EldritchAbility : byte { Claw, Charge, Slam, Beam, Pull, Miasma, Mend, Brood, Blink, Ring }

/// <summary>Shared by simulation and view. Integer millimetres and 30 Hz ticks, no Unity dependencies.</summary>
public sealed class EldritchDefinition
{
    internal EldritchDefinition(EldritchKind kind,string name,EldritchHabitat habitat,EldritchAbility ability,
        int health,int speed,int range,int radius,int windup,int active,int recovery,int damage,int coins,string counter)
    {Kind=kind;Name=name;Habitat=habitat;Ability=ability;Health=health;Speed=speed;Range=range;Radius=radius;
        WindupTicks=windup;ActiveTicks=active;RecoveryTicks=recovery;Damage=damage;Coins=coins;Counter=counter;}
    public EldritchKind Kind {get;} public string Name {get;} public string Counter {get;}
    public EldritchHabitat Habitat {get;} public EldritchAbility Ability {get;}
    public int Health {get;} public int Speed {get;} public int Range {get;} public int Radius {get;}
    public int WindupTicks {get;} public int ActiveTicks {get;} public int RecoveryTicks {get;}
    public int Damage {get;} public int Coins {get;}
    public bool Interruptible=>Ability==EldritchAbility.Beam || Ability==EldritchAbility.Mend || Ability==EldritchAbility.Brood;
}

public static class EldritchCatalog
{
    public const int SpeciesCount=10,MinionSlots=4,Population=SpeciesCount+MinionSlots;
    public const int BodyRadius=400,EmergenceTicks=45,AggroRange=14000,PullDistance=900,ChargeSpeed=9000;
    public const int RingInnerRadius=1600,HealAmount=24,MaxBroodPerGeneration=4;
    // Bump alongside rulesVersion when rules/definitions change; included in room/save compatibility.
    public const string Revision="eldritch-bestiary-island-1";
    public static IReadOnlyList<EldritchDefinition> All {get;}=Array.AsReadOnly(new[]{
        new EldritchDefinition(EldritchKind.DeepOne,"潮痕眷属",EldritchHabitat.Ground,EldritchAbility.Claw,65,2500,1250,1500,24,1,36,10,18,"后退诱空，再反击"),
        new EldritchDefinition(EldritchKind.ReefRam,"逆棘奔兽",EldritchHabitat.Ground,EldritchAbility.Charge,110,1600,6000,600,42,20,72,20,30,"侧移躲冲锋，追打硬直"),
        new EldritchDefinition(EldritchKind.ShellWarden,"圣螺守卫",EldritchHabitat.Ground,EldritchAbility.Slam,180,1100,1500,1900,48,1,75,22,42,"绕背，或趁重砸恢复攻击"),
        new EldritchDefinition(EldritchKind.RiftEye,"裂瞳观测者",EldritchHabitat.Hover,EldritchAbility.Beam,75,1300,10000,400,54,1,84,18,28,"离开光束预警线，攻击可打断"),
        new EldritchDefinition(EldritchKind.DrownedBell,"溺钟引路者",EldritchHabitat.Water,EldritchAbility.Pull,120,900,4800,5000,60,1,105,8,32,"远离鸣钟范围，防止被拉下水"),
        new EldritchDefinition(EldritchKind.BlightSac,"腐潮囊",EldritchHabitat.Ground,EldritchAbility.Miasma,90,1000,8500,1800,48,120,90,6,26,"离开落点，击杀可清除毒区"),
        new EldritchDefinition(EldritchKind.ChoirPriest,"无面唱诗者",EldritchHabitat.Hover,EldritchAbility.Mend,80,1100,6500,6500,60,1,120,0,36,"优先击杀，打断治疗"),
        new EldritchDefinition(EldritchKind.BroodMother,"千嗣母巢",EldritchHabitat.Ground,EldritchAbility.Brood,160,700,10000,5500,75,1,150,0,48,"打断孵化，尽早消灭母体"),
        new EldritchDefinition(EldritchKind.RiftStalker,"折影行者",EldritchHabitat.Hover,EldritchAbility.Blink,70,1700,7000,1500,42,1,75,16,30,"离开闪现落点，再反击"),
        new EldritchDefinition(EldritchKind.StarHerald,"星骸执钟者",EldritchHabitat.Hover,EldritchAbility.Ring,190,800,4800,5000,72,1,120,24,55,"进入内圈或退出外圈")
    });
    public static EldritchDefinition Get(EldritchKind kind)=>(int)kind<All.Count?All[(int)kind]:throw new ArgumentOutOfRangeException(nameof(kind));
    public static int MaxHealth(EldritchCreature c)=>c.IsMinion?30:Get(c.Kind).Health;
}
