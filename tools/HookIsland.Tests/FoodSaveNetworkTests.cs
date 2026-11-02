using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> FoodSaveNetworkCases()
    {
        yield return ("food_four_udp_clients_buy_eat_bind_save_restore_without_replay",()=>{
            IslandSave saved;int foodId=0;var events=new int[4];
            using(var party=new SaveParty())
            {
                for(int n=1;n<=4;n++)party.Add(n,n==1);
                var pilot=new Advanture.HookIsland.InventorySmokePilot();var clock=System.Diagnostics.Stopwatch.StartNew();
                HookInput Input(IslandClient client,ulong tick)
                {
                    if(client.Session?.LocalParticipant==null || client.Slot!=0)return default;
                    var s=client.Session.InspectState();var p=s.Players[0];
                    if(p.CoinsEarned<200)return pilot.Sample(party.Map,s,0,tick);
                    if(p.Phase!=HookPhase.Ready)return default;
                    int lead=(int)Math.Min(30,tick>s.Steps?tick-s.Steps-1:0);
                    HookInput Move(int x,int z)
                    {
                        short Speed(int delta)=>(short)Math.Max(-1000,Math.Min(1000,delta*10/(lead+3)));
                        return Math.Abs(p.X-x)>60?new HookInput(Speed(x-p.X),0):Math.Abs(p.Z-z)>60?new HookInput(0,Speed(z-p.Z)):default;
                    }
                    HookInput Command(InventoryAction action,int target,uint id,int amount=0,uint batch=0)=>
                        new HookInput(0,0,inventory:new InventoryCommand(action,target,amount:amount,batch:batch,requestId:id));
                    if(p.LastInventoryRequest<2)
                    {
                        var move=Move(0,0);if(move.MoveX!=0 || move.MoveZ!=0)return move;
                        return Command(InventoryAction.Buy,8,p.LastInventoryRequest+1,batch:s.Tackle!.Batch);
                    }
                    var food=s.Tackle!.Inventory.Items.FirstOrDefault(i=>i.Owner==0 && i.Kind==ItemKind.Food);if(food.Id!=0)foodId=food.Id;
                    if(p.LastInventoryRequest==2 && p.Vitals.Health==100)return Move(-3700,-2400);
                    if(p.LastInventoryRequest==2)
                    {
                        var move=Move(-2300,-2400);if(move.MoveX!=0 || move.MoveZ!=0 || p.Swimming.InWater)return move;
                        return Command(InventoryAction.BindQuickSlot,foodId,3,9);
                    }
                    if(p.LastInventoryRequest==3)return Command(InventoryAction.SelectQuickSlot,9,4);
                    return Command(InventoryAction.Use,foodId,5);
                }
                while(clock.Elapsed<TimeSpan.FromSeconds(120))
                {
                    party.Poll(Input);
                    foreach(var c in party.Clients)if(c.Slot<4)events[c.Slot]+=c.ConfirmedEvents.Count(e=>e.Event.Kind==HookGameplayEventKind.FoodConsumed);
                    if(party.Host.Session?.InspectState().Tackle?.Inventory.ConsumedFoods[0]==1 && events.All(n=>n==1))break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(events.All(n=>n==1),"Food flow did not produce one confirmed event on every peer: "+string.Join(",",events));
                party.Agree();var s=party.Host.Session!.InspectState();var p=s.Players[0];
                Require(p.Vitals.Health==100 && p.SelectedQuickSlot==8 && p.FoodCooldownTicks>0 && p.CoinsEarned-p.Coins==200,"Eating/quickbar/personal wallet did not agree.");
                Require(s.Tackle!.Inventory.Items.Single(i=>i.Id==foodId).Count==1 && s.Tackle.Merchants[0].FoodStock[0]==6,"Food count or shelf stock differs.");
                saved=IslandSaveCodec.Decode(IslandSaveCodec.Encode(party.Host.CaptureSave()));
            }
            using var resumed=new SaveParty(saved);foreach(int n in new[]{1,4,2,3})resumed.Add(n,n==1);
            resumed.Until(()=>resumed.Host.Session!=null);
            Require(resumed.Host.Session!.CaptureCheckpoint().ApplicationState.Span.SequenceEqual(saved.Document.Checkpoint.ApplicationState.Span),"Loading advanced cooldown or lost food/quickbar.");
            Array.Clear(events,0,events.Length);
            resumed.Until(()=>resumed.Clients.All(c=>c.ResumeTicket!=null) && resumed.Host.Session!.ConfirmedTick>=15,(c,t)=>
            {
                if(c.Slot<4)events[c.Slot]+=c.ConfirmedEvents.Count(e=>e.Event.Kind==HookGameplayEventKind.FoodConsumed);
                return c.Slot==0?new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Use,foodId,requestId:5)):default;
            });
            resumed.Agree();var restored=resumed.Host.Session!.InspectState();
            Require(events.All(n=>n==0) && restored.Tackle!.Inventory.ConsumedFoods[0]==1 && restored.Tackle.Inventory.Items.Single(i=>i.Id==foodId).Count==1,"Saved receipt replayed a meal.");
            Require(restored.Players[0].SelectedQuickSlot==8 && restored.Tackle!.Inventory.Quickbar[8].Kind==ItemKind.Food,"Quickbar did not restore.");
            Console.WriteLine("FOOD_UDP_SAVE tick="+restored.Steps+" hash="+FrameSync.Runtime.CheckpointCodec.Hash(resumed.Host.Session.CaptureCheckpoint()));
        });
    }
}
