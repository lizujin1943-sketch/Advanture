using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.InventoryTests;

internal static partial class Program
{
    private static void HealingFixture(IDeterministicSimulation<HookState> game,Action<Fisher[]> change)
    {
        var s=game.Capture();var players=s.Players.ToArray();change(players);
        game.Restore(new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,
            s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,s.Boss));
    }
    private static HookGameplayEvent[] HealingStep(IDeterministicSimulation<HookState> game,params HookInput[] commands)
    {
        var s=game.Capture();return game.Step(new FrameContext(s.Steps+1,s.Steps+1,HookGame.Rate),s.Players.Select((p,i)=>
            new SimulationInput(new Participant(p.Slot,new Guid(i+1,0,0,new byte[8])),new ByteString((i<commands.Length?commands[i]:default).Encode()))).ToArray())
            .Select(e=>HookGameplayEventCodec.Decode(e.Span)).ToArray();
    }
    private static IEnumerable<(string,Action)> HealingCases()
    {
        yield return ("healing_new_round_grants_three_personal_charges_in_solo_and_four_player_games",()=>{
            foreach(int count in new[]{1,4})
            {
                var game=Game(count);var initial=game.Capture();
                Require(initial.Players.All(p=>p.HealingCharges==3),"Starting charges missing.");
                HealingFixture(game,p=>p[0].Vitals.Health=20);
                var events=HealingStep(game,Command(InventoryAction.Heal,0,1));var s=game.Capture();
                Require(s.Players[0].Vitals.Health==60 && s.Players[0].HealingCharges==2,"Wrong charge transaction.");
                Require(s.Players.Skip(1).All(p=>p.HealingCharges==3 && p.Vitals.Health==100),"Healing changed a teammate.");
                Require(s.Coins==initial.Coins && InventoryStateCodec.Encode(s.Tackle!.Inventory).SequenceEqual(InventoryStateCodec.Encode(initial.Tackle!.Inventory)),"Healing consumed money or inventory.");
                Require(events.Count(e=>e.Kind==HookGameplayEventKind.Healed && e.Amount==40 && e.Slot==0 && e.Value==2)==1,"Missing confirmed healing result.");
            }
        });
        yield return ("healing_uses_current_maximum_health_and_clamps_actual_event_amount",()=>{
            var game=Game(config:new FishingConfig(character:new CharacterConfig(health:201)));
            HealingFixture(game,p=>p[0].Vitals.Health=20);
            var heal=HealingStep(game,Command(InventoryAction.Heal,0,1)).Single(e=>e.Kind==HookGameplayEventKind.Healed);
            Require(heal.Amount==80 && game.Capture().Players[0].Vitals.Health==100,"Healing was not 40 percent of max health rounded down.");
            HealingFixture(game,p=>p[0].Vitals.Health=195);
            heal=HealingStep(game,Command(InventoryAction.Heal,0,2)).Single(e=>e.Kind==HookGameplayEventKind.Healed);
            Require(heal.Amount==6 && game.Capture().Players[0].Vitals.Health==201 && game.Capture().Players[0].HealingCharges==1,"Overheal was not capped or did not spend one charge.");
        });
        yield return ("healing_three_uses_exhaust_and_failed_fourth_use_does_not_mutate_health",()=>{
            var game=Game();
            for(uint n=1;n<=3;n++)
            {
                HealingFixture(game,p=>p[0].Vitals.Health=10);
                GameStep(game,Command(InventoryAction.Heal,0,n));
                Require(game.Capture().Players[0].Vitals.Health==50 && game.Capture().Players[0].HealingCharges==3-n,"Wrong per-use consumption.");
            }
            HealingFixture(game,p=>p[0].Vitals.Health=10);
            var events=HealingStep(game,Command(InventoryAction.Heal,0,4));var after=game.Capture().Players[0];
            Require(after.LastInventoryResult==InventoryResult.NoHealingCharges && after.Vitals.Health==10 && after.HealingCharges==0,"Exhausted heal succeeded.");
            Require(!events.Any(e=>e.Kind==HookGameplayEventKind.Healed),"Rejected heal emitted feedback.");
        });
        yield return ("healing_separate_presses_work_on_consecutive_ticks_but_held_request_never_repeats",()=>{
            var game=Game();var full=Command(InventoryAction.Heal,0,1);GameStep(game,full);
            Require(game.Capture().Players[0].LastInventoryResult==InventoryResult.HealthFull && game.Capture().Players[0].HealingCharges==3,"Full-health request consumed a charge.");
            HealingFixture(game,p=>p[0].Vitals.Health=1);GameStep(game,full);
            Require(game.Capture().Players[0].Vitals.Health==1,"Failed full-health request replayed after damage.");
            var first=Command(InventoryAction.Heal,0,2);GameStep(game,first);
            Require(game.Capture().Players[0].Vitals.Health==41 && game.Capture().Players[0].HealingCharges==2,"First press failed.");
            var second=Command(InventoryAction.Heal,0,3);GameStep(game,second);
            Require(game.Capture().Players[0].Vitals.Health==81 && game.Capture().Players[0].HealingCharges==1,"A cooldown blocked the next press.");
            for(int n=0;n<5;n++)GameStep(game,second);
            Require(game.Capture().Players[0].Vitals.Health==81 && game.Capture().Players[0].HealingCharges==1,"A held/retried request consumed multiple charges.");
            GameStep(game,Command(InventoryAction.Heal,0,4));
            Require(game.Capture().Players[0].Vitals.Health==100 && game.Capture().Players[0].HealingCharges==0,"Third press did not clamp at full health.");
        });
        yield return ("healing_water_hook_downed_melee_and_rescue_reject_but_walking_works",()=>{
            foreach(string reason in new[]{"water","hook","downed","melee","rescue"})
            {
                var game=Game(melee:reason=="melee");HealingFixture(game,p=>p[0].Vitals.Health=30);
                if(reason=="hook")GameStep(game,new HookInput(0,0,-10000,0,true));
                if(reason=="melee")
                {
                    int pan=game.Capture().Tackle!.Inventory.Items.Single(i=>i.Owner==0 && i.Kind==ItemKind.Weapon).Id;
                    GameStep(game,Command(InventoryAction.Equip,pan,1));GameStep(game,new HookInput(0,0,-10000,0,true));
                }
                HealingFixture(game,p=>{
                    if(reason=="water"){p[0].X=-4000;p[0].Swimming.InWater=true;}
                    if(reason=="downed"){p[0].Vitals.Health=0;p[0].Revival.DownedTicks=1;}
                    if(reason=="rescue"){p[1].Vitals.Health=0;p[1].Revival.DownedTicks=1;p[1].Revival.Rescuer=1;p[1].Revival.RescueTicks=1;}
                });
                var events=HealingStep(game,Command(InventoryAction.Heal,0,2));var s=game.Capture();
                Require(s.Players[0].LastInventoryResult!=InventoryResult.Success && s.Players[0].HealingCharges==3,reason+" allowed healing.");
                Require(!events.Any(e=>e.Kind==HookGameplayEventKind.Healed),reason+" emitted a heal.");
            }
            var walking=Game();HealingFixture(walking,p=>p[0].Vitals.Health=30);int before=walking.Capture().Players[0].X;
            GameStep(walking,new HookInput(-1000,0,inventory:new InventoryCommand(InventoryAction.Heal,0,requestId:1)));
            Require(walking.Capture().Players[0].Vitals.Health==70 && walking.Capture().Players[0].X<before,"Walking did not allow healing and movement together.");
        });
        yield return ("healing_paid_respawn_refills_once_and_failed_payment_does_not_refill",()=>{
            foreach(int money in new[]{0,1000})
            {
                var game=Game(money:money);HealingFixture(game,p=>{p[0].HealingCharges=0;p[0].Vitals.Health=0;});
                GameStep(game);var down=game.Capture().Players[0];
                var pay=new HookInput(0,0,revival:new RevivalCommand(respawnRequest:1,deathId:down.Revival.DownedId));GameStep(game,pay);
                var p=game.Capture().Players[0];
                if(money==0){Require(p.HealingCharges==0 && p.Vitals.Downed && p.Revival.LastResult==RespawnResult.InsufficientCoins,"Failed payment refilled charges.");continue;}
                Require(p.HealingCharges==3 && p.Vitals.Health==100 && p.Revival.LastResult==RespawnResult.Success,"Successful payment did not refill.");
                HealingFixture(game,players=>players[0].Vitals.Health=20);GameStep(game,Command(InventoryAction.Heal,0,1));
                GameStep(game,pay);Require(game.Capture().Players[0].HealingCharges==2,"Duplicate paid-respawn request refilled again.");
            }
            var solo=Game(1);HealingFixture(solo,p=>{p[0].HealingCharges=0;p[0].Vitals.Health=0;});GameStep(solo);
            Require(solo.Capture().Players[0].HealingCharges==3 && solo.Capture().Players[0].Revival.LastResult==RespawnResult.Success,"Solo automatic paid revival did not refill.");
        });
        yield return ("healing_teammate_rescue_preserves_remaining_charges",()=>{
            var game=Game();HealingFixture(game,p=>{p[0].HealingCharges=1;p[0].Vitals.Health=0;});
            GameStep(game);
            int duration=game.Capture().Tackle!.Config.Revival.RescueTicks;
            for(int n=0;n<duration;n++)GameStep(game,default,new HookInput(0,0,revival:new RevivalCommand(rescueSlot:0)));
            var p=game.Capture().Players[0];
            Require(p.Vitals.Health==30 && p.HealingCharges==1,"Teammate rescue refilled charges.");
        });
        yield return ("healing_snapshot_preserves_charges_and_success_receipt_without_feedback_replay",()=>{
            var game=Game();HealingFixture(game,p=>p[0].Vitals.Health=10);var use=Command(InventoryAction.Heal,0,1);
            HealingStep(game,use);var codec=new HookGame();var bytes=codec.Encode(game.Capture());game.Restore(codec.Decode(bytes));
            Require(bytes.SequenceEqual(codec.Encode(game.Capture())),"Restore reset charges.");
            Require(!HealingStep(game,use).Any(e=>e.Kind==HookGameplayEventKind.Healed) && game.Capture().Players[0].HealingCharges==2,"Replay consumed another charge.");
            foreach(int invalid in new[]{-1,4})
            {
                var state=codec.Encode(game.Capture());
                bool rejected=false;try{HealingFixture(game,p=>p[0].HealingCharges=invalid);}catch(InvalidDataException){rejected=true;}
                Require(rejected,"Invalid saved healing charges accepted.");
                Require(state.SequenceEqual(codec.Encode(game.Capture())),"Rejected restore changed current state.");
            }
        });
        yield return ("healing_replaces_food_sales_and_food_use_including_after_restock",()=>{
            var game=Game();var before=game.Capture();
            for(uint n=1;n<=3;n++)GameStep(game,Command(InventoryAction.Buy,FoodCatalog.OfferOffset+(int)n-1,n,batch:1));
            GameStep(game,Command(InventoryAction.Use,1,4));var after=game.Capture();
            Require(after.Players[0].LastInventoryResult==InventoryResult.Disabled && after.Coins==before.Coins && after.Players[0].HealingCharges==3,"Retired food request changed resources.");
            Require(!after.Tackle!.Inventory.Items.Any(i=>i.Kind==ItemKind.Food) && after.Tackle.Merchants.All(m=>m.FoodStock.All(n=>n==0)),"Food still available.");
            var merchants=new MerchantSystem(after.Tackle.Config);merchants.Advance(IslandMap.CreatePlatform(),after.Tiles,after.Players,after.Tackle.NextRestock);
            Require(merchants.Capture().All(m=>m.FoodStock.All(n=>n==0)),"Restock brought back healing food.");
        });
        yield return ("healing_command_requires_no_item_target_and_failed_receipt_stops_retries",()=>{
            var intent=new InventoryCommand(InventoryAction.Heal,0,requestId:9);
            Require(InventoryCommand.Decode(intent.Encode()).Action==InventoryAction.Heal,"Heal command lost action.");
            foreach(var bad in new[]{new InventoryCommand(InventoryAction.Heal,1,requestId:9),new InventoryCommand(InventoryAction.Heal,0,amount:1,requestId:9)})
            {bool rejected=false;try{bad.Encode();}catch(InvalidDataException){rejected=true;}Require(rejected,"Heal accepted an item target or quantity.");}
            var buffer=new InventoryRequestBuffer();Require(buffer.TryQueue(8,intent),"Could not queue heal.");
            Require(buffer.Observe(9,InventoryResult.NoHealingCharges,out _) && !buffer.Pending,"Failed healing kept retrying.");
        });
    }
}
