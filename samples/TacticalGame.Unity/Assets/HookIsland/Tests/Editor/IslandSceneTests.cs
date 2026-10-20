using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class IslandSceneTests
    {
        private IslandMap Map=>IslandMap.Decode(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/IslandMap.bytes").bytes);
        [Test] public void SavedSceneHasConfiguredPlayableEntry()
        {
            EditorSceneManager.OpenScene("Assets/HookIsland/Scenes/HookIsland.unity");
            var game=Object.FindObjectOfType<IslandFishingController>();Assert.That(game,Is.Not.Null);
            var serialized=new SerializedObject(game);
            foreach(string field in new[]{"mapData","viewCamera","lineShader"})
                Assert.That(serialized.FindProperty(field).objectReferenceValue,Is.Not.Null,field);
            Assert.That(game.GetComponent<FishingOverlay>(),Is.Not.Null);
            Assert.That(Object.FindObjectsOfType<Camera>().Length,Is.EqualTo(1));
        }
        [Test] public void FourSpawnsWalkableAndAllFishOffshore()
        {
            var map=Map;Assert.That(map.Spawns.Count,Is.EqualTo(4));Assert.That(map.FishSpots.Count,Is.EqualTo(24));
            foreach(var p in map.Spawns)Assert.That(map.CanStand(p.X,p.Z),Is.True,"Invalid spawn");
            foreach(var f in map.FishSpots)Assert.That(map.OnLand(f.X,f.Z),Is.False,"Fish spawned on island/pier");
        }
        [Test] public void PierJoinsIslandWithoutLogicalOrVisualGap()
        {
            EditorSceneManager.OpenScene("Assets/HookIsland/Scenes/HookIsland.unity");Physics.SyncTransforms();
            var map=Map;var ground=GameObject.Find("Island_Ground").GetComponent<Collider>();
            var pier=GameObject.Find("Pier Walk Surface").GetComponent<Collider>();
            var boards=GameObject.Find("Island_Pier").GetComponent<Collider>();
            float previous=-1;
            for(int i=0;i<=40;i++)
            {
                // A 0.66 m footprint needs clearance from the 3.2 m pier tip.
                float distance=-2+i*.115f,x=-7.2666f-.62f*distance,z=-7.58693f-.7846018f*distance;
                Assert.That(map.CanStand(Mathf.RoundToInt(x*1000),Mathf.RoundToInt(z*1000)),Is.True,"Blocked pier ramp");
                var ray=new Ray(new Vector3(x,5,z),Vector3.down);
                bool land=ground.Raycast(ray,out var a,8),deck=pier.Raycast(ray,out var b,8);
                Assert.That(land||deck,Is.True,"Missing walk surface");
                float y=deck?b.point.y:a.point.y;
                if(deck && boards.Raycast(ray,out var visible,8))
                    Assert.That(Mathf.Abs(b.point.y-visible.point.y),Is.LessThan(.025f),"Walk surface does not match visible pier");
                if(previous>=0)Assert.That(Mathf.Abs(y-previous),Is.LessThan(.22f),"Pier height discontinuity");
                previous=y;
            }
            Assert.That(map.CanStand(-9360,-10236),Is.False,"Player can leave the end of the pier");
        }
    }
}
