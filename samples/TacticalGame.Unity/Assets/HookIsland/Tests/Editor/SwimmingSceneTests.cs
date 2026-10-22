using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class SwimmingSceneTests
    {
        [Test] public void PackagedMapIncludesAuthoredWaterRules()
        {
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset");
            Assert.That(settings.fishing.swimming,Is.Not.Null);
            var config=settings.fishing.swimming.CreateConfig();
            Assert.That(config.HealthLossMilliPerSecond,Is.EqualTo(5000));
            Assert.That(config.Speed,Is.EqualTo(1000));Assert.That(config.StaminaMilliPerSecond,Is.EqualTo(10000));
            var map=settings.CreateMap();var saved=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/PlatformMap.bytes");
            Assert.That(saved.bytes.SequenceEqual(map.Encode()),Is.True);

        }
        [Test] public void WaterConfigurationChangesRoomCompatibility()
        {
            var water=ScriptableObject.CreateInstance<SwimmingSettings>();var fishing=ScriptableObject.CreateInstance<FishingSettings>();
            try
            {
                fishing.swimming=water;var before=fishing.CreateConfig().Encode();water.swimStaminaPerSecond=8.5f;
                Assert.That(fishing.CreateConfig().Swimming.StaminaMilliPerSecond,Is.EqualTo(8500));
                Assert.That(before.SequenceEqual(fishing.CreateConfig().Encode()),Is.False);
            }
            finally{Object.DestroyImmediate(water);Object.DestroyImmediate(fishing);}
        }
        [Test] public void RingGeometryHasOutwardFacesAndSharedGroundScale()
        {
            var mesh=SwimmingAppearance.CreateRingMesh();
            try
            {
                var v=mesh.vertices;var n=mesh.normals;
                Assert.That(v.Length,Is.EqualTo(16*6*6));Assert.That(mesh.bounds.size.x,Is.InRange(1.6f,1.7f));
                for(int i=0;i<v.Length;i++)
                {
                    var outward=v[i]-new Vector3(v[i].x,0,v[i].z).normalized*.65f;
                    Assert.That(Vector3.Dot(outward,n[i]),Is.GreaterThan(0),"Ring surface is facing inward.");
                }
            }
            finally{Object.DestroyImmediate(mesh);}
        }
    }
}
