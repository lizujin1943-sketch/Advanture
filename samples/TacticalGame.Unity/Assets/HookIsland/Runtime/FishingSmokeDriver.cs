using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;
using FrameSync.Sessions;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Development-only input driver. Uses the same input freezer, UDP, hook rules and rendered views as human play.</summary>
    public sealed class FishingSmokeDriver : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private IslandFishingController room;
        private string report;
        private float started,finished;
        private bool running,written,closing,captured,quit;
        private int targetFish=-1;
        private void Start()
        {
            string path=IslandFishingController.LaunchValue("-island-report");
            if(!string.IsNullOrEmpty(path))Begin(path,true);
        }
        public void Begin(string path,bool quitWhenDone)
        {
            room=GetComponent<IslandFishingController>();report=path;quit=quitWhenDone;
            started=Time.realtimeSinceStartup;running=true;room.InputProvider=Sample;
        }
        private HookInput Sample(ulong tick)
        {
            if(room.State==null || room.LocalSlot>=room.State.Players.Count)return default;
            var p=room.State.Players[room.LocalSlot];
            if(targetFish<0)targetFish=room.State.Fish.OrderBy(f=>IslandMap.DistanceSquared(p.X,p.Z,f.X,f.Z)).First().Id;
            var target=room.Map.FishSpots[targetFish];
            short x=0,z=0;
            if(tick<35)
            {
                int dx=target.X-p.X,dz=target.Z-p.Z,len=HookGame.Sqrt((long)dx*dx+(long)dz*dz);
                if(len>0){x=(short)(dx*800/len);z=(short)(dz*800/len);}
            }
            // Exercise an empty cast first, then cast at visible fish, never assign or award a catch directly.
            if(tick==8)return new HookInput(x,z,p.X+22000,p.Z,true);
            var fish=room.State.Fish.Where(f=>f.Phase==FishPhase.Jumping && f.Y>700 &&
                IslandMap.DistanceSquared(p.X,p.Z,f.X,f.Z)<13000L*13000).OrderBy(f=>IslandMap.DistanceSquared(p.X,p.Z,f.X,f.Z)).ToArray();
            if(fish.Length>0 && p.Phase==HookPhase.Ready && tick%12==0)
                return new HookInput(x,z,fish[0].X,fish[0].Z,true);
            return new HookInput(x,z,target.X,target.Z);
        }
        private void LateUpdate()
        {
            if(!running)return;
            if(written){if(quit && Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                if(!captured && session.ConfirmedTick>=280)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(report));
                    ScreenCapture.CaptureScreenshot(report+".png");captured=true;
                }
                if(!closing && room.Host?.Session?.ConfirmedTick>=600)
                {
                    room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1));closing=true;
                }
                if(session.Status==SessionStatus.Closed)
                {
                    Write(report,session.CaptureCheckpoint(),room.LocalSlot);
                    written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;
                }
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>75)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Fishing smoke timeout"},true));
                running=false;Debug.LogError("FISHING_SMOKE_FAILED "+room.Failure);
                if(quit)Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot)
        {
            var state=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report {slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=state.Coins,
                renderedPlayers=room.Presentation.RenderedPlayers,presentedCatches=room.Presentation.PresentedCatches,
                players=state.Players.Select(p=>new Player{slot=p.Slot,x=p.X,z=p.Z,catches=p.Catches,coins=p.Coins,casts=p.Casts}).ToArray()};
            foreach(var p in state.Players)
            {
                var shown=room.Presentation.PlayerPosition(p.Slot);
                result.maxPositionError=Mathf.Max(result.maxPositionError,Vector2.Distance(new Vector2(shown.x,shown.z),new Vector2(p.X/1000f,p.Z/1000f)));
            }
            if(state.Coins<=0 || result.players.Any(p=>p.catches==0))result.error="A player did not finish a real fishing catch.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            Debug.Log("FISHING_SMOKE_REPORT "+path+" hash="+result.hash);
        }
        [Serializable]private sealed class Report
        {
            public string error,tick,hash;
            public int slot,coins,renderedPlayers,presentedCatches;
            public float maxPositionError;
            public Player[] players;
        }
        [Serializable]private sealed class Player {public int slot,x,z,catches,coins,casts;}
#endif
    }
}
