using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public enum FirearmSpreadSampling : byte { Triangular }
public enum FirearmCategory : byte { Pistol, Rifle, Sniper }

public readonly struct MovementSpreadNode
{
    public MovementSpreadNode(int speed,int halfAngle){Speed=speed;HalfAngle=halfAngle;}
    public int Speed {get;}
    public int HalfAngle {get;}
}

/// <summary>Immutable speed (mm/s) to half-angle (millidegrees) profile, bounded by deterministic shot trigonometry.</summary>
public sealed class FirearmSpreadDefinition
{
    public const int MaximumHalfAngle=10000;
    public FirearmSpreadDefinition(IEnumerable<MovementSpreadNode> curve,int stationarySpeedThreshold,int increasePerSecond,int recoveryPerSecond,FirearmSpreadSampling sampling=FirearmSpreadSampling.Triangular)
    {
        var nodes=curve.ToArray();
        if(nodes.Length<1 || nodes.Length>32 || stationarySpeedThreshold<0 || stationarySpeedThreshold>1000000 ||
            increasePerSecond<1 || increasePerSecond>MaximumHalfAngle*HookGame.Rate || recoveryPerSecond<1 || recoveryPerSecond>MaximumHalfAngle*HookGame.Rate ||
            sampling!=FirearmSpreadSampling.Triangular || nodes[0].Speed!=stationarySpeedThreshold)
            throw new InvalidDataException("Invalid firearm spread configuration.");
        for(int i=0;i<nodes.Length;i++)
            if(nodes[i].Speed<0 || nodes[i].Speed>1000000 || nodes[i].HalfAngle<0 || nodes[i].HalfAngle>MaximumHalfAngle ||
                i>0 && (nodes[i].Speed<=nodes[i-1].Speed || nodes[i].HalfAngle<nodes[i-1].HalfAngle))
                throw new InvalidDataException("Invalid firearm movement spread curve.");
        MovementSpreadCurve=Array.AsReadOnly(nodes);StationarySpeedThreshold=stationarySpeedThreshold;
        IncreasePerSecond=increasePerSecond;RecoveryPerSecond=recoveryPerSecond;Sampling=sampling;
    }
    public IReadOnlyList<MovementSpreadNode> MovementSpreadCurve {get;}
    public int StationarySpeedThreshold {get;}
    public int IncreasePerSecond {get;}
    public int RecoveryPerSecond {get;}
    public FirearmSpreadSampling Sampling {get;}
    public int MinimumHalfAngle=>MovementSpreadCurve[0].HalfAngle;
    public int MaximumConfiguredHalfAngle=>MovementSpreadCurve[MovementSpreadCurve.Count-1].HalfAngle;
    internal static FirearmSpreadDefinition Parse(string curve,string threshold,string increase,string recovery,string sampling)
    {
        var nodes=curve.Split('|').Select(value=>{
            var pair=value.Split(':');if(pair.Length!=2)throw new InvalidDataException("Invalid firearm spread node.");
            return new MovementSpreadNode(int.Parse(pair[0],CultureInfo.InvariantCulture),int.Parse(pair[1],CultureInfo.InvariantCulture));
        });
        if(!Enum.TryParse<FirearmSpreadSampling>(sampling,false,out var kind))throw new InvalidDataException("Invalid firearm spread sampling.");
        return new FirearmSpreadDefinition(nodes,int.Parse(threshold,CultureInfo.InvariantCulture),int.Parse(increase,CultureInfo.InvariantCulture),int.Parse(recovery,CultureInfo.InvariantCulture),kind);
    }
}

