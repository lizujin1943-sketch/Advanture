using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using Advanture.HookIsland.Network;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IDeterministicSimulation<HookState> TreasureModel(TreasureKind kind,int count=1,TreasureConfig? config=null,EnemyConfig? enemyConfig=null)
    {
        var basis=IslandMap.CreatePlatform();
        config??=new TreasureConfig(smallCount:kind==TreasureKind.Small?1:0,rareCount:kind==TreasureKind.Rare?1:0,
            largeCount:kind==TreasureKind.Large?1:0,initialDelayTicks:0,driftSpeed:0,rareDriftSpeed:0);
        var map=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,new[]{new Point(18000,18000)},basis.Platform,
            new FishingConfig(treasure:config,enemies:enemyConfig));
        var model=PullModel(map,count);ShopStep(model);var s=model.Capture();var players=s.Players.ToArray();
        for(int i=0;i<count;i++){players[i].X=-2600;players[i].Z=count<=2?-600+i*1200:-1800+i*1200;players[i].HookX=players[i].X;players[i].HookZ=players[i].Z;}
        var chests=s.Treasure.Chests.ToArray();
        Require(chests.Length==1 && chests[0].Phase==TreasurePhase.Floating,"Configured chest did not spawn.");
        chests[0].X=-4800;chests[0].Z=0;
        var wood=s.Driftwood.Select(d=>DriftwoodMotion.Create(d.Id,300)).ToArray();
        model.Restore(new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,wood,s.Tackle,s.Enemies,s.RoomRules,new TreasureState(chests)));
        return model;
    }
    private static void TreasureCast(IDeterministicSimulation<HookState> model,int slot)
    {
        var c=model.Capture().Treasure.Chests[0];var input=new HookInput[model.Capture().Players.Count];
        input[slot]=new HookInput(0,0,c.X,c.Z,true);ShopStep(model,input.Select(v=>v.Encode()).ToArray());
        for(int i=0;i<30 && model.Capture().Players[slot].HookedChest==0;i++)ShopStep(model);
    }
    private static void TreasureRestore(IDeterministicSimulation<HookState> model,Fisher[]? players=null,TreasureChest[]? chests=null,TackleState? tackle=null,EnemyState? enemies=null)
    {
        var s=model.Capture();model.Restore(new HookState(s.Steps,players??s.Players.ToArray(),s.Fish,
            players?.Sum(p=>p.Coins)??s.Coins,s.Tiles,s.Driftwood,tackle??s.Tackle,enemies??s.Enemies,s.RoomRules,new TreasureState(chests??s.Treasure.Chests.ToArray())));
    }
    private static TreasureChest TreasureLand(IDeterministicSimulation<HookState> model)
    {
        for(int i=0;i<240 && model.Capture().Treasure.Chests[0].Phase!=TreasurePhase.Landed;i++)ShopStep(model);
        Require(model.Capture().Treasure.Chests[0].Phase==TreasurePhase.Landed,"Large chest did not land.");
        for(int i=0;i<60;i++)ShopStep(model);
        return model.Capture().Treasure.Chests[0];
    }
    private static void TreasureOpen(IDeterministicSimulation<HookState> model,int slot,TreasureChest chest,uint request)
    {
        var input=new HookInput[model.Capture().Players.Count];
        input[slot]=new HookInput(0,0,treasure:new TreasureCommand(chest.Id+1,chest.Generation,request));
        ShopStep(model,input.Select(v=>v.Encode()).ToArray());
    }
    private static void TreasureCheckpoint(IDeterministicSimulation<HookState> model)
    {
        var codec=new HookGame();var bytes=codec.Encode(model.Capture());model.Restore(codec.Decode(bytes));
        Require(bytes.SequenceEqual(codec.Encode(model.Capture())),"Treasure checkpoint changed after restoration.");
    }
    private static IEnumerable<(string,Action)> TreasureCases()
    {
        yield return ("treasure_boundary_landing_prunes_newly_overlong_assistant",()=>{
            var m=TreasureModel(TreasureKind.Large,2);TreasureCast(m,0);TreasureCast(m,1);var s=m.Capture();
            var p=s.Players.ToArray();var c=s.Treasure.Chests.ToArray();c[0].X=-2810;c[0].Z=-2600;
            p[0].X=-2600;p[0].Z=-2790;p[1].X=-2600;p[1].Z=1390;
            foreach(int slot in new[]{0,1}){p[slot].HookX=c[0].X;p[slot].HookZ=c[0].Z;}
            TreasureRestore(m,p,c);s=m.Capture();
            Require(s.Players.All(v=>IslandMap.DistanceSquared(v.X,v.Z,c[0].X,c[0].Z)<=16000000),"Boundary fixture starts overlong.");
            ShopStep(m);s=m.Capture();var landed=s.Treasure.Chests[0];
            Require(landed.Phase==TreasurePhase.Landed && landed.RewardMembers==1 && landed.Owner==0 &&
                s.Players.All(v=>v.HookedChest==0),"An assistant overextended by the landing move retained a reward share.");
            Require(IslandMap.DistanceSquared(s.Players[1].X,s.Players[1].Z,landed.X,landed.Z)>16000000,"Landing did not cross the assistant's range.");
            TreasureCheckpoint(m);for(int i=0;i<60;i++)ShopStep(m);TreasureOpen(m,0,landed,1);s=m.Capture();
            Require(s.Players[0].Coins==landed.Coins && s.Players[1].Coins==0 && s.Players[1].TreasuresCollected==0,"Detached assistant received landing rewards.");
        });
        yield return ("treasure_boundary_full_backpacks_drop_complete_contents_on_platform",()=>{
            var m=TreasureModel(TreasureKind.Large,2);TreasureCast(m,0);TreasureCast(m,1);var c=TreasureLand(m);var s=m.Capture();
            var t=s.Tackle!;var inv=new InventorySystem(t.Inventory);var p=s.Players.ToArray();
            for(int i=0;i<2;i++)
            {
                var size=t.Inventory.Size(i);int wood=size.Width*size.Height*InventoryCatalog.MaterialStackLimit;
                Require(inv.CreditMaterial(i,ItemKind.Wood,wood,new Point(0,0))==wood,"Could not fill recipient backpack.");
                p[i].Wood=p[i].WoodEarned=wood;
            }
            var before=inv.Capture();TreasureRestore(m,p,tackle:new TackleState(t.Config,before,t.Stock,t.Batch,t.NextRestock));
            TreasureOpen(m,0,c,1);s=m.Capture();var after=s.Tackle!.Inventory;var ground=after.Items.Where(i=>i.Location==ItemLocation.Ground).ToArray();
            Require(s.Coins==c.Coins && s.Players.All(v=>v.TreasuresCollected==1 && v.Fishbones==0),"Full bags prevented complete settlement or placed bones in occupied cells.");
            Require(ground.Sum(i=>i.Kind==ItemKind.Wood?i.Count:0)==c.Wood && ground.Sum(i=>i.Kind==ItemKind.Fishbone?i.Count:0)==c.Bones &&
                ground.All(i=>i.Owner==-1 && i.WorldX==c.X && i.WorldZ==c.Z && PlatformRules.CanStand(s.Tiles,i.WorldX,i.WorldZ)),"Overflow was lost, duplicated or dropped off platform.");
            Require(after.Items.Where(i=>i.Location!=ItemLocation.Ground).SequenceEqual(before.Items),"Overflow changed existing full backpacks.");
            for(int i=0;i<2;i++)Require(s.Players[i].Wood==p[i].Wood && s.Players[i].WoodEarned==p[i].WoodEarned+c.Wood/2+(i==c.Owner?c.Wood%2:0) &&
                s.Players[i].FishbonesEarned==c.Bones/2+(i==c.Owner?c.Bones%2:0),"Overflow earnings did not preserve per-player shares.");
            TreasureCheckpoint(m);TreasureOpen(m,0,c,1);Require(m.Capture().Tackle!.Inventory.Items.SequenceEqual(after.Items),"Restored retry duplicated overflow.");
        });
        yield return ("treasure_boundary_old_generation_cannot_open_respawned_slot",()=>{
            var m=TreasureModel(TreasureKind.Large,2,new TreasureConfig(smallCount:0,rareCount:0,largeCount:1,initialDelayTicks:0,respawnTicks:30,driftSpeed:0));
            TreasureCast(m,0);TreasureCast(m,1);var old=TreasureLand(m);TreasureOpen(m,0,old,1);
            for(int i=0;i<31;i++)ShopStep(m);var s=m.Capture();var current=s.Treasure.Chests[0];var inventory=s.Tackle!.Inventory;
            Require(current.Generation==old.Generation+1 && current.Phase==TreasurePhase.Floating,"Slot did not actually respawn.");
            TreasureOpen(m,0,old,2);s=m.Capture();
            Require(s.Players[0].LastTreasureResult==TreasureResult.NotFound && s.Coins==old.Coins &&
                s.Tackle!.Inventory.Items.SequenceEqual(inventory.Items) && s.Treasure.Chests[0].Generation==current.Generation,"Old generation changed the fresh opportunity.");
            var chests=s.Treasure.Chests.ToArray();chests[0].X=-4800;chests[0].Z=0;TreasureRestore(m,chests:chests);
            TreasureCast(m,0);TreasureCast(m,1);current=TreasureLand(m);TreasureCheckpoint(m);TreasureOpen(m,0,current,2);
            Require(m.Capture().Coins==old.Coins && m.Capture().Treasure.Chests[0].Phase==TreasurePhase.Landed,"Failed stale request replayed after the new chest landed.");
            TreasureOpen(m,0,current,3);s=m.Capture();Require(s.Coins==old.Coins+current.Coins && s.Players.All(v=>v.TreasuresCollected==2),"Fresh click did not settle the new generation once.");
        });
        yield return ("treasure_boundary_two_same_frame_openers_settle_once",()=>{
            var m=TreasureModel(TreasureKind.Large,2);TreasureCast(m,0);TreasureCast(m,1);var c=TreasureLand(m);
            var input=new HookInput(0,0,treasure:new TreasureCommand(1,c.Generation,1)).Encode();ShopStep(m,input,input);var s=m.Capture();
            Require(s.Players[0].LastTreasureResult==TreasureResult.Success && s.Players[1].LastTreasureResult==TreasureResult.NotLanded &&
                s.Coins==c.Coins && s.Players.All(p=>p.TreasuresCollected==1),"Same-frame openers settled twice or lost receipts.");
            var inventory=s.Tackle!.Inventory;Require(inventory.Items.Where(i=>i.Kind==ItemKind.Wood).Sum(i=>i.Count)==c.Wood &&
                inventory.Items.Where(i=>i.Kind==ItemKind.Fishbone).Sum(i=>i.Count)==c.Bones,"Same-frame opening duplicated materials.");
            TreasureCheckpoint(m);ShopStep(m,input,input);s=m.Capture();
            Require(s.Coins==c.Coins && s.Tackle!.Inventory.Items.SequenceEqual(inventory.Items) && s.Players.All(p=>p.TreasuresCollected==1),"Restored opening receipts replayed rewards.");
        });
        yield return ("treasure_boundary_later_recipient_stat_limit_rolls_back_all",()=>{
            for(int field=1;field<4;field++)
            {
                var m=TreasureModel(TreasureKind.Large,2);TreasureCast(m,0);TreasureCast(m,1);var c=TreasureLand(m);var state=m.Capture();var p=state.Players.ToArray();var t=state.Tackle!;
                if(field==1)p[1].WoodEarned=int.MaxValue;
                if(field==2)p[1].FishbonesEarned=int.MaxValue;
                if(field==3)p[1].TreasuresCollected=int.MaxValue;
                if(field<3)
                {
                    // Earned material remains in the world even when its recipient's ledger is full.
                    var items=t.Inventory.Items.Concat(new[]{new InventoryItem{Id=t.Inventory.NextId,
                        Kind=field==1?ItemKind.Wood:ItemKind.Fishbone,Count=int.MaxValue,
                        Owner=-1,X=-1,Y=-1,Location=ItemLocation.Ground}});
                    var inventory=new InventoryState(2,items,t.Inventory.NextId+1,t.Inventory.SpentCoins);
                    t=new TackleState(t.Config,inventory,t.Stock,t.Batch,t.NextRestock);
                }
                TreasureRestore(m,p,tackle:t);var before=m.Capture();TreasureOpen(m,0,c,1);var after=m.Capture();
                Require(after.Players[0].LastTreasureResult==TreasureResult.LimitReached && after.Coins==before.Coins && after.Treasure.Chests[0].Equals(c),"Failed later recipient consumed the chest or wallet.");
                for(int i=0;i<2;i++)Require(after.Players[i].Coins==before.Players[i].Coins && after.Players[i].CoinsEarned==before.Players[i].CoinsEarned &&
                    after.Players[i].WoodEarned==before.Players[i].WoodEarned && after.Players[i].FishbonesEarned==before.Players[i].FishbonesEarned &&
                    after.Players[i].TreasuresCollected==before.Players[i].TreasuresCollected,"Later recipient limit partially changed a player's accounting.");
                Require(after.Tackle!.Inventory.Items.SequenceEqual(before.Tackle!.Inventory.Items) && after.Tackle.Inventory.NextId==before.Tackle.Inventory.NextId &&
                    after.Tackle.Inventory.SpentCoins.SequenceEqual(before.Tackle.Inventory.SpentCoins),"Later recipient limit mutated inventory.");
                TreasureCheckpoint(m);TreasureOpen(m,0,c,1);Require(m.Capture().Coins==before.Coins,"Failed limit receipt replayed after restore.");
            }
        });
        yield return ("treasure_waterball_detaches_before_landing_and_reselects",()=>{
            foreach(int health in new[]{100,1})
            {
                var m=TreasureModel(TreasureKind.Large,2,enemyConfig:new EnemyConfig(piranhaCount:0,initialDelayTicks:18000));
                TreasureCast(m,0);TreasureCast(m,1);var s=m.Capture();var p=s.Players.ToArray();p[0].Vitals.Health=health;
                var c=s.Treasure.Chests.ToArray();c[0].X=-2810;c[0].Z=-600;
                foreach(int slot in new[]{0,1}){p[slot].HookX=c[0].X;p[slot].HookZ=c[0].Z;}
                var e=s.Enemies;var ball=new Waterball{Id=1,OriginX=-3500,OriginZ=-600,X=-3234,Z=-600,DirectionX=1000000,Distance=266,TravelRemainder=20,KnockbackDistance=4000};
                TreasureRestore(m,p,c,enemies:new EnemyState(e.Piranhas,e.Monster,new[]{ball},1,0,1));ShopStep(m);s=m.Capture();
                Require(s.Players[0].HookedChest==0 && s.Players[1].HookedChest==1 && s.Treasure.Chests[0].Owner==1 &&
                    s.Treasure.Chests[0].Phase==TreasurePhase.Hooked && s.Treasure.Chests[0].X==-2810,"Hit player stayed in landing membership or one line moved the chest.");
            }
        });
        yield return ("treasure_landed_disconnection_keeps_frozen_reward",()=>{
            var m=TreasureModel(TreasureKind.Large,2);TreasureCast(m,0);TreasureCast(m,1);var c=TreasureLand(m);var s=m.Capture();
            m.Step(new FrameContext(s.Steps+1,s.Steps+1,30,new[]{new MemberEvent(s.Steps+1,MemberEventKind.Deactivate,0,1)}),Array.Empty<SimulationInput>());
            TreasureOpen(m,1,c,1);s=m.Capture();Require(s.Players[0].Unavailable && s.Players[0].TreasuresCollected==1 &&
                s.Players[0].Coins==c.Coins/2+c.Coins%2 && s.Coins==c.Coins,"Landing ownership changed when a hauler disconnected before opening.");
        });
        yield return ("treasure_wallet_limit_does_not_credit_materials",()=>{
            var m=TreasureModel(TreasureKind.Small);var s=m.Capture();var p=s.Players.ToArray();p[0].Coins=p[0].CoinsEarned=int.MaxValue;
            TreasureRestore(m,p);TreasureCast(m,0);for(int i=0;i<180;i++)ShopStep(m);s=m.Capture();
            Require(s.Coins==int.MaxValue && s.Players[0].WoodEarned==0 && s.Players[0].FishbonesEarned==0 &&
                s.Players[0].TreasuresCollected==0 && s.Players[0].HookedChest==1,"Wallet failure partially settled a small chest.");
        });
        yield return ("treasure_codec_rejects_changed_contents_or_duplicate_rank",()=>{
            var m=TreasureModel(TreasureKind.Large,2);TreasureCast(m,0);TreasureCast(m,1);var s=m.Capture();
            foreach(bool rank in new[]{false,true})
            {
                var p=s.Players.ToArray();var c=s.Treasure.Chests.ToArray();if(rank)p[1].ChestJoinOrder=p[0].ChestJoinOrder;else c[0].Coins++;
                bool rejected=false;try{TreasureRestore(m,p,c);}catch(System.IO.InvalidDataException){rejected=true;}
                Require(rejected,"Corrupt treasure metadata was accepted.");
            }
        });
        yield return ("treasure_expanded_platform_spawn_stays_in_water_and_world",()=>{
            var tiles=PlatformRules.InitialTiles().Concat(Enumerable.Range(2,23).Select(x=>new PlatformTile(x,0,TileKind.Wood,0,4))).ToArray();
            var config=new TreasureConfig();var c=TreasureMotion.Waiting(0,TreasureKind.Small,0);
            for(int i=0;i<100;i++)
            {
                Require(TreasureMotion.Spawn(ref c,config,tiles,7381),"Expanded platform has no reachable spawn.");
                Require(TreasureMotion.Water(tiles,c.X,c.Z),"Chest spawned inside expanded platform or beyond world boundary.");
                c=TreasureMotion.Waiting(0,TreasureKind.Small,0,c.Generation);
            }
        });
        yield return ("treasure_spawn_separates_live_chests",()=>{
            var basis=IslandMap.CreatePlatform();var map=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,
                new FishingConfig(treasure:new TreasureConfig(initialDelayTicks:0,slotDelayTicks:0,driftSpeed:0,rareDriftSpeed:0)));
            var m=PullModel(map,2);ShopStep(m);var chests=m.Capture().Treasure.Chests;
            Require(chests.All(c=>c.Phase==TreasurePhase.Floating),"Default chest opportunities did not spawn.");
            for(int i=0;i<chests.Count;i++)for(int j=i+1;j<chests.Count;j++)
                Require(IslandMap.DistanceSquared(chests[i].X,chests[i].Z,chests[j].X,chests[j].Z)>=1000L*1000,"Live chest spawns overlap.");
        });
        yield return ("treasure_request_buffer_retries_receipts_reset_and_composition",()=>{
            var buffer=new TreasureRequestBuffer();var command=new TreasureCommand(1,1,1);
            Require(buffer.TryQueue(0,1,1) && !buffer.TryQueue(0,2,1),"Open request was not retained.");
            var input=new HookInput(100,0,1000,2000,true,roomRules:new RoomRulesCommand(false,2));
            var repeated=buffer.Compose(input);Require(repeated.Treasure.RequestId==1 && !repeated.Fire && repeated.RoomRules.RequestId==2,"Opening composition lost policy or sent a simultaneous cast.");
            Require(repeated.Encode().SequenceEqual(buffer.Compose(input).Encode()),"Retry changed the request.");
            Require(!buffer.Observe(0,TreasureResult.None,out _) && buffer.Pending,"Prediction acknowledged request.");
            Require(buffer.Observe(1,TreasureResult.TooFar,out var result) && result==TreasureResult.TooFar && !buffer.Pending,"Failure was replayed.");
            Require(buffer.TryQueue(1,1,1),"New click was not accepted.");
            var trade=new HookInput(0,0,rodAction:RodAction.Unequip,rodRequestId:3);
            Require(buffer.Compose(trade).Treasure.IsEmpty && buffer.Pending,"Open and equipment overlapped.");buffer.Reset();
            Require(!buffer.Pending && !buffer.TryQueue(uint.MaxValue,1,1),"Reset or exhausted counter failed.");
            var policy=new RoomRulesRequestBuffer();policy.TryQueue(new RoomRulesState(true),false);
            var opening=new HookInput(0,0,treasure:command);
            Require(policy.Compose(opening).Treasure.RequestId==1,"Policy swallowed an opening request.");
            var build=new PlatformBuildRequestBuffer();Require(build.Compose(opening,true).Treasure.RequestId==1,"Build preview swallowed opening.");
            build.TryQueue(0,TileKind.Wood,2,0);Require(build.Compose(opening,true).Treasure.IsEmpty,"Build transaction overlapped opening.");
        });
        yield return ("treasure_large_requires_open_freezes_members_and_remainders",()=>{
            var m=TreasureModel(TreasureKind.Large,3);TreasureCast(m,0);TreasureCast(m,1);var c=TreasureLand(m);var s=m.Capture();
            Require(s.Coins==0 && c.RewardMembers==3 && c.Owner==0 && s.Players.All(p=>p.HookedChest==0),"Landing paid early or changed haulers.");
            var p=s.Players.ToArray();p[2].X=c.X+500;p[2].Z=c.Z;p[2].HookX=p[2].X;p[2].HookZ=p[2].Z;TreasureRestore(m,p);
            TreasureOpen(m,2,c,1);s=m.Capture();
            Require(s.Players[2].LastTreasureResult==TreasureResult.Success && s.Players[2].TreasuresCollected==0 && s.Players[2].Coins==0,"Opener took the haulers' rewards.");
            for(int i=0;i<2;i++)Require(s.Players[i].Coins==c.Coins/2+(i==0?c.Coins%2:0) &&
                s.Players[i].WoodEarned==c.Wood/2+(i==0?c.Wood%2:0) && s.Players[i].FishbonesEarned==c.Bones/2+(i==0?c.Bones%2:0),"Reward split or remainders differ.");
            var codec=new HookGame();m.Restore(codec.Decode(codec.Encode(s)));TreasureOpen(m,2,c,1);TreasureOpen(m,2,c,2);
            Require(m.Capture().Coins==c.Coins && m.Capture().Players[2].LastTreasureResult==TreasureResult.NotLanded,"Repeated opening paid again.");
        });
        yield return ("treasure_failed_early_request_never_replays_after_landing",()=>{
            var m=TreasureModel(TreasureKind.Large,2);var c=m.Capture().Treasure.Chests[0];TreasureOpen(m,0,c,1);
            Require(m.Capture().Players[0].LastTreasureResult==TreasureResult.NotLanded,"Early opening had no final failure.");
            TreasureCast(m,0);TreasureCast(m,1);c=TreasureLand(m);TreasureOpen(m,0,c,1);
            Require(m.Capture().Coins==0,"Failed request replayed when the chest became openable.");
            TreasureOpen(m,0,c,2);Require(m.Capture().Coins==c.Coins,"Fresh request could not open the chest.");
        });
        yield return ("treasure_large_no_reverse_stealing_or_stamina_drain",()=>{
            var m=TreasureModel(TreasureKind.Large,2);var s=m.Capture();var p=s.Players.ToArray();var c=s.Treasure.Chests.ToArray();
            p[0].X=-2600;p[1].X=2600;for(int i=0;i<2;i++){p[i].Z=-2600;p[i].HookX=p[i].X;p[i].HookZ=p[i].Z;p[i].Stamina=0;p[i].RegenDelay=60;}
            c[0].X=0;c[0].Z=-3500;TreasureRestore(m,p,c);TreasureCast(m,0);TreasureCast(m,1);s=m.Capture();
            Require(s.Players.All(v=>v.HookedChest==1 && v.Stamina==0) && s.Treasure.Chests[0].Owner==0,"Chest reused fish stamina or reverse steal rules.");
        });
        yield return ("treasure_exit_uses_earliest_line_and_pauses_until_two",()=>{
            var m=TreasureModel(TreasureKind.Large,3);TreasureCast(m,0);TreasureCast(m,1);TreasureCast(m,2);
            var p=m.Capture().Players.ToArray();p[1].Stamina=1;TreasureRestore(m,p);
            ShopStep(m,new HookInput(0,0,cancel:true).Encode());var s=m.Capture();
            Require(s.Treasure.Chests[0].Owner==1 && s.Players[0].HookedChest==0 && s.Players[1].ChestJoinOrder==0,"Chest elected by stamina instead of join order.");
            ShopStep(m,new HookInput(0,0).Encode(),new HookInput(0,0,cancel:true).Encode());var before=m.Capture().Treasure.Chests[0];
            for(int i=0;i<12;i++)ShopStep(m);s=m.Capture();
            Require(s.Treasure.Chests[0].Owner==2 && s.Treasure.Chests[0].X==before.X && s.Treasure.Chests[0].Timer==before.Timer,"Single remaining hook moved or expired.");
            ShopStep(m,new HookInput(0,0).Encode(),new HookInput(0,0).Encode(),new HookInput(0,0,cancel:true).Encode());
            Require(m.Capture().Treasure.Chests[0].Phase==TreasurePhase.Floating && m.Capture().Coins==0,"Last exit did not release without rewards.");
        });
        yield return ("treasure_disconnect_before_landing_excludes_departed_player",()=>{
            var m=TreasureModel(TreasureKind.Large,3);TreasureCast(m,0);TreasureCast(m,1);TreasureCast(m,2);var s=m.Capture();
            m.Step(new FrameContext(s.Steps+1,s.Steps+1,30,new[]{new MemberEvent(s.Steps+1,MemberEventKind.Deactivate,0,1)}),Array.Empty<SimulationInput>());
            var c=TreasureLand(m);TreasureOpen(m,1,c,1);s=m.Capture();
            Require(c.Owner==1 && c.RewardMembers==6 && s.Players[0].Coins==0 && s.Coins==c.Coins,"Disconnected hauler retained a share or blocked opening.");
        });
        yield return ("treasure_checkpoint_preserves_hold_drift_and_landed_receipts",()=>{
            var a=TreasureModel(TreasureKind.Large,2);TreasureCast(a,0);TreasureCast(a,1);var b=TreasureModel(TreasureKind.Large,2);var codec=new HookGame();
            b.Restore(codec.Decode(codec.Encode(a.Capture())));
            for(int i=0;i<180;i++){ShopStep(a);ShopStep(b);Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Restored chest diverged.");}
            var c=a.Capture().Treasure.Chests[0];TreasureOpen(a,0,c,1);TreasureOpen(b,0,c,1);
            Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Opening after restore diverged.");
        });
        yield return ("treasure_material_limit_rolls_back_every_recipient",()=>{
            var m=TreasureModel(TreasureKind.Large,2);TreasureCast(m,0);TreasureCast(m,1);var c=TreasureLand(m);var s=m.Capture();var t=s.Tackle!;
            var inv=new InventoryState(2,t.Inventory.Items,int.MaxValue-1,t.Inventory.SpentCoins);
            TreasureRestore(m,tackle:new TackleState(t.Config,inv,t.Stock,t.Batch,t.NextRestock));TreasureOpen(m,0,c,1);s=m.Capture();
            Require(s.Coins==0 && s.Players.All(p=>p.TreasuresCollected==0 && p.WoodEarned==0 && p.FishbonesEarned==0) &&
                s.Treasure.Chests[0].Phase==TreasurePhase.Landed && s.Tackle!.Inventory.NextId==int.MaxValue-1 &&
                s.Players[0].LastTreasureResult==TreasureResult.LimitReached,"Failed inventory transaction partially rewarded the party.");
        });
        yield return ("treasure_platform_preview_blocks_build_over_floating_chest",()=>{
            var m=TreasureModel(TreasureKind.Small);var s=m.Capture();var p=s.Players[0];p.Wood=100;
            Require(PlatformRules.Check(IslandMap.CreatePlatform(),s,p,TileKind.Wood,-2,0)==BuildResult.Occupied,"Construction can bury a chest.");
        });
        yield return ("treasure_solo_omits_large_slots_and_command_families_are_exclusive",()=>{
            var basis=IslandMap.CreatePlatform();var map=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,new FishingConfig(treasure:new TreasureConfig()));
            Require(PullModel(map,1).Capture().Treasure.Chests.Count==2 && PullModel(map,2).Capture().Treasure.Chests.Count==3,"Solo large chest rule differs.");
            bool rejected=false;try{new HookInput(0,0,rodAction:RodAction.Unequip,rodRequestId:1,treasure:new TreasureCommand(1,1,1)).Encode();}catch(System.IO.InvalidDataException){rejected=true;}
            Require(rejected,"Opening shared another transaction family.");
        });
        yield return ("treasure_small_normal_cast_rewards_once",()=>{
            var model=TreasureModel(TreasureKind.Small);var chest=model.Capture().Treasure.Chests[0];
            TreasureCast(model,0);Require(model.Capture().Players[0].HookedChest==1,"Ordinary cast missed the small chest.");
            for(int i=0;i<180 && model.Capture().Players[0].TreasuresCollected==0;i++)ShopStep(model);
            var s=model.Capture();var p=s.Players[0];
            Require(p.TreasuresCollected==1 && p.Coins==chest.Coins && p.WoodEarned==chest.Wood && p.FishbonesEarned==chest.Bones,
                "Small chest did not award its complete rolled contents exactly once.");
            Require(p.Catches==0 && p.HookedChest==0 && s.Treasure.Chests[0].Phase==TreasurePhase.Waiting,"Chest reused fish statistics or remained attached.");
            for(int i=0;i<40;i++)ShopStep(model);
            Require(model.Capture().Coins==s.Coins,"Small chest rewarded more than once.");
        });
        yield return ("treasure_large_one_hook_waits_second_hook_moves",()=>{
            var model=TreasureModel(TreasureKind.Large,2);TreasureCast(model,0);var held=model.Capture().Treasure.Chests[0];
            Require(model.Capture().Players[0].HookedChest==1,"First line did not hold the large chest.");
            for(int i=0;i<30;i++)ShopStep(model);
            var still=model.Capture();Require(still.Treasure.Chests[0].X==held.X && still.Treasure.Chests[0].Z==held.Z && still.Coins==0,
                "One player moved or opened a two-hook chest.");
            TreasureCast(model,1);var shared=model.Capture();
            Require(shared.Players.All(p=>p.HookedChest==1) && shared.Treasure.Chests[0].Owner==0,"Second line did not assist the first owner.");
            ShopStep(model);Require(model.Capture().Treasure.Chests[0].X>held.X,"Two valid hooks did not move the large chest.");
        });
        yield return ("treasure_rare_drifts_expires_and_respawns",()=>{
            var model=TreasureModel(TreasureKind.Rare,config:new TreasureConfig(smallCount:0,rareCount:1,largeCount:0,
                initialDelayTicks:0,floatLifetimeTicks:30,respawnTicks:30,rareDriftSpeed:500));
            var before=model.Capture().Treasure.Chests[0];
            for(int i=0;i<10;i++)ShopStep(model);var moved=model.Capture().Treasure.Chests[0];
            Require(moved.X!=before.X || moved.Z!=before.Z,"Rare chest did not drift.");
            for(int i=0;i<25;i++)ShopStep(model);Require(model.Capture().Treasure.Chests[0].Phase==TreasurePhase.Waiting,"Expired opportunity never drifted away.");
            for(int i=0;i<35;i++)ShopStep(model);var next=model.Capture();
            Require(next.Treasure.Chests[0].Generation==before.Generation+1 && next.Treasure.Chests[0].Phase==TreasurePhase.Floating && next.Coins==0,
                "Missed chest did not refresh without granting rewards.");
        });
    }
}
