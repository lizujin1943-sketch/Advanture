using System.Linq;
using Advanture.Tactical.Simulation;
using Advanture.TacticalGame.NetworkView;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
            foreach(var spawn in map.Spawns)
            {
                var feet=new Vector3(spawn.X*.001f,2.05f,spawn.Z*.001f);
                Assert.That(Physics.Raycast(feet+Vector3.up,Vector3.down,out var hit,2),Is.True);
                Assert.That(hit.collider.name,Does.Contain("Plaza"));
                Assert.That(hit.point.y,Is.EqualTo(map.GroundHeight(spawn.X,spawn.Z)*.001f).Within(.015f));
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
}
