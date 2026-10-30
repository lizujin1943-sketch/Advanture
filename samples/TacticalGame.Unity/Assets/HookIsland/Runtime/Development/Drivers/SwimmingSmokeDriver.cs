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
    public sealed class SwimmingSmokeDriver:MonoBehaviour
    {
        private IslandFishingController room;
        private readonly SwimmingSmokePilot pilot=new SwimmingSmokePilot();
        private string report;
        private float started,finished,nextProgress;
        private bool written,closing,captured,waterCaptured,shopCaptured,lockObserved;
        private Fisher previous;
        private ulong observedTick;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-swimming-smoke")!="1")return;
            UnityEngine.Object.FindObjectOfType<IslandFishingController>().gameObject.AddComponent<SwimmingSmokeDriver>();
        }
        private void Start()
        {
            room=GetComponent<IslandFishingController>();report=IslandFishingController.LaunchValue("-island-report");
            started=Time.realtimeSinceStartup;room.InputProvider=Sample;
        }
        private HookInput Sample(ulong tick)
        {
            if(room.State==null)return default;
            var command=pilot.Sample(room.Map,room.State,room.LocalSlot,tick);
            if(command.Inventory.Action!=InventoryAction.None)
            {
                room.OpenShop();room.Equipment.RequestInventory(command.Inventory);
                if(!shopCaptured){ScreenCapture.CaptureScreenshot(report+".shop.png");shopCaptured=true;}
                return default;
            }
            if(!room.Equipment.Pending)room.CloseShop();return command;
        }
        private void LateUpdate()
        {
            if(written){if(Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                var s=session.InspectState();var p=s.Players[0];
                if(s.Steps>observedTick)
                {
                    if(observedTick>0 && previous.Swimming.InWater && p.Swimming.InWater && previous.Phase!=HookPhase.Ready && p.Phase!=HookPhase.Ready)
                    {
                        if(p.X!=previous.X || p.Z!=previous.Z)throw new InvalidOperationException("Own cast allowed swimming movement.");
                        lockObserved=true;
                    }
                    previous=p;observedTick=s.Steps;
                }
                if(p.Swimming.InWater && !waterCaptured){ScreenCapture.CaptureScreenshot(report+".water.png");waterCaptured=true;}
                if(Time.realtimeSinceStartup>=nextProgress)
                {nextProgress=Time.realtimeSinceStartup+8;Write(report+".progress.json",session.CaptureCheckpoint(),room.LocalSlot,false);}
                bool complete=pilot.Complete(s);
                if(complete && !captured){ScreenCapture.CaptureScreenshot(report+".png");captured=true;}
                if(complete && !closing && room.Host?.Session!=null && session.ConfirmedTick>=600)
                {room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1,true));closing=true;}
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot,true);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>180)
            {
                if(session?.LocalParticipant!=null)Write(report+".state.json",session.CaptureCheckpoint(),room.LocalSlot,false);
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Swimming acceptance timeout"},true));Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot,bool validate)
        {
            var s=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report{slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=s.Coins,
                complete=pilot.Complete(s),lockObserved=lockObserved,renderedPlayers=room.Presentation.RenderedPlayers,
                players=s.Players.Select(p=>new Player{slot=p.Slot,x=p.X,z=p.Z,health=p.Vitals.Health,stamina=p.Stamina,water=p.Swimming.InWater,
                    ring=SwimRingCatalog.Equipped(s.Tackle.Inventory,p.Slot)?.Level??0,coins=p.Coins,earned=p.CoinsEarned,catches=p.Catches,rescued=p.TimesHooked}).ToArray()};
            if(validate && (!result.complete || !lockObserved))result.error="Swimming/rescue/movement lock incomplete.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            if(validate)File.WriteAllBytes(path+".checkpoint",CheckpointCodec.Encode(checkpoint));
        }
        [Serializable]private sealed class Report
        {public string error,tick,hash;public int slot,coins,renderedPlayers;public bool complete,lockObserved;public Player[] players;}
        [Serializable]private sealed class Player
        {public int slot,x,z,health,stamina,ring,coins,earned,catches,rescued;public bool water;}
    }
}
#endif
