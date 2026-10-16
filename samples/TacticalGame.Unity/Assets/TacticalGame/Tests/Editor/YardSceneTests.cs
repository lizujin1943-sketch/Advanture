using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Advanture.TacticalGame.Tests
{
    /// <summary>检查实际导入场景，防止 FBX 换轴或比例差异使可见模型与碰撞错位。</summary>
    public sealed class YardSceneTests
    {
        [SetUp]
        public void LoadScene()
        {
            EditorSceneManager.OpenScene("Assets/TacticalGame/Scenes/Yard07.unity");
            Physics.SyncTransforms();
        }

        [TearDown]
        public void CloseScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }

        [Test]
        public void CollisionBoxesMatchVisibleWallsFloorAndCover()
        {
            foreach(string visualName in new[] {
                "Playable_Ground", "Warehouse", "Boundary_W", "Boundary_E", "Boundary_S",
                "Spawn_A_Screen", "Spawn_B_Screen", "Container_North_Body", "Container_South_Body",
                "Cover_Center_High", "Cover_Center_Low", "Cover_North", "Cover_South" })
            {
                var renderer=Object.FindObjectsOfType<MeshRenderer>().Single(r=>r.name==visualName);
                var collider=Object.FindObjectsOfType<BoxCollider>().Single(c=>c.name=="COL_"+visualName);
                Assert.That(Vector3.Distance(renderer.bounds.center,collider.bounds.center),Is.LessThan(.005f),visualName+" center");
                Assert.That(Vector3.Distance(renderer.bounds.size,collider.bounds.size),Is.LessThan(.005f),visualName+" size");
            }
        }

        [Test]
        public void SpawnIsClearAndCameraIsAtStandingEyeHeight()
        {
            var player=Object.FindObjectOfType<FirstPersonMotor>();
            Assert.That(player,Is.Not.Null);
            var body=player.GetComponent<CharacterController>();
            var camera=player.GetComponentInChildren<Camera>();
            Assert.That(camera,Is.Not.Null);
            Assert.That(camera.transform.localPosition.y,Is.EqualTo(1.65f).Within(.001f));
            Assert.That(Physics.Raycast(player.transform.position+Vector3.up,Vector3.down,out var floor,2),Is.True);
            Assert.That(floor.collider.name,Is.EqualTo("COL_Playable_Ground"));
            var bottom=player.transform.position+Vector3.up*.37f;
            var top=player.transform.position+Vector3.up*1.43f;
            var overlaps=Physics.OverlapCapsule(bottom,top,.32f).Where(c=>c!=body).ToArray();
            Assert.That(overlaps,Is.Empty,"出生点不应嵌在墙或箱子内。");
        }

        [Test]
        public void ImportedContainerHasRealWorldScale()
        {
            var container=Object.FindObjectsOfType<MeshRenderer>().Single(r=>r.name=="Container_North_Body");
            Assert.That(container.bounds.size.x,Is.EqualTo(8).Within(.01f));
            Assert.That(container.bounds.size.y,Is.EqualTo(2.65f).Within(.01f));
            Assert.That(container.bounds.size.z,Is.EqualTo(2.8f).Within(.01f));
        }

        [Test]
        public void ActualSpawnWallBlocksSprint()
        {
            var motor=Object.FindObjectOfType<FirstPersonMotor>();
            var wall=Object.FindObjectsOfType<BoxCollider>().Single(c=>c.name=="COL_Spawn_A_Screen");
            Vector3 direction=Vector3.ProjectOnPlane(wall.bounds.center-motor.transform.position,Vector3.up).normalized;
            float targetYaw=Quaternion.LookRotation(direction).eulerAngles.y;
            float turn=Mathf.DeltaAngle(motor.transform.eulerAngles.y,targetYaw);
            motor.Step(Vector2.zero,new Vector2(turn/.12f,0),false,1f/60);
            Vector3 origin=motor.transform.position;
            for(int i=0;i<180;i++) motor.Step(Vector2.up,Vector2.zero,true,1f/60);
            float distance=Vector3.Dot(motor.transform.position-origin,direction);
            Assert.That(distance,Is.InRange(2.2f,2.6f));
            Assert.That(motor.transform.position.y,Is.InRange(-.05f,.1f));
        }
    }
}
