using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland.Simulation;
using Advanture.Tactical.Network;
using UnityEngine.TestTools;
using Advanture.Tactical.Simulation;
using Advanture.TacticalGame.NetworkView;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Advanture.TacticalGame.Tests
{
    public sealed class FortifiedCompoundTests
    {
        private const string Scene = "Assets/TacticalGame/Scenes/FortifiedCompound.unity";
        private TacticalMap map;

        [SetUp] public void OpenMap()
        {
            EditorSceneManager.OpenScene(Scene);
            Physics.SyncTransforms();
            map=TacticalMap.Decode(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/TacticalGame/Art/FortifiedCompound/FortifiedCompound.bytes").bytes);
        }
        [Test] public void PlazaSpawnsHaveHeadroomAndRoomUsesTheNetworkController()
        {
            Assert.That(EditorBuildSettings.scenes.Any(s=>s.enabled && s.path==Scene),Is.True);
            Assert.That(Object.FindObjectOfType<NetworkYardController>().AllowSolo,Is.True);
            Assert.That(Object.FindObjectsOfType<NetworkSmokeDriver>(),Is.Empty);
            Assert.That(Object.FindObjectsOfType<FirstPersonMotor>(),Is.Empty);
            Assert.That(map.Spawns.Count,Is.EqualTo(4));
            foreach(var spawn in map.Spawns)
            {
                var feet=new Vector3(spawn.X*.001f,2.05f,spawn.Z*.001f);
                Assert.That(Physics.Raycast(feet+Vector3.up,Vector3.down,out var hit,2),Is.True);
                Assert.That(hit.collider.name,Does.Contain("Plaza"));
                Assert.That(hit.point.y,Is.EqualTo(map.GroundHeight(spawn.X,spawn.Z)*.001f).Within(.015f));
                Assert.That(Physics.CheckCapsule(feet+Vector3.up*.36f,feet+Vector3.up*1.55f,.35f),Is.False);
            }
        }
        [Test] public void AllBreachEntrancesHavePhysicalSupportAndHeadroom()
        {
            Assert.That(map.MonsterEntrances.Count,Is.EqualTo(3));
            foreach(var port in map.MonsterEntrances)
            {
                var feet=new Vector3(port.X*.001f,map.GroundHeight(port.X,port.Z)*.001f,port.Z*.001f);
                Assert.That(Physics.SphereCast(feet+Vector3.up, .24f, Vector3.down,out var hit,1.6f),Is.True,feet.ToString());
                Assert.That(hit.point.y,Is.EqualTo(feet.y).Within(.22f),feet.ToString());
                Assert.That(Physics.CheckCapsule(feet+Vector3.up*.50f,feet+Vector3.up*1.55f,.28f),Is.False,feet.ToString());
            }
        }
        [Test] public void BothAmmoDepotsHaveAReachableFloorBesideTheirCases()
        {
            Assert.That(map.AmmoDepots.Count,Is.EqualTo(2));
            foreach (var point in map.AmmoDepots)
            {
                var feet = new Vector3(point.X*.001f,map.GroundHeight(point.X,point.Z)*.001f,point.Z*.001f);
                Assert.That(Physics.Raycast(feet+Vector3.up,Vector3.down,out var hit,1.5f),Is.True);
                Assert.That(hit.point.y,Is.EqualTo(feet.y).Within(.035f));
                Assert.That(Physics.CheckCapsule(feet+Vector3.up*.36f,feet+Vector3.up*1.55f,.35f),Is.False);
            }
        }

        [TestCase(-8.125f,-16.125f)]
        [TestCase(-8.125f,-13.125f)]
        [TestCase(-8.125f,-11.125f)]
        [TestCase(8.125f,42.875f)]
        [TestCase(30.375f,10.375f)]
        [TestCase(2.125f,-42.875f)]
        public void YardAndStairLandingsMatchTheSupportMesh(float x,float z)
        {
            Assert.That(map.CanStand(Mathf.RoundToInt(1000*x),Mathf.RoundToInt(1000*z)),Is.True);
            float height=map.GroundHeight(Mathf.RoundToInt(1000*x),Mathf.RoundToInt(1000*z))*.001f;
            Assert.That(Physics.Raycast(new Vector3(x,height+1,z),Vector3.down,out var hit,2),Is.True);
            Assert.That(hit.point.y,Is.EqualTo(height).Within(.025f));
        }
    }

    public sealed class FortifiedCompoundRoomTests
    {
        [UnityTest] public IEnumerator EquipmentBlocksGameplayAndRequiresAFreshClickInAFourPlayerRoom()
        {
            // A batch player cannot capture the OS cursor. The positive fire check needs a graphical Game view.
            if(Application.isBatchMode)Assert.Ignore("Run this input test in a graphical Unity test session.");
            EditorSceneManager.OpenScene("Assets/TacticalGame/Scenes/FortifiedCompound.unity");
            yield return new EnterPlayMode();
            yield return ExerciseFourPlayerInput();
            yield return new ExitPlayMode();
        }

        private static IEnumerator ExerciseFourPlayerInput()
        {
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            var room=Object.FindObjectOfType<NetworkYardController>();
            var overlay=room.GetComponent<FortifiedCompoundOverlay>();
            var guests=new List<TacticalClient>();
            var clock=System.Diagnostics.Stopwatch.StartNew();
            var oldKeyboard=Keyboard.current;
            var oldMouse=Mouse.current;
            // A cursor warp or physical mouse move can otherwise replace the test mouse during recapture.
            var otherDevices=InputSystem.devices.Where(d=>d.enabled && (d is Mouse || d is Keyboard)).ToArray();
            var keyboard=InputSystem.AddDevice<Keyboard>();
            var mouse=InputSystem.AddDevice<Mouse>();
            void Keys(params Key[] down)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(down));
            void Fire(bool down)=>InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left,down));
            IEnumerator WaitFor(System.Func<bool> ready,string description)
            {
                var deadline=clock.Elapsed+System.TimeSpan.FromSeconds(12);
                while(!ready() && clock.Elapsed<deadline)
                {
                    foreach(var guest in guests){guest.Poll(clock.Elapsed,_=>default);Assert.That(guest.Failure,Is.Null);}
                    Assert.That(room.Failure,Is.Null);
                    yield return null;
                }
                Assert.That(ready(),Is.True,description+" Panel "+overlay.ActivePanel+", focus "+Application.isFocused+
                    ", controls "+room.Input.HasControl+", mouse "+Mouse.current+", held "+mouse.leftButton.isPressed);
            }
            IEnumerator ConfirmFrames(ulong count)
            {
                ulong target=room.Client.Session.ConfirmedTick+count;
                return WaitFor(()=>guests.All(g=>g.Session.ConfirmedTick>=target) && room.Client.Session.ConfirmedTick>=target,
                    "Every player must confirm the frames while the panel is open.");
            }
            try
            {
                foreach(var device in otherDevices)InputSystem.DisableDevice(device,keepSendingEvents:true);
                room.HostRoom(0,4);
                for(int i=0;i<3;i++)guests.Add(new TacticalClient(room.Map,IPAddress.Loopback,room.Host.Port));
                yield return WaitFor(()=>room.LocalPlayer.HasValue && guests.All(g=>g.Session?.ConfirmedTick>=2),"Four players must enter the plaza.");
                Fire(false);
                yield return WaitFor(()=>room.Input.HasControl,"The host must capture the game view on spawn.");
                Fire(true);
                yield return WaitFor(()=>room.LocalPlayer.Value.Combat.ShotSequence>0,"The positive control must expend ammunition.");
                Fire(false);
                yield return ConfirmFrames(6);
                var before=room.LocalPlayer.Value;

                Keys(Key.Tab,Key.W,Key.Space,Key.R);
                Fire(true);
                yield return WaitFor(()=>overlay.ActivePanel==FortifiedCompoundOverlay.Panel.Equipment,"Tab must open equipment.");
                yield return ConfirmFrames(12);
                var blocked=room.LocalPlayer.Value;
                Assert.That((blocked.X,blocked.Z,blocked.IsAirborne),Is.EqualTo((before.X,before.Z,false)));
                Assert.That(blocked.Combat.ShotSequence,Is.EqualTo(before.Combat.ShotSequence));
                Assert.That(blocked.Combat.Ammo,Is.EqualTo(before.Combat.Ammo));
                Assert.That(blocked.Combat.ReloadUntilTick,Is.EqualTo(0));
                Assert.That(room.Input.HasControl,Is.False);

                Keys(Key.I);
                yield return WaitFor(()=>!overlay.BlocksPlay && room.Input.HasControl,"I must resume the captured game view.");
                yield return ConfirmFrames(9);
                Assert.That(room.LocalPlayer.Value.Combat.ShotSequence,Is.EqualTo(before.Combat.ShotSequence),"A held inventory click must not fire on closing.");
                Keys();Fire(false);
                yield return ConfirmFrames(4);
                Fire(true);
                yield return WaitFor(()=>room.LocalPlayer.Value.Combat.ShotSequence>before.Combat.ShotSequence,"A new click must fire.");
                Fire(false);Keys(Key.Escape);
                yield return WaitFor(()=>overlay.ActivePanel==FortifiedCompoundOverlay.Panel.Menu,"Escape must open the menu.");
                Keys(Key.F1);
                yield return WaitFor(()=>overlay.ActivePanel==FortifiedCompoundOverlay.Panel.Help,"F1 must open the guide.");
                Keys(Key.Escape);
                yield return WaitFor(()=>!overlay.BlocksPlay && room.Input.HasControl,"Escape must close the guide.");
            }
            finally
            {
                room.LeaveRoom();
                foreach(var guest in guests)guest.Dispose();
                InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);
                foreach(var device in otherDevices)if(device.added)InputSystem.EnableDevice(device);
                if(oldMouse!=null && oldMouse.added)oldMouse.MakeCurrent();
                if(oldKeyboard!=null && oldKeyboard.added)oldKeyboard.MakeCurrent();
            }
        }

        [UnityTest] public IEnumerator TownGunCameraUsesThePlazaEyeAndRestoresTheLobbyOnExit()
        {
            EditorSceneManager.OpenScene("Assets/TacticalGame/Scenes/FortifiedCompound.unity");
            yield return new EnterPlayMode();
            var room=Object.FindObjectOfType<NetworkYardController>();
            var overlay=room.GetComponent<FortifiedCompoundOverlay>();
            var camera=room.ViewCamera;
            var lobbyPosition=camera.transform.position;
            var lobbyRotation=camera.transform.rotation;
            float lobbyFov=camera.fieldOfView;
            try
            {
                Assert.That(overlay,Is.Not.Null);
                room.PlaySolo();
                double deadline=Time.realtimeSinceStartupAsDouble+15;
                while((!room.LocalPlayer.HasValue || Mathf.Abs(camera.fieldOfView-65)>.01f) && Time.realtimeSinceStartupAsDouble<deadline)
                {Assert.That(room.Failure,Is.Null);yield return null;}
                Assert.That(room.LocalPlayer.HasValue,Is.True);
                Assert.That(overlay.BlocksPlay,Is.False);
                Assert.That(camera.fieldOfView,Is.EqualTo(65).Within(.01f));
                var p=room.LocalPlayer.Value;
                var eye=new Vector3(p.X*.001f,room.Map.FeetHeight(p)*.001f+1.65f,p.Z*.001f);
                Assert.That(Vector3.Distance(camera.transform.position,eye),Is.LessThan(.025f));
                Assert.That(Quaternion.Angle(camera.transform.rotation,Quaternion.Euler(room.Input.Pitch,room.Input.Yaw,0)),Is.LessThan(.03f));

                room.InputProvider=_=>InputQuantizer.Create(Vector2.zero,92,-18);
                while(room.LocalPlayer.Value.Yaw!=9200 && Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                var reticle=camera.ViewportPointToRay(new Vector3(.5f,.5f,0));
                Assert.That(room.LocalPlayer.Value.Pitch,Is.EqualTo(-1800));
                Assert.That(Vector3.Angle(reticle.direction,Quaternion.Euler(-18,92,0)*Vector3.forward),Is.LessThan(.04f));
            }
            finally {room.LeaveRoom();}
            Assert.That(camera.transform.position,Is.EqualTo(lobbyPosition));
            Assert.That(Quaternion.Angle(camera.transform.rotation,lobbyRotation),Is.LessThan(.03f));
            Assert.That(camera.fieldOfView,Is.EqualTo(lobbyFov));
            Assert.That(room.Input.Sample(1).Encode(),Is.EqualTo(default(TacticalInput).Encode()));
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator FourPlayersSeeThreeDistinctTeammatesOnThePlaza()
        {
            EditorSceneManager.OpenScene("Assets/TacticalGame/Scenes/FortifiedCompound.unity");
            yield return new EnterPlayMode();
            var owner=Object.FindObjectOfType<NetworkYardController>();
            var rooms=new List<NetworkYardController>{owner};
            var asset=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/TacticalGame/Art/FortifiedCompound/FortifiedCompound.bytes");
            try
            {
                owner.InputProvider=_=>default;
                owner.HostRoom(0);
                for(int i=1;i<4;i++)
                {
                    var root=new GameObject("Guest "+i);
                    root.SetActive(false);
                    var camera=new GameObject("Guest camera").AddComponent<Camera>();
                    camera.transform.SetParent(root.transform);
                    camera.enabled=false;
                    var guest=root.AddComponent<NetworkYardController>();
                    guest.Configure(asset,camera,"要塞基地",true);
                    guest.InputProvider=_=>default;
                    rooms.Add(guest);
                    root.SetActive(true);
                    guest.JoinRoom("127.0.0.1",owner.Host.Port);
                }
                double deadline=Time.realtimeSinceStartupAsDouble+30;
                while(!rooms.All(r=>r.LocalPlayer.HasValue && r.Client.Session.ConfirmedTick>=3) && Time.realtimeSinceStartupAsDouble<deadline)
                {
                    foreach(var room in rooms)Assert.That(room.Failure,Is.Null);
                    yield return null;
                }
                Assert.That(rooms.All(r=>r.Playing && r.LocalPlayer.HasValue),Is.True);
                Assert.That(rooms.Select(r=>r.LocalPlayer.Value.Slot),Is.EquivalentTo(new ushort[]{0,1,2,3}));
                foreach(var room in rooms)
                {
                    var avatars=room.GetComponentsInChildren<NetworkAvatar>();
                    Assert.That(avatars.Length,Is.EqualTo(3));
                    var players=room.Client.Session.InspectState().Players;
                    foreach(var player in players.Where(p=>p.Slot!=room.LocalPlayer.Value.Slot))
                    {
                        string label="P"+(player.Slot+1);
                        var avatar=avatars.Single(a=>a.GetComponentInChildren<TextMesh>().text==label);
                        var spawn=room.Map.Spawns[player.Slot];
                        var feet=new Vector3(spawn.X*.001f,room.Map.GroundHeight(spawn.X,spawn.Z)*.001f,spawn.Z*.001f);
                        Assert.That(Vector3.Distance(avatar.transform.position,feet),Is.LessThan(.025f));
                        Assert.That(avatar.Weapon,Is.Not.Null);
                    }
                }
            }
            finally
            {
                foreach(var room in rooms)room.LeaveRoom();
                foreach(var guest in rooms.Skip(1))Object.Destroy(guest.gameObject);
            }
            yield return null;
            Assert.That(Object.FindObjectsOfType<NetworkAvatar>(),Is.Empty);
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator SoloRoomShowsConfirmedTownModelsAtEveryBreach()
        {
            EditorSceneManager.OpenScene("Assets/TacticalGame/Scenes/FortifiedCompound.unity");
            yield return new EnterPlayMode();
            var room=Object.FindObjectOfType<NetworkYardController>();
            var seen=new HashSet<int>();
            int peak=0;
            double deadline=Time.realtimeSinceStartupAsDouble+75;
            try
            {
                room.PlaySolo();
                while((seen.Count<3 || peak<12) && Time.realtimeSinceStartupAsDouble<deadline)
                {
                    yield return null;
                    Assert.That(room.Failure,Is.Null);
                    if(!room.Playing || !room.LocalPlayer.HasValue)continue;
                    var state=room.Client.Session.InspectState();
                    peak=Mathf.Max(peak,state.Monsters.Creatures.Count(c=>c.Damageable && !c.IsMinion));
                    foreach(var c in state.Monsters.Creatures)
                    {
                        if(c.Phase!=EldritchPhase.Emerging)continue;
                        int index=room.Map.MonsterEntrances.ToList().FindIndex(p=>p.X==c.X && p.Z==c.Z);
                        Assert.That(index,Is.GreaterThanOrEqualTo(0));
                        var view=room.transform.Find($"Eldritch {c.Id:00} — {EldritchCatalog.Get(c.Kind).Name} (confirmed)");
                        Assert.That(view.gameObject.activeInHierarchy,Is.True);
                        Assert.That(view.GetComponentInChildren<SkinnedMeshRenderer>(),Is.Not.Null);
                        float floor=room.Map.GroundHeight(c.X,c.Z)*.001f;
                        float hover=EldritchCatalog.Get(c.Kind).Habitat==EldritchHabitat.Hover ? .35f : 0;
                        var expected=new Vector3(c.X*.001f,floor+hover-.8f*c.Timer/EldritchCatalog.EmergenceTicks,c.Z*.001f);
                        Assert.That(Vector3.Distance(view.position,expected),Is.LessThan(.01f));
                        seen.Add(index);
                    }
                }
                Assert.That(seen.Count,Is.EqualTo(3),"Confirmed and rendered breach entrances");
                Assert.That(peak,Is.GreaterThanOrEqualTo(12));
                Assert.That(room.LocalPlayer.Value.Combat.Reserve,Is.EqualTo(360));
            }
            finally {room.LeaveRoom();}
            yield return null;
            Assert.That(room.GetComponentsInChildren<SkinnedMeshRenderer>(),Is.Empty);
            yield return new ExitPlayMode();
        }
    }
}
