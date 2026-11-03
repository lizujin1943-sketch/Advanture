using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Collections.Generic;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class FishingCameraViewTests
    {
        private GameObject root;
        private Camera camera;
        private FishingCameraView view;

        [SetUp] public void SetUp()
        {
            root = new GameObject("Camera perspective test");
            // Camera motion must not move the world-space presentation with it.
            camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            // Viewport assertions must not depend on the editor's current Game window layout.
            camera.aspect = 16f / 9f;
            camera.orthographic = true; camera.orthographicSize = 13;
            camera.nearClipPlane = .3f; camera.fieldOfView = 60;
            camera.transform.SetPositionAndRotation(new Vector3(0,24,-20), Quaternion.Euler(50,0,0));
            view = new FishingCameraView(camera, true);
        }
        [TearDown] public void TearDown() { view.Dispose(); Object.DestroyImmediate(root); }

        [Test] public void MouseDeviceEventsTurnTheCameraAndNoMotionStopsTurning()
        {
            var previous=Mouse.current;
            var mouse=InputSystem.AddDevice<Mouse>();
            var input=new FishingCameraInput();
            try
            {
                view.Toggle(Vector3.forward,Vector3.zero);
                InputSystem.QueueStateEvent(mouse,new UnityEngine.InputSystem.LowLevel.MouseState {position=new Vector2(100,100)});
                InputSystem.Update();
                view.Look(input.ReadLookDelta(true));
                InputSystem.QueueDeltaStateEvent(mouse.delta,new Vector2(120,80));
                InputSystem.Update();
                Assert.That(mouse.position.ReadValue(),Is.EqualTo(new Vector2(100,100)),"A captured mouse can stay centered while delivering motion.");
                Assert.That(mouse.delta.ReadValue(),Is.EqualTo(new Vector2(120,80)),"Deliver an actual relative mouse event.");
                view.Look(input.ReadLookDelta(true));
                Assert.That(camera.transform.forward.x,Is.GreaterThan(.1f),"Mouse movement must reach the camera input adapter.");
                Assert.That(camera.transform.eulerAngles.x,Is.LessThan(24),"Moving the mouse up must raise the view.");
                var rotated=camera.transform.rotation;
                InputSystem.Update();
                view.Look(input.ReadLookDelta(true));
                Assert.That(Quaternion.Angle(camera.transform.rotation,rotated),Is.LessThan(.01f),"No mouse movement must not replay the last delta.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                if(previous!=null && previous.added)previous.MakeCurrent();
            }
        }

        [Test] public void ResumingLookDiscardsMouseMotionMadeOverUiOrWhileUnfocused()
        {
            var previous=Mouse.current;var mouse=InputSystem.AddDevice<Mouse>();var input=new FishingCameraInput();
            try
            {
                view.Toggle(Vector3.forward,Vector3.zero);
                view.Look(input.ReadLookDelta(true));var initial=camera.transform.rotation;
                InputSystem.QueueDeltaStateEvent(mouse.delta,new Vector2(900,700));InputSystem.Update();
                view.Look(input.ReadLookDelta(false));
                InputSystem.QueueDeltaStateEvent(mouse.delta,new Vector2(400,200));InputSystem.Update();
                view.Look(input.ReadLookDelta(true));
                Assert.That(Quaternion.Angle(initial,camera.transform.rotation),Is.LessThan(.01f),"Resuming must discard motion from before capture.");
                InputSystem.QueueDeltaStateEvent(mouse.delta,new Vector2(20,0));InputSystem.Update();
                view.Look(input.ReadLookDelta(true));
                Assert.That(camera.transform.eulerAngles.y,Is.EqualTo(6).Within(.01f),"Only the new 20-pixel motion may turn the camera.");
                input.Reset();var beforeReset=camera.transform.rotation;
                InputSystem.QueueDeltaStateEvent(mouse.delta,new Vector2(50,50));InputSystem.Update();
                view.Look(input.ReadLookDelta(true));
                Assert.That(Quaternion.Angle(beforeReset,camera.transform.rotation),Is.LessThan(.01f),"A new session must discard the old device motion.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);if(previous!=null && previous.added)previous.MakeCurrent();
            }
        }

        [Test] public void FreeLookTurnsPastAFullCircleAndCanLookUpAndDownWithoutRecentering()
        {
            var feet=new Vector3(0,.465f,0);view.Toggle(Vector3.forward,feet);
            view.Look(new Vector2(270,1000));
            Assert.That(camera.transform.forward.y,Is.GreaterThan(.99f),"Looking up must not stop at the horizon.");
            Assert.That(Vector2.Distance(view.Movement(Vector2.up),Vector2.right),Is.LessThan(.001f));
            Assert.That(camera.transform.position.y,Is.GreaterThan(feet.y+.1f),"Upward orbit must stay above the platform.");
            var rotation=camera.transform.rotation;
            view.Follow(feet+Vector3.right);
            Assert.That(Quaternion.Angle(camera.transform.rotation,rotation),Is.LessThan(.01f),"Following a moving character must not recenter the view.");
            view.Look(new Vector2(0,-2000));
            Assert.That(camera.transform.forward.y,Is.LessThan(-.99f),"The player must be able to look almost straight down.");
            Assert.That(Vector2.Distance(view.Movement(Vector2.up),Vector2.right),Is.LessThan(.001f));
        }

        [Test] public void ToggleRestoresOverheadZoomAndLocksCurrentPlayer()
        {
            view.Zoom(-1);
            float zoom=camera.orthographicSize;
            var stageRotation=camera.transform.rotation;
            view.Toggle(Vector3.forward, new Vector3(3,.2f,4));
            Assert.That(view.ThirdPerson, Is.True);
            Assert.That(camera.orthographic, Is.False);
            Assert.That(camera.fieldOfView, Is.EqualTo(75));
            Assert.That(camera.nearClipPlane, Is.LessThan(.1f));
            view.Follow(new Vector3(5,.2f,6));
            var offset=camera.transform.position-new Vector3(5,.2f,6);
            Assert.That(offset.x,Is.EqualTo(.45f).Within(.001f));
            Assert.That(offset.y,Is.InRange(3.2f,3.4f));
            Assert.That(offset.z,Is.InRange(-2.9f,-2.7f));
            view.Toggle(Vector3.forward, new Vector3(2,.2f,2));
            Assert.That(camera.orthographic, Is.True);
            Assert.That(camera.orthographicSize, Is.EqualTo(zoom));
            Assert.That(camera.fieldOfView, Is.EqualTo(60));
            Assert.That(camera.nearClipPlane, Is.EqualTo(.1f));
            var centred=camera.WorldToViewportPoint(new Vector3(2,.2f,2));
            Assert.That(Vector2.Distance(new Vector2(centred.x,centred.y),new Vector2(.5f,.5f)),Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(camera.transform.rotation,stageRotation),Is.LessThan(.001f));
        }

        [TestCase(16f/9)] [TestCase(4f/3)] [TestCase(9f/16)]
        public void StageFramesPlatformAndNearbyFishingWaterAtDifferentAspectRatios(float aspect)
        {
            camera.aspect=aspect;view.Follow(new Vector3(0,.465f,0));
            foreach(float x in new[]{-6f,6f})foreach(float z in new[]{-6f,6f})
            {
                // Keep the six-metre platform plus three metres of near water on every side,
                // with an inset so fish do not sit against the screen edge at the closer zoom.
                var point=camera.WorldToViewportPoint(new Vector3(x,.12f,z));
                Assert.That(point.x,Is.InRange(.05f,.95f));
                Assert.That(point.y,Is.InRange(.05f,.95f));
                Assert.That(point.z,Is.GreaterThan(camera.nearClipPlane));
            }
            var rotation=camera.transform.rotation;
            foreach(float x in new[]{-2.8f,2.8f})foreach(float z in new[]{-2.8f,2.8f})
            {
                var feet=new Vector3(x,.465f,z);view.Follow(feet);
                var point=camera.WorldToViewportPoint(feet);
                Assert.That(Vector2.Distance(new Vector2(point.x,point.y),new Vector2(.5f,.5f)),Is.LessThan(.001f));
            }
            view.Look(new Vector2(500,500));
            Assert.That(Quaternion.Angle(rotation,camera.transform.rotation),Is.LessThan(.001f));
        }

        [Test] public void OverheadLocksEveryRenderedStepIncludingSwimmingAndLongTravel()
        {
            var rotation=camera.transform.rotation;float zoom=camera.orthographicSize;
            foreach(var feet in new[]{new Vector3(.03f,.465f,.04f),new Vector3(2,.465f,-1),
                new Vector3(18,SwimmingAppearance.FeetInWater,18),new Vector3(-4,.465f,6),Vector3.zero})
            {
                view.Follow(feet);
                var point=camera.WorldToViewportPoint(feet);
                Assert.That(Vector2.Distance(new Vector2(point.x,point.y),new Vector2(.5f,.5f)),Is.LessThan(.001f),
                    "Follow every rendered position immediately, without a dead zone or extra camera lag.");
                Assert.That(Quaternion.Angle(rotation,camera.transform.rotation),Is.LessThan(.001f));
                Assert.That(camera.orthographicSize,Is.EqualTo(zoom));
            }
        }

        [Test] public void FollowingPlayerKeepsConstructionRayAlignedWithWorldTiles()
        {
            var tile=new Vector3(4,.45f,2);
            foreach(var feet in new[]{Vector3.zero,new Vector3(2,.465f,1),new Vector3(6,.465f,-2)})
            {
                view.Follow(feet);
                var pointer=camera.WorldToScreenPoint(tile);var ray=camera.ScreenPointToRay(pointer);
                Assert.That(new Plane(Vector3.up,tile).Raycast(ray,out float hit),Is.True);
                Assert.That(Vector3.Distance(ray.GetPoint(hit),tile),Is.LessThan(.001f));
            }
        }

        [Test] public void StageZoomKeepsAimAtCursorAndResetRestoresInitialFraming()
        {
            var initial=camera.transform.position;float size=camera.orthographicSize;
            foreach(float scroll in new[]{1000f,-1000f})
            {
                view.Zoom(scroll);
                Assert.That(camera.orthographicSize,Is.InRange(4.8f,18));
                var target=new Vector3(3,.75f,4);var pointer=camera.WorldToScreenPoint(target);
                var aim=view.Aim(camera.ScreenPointToRay(pointer),Vector3.zero,4);
                Assert.That(Vector3.Distance(target,aim),Is.LessThan(.001f));
            }
            view.Follow(Vector3.one*20);
            view.Reset();
            Assert.That(camera.orthographicSize,Is.EqualTo(size));
            Assert.That(Vector3.Distance(initial,camera.transform.position),Is.LessThan(.001f));
        }

        [Test] public void ThirdPersonMovementUsesYawWithoutDiagonalOrPitchSpeedBoost()
        {
            Assert.That(view.Movement(Vector2.up),Is.EqualTo(Vector2.up));
            view.Toggle(Vector3.right, Vector3.zero);
            Assert.That(Vector2.Distance(view.Movement(Vector2.up),Vector2.right),Is.LessThan(.001f));
            Assert.That(Vector2.Distance(view.Movement(Vector2.right),Vector2.down),Is.LessThan(.001f));
            view.Look(new Vector2(0,1000));
            Assert.That(view.Movement(Vector2.one).magnitude,Is.EqualTo(1).Within(.001f));
            Assert.That(Vector2.Distance(view.Movement(Vector2.up),Vector2.right),Is.LessThan(.001f));
            Assert.That(Vector3.Angle(Vector3.up,camera.transform.forward),Is.GreaterThan(.5f),"Stop short of the pole so the view cannot flip over.");
        }

        [Test] public void ReturningFromOverheadKeepsThePreviousOrbitAndZoom()
        {
            var feet=new Vector3(3,.465f,4);view.Toggle(Vector3.forward,feet);
            view.Look(new Vector2(31,-8));view.Zoom(-2);
            var position=camera.transform.position;var rotation=camera.transform.rotation;
            view.Toggle(Vector3.forward,feet);
            view.Toggle(Vector3.left,feet);
            Assert.That(Quaternion.Angle(camera.transform.rotation,rotation),Is.LessThan(.01f),"Returning to third person must not discard the chosen view angle.");
            Assert.That(Vector3.Distance(camera.transform.position,position),Is.LessThan(.001f),"Returning to third person must retain its distance and shoulder framing.");
        }

        [Test] public void ResetStartsTheNextRoundWithTheConfiguredOrbitAndZoom()
        {
            var feet=new Vector3(3,.465f,4);view.Toggle(Vector3.forward,feet);
            var initialPosition=camera.transform.position;var initialRotation=camera.transform.rotation;
            view.Look(new Vector2(31,-8));view.Zoom(-2);view.Reset();
            view.Toggle(Vector3.forward,feet);
            Assert.That(Quaternion.Angle(camera.transform.rotation,initialRotation),Is.LessThan(.01f));
            Assert.That(Vector3.Distance(camera.transform.position,initialPosition),Is.LessThan(.001f),"A new round must not inherit the old round's zoom distance.");
        }

        [Test] public void HorizonAndUpwardAimUseFreshForwardRangeInsteadOfPreviousTarget()
        {
            view.Toggle(Vector3.forward, Vector3.zero);
            var feet = new Vector3(3,.2f,4);
            var horizontal = view.Aim(new Ray(feet+Vector3.up,Vector3.right),feet,12);
            Assert.That(horizontal.x,Is.EqualTo(15).Within(.001f));
            Assert.That(horizontal.z,Is.EqualTo(4).Within(.001f));
            var upward = view.Aim(new Ray(feet+Vector3.up,new Vector3(0,1,-1)),feet,12);
            Assert.That(upward.x,Is.EqualTo(3).Within(.001f));
            Assert.That(upward.z,Is.EqualTo(-8).Within(.001f));
            var downward = view.Aim(new Ray(new Vector3(3,2,4),new Vector3(0,-1,1)),feet,12);
            Assert.That(downward.z,Is.GreaterThan(4).And.LessThan(7));
        }

        [Test] public void ResetRestoresProjectionAndMouseAfterThirdPerson()
        {
            if(Application.isBatchMode)Assert.Ignore("OS cursor capture requires an interactive Game window; batchmode does not lock the cursor.");
            view.Toggle(Vector3.left,Vector3.zero);
            Assert.That(view.SetLookInputActive(true),Is.True);
            Assert.That(Cursor.lockState,Is.EqualTo(CursorLockMode.Locked));
            Assert.That(Cursor.visible,Is.False);
            Assert.That(view.SetLookInputActive(true),Is.False,"Continuous play must not reapply cursor capture each frame.");
            view.SetLookInputActive(false);
            Assert.That(Cursor.lockState,Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible,Is.True);
            Assert.That(view.LookInputActive,Is.False);
            Assert.That(view.SetLookInputActive(true),Is.True,"Closing a UI or regaining focus must report a fresh capture.");
            view.Reset();
            Assert.That(view.ThirdPerson,Is.False);
            Assert.That(camera.orthographic,Is.True);
            Assert.That(Cursor.lockState,Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible,Is.True);
        }

        [TestCase(.1f)] [TestCase(0f)] [TestCase(-.1f)]
        public void AimAcrossHorizonKeepsHeadingAndStaysWithinRodRange(float pitch)
        {
            var feet=new Vector3(0,.465f,0);view.Toggle(Vector3.forward,feet);
            var ray=new Ray(feet+Vector3.up,Quaternion.Euler(pitch,20,0)*Vector3.forward);
            var target=view.Aim(ray,feet,12);
            var offset=new Vector3(Mathf.Clamp(target.x,-90,90)-feet.x,0,Mathf.Clamp(target.z,-90,90)-feet.z);
            Assert.That(Vector3.Distance(offset.normalized,Quaternion.Euler(0,20,0)*Vector3.forward),Is.LessThan(.0001f));
            Assert.That(offset.magnitude,Is.EqualTo(12).Within(.001f));
        }

        [Test] public void ThirdPersonShowsLocalBodyBesideReticleAndPreservesEquipmentAndState()
        {
            var map=IslandMap.CreatePlatform();var game=new HookGame();var simulation=game.Create();
            var players=new[]{new Participant(0,Guid.NewGuid()),new Participant(1,Guid.NewGuid())};
            simulation.Initialize(new SimulationInitialization(new SessionDescriptor(Guid.NewGuid(),1,30,players),new ByteString(map.Encode())));
            var state=simulation.Capture();var before=game.Encode(state);
            using(var presentation=new FishingPresentation(root.transform,Shader.Find("Hook Island/Fishing Line"),map))
            {
                presentation.Present(state,state,0,Vector3.forward,false);
                view.Toggle(Vector3.forward,presentation.PlayerPosition(0));
                var local=root.transform.Find("Fishing visuals/Fisher 1/Body");
                var friend=root.transform.Find("Fishing visuals/Fisher 2/Body");
                Assert.That(local.Find("Fisher Body").GetComponent<SkinnedMeshRenderer>().forceRenderingOff,Is.False);
                Assert.That(local.Find("Fisher Body").gameObject.activeSelf,Is.True);
                Assert.That(local.Find("Equipped fishing rod/Rod Shaft").GetComponent<Renderer>().forceRenderingOff,Is.False);
                Assert.That(local.Find("Backpack").gameObject.activeSelf,Is.True);
                Assert.That(friend.GetComponentsInChildren<Renderer>(true).All(r=>!r.forceRenderingOff),Is.True);
                presentation.Present(state,state,0,Vector3.forward,false);
                var head=local.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Head");
                var face=camera.WorldToViewportPoint(head.position+Vector3.up*.1f);
                Assert.That(face.z,Is.GreaterThan(0));
                Assert.That(face.x,Is.InRange(.05f,.48f));
                Assert.That(face.y,Is.InRange(.05f,.55f));
                Assert.That(local.GetComponentsInChildren<Renderer>(true).All(r=>!r.forceRenderingOff),Is.True);
                Assert.That(before.SequenceEqual(game.Encode(state)),Is.True);
            }
        }

        [TestCase(.465f)] [TestCase(SwimmingAppearance.FeetInWater)]
        public void ShoulderCameraAimsThroughReticleAndKeepsWaterTargetsInFront(float feetHeight)
        {
            var feet=new Vector3(5,feetHeight,4);view.Toggle(Vector3.forward,feet);
            var target=view.Aim(camera.ViewportPointToRay(new Vector3(.5f,.5f)),feet,12);
            var screen=camera.WorldToViewportPoint(target);
            Assert.That(Vector2.Distance(new Vector2(screen.x,screen.y),new Vector2(.5f,.5f)),Is.LessThan(.001f));
            Assert.That(target.z,Is.GreaterThan(feet.z));
            view.Look(new Vector2(0,-1000));
            view.Zoom(1000);
            target=view.Aim(camera.ViewportPointToRay(new Vector3(.5f,.5f)),feet,12);
            Assert.That(target.z,Is.GreaterThan(feet.z),"Looking down must not cast behind the character.");
            view.Look(new Vector2(0,1000));
            Assert.That(camera.transform.position.y,Is.GreaterThan(.2f),"Orbit must stay above the sea while swimming.");
            Assert.That(Vector3.Distance(camera.transform.position,feet),Is.GreaterThan(.5f),"Keep the shoulder camera outside the character when the ground shortens the orbit.");
            view.Zoom(-1000);
            Assert.That(Vector3.Distance(camera.transform.position,feet),Is.LessThan(5.3f));
        }

        [Test] public void TurningUpdatesOrbitBeforeSamplingAimAndDoesNotMoveThePivot()
        {
            var feet=new Vector3(3,.465f,4);view.Toggle(Vector3.forward,feet);
            view.Look(new Vector2(30,0));
            Assert.That(camera.transform.position.x,Is.LessThan(feet.x-2.7f));
            Assert.That(camera.transform.position.z,Is.EqualTo(feet.z-.45f).Within(.001f));
            var target=view.Aim(camera.ViewportPointToRay(new Vector3(.5f,.5f)),feet,12);
            Assert.That(target.x,Is.GreaterThan(feet.x));
            var before=camera.transform.position;view.Follow(feet);
            Assert.That(Vector3.Distance(camera.transform.position,before),Is.LessThan(.001f));
        }

        [TestCase(16f/9,22f)] [TestCase(16f/9,40f)] [TestCase(4f/3,22f)] [TestCase(21f/9,40f)]
        public void FirearmFramesTheBodyBelowLeftOfTheCentreAimRay(float aspect,float angle)
        {
            camera.aspect=aspect;var feet=new Vector3(3,.465f,4);
            view.Toggle(Vector3.forward,feet);view.Look(new Vector2(15,(40-angle)/2.55f));
            view.UpdateFirearmFraming(true,1);
            var torso=camera.WorldToViewportPoint(feet+Vector3.up*.6f);
            var head=camera.WorldToViewportPoint(feet+Vector3.up*1.03f);
            Assert.That(torso.x,Is.EqualTo(.32f).Within(.001f));
            Assert.That(torso.y,Is.EqualTo(.30f).Within(.001f));
            Assert.That(head.x,Is.LessThan(.4f));Assert.That(head.y,Is.LessThan(.46f));
            var target=view.Aim(camera.ViewportPointToRay(new Vector3(.5f,.5f)),feet,12);
            var projected=camera.WorldToViewportPoint(target);
            Assert.That(Vector2.Distance(new Vector2(projected.x,projected.y),new Vector2(.5f,.5f)),Is.LessThan(.001f),
                "The central reticle must still sample the actual water aiming point.");
            foreach(float scroll in new[]{1000f,-1000f})
            {
                view.Zoom(scroll);view.Follow(feet+Vector3.right*2);
                torso=camera.WorldToViewportPoint(feet+Vector3.right*2+Vector3.up*.6f);
                Assert.That(torso.x,Is.EqualTo(.32f).Within(.001f));
                Assert.That(torso.y,Is.EqualTo(.30f).Within(.001f));
            }
        }

        [Test] public void FirearmFramingBlendsWithoutTurningAndRestoresTheFishingView()
        {
            var feet=new Vector3(0,.465f,0);view.Toggle(Vector3.forward,feet);
            var before=camera.transform.position;var rotation=camera.transform.rotation;
            view.UpdateFirearmFraming(true,.11f);var middle=camera.transform.position;
            view.UpdateFirearmFraming(true,.11f);var after=camera.transform.position;
            Assert.That(Vector3.Distance(before,middle),Is.GreaterThan(.1f));
            Assert.That(Vector3.Distance(before,middle),Is.LessThan(Vector3.Distance(before,after)));
            Assert.That(Quaternion.Angle(rotation,camera.transform.rotation),Is.LessThan(.001f));
            Assert.That(camera.fieldOfView,Is.EqualTo(65));
            view.Toggle(Vector3.forward,feet);var overhead=camera.transform.position;
            view.UpdateFirearmFraming(false,1);
            Assert.That(camera.transform.position,Is.EqualTo(overhead),"A gun selection must not recompose the overhead view.");
            view.Toggle(Vector3.forward,feet);
            Assert.That(Vector3.Distance(before,camera.transform.position),Is.LessThan(.001f));
            Assert.That(camera.fieldOfView,Is.EqualTo(75));
            view.UpdateFirearmFraming(true,1);view.Reset();view.Toggle(Vector3.forward,feet);
            Assert.That(Vector3.Distance(before,camera.transform.position),Is.LessThan(.001f),"New rounds must reset gun framing.");
        }

        [Test] public void TwoUdpClientsWithDifferentPerspectivesMoveCastAndConfirmTheSameState()
        {
            var map=IslandMap.CreatePlatform();var clock=System.Diagnostics.Stopwatch.StartNew();
            var otherObject=new GameObject("Overhead peer camera");var otherCamera=otherObject.AddComponent<Camera>();otherCamera.orthographic=true;
            var codec=new HookGame();var firstStates=new Dictionary<ulong,byte[]>();var secondStates=new Dictionary<ulong,byte[]>();
            var start0=new Vector3(map.Spawns[0].X/1000f,0,map.Spawns[0].Z/1000f);
            var start1=new Vector3(map.Spawns[1].X/1000f,0,map.Spawns[1].Z/1000f);
            try
            {
                using(var overhead=new FishingCameraView(otherCamera,true))
                using(var host=new IslandHost(map,0,2))
                using(var first=new IslandClient(map,IPAddress.Loopback,host.Port,0))
                using(var second=new IslandClient(map,IPAddress.Loopback,host.Port,ushort.MaxValue))
                {
                    // Face away from the other spawn so this input test does not tow the peer.
                    view.Toggle(Vector3.left,start0);
                    Func<ulong,HookInput> firstInput=tick=>CameraInput(view,tick,start0,Vector3.left);
                    Func<ulong,HookInput> secondInput=tick=>CameraInput(overhead,tick,start1,Vector3.forward);
                    ulong common=0;
                    while(common<60 && clock.Elapsed.TotalSeconds<8)
                    {
                        host.Poll(clock.Elapsed);first.Poll(clock.Elapsed,firstInput);second.Poll(clock.Elapsed,secondInput);
                        Assert.That(host.Failure??first.Failure??second.Failure,Is.Null);
                        if(first.Session?.LocalParticipant!=null)firstStates[first.Session.ConfirmedTick]=codec.Encode(first.Session.InspectState());
                        if(second.Session?.LocalParticipant!=null)secondStates[second.Session.ConfirmedTick]=codec.Encode(second.Session.InspectState());
                        foreach(var tick in firstStates.Keys)
                            if(secondStates.TryGetValue(tick,out var bytes))
                            {Assert.That(firstStates[tick],Is.EqualTo(bytes),"Peers disagreed at tick "+tick);common=Math.Max(common,tick);}
                        Thread.Sleep(1);
                    }
                    Assert.That(common,Is.GreaterThanOrEqualTo(60),"Two real UDP clients did not advance.");
                    var state=codec.Decode(firstStates[common]);
                    Assert.That(state.Players[0].X,Is.LessThan(map.Spawns[0].X));
                    Assert.That(state.Players[0].Z,Is.EqualTo(map.Spawns[0].Z));
                    Assert.That(state.Players[1].X,Is.EqualTo(map.Spawns[1].X));
                    Assert.That(state.Players[1].Z,Is.GreaterThan(map.Spawns[1].Z));
                    Assert.That(state.Players.All(p=>p.Casts==1),Is.True);
                    Assert.That(view.ThirdPerson,Is.True);Assert.That(overhead.ThirdPerson,Is.False);
                    Assert.That(camera.orthographic,Is.False);Assert.That(otherCamera.orthographic,Is.True);
                }
            }
            finally {Object.DestroyImmediate(otherObject);}
        }
        private static HookInput CameraInput(FishingCameraView view,ulong tick,Vector3 feet,Vector3 forward)
        {
            var movement=view.Movement(tick<=6?Vector2.up:Vector2.zero);
            var aim=view.Aim(new Ray(feet+Vector3.up,forward),feet,4);
            return new HookInput((short)(movement.x*1000),(short)(movement.y*1000),Mathf.RoundToInt(aim.x*1000),Mathf.RoundToInt(aim.z*1000),tick==12);
        }
    }
}
