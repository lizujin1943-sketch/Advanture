using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed class WeaponDefinition
{
    internal WeaponDefinition(string[] c)
    {
        if(c.Length!=14)throw new InvalidDataException("Invalid melee weapon catalog columns.");
        Id=c[0];Name=c[1];int[] n=c.Skip(2).Select(v=>int.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        Tier=n[0];Range=n[1];CosHalfAngle=n[2];Damage=n[3];WindupTicks=n[4];DurationTicks=n[5];CooldownTicks=n[6];StaminaCost=n[7];FriendlyKnockback=n[8];Price=n[9];Width=n[10];Height=n[11];
        if(n.Length!=12 || Tier<1 || Tier>5 || Range<300 || Range>5000 || CosHalfAngle<0 || CosHalfAngle>1000 || Damage<1 || Damage>1000 || WindupTicks<1 || WindupTicks>=DurationTicks || DurationTicks>180 || CooldownTicks<DurationTicks || CooldownTicks>600 || StaminaCost<1 || StaminaCost>100 || FriendlyKnockback<1000 || FriendlyKnockback>46000 || Price<1 || Width<1 || Width>4 || Height<1 || Height>4)throw new InvalidDataException("Invalid melee weapon catalog.");
    }
    public string Id {get;} public string Name {get;}
    public int Tier {get;} public int Range {get;} public int CosHalfAngle {get;} public int Damage {get;}
    public int WindupTicks {get;} public int DurationTicks {get;} public int CooldownTicks {get;}
    public int StaminaCost {get;} public int FriendlyKnockback {get;} public int Price {get;} public int Width {get;} public int Height {get;}
}
public static class WeaponCatalog
{
    public static IReadOnlyList<WeaponDefinition> All {get;}=Array.AsReadOnly(CatalogData.Rows("weapons.csv").Select(c=>new WeaponDefinition(c)).ToArray());
    public static WeaponDefinition Get(int type)=>type>=0 && type<All.Count?All[type]:throw new ArgumentOutOfRangeException(nameof(type));
    internal static int TypeOf(WeaponDefinition weapon){for(int i=0;i<All.Count;i++)if(ReferenceEquals(All[i],weapon))return i;throw new ArgumentException("Unknown weapon.",nameof(weapon));}
}
public sealed class MeleeConfig
{
    public static MeleeConfig Disabled {get;}=new MeleeConfig(false,false);
    public MeleeConfig(bool enabled=true,bool starterPan=true,int piranhaHealth=50,int stunTicks=15)
    {if(piranhaHealth<1 || piranhaHealth>10000 || stunTicks<1 || stunTicks>300 || !enabled && starterPan)throw new InvalidDataException("Invalid melee config.");Enabled=enabled;StarterPan=starterPan;PiranhaHealth=piranhaHealth;StunTicks=stunTicks;}
    public bool Enabled {get;} public bool StarterPan {get;} public int PiranhaHealth {get;} public int StunTicks {get;}
    internal void Write(BinaryWriter w){w.Write(Enabled);w.Write(StarterPan);w.Write(PiranhaHealth);w.Write(StunTicks);}
    internal static MeleeConfig Read(BinaryReader r)
    {byte enabled=r.ReadByte(),starter=r.ReadByte();if(enabled>1 || starter>1)throw new InvalidDataException("Invalid melee flags.");return new MeleeConfig(enabled==1,starter==1,r.ReadInt32(),r.ReadInt32());}
}
public struct MeleeState
{
    public uint Serial;
    public int WeaponType,RemainingTicks,CooldownTicks,DirectionX,DirectionZ,HitCount;
    public bool Active=>RemainingTicks>0;
}
public static class MeleeRules
{
    public static WeaponDefinition? Equipped(HookState s,Fisher p)
    {var item=s.Tackle?.Inventory.Equipped(p.Slot,EquipmentSlot.RightHand);return s.Tackle?.Config.Melee.Enabled==true && item?.Kind==ItemKind.Weapon?WeaponCatalog.Get(item.Value.Type):null;}
    public static bool CanAct(Fisher p,bool hasRing)=>!p.Unavailable && !p.Vitals.Downed && !p.Revival.Dead && !p.Knockback.Active && p.PulledBy<0 && p.HookedPlayer<0 && !p.Shopping && p.Phase==HookPhase.Ready && (!p.Swimming.InWater || hasRing);
    public static bool Begin(ref Fisher p,WeaponDefinition weapon,int aimX,int aimZ,bool hasRing)
    {
        if(!CanAct(p,hasRing) || p.Melee.Active || p.Melee.CooldownTicks>0 || p.Stamina<weapon.StaminaCost*StaminaSystem.UnitsPerPoint)return false;
        int dx=aimX-p.X,dz=aimZ-p.Z;if((long)dx*dx+(long)dz*dz<=10000)return false;
        EnemyGeometry.Direction(dx,dz,out int x,out int z);
        var m=p.Melee;m.Serial=unchecked(m.Serial+1);m.WeaponType=WeaponCatalog.TypeOf(weapon);m.RemainingTicks=weapon.DurationTicks;m.CooldownTicks=weapon.CooldownTicks;m.DirectionX=x;m.DirectionZ=z;p.Melee=m;
        StaminaSystem.Spend(ref p,weapon.StaminaCost*StaminaSystem.UnitsPerPoint);return true;
    }
    public static bool Contains(Fisher attacker,int x,int z,WeaponDefinition weapon,int targetRadius=0)
    {
        long dx=x-attacker.X,dz=z-attacker.Z,square=dx*dx+dz*dz;long range=weapon.Range+targetRadius;
        if(square>range*range)return false;if(square==0)return true;
        long dot=dx*attacker.Melee.DirectionX+dz*attacker.Melee.DirectionZ;
        return dot>=0 && dot>=(long)HookGame.Sqrt(square)*weapon.CosHalfAngle*1000;
    }
    internal static void Cancel(ref Fisher p){var m=p.Melee;m.RemainingTicks=0;p.Melee=m;}
}
