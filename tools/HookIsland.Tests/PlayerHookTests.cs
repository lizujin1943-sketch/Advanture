using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IslandMap PullMap(params Point[] spawns)
    {
        var map=TestMap();
        return new IslandMap(map.Shore,map.Pier,spawns.Concat(Enumerable.Repeat(new Point(-5000,5000),4-spawns.Length)),map.FishSpots);
    }
    private static IDeterministicSimulation<HookState> PullModel(IslandMap map,int count)
    {
        var model=new HookGame().Create();
        model.Initialize(new SimulationInitialization(new SessionDescriptor(Id,1,30,Enumerable.Range(0,count)
            .Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),new ByteString(map.Encode())));
        return model;
    }
    private static void WaitForPull(SimulationRunner<HookState> r,int caster=0)
    {
        for(int i=0;i<35 && r.InspectState().Players[caster].HookedPlayer<0;i++)Step(r);
        Require(r.InspectState().Players[caster].HookedPlayer>=0,"No active player pull.");
    }
    private static IEnumerable<(string,Action)> PlayerHookCases()
    {
        yield return ("player_hook_pulls_teammate_to_caster_without_awarding_coins",()=>{
            var r=Runner(2,PullMap(new Point(-5000,-5000),new Point(2000,-5000)));
            Step(r,new HookInput(0,0,2000,-5000,true));
            for(int i=0;i<90;i++)Step(r);
            var s=r.InspectState();
            Require(s.Players[1].X>=-4100 && s.Players[1].X<=-3900,"Teammate did not land beside the caster.");
            Require(s.Players[0].X==-5000 && s.Coins==0 && s.Players.All(p=>p.Catches==0),"Player hook moved caster or awarded a fish reward.");
        });
        yield return ("player_hook_right_click_releases_teammate",()=>{
            var r=Runner(2,PullMap(new Point(-5000,-5000),new Point(2000,-5000)));
            Step(r,new HookInput(0,0,2000,-5000,true));
            WaitForPull(r);for(int i=0;i<4;i++)Step(r);
            int moved=r.InspectState().Players[1].X;
            Require(moved<2000 && moved>-3900,"No active pull to cancel.");
            Step(r,new HookInput(0,0,cancel:true));
            for(int i=0;i<30;i++)Step(r);
            Require(r.InspectState().Players[1].X==moved,"Cancel did not release the teammate immediately.");
        });
        yield return ("player_pull_stops_at_concave_shore_instead_of_crossing_water",()=>{
            var original=PullMap(new Point(-4000,6000),new Point(4000,6000));
            var map=new IslandMap(new[]{new Point(-10000,-10000),new Point(10000,-10000),new Point(10000,10000),
                new Point(3000,10000),new Point(3000,-1000),new Point(-3000,-1000),new Point(-3000,10000),new Point(-10000,10000)},
                original.Pier,original.Spawns,original.FishSpots);
            var r=Runner(2,map);Step(r,new HookInput(0,0,4000,6000,true));
            for(int i=0;i<100;i++)
            {
                Step(r);var p=r.InspectState().Players[1];Require(map.CanStand(p.X,p.Z),"Pulled teammate crossed the walkable shore.");
            }
            Require(r.InspectState().Players[1].X>=3330 && r.InspectState().Players[1].X<4000,"Teammate was not pulled up to the shore edge.");
        });
        yield return ("player_hook_simultaneous_claims_have_one_owner",()=>{
            var r=Runner(3,PullMap(new Point(-5000,-5000),new Point(-5000,-4700),new Point(2000,-5000)));
            var fire=new HookInput(0,0,2000,-5000,true);Step(r,fire,fire);WaitForPull(r);
            var s=r.InspectState();
            Require(s.Players[2].PulledBy==0 && s.Players[0].HookedPlayer==2 && s.Players[1].HookedPlayer<0 &&
                s.Players.Sum(p=>p.PlayerHooks)==1,"Two hooks claimed the same teammate or ownership was unstable.");
        });
        yield return ("player_hook_mutual_casts_do_not_form_a_pull_cycle",()=>{
            var r=Runner(2,PullMap(new Point(-5000,-5000),new Point(1000,-5000)));
            Step(r,new HookInput(0,0,1000,-5000,true),new HookInput(0,0,-5000,-5000,true));WaitForPull(r);
            Require(r.InspectState().Players.Sum(p=>p.PlayerHooks)==1,"Mutual hooks created a chain/cycle.");
            for(int i=0;i<90;i++)Step(r);
            Require(r.InspectState().Players.All(p=>p.PulledBy<0 && p.HookedPlayer<0),"Mutual cast left a stuck player.");
        });
        yield return ("player_hook_shop_protection_is_synced_and_ends_on_exit",()=>{
            var r=Runner(2,PullMap(new Point(-8000,-1600),new Point(-2800,-1600)));
            for(int i=0;i<60;i++)Step(r,new HookInput(0,0,-2800,-1600,i==0),new HookInput(1000,0,8000,-1600,true,shopping:true));
            var s=r.InspectState();
            Require(s.Players[1].Shopping && s.Players[1].X==-2800 && s.Players[1].Casts==0 && s.Players[0].PlayerHooks==0,
                "Shopping did not protect the teammate or stop their own movement/fire.");
            Step(r,new HookInput(0,0,-2800,-1600,true));WaitForPull(r);
            Require(!r.InspectState().Players[1].Shopping,"Shop protection persisted after closing.");
        });
        yield return ("player_hook_cannot_claim_shop_protection_from_the_shore",()=>{
            var r=Runner(2,PullMap(new Point(-5000,-5000),new Point(2000,-5000)));
            for(int i=0;i<30;i++)Step(r,new HookInput(0,0,2000,-5000,i==0),new HookInput(0,0,shopping:true));
            Require(!r.InspectState().Players[1].Shopping && r.InspectState().Players[0].PlayerHooks==1,
                "An out-of-range shopping flag granted immunity.");
        });
        yield return ("player_hook_victim_cannot_walk_or_fire_while_pulled",()=>{
            var r=Runner(2,PullMap(new Point(-5000,-5000),new Point(2000,-5000)));
            Step(r,new HookInput(0,0,2000,-5000,true));WaitForPull(r);
            int before=r.InspectState().Players[1].X;
            Step(r,default,new HookInput(1000,0,25000,-5000,true,shopping:true));
            var p=r.InspectState().Players[1];
            Require(p.X==before-400 && p.Casts==0 && !p.Shopping,"Victim input escaped the pull or fired a new hook.");
        });
        yield return ("player_hook_protection_blocks_repeat_hooks_then_expires",()=>{
            var r=Runner(2,PullMap(new Point(-5000,-5000),new Point(2000,-5000)));
            Step(r,new HookInput(0,0,2000,-5000,true));WaitForPull(r);
            while(r.InspectState().Players[1].PulledBy>=0)Step(r);
            Require(r.InspectState().Players[1].ProtectionTicks==90,"Release did not grant three seconds of protection.");
            for(int i=0;i<12;i++)Step(r,new HookInput(-1000,0));
            Step(r,new HookInput(0,0,-4000,-5000,true));
            for(int i=0;i<40;i++)Step(r);
            Require(r.InspectState().Players[0].PlayerHooks==1 && r.InspectState().Players[1].X==-4000,"Protected teammate was pulled again.");
            for(int i=0;i<60;i++)Step(r);
            Step(r,new HookInput(0,0,-4000,-5000,true));WaitForPull(r);
            Require(r.InspectState().Players[0].PlayerHooks==2,"Protection never expired.");
        });
        yield return ("player_hook_fast_cast_hits_nearest_teammate_not_lowest_slot",()=>{
            var map=PullMap(new Point(-5000,-5000),new Point(-3200,-5000),new Point(-3500,-5000));
            var model=PullModel(map,3);var s=model.Capture();var players=s.Players.ToArray();
            players[0].SpeedLevel=3;players[0].Coins=330;model.Restore(new HookState(0,players,s.Fish,0));
            ShopStep(model,new HookInput(0,0,5000,-5000,true).Encode());ShopStep(model);
            Require(model.Capture().Players[0].HookedPlayer==2 && model.Capture().Players[1].PulledBy<0,
                "Fast hook skipped the nearer teammate between frames.");
        });
        yield return ("player_hook_fast_cast_cannot_skip_the_tree",()=>{
            var map=PullMap(new Point(-2500,-350),new Point(-2500,4000));
            var model=PullModel(map,2);var s=model.Capture();var players=s.Players.ToArray();
            players[0].SpeedLevel=3;players[0].Coins=330;model.Restore(new HookState(0,players,s.Fish,0));
            ShopStep(model,new HookInput(0,0,-2500,4000,true).Encode());ShopStep(model);ShopStep(model);
            Require(model.Capture().Players[0].Phase==HookPhase.Returning && model.Capture().Players[0].PlayerHooks==0,
                "Upgraded hook crossed a tree between two flight samples.");
        });
        yield return ("player_hook_preserves_victims_inflight_fish_reward",()=>{
            var map=PullMap(new Point(-5000,-5000),new Point(2000,-5000));
            var model=PullModel(map,2);var s=model.Capture();var players=s.Players.ToArray();var fish=s.Fish.ToArray();
            players[1].Phase=HookPhase.Returning;players[1].FishId=0;players[1].HookX=2000;players[1].HookZ=-24000;
            fish[0].Owner=1;fish[0].Phase=FishPhase.Hooked;fish[0].X=2000;fish[0].Z=-24000;fish[0].Y=1250;
            model.Restore(new HookState(0,players,fish,0));
            ShopStep(model,new HookInput(0,0,2000,-5000,true).Encode());
            for(int i=0;i<90;i++)ShopStep(model);
            s=model.Capture();
            Require(s.Players[0].PlayerHooks==1 && s.Players[1].Catches==1 && s.Coins==10,"Pull lost or duplicated the victim's existing catch.");
        });
        yield return ("player_hook_restore_replays_pull_and_rejects_orphan_ownership",()=>{
            var map=PullMap(new Point(-5000,-5000),new Point(2000,-5000));var a=Runner(2,map);
            Step(a,new HookInput(0,0,2000,-5000,true));WaitForPull(a);Step(a);
            var b=Runner(2,map);b.Restore(a.CaptureCheckpoint());
            for(int i=0;i<100;i++){Step(a);Step(b);}
            Require(CheckpointCodec.Hash(a.CaptureCheckpoint())==CheckpointCodec.Hash(b.CaptureCheckpoint()),"Restored pull or protection diverged.");
            var s=a.InspectState();var players=s.Players.ToArray();players[1].PulledBy=0;
            bool rejected=false;var codec=new HookGame();
            try{codec.Decode(codec.Encode(new HookState(s.Steps,players,s.Fish,s.Coins)));}catch(System.IO.InvalidDataException){rejected=true;}
            Require(rejected,"Snapshot accepted a pulled player without an owning hook.");
        });
        yield return ("player_hook_pull_has_a_bounded_duration",()=>{
            var map=PullMap(new Point(-5000,-5000),new Point(2000,-5000));var r=Runner(2,map);
            Step(r,new HookInput(0,0,2000,-5000,true));WaitForPull(r);
            var s=r.InspectState();var players=s.Players.ToArray();players[0].PullTicks=59;
            var model=PullModel(map,2);model.Restore(new HookState(s.Steps,players,s.Fish,s.Coins));
            ShopStep(model);s=model.Capture();
            Require(s.Players[1].PulledBy<0 && s.Players[1].ProtectionTicks==90,"A moving caster can keep the victim locked indefinitely.");
        });
        yield return ("player_hook_cancel_protection_is_independent_of_slot_order",()=>{
            foreach(int caster in new[]{0,1})
            {
                var r=Runner(2,PullMap(new Point(-5000,-5000),new Point(2000,-5000)));
                var fire=new HookInput(0,0,caster==0?2000:-5000,-5000,true);
                Step(r,caster==0?fire:default,caster==1?fire:default);WaitForPull(r,caster);
                var cancel=new HookInput(0,0,cancel:true);
                Step(r,caster==0?cancel:default,caster==1?cancel:default);
                Require(r.InspectState().Players[1-caster].ProtectionTicks==90,"Cancel gave different immunity duration by player slot.");
            }
        });
        yield return ("player_pull_stops_at_shop_when_caster_moves_behind_it",()=>{
            var map=PullMap(new Point(-5000,-5000),new Point(2000,-5000));var r=Runner(2,map);
            Step(r,new HookInput(0,0,2000,-5000,true));WaitForPull(r);
            var s=r.InspectState();var players=s.Players.ToArray();players[0].X=2000;players[0].Z=6000;
            var model=PullModel(map,2);model.Restore(new HookState(s.Steps,players,s.Fish,s.Coins));
            for(int i=0;i<90;i++)
            {
                ShopStep(model);var target=model.Capture().Players[1];
                Require(map.CanStand(target.X,target.Z),"Return pull crossed the store collider.");
            }
            var p=model.Capture().Players[1];
            Require(p.Z>-5000 && p.Z<0 && p.PulledBy<0,"Blocked pull did not stop and release before the shop.");
        });
    }
}
