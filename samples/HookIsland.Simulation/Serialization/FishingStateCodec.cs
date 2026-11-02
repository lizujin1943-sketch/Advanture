using System;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>钓具与统一库存快照：保存收据和个人收入／支出，校验共享库存、物品归属及跨对象约束。</summary>
internal static class FishingStateCodec
{
    internal static void Write(BinaryWriter w,HookState state)
    {
        w.Write(state.Tackle!=null);if(state.Tackle==null)return;
        var t=state.Tackle;t.Config.Write(w);MerchantStateCodec.Write(w,t.Merchants);
        InventoryStateCodec.Write(w,t.Inventory);
        foreach(var p in state.Players)
        {
            w.Write(p.EquippedRod);w.Write(p.Stamina);w.Write(p.RegenDelay);w.Write(p.HookTravelRemainder);w.Write(p.ReelRemainder);
            w.Write(p.CoinsEarned);w.Write(p.Escapes);w.Write(p.LastRodRequest);w.Write((byte)p.LastRodResult);
            w.Write(p.LastInventoryRequest);w.Write((byte)p.LastInventoryResult);w.Write(p.FoodCooldownTicks);w.Write(p.SelectedQuickSlot);
            CharacterStateCodec.Write(w,p.Vitals,p.MoveRemainderX,p.MoveRemainderZ);SwimmingStateCodec.Write(w,p.Swimming);RevivalStateCodec.Write(w,p.Revival);
        }
        foreach(var f in state.Fish){w.Write(f.Species);w.Write(f.WeightGrams);w.Write(f.Grade);w.Write(f.Generation);}
    }
    internal static HookState Read(BinaryReader r,HookState state)
    {
        byte enabled=r.ReadByte();if(enabled>1)throw new InvalidDataException("Invalid equipment flag.");
        if(enabled==0)
        {
            if(state.Players.Any(p=>p.Distance>HookUpgrades.Range(p.RangeLevel)))throw new InvalidDataException("Hook beyond supported range.");
            return state;
        }
        var config=FishingConfig.Read(r);var merchants=MerchantStateCodec.Read(r);
        var inventory=InventoryStateCodec.Read(r);
        var players=state.Players.ToArray();
        for(int n=0;n<players.Length;n++)
        {
            var p=players[n];p.EquippedRod=r.ReadInt32();p.Stamina=r.ReadInt32();p.RegenDelay=r.ReadInt32();
            p.HookTravelRemainder=r.ReadInt32();p.ReelRemainder=r.ReadInt32();p.CoinsEarned=r.ReadInt32();p.Escapes=r.ReadInt32();
            p.LastRodRequest=r.ReadUInt32();p.LastRodResult=(RodResult)r.ReadByte();
            p.LastInventoryRequest=r.ReadUInt32();p.LastInventoryResult=(InventoryResult)r.ReadByte();p.FoodCooldownTicks=r.ReadInt32();p.SelectedQuickSlot=r.ReadInt32();
            p.Vitals=CharacterStateCodec.Read(r,out p.MoveRemainderX,out p.MoveRemainderZ);p.Swimming=SwimmingStateCodec.Read(r);p.Revival=RevivalStateCodec.Read(r);players[n]=p;
        }
        var fish=state.Fish.ToArray();
        for(int n=0;n<fish.Length;n++){var f=fish[n];f.Species=r.ReadInt32();f.WeightGrams=r.ReadInt32();f.Grade=r.ReadInt32();f.Generation=r.ReadUInt32();fish[n]=f;}
        var result=new HookState(state.Steps,players,fish,state.Coins,state.Tiles,state.Driftwood,new TackleState(config,inventory,merchants));
        Validate(result);return result;
    }
    private static void Validate(HookState state)
    {
        var t=state.Tackle!;var inventory=t.Inventory;
        MerchantStateCodec.Validate(t.Config,t.Merchants,state.Steps);
        if(inventory.PlayerCount!=state.Players.Count)
            throw new InvalidDataException("Invalid inventory participants/restock clock.");
        // 出生装备可以转交，ID 和类型必须保留；转交不改变原购买者的支出账本。
        int players=state.Players.Count;
        int starterCount=(t.Config.Melee.StarterPan?3:2)*players;
        for(int id=1;id<=starterCount;id++)
        {
            var item=inventory.Items.SingleOrDefault(i=>i.Id==id);
            if(item.Id==0 || (id<=players?(item.Kind!=ItemKind.Rod || item.Type!=0):id<=2*players?(item.Kind!=ItemKind.Backpack || item.Type!=1):(item.Kind!=ItemKind.Weapon || item.Type!=0)))
                throw new InvalidDataException("Missing or altered starter equipment.");
        }
        if(inventory.Items.Any(i=>i.Kind==ItemKind.Rod && i.Type==0 && i.Id>players) ||
            inventory.Items.Where(i=>!InventoryCatalog.IsMaterial(i.Kind) && i.Id>starterCount).Sum(i=>(long)i.Definition.Price*i.Count)+inventory.ConsumedFoods.Select((count,type)=>(long)count*FoodCatalog.Get(type).Price).Sum()!=inventory.SpentCoins.Sum(i=>(long)i))
            throw new InvalidDataException("Equipment purchase ledger differs from surviving items.");
        foreach(var i in inventory.Items.Where(i=>i.Location==ItemLocation.Ground))
            if(!PlatformRules.CanStand(state.Tiles,i.WorldX,i.WorldZ))throw new InvalidDataException("Ground item is outside the platform.");
        foreach(var p in state.Players)
        {
            SwimmingStateCodec.Validate(p.Swimming);
            if(state.Tiles.Count>0 && p.Swimming.InWater==PlatformRules.CanStand(state.Tiles,p.X,p.Z))throw new InvalidDataException("Water state differs from terrain.");
            if((p.Swimming.InWater && p.Shopping) || (p.Swimming.Active && (p.Vitals.Downed || p.PulledBy>=0)))throw new InvalidDataException("Invalid water activity.");
            CharacterStateCodec.Validate(p.Vitals,p.MoveRemainderX,p.MoveRemainderZ,FishingRules.Attributes(state,p));
            var right=inventory.Equipped(p.Slot,EquipmentSlot.RightHand);
            if(p.EquippedRod!=(right?.Kind==ItemKind.Rod?right.Value.Id:0) ||
                p.Stamina<0 || p.Stamina>FishingRules.MaxStamina(state,p) || p.RegenDelay<0 || p.RegenDelay>t.Config.RegenDelayTicks ||
                p.HookTravelRemainder<0 || p.HookTravelRemainder>=HookGame.Rate || p.ReelRemainder<0 || p.ReelRemainder>=HookGame.Rate ||
                p.CoinsEarned<0 || p.CoinsEarned-(long)inventory.SpentCoins[p.Slot]-p.Revival.CoinsSpent!=p.Coins || p.Escapes<0 ||
                p.LastRodResult>RodResult.NotOwned || (p.LastRodRequest==0)!=(p.LastRodResult==RodResult.None) ||
                p.FoodCooldownTicks<0 || p.FoodCooldownTicks>t.Config.FoodCooldownTicks || p.SelectedQuickSlot< -1 || p.SelectedQuickSlot>=QuickbarRules.SlotCount ||
                p.LastInventoryResult>InventoryResult.CoolingDown || (p.LastInventoryRequest==0)!=(p.LastInventoryResult==InventoryResult.None) ||
                p.RangeLevel!=0 || p.SpeedLevel!=0 || (p.Phase!=HookPhase.Ready && (p.EquippedRod==0 || p.Distance>FishingRules.Equipped(state,p)!.Range)))
                throw new InvalidDataException("Invalid equipment, stamina, personal wallet or receipt.");
        }
        foreach(var f in state.Fish)
            if(f.Species<0 || f.Species>=FishCatalog.All.Count || f.WeightGrams<1000 || f.WeightGrams>10000 || f.WeightGrams%100!=0 || f.Grade<1 || f.Grade>3)
                throw new InvalidDataException("Invalid fish attributes.");
    }
}
