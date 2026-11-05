using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>
/// 游戏装备事务入口：校验商品、距离、操作时机和请求去重，再交给唯一 InventorySystem 提交。
/// 不维护第二套钓竿列表；个人钱包、共享货架和库存同时成功，失败回执也会被保存。
/// </summary>
internal sealed class EquipmentSystem
{
    private readonly FishingConfig config;
    private readonly MerchantSystem merchants;
    private readonly CharacterStats[] attributes;
    internal InventorySystem Inventory { get; }
    internal EquipmentSystem(FishingConfig config,Fisher[] players)
    {
        this.config=config;Inventory=new InventorySystem(players.Length,config.Melee.StarterPan,config.Firearms.Enabled,config.FishingRodsEnabled,config.Firearms.Enabled);attributes=new CharacterStats[players.Length];
        merchants=new MerchantSystem(config);
        for(int i=0;i<players.Length;i++)
        {
            players[i].SelectedQuickSlot=-1;Recalculate(i);Refresh(ref players[i]);
            players[i].Vitals=CharacterSystem.Spawn(attributes[i]);players[i].Stamina=attributes[i].StaminaMax;
            HealingSystem.Refill(ref players[i],config.Healing);
        }
    }
    internal EquipmentSystem(TackleState state)
    {
        config=state.Config;Inventory=new InventorySystem(state.Inventory);merchants=new MerchantSystem(config,state.Merchants);
        attributes=new CharacterStats[state.Inventory.PlayerCount];for(int i=0;i<attributes.Length;i++)Recalculate(i);
    }
    internal TackleState Capture()=>new TackleState(config,Inventory.Capture(),merchants.Capture());
    internal RodDefinition? Equipped(Fisher player)
    {var item=Inventory.ActiveItem(player.Slot);return item?.Kind==ItemKind.Rod?RodCatalog.Get(item.Value.Type):null;}
    internal WeaponDefinition? Weapon(Fisher player)
    {var item=Inventory.ActiveItem(player.Slot);return config.Melee.Enabled && item?.Kind==ItemKind.Weapon?WeaponCatalog.Get(item.Value.Type):null;}
    internal CharacterStats Attributes(Fisher player)=>attributes[player.Slot];
    internal int Maximum(Fisher player)=>Attributes(player).StaminaMax;
    /// <summary>自动回切也必须刷新派生属性和 EquippedRod；不能只改库存的活动物品编号。</summary>
    internal void RefreshSelection(ref Fisher player){Recalculate(player.Slot);Refresh(ref player);}
    internal SwimRingDefinition? Ring(Fisher player)
    {var item=Inventory.Equipped(player.Slot,EquipmentSlot.SwimRing);return item.HasValue?SwimRingCatalog.Get(item.Value.Type):null;}
    private void Recalculate(int owner)
    {
        attributes[owner]=CharacterEquipment.Calculate(config.Character,config.StaminaBase,Inventory.Capture(),owner);
    }
    internal void Advance(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,ulong step)
        =>merchants.Advance(map,tiles,players,step);
    internal void Apply(IslandMap map,IReadOnlyList<PlatformTile> tiles,ref Fisher player,HookInput command,ref int coins,bool rescuing=false,int boundWeaponId=0,int boundLoaded=0)
    {
        if(command.Inventory.Action!=InventoryAction.None && command.Inventory.RequestId>player.LastInventoryRequest)
        {
            int previousActive=Inventory.ActiveItem(player.Slot)?.Id??0;
            player.LastInventoryRequest=command.Inventory.RequestId;
            player.LastInventoryResult=Transact(map,tiles,ref player,command.Inventory,ref coins,rescuing,command.Inventory.Target==boundWeaponId?boundLoaded:0);
            if(player.LastInventoryResult==InventoryResult.Success)
            {
                if(Inventory.ActiveItem(player.Slot)?.Kind==ItemKind.Throwable && previousActive!=Inventory.ActiveItem(player.Slot)?.Id)player.Grenade.ReturnWeaponId=previousActive;
                Recalculate(player.Slot);
            }
        }
        // 保留旧钓竿输入适配，现有自动游玩和调用方也必须通过同一库存事务，不能绕过材料占格。
        if(command.RodAction!=RodAction.None && command.RodRequestId>player.LastRodRequest)
        {
            player.LastRodRequest=command.RodRequestId;
            var action=command.RodAction==RodAction.Buy?InventoryAction.Buy:command.RodAction==RodAction.BuyAndEquip?
                InventoryAction.BuyAndEquip:command.RodAction==RodAction.Equip?InventoryAction.Equip:InventoryAction.Unequip;
            int target=command.RodAction==RodAction.Unequip?(int)EquipmentSlot.RightHand:command.RodTarget;
            var stock=merchants.Get(0)!.Stock;
            bool wrongKind=(action==InventoryAction.Buy || action==InventoryAction.BuyAndEquip) && target<stock.Count && stock[target].Kind!=ItemKind.Rod;
            wrongKind|=action==InventoryAction.Equip && Inventory.Find(target)?.Kind!=ItemKind.Rod;
            var result=!config.FishingRodsEnabled?InventoryResult.Disabled:wrongKind?InventoryResult.InvalidItem:Transact(map,tiles,ref player,
                new InventoryCommand(action,target,batch:command.RodBatch,requestId:command.RodRequestId),ref coins);
            player.LastRodResult=LegacyResult(result);
            if(result==InventoryResult.Success)Recalculate(player.Slot);
        }
        Refresh(ref player);
    }
    private void Refresh(ref Fisher player)
    {
        var right=Inventory.ActiveItem(player.Slot);
        player.EquippedRod=right?.Kind==ItemKind.Rod?right.Value.Id:0;
        PersonalMaterialInventory.Refresh(ref player,Inventory);
        // 换装只裁切上限，不补满。自然休息仍由每帧的 StaminaSystem 负责。
        CharacterSystem.Clamp(ref player.Vitals,ref player.Stamina,Attributes(player));
    }
    private InventoryResult Transact(IslandMap map,IReadOnlyList<PlatformTile> tiles,ref Fisher player,InventoryCommand command,ref int coins,bool rescuing=false,int discardLoaded=0)
    {
        if(player.Unavailable || player.Vitals.Downed)return InventoryResult.Disabled;
        if(map.IsTown)
        {
            if(command.Action==InventoryAction.Buy || command.Action==InventoryAction.BuyAndEquip || command.DropOld)return InventoryResult.Disabled;
            if(command.Action==InventoryAction.Drop && command.Target<=Inventory.Capture().PlayerCount*3)return InventoryResult.InvalidItem;
        }
        if(player.Grenade.Active)return InventoryResult.HookBusy;
        if(player.Firearm.DrawTicks>0 || player.Firearm.RecoveryTicks>0)return InventoryResult.HookBusy;
        if(player.Firearm.Reloading && !WeaponLoadout.CanCancelReload(Inventory.Capture(),player.Slot,command))return InventoryResult.HookBusy;
        if(player.PulledBy>=0)return InventoryResult.HookBusy;
        bool grounded=SwimmingTerrain.Ground(map,tiles,player.X,player.Z);
        if(!grounded && (command.DropOld || command.Action==InventoryAction.Drop || command.Action==InventoryAction.Pickup ||
            command.Action==InventoryAction.PickupAndEquip || (command.Action==InventoryAction.Unequip && command.Target==(int)EquipmentSlot.Backpack)))return InventoryResult.InvalidPlacement;
        bool buying=command.Action==InventoryAction.Buy || command.Action==InventoryAction.BuyAndEquip;
        var point=new Point(player.X,player.Z);int owner=player.Slot;
        if(buying)
        {
            if(!MerchantRules.Near(map,tiles,player,command.MerchantId))return InventoryResult.TooFar;
            if(!MerchantRules.CanInteract(player))return InventoryResult.Disabled;
            var merchant=merchants.Get(command.MerchantId);
            if(merchant==null || !merchant.Active || command.Batch!=merchant.Batch)return InventoryResult.StaleStock;
            if(command.Target>=FirearmCatalog.OfferOffset && command.Target<FirearmCatalog.OfferOffset+FirearmCatalog.All.Count)
            {
                if(!config.Firearms.Enabled)return InventoryResult.Disabled;
                int type=command.Target-FirearmCatalog.OfferOffset;bool equipFirearm=command.Action==InventoryAction.BuyAndEquip;
                if(merchant.FirearmStock[type]==0)return InventoryResult.SoldOut;
                if(equipFirearm && (player.Phase!=HookPhase.Ready || player.Melee.Active))return InventoryResult.HookBusy;
                int before=player.Coins;
                var purchased=Inventory.Purchase(owner,ItemKind.Firearm,type,equipFirearm,command.DropOld,point,ref player.Coins);
                if(purchased==InventoryResult.Success)
                {coins-=before-player.Coins;merchants.SellFirearm(command.MerchantId,type);if(equipFirearm)player.SelectedQuickSlot=-1;}
                return purchased;
            }
            if(command.Target>=AmmunitionCatalog.OfferOffset && command.Target<AmmunitionCatalog.OfferOffset+AmmunitionCatalog.All.Count)
            {
                // Unlimited reserve: reject even stale requests from a shelf saved by the previous rules.
                return InventoryResult.Disabled;
            }
            if(command.Target>=FoodCatalog.OfferOffset && command.Target<FoodCatalog.OfferOffset+FoodCatalog.All.Count)
            {
                return InventoryResult.Disabled;
            }
            var stock=merchant.Stock;
            if(command.Target<0 || command.Target>=stock.Count || stock[command.Target].Sold)return InventoryResult.SoldOut;
            var offer=stock[command.Target];bool equip=command.Action==InventoryAction.BuyAndEquip;
            if(offer.Kind==ItemKind.Rod && !config.FishingRodsEnabled)return InventoryResult.Disabled;
            if(equip && WeaponLoadout.IsHandSlot(InventoryCatalog.Get(offer.Kind,offer.Type).Slot) && (player.Phase!=HookPhase.Ready || player.Melee.Active))return InventoryResult.HookBusy;
            int previousCoins=player.Coins;
            var result=Inventory.Purchase(owner,offer.Kind,offer.Type,equip,command.DropOld,point,ref player.Coins);
            if(result==InventoryResult.Success)
            {coins-=previousCoins-player.Coins;merchants.Sell(command.MerchantId,command.Target);if(equip && WeaponLoadout.IsHandSlot(InventoryCatalog.Get(offer.Kind,offer.Type).Slot))player.SelectedQuickSlot=-1;}
            return result;
        }
        if(command.Action==InventoryAction.SelectWeapon)
        {
            if(player.Phase!=HookPhase.Ready || player.Melee.Active)return InventoryResult.HookBusy;
            var result=Inventory.SelectWeapon(owner,(EquipmentSlot)command.Target);
            if(result==InventoryResult.Success)player.SelectedQuickSlot=-1;
            return result;
        }
        if(command.Action==InventoryAction.Heal)
            return HealingSystem.Use(ref player,Attributes(player),config.Healing,grounded,rescuing,Inventory.Events);
        // Food is retired from gameplay; stale use intents cannot provide extra healing.
        if(command.Action==InventoryAction.Use)return InventoryResult.Disabled;
        if(command.Action==InventoryAction.BindQuickSlot)return Inventory.BindQuickSlot(owner,command.Target,command.Amount-1);
        if(command.Action==InventoryAction.ClearQuickSlot)return Inventory.ClearQuickSlot(owner,command.Target-1);
        if(command.Action==InventoryAction.SelectQuickSlot)
        {
            int slot=command.Target-1;
            if(slot<0 || slot>=QuickbarRules.SlotCount)return InventoryResult.InvalidPlacement;
            var selected=Inventory.QuickItem(owner,slot);
            if(player.Phase!=HookPhase.Ready || player.Melee.Active)return InventoryResult.HookBusy;
            if(selected.HasValue && selected.Value.Kind!=ItemKind.Food)
            {
                var result=Inventory.Equip(owner,selected.Value.Id,false,point);
                if(result!=InventoryResult.Success)return result;
            }
            player.SelectedQuickSlot=slot;return InventoryResult.Success;
        }
        var item=Inventory.Find(command.Target)??default;
        bool changesRod=(command.Action==InventoryAction.Unequip && WeaponLoadout.IsHandSlot((EquipmentSlot)command.Target)) ||
            ((command.Action==InventoryAction.Equip || command.Action==InventoryAction.PickupAndEquip) && item.Id!=0 && WeaponLoadout.IsHandSlot(item.Definition.Slot));
        if(changesRod && (player.Phase!=HookPhase.Ready || player.Melee.Active))return InventoryResult.HookBusy;
        var outcome=command.Action switch {
            InventoryAction.Move=>Inventory.Move(owner,command.Target,command.X,command.Y,command.Rotated),
            InventoryAction.Split=>Inventory.Split(owner,command.Target,command.Amount,command.X,command.Y),
            InventoryAction.Drop=>Inventory.Drop(owner,command.Target,command.Amount,point,discardLoaded),
            InventoryAction.Pickup=>Inventory.Pickup(owner,command.Target,point,out _),
            InventoryAction.PickupAndEquip=>Inventory.PickupAndEquip(owner,command.Target,point,command.DropOld),
            InventoryAction.Equip=>Inventory.Equip(owner,command.Target,command.DropOld,point),
            InventoryAction.Unequip=>Inventory.Unequip(owner,(EquipmentSlot)command.Target,point),
            _=>InventoryResult.InvalidItem
        };
        if(outcome==InventoryResult.Success && changesRod)
        {
            player.SelectedQuickSlot=-1;
            var equipped=Inventory.ActiveItem(owner);
            if(equipped.HasValue)for(int n=0;n<QuickbarRules.SlotCount;n++)
                if(Inventory.QuickSlot(owner,n).ItemId==equipped.Value.Id){player.SelectedQuickSlot=n;break;}
        }
        return outcome;
    }
    internal bool ActiveTool(Fisher player)
    {
        if(player.SelectedQuickSlot<0)return true;
        var item=Inventory.QuickItem(player.Slot,player.SelectedQuickSlot);
        return item.HasValue && item.Value.Location==ItemLocation.Equipped && item.Value.Id==Inventory.ActiveItem(player.Slot)?.Id;
    }
    private static RodResult LegacyResult(InventoryResult result)=>result switch {
        InventoryResult.Disabled=>RodResult.Disabled,
        InventoryResult.Success=>RodResult.Success,InventoryResult.TooFar=>RodResult.TooFar,
        InventoryResult.HookBusy=>RodResult.HookBusy,InventoryResult.StaleStock=>RodResult.StaleStock,
        InventoryResult.SoldOut=>RodResult.SoldOut,InventoryResult.InsufficientCoins=>RodResult.InsufficientCoins,
        InventoryResult.NoSpace or InventoryResult.LayoutTooComplex or InventoryResult.LimitReached=>RodResult.NoSpace,
        _=>RodResult.NotOwned
    };
}