/// <summary>Ranged weapon configuration; independent from the melee weapon catalog.</summary>
public sealed class FirearmDefinition
{
    internal FirearmDefinition(string[] c)
    {
        if(c.Length!=23)throw new InvalidDataException("Invalid firearm catalog columns.");
        Id=c[0];Name=c[1];var n=c.Skip(2).Take(13).Select(v=>int.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        Tier=n[0];Range=n[1];Damage=n[2];CooldownTicks=n[3];ReloadTicks=n[4];DrawTicks=n[5];RecoveryTicks=n[6];Capacity=n[7];AmmoType=n[8];Price=n[9];Width=n[10];Height=n[11];Stock=n[12];
        if(string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Tier<1 || Tier>5 || Range<100 || Range>50000 || Damage<1 || Damage>1000 || CooldownTicks<1 || CooldownTicks>18000 || ReloadTicks<1 || ReloadTicks>18000 || DrawTicks<1 || DrawTicks>18000 || RecoveryTicks<1 || RecoveryTicks>CooldownTicks || Capacity<1 || Capacity>99 || AmmoType<0 || AmmoType>=AmmunitionCatalog.All.Count || Price<1 || Width<1 || Width>4 || Height<1 || Height>4 || Stock<1 || Stock>99)
            throw new InvalidDataException("Invalid firearm configuration.");
        Spread=FirearmSpreadDefinition.Parse(c[15],c[16],c[17],c[18],c[19]);
        if(!Enum.TryParse(c[20],out FirearmCategory category) || !Enum.IsDefined(typeof(FirearmCategory),category) || (c[21]!="0" && c[21]!="1"))
            throw new InvalidDataException("Invalid firearm category or trigger mode.");
        Category=category;Automatic=c[21]=="1";ScopeMagnification=int.Parse(c[22],CultureInfo.InvariantCulture);
        if(ScopeMagnification<1 || ScopeMagnification>12 || Category==FirearmCategory.Sniper && ScopeMagnification==1 || Category!=FirearmCategory.Sniper && ScopeMagnification!=1)
            throw new InvalidDataException("Invalid firearm scope.");
    }
    public string Id {get;} public string Name {get;}
    public int Tier {get;} public int Range {get;} public int Damage {get;} public int CooldownTicks {get;}
    public int ReloadTicks {get;} public int DrawTicks {get;} public int RecoveryTicks {get;} public int Capacity {get;}
    public int AmmoType {get;} public int Price {get;} public int Width {get;} public int Height {get;} public int Stock {get;}
    public FirearmSpreadDefinition Spread {get;}
    public FirearmCategory Category {get;}
    public bool Automatic {get;}
    public int ScopeMagnification {get;}
}

public static class FirearmCatalog
{
    public const int OfferOffset=16;
    public static IReadOnlyList<FirearmDefinition> All {get;}=Array.AsReadOnly(CatalogData.Rows("firearms.csv").Select(c=>new FirearmDefinition(c)).ToArray());
    public static FirearmDefinition Get(int type)=>type>=0 && type<All.Count?All[type]:throw new ArgumentOutOfRangeException(nameof(type));
    internal static int TypeOf(FirearmDefinition weapon){for(int n=0;n<All.Count;n++)if(ReferenceEquals(All[n],weapon))return n;throw new ArgumentException("Unknown firearm.",nameof(weapon));}
}

public sealed class AmmunitionDefinition
{
    internal AmmunitionDefinition(string[] c)
    {
        if(c.Length!=6)throw new InvalidDataException("Invalid ammunition catalog columns.");
        Id=c[0];Name=c[1];Price=int.Parse(c[2],CultureInfo.InvariantCulture);Quantity=int.Parse(c[3],CultureInfo.InvariantCulture);Stock=int.Parse(c[4],CultureInfo.InvariantCulture);StackLimit=int.Parse(c[5],CultureInfo.InvariantCulture);
        if(string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Price<1 || Quantity<1 || Quantity>99 || Stock<1 || Stock>99 || StackLimit<1 || StackLimit>99)throw new InvalidDataException("Invalid ammunition configuration.");
    }
    public string Id {get;} public string Name {get;} public int Price {get;} public int Quantity {get;} public int Stock {get;} public int StackLimit {get;}
}

public static class AmmunitionCatalog
{
    public const int OfferOffset=32;
    public static IReadOnlyList<AmmunitionDefinition> All {get;}=Array.AsReadOnly(CatalogData.Rows("ammunition.csv").Select(c=>new AmmunitionDefinition(c)).ToArray());
    public static AmmunitionDefinition Get(int type)=>type>=0 && type<All.Count?All[type]:throw new ArgumentOutOfRangeException(nameof(type));
}
