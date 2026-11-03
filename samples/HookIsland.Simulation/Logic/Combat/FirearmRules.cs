using System;

namespace Advanture.HookIsland.Simulation;

public static class FirearmRules
{
    public static FirearmDefinition? Equipped(HookState s,Fisher p)
    {var item=s.Tackle?.Inventory.Equipped(p.Slot,EquipmentSlot.RightHand);return s.Tackle?.Config.Firearms.Enabled==true && item?.Kind==ItemKind.Firearm?FirearmCatalog.Get(item.Value.Type):null;}
    public static bool CanMaintain(Fisher p,bool hasRing)=>!p.Unavailable && !p.Vitals.Downed && !p.Revival.Dead && !p.Knockback.Active && p.PulledBy<0 && p.HookedPlayer<0 && p.Phase==HookPhase.Ready && !p.Melee.Active && (!p.Swimming.InWater || hasRing);
    public static bool CanAct(Fisher p,bool hasRing)=>CanMaintain(p,hasRing) && !p.Shopping;
    public static int FriendlyDamage(int damage,int armor,int percent=50)
    {if(damage<0 || armor<0 || percent<0 || percent>100)throw new ArgumentOutOfRangeException(nameof(damage));long denominator=100L+armor;return (int)(((long)damage*percent+denominator-1)/denominator);}
    public static int MovementPermille(Fisher p,bool recoveringAtFrameStart=false)=>p.Firearm.Reloading?700:p.Firearm.RecoveryTicks>0 || recoveringAtFrameStart?800:1000;
    public static void CancelReload(ref Fisher p){p.Firearm.ReloadTicks=0;p.Firearm.ReloadWeaponId=0;}
    public static void CancelActions(ref Fisher p){CancelReload(ref p);p.Firearm.DrawTicks=p.Firearm.RecoveryTicks=0;}
    /// <summary>First ray/circle contact in millimetres; ties are resolved by the caller's stable iteration order.</summary>
    public static int RayCircle(int ox,int oz,int dx,int dz,int x,int z,int radius,int range)
    {
        long rx=(long)x-ox,rz=(long)z-oz;
        if(rx*rx+rz*rz<=(long)radius*radius)return 0;
        long along=(rx*dx+rz*dz)/EnemyGeometry.DirectionScale;
        long across=(rx*dz-rz*dx)/EnemyGeometry.DirectionScale;
        if(along<0 || Math.Abs(across)>radius)return int.MaxValue;
        long entry=along-HookGame.Sqrt((long)radius*radius-across*across);
        return entry<0?0:entry<=range?(int)entry:int.MaxValue;
    }
    public static int RayBox(int ox,int oz,int dx,int dz,int x,int z,int half,int range)
    {
        long near=0,far=range;
        bool Axis(int origin,int direction,int center)
        {
            if(direction==0)return origin>=center-half && origin<=center+half;
            long a=((long)center-half-origin)*EnemyGeometry.DirectionScale/direction,b=((long)center+half-origin)*EnemyGeometry.DirectionScale/direction;
            near=Math.Max(near,Math.Min(a,b));far=Math.Min(far,Math.Max(a,b));return near<=far;
        }
        return Axis(ox,dx,x) && Axis(oz,dz,z)?(int)near:int.MaxValue;
    }
    /// <summary>三维实体遮挡使用三个整数平板区间，俯仰不会绕过同距离的商店墙。</summary>
    public static int RayBox3(int ox,int oy,int oz,int dx,int dy,int dz,int x,int y,int z,int halfX,int halfY,int halfZ,int range)
    {
        long near=0,far=range;
        bool Axis(int origin,int direction,int center,int half)
        {
            if(direction==0)return origin>=center-half && origin<=center+half;
            long a=((long)center-half-origin)*EnemyGeometry.DirectionScale/direction,b=((long)center+half-origin)*EnemyGeometry.DirectionScale/direction;
            near=Math.Max(near,Math.Min(a,b));far=Math.Min(far,Math.Max(a,b));return near<=far;
        }
        return Axis(ox,dx,x,halfX) && Axis(oy,dy,y,halfY) && Axis(oz,dz,z,halfZ)?(int)near:int.MaxValue;
    }

}
