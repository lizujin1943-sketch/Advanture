using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static HookInput HealingMove(Fisher p,HookState s,ulong tick,int x,int z)
    {
        int lead=(int)Math.Min(30,tick>s.Steps?tick-s.Steps-1:0);
        short Speed(int delta)=>(short)Math.Max(-1000,Math.Min(1000,delta*10/(lead+3)));
        // Separate the four approach lanes on land before entering water. Crossing along the
        // water edge can exhaust swimming stamina or block another player's landing.
        return Math.Abs(p.Z-z)>60?new HookInput(0,Speed(z-p.Z)):Math.Abs(p.X-x)>60?new HookInput(Speed(x-p.X),0):default;
    }
    private static IEnumerable<(string,Action)> HealingSaveNetworkCases()
    {
        yield return ("healing_four_udp_clients_use_charges_save_restore_replay_and_paid_respawn",()=>{
            IslandSave saved;var events=new int[4];var amounts=new int[4];
            // Use the public water-damage configuration to reach each lifecycle boundary promptly.
            var b=RecoveryMap();var map=new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,
                new FishingConfig(startingCoins:1000,swimming:new SwimmingConfig(healthLossBasisPointsPerSecond:1000)));
            using(var party=new SaveParty(map:map))
            {
                for(int n=1;n<=4;n++)party.Add(n,n==1);
                var clock=System.Diagnostics.Stopwatch.StartNew();
                HookInput Input(IslandClient client,ulong tick)
                {
                    if(client.Session?.LocalParticipant==null || client.Slot>=4)return default;
                    var s=client.Session.InspectState();var p=s.Players[client.Slot];
                    if(p.LastInventoryRequest!=0)return default;
                    int z=-2400+client.Slot*1200;
                    if(p.Vitals.Health>50)return HealingMove(p,s,tick,-3700,z);
                    var move=HealingMove(p,s,tick,-2300,z);
                    if(move.MoveX!=0 || move.MoveZ!=0 || p.Swimming.InWater)return move;
                    return new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Heal,0,requestId:1));
                }
                while(clock.Elapsed<TimeSpan.FromSeconds(45))
                {
                    party.Poll(Input);
                    foreach(var c in party.Clients)if(c.Slot<4)foreach(var e in c.ConfirmedEvents.Where(e=>e.Event.Kind==HookGameplayEventKind.Healed))
                    {events[c.Slot]++;amounts[c.Slot]+=e.Event.Amount;}
                    if(events.All(n=>n==4))break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(events.All(n=>n==4) && amounts.All(n=>n==160),"Every peer must confirm four independent 40-point heals: "+string.Join(",",events)+
                    " states="+string.Join(";",party.Host.Session!.InspectState().Players.Select(p=>$"{p.Slot}:hp={p.Vitals.Health},xy={p.X}/{p.Z},water={p.Swimming.InWater},request={p.LastInventoryRequest},result={p.LastInventoryResult},charges={p.HealingCharges}")));
                party.Agree();var s=party.Host.Session!.InspectState();
                Require(s.Players.All(p=>p.HealingCharges==2 && p.LastInventoryResult==InventoryResult.Success && p.Coins==1000),"Charges, receipts or wallets differ after healing.");
                Require(s.Tackle!.Merchants.All(m=>m.FoodStock.All(n=>n==0)),"Food remains available in multiplayer.");
                saved=IslandSaveCodec.Decode(IslandSaveCodec.Encode(party.Host.CaptureSave()));
                Console.WriteLine("HEALING_UDP_SAVED tick="+s.Steps+" health="+string.Join(",",s.Players.Select(p=>p.Vitals.Health)));
            }
            using var resumed=new SaveParty(saved);foreach(int n in new[]{1,4,2,3})resumed.Add(n,n==1);
            resumed.Until(()=>resumed.Host.Session!=null);
            Require(resumed.Host.Session!.CaptureCheckpoint().ApplicationState.Span.SequenceEqual(saved.Document.Checkpoint.ApplicationState.Span),"Loading reset charges or changed the saved state.");
            Array.Clear(events,0,events.Length);
            resumed.Until(()=>resumed.Clients.All(c=>c.ResumeTicket!=null) && resumed.Host.Session!.ConfirmedTick>=15,(c,t)=>
            {
                if(c.Slot<4)events[c.Slot]+=c.ConfirmedEvents.Count(e=>e.Event.Kind==HookGameplayEventKind.Healed);
                return new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Heal,0,requestId:1));
            });
            resumed.Agree();var restored=resumed.Host.Session!.InspectState();
            Require(events.All(n=>n==0) && restored.Players.All(p=>p.HealingCharges==2),"Saved receipt replayed a heal or refilled charges.");
            // Real water damage and a normal paid-revival command; no authority-state injection.
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(timer.Elapsed<TimeSpan.FromSeconds(30) && resumed.Host.Session.InspectState().Players[0].Revival.LastResult!=RespawnResult.Success)
            {
                resumed.Poll((c,t)=>{
                    if(c.Slot!=0 || c.Session?.LocalParticipant==null)return default;
                    var s=c.Session.InspectState();var p=s.Players[0];
                    if(p.Vitals.Downed && p.Revival.HasQuote)return new HookInput(0,0,revival:new RevivalCommand(respawnRequest:1,deathId:p.Revival.DownedId));
                    return HealingMove(p,s,t,-3700,-2400);
                });
                System.Threading.Thread.Sleep(1);
            }
            resumed.Agree();restored=resumed.Host.Session.InspectState();
            var revived=restored.Players[0];
            Require(revived.Revival.LastResult==RespawnResult.Success && revived.HealingCharges==3,
                $"Paid revival did not refill: health={revived.Vitals.Health}, charges={revived.HealingCharges}, water={revived.Swimming.InWater}, position={revived.X}/{revived.Z}, result={revived.Revival.LastResult}.");
            Require(restored.Players.Skip(1).All(p=>p.HealingCharges==2),"One player's respawn refilled teammates.");
            Console.WriteLine("HEALING_UDP_SAVE tick="+restored.Steps+" hash="+FrameSync.Runtime.CheckpointCodec.Hash(resumed.Host.Session.CaptureCheckpoint()));
        });
    }
}
