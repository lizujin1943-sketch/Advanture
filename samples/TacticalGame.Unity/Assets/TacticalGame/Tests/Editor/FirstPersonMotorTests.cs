using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Advanture.TacticalGame.Tests
{
    /// <summary>
    /// 使用真实 CharacterController 检查运动结果，不模拟碰撞器。
    /// 去掉输入归一化、水平投影、视角限位、重力或重生重置时，相应测试必须失败。
    /// </summary>
    public sealed class FirstPersonMotorTests
    {
        private GameObject player;
        private GameObject floor;
        private GameObject wall;
        private Transform view;
        private FirstPersonMotor motor;
        private Scene testScene;

        [SetUp]
        public void SetUp()
        {
            // 每个用例使用独立的 3D 物理场景：编辑器里可能已经打开 Yard07，
            // 如果共用默认物理世界，测试角色会撞到地图中央的箱子，影响速度和落地断言。
            // 只隔离本用例创建的物体，不关闭、禁用或修改用户当前打开的地图。
            // 预览场景在 EditMode 下可创建，并拥有独立的物理世界。
            testScene = EditorSceneManager.NewPreviewScene();
            Assert.That(testScene.GetPhysicsScene(), Is.Not.EqualTo(Physics.defaultPhysicsScene));
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(floor, testScene);
            floor.name = "TestFloor";
            floor.transform.position = new Vector3(0, -.5f, 0);
            floor.transform.localScale = new Vector3(100, 1, 100);
            player = new GameObject("TestPlayer");
            SceneManager.MoveGameObjectToScene(player, testScene);
            player.transform.position = new Vector3(0, .05f, 0);
            var capsule = player.AddComponent<CharacterController>();
            capsule.height = 1.8f;
            capsule.radius = .35f;
            capsule.center = new Vector3(0, .9f, 0);
            capsule.skinWidth = .035f;
            capsule.stepOffset = .25f;
            capsule.minMoveDistance = 0;
            view = new GameObject("View").transform;
            SceneManager.MoveGameObjectToScene(view.gameObject, testScene);
            view.SetParent(player.transform, false);
            view.localPosition = new Vector3(0, 1.65f, 0);
            motor = player.AddComponent<FirstPersonMotor>();
            motor.Configure(view);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            // 关闭测试场景时一并销毁本用例的地面、角色、相机节点和墙体。
            // 即使断言失败也会执行 TearDown，避免碰撞体泄漏到下一项测试。
            if (testScene.IsValid()) EditorSceneManager.ClosePreviewScene(testScene);
        }

        private void Walk(Vector2 input, bool sprint = false, int frames = 60)
        {
            for (int i = 0; i < frames; i++) motor.Step(input, Vector2.zero, sprint, 1f / 60);
        }

        [Test]
        public void ForwardInputMovesAtWalkingSpeed()
        {
            Walk(Vector2.up);
            Assert.That(player.transform.position.z, Is.EqualTo(4.5f).Within(.05f));
        }

        [Test]
        public void DiagonalDoesNotMoveFasterThanForward()
        {
            Walk(Vector2.one);
            var p = player.transform.position;
            Assert.That(new Vector2(p.x, p.z).magnitude, Is.EqualTo(4.5f).Within(.05f));
        }

        [Test]
        public void SprintMovesFasterThanWalking()
        {
            Walk(Vector2.up, true);
            Assert.That(player.transform.position.z, Is.EqualTo(7f).Within(.05f));
        }

        [Test]
        public void LookingRightChangesWalkingDirection()
        {
            motor.Step(Vector2.zero, new Vector2(750, 0), false, 1f / 60);
            Walk(Vector2.up);
            Assert.That(player.transform.position.x, Is.EqualTo(4.5f).Within(.05f));
            Assert.That(player.transform.position.z, Is.EqualTo(0).Within(.05f));
        }

        [Test]
        public void LookingUpIsClampedAndDoesNotMakePlayerFly()
        {
            motor.Step(Vector2.zero, new Vector2(0, 5000), false, 1f / 60);
            Assert.That(Vector3.Angle(view.forward, player.transform.forward), Is.EqualTo(85).Within(.05f));
            Assert.That(view.forward.y, Is.GreaterThan(.99f));
            Walk(Vector2.up);
            Assert.That(player.transform.position.z, Is.EqualTo(4.5f).Within(.05f));
            Assert.That(player.transform.position.y, Is.InRange(-.05f,.10f));
        }

        [Test]
        public void GravitySettlesOnFloorWithoutFallingThrough()
        {
            player.transform.position = new Vector3(0, 3, 0);
            Physics.SyncTransforms();
            Walk(Vector2.zero, frames: 180);
            Assert.That(player.transform.position.y, Is.InRange(-.05f,.10f));
        }

        [Test]
        public void CharacterStopsBeforeWall()
        {
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(wall, testScene);
            wall.transform.position = new Vector3(0, 1.5f, 2);
            wall.transform.localScale = new Vector3(10, 3, .5f);
            Physics.SyncTransforms();
            Walk(Vector2.up, true, 120);
            Assert.That(player.transform.position.z, Is.InRange(1.3f, 1.5f));
        }

        [Test]
        public void RespawnRestoresPositionAndView()
        {
            motor.Step(Vector2.zero, new Vector2(400, 200), false, 1f / 60);
            Walk(Vector2.up);
            motor.Respawn();
            Assert.That(Vector3.Distance(player.transform.position, new Vector3(0,.05f,0)), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(player.transform.rotation, Quaternion.identity), Is.LessThan(.01f));
            Assert.That(Quaternion.Angle(view.localRotation, Quaternion.identity), Is.LessThan(.01f));
        }

        [Test]
        public void SameInputHasSameDistanceAtDifferentFrameRates()
        {
            for (int i=0;i<30;i++) motor.Step(Vector2.up, Vector2.zero, false, 1f/30);
            float distanceAt30 = player.transform.position.z;
            motor.Respawn();
            for (int i=0;i<120;i++) motor.Step(Vector2.up, Vector2.zero, false, 1f/120);
            Assert.That(distanceAt30, Is.GreaterThan(4));
            Assert.That(player.transform.position.z, Is.EqualTo(distanceAt30).Within(.05f));
        }
    }
}
