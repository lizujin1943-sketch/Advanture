using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IslandMap MerchantMap(int seconds=600,MerchantConfig? merchants=null)
    {
        var m=IslandMap.CreatePlatform();
        return new IslandMap(m.Shore,m.Pier,m.Spawns,m.FishSpots,m.Platform,
            new FishingConfig(restockSeconds:seconds,merchants:merchants??MerchantConfig.Default));
    }
    private static PlatformTile[] MerchantRoads()=>PlatformRules.InitialTiles()
        .Concat(Enumerable.Range(2,3).Select(i=>new PlatformTile(i,0,TileKind.Wood)))
        .Concat(Enumerable.Range(2,5).Select(i=>new PlatformTile(0,i,TileKind.Wood)))
        .Concat(Enumerable.Range(2,7).Select(i=>new PlatformTile(-i,0,TileKind.Wood)))
        .Concat(Enumerable.Range(2,9).Select(i=>new PlatformTile(0,-i,TileKind.Wood))).ToArray();
    private static string MerchantOffers(IEnumerable<RodStock> stock)=>string.Join(";",stock.Select(i=>$"{i.Kind}:{i.Type}:{i.X}:{i.Y}:{i.Sold}"));
    private static IEnumerable<(string,Action)> MerchantCases()
    {
        yield return ("merchant_original_shelf_unchanged_and_extra_shelves_initially_inactive",()=>{
            var config=MerchantMap().Fishing!;var system=new MerchantSystem(config);var old=RodModel().Capture().Tackle!;
            Require(system.Capture().Count==5,"Missing configured merchants.");
            var first=system.Get(0)!;
            Require(first.Active && first.ActivatedAt==0 && first.Batch==1 && first.NextRestock==18000 && MerchantOffers(first.Stock)==MerchantOffers(old.Stock),"Original merchant first shelf changed.");
            Require(system.Capture().Skip(1).All(s=>!s.Active && s.Batch==0 && s.ActivatedAt==0 && s.NextRestock==0 && s.Stock.Count==0),"Unvisited ship already generated stock.");
        });
        yield return ("merchant_first_ground_approach_starts_independent_ten_minute_clock",()=>{
            var map=MerchantMap();var system=new MerchantSystem(map.Fishing!);var players=RodModel().Capture().Players.ToArray();
            players[0].X=8000;players[0].Z=0;
            system.Advance(map,MerchantRoads(),players,100);
            Require(system.Get(1)!.Active && system.Get(1)!.ActivatedAt==100 && system.Get(1)!.NextRestock==18100 && system.Get(1)!.Stock.Count>0,"First ground approach did not open shelf with its own timer.");
            players[0].X=0;players[0].Z=12000;system.Advance(map,MerchantRoads(),players,200);
            Require(system.Get(2)!.ActivatedAt==200 && system.Get(2)!.NextRestock==18200 && system.Get(1)!.NextRestock==18100,"Visiting another ship reset the first timer.");
            players[0].X=0;players[0].Z=0;system.Advance(map,MerchantRoads(),players,18100);
            Require(system.Get(0)!.Batch==2 && system.Get(1)!.Batch==2 && system.Get(1)!.NextRestock==36100 && system.Get(2)!.Batch==1,"Independent restock clocks crossed.");
        });
        yield return ("merchant_same_level_ships_have_replayable_independent_assortments",()=>{
            var config=new MerchantConfig(new[]{new MerchantDefinition(1,5,5,0),new MerchantDefinition(2,5,0,7)});
            var map=MerchantMap(600,config);var a=new MerchantSystem(map.Fishing!);var b=new MerchantSystem(map.Fishing!);
            var players=RodModel(count:2).Capture().Players.ToArray();players[0].X=8000;players[0].Z=0;players[1].X=0;players[1].Z=12000;
            a.Advance(map,MerchantRoads(),players,1);b.Advance(map,MerchantRoads(),players,1);
            Require(a.Get(1)!.Stock.Count>0 && a.Get(2)!.Stock.Count>0,"Ships did not activate.");
            Require(MerchantOffers(a.Get(1)!.Stock)!=MerchantOffers(a.Get(2)!.Stock),"Different merchants shared their random shelf.");
            Require(MerchantOffers(a.Get(1)!.Stock)==MerchantOffers(b.Get(1)!.Stock) && MerchantOffers(a.Get(2)!.Stock)==MerchantOffers(b.Get(2)!.Stock),"Merchant generation did not replay.");
        });
        yield return ("merchant_unavailable_water_downed_towed_and_knocked_players_do_not_activate",()=>{
            var map=MerchantMap();
            foreach(int reason in Enumerable.Range(0,7))
            {
                var system=new MerchantSystem(map.Fishing!);var players=RodModel().Capture().Players.ToArray();
                players[0].X=8000;players[0].Z=0;
                switch(reason)
                {
                    case 0:players[0].Unavailable=true;break;
                    case 1:players[0].Swimming.InWater=true;break;
                    case 2:players[0].Vitals.Health=0;break;
                    case 3:players[0].PulledBy=1;break;
                    case 4:players[0].Knockback.Active=true;break;
                    case 5:players[0].HookedPlayer=1;break;
                }
                system.Advance(map,reason==6?PlatformRules.InitialTiles():MerchantRoads(),players,42);
                Require(!system.Get(1)!.Active,$"Ineligible approach activated ship ({reason}).");
            }
        });
        yield return ("merchant_nearest_uses_distance_then_id_and_inclusive_three_metre_range",()=>{
            var config=new MerchantConfig(new[]{new MerchantDefinition(1,2,3,1),new MerchantDefinition(2,3,3,-1)});
            var map=MerchantMap(600,config);var tiles=MerchantRoads().Concat(new[]{new PlatformTile(3,-1,TileKind.Wood)}).ToArray();
            var p=RodModel().Capture().Players[0];p.X=6000;p.Z=0;
            Require(MerchantRules.Nearest(map,tiles,p)==1,"Equal distance did not choose lower merchant ID.");
            p.Z=-500;Require(MerchantRules.Nearest(map,tiles,p)==2,"Nearest merchant ignored distance.");
            p.Z=-1000;Require(MerchantRules.Near(map,tiles,p,1),"Exactly 3 m should be in range.");
            p.Z=-1001;Require(!MerchantRules.Near(map,tiles,p,1),"Beyond 3 m allowed interaction.");
            p.Swimming.InWater=true;Require(MerchantRules.Nearest(map,tiles,p)==-1,"Water showed a shop interaction.");
        });
        yield return ("merchant_reentry_preserves_stock_and_clock_and_capture_is_independent",()=>{
            var map=MerchantMap(2);var p=RodModel().Capture().Players.ToArray();p[0].X=8000;p[0].Z=0;
            var system=new MerchantSystem(map.Fishing!);system.Advance(map,MerchantRoads(),p,10);var original=system.Capture();
            p[0].X=0;system.Advance(map,MerchantRoads(),p,20);p[0].X=8000;system.Advance(map,MerchantRoads(),p,40);
            Require(system.Get(1)!.ActivatedAt==10 && system.Get(1)!.NextRestock==70 && MerchantOffers(system.Get(1)!.Stock)==MerchantOffers(original[1].Stock),"Re-entry regenerated stock or delayed restock.");
            var restored=new MerchantSystem(map.Fishing!,system.Capture());
            system.Advance(map,MerchantRoads(),p,130);restored.Advance(map,MerchantRoads(),p,130);
            Require(system.Get(1)!.Batch==3 && system.Get(1)!.NextRestock==190 && original[1].Batch==1,"Restock shifted epoch or mutated captured state.");
            Require(MerchantOffers(system.Get(1)!.Stock)==MerchantOffers(restored.Get(1)!.Stock),"Restored merchant clock changed random shelf.");
        });
        yield return ("merchant_invalid_layouts_rejected",()=>{
            var bad=new[]{new[]{new MerchantDefinition(0,2,5,0)},new[]{new MerchantDefinition(2,2,5,0)},
                new[]{new MerchantDefinition(1,0,5,0)},new[]{new MerchantDefinition(1,6,5,0)},
                new[]{new MerchantDefinition(1,2,1,1)},new[]{new MerchantDefinition(1,2,25,0)},
                new[]{new MerchantDefinition(1,2,int.MinValue,0)},
                new[]{new MerchantDefinition(1,2,5,0),new MerchantDefinition(2,3,5,0)},
                Enumerable.Range(1,5).Select(i=>new MerchantDefinition(i,2,3+i,0)).ToArray()};
            foreach(var definitions in bad)
            {
                bool rejected=false;try{new MerchantConfig(definitions);}catch(System.IO.InvalidDataException){rejected=true;}
                Require(rejected,"Invalid offshore layout accepted.");
            }
            Require(MerchantConfig.Empty.Definitions.Count==0 && MerchantConfig.Default.Definitions.Select(d=>d.Level).SequenceEqual(new[]{2,3,4,5}),"Default merchant tiers changed.");
        });
    }
}
