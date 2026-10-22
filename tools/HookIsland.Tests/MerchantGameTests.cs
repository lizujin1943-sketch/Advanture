using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using Advanture.HookIsland.Network;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    // Transaction boundary fixtures; the later acceptance pilot earns resources and builds these routes through inputs.
    private static IDeterministicSimulation<HookState> MerchantModel(int count=1,int seconds=600)
    {
        var map=MerchantMap(seconds);var m=PullModel(map,count);var s=m.Capture();var p=s.Players.ToArray();
        var tiles=MerchantRoads().Select(t=>Math.Abs(t.X)<=1 && Math.Abs(t.Z)<=1?t:new PlatformTile(t.X,t.Z,TileKind.Wood,0,4)).ToArray();
        for(int i=0;i<count;i++){p[i].X=i<2?8000:0;p[i].Z=i<2?i*400:12000+(i-2)*400;p[i].Coins=p[i].CoinsEarned=10000;}
        p[0].WoodEarned=tiles.Sum(t=>t.Paid);
        m.Restore(new HookState(s.Steps,p,s.Fish,p.Sum(v=>v.Coins),tiles,s.Driftwood,s.Tackle));return m;
    }
    private static byte[] MerchantBuy(int merchant,int index,uint batch,uint request,bool equip=false)=>
        new HookInput(0,0,shopping:true,inventory:new InventoryCommand(equip?InventoryAction.BuyAndEquip:InventoryAction.Buy,index,batch:batch,requestId:request,merchantId:merchant)).Encode();
    private static IEnumerable<(string,Action)> MerchantGameCases()
    {
        yield return ("merchant_game_purchase_activates_shelf_and_shopping_checkpoint_restores",()=>{
            var m=MerchantModel();ShopStep(m,MerchantBuy(1,0,1,1,true));var s=m.Capture();
            Require(s.Players[0].LastInventoryResult==InventoryResult.Success && s.Players[0].Shopping,"Expanded shore merchant rejected purchase or shopping protection.");
            var shelf=s.Tackle!.Merchants[1];var price=InventoryCatalog.Get(shelf.Stock[0].Kind,shelf.Stock[0].Type).Price;
            Require(shelf.Active && shelf.ActivatedAt==1 && shelf.Stock[0].Sold && s.Players[0].Coins==10000-price && s.Tackle.Items.Count==2,"Shelf, charge and item were not committed together.");
            var codec=new HookGame();m.Restore(codec.Decode(codec.Encode(s)));
            Require(m.Capture().Players[0].Shopping && m.Capture().Tackle!.Merchants[1].Stock[0].Sold,"Shopping checkpoint lost offshore shelf.");
        });
        yield return ("merchant_game_same_shelf_race_and_different_shops_are_independent",()=>{
            var m=MerchantModel(4);ShopStep(m,MerchantBuy(1,0,1,1),MerchantBuy(1,0,1,1),MerchantBuy(2,0,1,1),MerchantBuy(2,0,1,1));
            var s=m.Capture();Require(s.Players.Select(p=>p.LastInventoryResult).SequenceEqual(new[]{InventoryResult.Success,InventoryResult.SoldOut,InventoryResult.Success,InventoryResult.SoldOut}),"Shop IDs crossed or concurrent shelf sold twice.");
            Require(s.Tackle!.Items.Count==6 && s.Tackle.Merchants[1].Stock[0].Sold && s.Tackle.Merchants[2].Stock[0].Sold && s.Tackle.Stock.All(i=>!i.Sold),"One shop purchase altered another shelf.");
            int coins=s.Coins;ShopStep(m,MerchantBuy(1,0,1,1),MerchantBuy(1,0,1,1),MerchantBuy(2,0,1,1),MerchantBuy(2,0,1,1));
            Require(m.Capture().Coins==coins && m.Capture().Tackle!.Items.Count==6,"Repeated receipt charged twice.");
        });
        yield return ("merchant_game_remote_shop_rejection_does_not_replay_on_switch",()=>{
            var m=MerchantModel();ShopStep(m,MerchantBuy(2,0,1,1));
            Require(m.Capture().Players[0].LastInventoryResult==InventoryResult.TooFar && m.Capture().Players[0].Coins==10000,"Remote merchant request spent money.");
            ShopStep(m,MerchantBuy(1,0,1,1));
            Require(m.Capture().Players[0].LastInventoryResult==InventoryResult.TooFar && m.Capture().Tackle!.Merchants[1].Stock.All(i=>!i.Sold),"Failed request replayed after merchant switch.");
            ShopStep(m,MerchantBuy(1,0,1,2));
            Require(m.Capture().Players[0].LastInventoryResult==InventoryResult.Success,"Fresh request at current merchant failed.");
        });
        yield return ("merchant_game_restock_precedes_purchase_and_legacy_action_stays_at_original",()=>{
            var m=MerchantModel(seconds:1);ShopStep(m);var initial=m.Capture();
            Require(initial.Tackle!.Merchants[1].NextRestock==31,"Activation-relative first deadline shifted.");
            ShopStep(m,RodCommand(RodAction.Buy,0,1,1));
            Require(m.Capture().Players[0].LastRodResult==RodResult.TooFar && m.Capture().Coins==10000,"Legacy purchase guessed a nearby merchant.");
            while(m.Capture().Steps<30)ShopStep(m);
            ShopStep(m,MerchantBuy(1,0,1,1));var s=m.Capture();
            Require(s.Tackle!.Merchants[1].Batch==2 && s.Tackle.Merchants[1].Stock.All(i=>!i.Sold) && s.Players[0].LastInventoryResult==InventoryResult.StaleStock && s.Coins==10000,"Old request bought newly restocked offer.");
            ShopStep(m,MerchantBuy(1,0,2,2));
            Require(m.Capture().Players[0].LastInventoryResult==InventoryResult.Success,"Current batch purchase failed.");
        });
        yield return ("merchant_game_restore_preserves_sold_stock_and_each_clock",()=>{
            var a=MerchantModel(4,2);ShopStep(a,MerchantBuy(1,0,1,1),default(HookInput).Encode(),MerchantBuy(2,0,1,1));
            for(int i=0;i<24;i++)ShopStep(a);var codec=new HookGame();var b=MerchantModel(4,2);b.Restore(codec.Decode(codec.Encode(a.Capture())));
            Require(b.Capture().Tackle!.Merchants[1].Stock[0].Sold && b.Capture().Tackle!.Merchants[2].Stock[0].Sold,"Restore reopened sold stock.");
            for(int i=0;i<90;i++)
            {
                var duplicate=MerchantBuy(1,0,1,1);ShopStep(a,duplicate);ShopStep(b,duplicate);
                Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Restored merchant state diverged.");
            }
        });
        yield return ("merchant_game_departure_does_not_activate_and_fresh_game_resets_shelves",()=>{
            var m=MerchantModel();var s=m.Capture();m.Step(new FrameContext(1,1,30,new[]{new MemberEvent(1,MemberEventKind.Deactivate,0,1)}),Array.Empty<SimulationInput>());
            Require(!m.Capture().Tackle!.Merchants[1].Active,"Disconnected player activated a shop on departure.");
            var fresh=PullModel(MerchantMap(),1).Capture();
            Require(fresh.Tackle!.Merchants[0].Active && fresh.Tackle.Merchants[0].Batch==1 && fresh.Tackle.Merchants.Skip(1).All(t=>!t.Active),"New world retained offshore stock.");
        });
        yield return ("merchant_game_rejects_corrupt_identity_stock_and_activation_clocks",()=>{
            var m=MerchantModel();ShopStep(m,MerchantBuy(1,0,1,1));var s=m.Capture();var shelf=s.Tackle!.Merchants[1];
            var wrong=shelf.Stock.ToArray();wrong[0].Type=0;
            var corrupt=new[]{new MerchantState(2,true,shelf.ActivatedAt,shelf.Batch,shelf.NextRestock,shelf.Stock),
                new MerchantState(1,false,0,0,0,shelf.Stock),new MerchantState(1,true,0,1,18000,shelf.Stock),
                new MerchantState(1,true,2,1,18002,shelf.Stock),new MerchantState(1,true,1,2,18001,shelf.Stock),
                new MerchantState(1,true,1,1,18002,shelf.Stock),new MerchantState(1,true,1,1,18001,wrong)};
            foreach(var bad in corrupt)
            {
                var states=s.Tackle.Merchants.ToArray();states[1]=bad;ExpectMerchantSnapshotRejected(s,states);
            }
            ExpectMerchantSnapshotRejected(s,s.Tackle.Merchants.Take(4));
            var inactive=s.Tackle.Merchants.ToArray();inactive[2]=new MerchantState(2,false,0,1,0,Array.Empty<RodStock>());ExpectMerchantSnapshotRejected(s,inactive);
        });
        yield return ("merchant_game_command_id_survives_queue_retry_and_wire_roundtrip",()=>{
            var queue=new InventoryRequestBuffer();var intent=new InventoryCommand(InventoryAction.BuyAndEquip,2,batch:5,merchantId:4);
            Require(queue.TryQueue(7,intent) && queue.Command.MerchantId==4 && queue.Command.RequestId==8,"Queue dropped selected merchant.");
            var roundtrip=HookInput.Decode(new HookInput(0,0,inventory:queue.Command).Encode()).Inventory;
            Require(roundtrip.MerchantId==4 && roundtrip.Target==2 && roundtrip.Batch==5 && roundtrip.RequestId==8,"Wire changed purchase identity.");
            Require(!queue.TryQueue(7,new InventoryCommand(InventoryAction.Buy,0,batch:1,merchantId:1)) && queue.Command.MerchantId==4,"Pending request switched merchants.");
            Require(queue.Observe(8,InventoryResult.TooFar,out _) && !queue.Pending,"Rejected request remained queued.");
            foreach(var bad in new[]{new InventoryCommand(InventoryAction.Buy,0,batch:1,requestId:1,merchantId:-1),new InventoryCommand(InventoryAction.Buy,0,batch:1,requestId:1,merchantId:5),new InventoryCommand(InventoryAction.Equip,1,requestId:1,merchantId:1)})
            {bool rejected=false;try{bad.Encode();}catch(System.IO.InvalidDataException){rejected=true;}Require(rejected,"Malformed merchant ID accepted.");}
        });
        yield return ("merchant_game_map_reserved_cells_are_idempotent_and_block_shared_placement",()=>{
            var map=MerchantMap();byte[] bytes=map.Encode();
            for(int i=0;i<4;i++){map=IslandMap.Decode(map.Encode());Require(bytes.SequenceEqual(map.Encode()) && map.Platform!.BlockedCells.Count==4,"Merchant normalization changed map or duplicated cells.");}
            var m=MerchantModel();var s=m.Capture();var p=s.Players[0];p.Wood=100;
            Require(PlatformRules.Check(map,s,p,TileKind.Wood,5,0)==BuildResult.Occupied,"Ship cell accepted construction.");
            var build=PlatformRules.Check(map,s,p,TileKind.Wood,4,-1);Require(build==BuildResult.Success,$"Adjacent clear cell rejected: {build}.");
            Require(!OceanPlacement.LaneWater(map,s.Tiles,new Point(10000,0)) && !FinalePlacement.Water(map,new FinaleConfig(),s.Tiles,new Point(10000,0)),"Fish or giant placement ignored ship reserved cell.");
            var legacy=map.Encode();System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(legacy,0x454D4948);
            bool rejected=false;try{IslandMap.Decode(legacy);}catch(System.IO.InvalidDataException){rejected=true;}Require(rejected,"Old equipment map accepted.");
        });
        yield return ("merchant_game_swim_and_knockback_stop_at_ship_body",()=>{
            var map=MerchantMap();var s=MerchantModel().Capture();var p=s.Players[0];
            var reached=SwimmingTerrain.Move(map,s.Tiles,s.Players,0,new Point(8000,0),new Point(12000,0),false,false,out bool water);
            Require(reached.X==9000 && water && !SwimmingTerrain.Obstacle(map,reached.X,reached.Z) && SwimmingTerrain.Obstacle(map,9001,0),"Swim path crossed ship or mishandled tangent boundary.");
            KnockbackSystem.Start(ref p,1000,0,6000);
            for(int i=0;i<30 && p.Knockback.Active;i++)KnockbackSystem.Advance(map,s.Tiles,s.Players,ref p);
            Require(p.X<=9000 && p.X>=8950 && !p.Knockback.Active && p.Swimming.InWater,"Knockback crossed ship body.");
        });
        yield return ("merchant_game_player_tow_cannot_pull_through_ship",()=>{
            var m=MerchantModel(2);var s=m.Capture();var p=s.Players.ToArray();p[1].X=12000;p[1].Z=0;
            var tiles=s.Tiles.Concat(Enumerable.Range(2,5).Select(x=>new PlatformTile(x,1,TileKind.Wood,0,4)))
                .Concat(new[]{new PlatformTile(6,0,TileKind.Wood,0,4)}).ToArray();p[0].WoodEarned=tiles.Sum(t=>t.Paid);
            m.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,tiles,s.Driftwood,s.Tackle));
            ShopStep(m,default(HookInput).Encode(),new HookInput(0,0,8000,0,true).Encode());
            for(int i=0;i<55;i++)ShopStep(m);s=m.Capture();
            Require(s.Players[1].PlayerHooks==1 && s.Players[0].TimesHooked==1,"Tow fixture did not hit the player.");
            // Reaching water can release the rescue before body contact if no safe landing exists across the boat.
            Require(s.Players[0].X<=9000 && s.Players[0].X>=8800 && s.Players[0].Swimming.InWater,$"Friendly tow passed through ship: x={s.Players[0].X}.");
        });
        yield return ("merchant_game_water_monster_must_surface_clear_of_ship",()=>{
            var b=MerchantMap();var map=new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,
                new FishingConfig(enemies:new EnemyConfig(piranhaCount:0,initialDelayTicks:0,monsterX:10000,monsterZ:0),merchants:MerchantConfig.Default));
            var m=PullModel(map,1);ShopStep(m);var monster=m.Capture().Enemies.Monster;
            Require(monster.Phase==MonsterPhase.Windup,"Monster did not surface for geometry check.");
            Require(map.Fishing!.Merchants.Definitions.All(d=>Math.Abs(monster.X-d.X)>=1900 || Math.Abs(monster.Z-d.Z)>=1900),"Water monster surfaced in a ship's reserved water.");
        });
    }
    private static void ExpectMerchantSnapshotRejected(HookState s,IEnumerable<MerchantState> merchants)
    {
        var bad=new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,new TackleState(s.Tackle!.Config,s.Tackle.Inventory,merchants));
        bool rejected=false;try{var codec=new HookGame();codec.Decode(codec.Encode(bad));}catch(System.IO.InvalidDataException){rejected=true;}
        Require(rejected,"Corrupt merchant snapshot accepted.");
    }
}
