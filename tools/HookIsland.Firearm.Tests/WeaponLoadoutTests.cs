using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.FirearmTests;
internal static partial class Program
{
    private static IslandMap WeaponMap(int restockSeconds=600)
    {
        var basis=IslandMap.CreatePlatform();
        return new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,
            new FishingConfig(melee:new MeleeConfig(),firearms:new FirearmConfig(),startingCoins:3000,
                fishingRodsEnabled:false,restockSeconds:restockSeconds));
    }
    private static HookInput SelectWeapon(EquipmentSlot slot,uint id)=>new(0,0,
        inventory:new InventoryCommand(InventoryAction.SelectWeapon,(int)slot,requestId:id));
    private static HookState FillWeaponBackpacks(IDeterministicSimulation<HookState> model)
    {
        // Test fixture uses the public inventory transaction and conserves earned material counters.
        var state=model.Capture();var inventory=new InventorySystem(state.Tackle!.Inventory);var players=state.Players.ToArray();
        for(int i=0;i<players.Length;i++)
        {
            var size=state.Tackle.Inventory.Size(i);int wood=size.Width*size.Height*InventoryCatalog.MaterialStackLimit;
            Require(inventory.CreditMaterial(i,ItemKind.Wood,wood,new Point(players[i].X,players[i].Z))==wood,"Could not fill backpack fixture.");
            players[i].WoodEarned+=wood;PersonalMaterialInventory.Refresh(ref players[i],inventory);
        }
        return new HookState(state.Steps,players,state.Fish,state.Coins,state.Tiles,state.Driftwood,
            new TackleState(state.Tackle.Config,inventory.Capture(),state.Tackle.Merchants),state.Enemies,state.RoomRules,
            state.Treasure,state.Commissions,state.Ocean,state.Finale,state.Crabs,state.Boss);
    }
    private static IEnumerable<(string,Action)> WeaponCases()
    {
        yield return ("loadout_formal_solo_and_four_players_start_with_equipped_pistol_without_rods",()=>{
            var map=IslandMap.Decode(System.IO.File.ReadAllBytes("samples/TacticalGame.Unity/Assets/HookIsland/Data/PlatformMap.bytes"));
            Require(!map.Fishing!.FishingRodsEnabled && map.Fishing.Firearms.Enabled,"Formal map grants rods or disables guns.");
            foreach(int count in new[]{1,4})
            {
                var model=FreshGame(count,map);var state=model.Capture();var inventory=state.Tackle!.Inventory;
                Require(!inventory.Items.Any(i=>i.Kind==ItemKind.Rod || i.Location==ItemLocation.Backpack),"Starter weapon occupies backpack or rod exists.");
                foreach(var p in state.Players)
                {
                    Require(inventory.ActiveItem(p.Slot)?.Id==inventory.Equipped(p.Slot,EquipmentSlot.Sidearm)?.Id && inventory.ActiveItem(p.Slot)?.Kind==ItemKind.Firearm,"Pistol is not active at spawn.");
                    Require(inventory.Equipped(p.Slot,EquipmentSlot.MeleeWeapon)?.Kind==ItemKind.Weapon && !inventory.Equipped(p.Slot,EquipmentSlot.PrimaryWeapon).HasValue,"Wrong category starter kit.");
                    Require(p.EquippedRod==0 && p.SelectedQuickSlot==-1 && p.Stamina==100*StaminaSystem.UnitsPerPoint,"No-rod spawn retained rod attributes or quickbar selection.");
                }
                var codec=new HookGame();model.Restore(codec.Decode(codec.Encode(state)));
                Require(model.Capture().Tackle!.Inventory.ActiveItems.SequenceEqual(inventory.ActiveItems),"Restored starter changed active selection.");
            }
        });
        yield return ("loadout_rod_free_shop_stays_rod_free_after_restock_and_legacy_request",()=>{
            var map=WeaponMap(1);var model=FreshGame(1,map);
            for(int i=0;i<4;i++)
            {
                Require(model.Capture().Tackle!.Merchants.All(m=>m.Stock.All(v=>v.Kind!=ItemKind.Rod)),"Shop generated a rod.");Wait(model,30);
            }
            Step(model,new HookInput(0,0,rodAction:RodAction.Equip,rodTarget:1,rodRequestId:1));
            Require(model.Capture().Players[0].LastRodResult==RodResult.Disabled && model.Capture().Tackle!.Inventory.Items.All(i=>i.Kind!=ItemKind.Rod),"Legacy rod action bypassed disabled rods.");
            new HookGame().Decode(new HookGame().Encode(model.Capture()));
        });
        yield return ("loadout_full_bag_switch_and_failed_same_category_purchase_keep_resources_and_receipts",()=>{
            var model=FreshGame(1,WeaponMap());Wait(model,10);model.Restore(FillWeaponBackpacks(model));
            Step(model,Buy(FirearmCatalog.OfferOffset+1,1,true));Wait(model,FirearmCatalog.Get(1).DrawTicks);
            var bought=model.Capture();Require(bought.Players[0].LastInventoryResult==InventoryResult.Success && bought.Players[0].Coins==2100,"Empty primary slot required bag space.");
            var failed=Buy(FirearmCatalog.OfferOffset+2,2,true);Step(model,failed);var after=model.Capture();
            Require(after.Players[0].LastInventoryResult==InventoryResult.NoSpace && after.Players[0].Coins==2100,"Full bag replacement did not fail atomically.");
            Require(InventoryStateCodec.Encode(bought.Tackle!.Inventory).SequenceEqual(InventoryStateCodec.Encode(after.Tackle!.Inventory)) && bought.Tackle.Merchants[0].FirearmStock.SequenceEqual(after.Tackle.Merchants[0].FirearmStock),"Failed purchase changed inventory or shelf.");
            uint request=3;
            foreach(var slot in new[]{EquipmentSlot.Sidearm,EquipmentSlot.MeleeWeapon,EquipmentSlot.PrimaryWeapon})
            {
                Step(model,SelectWeapon(slot,request++));Wait(model,30);
                Require(model.Capture().Players[0].LastInventoryResult==InventoryResult.Success && model.Capture().Tackle!.Inventory.ActiveItem(0)?.Definition.Slot==slot,"Full bag switch failed.");
                Require(after.Tackle.Inventory.Items.SequenceEqual(model.Capture().Tackle!.Inventory.Items),"Switch moved a weapon into the full bag.");
            }
            var codec=new HookGame();var restored=FreshGame(1,WeaponMap());restored.Restore(codec.Decode(codec.Encode(model.Capture())));
            Step(restored,failed);Require(restored.Capture().Players[0].LastInventoryRequest==request-1 && restored.Capture().Players[0].Coins==2100,"Stale failed request replayed after restore.");
        });
        yield return ("loadout_empty_slot_keeps_reload_and_successful_melee_switch_cancels_without_refill",()=>{
            var model=FreshGame(1,WeaponMap());Wait(model,10);var p=model.Capture().Players[0];
            Step(model,new HookInput(0,0,p.X,p.Z-14000,fire:true));Wait(model,6);
            Step(model,new HookInput(0,0,reload:true));int ammo=Gun(model).LoadedAmmo;
            Step(model,SelectWeapon(EquipmentSlot.PrimaryWeapon,1));
            Require(model.Capture().Players[0].Firearm.Reloading && Gun(model).LoadedAmmo==ammo,"Failed switch cancelled/refilled reload.");
            Step(model,SelectWeapon(EquipmentSlot.MeleeWeapon,2));
            Require(!model.Capture().Players[0].Firearm.Reloading && MeleeRules.Equipped(model.Capture(),model.Capture().Players[0])!=null,"Successful melee switch did not cancel reload.");
            Step(model,SelectWeapon(EquipmentSlot.Sidearm,3));var saved=model.Capture();
            Require(saved.Players[0].Firearm.DrawTicks==FirearmCatalog.Get(0).DrawTicks && Gun(model).LoadedAmmo==ammo,"Returning pistol skipped draw or refilled ammo.");
            var codec=new HookGame();var copy=FreshGame(1,WeaponMap());copy.Restore(codec.Decode(codec.Encode(saved)));
            for(int n=0;n<60;n++){Step(model,SelectWeapon(EquipmentSlot.Sidearm,3));Step(copy,SelectWeapon(EquipmentSlot.Sidearm,3));}
            Require(codec.Encode(model.Capture()).SequenceEqual(codec.Encode(copy.Capture())) && Gun(model).LoadedAmmo==ammo && !model.Capture().Players[0].Firearm.Busy,"Switch retry or restore restarted draw/reload.");
        });
    }
}
