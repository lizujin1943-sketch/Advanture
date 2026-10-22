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
    /// <summary>Opt-in native acceptance: lose via normal swimming, host restarts, then the party fishes again.</summary>
    public sealed class RestartSmokeDriver : MonoBehaviour
    {
        private IslandFishingController room;
        private readonly RodSmokePilot fishing=new RodSmokePilot();
        private string report,roomId;
        private string[] members;
        private int localPort,hostPort,changes;
        private float started,failedAt=-1,finished;
        private bool failed,fresh,clicked,closing,written;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-restart-smoke")!="1" || string.IsNullOrEmpty(IslandFishingController.LaunchValue("-island-report")))return;
            var c=UnityEngine.Object.FindObjectOfType<IslandFishingController>();
            if(c!=null)c.gameObject.AddComponent<RestartSmokeDriver>();
        }
        private void Start()
        {
            room=GetComponent<IslandFishingController>();report=IslandFishingController.LaunchValue("-island-report");started=Time.realtimeSinceStartup;
            room.InputProvider=t=>
            {
                if(room.State==null || !room.TryGetLocalPlayer(out var p))return default;
                if(room.Client.RoundEpoch==1)return new HookInput(room.LocalSlot%2==0?(short)1000:(short)-1000,0);
                return p.Catches>0?default:fishing.Sample(room.Map,room.State,room.LocalSlot,t);
            };
        }
        private void LateUpdate()
        {
            if(written){if(Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                var cp=session.CaptureCheckpoint();var state=session.InspectState();
                if(roomId==null)
                {
                    roomId=cp.Session.SessionId.ToString();members=cp.Session.Participants.Select(p=>p.ParticipantId.ToString()).ToArray();
                    localPort=room.Client.LocalPort;hostPort=room.Host?.Port??0;
                    room.Client.RoundChanged+=()=>changes++;
                }
                if(state.GameOver && !failed)
                { failed=true;failedAt=Time.realtimeSinceStartup;ScreenCapture.CaptureScreenshot(report+".failed.png"); }
                if(failed && !clicked && room.Host!=null && Time.realtimeSinceStartup-failedAt>1)
                {
                    clicked=room.RestartRound();
                    if(!clicked || room.RestartRound())throw new InvalidOperationException("Host restart/double-click contract failed.");
                }
                if(cp.Session.Epoch==2 && !fresh && state.Steps<15)
                {
                    fresh=!state.GameOver && state.Tiles.Count==9 && state.Coins==0 && state.Players.All(p=>!p.Vitals.Downed && p.Coins==0 && p.Revival.Deaths==0 && p.Revival.CoinsSpent==0 && p.LastRodRequest==0 && p.LastInventoryRequest==0 && p.LastBuildId==0);
                }
                if(cp.Session.Epoch==2 && Complete(state) && room.Host?.Session!=null && !closing)
                {
                    ScreenCapture.CaptureScreenshot(report+".restarted.png");
                    room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1));closing=true;
                }
                if(session.Status==SessionStatus.Closed)
                {Write(report,cp,room.LocalSlot);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>150)
            {
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Restart acceptance timeout"},true));Application.Quit(1);
            }
        }
        private static bool Complete(HookState state)=>state.Steps>=45 && state.Players.All(p=>p.Casts>0) && state.Players.Any(p=>p.Catches>0);
        private void Write(string path,Checkpoint checkpoint,int slot)
        {
            var s=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var r=new Report{slot=slot,epoch=checkpoint.Session.Epoch.ToString(),tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=s.Coins,
                sawFailure=failed,fresh=fresh,changes=changes,complete=Complete(s),port=room.Client.LocalPort,
                connectionRetained=room.Client.LocalPort==localPort && (room.Host==null || room.Host.Port==hostPort),
                identityRetained=checkpoint.Session.SessionId.ToString()==roomId && checkpoint.Session.Participants.Select(p=>p.ParticipantId.ToString()).SequenceEqual(members),
                renderedPlayers=room.Presentation.RenderedPlayers,renderedTiles=room.Construction.RenderedTiles,
                swimmerWakes=room.GetComponentsInChildren<LineRenderer>(true).Count(l=>l.name=="Swimming wake"),
                activeSmokeDrivers=room.GetComponents<MonoBehaviour>().Count(c=>c.GetType().Name.EndsWith("SmokeDriver",StringComparison.Ordinal)),
                players=s.Players.Select(p=>new Player{slot=p.Slot,casts=p.Casts,catches=p.Catches,coins=p.Coins,deaths=p.Revival.Deaths}).ToArray()};
            if(!r.sawFailure || !r.fresh || r.changes!=1 || !r.complete || !r.connectionRetained || !r.identityRetained || r.epoch!="2" || r.activeSmokeDrivers!=1 || r.swimmerWakes!=s.Players.Count)
                r.error="Restart lifecycle, fresh state, identity or presentation mismatch.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(r,true));
            File.WriteAllBytes(path+".checkpoint",CheckpointCodec.Encode(checkpoint));
        }
        [Serializable] private sealed class Report
        {public string error,epoch,tick,hash;public int slot,coins,port,changes,renderedPlayers,renderedTiles,swimmerWakes,activeSmokeDrivers;public bool sawFailure,fresh,complete,connectionRetained,identityRetained;public Player[] players;}
        [Serializable] private sealed class Player {public int slot,casts,catches,coins;public uint deaths;}
    }
}
#endif
