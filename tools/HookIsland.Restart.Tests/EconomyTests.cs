using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.RestartTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> EconomyCases()
    {
        foreach(int count in new[]{1,4})yield return ($"network_ocean_progress_and_visits_reset_{count}_players",()=>{
            var source=OceanSmokePilot.CreateMap();
            var map=new IslandMap(source.Shore,source.Pier,source.Spawns,source.FishSpots,source.Platform,
                new FishingConfig(swimming:new SwimmingConfig(healthLossMilliPerSecond:1000000),
                    revival:new RevivalConfig(downedTicks:30,baseFee:1000000),
                    commissions:source.Fishing!.Commissions,ocean:source.Fishing.Ocean));
            using var party=new Party(count,map);party.Until(()=>party.Running,_=>default);
            var pilots=party.Clients.Select(_=>new OceanSmokePilot()).ToArray();
            party.Until(()=>pilots[0].Complete(party.Host.Session!.InspectState()),
                c=>pilots[c.Slot].Sample(map,c.Session!.InspectState(),c.Slot,c.LastSubmittedTick+1),seconds:90);
            var ocean=party.Host.Session!.InspectState().Ocean;
            Require(ocean.Sequence>0 && ocean.RewardedStage==2 && ocean.LastCatchTick>0,"No actual ocean progression to reset.");
            party.Until(()=>party.Host.Session!.InspectState().GameOver,LeavePlatform);
            var retired=party.Host.Session!;Require(party.Host.TryRestart(),"Developed ocean round did not restart.");
            party.Until(()=>!ReferenceEquals(retired,party.Host.Session),_=>default);
            Require(party.Host.Session!.InspectState().Steps==0,"Missed fresh round state.");
            party.Until(()=>party.Running,_=>default);party.Flush();
            foreach(var state in party.Clients.Select(c=>c.Session!.InspectState()).Prepend(party.Host.Session.InspectState()))
            {
                var fresh=state.Ocean;
                Require(fresh.RewardedStage==0 && fresh.Sequence==0 && fresh.LastEvent==OceanEvent.None && fresh.LastEventTick==0 &&
                    fresh.LastCatchTick==0 && fresh.ThreatWindowStart==0 && fresh.ThreatCount==0 && fresh.HandledAttacks==0 &&
                    fresh.QuietUntil==0 && fresh.ReliefReadyTick==0,"Ocean timers or one-time unlocks survived restart.");
                Require(fresh.Visits.Count==2 && fresh.Visits.All(v=>!v.Active && v.Until==0 && v.HomeX==0 && v.HomeZ==0) &&
                    state.Fish.Skip(map.Fishing!.Ocean.Unlocks.Count).All(f=>f.Phase==FishPhase.Waiting && f.Timer==0),
                    "Old temporary fish or dynamic homes survived restart.");
            }
        });
        yield return ("network_earned_coins_materials_and_built_tiles_do_not_survive_restart", () => {
            var source=Map();
            // Valid authored-rule overrides keep this acceptance short without granting resources or editing live state.
            var map=new IslandMap(source.Shore,source.Pier,source.Spawns,source.FishSpots,
                new PlatformConfig(fishboneTileCost:2),new FishingConfig(
                    swimming:new SwimmingConfig(healthLossMilliPerSecond:1000000),
                    revival:new RevivalConfig(downedTicks:30,baseFee:1000000),
                    commissions:new CommissionConfig(new[]{new CommissionDefinition(1,1),new CommissionDefinition(3,1)})));
            using var party=new Party(1,map);party.Until(()=>party.Running,_=>default);
            var pilot=new RodSmokePilot();
            party.Until(()=>party.Host.Session!.InspectState().Players[0].Catches>0,
                c=>pilot.Sample(map,c.Session!.InspectState(),c.Slot,c.LastSubmittedTick+1),seconds:45);
            party.Until(()=>party.Host.Session!.InspectState().Tiles.Count==10,c=>{
                var p=c.Session!.InspectState().Players[c.Slot];
                return new HookInput(0,0,build:TileKind.Fishbone,buildX:-1,buildZ:-2,buildId:p.LastBuildId+1);
            });
            var developed=party.Host.Session!.InspectState();
            Require(developed.Players[0].Coins>0 && developed.Players[0].FishbonesEarned>=2 && developed.Players[0].LastBuildId>0,"Did not exercise actual earned rewards and construction before restart.");
            Require(developed.Commissions.Completed==1 && developed.Commissions.Bait==1 && developed.Commissions.Entries[0].Fish==1,
                "Actual capture did not award shared commission progress before restart.");
            party.Until(()=>party.Host.Session!.InspectState().GameOver,LeavePlatform);
            Require(party.Host.TryRestart(),"Failed developed round could not restart.");
            party.Until(()=>party.Host.RoundEpoch==2 && party.Running,_=>default);
            party.Flush();var state=party.Host.Session!.InspectState();var p=state.Players[0];
            Require(state.Tiles.Count==9 && p.Coins==0 && p.CoinsEarned==0 && p.Fishbones==0 && p.FishbonesEarned==0 && p.Catches==0 && p.LastBuildId==0 && p.Revival.Deaths==0,"Previous round rewards or construction survived.");
            Require(state.Tackle!.Inventory.Items.Count==2 && state.Tackle.Stock.All(o=>!o.Sold),"Inventory/merchant initial stock did not reset.");
            Require(state.Commissions.Completed==0 && state.Commissions.Bait==0 && state.Commissions.Entries.Count==2 &&
                state.Commissions.Entries.All(e=>e.Fish==0 && e.Rare==0),"Previous round commissions or bait survived restart.");
            Require(party.Clients.All(c=>c.Session!.InspectState().Commissions.Bait==0 && c.Session.InspectState().Commissions.Completed==0),
                "Client retained old commission progress after restart.");
        });
    }
}
