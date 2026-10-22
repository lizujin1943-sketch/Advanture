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
    public sealed class EnemySmokeDriver:MonoBehaviour
    {
        private IslandFishingController room;
        private string report;
        private float started,finished,nextProgress;
        private bool written,closing,captured,windup,waterball,knockback,impact,waterCaptured,patrol,submerged,relocated,hooked;
        private int? firstPiranhaX;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-enemy-smoke")!="1")return;
            UnityEngine.Object.FindObjectOfType<IslandFishingController>().gameObject.AddComponent<EnemySmokeDriver>();
        }
        private void Start()
        {
            room=GetComponent<IslandFishingController>();report=IslandFishingController.LaunchValue("-island-report");started=Time.realtimeSinceStartup;
            room.InputProvider=_=>room.State==null?default:EnemySmokePilot.Sample(room.State,room.LocalSlot);
        }
        private void LateUpdate()
        {
            if(written){if(Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                var state=session.InspectState();var views=room.Enemies;
                if(views!=null)
                {
                    if(views.WindupVisible && state.Enemies.Monster.Timer<=state.Tackle.Config.Enemies.WindupTicks-15 && !windup){windup=true;ScreenCapture.CaptureScreenshot(report+".windup.png");}
                    // Wait until the projectile clears the muzzle so the saved image shows its flight.
                    if(views.RenderedWaterballs>0 && state.Enemies.Waterballs.Any(b=>b.Distance>=1400) && !waterball)
                    {waterball=true;ScreenCapture.CaptureScreenshot(report+".waterball.png");}
                    impact|=views.ImpactVisible;
                }
                if(firstPiranhaX==null)firstPiranhaX=state.Enemies.Piranhas[0].X;
                patrol|=state.Enemies.Piranhas[0].Owner<0 && firstPiranhaX!=state.Enemies.Piranhas[0].X;
                submerged|=state.Enemies.Monster.Phase==MonsterPhase.Cooldown && views!=null && views.RenderedMonsters==0;
                relocated|=state.Enemies.Monster.Cycle>1;
                if(!hooked && state.Players.Any(p=>p.HookedPiranha>0)){hooked=true;ScreenCapture.CaptureScreenshot(report+".capture.png");}
                knockback|=state.Players.Any(p=>p.Knockback.Active);
                if(state.Players.Any(p=>p.Swimming.InWater) && !waterCaptured){waterCaptured=true;ScreenCapture.CaptureScreenshot(report+".water.png");}
                if(Time.realtimeSinceStartup>=nextProgress)
                {nextProgress=Time.realtimeSinceStartup+8;Write(report+".progress.json",session.CaptureCheckpoint(),room.LocalSlot,false);}
                bool complete=EnemySmokePilot.Complete(state);
                if(complete && !captured){ScreenCapture.CaptureScreenshot(report+".png");captured=true;}
                if(complete && !closing && room.Host?.Session!=null && session.ConfirmedTick>=600)
                {room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1,true));closing=true;}
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot,true);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>90)
            {
                if(session?.LocalParticipant!=null)Write(report+".state.json",session.CaptureCheckpoint(),room.LocalSlot,false);
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Enemy acceptance timeout"},true));Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot,bool validate)
        {
            var s=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report {slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=s.Coins,
                complete=EnemySmokePilot.Complete(s),patrol=patrol,submerged=submerged,relocated=relocated,hooked=hooked,piranhaCaptures=s.Enemies.Captures,monsterLevel=s.Enemies.Monster.Level,windup=windup,waterball=waterball,knockback=knockback,impact=impact,
                renderedPlayers=room.Presentation.RenderedPlayers,renderedPiranhas=room.Enemies?.RenderedPiranhas??0,renderedMonsters=room.Enemies?.RenderedMonsters??0,
                activeSmokeDrivers=room.GetComponents<MonoBehaviour>().Count(c=>c.GetType().Name.EndsWith("SmokeDriver",StringComparison.Ordinal)),
                bites=s.Enemies.Bites,shots=s.Enemies.Shots,interruptions=s.Enemies.Interruptions,waterHits=s.Enemies.WaterHits,
                players=s.Players.Select(p=>new Player {slot=p.Slot,x=p.X,z=p.Z,health=p.Vitals.Health,water=p.Swimming.InWater}).ToArray()};
            if(validate && (!result.complete || !patrol || !submerged || !relocated || !hooked || !windup || !waterball || !knockback || !impact || result.activeSmokeDrivers!=1))result.error="Enemy rules or presentation acceptance incomplete.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            if(validate)File.WriteAllBytes(path+".checkpoint",CheckpointCodec.Encode(checkpoint));
        }
        [Serializable] private sealed class Report
        {public string error,tick,hash;public int slot,coins,renderedPlayers,renderedPiranhas,renderedMonsters,activeSmokeDrivers,bites,shots,interruptions,waterHits,piranhaCaptures,monsterLevel;public bool complete,windup,waterball,knockback,impact,patrol,submerged,relocated,hooked;public Player[] players;}
        [Serializable] private sealed class Player {public int slot,x,z,health;public bool water;}
    }
}
#endif
