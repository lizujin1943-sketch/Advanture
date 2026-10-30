using System;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class MeleeStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        w.Write(0x314C454D); // MEL1
        foreach(var p in s.Players){var m=p.Melee;w.Write(m.Serial);w.Write(m.WeaponType);w.Write(m.RemainingTicks);w.Write(m.CooldownTicks);w.Write(m.DirectionX);w.Write(m.DirectionZ);w.Write(m.HitCount);}
        foreach(var f in s.Enemies.Piranhas){w.Write(f.DamageTaken);w.Write(f.StunTicks);}
    }
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x314C454D)throw new InvalidDataException("Melee state version mismatch.");
        var config=s.Tackle?.Config.Melee??MeleeConfig.Disabled;var players=s.Players.ToArray();
        for(int i=0;i<players.Length;i++)
        {
            var m=new MeleeState{Serial=r.ReadUInt32(),WeaponType=r.ReadInt32(),RemainingTicks=r.ReadInt32(),CooldownTicks=r.ReadInt32(),DirectionX=r.ReadInt32(),DirectionZ=r.ReadInt32(),HitCount=r.ReadInt32()};
            if(!config.Enabled && !m.Equals(default(MeleeState)))throw new InvalidDataException("Disabled melee has action receipts.");
            if(m.WeaponType<0 || m.WeaponType>=WeaponCatalog.All.Count)throw new InvalidDataException("Unknown melee weapon.");
            var weapon=WeaponCatalog.Get(m.WeaponType);var p=players[i];
            long square=(long)m.DirectionX*m.DirectionX+(long)m.DirectionZ*m.DirectionZ;
            bool direction=square>=999998L*999998 && square<=1000002L*1000002;
            if(m.RemainingTicks<0 || m.RemainingTicks>=weapon.DurationTicks || m.CooldownTicks<0 || m.CooldownTicks>weapon.CooldownTicks || m.HitCount<0 ||
                (!m.Equals(default(MeleeState)) && !direction) ||
                m.Active && (m.CooldownTicks-m.RemainingTicks!=weapon.CooldownTicks-weapon.DurationTicks+1 || !MeleeRules.CanAct(p,s.Tackle!.Inventory.Equipped(p.Slot,EquipmentSlot.SwimRing)!=null) || MeleeRules.Equipped(s,p)!=weapon))
                throw new InvalidDataException("Invalid melee action state.");
            players[i].Melee=m;
        }
        var fish=s.Enemies.Piranhas.ToArray();
        for(int i=0;i<fish.Length;i++)
        {
            int damage=r.ReadInt32(),stun=r.ReadInt32();
            if(damage<0 || damage>config.PiranhaHealth || stun<0 || stun>config.StunTicks || !config.Enabled && (damage!=0 || stun!=0) || damage==config.PiranhaHealth && fish[i].RespawnTicks==0 || stun>0 && (damage==0 || fish[i].RespawnTicks>0))throw new InvalidDataException("Invalid melee enemy damage.");
            fish[i].DamageTaken=damage;fish[i].StunTicks=stun;
        }
        var e=s.Enemies;return new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,new EnemyState(fish,e.Monster,e.Waterballs,e.Serial,e.Bites,e.Shots,e.Interruptions,e.WaterHits,e.Captures),s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale);
    }
}
