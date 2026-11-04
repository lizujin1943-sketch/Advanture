using NUnit.Framework;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    /// <summary>验证取消后的按键归属和镜头还原，不向游戏状态注入投掷物。</summary>
    public sealed class GrenadePresentationTests
    {
        [Test] public void CancelRequiresAReleaseAndNewPress()
        {
            var input=new GrenadeAimInput();
            Assert.That(input.Begin(true,true,true),Is.False,"进房时已按住鼠标不能误投");
            input.Begin(true,false,false);
            Assert.That(input.Begin(true,true,true),Is.True);
            input.Interrupt(true);
            Assert.That(input.Release(true),Is.False,"取消后迟到的松手必须丢弃");
            Assert.That(input.Begin(true,true,true),Is.False);
            input.Begin(true,false,false);
            Assert.That(input.Begin(true,true,true),Is.True);
            Assert.That(input.Release(true),Is.True);
            Assert.That(input.Release(true),Is.False,"同一次松手只提交一次");
        }
        [TestCase(false)] [TestCase(true)]
        public void PullbackPreservesModeDirectionAndRestoresWithoutDrift(bool firstPerson)
        {
            var go=new GameObject("Grenade camera test");var camera=go.AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=13;
            camera.transform.SetPositionAndRotation(new Vector3(0,24,-20),Quaternion.Euler(50,0,0));
            var view=new FishingCameraView(camera,true);
            try
            {
                var feet=new Vector3(100,.465f,100); // 远离场景碰撞体，专测镜头数学。
                if(firstPerson)view.UpdateFirearmFraming(true,1);
                view.Follow(feet);var start=camera.transform.position;var rotation=camera.transform.rotation;float size=camera.orthographicSize;
                view.SetGrenadeEquipped(true);view.UpdateFirearmFraming(false,1);
                view.UpdateGrenadeAim(true,.2f);view.Follow(feet);
                Assert.That(view.FirstPerson,Is.EqualTo(firstPerson));
                Assert.That(Quaternion.Angle(rotation,camera.transform.rotation),Is.LessThan(.001f));
                Assert.That(Vector3.Distance(camera.transform.position,start-camera.transform.forward*2),Is.LessThan(.001f));
                if(!firstPerson)Assert.That(camera.orthographicSize,Is.EqualTo(size*1.2f).Within(.001f));
                for(int i=0;i<20;i++)view.Follow(feet);
                Assert.That(Vector3.Distance(camera.transform.position,start-camera.transform.forward*2),Is.LessThan(.001f),"多次 Follow 不能叠加后移");
                view.Look(new Vector2(200,200));view.Zoom(8);view.Toggle(Vector3.forward,feet);
                Assert.That(Quaternion.Angle(rotation,camera.transform.rotation),Is.LessThan(.001f));
                view.UpdateGrenadeAim(false,.2f);view.Follow(feet);
                Assert.That(Vector3.Distance(camera.transform.position,start),Is.LessThan(.001f));
                Assert.That(camera.orthographicSize,Is.EqualTo(size).Within(.001f));
            }
            finally{view.Dispose();Object.DestroyImmediate(go);}
        }
    }
}
