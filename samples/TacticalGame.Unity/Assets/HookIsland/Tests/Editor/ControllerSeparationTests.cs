using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class ControllerSeparationTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
        private static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Private).GetValue(target);
        [Test] public void LocalInputLatchesOneCastAndResetDropsAllPriorIntent()
        {
            using(var input=new FishingPlayerInput())
            {
                Set(input,"moveX",(short)1000);Set(input,"pendingFire",true);Set(input,"pendingCancel",true);Set(input,"aim",new Vector3(3,0,7));
                var first=input.Consume(1,null,false);Assert.That(first.Fire && first.Cancel,Is.True);Assert.That(first.AimX,Is.EqualTo(3000));
                var next=input.Consume(2,null,false);Assert.That(next.Fire || next.Cancel,Is.False);Assert.That(next.MoveX,Is.EqualTo(1000));
                input.Reset();Assert.That(input.Consume(3,null,false).Encode(),Is.EqualTo(default(HookInput).Encode()));
                bool providerCalled=false;input.Consume(4,_=>{providerCalled=true;return default;},true);Assert.That(providerCalled,Is.False);
            }
        }
        [Test] public void RealControllerUpdateDeliversConfirmedCastToPresentationExactlyOnce()
        {
            var go=new GameObject("confirmed-controller-integration");go.SetActive(false);
            var cameraObject=new GameObject("confirmed-controller-camera");var camera=cameraObject.AddComponent<Camera>();
            var room=go.AddComponent<IslandFishingController>();var map=IslandMap.CreatePlatform();
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../artifacts/presentation-separation/unity-integration",Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            FishingPresentation view=null;
            try
            {
                view=new FishingPresentation(go.transform,Shader.Find("Sprites/Default"),map);
                Set(room,"map",map);Set(room,"viewCamera",camera);Set(room,"presentation",view);Set(room,"saves",new IslandSaveCoordinator(directory));
                Get<FishingPlayerInput>(room,"playerInput").Initialize(camera,new FishingCameraView(camera,true));
                var clock=Get<Stopwatch>(room,"clock");clock.Start();
                var update=typeof(IslandFishingController).GetMethod("Update",Private);
                void Until(Func<bool> done)
                {
                    var end=clock.Elapsed+TimeSpan.FromSeconds(8);
                    while(!done() && clock.Elapsed<end){update.Invoke(room,null);Thread.Sleep(1);}
                    Assert.That(done(),Is.True,room.Failure);
                }
                room.InputProvider=_=>default;room.HostRoom(0,1);Until(()=>room.Playing && room.State!=null);
                bool fire=true;room.InputProvider=_=>{bool cast=fire;fire=false;return new HookInput(0,0,0,-20000,cast);};
                Until(()=>view.PresentedCasts==1);
                Assert.That(room.ThirdPerson,Is.True,"Entry camera mode must survive input extraction.");
                Until(()=>room.State.Players[0].Phase==HookPhase.Ready);
                Assert.That(view.PresentedCasts,Is.EqualTo(1));
                for(int i=0;i<5;i++)update.Invoke(room,null);
                Assert.That(view.PresentedCasts,Is.EqualTo(1),"Repeated rendered frames replayed cast audio.");
                Assert.That(room.RequestSave(),Is.True);Until(()=>!room.SavePending);Assert.That(room.HasManualSave,Is.True);
                room.LeaveRoom();Assert.That(room.ContinueGame(0),Is.True);Until(()=>room.Playing && room.State!=null);
                Assert.That(view.PresentedCasts,Is.Zero,"Loading a saved cast counter must not play a historical sound.");
                Assert.That(room.Feedback.Acquisitions.Notices,Is.Empty);
            }
            finally
            {
                room.LeaveRoom();Get<FishingPlayerInput>(room,"playerInput").Dispose();view?.Dispose();
                Object.DestroyImmediate(go);Object.DestroyImmediate(cameraObject);Directory.Delete(directory,true);
            }
        }
    }
}
