using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> MerchantInputCases()
    {
        foreach(int count in new[]{1,2,4})foreach(int delay in new[]{0,3,6})
            yield return ($"merchant_normal_input_{count}_players_delay_{delay}",()=>MerchantInputScenario(count,delay));
    }
    private static void MerchantInputScenario(int count,int delay)
    {
        var map=MerchantSmokePilot.CreateMap();var runner=Runner(count,map);var pilot=new MerchantSmokePilot();
        var pending=new Queue<HookInput[]>();for(int i=0;i<delay;i++)pending.Enqueue(new HookInput[count]);
        for(int i=0;i<30000 && !pilot.Complete(runner.InspectState());i++)
        {
            var s=runner.InspectState();pending.Enqueue(s.Players.Select(p=>pilot.Sample(map,s,p.Slot,runner.Tick+(ulong)delay+1)).ToArray());Step(runner,pending.Dequeue());
        }
        var result=runner.InspectState();
        if(count==1 && delay==3)Console.WriteLine("MERCHANT_OFFERS "+string.Join(";",result.Tackle!.Merchants.Select(m=>m.Id+":"+string.Join(",",m.Stock.Where(o=>o.Kind==ItemKind.Rod).Select(o=>RodCatalog.Get(o.Type).Tier+"/"+o.Type)))));
        Console.WriteLine($"MERCHANT_INPUT players={count} delay={delay} tick={runner.Tick} active={result.Tackle!.Merchants.Count(m=>m.Active)} tiles={result.Tiles.Count} tier={pilot.PurchaseTier} visited={pilot.VisitedMask} returned={pilot.ReturnedMask} coins={string.Join(",",result.Players.Select(p=>p.Coins))} catches={string.Join(",",result.Players.Select(p=>p.Catches))} receipts={string.Join(",",result.Players.Select(p=>p.LastInventoryResult))}");
        Require(pilot.Complete(result),"Ordinary input did not earn/build/reach all ships, buy a better rod and catch after returning.");
        Require(pilot.PurchaseTier>=2,"The route must purchase a tier unavailable from the starting merchant.");
        Require(result.Tiles.Count==33 && result.Tackle.Merchants.All(m=>m.Active),"A configured route or merchant is missing.");
        Require(result.Players.Sum(p=>p.CoinsEarned-p.Coins)>0 && result.Tackle.Inventory.Items.Any(i=>i.Owner>=0 && i.Kind==ItemKind.Rod && i.Type>0),"No earned income bought a better rod.");
        Require(result.Players.Sum(p=>p.FishbonesEarned-p.Fishbones)==96,"Routes were not paid through normal fishing.");
        _=new HookGame().Decode(new HookGame().Encode(result));
    }
    private static IEnumerable<(string,Action)> MerchantNetworkCases()
    {
        foreach(int count in new[]{1,2,4})yield return ($"merchant_real_udp_{count}_clients",()=>{
            var map=MerchantSmokePilot.CreateMap();using var host=new IslandHost(map,0,count);
            var clients=Enumerable.Range(0,count).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=clients.Select(_=>new MerchantSmokePilot()).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(420))
                {
                    last=clock.Elapsed;host.Poll(last);
                    for(int i=0;i<count;i++)
                    {
                        var c=clients[i];var pilot=pilots[i];c.Poll(last,t=>c.Session?.LocalParticipant==null?default:pilot.Sample(map,c.Session.InspectState(),c.Slot,t));
                        Require(c.Failure==null,c.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && clients.Select((c,i)=>c.Session?.LocalParticipant!=null && pilots[i].Complete(c.Session.InspectState())).All(v=>v))break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<200;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                Require(host.Session!=null,"Merchant room did not start.");var state=host.Session!.InspectState();
                Require(clients.Select((c,i)=>pilots[i].Complete(c.Session!.InspectState())).All(v=>v),"A peer missed merchant travel, purchase or the return catch.");
                Require(state.Players.Count(p=>p.LastInventoryResult==InventoryResult.Success)==1 &&
                    state.Players.Count(p=>p.LastInventoryResult==InventoryResult.SoldOut)==count-1,"The shared offer did not produce exactly one purchase and terminal sold-out receipts.");
                Require(pilots.All(p=>p.PurchaseTier>=2),"No higher merchant tier was purchased.");
                Require(state.Tackle!.Merchants[MerchantSmokePilot.PurchaseMerchantId].Stock.Count(s=>s.Sold)==1 &&
                    state.Tackle.Merchants.Where(m=>m.Id!=MerchantSmokePilot.PurchaseMerchantId).All(m=>m.Active && m.Stock.All(s=>!s.Sold)),"Buying from the north boat changed another shop.");
                var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint())),"Merchant checkpoints differ byte-for-byte.");
                Console.WriteLine($"MERCHANT_UDP players={count} tick={host.Session.ConfirmedTick} tier={pilots[0].PurchaseTier} receipts={string.Join(",",state.Players.Select(p=>p.LastInventoryResult))} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
    }
}
