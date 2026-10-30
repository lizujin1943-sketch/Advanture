using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static HookGameplayEvent[] EventStep(IDeterministicSimulation<HookState> model,params HookInput[] commands)
    {
        var s=model.Capture();return model.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select((p,i)=>
            new SimulationInput(new Participant(p.Slot,new Guid(i+1,0,0,new byte[8])),
                new ByteString((i<commands.Length?commands[i]:default).Encode()))).ToArray())
            .Select(e=>HookGameplayEventCodec.Decode(e.Span)).ToArray();
    }
    private static void PrepareEventFish(IDeterministicSimulation<HookState> model,int species=0,int grams=5000,int grade=1)
    {
        CoopEdit(model,p=>{
            p[0].X=0;p[0].Z=0;p[0].HookX=-2800;p[0].HookZ=0;p[0].Distance=2800;
            p[0].Phase=HookPhase.Returning;p[0].FishId=0;p[0].Stamina=600000;p[0].Combo.PendingCast=true;
        },f=>{
            f[0].X=-2800;f[0].Z=0;f[0].Y=1250;f[0].Owner=0;f[0].Phase=FishPhase.Hooked;
            f[0].Species=species;f[0].WeightGrams=grams;f[0].Grade=grade;
        });
    }
    private static IEnumerable<(string,Action)> GameplayEventCases()
    {
        yield return ("gameplay_events_codec_roundtrips_and_rejects_invalid_payloads",()=>{
            foreach(HookGameplayEventKind kind in Enum.GetValues(typeof(HookGameplayEventKind)))
            {
                var value=new HookGameplayEvent(kind,3,7,11,42,-123,456,ItemKind.Rod,1);
                var bytes=HookGameplayEventCodec.Encode(value);var decoded=HookGameplayEventCodec.Decode(bytes);
                Require(bytes.Length==HookGameplayEventCodec.ByteCount && bytes.SequenceEqual(HookGameplayEventCodec.Encode(decoded)),"Event roundtrip changed payload.");
            }
            foreach(var bytes in new[]{new byte[0],new byte[29],Enumerable.Repeat((byte)255,29).ToArray()})
            {bool rejected=false;try{HookGameplayEventCodec.Decode(bytes);}catch(System.IO.InvalidDataException){rejected=true;}Require(rejected,"Malformed event accepted.");}
        });
        yield return ("gameplay_events_committed_catch_rewards_once_and_restore_is_quiet",()=>{
            var m=CoopModel(1);PrepareEventFish(m);var before=m.Capture();var events=EventStep(m);var after=m.Capture();
            Require(events.Single(e=>e.Kind==HookGameplayEventKind.CoinsReceived).Amount==after.Coins-before.Coins,"Coin event differs from settlement.");
            Require(events.Single(e=>e.Kind==HookGameplayEventKind.MaterialReceived).Amount==after.Players[0].Fishbones-before.Players[0].Fishbones,"Bone event differs from backpack credit.");
            Require(events.Count(e=>e.Kind==HookGameplayEventKind.FishCaught)==1 && events.Single(e=>e.Kind==HookGameplayEventKind.ComboIncreased).Value==1,"Catch/combo result missing.");
            var codec=new HookGame();var bytes=codec.Encode(after);m.Restore(codec.Decode(bytes));
            Require(bytes.SequenceEqual(codec.Encode(m.Capture())),"Events entered persistent state.");
            Require(EventStep(m).Length==0,"Restore or empty return replayed catch events.");
            PrepareEventFish(m);Require(EventStep(m).Count(e=>e.Kind==HookGameplayEventKind.FishCaught)==1,"A new post-restore catch was suppressed.");
        });
        yield return ("gameplay_events_failed_catch_transaction_emits_no_reward_or_combo",()=>{
            var m=CoopModel(1);PrepareEventFish(m);var s=m.Capture();var t=s.Tackle!;
            var inv=new InventoryState(1,t.Inventory.Items,int.MaxValue,t.Inventory.SpentCoins);
            m.Restore(new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,new TackleState(t.Config,inv,t.Stock,t.Batch,t.NextRestock),s.Enemies));
            for(int n=0;n<3;n++)Require(EventStep(m).Length==0,"Failed transaction emitted an uncommitted result.");
            Require(m.Capture().Coins==s.Coins && m.Capture().Players[0].Catches==0,"Failed transaction changed wallet/catches.");
        });
        yield return ("gameplay_events_same_frame_income_and_purchase_survive_zero_net_wallet_change",()=>{
            var m=CoopModel(1);PrepareEventFish(m,1,10000,3);var s=m.Capture();
            var offer=s.Tackle!.Stock[0];int price=InventoryCatalog.Get(offer.Kind,offer.Type).Price;
            Require(price==180,"Fixture requires the standard 180-coin long rod.");
            var command=new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Buy,0,batch:s.Tackle.Batch,requestId:1));
            var events=EventStep(m,command);var after=m.Capture();
            Require(after.Players[0].LastInventoryResult==InventoryResult.Success && after.Players[0].Coins==s.Players[0].Coins,"Fixture did not earn and spend the same amount.");
            Require(events.Single(e=>e.Kind==HookGameplayEventKind.CoinsReceived).Amount==180 && events.Single(e=>e.Kind==HookGameplayEventKind.EquipmentReceived).ItemKind==ItemKind.Rod,"Net-zero frame lost income/purchased equipment.");
            Require(EventStep(m,command).Length==0,"Repeated purchase/catch emitted another result.");
        });
        yield return ("gameplay_events_failed_inventory_request_cannot_replay_after_funding",()=>{
            var m=RodModel();var s=m.Capture();var command=new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Buy,0,batch:s.Tackle!.Batch,requestId:1));
            Require(EventStep(m,command).Length==0 && m.Capture().Players[0].LastInventoryResult==InventoryResult.InsufficientCoins,"Failed purchase emitted income.");
            s=m.Capture();var p=s.Players.ToArray();p[0].Coins=p[0].CoinsEarned=1000;
            m.Restore(new HookState(s.Steps,p,s.Fish,1000,s.Tiles,s.Driftwood,s.Tackle,s.Enemies));
            Require(EventStep(m,command).Length==0,"Funded retry replayed a failed request.");
            command=new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Buy,0,batch:s.Tackle!.Batch,requestId:2));
            Require(EventStep(m,command).Count(e=>e.Kind==HookGameplayEventKind.EquipmentReceived)==1,"Fresh request did not emit equipment income.");
        });
        yield return ("gameplay_events_equipment_pickup_and_direct_equip_report_actual_income_once",()=>{
            foreach(var action in new[]{InventoryAction.Pickup,InventoryAction.PickupAndEquip})
            {
                var m=RodModel();var rod=m.Capture().Players[0].EquippedRod;
                Require(EventStep(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Unequip,(int)EquipmentSlot.RightHand,requestId:1))).Length==0,"Unequip reported income.");
                Require(EventStep(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Drop,rod,amount:1,requestId:2))).Length==0,"Drop reported income.");
                var command=new HookInput(0,0,inventory:new InventoryCommand(action,rod,requestId:3));var events=EventStep(m,command);
                var received=events.Single(e=>e.Kind==HookGameplayEventKind.EquipmentReceived);
                Require(received.Amount==1 && received.TargetId==rod && received.ItemKind==ItemKind.Rod && received.ItemType==0,"Equipment pickup misidentified the actual instance/type.");
                Require(EventStep(m,command).Length==0,"Repeated equipment pickup emitted another result.");
            }
        });
        yield return ("gameplay_events_damage_then_same_frame_respawn_keeps_exact_damage",()=>{
            var m=RodModel(1000,config:new FishingConfig(enemies:EnemyConfig.Disabled));var s=m.Capture();var p=s.Players.ToArray();
            p[0].X=0;p[0].Z=-5000;p[0].Vitals.Health=1;p[0].Swimming.InWater=true;p[0].Swimming.DamageTicks=29;
            m.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies));
            var events=EventStep(m);var after=m.Capture().Players[0];
            Require(after.Vitals.Health>1 && after.Revival.LastResult==RespawnResult.Success,"Fixture did not damage then heal in one step.");
            Require(events.Single(e=>e.Kind==HookGameplayEventKind.Damaged).Amount==1,"Recovery hid damage or included healing in its amount.");
        });
        yield return ("gameplay_events_treasure_full_bag_overflow_is_quiet_until_actual_partial_pickup",()=>{
            var m=TreasureModel(TreasureKind.Large,2);TreasureCast(m,0);TreasureCast(m,1);var chest=TreasureLand(m);var s=m.Capture();
            var inv=new InventorySystem(s.Tackle!.Inventory);var p=s.Players.ToArray();
            for(int i=0;i<2;i++)
            {var size=inv.Capture().Size(i);int full=size.Width*size.Height*99;Require(inv.CreditMaterial(i,ItemKind.Wood,full,new Point(0,0))==full,"Could not fill bag.");p[i].Wood=p[i].WoodEarned=full;}
            TreasureRestore(m,p,tackle:new TackleState(s.Tackle.Config,inv.Capture(),s.Tackle.Stock,s.Tackle.Batch,s.Tackle.NextRestock));
            var open=new HookInput(0,0,treasure:new TreasureCommand(chest.Id+1,chest.Generation,1));var events=EventStep(m,open);
            Require(events.All(e=>e.Kind!=HookGameplayEventKind.MaterialReceived) && events.Count(e=>e.Kind==HookGameplayEventKind.CoinsReceived)==2,"Ground overflow was reported as backpack income.");
            Require(EventStep(m,open).Length==0,"Repeated opening emitted another reward.");
            s=m.Capture();inv=new InventorySystem(s.Tackle!.Inventory);var stack=inv.Capture().Items.First(i=>i.Owner==0 && i.Kind==ItemKind.Wood && i.Location==ItemLocation.Backpack);
            Require(inv.Drop(0,stack.Id,1,new Point(chest.X,chest.Z))==InventoryResult.Success,"Could not make one unit of space.");
            p=s.Players.ToArray();p[0].Wood--;
            var ground=inv.Capture().Items.Single(i=>i.Kind==ItemKind.Wood && i.Location==ItemLocation.Ground);
            TreasureRestore(m,p,tackle:new TackleState(s.Tackle.Config,inv.Capture(),s.Tackle.Stock,s.Tackle.Batch,s.Tackle.NextRestock));
            var pickup=new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Pickup,ground.Id,requestId:1));events=EventStep(m,pickup);
            Require(events.Single(e=>e.Kind==HookGameplayEventKind.MaterialReceived).Amount==1,"Partial pickup reported full ground stack.");
            Require(m.Capture().Tackle!.Inventory.Items.Single(i=>i.Id==ground.Id).Count==ground.Count-1,"Pickup did not leave overflow on ground.");
            Require(EventStep(m,pickup).Length==0,"Duplicate pickup emitted twice.");
        });
        yield return ("gameplay_events_cast_player_hook_and_upgrade_are_success_receipts",()=>{
            var m=RodModel(count:2);CoopEdit(m,p=>{p[0].X=0;p[1].X=1800;});
            var events=EventStep(m,new HookInput(0,0,1800,0,true));
            Require(events.Count(e=>e.Kind==HookGameplayEventKind.HookCast)==1,"Successful cast lacked event.");
            for(int n=0;n<20 && !events.Any(e=>e.Kind==HookGameplayEventKind.PlayerHooked);n++)events=EventStep(m);
            var hit=events.Single(e=>e.Kind==HookGameplayEventKind.PlayerHooked);Require(hit.Slot==0 && hit.TargetId==1,"Player-hook event swapped hooker and target.");
            m=FundedShop(100);events=EventStep(m,new HookInput(0,0,upgrade:HookUpgrade.Range,purchaseId:1));
            Require(events.Single(e=>e.Kind==HookGameplayEventKind.Upgraded).Value==1,"Successful legacy upgrade lacked resulting level.");
            Require(EventStep(m,new HookInput(0,0,upgrade:HookUpgrade.Range,purchaseId:1)).Length==0,"Duplicate upgrade emitted twice.");
        });
        yield return ("gameplay_events_melee_swing_and_impact_are_once_only_across_restore",()=>{
            var m=RodModel(count:2,config:new FishingConfig(enemies:EnemyConfig.Disabled,melee:new MeleeConfig()));
            var s=m.Capture();var pan=s.Tackle!.Inventory.Items.First(i=>i.Owner==0 && i.Kind==ItemKind.Weapon);
            Require(EventStep(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,pan.Id,requestId:1))).Length==0,"Equipping owned gear reported new equipment income.");
            CoopEdit(m,p=>{p[0].X=0;p[0].Z=-1400;p[1].X=1000;p[1].Z=-1400;});
            var events=EventStep(m,new HookInput(0,0,2000,-1400,true));
            Require(events.Count(e=>e.Kind==HookGameplayEventKind.MeleeSwing)==1 && !events.Any(e=>e.Kind==HookGameplayEventKind.MeleeHit),"Swing/impact timing was merged.");
            var all=new List<HookGameplayEvent>();for(int n=0;n<7;n++)all.AddRange(EventStep(m));
            Require(all.Count(e=>e.Kind==HookGameplayEventKind.MeleeHit)==1 && all.Single(e=>e.Kind==HookGameplayEventKind.MeleeHit).TargetId==1,"Melee impact missing or duplicated.");
            var codec=new HookGame();m.Restore(codec.Decode(codec.Encode(m.Capture())));
            for(int n=0;n<30;n++)Require(!EventStep(m).Any(e=>e.Kind==HookGameplayEventKind.MeleeHit),"Post-impact restore replayed a hit.");
        });
        yield return ("gameplay_events_batch_orders_both_polls_deduplicates_full_identity_and_resets_epoch",()=>{
            var batch=new ConfirmedGameplayEventBatch();batch.Reset(Id,1);
            FrameResult Frame(ulong tick,ulong epoch=1)=>new FrameResult(tick,new[]{new SimulationEvent(Id,epoch,tick,0,
                new ByteString(HookGameplayEventCodec.Encode(new HookGameplayEvent(HookGameplayEventKind.HookCast,0))))});
            batch.Add(new[]{Frame(10),Frame(11)});batch.Add(new[]{Frame(11),Frame(12)});
            Require(batch.Values.Select(e=>e.Tick).SequenceEqual(new ulong[]{10,11,12}),"Multiple confirmation batches lost/repeated/out-of-order events.");
            batch.BeginPoll();batch.Add(new[]{Frame(10),Frame(12)});Require(batch.Values.Count==0,"Duplicate confirmed frames replayed after Poll clear.");
            batch.Reset(Id,2);Require(batch.Values.Count==0,"New epoch retained old batch.");batch.Add(new[]{Frame(1,2)});
            Require(batch.Values.Single().Epoch==2 && batch.Values.Single().Tick==1,"New epoch suppressed a valid new identity.");
        });
    }
}
