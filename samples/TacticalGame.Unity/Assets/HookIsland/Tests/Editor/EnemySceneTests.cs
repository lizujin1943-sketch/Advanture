using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class EnemySceneTests
    {
        [Test] public void FormalPlatformPackagesEnemyRules()
        {
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset");
            Assert.That(settings.fishing.enemies,Is.Not.Null);var c=settings.CreateMap().Fishing.Enemies;
            Assert.That(c.PiranhaCount,Is.EqualTo(3));Assert.That(c.MonsterEnabled,Is.True);Assert.That(c.MonsterWeightGrams,Is.EqualTo(50000));
            Assert.That(c.BiteDamagePerKg,Is.EqualTo(3));Assert.That(c.MonsterLevel,Is.EqualTo(1));Assert.That(c.KnockbackDistance,Is.EqualTo(4000));Assert.That(c.WindupTicks,Is.EqualTo(60));Assert.That(c.AttackIntervalTicks,Is.EqualTo(180));
            var bytes=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/PlatformMap.bytes").bytes;
            Assert.That(bytes.SequenceEqual(settings.CreateMap().Encode()),Is.True);
        }
        [Test] public void TuningChangesRoomCompatibility()
        {
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();var settings=ScriptableObject.CreateInstance<EnemySettings>();
            try
            {
                fishing.enemies=settings;var before=fishing.CreateConfig().Encode();settings.windupSeconds=1.5f;settings.waterballDamage=25;
                Assert.That(fishing.CreateConfig().Enemies.WindupTicks,Is.EqualTo(45));Assert.That(before.SequenceEqual(fishing.CreateConfig().Encode()),Is.False);
            }
            finally{Object.DestroyImmediate(fishing);Object.DestroyImmediate(settings);}
        }
        [Test] public void ViewsRenderConfirmedEnemiesWithoutPhysicsOrStateMutation()
        {
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset");var map=settings.CreateMap();
            var state=new HookState(0,new[]{new Fisher {Slot=0,Vitals=new CharacterVitals{Health=100,Hunger=100}}},Array.Empty<Fish>(),0,
                enemies:new EnemySystem(map.Fishing.Enemies).Capture());
            var root=new GameObject("Enemy test");var camera=new GameObject("Test camera").AddComponent<Camera>();
            var views=new EnemyPresentation(root.transform,camera,Shader.Find("Hook Island/Fishing Line"),map.Fishing.Enemies);
            try
            {
                var bytes=new HookGame().Encode(state);views.Synchronize(state);
                Assert.That(views.RenderedPiranhas,Is.EqualTo(3));Assert.That(views.RenderedMonsters,Is.EqualTo(0));
                Assert.That(root.GetComponentsInChildren<Collider>().Length,Is.EqualTo(0));Assert.That(bytes.SequenceEqual(new HookGame().Encode(state)),Is.True);
                var mesh=root.GetComponentsInChildren<MeshFilter>().First(f=>f.sharedMesh.name=="Enemy rounded body").sharedMesh;
                for(int i=0;i<mesh.vertexCount;i++)Assert.That(Vector3.Dot(mesh.vertices[i],mesh.normals[i]),Is.GreaterThanOrEqualTo(0));
                var enemy=new EnemySystem(map.Fishing.Enemies);var players=state.Players.ToArray();
                for(int tick=0;tick<=map.Fishing.Enemies.InitialDelayTicks;tick++)enemy.Advance(players,_=>{});
                var surfaced=new HookState(1,players,state.Fish,0,enemies:enemy.Capture());views.Synchronize(surfaced);
                Assert.That(views.RenderedMonsters,Is.EqualTo(1));Assert.That(views.WindupVisible,Is.True);
                Assert.That(root.GetComponentsInChildren<TextMesh>().Any(t=>t.text.Contains("Lv1")),Is.True);
                enemy.TryHook(0,0);enemy.CompleteCapture(0,0);enemy.Interrupt();
                for(int tick=0;tick<17;tick++)enemy.Advance(players,_=>{});
                views.Synchronize(new HookState(2,players,state.Fish,0,enemies:enemy.Capture()));
                Assert.That(views.RenderedMonsters,Is.EqualTo(0));Assert.That(views.RenderedPiranhas,Is.EqualTo(2));
                views.Clear();Assert.That(views.RenderedPiranhas,Is.EqualTo(0));Assert.That(views.RenderedMonsters,Is.EqualTo(0));
            }
            finally{views.Dispose();Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);}
        }
    }
}
