using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.SwimmingTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> NetworkCases()
    {
        yield return ("network_fishing_income_buys_equips_ring_and_swims_on_four_clients",()=>{
            var map=IslandMap.CreatePlatform();using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(120))
                {
                    var now=last=clock.Elapsed;host.Poll(now);
                    foreach(var c in clients)
                    {
                        c.Poll(now,t=>c.Session?.LocalParticipant==null || c.Slot!=0?default:EarnRingInput(map,c.Session.InspectState(),t));
                        Require(c.Failure==null,c.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && host.Session.InspectState().Players[0].X< -4000)break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<300;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                var s=host.Session!.InspectState();var p=s.Players[0];var ring=SwimRingCatalog.Equipped(s.Tackle!.Inventory,0);
                Require(ring!=null && p.Catches>0 && p.CoinsEarned>=ring.Price && p.Swimming.InWater && p.X< -4000,"Normal-input earn/buy/swim loop incomplete.");
                Require(p.Vitals.Health==100 && p.Stamina<120*30000 && s.Players.Skip(1).All(v=>v.Coins==0),"Ring protection or personal payment failed.");
                string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());foreach(var c in clients)
                    Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash,"Purchased ring diverged across peers.");
                Console.WriteLine("RING_NETWORK tick="+s.Steps+" earned="+p.CoinsEarned+" hash="+hash);
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        yield return ("network_four_clients_fall_swim_and_rescue_with_equal_checkpoints",()=>{
            var map=IslandMap.CreatePlatform();using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(18))
                {
                    var now=last=clock.Elapsed;host.Poll(now);
                    foreach(var c in clients)
                    {
                        c.Poll(now,_=>c.Session?.LocalParticipant==null?default:RescueInput(c.Session.InspectState(),c.Slot));
                        Require(c.Failure==null,c.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && host.Session.ConfirmedTick>=210)break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<300;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                var s=host.Session!.InspectState();var v=s.Players[1];
                Require(v.TimesHooked==1 && !v.Swimming.InWater && v.Vitals.Health<100,"Live network rescue did not reach shore.");
                Require(s.Players[2].Swimming.InWater && s.Players[2].Stamina<120*30000 && s.Players[2].Vitals.Health<100,"Live network swimming did not spend resources.");
                string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash,"Water/health/stamina checkpoints differ.");
                Console.WriteLine("WATER_NETWORK tick="+s.Steps+" hash="+hash);
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        yield return ("network_lobby_rejects_different_water_configuration",()=>{
            var map=IslandMap.CreatePlatform();using var host=new IslandHost(map,0,2);
            var altered=new IslandMap(map.Shore,map.Pier,map.Spawns,map.FishSpots,map.Platform,new FishingConfig(swimming:new SwimmingConfig(6000)));
            using var client=new IslandClient(altered,IPAddress.Loopback,host.Port,ushort.MaxValue);
            for(int ms=0;ms<1500 && client.Failure==null;ms+=2){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);client.Poll(now,_=>default);}
            Require(client.Failure!=null && host.Session==null,"Mismatched water rules joined room.");
        });
    }
    private static HookInput RescueInput(HookState s,int slot)
    {
        var p=s.Players[slot];
        if(slot==0)
        {
            if(p.X<1300)return new HookInput(1000,0);
            if(p.Z> -2300)return new HookInput(0,-1000);
            var target=s.Players[1];
            if(target.Z< -3700 && target.TimesHooked==0 && p.Phase==HookPhase.Ready)return new HookInput(0,0,target.X,target.Z,true);
        }
        if(slot==1 && p.TimesHooked==0 && p.Z> -3900)return new HookInput(0,-1000);
        if(slot==2 && p.X<4400)return new HookInput(1000,0);
        return default;
    }
    private static HookInput EarnRingInput(IslandMap map,HookState s,ulong tick)
    {
        var p=s.Players[0];var t=s.Tackle!;
        if(SwimRingCatalog.Equipped(t.Inventory,0)!=null)
        {
            if(p.Z> -1300)return new HookInput(0,-1000);
            return p.X> -4100?new HookInput(-1000,0):default;
        }
        if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
        int offer=Enumerable.Range(0,t.Stock.Count).Where(i=>!t.Stock[i].Sold && t.Stock[i].Kind==ItemKind.SwimRing)
            .OrderBy(i=>SwimRingCatalog.Get(t.Stock[i].Type).Price).DefaultIfEmpty(-1).First();
        if(offer>=0 && p.Coins>=SwimRingCatalog.Get(t.Stock[offer].Type).Price)
        {
            if(p.Z< -100)return new HookInput(0,1000);
            return new HookInput(0,0,shopping:true,inventory:new InventoryCommand(InventoryAction.BuyAndEquip,offer,batch:t.Batch,requestId:p.LastInventoryRequest+1));
        }
        if(p.Z> -2500)return new HookInput(0,-1000);
        int speed=FishingRules.Speed(s,p),range=FishingRules.Range(map,s,p),lead=(int)Math.Min(30,tick>s.Steps?tick-s.Steps-1:0);
        foreach(var fish in s.Fish)
        {
            if(fish.Owner>=0)continue;var future=FishMotion.Predict(fish,map.FishSpots[fish.Id],lead);
            for(int n=1;n<=Math.Min(120,range*30/speed+1);n++)
            {
                FishMotion.Advance(ref future,map.FishSpots[fish.Id]);if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                int d=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                if(d<=range && Math.Abs(d-n*speed/30)<speed/60+40 && (long)FishCatalog.DrainPerTick(fish)*(d*30/t.Config.ReelSpeed+4)<p.Stamina)
                    return new HookInput(0,0,future.X,future.Z,true);
            }
        }
        return default;
    }
}
