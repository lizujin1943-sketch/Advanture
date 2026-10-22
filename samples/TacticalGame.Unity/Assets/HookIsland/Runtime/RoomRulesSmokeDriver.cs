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
    public sealed class RoomRulesSmokeDriver:MonoBehaviour
    {
        private readonly RoomRulesSmokePilot pilot=new RoomRulesSmokePilot();
        private IslandFishingController room;
        private string report;
        private float started,finished,menuOpened=-1;
        private bool written,closing,menuCaptured,guestRejected;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-room-rules-smoke")!="1" || string.IsNullOrEmpty(IslandFishingController.LaunchValue("-island-report")))return;
            UnityEngine.Object.FindObjectOfType<IslandFishingController>().gameObject.AddComponent<RoomRulesSmokeDriver>();
        }
        private void Start()
        {
            room=GetComponent<IslandFishingController>();report=IslandFishingController.LaunchValue("-island-report");started=Time.realtimeSinceStartup;
            room.InputProvider=_=>{
                if(room.State==null)return default;
                var input=pilot.Sample(room.State,room.LocalSlot);
                // Exercise the same request queue as the menu, including confirmed-state acknowledgements.
                if(!input.RoomRules.IsEmpty)room.RequestFriendlyPranks(input.RoomRules.Enabled);
                return new HookInput(input.MoveX,input.MoveZ,input.AimX,input.AimZ,input.Fire,input.Cancel);
            };
        }
        private void LateUpdate()
        {
            if(written){if(Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                var state=session.InspectState();pilot.Observe(state);
                if(room.LocalSlot!=0 && room.Playing)
                {
                    if(room.RequestFriendlyPranks(!state.RoomRules.FriendlyPranksEnabled))throw new InvalidOperationException("Guest queued host policy.");
                    guestRejected=true;
                }
                if(pilot.Complete(state) && menuOpened<0){room.GetComponent<FishingOverlay>().OpenMenu();menuOpened=Time.realtimeSinceStartup;}
                if(menuOpened>=0 && !menuCaptured && Time.realtimeSinceStartup-menuOpened>.2f)
                {ScreenCapture.CaptureScreenshot(report+".menu.png");menuCaptured=true;}
                if(pilot.Complete(state) && menuCaptured && !closing && room.Host?.Session!=null && session.ConfirmedTick>=600)
                {room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1));closing=true;}
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>75)
            {
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Room rules timeout at stage "+pilot.Stage},true));Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot)
        {
            var state=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report{slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=state.Coins,
                complete=pilot.Complete(state),disabledSeen=pilot.DisabledSeen,enabledSeen=pilot.EnabledSeen,blockedLandCast=pilot.BlockedLandCast,
                landPullSeen=pilot.LandPullSeen,releasedLandPull=pilot.ReleasedLandPull,rescued=pilot.Rescued,menuCaptured=menuCaptured,guestRejected=guestRejected,
                friendlyPranksEnabled=state.RoomRules.FriendlyPranksEnabled,lastRequest=state.RoomRules.LastRequest,
                renderedPlayers=room.Presentation.RenderedPlayers,
                activeSmokeDrivers=room.GetComponents<MonoBehaviour>().Count(c=>c.GetType().Name.EndsWith("SmokeDriver",StringComparison.Ordinal))};
            if(!result.complete || !menuCaptured || result.activeSmokeDrivers!=1 || (slot>0 && !guestRejected))result.error="Room rules or menu acceptance incomplete.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            File.WriteAllBytes(path+".checkpoint",CheckpointCodec.Encode(checkpoint));
        }
        [Serializable] private sealed class Report
        {
            public string error,tick,hash;public int slot,coins,renderedPlayers,activeSmokeDrivers;public uint lastRequest;
            public bool complete,disabledSeen,enabledSeen,blockedLandCast,landPullSeen,releasedLandPull,rescued,menuCaptured,guestRejected,friendlyPranksEnabled;
        }
    }
}
#endif
