using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class PlatformSceneTests
    {
        private const string Scene="Assets/HookIsland/Scenes/HookPlatform.unity";
        [Test] public void SceneWiresPlatformMapBothTilePrefabsAndFourSeparateSpawns()
        {
            EditorSceneManager.OpenScene(Scene);var game=Object.FindObjectOfType<IslandFishingController>();
            Assert.That(game,Is.Not.Null);var serialized=new SerializedObject(game);
            var settings=serialized.FindProperty("platformSettings").objectReferenceValue as PlatformSettings;
            Assert.That(settings,Is.Not.Null);var map=settings.CreateMap();
            foreach(var spawn in map.Spawns)Assert.That(map.CanStand(spawn.X,spawn.Z),Is.True);
            Assert.That(map.HookRange(0),Is.EqualTo(4000));
            var construction=new SerializedObject(game.GetComponent<PlatformConstruction>());
            foreach(var field in new[]{"woodPrefab","bonePrefab","editorPreview","shoreline"})Assert.That(construction.FindProperty(field).objectReferenceValue,Is.Not.Null,field);
            Assert.That(Object.FindObjectsOfType<Camera>().Length,Is.EqualTo(1));
            Assert.That(GameObject.Find("Hook Island"),Is.Null,"Legacy island obstructs the new grid.");
        }
        [Test] public void BothMaterialsHaveAnUnbrokenTwoMetreWalkSurface()
        {
            foreach(var name in new[]{"TileWood","TileFishbone"})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HookIsland/Prefabs/"+name+".prefab");
                var go=Object.Instantiate(prefab);
                try
                {
                    var collider=go.GetComponent<BoxCollider>();Assert.That(collider.size.x,Is.EqualTo(2));Assert.That(collider.size.z,Is.EqualTo(2));
                    Physics.SyncTransforms();
                    for(int x=-9;x<=9;x+=3)for(int z=-9;z<=9;z+=3)
                    {
                        Assert.That(collider.Raycast(new Ray(new Vector3(x*.1f,3,z*.1f),Vector3.down),out var hit,4),Is.True);
                        Assert.That(hit.point.y,Is.EqualTo(.45f).Within(.002f));
                    }
                    Assert.That(go.GetComponentsInChildren<Collider>().Length,Is.EqualTo(1),"Decorative gaps must not introduce collider gaps.");
                }
                finally{Object.DestroyImmediate(go);}
            }
        }
        [Test] public void GameSceneDoesNotSerializeAnAutomatedPlayer()
        {
            EditorSceneManager.OpenScene(Scene);
            Assert.That(Object.FindObjectsOfType<PlatformSmokeDriver>().Length,Is.Zero,
                "Automated drivers must be opt-in development bootstraps, not gameplay scene components.");
        }
        [Test] public void DestroyedOceanDoesNotReceiveStaleEditorCallbacks()
        {
            var go=new GameObject("Ocean callback fixture");go.AddComponent<MeshRenderer>();var ocean=go.AddComponent<IslandOcean>();
            var tick=typeof(IslandOcean).GetMethod("EditorTick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Object.DestroyImmediate(go);
            Assert.DoesNotThrow(()=>tick.Invoke(ocean,null));
        }
    }
}
