using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.EnemyTests;
internal static partial class Program
{
    private static HookState Deck(HookState state,int radius,bool gap=false)
    {
        var tiles=new List<PlatformTile>();
        for(int z=-radius;z<=radius;z++)for(int x=-radius;x<=radius;x++)
        {
            if(gap && z==1 && (x==2 || x==3))continue;
            bool initial=Math.Abs(x)<=1 && Math.Abs(z)<=1;
            tiles.Add(new PlatformTile(x,z,TileKind.Wood,initial?-1:0,initial?0:4));
        }
        var players=state.Players.ToArray();players[0].WoodEarned=tiles.Sum(t=>t.Paid);
        players[0].X=players[0].Z=players[0].HookX=players[0].HookZ=0;
        return new HookState(state.Steps,players,state.Fish,state.Coins,tiles,state.Driftwood,state.Tackle,state.Enemies);
    }
    private static bool ClearOfDeck(HookState state,int x,int z)=>
        state.Tiles.All(t=>Math.Abs(x-t.X*2000)>=1900 || Math.Abs(z-t.Z*2000)>=1900);
    private static IEnumerable<(string,Action)> PlacementCases()
    {
        yield return ("placement_concave_deck_keeps_open_water_reachable_after_restore",()=>{
            var config=new EnemyConfig(piranhaCount:0,initialDelayTicks:0,waterballRange:6000);
            var a=Model(config:config);var s=Deck(a.Capture(),3,true);var monster=s.Enemies.Monster;monster.Cycle=1;
            s=Replace(s,enemies:new EnemyState(s.Enemies.Piranhas,monster,s.Enemies.Waterballs));
            Require(s.Tiles.Count==47 && ClearOfDeck(s,5500,2000),"Fixture has no legal gap");
            var b=Model(config:config);a.Restore(s);b.Restore(new HookGame().Decode(new HookGame().Encode(s)));Step(a);Step(b);
            var end=a.Capture();monster=end.Enemies.Monster;
            Require(monster.Phase==MonsterPhase.Windup && ClearOfDeck(end,monster.X,monster.Z),"Open gap was never selected");
            Require(IslandMap.DistanceSquared(0,0,monster.X,monster.Z)<=36000000,"Outside configured range");
            Require(new HookGame().Encode(end).SequenceEqual(new HookGame().Encode(b.Capture())),"Restored placement differs");
        });
        yield return ("placement_large_deck_uses_water_beyond_preferred_radius",()=>{
            var m=Model(config:new EnemyConfig(piranhaCount:0,initialDelayTicks:0));m.Restore(Deck(m.Capture(),6));Step(m);
            var s=m.Capture();var monster=s.Enemies.Monster;
            Require(monster.Phase==MonsterPhase.Windup && ClearOfDeck(s,monster.X,monster.Z),"Expanded deck permanently suppressed attacks");
            long distance=IslandMap.DistanceSquared(0,0,monster.X,monster.Z);
            Require(distance>6000L*6000 && distance<=24000L*24000,"Fallback ignored attack range");
        });
        yield return ("placement_blocked_retry_advances_saved_sequence_then_resumes",()=>{
            var config=new EnemyConfig(piranhaCount:0,initialDelayTicks:0,waterballRange:6000);
            var a=Model(config:config);a.Restore(Deck(a.Capture(),3));Step(a);var s=a.Capture();
            Require(s.Enemies.Monster.Phase==MonsterPhase.Cooldown && s.Enemies.Monster.Cycle>0,"Failed attempt did not advance sequence");
            var b=Model(config:config);b.Restore(new HookGame().Decode(new HookGame().Encode(s)));
            for(int i=0;i<60;i++){Step(a);Step(b);}
            Require(a.Capture().Enemies.Monster.Cycle>s.Enemies.Monster.Cycle && a.Capture().Enemies.Shots==0,"Retry did not remain submerged with new sequence");
            Require(new HookGame().Encode(a.Capture()).SequenceEqual(new HookGame().Encode(b.Capture())),"Blocked retry restore diverged");
            a.Restore(Deck(a.Capture(),1));for(int i=0;i<30;i++)Step(a);
            Require(a.Capture().Enemies.Monster.Phase==MonsterPhase.Windup,"Did not resume when water became available");
        });
        yield return ("placement_initial_body_cannot_overlap_deck_edge",()=>{
            var m=Model(config:new EnemyConfig(piranhaCount:0,initialDelayTicks:0,monsterX:3850,monsterZ:0));Step(m);
            var s=m.Capture();var monster=s.Enemies.Monster;
            Require(monster.Phase==MonsterPhase.Windup && ClearOfDeck(s,monster.X,monster.Z),"Body overlaps the deck by 50 mm");
        });
        yield return ("placement_new_tile_touching_body_edge_cancels_windup",()=>{
            var m=Model(config:new EnemyConfig(piranhaCount:0,initialDelayTicks:0,monsterX:5850,monsterZ:0));Step(m);
            var s=m.Capture();Require(s.Enemies.Monster.Phase==MonsterPhase.Windup && s.Enemies.Monster.X==5850,"Fixture did not surface");
            var players=s.Players.ToArray();players[0].WoodEarned+=4;
            m.Restore(new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles.Concat(new[]{new PlatformTile(2,0,TileKind.Wood,0,4)}),s.Driftwood,s.Tackle,s.Enemies));
            Step(m);var end=m.Capture();Require(end.Enemies.Monster.Phase==MonsterPhase.Cooldown && end.Enemies.Shots==0,"New floor overlapping body edge did not cancel attack");
        });
    }
}
