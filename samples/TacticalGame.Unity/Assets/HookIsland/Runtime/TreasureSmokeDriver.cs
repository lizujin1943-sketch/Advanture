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
    // Explicit development acceptance only. The pilot supplies ordinary inputs; it never edits authoritative state.
    public sealed class TreasureSmokeDriver:MonoBehaviour
    {
        private readonly TreasureSmokePilot pilot=new TreasureSmokePilot();
        private IslandFishingController room;
        private string report;
        private float started,finished,nextProgress;
        private bool written,closing,sharedImage,landedImage;
        private int maxRenderedLines,maxRenderedChests;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-treasure-smoke")!="1")return;
            UnityEngine.Object.FindObjectOfType<IslandFishingController>().gameObject.AddComponent<TreasureSmokeDriver>();
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
                var state=session.InspectState();pilot.Observe(state);
                maxRenderedLines=Math.Max(maxRenderedLines,room.Presentation.RenderedFishingLines);
                maxRenderedChests=Math.Max(maxRenderedChests,room.Treasures?.RenderedChests??0);
                if(pilot.Shared && !sharedImage){sharedImage=true;ScreenCapture.CaptureScreenshot(report+".shared.png");}
                if(pilot.Landed && !landedImage){landedImage=true;ScreenCapture.CaptureScreenshot(report+".landed.png");}
                if(Time.realtimeSinceStartup>=nextProgress)
                {nextProgress=Time.realtimeSinceStartup+8;Write(report+".progress.json",session.CaptureCheckpoint(),room.LocalSlot,false);}
                if(pilot.Complete(state) && !closing && room.Host?.Session!=null && session.ConfirmedTick>=600)
                {ScreenCapture.CaptureScreenshot(report+".png");room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1,true));closing=true;}
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot,true);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>120)
            {
                if(session?.LocalParticipant!=null)Write(report+".state.json",session.CaptureCheckpoint(),room.LocalSlot,false);
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Treasure acceptance timeout"},true));Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot,bool validate)
        {
            var s=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report {slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=s.Coins,stage=pilot.Stage,
                complete=pilot.Complete(s),shared=pilot.Shared,landed=pilot.Landed,opened=pilot.Opened,maxParticipants=pilot.MaxParticipants,
                renderedPlayers=room.Presentation.RenderedPlayers,maxRenderedLines=maxRenderedLines,maxRenderedChests=maxRenderedChests,
                activeSmokeDrivers=room.GetComponents<MonoBehaviour>().Count(c=>c.GetType().Name.EndsWith("SmokeDriver",StringComparison.Ordinal)),
                players=s.Players.Select(p=>new Player{slot=p.Slot,coins=p.Coins,bones=p.Fishbones,wood=p.Wood,earned=p.CoinsEarned,collected=p.TreasuresCollected}).ToArray()};
            if(validate && (!result.complete || maxRenderedChests<2 || (s.Players.Count>1 && maxRenderedLines<s.Players.Count) || result.activeSmokeDrivers!=1))
                result.error="Treasure rules or presentation acceptance incomplete.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            if(validate)File.WriteAllBytes(path+".checkpoint",CheckpointCodec.Encode(checkpoint));
        }
        [Serializable] private sealed class Report
        {public string error,tick,hash;public int slot,coins,stage,maxParticipants,renderedPlayers,maxRenderedLines,maxRenderedChests,activeSmokeDrivers;public bool complete,shared,landed,opened;public Player[] players;}
        [Serializable] private sealed class Player {public int slot,coins,bones,wood,earned,collected;}
    }
}
#endif
