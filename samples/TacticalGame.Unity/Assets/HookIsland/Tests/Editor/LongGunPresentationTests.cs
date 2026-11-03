using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class LongGunPresentationTests
    {
        [Test] public void AutomaticTriggerSurvivesCatchupTicksAndStopsOnReleaseOrInterruption()
        {
            var trigger=new FirearmTriggerInput();trigger.Capture(10,true,true,true,true);
            for(int i=0;i<4;i++)Assert.That(trigger.Consume(),Is.True,"Catch-up ticks must retain the held automatic trigger.");
            trigger.Capture(10,true,true,false,false);Assert.That(trigger.Consume(),Is.False);
            trigger.Capture(10,true,true,true,true);trigger.Interrupt();Assert.That(trigger.Consume(),Is.False);
            trigger.Capture(10,true,true,false,true);Assert.That(trigger.Consume(),Is.False,"Closing a menu with the button still held must not resume firing.");
            trigger.Capture(10,true,true,false,false);trigger.Capture(10,true,true,true,true);Assert.That(trigger.Consume(),Is.True);
            trigger.Capture(11,true,true,false,true);Assert.That(trigger.Consume(),Is.False,"A new weapon cannot inherit the previous trigger hold.");
        }
        [Test] public void SniperAndPistolRequireANewPressAndShortClicksAreRetained()
        {
            foreach(int type in new[]{0,2,3})
            {
                var trigger=new FirearmTriggerInput();trigger.Capture(type+1,FirearmCatalog.Get(type).Automatic,true,true,true);
                trigger.Capture(type+1,false,true,false,false);Assert.That(trigger.Consume(),Is.True);
                Assert.That(trigger.Consume(),Is.False);trigger.Capture(type+1,false,true,false,true);Assert.That(trigger.Consume(),Is.False);
            }
        }
        [TestCase(2,4)] [TestCase(3,6)]
        public void ScopeMagnifiesWorldReducesSensitivityAndPreservesCentralAim(int type,int power)
        {
            var root=new GameObject("Scope test");var camera=root.AddComponent<Camera>();
            camera.aspect=16f/9;camera.fieldOfView=75;
            using(var view=new FishingCameraView(camera,false))
            {
                view.UpdateFirearmFraming(true,1);view.Follow(new Vector3(2,0,3));
                var before=camera.ViewportPointToRay(new Vector3(.5f,.5f));
                view.UpdateScope(FirearmCatalog.Get(type),true,true,1);
                Assert.That(view.Scoped,Is.True);Assert.That(view.ScopeMagnification,Is.EqualTo(power));
                Assert.That(camera.fieldOfView,Is.EqualTo(FishingCameraView.ScopedFieldOfView(65,power)).Within(.001f));
                Assert.That(view.LookSensitivityScale,Is.EqualTo(1f/power).Within(.001f));
                Assert.That(Vector3.Dot(before.direction,camera.ViewportPointToRay(new Vector3(.5f,.5f)).direction),Is.GreaterThan(.99999f));
                float yaw=camera.transform.eulerAngles.y;view.Look(new Vector2(10,0));
                Assert.That(Mathf.DeltaAngle(yaw,camera.transform.eulerAngles.y),Is.EqualTo(30f/power).Within(.01f));
                view.UpdateScope(FirearmCatalog.Get(type),false,true,1);Assert.That(view.Scoped,Is.False);Assert.That(camera.fieldOfView,Is.EqualTo(65).Within(.001f));
            }
            Object.DestroyImmediate(root);
        }
        [Test] public void ScopeClearsOnSwitchReloadFocusLossAndReset()
        {
            var root=new GameObject("Scope cancellation");var camera=root.AddComponent<Camera>();
            using(var view=new FishingCameraView(camera,false))
            {
                view.UpdateFirearmFraming(true,1);view.UpdateScope(FirearmCatalog.Get(2),true,true,1);
                view.UpdateScope(FirearmCatalog.Get(1),true,true,0);Assert.That(view.Scoped,Is.False);
                view.UpdateScope(FirearmCatalog.Get(2),true,true,1);view.UpdateScope(FirearmCatalog.Get(2),true,false,0);
                Assert.That(view.Scoped,Is.False);Assert.That(camera.fieldOfView,Is.EqualTo(65).Within(.001f));
                view.UpdateScope(FirearmCatalog.Get(3),true,true,1);view.SetLookInputActive(false);Assert.That(view.Scoped,Is.False);
                view.UpdateScope(FirearmCatalog.Get(2),true,true,1);view.UpdateFirearmFraming(false,1);Assert.That(view.Scoped,Is.False);
                view.Reset();Assert.That(view.ScopeMagnification,Is.EqualTo(1));
            }
            Object.DestroyImmediate(root);
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void LongGunModelsHaveDistinctShapesMovingPartsAndBoundedRenderers(int type)
        {
            var root=new GameObject("Long gun test");
            try
            {
                using(var art=new FirearmArt(Shader.Find("Sprites/Default")))
                {
                    var selection=new FirearmModelSelection(root.transform,art);var gun=FirearmCatalog.Get(type);var model=selection.Select(gun);
                    Assert.That(model.Root.name,Is.EqualTo(gun.Name));Assert.That(model.Muzzle.localPosition.z,Is.GreaterThan(.6f));
                    Assert.That(model.Slide,Is.Not.Null);Assert.That(model.Magazine,Is.Not.Null);
                    Assert.That(model.Root.GetComponentsInChildren<Renderer>().Length,Is.LessThanOrEqualTo(12));
                    Assert.That(model.Root.GetComponentsInChildren<Collider>(true),Is.Empty);
                    var pistol=selection.Select(FirearmCatalog.Get(0));Assert.That(model.Root.gameObject.activeSelf,Is.False);
                    Assert.That(selection.Select(gun),Is.SameAs(model));Assert.That(pistol.Root.gameObject.activeSelf,Is.False);
                    selection.Select(null);Assert.That(model.Root.gameObject.activeSelf,Is.False);
                }
            }
            finally{Object.DestroyImmediate(root);}
        }
        [Test] public void ScopedViewHidesNearGunAndSwitchingRestoresItsOwnModel()
        {
            var root=new GameObject("Scoped first person test");var camera=new GameObject("Camera").AddComponent<Camera>();
            var hand=GameObject.CreatePrimitive(PrimitiveType.Cube);var material=new Material(Shader.Find("Standard"));
            try
            {
                using(var art=new FirearmArt(Shader.Find("Sprites/Default")))
                {
                    var view=new FirstPersonFirearmView(root.transform,art,hand.GetComponent<MeshFilter>().sharedMesh,material);
                    var p=new Fisher{Firearm=new FirearmState{WeaponId=5}};var gun=FirearmCatalog.Get(2);
                    view.Present(camera,p,p,gun,Vector3.forward*40,material,false,true);
                    Assert.That(view.Visible,Is.True);Assert.That(root.GetComponentsInChildren<Renderer>(),Is.Empty);
                    Assert.That(view.Muzzle,Is.Not.Null);
                    view.Present(camera,p,p,gun,Vector3.forward*40,material,false);
                    Assert.That(root.GetComponentsInChildren<Renderer>().Length,Is.GreaterThan(0));
                    p.Firearm.WeaponId=6;view.Present(camera,p,p,FirearmCatalog.Get(1),Vector3.forward*24,material,false);
                    Assert.That(root.transform.Find("First person firearm/AWM").gameObject.activeSelf,Is.False);
                    Assert.That(root.transform.Find("First person firearm/AK47").gameObject.activeSelf,Is.True);
                }
            }
            finally{Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(hand);Object.DestroyImmediate(material);}
        }
    }
}
