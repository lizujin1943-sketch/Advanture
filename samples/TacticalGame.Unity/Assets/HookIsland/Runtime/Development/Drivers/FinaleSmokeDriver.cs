#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;
using FrameSync.Sessions;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Explicit ordinary-input acceptance: earn bait, win, wait on the result, restart and fish again.</summary>
    public sealed class FinaleSmokeDriver:MonoBehaviour
    {
        private readonly FinaleSmokePilot pilot=new FinaleSmokePilot();
        private readonly CommissionSmokePilot nextRound=new CommissionSmokePilot();
        private IslandFishingController room;
        private string report,roomId,victoryTick;
        private string[] members;
        private byte[] frozen;
        private int localPort,hostPort,changes,shoresSeen,shoreImages,hookedPlayers,maxHooks,maxRenderedLines;
        private ulong victoryFrame;
        private float started,finished,nextProgress;
        private bool won,fresh,clicked,closing,written,giantSeen,dived,markerSeen,guidanceImage,stable=true;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-finale-smoke")!="1" || string.IsNullOrEmpty(IslandFishingController.LaunchValue("-island-report")))return;
            var c=UnityEngine.Object.FindObjectOfType<IslandFishingController>();if(c!=null)c.gameObject.AddComponent<FinaleSmokeDriver>();
        }
        private void Start()
        {
            room=GetComponent<IslandFishingController>();report=IslandFishingController.LaunchValue("-island-report");started=Time.realtimeSinceStartup;
            room.InputProvider=t=>room.State==null?default:room.Client.RoundEpoch==1?pilot.Sample(room.Map,room.State,room.LocalSlot,t):
                room.State.Players.All(p=>p.Catches>0)?default:nextRound.Sample(room.Map,room.State,room.LocalSlot,t);
        }
        private void LateUpdate()
        {
            if(written){if(Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                var state=session.InspectState();
                if(roomId==null)
                {
                    var cp=session.CaptureCheckpoint();roomId=cp.Session.SessionId.ToString();members=cp.Session.Participants.Select(p=>p.ParticipantId.ToString()).ToArray();
                    localPort=room.Client.LocalPort;hostPort=room.Host?.Port??0;room.Client.RoundChanged+=()=>changes++;
                }
                if(room.Client.RoundEpoch==1)
                {
                    var s=state.Finale;int count=FinaleRules.Count(state.Players);maxHooks=Math.Max(maxHooks,count);
                    maxRenderedLines=Math.Max(maxRenderedLines,room.Presentation.RenderedFishingLines);
                    foreach(var p in state.Players)if(p.HookedFinale)hookedPlayers|=1<<p.Slot;
                    if(s.Phase==FinalePhase.Surfaced)
                    {
                        int bit=1<<s.Completed;shoresSeen|=bit;giantSeen|=room.Finale.RenderedGiants==1;markerSeen|=room.Finale.ShoreMarkerVisible;
                        if(!guidanceImage && FinaleHud.Guidance(room.ViewCamera,state.Tackle.Config.Finale,s,state.Players[room.LocalSlot]).Contains(" m"))
                        {guidanceImage=true;ScreenCapture.CaptureScreenshot(report+".guidance.png");}
                        if((shoreImages&bit)==0 && count==state.Players.Count && s.Progress>0)
                        {shoreImages|=bit;ScreenCapture.CaptureScreenshot(report+".shore"+s.Completed+".png");}
                    }
                    if(s.Phase==FinalePhase.Submerged)dived|=room.Finale.RenderedGiants==0;
                    if(state.Won)
                    {
                        var bytes=new HookGame().Encode(state);
                        if(!won)
                        {
                            won=true;victoryFrame=session.ConfirmedTick;victoryTick=state.Steps.ToString();frozen=bytes;
                            ScreenCapture.CaptureScreenshot(report+".victory.png");Write(report+".victory.json",session.CaptureCheckpoint(),room.LocalSlot,false);
                        }
                        stable&=frozen.SequenceEqual(bytes);
                        if(!stable)throw new InvalidOperationException("Victory state changed while awaiting restart.");
                        if(!clicked && room.Host!=null && session.ConfirmedTick>=victoryFrame+90)
                        {
                            clicked=room.RestartRound();
                            if(!clicked || room.RestartRound())throw new InvalidOperationException("Victory restart/double-click contract failed.");
                        }
                    }
                }
                if(room.Client.RoundEpoch==2 && !fresh && state.Steps<15)
                    fresh=!state.RoundFinished && state.Finale.Phase==FinalePhase.Dormant && state.Finale.SpentBait==0 && state.Commissions.Completed==0 && state.Commissions.Bait==0 &&
                        state.Players.All(p=>p.Coins==0 && p.Catches==0 && !p.HookedFinale && p.FinaleDrainRemainder==0);
                if(Time.realtimeSinceStartup>=nextProgress)
                {nextProgress=Time.realtimeSinceStartup+8;Write(report+".progress.json",session.CaptureCheckpoint(),room.LocalSlot,false);}
                if(room.Client.RoundEpoch==2 && Complete(state) && room.Host?.Session!=null && !closing)
                {
                    ScreenCapture.CaptureScreenshot(report+".restarted.png");
                    room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1,true));closing=true;
                }
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot,true);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>180)
            {
                if(session?.LocalParticipant!=null)Write(report+".state.json",session.CaptureCheckpoint(),room.LocalSlot,false);
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Finale acceptance timeout"},true));Application.Quit(1);
            }
        }
        private static bool Complete(HookState state)=>state.Steps>=90 && state.Players.All(p=>p.Catches>0);
        private void Write(string path,Checkpoint checkpoint,int slot,bool validate)
        {
            var s=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var r=new Report {slot=slot,tick=checkpoint.Tick.ToString(),epoch=checkpoint.Session.Epoch.ToString(),victoryTick=victoryTick,
                hash=CheckpointCodec.Hash(checkpoint),coins=s.Coins,won=won,fresh=fresh,complete=checkpoint.Session.Epoch==2 && Complete(s),stable=stable,
                changes=changes,shoresSeen=shoresSeen,shoreImages=shoreImages,hookedPlayers=hookedPlayers,maxHooks=maxHooks,maxRenderedLines=maxRenderedLines,
                giantSeen=giantSeen,dived=dived,markerSeen=markerSeen,guidanceImage=guidanceImage,renderedPlayers=room.Presentation.RenderedPlayers,
                connectionRetained=room.Client.LocalPort==localPort && (room.Host==null || room.Host.Port==hostPort),
                identityRetained=checkpoint.Session.SessionId.ToString()==roomId && checkpoint.Session.Participants.Select(p=>p.ParticipantId.ToString()).SequenceEqual(members),
                activeSmokeDrivers=room.GetComponents<MonoBehaviour>().Count(c=>c.GetType().Name.EndsWith("SmokeDriver",StringComparison.Ordinal)),
                players=s.Players.Select(p=>new Player{slot=p.Slot,catches=p.Catches,coins=p.Coins,earned=p.CoinsEarned,bones=p.Fishbones}).ToArray()};
            if(validate && (!r.complete || !won || !fresh || !stable || changes!=1 || shoresSeen!=7 || shoreImages!=7 || maxHooks!=s.Players.Count ||
                hookedPlayers!=(1<<s.Players.Count)-1 || !giantSeen || !dived || !markerSeen || !guidanceImage || !r.connectionRetained || !r.identityRetained || r.activeSmokeDrivers!=1))
                r.error="Finale, participation, presentation or restart incomplete.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(r,true));
            if(validate)File.WriteAllBytes(path+".checkpoint",CheckpointCodec.Encode(checkpoint));
        }
        [Serializable] private sealed class Report
        {
            public string error,tick,epoch,victoryTick,hash;
            public int slot,coins,changes,shoresSeen,shoreImages,hookedPlayers,maxHooks,maxRenderedLines,renderedPlayers,activeSmokeDrivers;
            public bool won,fresh,complete,stable,giantSeen,dived,markerSeen,guidanceImage,connectionRetained,identityRetained;
            public Player[] players;
        }
        [Serializable] private sealed class Player {public int slot,catches,coins,earned,bones;}
    }
}
#endif
