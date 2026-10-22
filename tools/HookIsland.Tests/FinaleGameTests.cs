using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IslandMap FinaleGameMap(FinaleConfig? config=null,bool enemies=false)
    {
        var b=FinaleRuleMap();
        return new IslandMap(b.Shore,b.Pier,b.Spawns,new[]{new Point(40000,40000)},b.Platform,
            new FishingConfig(commissions:b.Fishing!.Commissions,finale:config??new FinaleConfig(summonTicks:2,relocateTicks:2,holdTicks:5),
                enemies:enemies?new EnemyConfig(piranhaCount:0,initialDelayTicks:18000):EnemyConfig.Disabled));
    }
    private static HookState FinaleCopy(HookState s,FinaleState finale,Fisher[]? players=null,CommissionState? commissions=null,Fish[]? fish=null,EnemyState? enemies=null)=>
        new HookState(s.Steps,players??s.Players.ToArray(),fish??s.Fish.ToArray(),s.Coins,s.Tiles,s.Driftwood,s.Tackle,enemies??s.Enemies,s.RoomRules,s.Treasure,commissions??s.Commissions,s.Ocean,finale);
    private static IDeterministicSimulation<HookState> FinaleReadyModel(int count=1,FinaleConfig? config=null,bool hooked=false,int completed=0,int progress=0,bool enemies=false)
    {
        var map=FinaleGameMap(config,enemies);var m=PullModel(map,count);
        for(int i=0;i<3;i++)CommissionCatch(m);
        var s=m.Capture();var players=s.Players.ToArray();
        for(int i=0;i<count;i++)
        {
            players[i].X=(i*2-count+1)*450;players[i].Z=-2600;
            players[i].Phase=HookPhase.Ready;players[i].Cooldown=0;players[i].HookX=players[i].X;players[i].HookZ=players[i].Z;
            if(hooked){players[i].HookedFinale=true;players[i].HookX=0;players[i].HookZ=-5100;players[i].Phase=HookPhase.Returning;}
        }
        m.Restore(FinaleCopy(s,new FinaleState(FinalePhase.Surfaced,completed:completed,progress:progress,spentBait:3,x:0,z:-5100,previousX:completed>0?5100:0),players));
        return m;
    }
    private static void FinaleReject(Action action)
    {bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Require(rejected,"Invalid finale state was accepted.");}
    private static IEnumerable<(string,Action)> FinaleGameCases()
    {
        yield return ("finale_game_capture_summons_and_serializes_bait",()=>{
            var map=FinaleGameMap();var m=PullModel(map,1);
            for(int i=0;i<3;i++)CommissionCatch(m);
            var s=m.Capture();Require(s.Finale.Phase==FinalePhase.Summoning && s.Finale.SpentBait==3 && s.Commissions.Bait==3,"Successful final commission did not atomically start the finale.");
            var codec=new HookGame();var decoded=codec.Decode(codec.Encode(s));
            Require(decoded.Finale.Phase==FinalePhase.Summoning && decoded.Finale.SpentBait==3 && decoded.Tackle!.Config.Finale.Enabled,"Snapshot lost finale configuration or spent bait.");
        });
        yield return ("finale_game_hook_hits_target_and_cancel_retracts_empty",()=>{
            var m=FinaleReadyModel(config:new FinaleConfig(holdTicks:150));
            ShopStep(m,new HookInput(0,0,0,-5100,true).Encode());
            for(int i=0;i<20 && !m.Capture().Players[0].HookedFinale;i++)ShopStep(m);
            var s=m.Capture();Require(s.Players[0].HookedFinale && s.Players[0].HookX==0 && s.Players[0].HookZ==-5100,"Flying hook did not attach to the surfaced giant.");
            int stamina=s.Players[0].Stamina;ShopStep(m,new HookInput(0,0,cancel:true).Encode());s=m.Capture();
            Require(!s.Players[0].HookedFinale && s.Finale.Progress==0 && s.Players[0].Stamina==stamina,"Cancellation retained progress, charged more stamina or kept the line.");
            for(int i=0;i<20;i++)ShopStep(m);
            Require(m.Capture().Players[0].Phase==HookPhase.Ready,"Cancelled giant hook did not return empty.");
        });
        yield return ("finale_game_victory_freezes_gameplay_clock_and_results",()=>{
            var m=FinaleReadyModel(config:new FinaleConfig(holdTicks:2),hooked:true,completed:2,progress:1);ShopStep(m);
            var won=m.Capture();Require(won.Won && won.RoundFinished && !won.GameOver && won.Finale.VictoryTick==won.Steps,"Final hold did not yield a distinct successful round outcome.");
            Require(won.Players.All(p=>!p.HookedFinale && p.Phase==HookPhase.Ready),"Victory retained active hooks.");
            var codec=new HookGame();var frozen=codec.Encode(won);
            for(int i=0;i<400;i++)ShopStep(m,new HookInput(1000,0,10000,10000,true).Encode());
            Require(frozen.SequenceEqual(codec.Encode(m.Capture())),"Input or elapsed confirmation frames changed completed gameplay.");
            Require(codec.Decode(codec.Encode(m.Capture())).Won,"Frozen victory no longer decodes.");
        });
        yield return ("finale_game_nearer_fish_blocks_giant",()=>{
            var m=FinaleReadyModel(2);var s=m.Capture();var fish=s.Fish.ToArray();var p=s.Players.ToArray();
            p[0].X=0;p[1].X=2000;p[1].FishId=0;p[1].Phase=HookPhase.Returning;p[1].HookX=0;p[1].HookZ=-3400;
            fish[0].X=0;fish[0].Z=-3400;fish[0].Y=1250;fish[0].Timer=0;fish[0].Owner=1;fish[0].Phase=FishPhase.Hooked;
            m.Restore(FinaleCopy(s,s.Finale,p,fish:fish));ShopStep(m,new HookInput(0,0,0,-5100,true).Encode());
            Require(!m.Capture().Players[0].HookedFinale && m.Capture().Players[0].FishId==0,"Giant ignored the nearer swept fish collision.");
        });
        yield return ("finale_game_opposite_hooks_cooperate_without_cutting",()=>{
            var m=FinaleReadyModel(2,config:new FinaleConfig(holdTicks:150),hooked:true);var s=m.Capture();var p=s.Players.ToArray();
            p[0].X=-2700;p[0].Z=-2700;p[1].X=2700;p[1].Z=-2700;FinaleRules.ClearLine(ref p[1]);p[1].Phase=HookPhase.Ready;
            Require(CooperativeFishing.IsOpposite(new Point(0,-5100),new Point(p[0].X,p[0].Z),new Point(p[1].X,p[1].Z)),"Fixture is not on opposite sides of the giant's hook angle.");
            m.Restore(FinaleCopy(s,s.Finale,p));ShopStep(m,default(HookInput).Encode(),new HookInput(0,0,0,-5100,true).Encode());
            for(int i=0;i<20 && !m.Capture().Players[1].HookedFinale;i++)ShopStep(m);
            s=m.Capture();Require(s.Players.All(x=>x.HookedFinale) && s.Finale.Progress>0,"Giant applied ordinary reverse stealing or refused an opposite hook.");
        });
        foreach(bool exhausted in new[]{false,true})yield return (exhausted?"finale_game_zero_stamina_join_preserves_existing_hook":"finale_game_contact_beyond_rod_range_preserves_existing_hook",()=>{
            var m=FinaleReadyModel(2,config:new FinaleConfig(holdTicks:150),hooked:true);var s=m.Capture();var p=s.Players.ToArray();
            FinaleRules.ClearLine(ref p[0]);p[0].Phase=HookPhase.Ready;
            if(exhausted){p[0].Stamina=0;p[0].RegenDelay=60;}else{p[0].X=0;p[0].Z=-1000;p[1].X=2300;}
            m.Restore(FinaleCopy(s,s.Finale,p));ShopStep(m,new HookInput(0,0,0,-5100,true).Encode());
            for(int i=0;i<12;i++)ShopStep(m);s=m.Capture();
            Require(!s.Players[0].HookedFinale && s.Players[1].HookedFinale && s.Finale.Progress==0,"Rejected join cut an existing giant line or contributed progress.");
        });
        yield return ("finale_game_exhaustion_on_last_frame_prevents_victory",()=>{
            var m=FinaleReadyModel(config:new FinaleConfig(holdTicks:2),hooked:true,completed:2,progress:1);var s=m.Capture();var p=s.Players.ToArray();
            p[0].Stamina=12000;m.Restore(FinaleCopy(s,s.Finale,p));ShopStep(m);s=m.Capture();
            Require(!s.Won && s.Finale.Completed==2 && s.Finale.Progress==0 && !s.Players[0].HookedFinale && s.Players[0].Stamina==0,"Last-frame exhausted line still won.");
        });
        foreach(int health in new[]{100,1})yield return ($"finale_game_waterball_last_frame_health_{health}",()=>{
            var m=FinaleReadyModel(2,config:new FinaleConfig(holdTicks:2),hooked:true,completed:2,progress:1,enemies:true);
            var s=m.Capture();var p=s.Players.ToArray();p[0].Vitals.Health=health;
            var ball=new Waterball{Id=1,OriginX=-1400,OriginZ=-2600,X=-1134,Z=-2600,DirectionX=1000000,Distance=266,TravelRemainder=20,KnockbackDistance=4000};
            var e=s.Enemies;m.Restore(FinaleCopy(s,s.Finale,p,enemies:new EnemyState(e.Piranhas,e.Monster,new[]{ball},1,0,1)));
            ShopStep(m);s=m.Capture();
            Require(!s.Won && s.Finale.Completed==2 && s.Finale.Progress==0 && !s.Players[0].HookedFinale && s.Players[1].HookedFinale && s.Enemies.WaterHits==1,"Last-frame waterball or downing retained a winning line.");
            new HookGame().Decode(new HookGame().Encode(s));
        });
        yield return ("finale_game_teammate_tow_clears_hook_before_last_frame",()=>{
            var m=FinaleReadyModel(3,config:new FinaleConfig(holdTicks:6),hooked:true,completed:2,progress:1);var s=m.Capture();var p=s.Players.ToArray();
            p[0].X=0;p[0].Z=-2600;p[1].X=2000;p[1].Z=-2600;p[2].X=0;p[2].Z=-1400;FinaleRules.ClearLine(ref p[2]);p[2].Phase=HookPhase.Ready;
            m.Restore(FinaleCopy(s,s.Finale,p));ShopStep(m,default(HookInput).Encode(),default(HookInput).Encode(),new HookInput(0,0,0,-2600,true).Encode());
            for(int i=0;i<4;i++)ShopStep(m);s=m.Capture();
            Require(!s.Won && !s.Players[0].HookedFinale && s.Players[0].PulledBy==2 && s.Finale.Progress==0,$"Towed giant hook contributed the final hold frame: won={s.Won}, progress={s.Finale.Progress}, pulled={s.Players[0].PulledBy}, caster={s.Players[2].Phase}/{s.Players[2].HookX},{s.Players[2].HookZ}.");
        });
        yield return ("finale_game_surface_blocks_build_preview_and_transaction",()=>{
            var map=FinaleGameMap();var m=FinaleReadyModel();var before=m.Capture();
            Require(PlatformRules.Check(map,before,before.Players[0],TileKind.Wood,0,-2)==BuildResult.Occupied,"Preview allowed a tile through the surfaced giant.");
            ShopStep(m,new HookInput(0,0,build:TileKind.Wood,buildX:0,buildZ:-2,buildId:1).Encode());var after=m.Capture();
            Require(after.Players[0].LastBuildResult==BuildResult.Occupied && after.Tiles.Count==before.Tiles.Count && after.Players[0].Wood==before.Players[0].Wood,"Build transaction disagreed with preview or spent materials.");
        });
        for(int rotation=0;rotation<4;rotation++)
        {
            int turn=rotation;
            yield return ($"finale_game_tangent_build_restores_shore_{turn}",()=>{
                Point Rotate(int x,int z){for(int i=0;i<turn;i++){int old=x;x=-z;z=old;}return new Point(x,z);}
                var config=new FinaleConfig(edgeOffset:3800);var map=FinaleGameMap(config);var m=PullModel(map,1);
                for(int i=0;i<3;i++)CommissionCatch(m);
                var s=m.Capture();var p=s.Players.ToArray();var inventory=new InventorySystem(s.Tackle!.Inventory);
                Require(inventory.CreditMaterial(0,ItemKind.Wood,20,new Point(p[0].X,p[0].Z))==20,"Build fixture lacks its material budget.");
                p[0].Wood+=20;p[0].WoodEarned+=20;p[0].Phase=HookPhase.Ready;p[0].Cooldown=0;
                var position=Rotate(5800,0);
                var giant=new FinaleState(FinalePhase.Surfaced,spentBait:3,x:position.X,z:position.Z,direction:(1+turn)%4);
                var tackle=new TackleState(s.Tackle.Config,inventory.Capture(),s.Tackle.Stock,s.Tackle.Batch,s.Tackle.NextRestock);
                m.Restore(new HookState(s.Steps,p,s.Fish,s.Coins,s.Tiles,s.Driftwood.Select(d=>DriftwoodMotion.Create(d.Id,300)),tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,giant));
                uint request=0;var codec=new HookGame();
                foreach(var cell in new[]{new Point(1,2),new Point(2,2),new Point(3,2),new Point(4,2),new Point(4,1)})
                {
                    var build=Rotate(cell.X,cell.Z);s=m.Capture();
                    Require(PlatformRules.Check(map,s,s.Players[0],TileKind.Wood,build.X,build.Z)==BuildResult.Success,"Tangent route preview rejected a legal tile.");
                    ShopStep(m,Build(TileKind.Wood,build.X,build.Z,++request));s=m.Capture();
                    Require(s.Players[0].LastBuildResult==BuildResult.Success,"Tangent route transaction disagreed with its preview.");
                    var restored=PullModel(map,1);restored.Restore(codec.Decode(codec.Encode(s)));
                    Require(codec.Encode(restored.Capture()).SequenceEqual(codec.Encode(s)),"A legally built platform cannot restore unchanged.");
                }
                Require(m.Capture().Tiles.Count==14 && m.Capture().Players[0].Wood==0,"Tangent route did not charge exactly five tiles.");
            });
        }
        yield return ("finale_game_saved_hold_replays_with_exact_stamina",()=>{
            var config=new FinaleConfig(holdTicks:15,staminaMilliPerSecond:1);var a=FinaleReadyModel(3,config,hooked:true);ShopStep(a);
            var b=PullModel(FinaleGameMap(config),3);var codec=new HookGame();b.Restore(codec.Decode(codec.Encode(a.Capture())));
            for(int i=0;i<20;i++)
            {
                ShopStep(a);ShopStep(b);
                Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Restoring a fractional shared drain changed continuation.");
            }
            Require(a.Capture().Finale.Completed==1,"Saved hold failed to complete its round.");
        });
        yield return ("finale_game_rejects_malformed_stages_and_bait",()=>{
            var m=FinaleReadyModel(hooked:true);var s=m.Capture();var codec=new HookGame();
            var invalid=new[]{
                new FinaleState((FinalePhase)99),new FinaleState(timer:1),
                new FinaleState(FinalePhase.Surfaced,spentBait:2,x:0,z:-5100),
                new FinaleState(FinalePhase.Surfaced,spentBait:3,x:0,z:-5101),
                new FinaleState(FinalePhase.Surfaced,spentBait:3,x:0,z:-3100),
                new FinaleState(FinalePhase.Surfaced,completed:3,spentBait:3,x:0,z:-5100,previousX:5100),
                new FinaleState(FinalePhase.Surfaced,progress:5,spentBait:3,x:0,z:-5100),
                new FinaleState(FinalePhase.Summoning,timer:3,spentBait:3),
                new FinaleState(FinalePhase.Submerged,completed:0,spentBait:3,x:0,z:-5100),
                new FinaleState(FinalePhase.Victory,completed:3,spentBait:3,x:0,z:-5100,previousX:5100,victoryTick:s.Steps+1)};
            foreach(var bad in invalid)FinaleReject(()=>codec.Decode(codec.Encode(FinaleCopy(s,bad))));
        });
        yield return ("finale_game_rejects_invalid_line_ownership_and_remainder",()=>{
            var m=FinaleReadyModel(hooked:true);var s=m.Capture();var codec=new HookGame();
            var edits=new Action<Fisher[]>[]{p=>p[0].HookX++,p=>p[0].X=10000,p=>p[0].Stamina=0,p=>p[0].Unavailable=true,
                p=>p[0].Phase=HookPhase.Ready,p=>p[0].FinaleDrainRemainder=12,p=>{p[0].HookedFinale=false;p[0].FinaleDrainRemainder=1;}};
            foreach(var edit in edits){var p=s.Players.ToArray();edit(p);FinaleReject(()=>codec.Decode(codec.Encode(FinaleCopy(s,s.Finale,p))));}
            var players=s.Players.ToArray();var fish=s.Fish.ToArray();players[0].FishId=0;
            fish[0].Owner=0;fish[0].Phase=FishPhase.Hooked;fish[0].X=0;fish[0].Z=-5100;fish[0].Y=1250;
            FinaleReject(()=>codec.Decode(codec.Encode(FinaleCopy(s,s.Finale,players,fish:fish))));
        });
        yield return ("finale_game_requires_platform_and_reachable_bait",()=>{
            FinaleReject(()=>new FishingConfig(finale:new FinaleConfig()));
            FinaleReject(()=>new FishingConfig(commissions:new CommissionConfig(new[]{new CommissionDefinition(1,1)}),finale:new FinaleConfig()));
            var map=FinaleGameMap();FinaleReject(()=>new IslandMap(map.Shore,map.Pier,map.Spawns,map.FishSpots,fishing:map.Fishing));
            var old=map.Encode();old[3]=0x44;FinaleReject(()=>IslandMap.Decode(old));
            var codec=new HookGame();var state=codec.Encode(FinaleReadyModel().Capture());
            bool rejected=false;try{codec.Decode(state.AsSpan(0,state.Length-54));}catch(IOException){rejected=true;}
            Require(rejected,"State without FIN1 was accepted.");
        });
    }
}
