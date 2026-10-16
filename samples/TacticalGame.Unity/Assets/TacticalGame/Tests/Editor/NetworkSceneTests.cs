using System.Linq;
using Advanture.Tactical.Simulation;
using Advanture.TacticalGame.NetworkView;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Advanture.TacticalGame.Tests
{
    public sealed class NetworkSceneTests
    {
        private const string ScenePath="Assets/TacticalGame/Scenes/Yard07Network.unity";
        [Test] public void SceneUsesNetworkControllerWithoutLocalPhysicsMotor()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null, "先生成独立联机场景");
            EditorSceneManager.OpenScene(ScenePath);
            Assert.That(Object.FindObjectsOfType<NetworkYardController>().Length,Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<Camera>().Length,Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<FirstPersonMotor>().Length,Is.Zero);
            Assert.That(Object.FindObjectsOfType<CharacterController>().Length,Is.Zero);
            Assert.That(PlayerSettings.runInBackground,Is.True);
        }
        [Test] public void ExportedMapMatchesAllSceneObstaclesAndClearSpawns()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath),Is.Not.Null);
            EditorSceneManager.OpenScene(ScenePath);Physics.SyncTransforms();
            var asset=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/TacticalGame/Network/Yard07Map.bytes");Assert.That(asset,Is.Not.Null);
            var map=TacticalMap.Decode(asset.bytes);var boxes=Object.FindObjectsOfType<BoxCollider>().Where(b=>b.name!="COL_Playable_Ground").OrderBy(b=>b.name,System.StringComparer.Ordinal).ToArray();
            Assert.That(map.Obstacles.Count,Is.EqualTo(16));Assert.That(boxes.Length,Is.EqualTo(map.Obstacles.Count));
            for(int i=0;i<boxes.Length;i++)
            {
                var b=boxes[i].bounds;var r=map.Obstacles[i];
                Assert.That(r.MinY,Is.EqualTo(Mathf.RoundToInt(b.min.y*1000)));Assert.That(r.MaxY,Is.EqualTo(Mathf.RoundToInt(b.max.y*1000)));
                Assert.That(r.MinX,Is.EqualTo(Mathf.RoundToInt(b.min.x*1000)));Assert.That(r.MaxX,Is.EqualTo(Mathf.RoundToInt(b.max.x*1000)));
                Assert.That(r.MinZ,Is.EqualTo(Mathf.RoundToInt(b.min.z*1000)));Assert.That(r.MaxZ,Is.EqualTo(Mathf.RoundToInt(b.max.z*1000)));
            }
            foreach(var spawn in map.Spawns)Assert.That(map.CanStand(spawn.X,spawn.Z),Is.True);
        }
    }
}
