#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;
using FrameSync.Sessions;
using ReliableUdp;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Normal input acceptance plus an explicit transport interruption; never edits gameplay state.</summary>
    public sealed class RecoverySmokeDriver:MonoBehaviour
    {
        private RecoverySmokePilot pilot=new RecoverySmokePilot();
        private IslandFishingController room;
        private ClientSession<HookState> originalSession;
        private string report;
        private float started,finished,cutAt,nextProgress;
        private bool late,written,closing,cut,reconnectRequested,offlineSeen,resumed,preserved,syncImage,parking;
        private ulong frozenAt;
        private int joinedMask,target;
        private Fisher before;
        private InventoryItem[] items;
        private Guid identity;
        private ulong generation;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-recovery-smoke")!="1" || string.IsNullOrEmpty(IslandFishingController.LaunchValue("-island-report")))return;
            UnityEngine.Object.FindObjectOfType<IslandFishingController>().gameObject.AddComponent<RecoverySmokeDriver>();
        }
        private void Start()
        {
            room=GetComponent<IslandFishingController>();report=IslandFishingController.LaunchValue("-island-report");
            late=IslandFishingController.LaunchValue("-island-late-join")=="1";started=Time.realtimeSinceStartup;room.InputProvider=Sample;
        }
        private HookInput Sample(ulong tick)
        {
            var s=room.State;if(s==null || !RecoverySmokePilot.AllEntered(s))return default;
            if(frozenAt!=0 && (late || !resumed || room.LocalSlot!=target))return default;
            if(parking && !resumed)return RecoverySmokePilot.Park(s.Players[room.LocalSlot]);
            return pilot.Sample(room.Map,s,room.LocalSlot,tick);
        }
        private static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
        private void InterruptTransport()
        {
            var client=room.Client;var node=Field<UdpNode>(client,"_node");
            node.Close(Field<ulong>(client,"_peer"),"Development acceptance interruption");
            node.Poll(Field<Stopwatch>(room,"clock").Elapsed-Field<TimeSpan?>(client,"_clockOrigin").Value);
            cut=true;cutAt=Time.realtimeSinceStartup;
        }
        private void LateUpdate()
        {
            if(written){if(Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            try{Advance();}
            catch(Exception e){File.WriteAllText(report,JsonUtility.ToJson(new Report{error=e.ToString()},true));UnityEngine.Debug.LogException(e);Application.Quit(1);}
        }
        private void Advance()
        {
            if(late && room.CanStartRoom && !room.StartRoomNow())throw new InvalidOperationException("Early start refused.");
            var session=room.Client?.Session;
            if(originalSession==null && room.Playing)originalSession=session;
            if(!late && cut && !reconnectRequested && room.CanReconnect)
            {
                if(!offlineSeen){offlineSeen=true;ScreenCapture.CaptureScreenshot(report+".offline.png");}
                if(Time.realtimeSinceStartup-cutAt>2)
                {
                    if(!room.Reconnect() || !room.Synchronizing || room.Playing)throw new InvalidOperationException("Formal reconnect/synchronizing action failed.");
                    reconnectRequested=true;syncImage=true;ScreenCapture.CaptureScreenshot(report+".sync.png");return;
                }
            }
            if(session?.LocalParticipant!=null && room.State!=null)
            {
                var state=session.InspectState();target=state.Players.Count-1;
                int entered=state.Players.Count(p=>!p.WaitingToJoin);
                if(entered>0 && (joinedMask&(1<<(entered-1)))==0 && room.Presentation.RenderedPlayers==entered)
                {joinedMask|=1<<(entered-1);ScreenCapture.CaptureScreenshot(report+".joined-"+entered+".png");}
                parking|=RecoverySmokePilot.Purchased(state);
                if(frozenAt==0 && parking && RecoverySmokePilot.Parked(state))frozenAt=state.Steps;
                if(frozenAt!=0 && state.Steps>=frozenAt+30 && items==null)
                {
                    before=state.Players[target];items=state.Tackle.Inventory.Items.Where(i=>i.Owner==target).ToArray();
                    var member=session.CaptureCheckpoint().Membership.Find((ushort)target);identity=member.ParticipantId;generation=member.Generation;
                }
                if(!late && items!=null)
                {
                    if(room.LocalSlot==target && !cut && state.Steps>=frozenAt+45)InterruptTransport();
                    if(state.Players[target].Unavailable && !offlineSeen)
                    {offlineSeen=true;ScreenCapture.CaptureScreenshot(report+".offline.png");}
                    var member=session.CaptureCheckpoint().Membership.Find((ushort)target);
                    if(!resumed && member.IsActive && member.Generation>generation)
                    {
                        var p=state.Players[target];
                        preserved=member.ParticipantId==identity && p.Coins==before.Coins && p.CoinsEarned==before.CoinsEarned &&
                            p.Catches==before.Catches && p.EquippedRod==before.EquippedRod && p.LastRodRequest==before.LastRodRequest && p.LastRodResult==before.LastRodResult &&
                            p.X==before.X && p.Z==before.Z && p.Vitals.Health==before.Vitals.Health && items.SequenceEqual(state.Tackle.Inventory.Items.Where(i=>i.Owner==target));
                        if(!preserved)throw new InvalidOperationException("Recovery changed state: identity="+(member.ParticipantId==identity)+
                            " position="+before.X+","+before.Z+" -> "+p.X+","+p.Z+" health="+before.Vitals.Health+" -> "+p.Vitals.Health+
                            " coins="+before.Coins+" -> "+p.Coins+" earned="+before.CoinsEarned+" -> "+p.CoinsEarned+" catches="+before.Catches+" -> "+p.Catches+
                            " rod="+before.EquippedRod+" -> "+p.EquippedRod+" receipt="+before.LastRodRequest+"/"+before.LastRodResult+" -> "+p.LastRodRequest+"/"+p.LastRodResult+
                            " inventory="+items.SequenceEqual(state.Tackle.Inventory.Items.Where(i=>i.Owner==target)));
                        resumed=true;pilot=new RecoverySmokePilot();ScreenCapture.CaptureScreenshot(report+".resumed.png");
                    }
                }
                bool complete=Complete(state);
                if(Time.realtimeSinceStartup>=nextProgress)
                {nextProgress=Time.realtimeSinceStartup+10;Write(report+".progress.json",session.CaptureCheckpoint(),room.LocalSlot,false);}
                if(complete && !closing && room.Host?.Session!=null && session.ConfirmedTick>=600)
                {room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1,true));closing=true;}
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot,true);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            bool recovering=cut && (!reconnectRequested || room.Synchronizing);
            if((room.Failure!=null && !recovering) || Time.realtimeSinceStartup-started>220)
                throw new InvalidOperationException(room.Failure??"Recovery acceptance timeout.");
        }
        private bool Complete(HookState state)=>late?items!=null && state.Steps>=frozenAt+45:
            resumed && preserved && state.Players[target].Catches>before.Catches && state.Players[target].Phase==HookPhase.Ready;
        private void Write(string path,Checkpoint checkpoint,int slot,bool validate)
        {
            var state=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report{slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=state.Coins,late=late,
                complete=Complete(state),offlineSeen=offlineSeen,resumed=resumed,preserved=preserved,syncImage=syncImage,joinedMask=joinedMask,
                sessionRetained=ReferenceEquals(originalSession,room.Client.Session),renderedPlayers=room.Presentation.RenderedPlayers,
                activeSmokeDrivers=room.GetComponents<MonoBehaviour>().Count(c=>c.GetType().Name.EndsWith("SmokeDriver",StringComparison.Ordinal)),
                players=state.Players.Select(p=>new Player{slot=p.Slot,coins=p.Coins,earned=p.CoinsEarned,catches=p.Catches,rod=p.EquippedRod,request=p.LastRodRequest,receipt=p.LastRodResult.ToString()}).ToArray()};
            if(validate && (!result.complete || result.renderedPlayers!=state.Players.Count || result.activeSmokeDrivers!=1 ||
                (!late && (!offlineSeen || (room.LocalSlot==target && !syncImage)))))result.error="Recovery flow, rendering or driver isolation incomplete.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            if(validate)File.WriteAllBytes(path+".checkpoint",CheckpointCodec.Encode(checkpoint));
        }
        [Serializable] private sealed class Report
        {
            public string error,tick,hash;public int slot,coins,joinedMask,renderedPlayers,activeSmokeDrivers;
            public bool late,complete,offlineSeen,resumed,preserved,syncImage,sessionRetained;public Player[] players;
        }
        [Serializable] private sealed class Player{public int slot,coins,earned,catches,rod;public uint request;public string receipt;}
    }
}
#endif
