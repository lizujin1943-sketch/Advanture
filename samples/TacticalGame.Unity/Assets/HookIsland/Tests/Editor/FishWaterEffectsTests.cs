using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class FishWaterEffectsTests
    {
        private GameObject root, cameraObject;
        private IslandMap map;
        private FishingPresentation view;
        private Fish[] fish;

        [SetUp] public void SetUp()
        {
            map = UnityEditor.AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset").CreateMap();
            root = new GameObject("Fish water integration test"); cameraObject = new GameObject("Fish water camera");
            view = new FishingPresentation(root.transform, cameraObject.AddComponent<Camera>(), Shader.Find("Hook Island/Fishing Line"), map);
            fish = Enumerable.Range(0, map.FishSpots.Count).Select(i => FishMotion.Create(i, map.FishSpots[i], 300)).ToArray();
            for (int i = 0; i < fish.Length; i++) { fish[i].Species = 0; fish[i].WeightGrams = 2000; fish[i].Grade = 1; }
        }

        [TearDown] public void TearDown()
        { view.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }

        [Test] public void ConfirmedSwimmingAndCrossingsProduceParticlesWithoutChangingAuthority()
        {
            fish[0].Phase = FishPhase.Swimming; fish[0].Timer = FishMotion.SwimTicks - 5;
            Show(100);
            Assert.That(Particles("Swimming bubbles").Length, Is.GreaterThan(0), "Swimming must use actual particles.");
            Advance(7); Show(107);
            Assert.That(Particles("Water droplets").Length, Is.GreaterThan(0));
            var first = Particles("Surface foam"); Assert.That(first.Length, Is.GreaterThan(0));
            var drops = Particles("Water droplets");
            Show(107); // The controller may render one confirmed tick several times.
            Assert.That(Particles("Water droplets").Select(p => p.position), Is.EqualTo(drops.Select(p => p.position)));
            Advance(3); Show(110);
            Assert.That(Particles("Surface foam")[0].position, Is.EqualTo(first[0].position), "Departure foam stays in the water, not attached to the moving fish.");
            Advance(46); Show(156); // A long observation gap is deliberately not replayed.
            view.ClearPlayers();
            fish[0].Phase = FishPhase.Jumping; fish[0].Timer = FishMotion.JumpTicks - 2;
            Show(200); Advance(3); Show(203);
            Assert.That(Particles("Splash crowns").Length, Is.GreaterThan(0));
            Assert.That(Particles("Surface foam").Length, Is.GreaterThan(0));
            view.ClearPlayers();
            Assert.That(root.GetComponentsInChildren<ParticleSystem>(true).Sum(p => p.particleCount), Is.Zero);
        }

        [Test] public void SnapshotHookInterruptionAndReplacementCannotReplayOrInventSplashes()
        {
            fish[0].Phase = FishPhase.Jumping; fish[0].Timer = 8; Show(100);
            Assert.That(Particles("Water droplets"), Is.Empty, "A fresh snapshot must not replay its old takeoff.");
            fish[0].Phase = FishPhase.Hooked; Show(101);
            fish[0].Timer = 0; Show(150);
            Assert.That(Particles("Splash crowns"), Is.Empty, "A hooked fish has no scheduled landing.");
            fish[0].Phase = FishPhase.Jumping; fish[0].Timer = FishMotion.JumpTicks - 1; Show(151);
            Advance(2); Show(153);
            Assert.That(Particles("Surface foam").Length, Is.GreaterThan(0));
            Assert.That(Particles("Splash crowns").Length, Is.GreaterThan(0));
            fish[0].Generation++; fish[0].Phase = FishPhase.Waiting; Show(154);
            Assert.That(Particles("Surface foam"), Is.Empty, "Replacing a fish clears its old footprint.");
            fish[0].Phase = FishPhase.Swimming; fish[0].Timer = FishMotion.SwimTicks - 1; Show(200);
            Advance(2); Show(202);
            Assert.That(Particles("Water droplets").Length, Is.GreaterThan(0));
            Show(1); // Restore/restart with a lower confirmed step.
            Assert.That(Particles("Water droplets"), Is.Empty);
        }

        [Test] public void MeshParticlesRetainTheVisibleWaterTintThroughUnityRendering()
        {
            fish[0].Phase = FishPhase.Swimming; fish[0].Timer = FishMotion.SwimTicks - 1; Show(100);
            Advance(3); Show(103);
            Assert.That(Particles("Water droplets").All(p => p.startColor.a > 200), Is.True,
                "Fresh spray must stay visible before its final fade rather than starting almost transparent.");
            var foam = root.GetComponentsInChildren<ParticleSystem>().Single(p => p.name == "Surface foam");
            var baked = new Mesh();
            try
            {
                foam.GetComponent<ParticleSystemRenderer>().BakeMesh(baked, cameraObject.GetComponent<Camera>(), false);
                Assert.That(baked.vertexCount, Is.GreaterThan(0));
                var expected = Resources.Load<FishWaterEffectsSettings>(FishWaterEffectsSettings.ResourcePath).foamTint;
                if (QualitySettings.activeColorSpace == ColorSpace.Linear) expected = expected.linear;
                // Unity's mesh-particle vertex stream must preserve the pale water color,
                // rather than interpreting floating-point mesh colors as packed bytes.
                foreach (var color in baked.colors)
                {
                    Assert.That(color.r, Is.EqualTo(expected.r).Within(.012f));
                    Assert.That(color.g, Is.EqualTo(expected.g).Within(.012f));
                    Assert.That(color.a, Is.GreaterThan(.3f));
                }
            }
            finally { Object.DestroyImmediate(baked); }
        }

        private void Advance(int ticks)
        { for (int i = 0; i < ticks; i++) FishMotion.Advance(ref fish[0], map.FishSpots[0]); }
        private void Show(ulong step)
        {
            var ocean = new OceanState(visits: new OceanVisit[map.Fishing.Ocean.VisitSlots]);
            var state = new HookState(step, Array.Empty<Fisher>(), fish, 0, ocean: ocean);
            var bytes = new HookGame().Encode(state);
            view.Present(state, state, 0, Vector3.zero, false);
            Assert.That(new HookGame().Encode(state), Is.EqualTo(bytes));
        }
        private ParticleSystem.Particle[] Particles(string name)
        {
            var system = root.GetComponentsInChildren<ParticleSystem>(true).SingleOrDefault(p => p.name == name);
            Assert.That(system, Is.Not.Null, name + " is not connected to FishingPresentation.");
            var particles = new ParticleSystem.Particle[system.particleCount]; system.GetParticles(particles); return particles;
        }
    }
}
