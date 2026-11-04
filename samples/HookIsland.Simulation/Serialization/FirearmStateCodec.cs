using System;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class FirearmStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        w.Write(0x314E5547); // GUN1
        foreach(var p in s.Players)
        {
            var f=p.Firearm;w.Write(f.ShotSequence);w.Write(f.WeaponId);w.Write(f.DrawTicks);w.Write(f.RecoveryTicks);w.Write(f.CooldownTicks);w.Write(f.ReloadTicks);w.Write(f.ReloadWeaponId);w.Write(f.MoveSpeed);
            w.Write(f.Spreads.Count);foreach(var spread in f.Spreads){w.Write(spread.Type);w.Write(spread.Angle);w.Write(spread.Remainder);}
        }
    }
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x314E5547)throw new InvalidDataException("Firearm state version mismatch.");
        var players=s.Players.ToArray();
        for(int i=0;i<players.Length;i++)
        {
            var f=new FirearmState {ShotSequence=r.ReadUInt32(),WeaponId=r.ReadInt32(),DrawTicks=r.ReadInt32(),RecoveryTicks=r.ReadInt32(),CooldownTicks=r.ReadInt32(),ReloadTicks=r.ReadInt32(),ReloadWeaponId=r.ReadInt32(),MoveSpeed=r.ReadInt32()};
            int count=r.ReadInt32();if(count<0 || count>FirearmCatalog.All.Count)throw new InvalidDataException("Invalid firearm spread count.");
            var spreads=new FirearmSpreadState[count];int previous=-1;
            for(int j=0;j<count;j++)
            {
                int type=r.ReadInt32(),angle=r.ReadInt32(),remainder=r.ReadInt32();
                if(type<=previous || type>=FirearmCatalog.All.Count || remainder<=-HookGame.Rate || remainder>=HookGame.Rate)throw new InvalidDataException("Invalid firearm spread state.");
                var definition=FirearmCatalog.Get(type).Spread;
                if(angle<definition.MinimumHalfAngle || angle>definition.MaximumConfiguredHalfAngle)throw new InvalidDataException("Invalid configured firearm spread angle.");
                spreads[j]=new FirearmSpreadState(type,angle,remainder);previous=type;
            }
            if(count>0)f.SetSpreads(spreads);
            var p=players[i];p.Firearm=f;var right=s.Tackle?.Inventory.ActiveItem(p.Slot);
            if(f.WeaponId<0 || f.ReloadWeaponId<0 || f.MoveSpeed<0 || f.MoveSpeed>1000000 ||
                f.DrawTicks<0 || f.DrawTicks>FirearmCatalog.All.Max(g=>g.DrawTicks) || f.RecoveryTicks<0 || f.RecoveryTicks>FirearmCatalog.All.Max(g=>g.RecoveryTicks) ||
                f.CooldownTicks<0 || f.CooldownTicks>FirearmCatalog.All.Max(g=>g.CooldownTicks) || f.ReloadTicks<0 || f.ReloadTicks>FirearmCatalog.All.Max(g=>g.ReloadTicks) ||
                f.WeaponId>0 && (right?.Kind!=ItemKind.Firearm || right?.Id!=f.WeaponId) ||
                (f.ReloadTicks==0)!=(f.ReloadWeaponId==0) || f.Reloading && (f.ReloadWeaponId!=f.WeaponId || f.DrawTicks>0 || f.RecoveryTicks>0) ||
                f.Busy && (f.WeaponId==0 || !FirearmRules.CanMaintain(p,s.Tackle!.Inventory.Equipped(p.Slot,EquipmentSlot.SwimRing)!=null)) ||
                s.Tackle?.Config.Firearms.Enabled!=true && (f.ShotSequence!=0 || f.WeaponId!=0 || f.Busy || f.CooldownTicks!=0 || f.MoveSpeed!=0 || count!=0))throw new InvalidDataException("Invalid firearm action state.");
            if(f.Busy)
            {
                var definition=FirearmCatalog.Get(right!.Value.Type);
                if(f.DrawTicks>definition.DrawTicks || f.RecoveryTicks>definition.RecoveryTicks || f.ReloadTicks>definition.ReloadTicks || f.Reloading && right.Value.LoadedAmmo>=definition.Capacity)throw new InvalidDataException("Invalid weapon action clocks.");
            }
            players[i]=p;
        }
        return new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs);
    }
}
