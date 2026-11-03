using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.CrabTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> NavigationCacheCases()
    {
        yield return ("navigation_same_count_tile_replacement_and_rollback",()=>{
            var original=PlatformRules.InitialTiles();var changed=original.ToArray();
            changed[Array.FindIndex(changed,t=>t.X==1 && t.Z==0)]=new PlatformTile(2,0,TileKind.Wood);
            var players=new[]{new Fisher{Slot=0,X=2000,Z=0,Vitals=new CharacterVitals{Health=100}}};
            var nav=new CrabNavigation(Map());nav.Refresh(original);
            Require(nav.Target(players,0,0,out _) == 0,"Initial target is unreachable");
            nav.Refresh(changed);
            Require(!nav.CanStand(2000,0) && nav.CanStand(4000,0),"Same-count terrain retained stale cells");
            players[0].X=4000;
            Require(nav.Target(players,0,0,out _)==-1,"Path crosses the destroyed tile");
            CompareNavigation(nav,changed,players);
            nav.Refresh(original);players[0].X=2000;
            Require(nav.Target(players,0,0,out _)==0,"Rollback retained the newer terrain graph");
            CompareNavigation(nav,original,players);
        });
        yield return ("navigation_cache_matches_fresh_graph_after_in_place_edits",()=>{
            var tiles=PlatformRules.InitialTiles().ToList();var nav=new CrabNavigation(Map());
            var players=new[]{new Fisher{Slot=0,X=-1400,Z=-1400,Vitals=new CharacterVitals{Health=100}}};
            for(int i=0;i<4;i++)
            {
                tiles.Add(new PlatformTile(i+2,-1,TileKind.Wood));nav.Refresh(tiles);CompareNavigation(nav,tiles,players);
                tiles.Reverse();nav.Refresh(tiles);CompareNavigation(nav,tiles,players);
            }
            tiles.RemoveAt(0);nav.Refresh(tiles);CompareNavigation(nav,tiles,players);
        });
        yield return ("three_crabs_repeated_prediction_restore_matches_continuous_simulation",()=>{
            var config=new CrabConfig(initialDelayTicks:30,count:3,spawnStaggerTicks:30);
            var continuous=Model(4,config);var predicted=Model(4,config);
            for(int i=0;i<400;i++)
            {
                // Restore into a model whose terrain cache has already been used.
                predicted.Restore(new HookGame().Decode(new HookGame().Encode(continuous.Capture())));
                Step(continuous);Step(predicted);Same(continuous.Capture(),predicted.Capture());
            }
        });
    }
    private static void CompareNavigation(CrabNavigation cached,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players)
    {
        var fresh=new CrabNavigation(Map());fresh.Refresh(tiles);
        Require(cached.Target(players,0,0,out var a)==fresh.Target(players,0,0,out var b) && a.Equals(b),"Cached route changed target or waypoint");
        for(uint generation=1;generation<=3;generation++)
        {
            bool first=cached.TrySpawn(players,generation,out var landA,out var waterA);
            bool second=fresh.TrySpawn(players,generation,out var landB,out var waterB);
            Require(first==second && landA.Equals(landB) && waterA.Equals(waterB),"Cached shoreline changed spawn order");
        }
        Require(cached.Retreat(0,0,out var goalA,out var seaA)==fresh.Retreat(0,0,out var goalB,out var seaB) && goalA.Equals(goalB) && seaA.Equals(seaB),"Cached retreat changed route");
    }
}
