using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.InventoryTests;

internal static partial class Program
{
    private static void FoodHealth(FrameSync.Abstractions.IDeterministicSimulation<HookState> game,int health)
    {
        var s=game.Capture();var p=s.Players.ToArray();p[0].Vitals.Health=health;
        game.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale));
    }
    private static HookGameplayEvent[] FoodStep(IDeterministicSimulation<HookState> game,params HookInput[] commands)
    {
        var s=game.Capture();return game.Step(new FrameContext(s.Steps+1,s.Steps+1,HookGame.Rate),s.Players.Select((p,i)=>
            new SimulationInput(new Participant(p.Slot,new Guid(i+1,0,0,new byte[8])),new ByteString((i<commands.Length?commands[i]:default).Encode()))).ToArray())
            .Select(e=>HookGameplayEventCodec.Decode(e.Span)).ToArray();
    }
    private static IEnumerable<(string,Action)> FoodGameCases()
    {
        yield return ("food_transfer_split_consume_keeps_purchase_ledger_and_emits_once",()=>{
            var game=Game(2,2000);for(uint n=1;n<=3;n++)GameStep(game,Command(InventoryAction.Buy,9,n,batch:1));
            int id=game.Capture().Tackle!.Inventory.Items.Single(i=>i.Kind==ItemKind.Food).Id;
            GameStep(game,Command(InventoryAction.Split,id,4,amount:1,x:3,y:3));
            var split=game.Capture().Tackle!.Inventory.Items.Single(i=>i.Kind==ItemKind.Food && i.Id!=id);
            GameStep(game,Command(InventoryAction.Drop,split.Id,5,amount:1));
            GameStep(game,default,Command(InventoryAction.Pickup,split.Id,1));
            var s=game.Capture();var players=s.Players.ToArray();players[1].Vitals.Health=91;
            game.Restore(new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle));
            int received=s.Tackle!.Inventory.Items.Single(i=>i.Owner==1 && i.Kind==ItemKind.Food).Id;
            var use=Command(InventoryAction.Use,received,2);var events=FoodStep(game,default,use);
            s=game.Capture();var heal=events.Single(e=>e.Kind==HookGameplayEventKind.FoodConsumed);
            Require(heal.Slot==1 && heal.Amount==9 && heal.ItemType==1,"Healing event did not report clamped amount and consumer.");
            Require(s.Players[1].Vitals.Health==100 && s.Tackle!.Inventory.SpentCoins.SequenceEqual(new[]{600,0}) && s.Tackle.Inventory.ConsumedFoods[1]==1,"Trade moved purchase debt or missed consumption.");
            var codec=new HookGame();game.Restore(codec.Decode(codec.Encode(s)));
            Require(FoodStep(game,default,use).All(e=>e.Kind!=HookGameplayEventKind.FoodConsumed),"Restore/duplicate replayed eating.");
            Require(game.Capture().Tackle!.Inventory.Items.Single(i=>i.Id==id).Count==2,"Teammate consumption removed buyer's remaining stack.");
        });
        yield return ("food_water_hook_downed_melee_and_rescue_reject_without_consumption",()=>{
            foreach(string reason in new[]{"water","hook","downed","melee","rescue"})
            {
                var game=Game(melee:reason=="melee");GameStep(game,Command(InventoryAction.Buy,8,1,batch:1));FoodHealth(game,30);
                if(reason=="hook")GameStep(game,new HookInput(0,0,-10000,0,true));
                if(reason=="melee")
                {
                    int pan=game.Capture().Tackle!.Inventory.Items.Single(i=>i.Owner==0 && i.Kind==ItemKind.Weapon).Id;
                    GameStep(game,Command(InventoryAction.Equip,pan,2));GameStep(game,new HookInput(0,0,-10000,0,true));
                }
                var s=game.Capture();var p=s.Players.ToArray();int id=s.Tackle!.Inventory.Items.Single(i=>i.Kind==ItemKind.Food).Id;
                if(reason=="water"){p[0].X=-4000;p[0].Swimming.InWater=true;}
                if(reason=="downed"){p[0].Vitals.Health=0;p[0].Revival.DownedTicks=1;}
                if(reason=="rescue"){p[1].Vitals.Health=0;p[1].Revival.DownedTicks=1;p[1].Revival.Rescuer=1;p[1].Revival.RescueTicks=1;}
                game.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle));
                var events=FoodStep(game,Command(InventoryAction.Use,id,3));s=game.Capture();
                Require(s.Players[0].LastInventoryResult!=InventoryResult.Success && s.Players[0].FoodCooldownTicks==0,reason+" allowed eating.");
                Require(s.Tackle!.Inventory.Items.Any(i=>i.Id==id) && !events.Any(e=>e.Kind==HookGameplayEventKind.FoodConsumed),reason+" spent food or emitted a meal.");
            }
            var walking=Game();GameStep(walking,Command(InventoryAction.Buy,8,1,batch:1));FoodHealth(walking,30);
            var before=walking.Capture();int food=before.Tackle!.Inventory.Items.Single(i=>i.Kind==ItemKind.Food).Id;
            GameStep(walking,new HookInput(-1000,0,inventory:new InventoryCommand(InventoryAction.Use,food,requestId:2)));
            Require(walking.Capture().Players[0].Vitals.Health==60 && walking.Capture().Players[0].X<before.Players[0].X,"Walking prevented food use or movement.");
        });
        yield return ("food_stock_sells_out_atomically_and_restock_replaces_quantities",()=>{
            var game=Game(2,10000);var s=game.Capture();var p=s.Players.ToArray();p[1].Coins=p[1].CoinsEarned=10000;
            game.Restore(new HookState(s.Steps,p,s.Fish,20000,s.Tiles,s.Driftwood,s.Tackle));
            GameStep(game,Command(InventoryAction.Buy,10,1,batch:1),Command(InventoryAction.Buy,10,1,batch:1));
            GameStep(game,Command(InventoryAction.Buy,10,2,batch:1),Command(InventoryAction.Buy,10,2,batch:1));s=game.Capture();
            Require(s.Players.All(v=>v.Coins==9500 && v.LastInventoryResult==InventoryResult.SoldOut) && s.Tackle!.Merchants[0].FoodStock[2]==0,"Sold-out race spent extra coins.");
            GameStep(game,Command(InventoryAction.Buy,0,3,batch:1));
            Require(game.Capture().Tackle!.Merchants[0].FoodStock[2]==0,"Equipment sale replenished food.");
            s=game.Capture();var shelf=s.Tackle!.Merchants[0];ulong last=shelf.NextRestock-1;
            game.Restore(new HookState(last,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle));GameStep(game);
            Require(game.Capture().Tackle!.Merchants[0].FoodStock.SequenceEqual(new[]{8,4,2}),"Restock did not reset food quantities.");
            new HookGame().Decode(new HookGame().Encode(game.Capture()));
        });
        yield return ("food_game_buy_use_clamps_health_and_preserves_receipt_cooldown_on_save",()=>{
            foreach(var example in new[]{(0,10,40,100),(1,10,70,200),(2,10,100,500)})
            {
                var game=Game(2,2000);var before=game.Capture();
                GameStep(game,Command(InventoryAction.Buy,8+example.Item1,1,batch:1));
                var s=game.Capture();Require(s.Players[0].LastInventoryResult==InventoryResult.Success,"Food shelf purchase rejected.");
                Require(s.Players[0].Coins==2000-example.Item4 && s.Tackle!.Merchants[0].FoodStock[example.Item1]==before.Tackle!.Merchants[0].FoodStock[example.Item1]-1,"Wrong shelf/wallet debit.");
                var food=s.Tackle!.Inventory.Items.First(i=>i.Kind==ItemKind.Food);FoodHealth(game,example.Item2);
                var use=Command(InventoryAction.Use,food.Id,2);GameStep(game,use);s=game.Capture();
                Require(s.Players[0].Vitals.Health==example.Item3 && s.Players[0].FoodCooldownTicks==240,"Wrong healing or cooldown.");
                Require(!s.Tackle!.Inventory.Items.Any(i=>i.Kind==ItemKind.Food) && s.Tackle.Inventory.ConsumedFoods[example.Item1]==1,"Consumption ledger missing.");
                var codec=new HookGame();var saved=codec.Encode(s);game.Restore(codec.Decode(saved));
                Require(saved.SequenceEqual(codec.Encode(game.Capture())),"Food save state changed on restore.");
                GameStep(game,use);s=game.Capture();Require(s.Players[0].Vitals.Health==example.Item3 && s.Tackle!.Inventory.ConsumedFoods[example.Item1]==1,"Duplicate healed/consumed again.");
            }
        });
        yield return ("food_game_full_health_failed_request_never_retries_after_damage",()=>{
            var game=Game();GameStep(game,Command(InventoryAction.Buy,8,1,batch:1));
            var food=game.Capture().Tackle!.Inventory.Items.First(i=>i.Kind==ItemKind.Food);
            var use=Command(InventoryAction.Use,food.Id,2);GameStep(game,use);
            Require(game.Capture().Players[0].LastInventoryResult==InventoryResult.HealthFull && game.Capture().Players[0].FoodCooldownTicks==0,"Full health use spent cooldown.");
            FoodHealth(game,20);GameStep(game,use);
            Require(game.Capture().Players[0].Vitals.Health==20 && game.Capture().Tackle!.Inventory.Items.Any(i=>i.Id==food.Id),"Failed use retried after damage.");
            GameStep(game,Command(InventoryAction.Use,food.Id,3));Require(game.Capture().Players[0].Vitals.Health==50,"Fresh intentional use failed.");
        });
        yield return ("food_game_shared_cooldown_does_not_consume_second_type",()=>{
            var game=Game(2,2000);GameStep(game,Command(InventoryAction.Buy,8,1,batch:1));GameStep(game,Command(InventoryAction.Buy,9,2,batch:1));
            var foods=game.Capture().Tackle!.Inventory.Items.Where(i=>i.Kind==ItemKind.Food).ToArray();FoodHealth(game,1);
            GameStep(game,Command(InventoryAction.Use,foods[0].Id,3));GameStep(game,Command(InventoryAction.Use,foods[1].Id,4));
            Require(game.Capture().Players[0].LastInventoryResult==InventoryResult.CoolingDown && game.Capture().Players[0].Vitals.Health==31,"Second food bypassed shared cooldown.");
            for(int n=0;n<240;n++)GameStep(game);
            GameStep(game,Command(InventoryAction.Use,foods[1].Id,4));Require(game.Capture().Players[0].Vitals.Health==31,"Failed cooldown intent automatically replayed.");
            GameStep(game,Command(InventoryAction.Use,foods[1].Id,5));Require(game.Capture().Players[0].Vitals.Health==91,"Cooldown failed to expire.");
        });
        yield return ("food_quickbar_bind_select_and_restore_does_not_copy_or_consume",()=>{
            var game=Game();GameStep(game,Command(InventoryAction.Buy,8,1,batch:1));
            int id=game.Capture().Tackle!.Inventory.Items.First(i=>i.Kind==ItemKind.Food).Id;
            GameStep(game,Command(InventoryAction.BindQuickSlot,id,2,amount:9));GameStep(game,Command(InventoryAction.SelectQuickSlot,9,3));
            var s=game.Capture();Require(s.Players[0].SelectedQuickSlot==8 && s.Tackle!.Inventory.Quickbar[8].Kind==ItemKind.Food && s.Tackle.Inventory.Quickbar[2].Empty,"Binding did not move to slot nine.");
            Require(s.Tackle!.Inventory.Items.Single(i=>i.Id==id).Count==1 && s.Players[0].FoodCooldownTicks==0,"Selecting consumed an item.");
            var codec=new HookGame();game.Restore(codec.Decode(codec.Encode(s)));
            GameStep(game,new HookInput(0,0,10000,0,true));Require(game.Capture().Players[0].Phase==HookPhase.Ready,"Selected food accidentally fired equipped rod.");
        });
    }
    private static IEnumerable<(string,Action)> FoodCases()
    {
        yield return ("food_purchase_stacks_five_and_charges_only_buyer",()=>{
            var inventory=new InventorySystem(2);int coins=2000;var food=(ItemKind)8;
            foreach(var pair in new[]{(0,100),(1,200),(2,500)})
            {
                int before=coins;
                Require(inventory.Purchase(1,food,pair.Item1,false,false,Ground,ref coins)==InventoryResult.Success,"Food purchase rejected.");
                Require(coins==before-pair.Item2,"Wrong food price.");
            }
            for(int n=0;n<5;n++)Require(inventory.Purchase(1,food,0,false,false,Ground,ref coins)==InventoryResult.Success,"Repeated food purchase failed.");
            Require(inventory.Capture().Items.Where(i=>i.Kind==food && i.Type==0).Select(i=>i.Count).SequenceEqual(new[]{5,1}),"Food did not fill five-unit stacks.");
            Require(coins==700 && inventory.Capture().SpentCoins.SequenceEqual(new[]{0,1300}),"Purchase touched another wallet or lost spend ledger.");
            InventoryStateCodec.Decode(InventoryStateCodec.Encode(inventory.Capture()));
        });
        yield return ("food_full_bag_fills_existing_stack_then_rejects_without_charge",()=>{
            var inventory=new InventorySystem(1);int coins=5000;var food=(ItemKind)8;
            Require(inventory.Purchase(0,food,0,false,false,Ground,ref coins)==InventoryResult.Success,"Food purchase rejected.");
            inventory.CreditMaterial(0,ItemKind.Wood,15*99,Ground);
            for(int n=0;n<4;n++)Require(inventory.Purchase(0,food,0,false,false,Ground,ref coins)==InventoryResult.Success,"Existing food stack not filled in full bag.");
            var before=InventoryStateCodec.Encode(inventory.Capture());
            Require(inventory.Purchase(0,food,0,false,false,Ground,ref coins)==InventoryResult.NoSpace && coins==4500,"Overflow charged or was accepted.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())),"Failed purchase changed inventory.");
        });
    }
}
