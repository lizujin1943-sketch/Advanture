using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;
using NUnit.Framework;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class RecoverySceneTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
        [Test] public void FirstEntryFailureRetainsItsPendingProofForAnExplicitRetry()
        {
            var go=new GameObject("pending-recovery-controller");go.SetActive(false);
            try
            {
                var room=go.AddComponent<IslandFishingController>();Set(room,"map",IslandMap.CreatePlatform());
                var clock=(Stopwatch)typeof(IslandFishingController).GetField("clock",Private).GetValue(room);clock.Start();
                var poll=typeof(IslandFishingController).GetMethod("PollConnection",Private);
                room.HostRoom(0,2);var end=clock.Elapsed+TimeSpan.FromSeconds(4);
                while(room.Client.RecoveryTicket==null && clock.Elapsed<end){poll.Invoke(room,null);Thread.Sleep(1);}
                Assert.That(room.Client.RecoveryTicket,Is.Not.Null);Assert.That(room.Client.ResumeTicket,Is.Null);
                Assert.That(room.Playing,Is.False,"A pending proof must not grant gameplay control.");
                typeof(IslandClient).GetMethod("Fail",Private).Invoke(room.Client,new object[]{"First entry interrupted"});
                poll.Invoke(room,null);Assert.That(room.CanReconnect,Is.True);
                Assert.That(room.Reconnect(),Is.True);Assert.That(room.Synchronizing,Is.True);Assert.That(room.Playing,Is.False);
                room.LeaveRoom();Assert.That(room.CanReconnect,Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void OwnFailureClearsPendingActionsAndOffersResumeWhileHostKeepsRunning()
        {
            var go=new GameObject("recovery-controller");go.SetActive(false);
            try
            {
                var room=go.AddComponent<IslandFishingController>();Set(room,"map",IslandMap.CreatePlatform());
                var clock=(Stopwatch)typeof(IslandFishingController).GetField("clock",Private).GetValue(room);clock.Start();
                var poll=typeof(IslandFishingController).GetMethod("PollConnection",Private);Assert.That(poll,Is.Not.Null);
                void Until(Func<bool> done)
                {var end=clock.Elapsed+TimeSpan.FromSeconds(4);while(!done() && clock.Elapsed<end){poll.Invoke(room,null);Thread.Sleep(1);}Assert.That(done(),Is.True,room.Failure);}
                room.HostRoom(0,2);Until(()=>room.CanStartRoom);Assert.That(room.StartRoomNow(),Is.True);Until(()=>room.Playing);
                var original=room.Client.Session;var host=room.Host;
                Set(room,"pendingPurchaseId",17u);Set(room,"pendingUpgrade",HookUpgrade.Range);Set(room,"pendingFire",true);
                using(var guest=new IslandClient(room.Map,IPAddress.Loopback,host.Port,ushort.MaxValue))
                {
                    Until(()=>{guest.Poll(clock.Elapsed,_=>default);return guest.ResumeTicket!=null;});
                    Assert.That(room.PurchasePending,Is.True,"Another player's join cleared our pending request.");
                    Assert.That(room.Client.Session,Is.SameAs(original));
                }
                typeof(IslandClient).GetMethod("Fail",Private).Invoke(room.Client,new object[]{"Test connection lost"});
                poll.Invoke(room,null);Assert.That(room.CanReconnect,Is.True);Assert.That(room.Playing,Is.False);
                Assert.That(room.PurchasePending,Is.False);Assert.That(room.Host,Is.SameAs(host));Assert.That(host.Failure,Is.Null);
                ulong tick=host.Session.ConfirmedTick;Until(()=>host.Session.ConfirmedTick>tick);
                Assert.That(room.Reconnect(),Is.True);Assert.That(room.Synchronizing,Is.True);Assert.That(room.CanReconnect || room.Playing,Is.False);
                room.LeaveRoom();Assert.That(room.CanReconnect || room.CanStartRoom,Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void RoomActionsAreUnavailableWithoutAConnection()
        {
            var go=new GameObject("recovery-actions");go.SetActive(false);
            try
            {
                var room=go.AddComponent<IslandFishingController>();
                foreach(string name in new[]{"CanStartRoom","CanReconnect"})
                {var property=typeof(IslandFishingController).GetProperty(name);Assert.That(property,Is.Not.Null,name);Assert.That(property.GetValue(room),Is.False);}
                foreach(string name in new[]{"StartRoomNow","Reconnect"})
                {var method=typeof(IslandFishingController).GetMethod(name);Assert.That(method,Is.Not.Null,name);Assert.That(method.Invoke(room,null),Is.False);}
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void ReservedPlayersAreHiddenWhileDisconnectedBodiesRemainVisible()
        {
            var go=new GameObject("recovery-visibility");var camera=go.AddComponent<Camera>();var map=IslandMap.CreatePlatform();
            var players=new[]{new Participant(0,Guid.NewGuid()),new Participant(1,Guid.NewGuid(),isActive:false)};
            var simulation=new HookGame().Create();
            simulation.Initialize(new SimulationInitialization(new SessionDescriptor(Guid.NewGuid(),1,30,players),new ByteString(map.Encode())));
            try
            {
                using(var view=new FishingPresentation(go.transform,camera,Shader.Find("Sprites/Default"),map))
                {
                    var s=simulation.Capture();view.Present(s,s,0,Vector3.zero,false);
                    Assert.That(view.RenderedPlayers,Is.EqualTo(1),"Unused reserved seat has an avatar.");
                    var bodies=s.Players.ToArray();bodies[0].Unavailable=true;
                    s=new HookState(s.Steps,bodies,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle);
                    view.Present(s,s,0,Vector3.zero,false);Assert.That(view.RenderedPlayers,Is.EqualTo(1),"Disconnected entered body disappeared.");
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void InitialRecoverySnapshotDoesNotReplayOldCatchPopups()
        {
            var go=new GameObject("recovery-catch-feedback");var camera=go.AddComponent<Camera>();var map=IslandMap.CreatePlatform();
            try
            {
                using(var view=new FishingPresentation(go.transform,camera,Shader.Find("Sprites/Default"),map))
                {
                    var player=new Fisher{Slot=0,Phase=HookPhase.Ready,Catches=4,Coins=20,CoinsEarned=50,Casts=10,FishId=-1,DriftwoodId=-1,HookedPlayer=-1,PulledBy=-1};
                    var fish=map.FishSpots.Select((p,i)=>new Fish{Id=i,X=p.X,Z=p.Z,Owner=-1}).ToArray();
                    var s=new HookState(200,new[]{player},fish,20);view.Present(s,s,0,Vector3.zero,false);
                    Assert.That(view.Popups.Count,Is.Zero,"Restored earnings were shown as a new reward.");
                    Assert.That(view.PresentedCatches,Is.Zero);
                    player.Catches++;player.Coins+=10;s=new HookState(201,new[]{player},fish,30);view.Present(s,s,0,Vector3.zero,false);
                    Assert.That(view.Popups.Count,Is.EqualTo(1));Assert.That(view.Popups[0].Value,Is.EqualTo(10));
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}
