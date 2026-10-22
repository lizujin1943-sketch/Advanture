#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;
using FrameSync.Sessions;

namespace Advanture.HookIsland
{
    public sealed class PlatformSmokeDriver : MonoBehaviour
    {
        private IslandFishingController room;
        private string report;
        private bool running,closing,written,quit,captured;
        private float started,finished,nextProgress;
        public void Begin(string path,bool quitWhenDone)
        {
            room=GetComponent<IslandFishingController>();report=path;quit=quitWhenDone;running=true;started=Time.realtimeSinceStartup;
            room.InputProvider=Sample;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AttachWhenRequested()
        {
            if(string.IsNullOrEmpty(IslandFishingController.LaunchValue("-island-report")) || IslandFishingController.LaunchValue("-island-rod-smoke")=="1" || IslandFishingController.LaunchValue("-island-inventory-smoke")=="1" || IslandFishingController.LaunchValue("-island-swimming-smoke")=="1" || IslandFishingController.LaunchValue("-island-enemy-smoke")=="1" || IslandFishingController.LaunchValue("-island-revival-smoke")=="1" || IslandFishingController.LaunchValue("-island-restart-smoke")=="1" || IslandFishingController.LaunchValue("-island-cooperative-smoke")=="1" || IslandFishingController.LaunchValue("-island-room-rules-smoke")=="1" || IslandFishingController.LaunchValue("-island-treasure-smoke")=="1" || IslandFishingController.LaunchValue("-island-commission-smoke")=="1" || IslandFishingController.LaunchValue("-island-ocean-smoke")=="1" || IslandFishingController.LaunchValue("-island-finale-smoke")=="1" || IslandFishingController.LaunchValue("-island-merchant-smoke")=="1" || IslandFishingController.LaunchValue("-island-recovery-smoke")=="1")return;
            var construction=UnityEngine.Object.FindObjectOfType<PlatformConstruction>();
            if(construction!=null && construction.GetComponent<PlatformSmokeDriver>()==null)
                construction.gameObject.AddComponent<PlatformSmokeDriver>();
        }
        private void Start()
        {
            var path=IslandFishingController.LaunchValue("-island-report");if(!string.IsNullOrEmpty(path))Begin(path,true);
        }
        private HookInput Sample(ulong tick)
        {
            if(room.State==null)return default;
            var c=PlatformSmokePilot.Sample(room.Map,room.State,room.LocalSlot,tick);
            if(c.Build!=TileKind.None)
            {
                if(!room.Construction.Open)room.Construction.Toggle();room.Construction.Select(c.Build);
                room.Construction.RequestPlacement(c.Build,c.BuildX,c.BuildZ);
                // Exercise the real UI request queue and its repeated receipt, not a direct state change.
                return default;
            }
            return c;
        }
        private void LateUpdate()
        {
            if(!running)return;
            if(written){if(quit && Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                if(Time.realtimeSinceStartup>=nextProgress)
                {nextProgress=Time.realtimeSinceStartup+8;Write(report+".progress.json",session.CaptureCheckpoint(),room.LocalSlot,false);}
                bool complete=PlatformSmokePilot.Complete(session.InspectState());
                if(!captured && complete)
                {ScreenCapture.CaptureScreenshot(report+".png");captured=true;}
                if(!closing && room.Host?.Session!=null && complete && session.ConfirmedTick>=600)
                {room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1,true));closing=true;}
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot,true);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>120)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                if(session?.LocalParticipant!=null)Write(report+".state.json",session.CaptureCheckpoint(),room.LocalSlot,false);
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Platform smoke timeout"},true));
                running=false;Debug.LogError("PLATFORM_SMOKE_FAILED "+room.Failure);if(quit)Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot,bool validate)
        {
            var s=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var r=new Report{slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=s.Coins,
                tileCount=s.Tiles.Count,woodTiles=s.Tiles.Count(t=>t.Kind==TileKind.Wood),boneTiles=s.Tiles.Count(t=>t.Kind==TileKind.Fishbone),
                renderedTiles=room.Construction.RenderedTiles,renderedPlayers=room.Presentation.RenderedPlayers,complete=PlatformSmokePilot.Complete(s),
                pending=room.Construction.Pending,buildMessage=room.Construction.Message,
                players=s.Players.Select(p=>new Player{slot=p.Slot,x=p.X,z=p.Z,wood=p.Wood,bones=p.Fishbones,
                    woodEarned=p.WoodEarned,bonesEarned=p.FishbonesEarned,catches=p.Catches,buildId=p.LastBuildId,
                    buildResult=p.LastBuildResult.ToString()}).ToArray()};
            if(slot>=0)
            {
                var next=PlatformSmokePilot.Sample(room.Map,s,slot,s.Steps+1);
                r.nextBuild=next.Build+" ("+next.BuildX+","+next.BuildZ+")";
                r.nextCheck=PlatformRules.Check(room.Map,s,s.Players[slot],next.Build,next.BuildX,next.BuildZ).ToString();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path+".snapshot",checkpoint.ApplicationState.ToArray());
            if(validate && (!r.complete || s.Players.Any(p=>p.WoodEarned<4 || p.FishbonesEarned<4)))r.error="Collection, construction or traversal incomplete.";
            File.WriteAllText(path,JsonUtility.ToJson(r,true));
        }
        [Serializable]private sealed class Report
        {
            public string error,tick,hash,buildMessage,nextBuild,nextCheck;
            public int slot,coins,tileCount,woodTiles,boneTiles,renderedTiles,renderedPlayers;
            public bool complete,pending;
            public Player[] players;
        }
        [Serializable]private sealed class Player
        {public int slot,x,z,wood,bones,woodEarned,bonesEarned,catches;public uint buildId;public string buildResult;}
    }
}
#endif
