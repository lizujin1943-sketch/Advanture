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
    public sealed class RodSmokeDriver : MonoBehaviour
    {
        private IslandFishingController room;
        private readonly RodSmokePilot pilot=new RodSmokePilot();
        private string report;
        private float started,finished,nextProgress;
        private bool written,closing,shopCaptured,captured,spentStamina,recovered,panelsChecked;
        private int lastStamina=-1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-rod-smoke")!="1")return;
            UnityEngine.Object.FindObjectOfType<IslandFishingController>().gameObject.AddComponent<RodSmokeDriver>();
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
            if(command.RodAction!=RodAction.None)
            {
                if(!panelsChecked)CheckPanelTransitions();
                room.OpenShop();room.Equipment.Request(command.RodAction,command.RodTarget,command.RodBatch);
                if(!shopCaptured){ScreenCapture.CaptureScreenshot(report+".shop.png");shopCaptured=true;}
                return default;
            }
            if(!room.Equipment.Pending)room.CloseShop();return command;
        }
        private void CheckPanelTransitions()
        {
            room.Equipment.ToggleInventory();
            if(!room.Equipment.InventoryOpen)throw new InvalidOperationException("Equipment panel did not open.");
            room.Construction.Toggle();
            if(!room.Construction.Open || room.Equipment.InventoryOpen)throw new InvalidOperationException("Construction did not close equipment.");
            room.Equipment.ToggleInventory();
            if(!room.Equipment.InventoryOpen || room.Construction.Open)throw new InvalidOperationException("Equipment did not close construction.");
            room.OpenShop();
            if(!room.ShopOpen || room.Equipment.InventoryOpen || room.Construction.Open)throw new InvalidOperationException("Merchant panels are not mutually exclusive.");
            panelsChecked=true;
        }
        private void LateUpdate()
        {
            if(written){if(Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                var state=session.InspectState();var p=state.Players[room.LocalSlot];
                spentStamina|=p.FishId>=0 && p.Stamina<FishingRules.MaxStamina(state,p);
                recovered|=lastStamina>=0 && p.Stamina>lastStamina && p.Phase==HookPhase.Ready;lastStamina=p.Stamina;
                if(Time.realtimeSinceStartup>=nextProgress)
                {nextProgress=Time.realtimeSinceStartup+8;Write(report+".progress.json",session.CaptureCheckpoint(),room.LocalSlot,false);}
                bool complete=pilot.Complete(state);
                if(complete && !captured){ScreenCapture.CaptureScreenshot(report+".png");captured=true;}
                if(complete && !closing && room.Host?.Session!=null && session.ConfirmedTick>=600)
                {room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1,true));closing=true;}
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot,true);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>180)
            {
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Rod acceptance timeout"},true));
                Debug.LogError("ROD_SMOKE_FAILED "+room.Failure);Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot,bool validate)
        {
            var s=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report{slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=s.Coins,
                complete=pilot.Complete(s),spentStamina=spentStamina,recovered=recovered,panelsChecked=panelsChecked,renderedPlayers=room.Presentation.RenderedPlayers,
                players=s.Players.Select(p=>new Player{slot=p.Slot,catches=p.Catches,casts=p.Casts,coins=p.Coins,earned=p.CoinsEarned,
                    equipped=p.EquippedRod,stamina=p.Stamina,rod=FishingRules.Equipped(s,p)?.Name}).ToArray()};
            if(validate && !result.complete)result.error="Not every player purchased a rod with personal income and caught again.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
        }
        [Serializable]private sealed class Report
        {public string error,tick,hash;public int slot,coins,renderedPlayers;public bool complete,spentStamina,recovered,panelsChecked;public Player[] players;}
        [Serializable]private sealed class Player
        {public int slot,catches,casts,coins,earned,equipped,stamina;public string rod;}
    }
}
#endif
