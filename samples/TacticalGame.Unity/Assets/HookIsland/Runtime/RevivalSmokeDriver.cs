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
    /// <summary>Explicit development-only end-to-end acceptance using the normal room input path.</summary>
    public sealed class RevivalSmokeDriver : MonoBehaviour
    {
        private IslandFishingController room;
        private readonly RevivalSmokePilot pilot=new RevivalSmokePilot();
        private string report;
        private float started,finished,nextProgress;
        private bool written,closing,downed,helping,dead,captured,paymentCaptured;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-revival-smoke")!="1")return;
            UnityEngine.Object.FindObjectOfType<IslandFishingController>().gameObject.AddComponent<RevivalSmokeDriver>();
        }
        private void Start()
        {
            room=GetComponent<IslandFishingController>();report=IslandFishingController.LaunchValue("-island-report");started=Time.realtimeSinceStartup;
            room.InputProvider=t=>
            {
                if(room.State==null)return default;
                var input=pilot.Sample(room.Map,room.State,room.LocalSlot,t);
                // Exercise the same acknowledgment queue as the real paid-respawn button.
                if(input.Revival.RespawnRequest!=0)
                {
                    // Keep one rendered frame of the enabled downed-state button before submitting the click.
                    if(!paymentCaptured){paymentCaptured=true;ScreenCapture.CaptureScreenshot(report+".payment.png");return default;}
                    room.Revival.RequestRespawn();return default;
                }
                return input;
            };
        }
        private void LateUpdate()
        {
            if(written){if(Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                var state=session.InspectState();
                if(state.Players.Any(p=>p.Vitals.Downed && !p.Revival.Dead) && !downed)
                {downed=true;ScreenCapture.CaptureScreenshot(report+".downed.png");}
                if(state.Players.Any(p=>p.Revival.RescueTicks>0) && !helping)
                {helping=true;ScreenCapture.CaptureScreenshot(report+".help.png");}
                if(state.Players.Any(p=>p.Revival.Dead) && !dead)
                {dead=true;ScreenCapture.CaptureScreenshot(report+".dead.png");}
                if(Time.realtimeSinceStartup>=nextProgress)
                {nextProgress=Time.realtimeSinceStartup+8;Write(report+".progress.json",session.CaptureCheckpoint(),room.LocalSlot,false);}
                bool complete=pilot.Complete(state);
                if(complete && !captured){ScreenCapture.CaptureScreenshot(report+".png");captured=true;}
                if(complete && !closing && room.Host?.Session!=null)
                {room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1,true));closing=true;}
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot,true);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>170)
            {
                if(session?.LocalParticipant!=null)Write(report+".state.json",session.CaptureCheckpoint(),room.LocalSlot,false);
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Revival acceptance timeout"},true));Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot,bool validate)
        {
            var s=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report {slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=s.Coins,
                complete=pilot.Complete(s),freeRescue=pilot.FreeRescueSeen,paidRespawn=pilot.PaidRespawnSeen,downed=downed,helping=helping,dead=dead,
                earlyRespawn=s.Players[2].Revival.CoinsSpent>0 && s.Players[2].Revival.Deaths==0,
                paymentCaptured=paymentCaptured,
                renderedPlayers=room.Presentation.RenderedPlayers,
                activeSmokeDrivers=room.GetComponents<MonoBehaviour>().Count(c=>c.GetType().Name.EndsWith("SmokeDriver",StringComparison.Ordinal)),
                players=s.Players.Select(p=>new Player{slot=p.Slot,coins=p.Coins,earned=p.CoinsEarned,spent=p.Revival.CoinsSpent,health=p.Vitals.Health,water=p.Swimming.InWater,dead=p.Revival.Dead,deaths=p.Revival.Deaths,towed=p.TimesHooked}).ToArray()};
            if(validate && (!result.complete || !downed || !helping || !result.earlyRespawn || result.activeSmokeDrivers!=1))result.error="Revival flow or presentation acceptance incomplete.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            if(validate)File.WriteAllBytes(path+".checkpoint",CheckpointCodec.Encode(checkpoint));
        }
        [Serializable] private sealed class Report
        {public string error,tick,hash;public int slot,coins,renderedPlayers,activeSmokeDrivers;public bool complete,freeRescue,paidRespawn,earlyRespawn,downed,helping,dead,paymentCaptured;public Player[] players;}
        [Serializable] private sealed class Player {public int slot,coins,earned,spent,health,towed;public uint deaths;public bool water,dead;}
    }
}
#endif
