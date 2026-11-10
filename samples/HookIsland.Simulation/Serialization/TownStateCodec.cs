using System;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class TownStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        var t=s.Town;w.Write(0x334E5754);w.Write(t.Enabled);if(!t.Enabled)return;
        w.Write(t.Goals);w.Write(t.Shortcuts);w.Write(t.ReadyMask);w.Write(t.FinishMask);w.Write(t.MapKnownMask);w.Write(t.ContractMask);w.Write(t.ContractsPaidMask);
        w.Write(t.Attempt);w.Write(t.Members);w.Write(t.FinaleCompleted);w.Write(t.Finished);
        foreach(var z in t.Zones){w.Write(z.Started);w.Write(z.Cleared);w.Write(z.Paused);w.Write(z.IdleTicks);w.Write(z.TriggerTick);w.Write(z.DefeatedAdults);}
        foreach(var p in t.Players)
        {w.Write(p.LastRequest);w.Write((byte)p.LastResult);w.Write(p.GrantedAttempt);w.Write(p.InnRegistered);w.Write(p.NextEmergencyTick);w.Write(p.EmergencyReserve);w.Write(p.BoundReserve);w.Write(p.BoundLoaded);w.Write(p.BoundWeaponId);w.Write(p.ServiceSpent);w.Write(p.SoldValue);}
    }
    private static bool Flag(BinaryReader r){byte b=r.ReadByte();if(b>1)throw new InvalidDataException("Invalid town flag.");return b==1;}
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x334E5754)throw new InvalidDataException("Town state version mismatch.");
        bool enabled=Flag(r);if(enabled!=(s.Tackle?.Config.TownMode==true))throw new InvalidDataException("Town/map mode mismatch.");
        if(!enabled)return s;
        int goals=r.ReadInt32(),shortcuts=r.ReadInt32(),ready=r.ReadInt32(),finish=r.ReadInt32(),known=r.ReadInt32(),contracts=r.ReadInt32(),paid=r.ReadInt32();
        uint attempt=r.ReadUInt32();int members=r.ReadInt32();bool completed=Flag(r),finished=Flag(r);int mask=(1<<s.Players.Count)-1;
        if(goals<0 || goals>7 || shortcuts<0 || shortcuts>3 || ready<0 || (ready&~mask)!=0 || finish<0 || (finish&~mask)!=0 || known<0 || (known&~mask)!=0 || contracts<0 || (contracts&~mask)!=0 || paid<0 || paid>=(1<<(s.Players.Count*2)) || members<0 || (members&~mask)!=0 || completed!=(s.Boss.Phase==BossPhase.Defeated && s.Boss.Rewarded) || completed && goals!=7 || finished && !completed)
            throw new InvalidDataException("Invalid town progression.");
        var zones=new TownZoneState[6];
        for(int i=0;i<zones.Length;i++)
        {
            zones[i]=new TownZoneState{Started=Flag(r),Cleared=Flag(r),Paused=Flag(r),IdleTicks=r.ReadInt32(),TriggerTick=r.ReadUInt64(),DefeatedAdults=r.ReadInt32()};
            var z=zones[i];int target=TownEncounters.ClearTarget(i);
            // 连续生成期间，已清理区仍可有活怪、暂停或再次排队；首次进度单独校验。
            if(z.IdleTicks<0 || z.IdleTicks>360 || (z.Paused || z.DefeatedAdults>0) && !z.Started ||
                z.TriggerTick>s.Steps || z.DefeatedAdults<0 || z.DefeatedAdults>target || z.Cleared!=(z.DefeatedAdults==target))
                throw new InvalidDataException("Invalid district progression.");
        }
        if(zones.Count(z=>z.Started && !z.Paused)>2)throw new InvalidDataException("Town encounter occupancy exceeded.");
        if((goals&1)!=0 && !zones[2].Cleared || (goals&2)!=0 && !zones[4].Cleared || (goals&4)!=0 && !zones[5].Cleared)throw new InvalidDataException("Town objective bypassed encounter.");
        var personals=new TownPersonalState[s.Players.Count];
        for(int i=0;i<personals.Length;i++)
        {
            var p=new TownPersonalState{LastRequest=r.ReadUInt32(),LastResult=(TownResult)r.ReadByte(),GrantedAttempt=r.ReadUInt32(),InnRegistered=Flag(r),NextEmergencyTick=r.ReadUInt64(),EmergencyReserve=r.ReadInt32(),BoundReserve=r.ReadInt32(),BoundLoaded=r.ReadInt32(),BoundWeaponId=r.ReadInt32(),ServiceSpent=r.ReadInt32(),SoldValue=r.ReadInt32()};
            var gun=s.Tackle!.Inventory.Items.FirstOrDefault(g=>g.Id==p.BoundWeaponId && g.Owner==i && g.Kind==ItemKind.Firearm && g.Type==0);
            if(p.LastResult>TownResult.LimitReached || (p.LastRequest==0)!=(p.LastResult==TownResult.None) || p.GrantedAttempt>attempt || p.EmergencyReserve<0 || p.EmergencyReserve>7 || p.BoundReserve<0 || p.BoundReserve>10000 || p.BoundLoaded<0 || p.BoundLoaded>14 || p.BoundLoaded>gun.LoadedAmmo || p.BoundLoaded>0 && gun.Id==0 || p.BoundWeaponId<0 || p.ServiceSpent<0 || p.SoldValue<0 || p.NextEmergencyTick>s.Steps+2700 || p.BoundReserve>0 && (p.GrantedAttempt!=attempt || (members&(1<<i))==0 || !s.Boss.Active))throw new InvalidDataException("Invalid town personal receipt.");
            var fisher=s.Players[i];if(fisher.CoinsEarned-(long)s.Tackle.Inventory.SpentCoins[i]-fisher.Revival.CoinsSpent-p.ServiceSpent!=fisher.Coins)throw new InvalidDataException("Town wallet conservation failed.");personals[i]=p;
        }
        var inv=s.Tackle!.Inventory;int toolCount=s.Players.Count*2;
        long value=inv.Items.Where(i=>!InventoryCatalog.IsMaterial(i.Kind) && i.Kind!=ItemKind.Ammunition && i.Id>toolCount).Sum(i=>(long)i.Definition.Price*i.Count)
            +inv.ConsumedFoods.Select((n,i)=>(long)n*FoodCatalog.Get(i).Price).Sum()+inv.PurchasedAmmunition.Select((n,i)=>(long)n*AmmunitionCatalog.Get(i).Price).Sum()+personals.Sum(p=>(long)p.SoldValue)-inv.SpentCoins.Sum(n=>(long)n);
        if(value!=(long)s.Players.Count*FirearmCatalog.Get(0).Price)throw new InvalidDataException("Town purchased equipment conservation failed.");
        if(TownLayout.InitialTiles().Any(t=>!s.Tiles.Any(v=>v.X==t.X && v.Z==t.Z) && !s.Boss.DestroyedTiles.Any(v=>v.X==t.X && v.Z==t.Z)))
            throw new InvalidDataException("Missing arena floor has no destruction receipt.");
        var expected=TownLayout.InitialTiles().Where(t=>s.Tiles.Any(v=>v.X==t.X && v.Z==t.Z)).ToList();
        for(int i=0;i<2;i++)if((shortcuts&(1<<i))!=0)expected.AddRange(TownLayout.ShortcutTiles(i));
        if(completed)expected.AddRange(TownLayout.ReturnBridgeTiles());
        if(!expected.Select(t=>(t.X,t.Z)).ToHashSet().SetEquals(s.Tiles.Select(t=>(t.X,t.Z))))throw new InvalidDataException("Town bridge receipt differs from terrain.");
        var town=new TownState(true,personals,zones,goals,shortcuts,ready,finish,known,contracts,paid,attempt,members,completed,finished);
        return new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,s.Boss,s.Grenades,s.Eldritch,town);
    }
}
