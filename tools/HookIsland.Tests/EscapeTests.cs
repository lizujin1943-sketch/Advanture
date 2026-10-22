using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> EscapeCases()
    {
        yield return ("fish_escape_starts_at_hook_without_reward_or_respawn",()=>{
            var model=RodModel();SetRodStamina(model,1);AttachRodFish(model,10000,9,3,4000);
            var before=model.Capture().Fish[0];ShopStep(model);var state=model.Capture();var after=state.Fish[0];
            Require(after.X==before.X && after.Z==before.Z && after.Y==before.Y,"Escaping fish teleported away from its hook.");
            Require(after.Species==before.Species && after.WeightGrams==before.WeightGrams && after.Grade==before.Grade && after.Generation==before.Generation,"Escape replaced the fish before its animation.");
            Require(after.Owner==-1 && state.Players[0].FishId==-1 && state.Players[0].Escapes==1,"Escape ownership/counter invalid.");
            Require(state.Coins==0 && state.Players[0].Fishbones==0 && state.Players[0].Catches==0,"Escaping fish awarded loot.");
        });
        yield return ("fish_escape_struggles_arcs_into_open_water_then_submerges_before_respawn",()=>{
            var model=RodModel();SetRodStamina(model,1);AttachRodFish(model,10000,9,3,1);ShopStep(model);
            var first=model.Capture().Fish[0];var map=IslandMap.CreatePlatform();
            Require(first.Escape.Active && first.Phase==FishPhase.Escaping,"Missing authoritative escape phase.");
            Require(model.Capture().Tiles.All(t=>Math.Abs(first.Escape.LandingX-t.X*2000)>=1450 || Math.Abs(first.Escape.LandingZ-t.Z*2000)>=1450),"Escape landed on or clipped a platform.");
            bool rose=false,splashed=false;int previousY=first.Y;
            for(int i=1;i<CatchEscapeMotion.TotalTicks;i++)
            {
                ShopStep(model);var f=model.Capture().Fish[0];
                Require(f.Phase==FishPhase.Escaping && f.Generation==first.Generation,"Fish vanished before fully submerging.");
                if(f.Escape.Tick<=CatchEscapeMotion.StruggleTicks)Require(f.X==first.X && f.Z==first.Z,"Struggle drifted from detachment point.");
                rose|=f.Y>previousY;splashed|=f.Y<=0;previousY=f.Y;
                Require(f.Owner==-1 && model.Capture().Players[0].FishId==-1,"Escaping fish was claimed again.");
            }
            Require(rose && splashed && model.Capture().Fish[0].Y==-700,"Missing arc, water entry or full submersion.");
            ShopStep(model);var end=model.Capture();
            Require(end.Fish[0].Phase==FishPhase.Waiting && end.Fish[0].Generation==first.Generation+1,"Submerged fish did not enter respawn delay.");
            Require(end.Coins==0 && end.Players[0].Catches==0 && end.Players[0].Fishbones==0,"Escape awarded loot.");
        });
        yield return ("fish_escape_checkpoint_restores_every_animation_phase_exactly",()=>{
            var first=RodModel();SetRodStamina(first,1);AttachRodFish(first,10000,9,3,4000);ShopStep(first);
            var second=RodModel();var codec=new HookGame();
            for(int i=0;i<90;i++)
            {
                second.Restore(codec.Decode(codec.Encode(first.Capture())));
                ShopStep(first);ShopStep(second);
                Require(codec.Encode(first.Capture()).SequenceEqual(codec.Encode(second.Capture())),"Escape restore diverged at frame "+i);
            }
        });
        yield return ("fish_escape_codec_rejects_invalid_phase_time_and_position",()=>{
            var model=RodModel();SetRodStamina(model,1);AttachRodFish(model,10000,9,3,4000);ShopStep(model);
            var s=model.Capture();var codec=new HookGame();
            for(int n=0;n<5;n++)
            {
                var fish=s.Fish.ToArray();
                if(n==0)fish[0].Escape.Tick=-1;
                if(n==1)fish[0].Escape.Tick=CatchEscapeMotion.TotalTicks+1;
                if(n==2)fish[0].X++;
                if(n==3)fish[0].Phase=FishPhase.Jumping;
                if(n==4)fish[0].Escape.LandingX=100000;
                bool rejected=false;
                try{codec.Decode(codec.Encode(new HookState(s.Steps,s.Players,fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies)));}
                catch(InvalidDataException){rejected=true;}
                Require(rejected,"Invalid escape checkpoint accepted: "+n);
            }
        });
        yield return ("fish_escape_landing_avoids_expanded_platform_and_world_edge",()=>{
            var map=IslandMap.CreatePlatform();
            var tiles=Enumerable.Range(-8,17).SelectMany(x=>Enumerable.Range(-8,17).Select(z=>new PlatformTile(x,z,TileKind.Wood,0,0))).ToArray();
            foreach(var origin in new[]{new Point(0,0),new Point(48000,0),new Point(-48000,48000)})
            {
                var landing=CatchEscapeMotion.Landing(map,tiles,origin,new Point(origin.X+1,origin.Z));
                Require(Math.Abs(landing.X)<=49500 && Math.Abs(landing.Z)<=49500,"Landing outside world.");
                Require(tiles.All(t=>Math.Abs(landing.X-t.X*2000)>=1450 || Math.Abs(landing.Z-t.Z*2000)>=1450),"Landing clipped expanded deck.");
            }
        });
        yield return ("fish_escape_piranha_cannot_bite_or_be_rehooked_until_submerged",()=>{
            var config=new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30);
            var baseMap=IslandMap.CreatePlatform();var map=new IslandMap(baseMap.Shore,baseMap.Pier,baseMap.Spawns,baseMap.FishSpots,baseMap.Platform,new FishingConfig(enemies:config));
            var model=PullModel(map,1);ShopStep(model,new HookInput(0,0,-4800,0,true).Encode());
            for(int i=0;i<60 && model.Capture().Players[0].HookedPiranha==0;i++)ShopStep(model);
            var before=model.Capture();Require(before.Players[0].HookedPiranha==1,"Fixture failed to hook piranha.");
            var players=before.Players.ToArray();players[0].Stamina=0;
            model.Restore(new HookState(before.Steps,players,before.Fish,before.Coins,before.Tiles,before.Driftwood,before.Tackle,before.Enemies));ShopStep(model);
            var escaped=model.Capture();var fish=escaped.Enemies.Piranhas[0];
            Require(fish.Escape.Active && fish.X==before.Enemies.Piranhas[0].X && fish.Z==before.Enemies.Piranhas[0].Z,"Piranha teleported at detachment.");
            var enemy=new EnemySystem(config,escaped.Enemies);players[0].Swimming.InWater=true;players[0].X=fish.X;players[0].Z=fish.Z;
            int hits=0;var codec=new HookGame();var replay=PullModel(map,1);
            for(int i=0;i<CatchEscapeMotion.TotalTicks;i++)
            {
                Require(!enemy.TryHook(0,0),"Escaping/respawning piranha hooked again.");
                enemy.Advance(players,_=>hits++);
                replay.Restore(codec.Decode(codec.Encode(model.Capture())));ShopStep(model);ShopStep(replay);
                Require(codec.Encode(model.Capture()).SequenceEqual(codec.Encode(replay.Capture())),"Piranha escape restore diverged.");
            }
            Require(hits==0 && model.Capture().Enemies.Piranhas[0].RespawnTicks>0,"Piranha attacked or respawned before submersion completed.");
            Require(model.Capture().Coins==before.Coins && model.Capture().Enemies.Captures==0 && model.Capture().Players[0].Escapes==1,"Piranha escape reward/counter mismatch.");
        });
    }
}
