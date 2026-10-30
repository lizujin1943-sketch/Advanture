using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class SaveSceneTests
    {
        private sealed class SceneRoom:IDisposable
        {
            internal readonly GameObject Object;
            internal readonly IslandFishingController Room;
            internal readonly string DirectoryPath;
            private readonly Stopwatch clock;
            private readonly MethodInfo poll;
            internal SceneRoom()
            {
                DirectoryPath=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../artifacts/game-saves/unity-tests",Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(DirectoryPath);
                Object=new GameObject("save-scene-tests");Object.SetActive(false);Room=Object.AddComponent<IslandFishingController>();
                Set("map",IslandMap.CreatePlatform());Set("saves",new IslandSaveCoordinator(DirectoryPath));
                clock=(Stopwatch)typeof(IslandFishingController).GetField("clock",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(Room);clock.Start();
                poll=typeof(IslandFishingController).GetMethod("PollConnection",BindingFlags.NonPublic|BindingFlags.Instance);
                Room.HostRoom(0,1);Until(()=>Room.CanRequestSave && Room.Host.CanSave);
            }
            internal string PathFor(string name)=>Path.Combine(DirectoryPath,name+".hisave");
            internal void Set(string field,object value)=>typeof(IslandFishingController).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(Room,value);
            internal void Poll()=>poll.Invoke(Room,null);
            internal void Until(Func<bool> done)
            {var end=clock.Elapsed+TimeSpan.FromSeconds(8);while(!done() && clock.Elapsed<end){Poll();Thread.Sleep(1);}Assert.That(done(),Is.True,Room.Failure??Room.SaveMessage);}
            public void Dispose(){Room.LeaveRoom();UnityEngine.Object.DestroyImmediate(Object);Directory.Delete(DirectoryPath,true);}
        }
        [Test] public void SaveWaitsForCastThenContinuesThroughTheRealController()
        {
            using(var scene=new SceneRoom())
            {
                var room=scene.Room;bool cast=false;
                room.InputProvider=t=>{bool fire=!cast;cast=true;return new HookInput(0,0,0,-20000,fire);};
                scene.Until(()=>room.Host.Session.InspectState().Players[0].Phase==HookPhase.Flying);
                Assert.That(room.RequestSave(),Is.True);scene.Poll();
                Assert.That(room.SavePending,Is.True);Assert.That(File.Exists(scene.PathFor("manual")),Is.False);
                scene.Until(()=>!room.SavePending);Assert.That(room.HasManualSave,Is.True);Assert.That(room.SaveMessage,Does.Contain("保存成功"));
                var saved=new IslandSaveStore(scene.PathFor("manual")).Load();
                room.InputProvider=null;room.LeaveRoom();Assert.That(room.ContinueGame(0),Is.True);
                scene.Until(()=>room.Playing && room.Host.CanSave);
                Assert.That(room.Host.Capacity,Is.EqualTo(saved.Capacity));Assert.That(room.Host.Session.InspectState().Players[0].Phase,Is.EqualTo(HookPhase.Ready));
            }
        }
        [Test] public void FailedSaveAndLeaveKeepsTheRoomAndPreviousFile()
        {
            using(var scene=new SceneRoom())
            {
                var room=scene.Room;Assert.That(room.RequestSave(),Is.True);scene.Until(()=>!room.SavePending);
                string path=scene.PathFor("manual");var previous=File.ReadAllBytes(path);
                using(var blocker=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                {
                    Assert.That(room.RequestSave(true),Is.True);scene.Until(()=>!room.SavePending);
                    Assert.That(room.InRoom,Is.True);Assert.That(room.SaveMessage,Does.Contain("保存失败"));CollectionAssert.AreEqual(previous,File.ReadAllBytes(path));
                }
                Assert.That(room.RequestSave(true),Is.True);scene.Until(()=>!room.InRoom);
                Assert.That(new IslandSaveStore(path).Load(),Is.Not.Null);
            }
        }
        [Test] public void AutomaticSlotIsSeparateAndBadLoadDoesNotCreateARoom()
        {
            using(var scene=new SceneRoom())
            {
                var room=scene.Room;room.RequestSave();scene.Until(()=>!room.SavePending);var manual=File.ReadAllBytes(scene.PathFor("manual"));
                typeof(IslandSaveCoordinator).GetField("nextAutoSave",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(typeof(IslandFishingController).GetField("saves",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(room),-1d);scene.Poll();scene.Until(()=>room.HasAutoSave);
                CollectionAssert.AreEqual(manual,File.ReadAllBytes(scene.PathFor("manual")));
                room.LeaveRoom();File.WriteAllBytes(scene.PathFor("manual"),new byte[]{1,2,3});
                Assert.That(room.ContinueGame(0),Is.False);Assert.That(room.InRoom,Is.False);Assert.That(room.Host,Is.Null);Assert.That(room.SaveMessage,Does.Contain("读取失败"));
                Assert.That(room.ContinueGame(0,true),Is.True);scene.Until(()=>room.Playing);
            }
        }
    }
}
