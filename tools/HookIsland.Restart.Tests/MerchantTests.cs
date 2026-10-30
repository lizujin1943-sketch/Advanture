using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace Advanture.HookIsland.RestartTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> MerchantCases()
    {
        foreach(int count in new[]{1,4})yield return ($"network_merchant_purchase_clocks_and_receipts_reset_{count}_players",()=>{
            var source=MerchantSmokePilot.CreateMap();
            var map=new IslandMap(source.Shore,source.Pier,source.Spawns,source.FishSpots,source.Platform,
                new FishingConfig(swimming:new SwimmingConfig(healthLossBasisPointsPerSecond:10000),
                    revival:new RevivalConfig(downedTicks:30,baseFee:1000000),merchants:source.Fishing!.Merchants));
            using var party=new Party(count,map);party.Until(()=>party.Running,_=>default);
            var pilots=party.Clients.Select(_=>new MerchantSmokePilot()).ToArray();
            party.Until(()=>party.Host.Session!.InspectState().Players.All(p=>p.LastInventoryRequest==1),
                c=>pilots[c.Slot].Sample(map,c.Session!.InspectState(),c.Slot,c.LastSubmittedTick+1),seconds:420);
            var developed=party.Host.Session!.InspectState();
            Require(developed.Tackle!.Merchants[2].Stock.Any(o=>o.Sold) && developed.Tackle.Merchants[2].ActivatedAt>0 &&
                developed.Players.Any(p=>FishingRules.Equipped(developed,p)!.Tier==2),"No real offshore purchase/activation before restart.");
            party.Until(()=>party.Host.Session!.InspectState().GameOver,LeavePlatform,seconds:40);party.Flush();
            var retired=party.Host.Session!;var oldClient=party.Clients[0].Session!;
            Require(party.Host.TryRestart(),"Developed merchant round could not restart.");
            party.Until(()=>!ReferenceEquals(retired,party.Host.Session),_=>default);
            var fresh=party.Host.Session!.CaptureCheckpoint();var initial=new HookGame().Create();
            initial.Initialize(new SimulationInitialization(fresh.Session,fresh.InitializationData));
            Require(fresh.Tick==0 && new HookGame().Encode(initial.Capture()).AsSpan().SequenceEqual(fresh.ApplicationState.Span),"Restart did not restore exact configured initial state.");
            party.Until(()=>party.Running,_=>default);
            var delayed=new HookInput(0,0,shopping:true,inventory:new InventoryCommand(InventoryAction.BuyAndEquip,1,batch:1,requestId:1,merchantId:2));
            Require(oldClient.TrySubmit(oldClient.ConfirmedTick+100,delayed.Encode()),"Could not enqueue old purchase.");
            oldClient.Poll();retired.Poll(party.Now);
            party.Until(()=>party.Host.Session!.ConfirmedTick>=30,_=>default);party.Flush();
            foreach(var state in party.Clients.Select(c=>c.Session!.InspectState()).Prepend(party.Host.Session.InspectState()))
            {
                var shelves=state.Tackle!.Merchants;
                Require(state.Tiles.Count==9 && state.Players.All(p=>p.LastInventoryRequest==0 && p.Coins==0 && p.CoinsEarned==0 && p.Catches==0),"Old roads, purchase receipt or rewards survived restart.");
                Require(shelves[0].Active && shelves[0].ActivatedAt==0 && shelves[0].Batch==1 && shelves[0].Stock.All(o=>!o.Sold) &&
                    shelves.Skip(1).All(m=>!m.Active && m.ActivatedAt==0 && m.Batch==0 && m.NextRestock==0 && m.Stock.Count==0),"Merchant clocks or sold stock survived restart.");
            }
            var bytes=CheckpointCodec.Encode(party.Host.Session.CaptureCheckpoint());
            Require(party.Clients.All(c=>bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint()))),"Merchant restart checkpoints differ.");
            Require(party.Host.Session.RejectedMessages>0,"Old purchase input was not isolated by epoch.");
            Console.WriteLine($"MERCHANT_RESTART players={count} epoch={party.Host.RoundEpoch} tick={party.Host.Session.ConfirmedTick}");
        });
    }
}
