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
    private static IEnumerable<(string,Action)> FinaleCases()
    {
        foreach(int count in new[]{1,4})yield return ($"network_finale_victory_and_restart_{count}_players",()=>{
            var map=FinaleSmokePilot.CreateMap();using var party=new Party(count,map);party.Until(()=>party.Running,_=>default);
            var pilots=party.Clients.Select(_=>new FinaleSmokePilot()).ToArray();
            int giantParticipants=0;
            // One teammate joins only the giant: personal catches are not a victory prerequisite.
            party.Until(()=>{
                var s=party.Host.Session!.InspectState();
                foreach(var p in s.Players)if(p.HookedFinale)giantParticipants|=1<<p.Slot;
                return s.Won;
            },c=>count==4 && c.Slot==3 && c.Session!.InspectState().Finale.Phase==FinalePhase.Dormant?
                default:pilots[c.Slot].Sample(map,c.Session!.InspectState(),c.Slot,c.LastSubmittedTick+1),seconds:100);
            party.Flush();Require(party.Host.CanRestart && party.Clients.All(c=>c.Session!.InspectState().Won),"Victory did not expose a shared restartable outcome.");
            var oldServer=party.Host.Session!;var oldClient=party.Clients[0].Session!;var old=oldServer.CaptureCheckpoint();
            Console.WriteLine($"FINALE_RESTART_BEFORE players={count} bait={oldServer.InspectState().Finale.SpentBait} catches={string.Join(",",oldServer.InspectState().Players.Select(p=>p.Catches))}");
            var victory=oldServer.InspectState();
            Require(victory.Finale.SpentBait==3 && victory.Commissions.Completed==3 && victory.Commissions.Entries.Sum(e=>e.Fish)==6 &&
                victory.Players.Sum(p=>p.Catches)>=6 && giantParticipants==(1<<count)-1,"Victory lacks earned team bait or a teammate's actual giant hook.");
            if(count==4)Require(victory.Players[3].Catches==0,"Late contributor fixture unexpectedly caught a commission fish.");
            int port=party.Host.Port;int[] ports=party.Clients.Select(c=>c.LocalPort).ToArray();
            Require(party.Host.TryRestart() && !party.Host.TryRestart(),"Victory restart failed or duplicated.");
            party.Until(()=>!ReferenceEquals(oldServer,party.Host.Session),_=>default);
            var fresh=party.Host.Session!.CaptureCheckpoint();var model=new HookGame().Create();
            model.Initialize(new SimulationInitialization(fresh.Session,fresh.InitializationData));
            Require(fresh.Tick==0 && fresh.Session.Epoch==2 && fresh.Session.SessionId==old.Session.SessionId &&
                new HookGame().Encode(model.Capture()).AsSpan().SequenceEqual(fresh.ApplicationState.Span),"Victory restart did not create an exact fresh round.");
            party.Until(()=>party.Running,_=>default);
            Require(party.Host.Port==port && party.Clients.Select(c=>c.LocalPort).SequenceEqual(ports),"Victory restart replaced room sockets.");
            Require(oldClient.TrySubmit(oldClient.ConfirmedTick+100,new HookInput(1000,0,0,-5100,true).Encode()),"Could not queue a delayed previous-round hook.");
            oldClient.Poll();oldServer.SaveAndClose(_=>{});oldServer.Poll(party.Now);
            party.Until(()=>party.Host.Session!.ConfirmedTick>=30,_=>default);party.Flush();
            var bytes=CheckpointCodec.Encode(party.Host.Session!.CaptureCheckpoint());
            foreach(var s in party.Clients.Select(c=>c.Session!.InspectState()).Prepend(party.Host.Session.InspectState()))
            {
                Require(!s.RoundFinished && s.Finale.Phase==FinalePhase.Dormant && s.Finale.SpentBait==0 && s.Finale.Completed==0 &&
                    s.Commissions.Completed==0 && s.Commissions.Bait==0 && s.Players.All(p=>p.Coins==0 && p.Casts==0 && p.Catches==0 && !p.HookedFinale && p.FinaleDrainRemainder==0),"Old victory, bait, rewards or input survived restart.");
            }
            Require(party.Host.Session.RejectedMessages>0 && party.Clients.All(c=>c.Session!.RejectedMessages>0 && c.Failure==null),"Old epoch input/close was not isolated.");
            Require(party.Clients.All(c=>bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint()))),"Restarted finale checkpoints differ.");
            Console.WriteLine($"FINALE_RESTART players={count} epoch={party.Host.RoundEpoch} tick={party.Host.Session.ConfirmedTick} hash={CheckpointCodec.Hash(party.Host.Session.CaptureCheckpoint())}");
        });
    }
}
