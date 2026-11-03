using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.FirearmTests;
internal static partial class Program
{
    private static IDeterministicSimulation<HookState> FreshGame(int players,IslandMap? map=null)
    {
        var model=new HookGame(false).Create();
        var participants=Enumerable.Range(0,players).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray();
        model.Initialize(new SimulationInitialization(new SessionDescriptor(SessionId,1,30,participants),new ByteString((map??Map()).Encode())));
        return model;
    }
    private static HookInput SelectSlot(int slot,uint request)=>new(0,0,inventory:new InventoryCommand(InventoryAction.SelectQuickSlot,slot,requestId:request));
    private static IEnumerable<(string,Action)> StarterCases()
    {
        yield return ("starter_pistol_each_player_has_one_loaded_gun_and_can_use_slot_six",()=>{
            foreach(int count in new[]{1,4})
            {
                var model=FreshGame(count);var state=model.Capture();var inventory=state.Tackle!.Inventory;
                Require(inventory.Items.Count(i=>i.Kind==ItemKind.Firearm)==count,"New game must give exactly one pistol per player");
                foreach(var player in state.Players)
                {
                    var gun=inventory.Items.Single(i=>i.Owner==player.Slot && i.Kind==ItemKind.Firearm);
                    Require(gun.Type==0 && gun.Location==ItemLocation.Backpack && gun.LoadedAmmo==7,"Starter pistol must be stored with a full magazine");
                    Require(QuickbarRules.Resolve(inventory,player.Slot,5)?.Id==gun.Id,"Slot six is not bound to the owner's pistol");
                    Require(inventory.Equipped(player.Slot,EquipmentSlot.RightHand)?.Kind==ItemKind.Rod && player.SelectedQuickSlot==0,"New game must still start holding the rod");
                    Require(QuickbarRules.Resolve(inventory,player.Slot,0)?.Kind==ItemKind.Rod && QuickbarRules.Resolve(inventory,player.Slot,1)?.Kind==ItemKind.Weapon,"Starter gun replaced an existing tool binding");
                    for(int type=0;type<3;type++)Require(inventory.Quickbar[player.Slot*9+2+type].Kind==ItemKind.Food && inventory.Quickbar[player.Slot*9+2+type].Type==type,"Starter gun replaced a food binding");
                }
                Require(state.Coins==0 && state.Players.All(p=>p.Coins==0) && inventory.SpentCoins.All(n=>n==0) && !inventory.Items.Any(i=>i.Kind==ItemKind.Ammunition),"Starter kit charged money or granted spare ammunition");
                var codec=new HookGame();codec.Decode(codec.Encode(state)); // Includes backpack overlap and ammunition conservation validation.
                Step(model,Enumerable.Range(0,count).Select(_=>SelectSlot(6,1)).ToArray());Wait(model,9);
                Require(model.Capture().Players.All(p=>p.LastInventoryResult==InventoryResult.Success && p.SelectedQuickSlot==5),"Starter pistol cannot be selected through the quickbar");
                var events=Step(model,model.Capture().Players.Select(p=>new HookInput(0,0,p.X,p.Z-14000,fire:true)).ToArray());
                Require(events.Count(e=>e.Kind==HookGameplayEventKind.FirearmShot)==count && model.Capture().Tackle!.Inventory.Items.Where(i=>i.Kind==ItemKind.Firearm).All(i=>i.LoadedAmmo==6),"Starter ammunition cannot be fired normally");
                codec.Decode(codec.Encode(model.Capture()));
            }
        });
        yield return ("starter_pistol_restore_keeps_spent_ammo_and_dropped_gun_without_regifting",()=>{
            var model=FreshGame(1);Step(model,SelectSlot(6,1));Wait(model,9);
            var player=model.Capture().Players[0];Step(model,new HookInput(0,0,player.X,player.Z-14000,fire:true));Wait(model,6);
            int id=model.Capture().Tackle!.Inventory.Items.Single(i=>i.Kind==ItemKind.Firearm).Id;
            Step(model,SelectSlot(1,2));Step(model,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Drop,id,amount:1,requestId:3)));
            var saved=model.Capture();var dropped=saved.Tackle!.Inventory.Items.Single(i=>i.Id==id);
            Require(dropped.Location==ItemLocation.Ground && dropped.LoadedAmmo==6 && saved.Tackle.Inventory.Quickbar[5].Empty,"Dropping starter pistol did not preserve ammo or clear its binding");
            var codec=new HookGame();var bytes=codec.Encode(saved);var restored=FreshGame(1);
            for(int n=0;n<2;n++)
            {
                restored.Restore(codec.Decode(bytes));
                Require(codec.Encode(restored.Capture()).SequenceEqual(bytes),"Restore changed starter items, spent ammo or quickbar");
                Require(restored.Capture().Tackle!.Inventory.Items.Count(i=>i.Kind==ItemKind.Firearm)==1,"Restore gave another starter pistol");
            }
            Step(model);Step(restored);Require(codec.Encode(model.Capture()).SequenceEqual(codec.Encode(restored.Capture())),"Starter item restore diverges on the next tick");
        });
        yield return ("starter_pistol_disabled_game_does_not_receive_unusable_firearm",()=>{
            var model=FreshGame(4,IslandMap.CreatePlatform());var inventory=model.Capture().Tackle!.Inventory;
            Require(!inventory.Items.Any(i=>i.Kind==ItemKind.Firearm) && Enumerable.Range(0,4).All(i=>inventory.Quickbar[i*9+5].Empty),"Disabled firearm mode received starter pistols");
            var codec=new HookGame();codec.Decode(codec.Encode(model.Capture()));
        });
        yield return ("starter_pistol_legacy_paid_inventory_restores_without_a_new_gift",()=>{
            // Recreate the previous release's initial kit and normal paid gun purchase.
            foreach(bool purchased in new[]{false,true})
            {
                var model=FreshGame(1);var state=model.Capture();var players=state.Players.ToArray();
                var inventory=new InventorySystem(1,true);int coins=500;
                if(purchased)Require(inventory.Purchase(0,ItemKind.Firearm,0,false,false,new Point(players[0].X,players[0].Z),ref coins)==InventoryResult.Success,"Legacy paid gun fixture did not buy successfully");
                players[0].CoinsEarned=500;players[0].Coins=coins;
                var legacy=new HookState(state.Steps,players,state.Fish,coins,state.Tiles,state.Driftwood,new TackleState(state.Tackle!.Config,inventory.Capture(),state.Tackle.Merchants),state.Enemies,state.RoomRules,state.Treasure,state.Commissions,state.Ocean,state.Finale);
                var codec=new HookGame();var bytes=codec.Encode(legacy);model.Restore(codec.Decode(bytes));
                Require(codec.Encode(model.Capture()).SequenceEqual(bytes) && model.Capture().Tackle!.Inventory.Items.Count(i=>i.Kind==ItemKind.Firearm)==(purchased?1:0),"Loading the prior kit changed inventory or granted a pistol");
            }
        });
    }
}
