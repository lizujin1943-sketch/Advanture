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
        this.config=config;Inventory=new InventorySystem(players.Length);attributes=new CharacterStats[players.Length];
        merchants=new MerchantSystem(config);
        for(int i=0;i<players.Length;i++)
        {
            Recalculate(i);Refresh(ref players[i]);
            players[i].Vitals=CharacterSystem.Spawn(attributes[i]);players[i].Stamina=attributes[i].StaminaMax;
        }
    }
    internal EquipmentSystem(TackleState state)
    {
        config=state.Config;Inventory=new InventorySystem(state.Inventory);merchants=new MerchantSystem(config,state.Merchants);
        attributes=new CharacterStats[state.Inventory.PlayerCount];for(int i=0;i<attributes.Length;i++)Recalculate(i);
    }
    internal TackleState Capture()=>new TackleState(config,Inventory.Capture(),merchants.Capture());
    internal RodDefinition? Equipped(Fisher player)
    {var item=Inventory.Equipped(player.Slot,EquipmentSlot.RightHand);return item.HasValue?RodCatalog.Get(item.Value.Type):null;}
    internal CharacterStats Attributes(Fisher player)=>attributes[player.Slot];
    internal int Maximum(Fisher player)=>Attributes(player).StaminaMax;
    internal SwimRingDefinition? Ring(Fisher player)
    {var item=Inventory.Equipped(player.Slot,EquipmentSlot.SwimRing);return item.HasValue?SwimRingCatalog.Get(item.Value.Type):null;}
    private void Recalculate(int owner)
    {
        var modifiers=CharacterEquipment.Slots.Select(slot=>Inventory.Equipped(owner,slot)).Where(item=>item.HasValue).Select(item=>item!.Value.Definition.Modifiers);
        attributes[owner]=CharacterStats.Calculate(config.Character,config.StaminaBase,modifiers);
    }
    internal void Advance(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,ulong step)
        =>merchants.Advance(map,tiles,players,step);
    internal void Apply(IslandMap map,IReadOnlyList<PlatformTile> tiles,ref Fisher player,HookInput command,ref int coins)
    {
        if(command.Inventory.Action!=InventoryAction.None && command.Inventory.RequestId>player.LastInventoryRequest)
        {
            player.LastInventoryRequest=command.Inventory.RequestId;
            player.LastInventoryResult=Transact(map,tiles,ref player,command.Inventory,ref coins);
            if(player.LastInventoryResult==InventoryResult.Success)Recalculate(player.Slot);
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
            var result=wrongKind?InventoryResult.InvalidItem:Transact(map,tiles,ref player,
                new InventoryCommand(action,target,batch:command.RodBatch,requestId:command.RodRequestId),ref coins);
            player.LastRodResult=LegacyResult(result);
            if(result==InventoryResult.Success)Recalculate(player.Slot);
        }
        Refresh(ref player);
    }
    private void Refresh(ref Fisher player)
    {
        player.EquippedRod=Inventory.Equipped(player.Slot,EquipmentSlot.RightHand)?.Id??0;
        PersonalMaterialInventory.Refresh(ref player,Inventory);
        // 换装只裁切上限，不补满。自然休息仍由每帧的 StaminaSystem 负责。
        CharacterSystem.Clamp(ref player.Vitals,ref player.Stamina,Attributes(player));
    }
    private InventoryResult Transact(IslandMap map,IReadOnlyList<PlatformTile> tiles,ref Fisher player,InventoryCommand command,ref int coins)
    {
        if(player.Unavailable || player.Vitals.Downed)return InventoryResult.Disabled;
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
            var stock=merchant.Stock;
            if(command.Target>=stock.Count || stock[command.Target].Sold)return InventoryResult.SoldOut;
            var offer=stock[command.Target];bool equip=command.Action==InventoryAction.BuyAndEquip;
            if(equip && offer.Kind==ItemKind.Rod && player.Phase!=HookPhase.Ready)return InventoryResult.HookBusy;
            int previousCoins=player.Coins;
            var result=Inventory.Purchase(owner,offer.Kind,offer.Type,equip,command.DropOld,point,ref player.Coins);
            if(result==InventoryResult.Success)
            {coins-=previousCoins-player.Coins;merchants.Sell(command.MerchantId,command.Target);}
            return result;
        }
        var item=Inventory.Find(command.Target)??default;
        bool changesRod=(command.Action==InventoryAction.Unequip && command.Target==(int)EquipmentSlot.RightHand) ||
            ((command.Action==InventoryAction.Equip || command.Action==InventoryAction.PickupAndEquip) && item.Kind==ItemKind.Rod);
        if(changesRod && player.Phase!=HookPhase.Ready)return InventoryResult.HookBusy;
        return command.Action switch {
            InventoryAction.Move=>Inventory.Move(owner,command.Target,command.X,command.Y,command.Rotated),
            InventoryAction.Split=>Inventory.Split(owner,command.Target,command.Amount,command.X,command.Y),
            InventoryAction.Drop=>Inventory.Drop(owner,command.Target,command.Amount,point),
            InventoryAction.Pickup=>Inventory.Pickup(owner,command.Target,point,out _),
            InventoryAction.PickupAndEquip=>Inventory.PickupAndEquip(owner,command.Target,point,command.DropOld),
            InventoryAction.Equip=>Inventory.Equip(owner,command.Target,command.DropOld,point),
            InventoryAction.Unequip=>Inventory.Unequip(owner,(EquipmentSlot)command.Target,point),
            _=>InventoryResult.InvalidItem
        };
    }
    private static RodResult LegacyResult(InventoryResult result)=>result switch {
        InventoryResult.Success=>RodResult.Success,InventoryResult.TooFar=>RodResult.TooFar,
        InventoryResult.HookBusy=>RodResult.HookBusy,InventoryResult.StaleStock=>RodResult.StaleStock,
        InventoryResult.SoldOut=>RodResult.SoldOut,InventoryResult.InsufficientCoins=>RodResult.InsufficientCoins,
        InventoryResult.NoSpace or InventoryResult.LayoutTooComplex or InventoryResult.LimitReached=>RodResult.NoSpace,
        _=>RodResult.NotOwned
    };
}
