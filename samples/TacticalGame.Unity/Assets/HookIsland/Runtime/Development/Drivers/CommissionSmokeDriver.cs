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
    public sealed class CommissionSmokeDriver:MonoBehaviour
    {
        private readonly CommissionSmokePilot pilot=new CommissionSmokePilot();
        private IslandFishingController room;
        private string report;
        private float started,finished,nextProgress;
        private bool written,closing,progressSeen,sharedSeen,initialImage,progressImage;
        private int completionsSeen,maxRenderedLines;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-commission-smoke")!="1")return;
            UnityEngine.Object.FindObjectOfType<IslandFishingController>().gameObject.AddComponent<CommissionSmokeDriver>();
        }
        private void Start()
        {
            room=GetComponent<IslandFishingController>();report=IslandFishingController.LaunchValue("-island-report");started=Time.realtimeSinceStartup;
            room.InputProvider=t=>room.State==null?default:pilot.Sample(room.Map,room.State,room.LocalSlot,t);
        }
        private void LateUpdate()
        {
            if(written){if(Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                var state=session.InspectState();var c=state.Commissions;
                progressSeen|=c.Entries.Any(e=>e.Fish>0);sharedSeen|=state.Players.Any(p=>CooperativeFishing.Attached(p) && CooperativeFishing.Count(state.Players,p)>1);
                maxRenderedLines=Math.Max(maxRenderedLines,room.Presentation.RenderedFishingLines);
                int bit=1<<c.Completed;
                if((completionsSeen&bit)==0 && c.Completed>0)ScreenCapture.CaptureScreenshot(report+".commission"+c.Completed+".png");
                completionsSeen|=bit;
                if(!initialImage && state.Steps>=30){initialImage=true;ScreenCapture.CaptureScreenshot(report+".initial.png");}
                if(!progressImage && progressSeen){progressImage=true;ScreenCapture.CaptureScreenshot(report+".progress.png");}
                if(Time.realtimeSinceStartup>=nextProgress)
                {nextProgress=Time.realtimeSinceStartup+8;Write(report+".progress.json",session.CaptureCheckpoint(),room.LocalSlot,false);}
                if(pilot.Complete(state) && !closing && room.Host?.Session!=null && session.ConfirmedTick>=600)
                {ScreenCapture.CaptureScreenshot(report+".png");room.Host.Session.SaveAndClose(capture=>Write(report+".server.json",capture,-1,true));closing=true;}
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot,true);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>180)
            {
                if(session?.LocalParticipant!=null)Write(report+".state.json",session.CaptureCheckpoint(),room.LocalSlot,false);
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Commission acceptance timeout"},true));Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot,bool validate)
        {
            var s=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report {slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=s.Coins,
                complete=pilot.Complete(s),completed=s.Commissions.Completed,bait=s.Commissions.Bait,fish=s.Commissions.Entries.Sum(e=>e.Fish),
                progressSeen=progressSeen,sharedSeen=sharedSeen,completionsSeen=completionsSeen,maxRenderedLines=maxRenderedLines,renderedPlayers=room.Presentation.RenderedPlayers,
                activeSmokeDrivers=room.GetComponents<MonoBehaviour>().Count(c=>c.GetType().Name.EndsWith("SmokeDriver",StringComparison.Ordinal)),
                players=s.Players.Select(p=>new Player {slot=p.Slot,coins=p.Coins,earned=p.CoinsEarned,bones=p.Fishbones,catches=p.Catches}).ToArray()};
            if(validate && (!result.complete || result.bait!=3 || !progressSeen || completionsSeen!=15 || result.activeSmokeDrivers!=1 ||
                s.Players.Count>1 && !sharedSeen || result.players.Any(p=>p.catches==0 || p.coins!=p.earned)))result.error="Shared commission flow incomplete.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            if(validate)File.WriteAllBytes(path+".checkpoint",CheckpointCodec.Encode(checkpoint));
        }
        [Serializable] private sealed class Report
        {public string error,tick,hash;public int slot,coins,completed,bait,fish,completionsSeen,maxRenderedLines,renderedPlayers,activeSmokeDrivers;public bool complete,progressSeen,sharedSeen;public Player[] players;}
        [Serializable] private sealed class Player {public int slot,coins,earned,bones,catches;}
    }
}
#endif
