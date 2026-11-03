using System;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class FirearmWaterImpactTests
    {
        private GameObject root;
        private IslandMap map;
        private HookState state;
        private DesertEagleArt art;
        private FirearmFeedback shots;

        [SetUp] public void SetUp()
        {
            root = new GameObject("Bullet water feedback test");
            map = UnityEditor.AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset").CreateMap();
            state = new HookState(12, new[] { new Fisher { Slot = 0, Vitals = new CharacterVitals { Health = 100 } } },
                Array.Empty<Fish>(), 0, tiles: PlatformRules.InitialTiles());
            art = new DesertEagleArt(Shader.Find("Hook Island/Fishing Line"));
            shots = new FirearmFeedback(root.transform, art);
        }
        [TearDown] public void TearDown() { shots.Dispose(); art.Dispose(); Object.DestroyImmediate(root); }
        private HookGameplayEvent Shot(int x = 8000, int target = -1) => new HookGameplayEvent(
            HookGameplayEventKind.FirearmShot, 0, targetId: target, x: x, z: 0, itemKind: ItemKind.Firearm, itemType: 0);
        private void Draw() => shots.Present(map, state, state, 0, new Vector3(-5, 0, 2), false);
        private ParticleSystem.Particle[] Particles(string name)
        {
            var system = root.GetComponentsInChildren<ParticleSystem>(true).Single(p => p.name == name);
            var values = new ParticleSystem.Particle[system.particleCount]; system.GetParticles(values); return values;
        }

        [Test] public void OnlyConfirmedUnobstructedSeaContactsSplashAndTheTraceStartsAtTheGun()
        {
            var bytes = new HookGame().Encode(state);
            Draw();
            Assert.That(Particles("Bullet water ripples"), Is.Empty, "Aiming alone must not emit a hit.");
            shots.Apply(Shot(0), 0, map, state); Draw();
            Assert.That(Particles("Bullet water ripples"), Is.Empty, "Deck contact is not water.");
            shots.Reset(); shots.Apply(Shot(2900), 0, map, state); Draw();
            Assert.That(Particles("Bullet water ripples"), Is.Empty, "The narrow deck lip is still wood even outside the safe standing footprint.");
            shots.Reset(); shots.Apply(Shot(target: -4), 0, map, state); Draw();
            Assert.That(Particles("Bullet water ripples"), Is.Empty, "A solid stops the shot before any water splash.");
            shots.Reset(); shots.Apply(Shot(target: -2), 0, map, state); Draw();
            Assert.That(Particles("Bullet water ripples"), Is.Empty, "An enemy hit must not masquerade as an empty-water hit.");
            shots.Reset();
            var muzzle = new Vector3(.35f, .83f, -.1f);
            shots.Apply(Shot(), 0, map, state, muzzle); Draw();
            var rings = Particles("Bullet water ripples");
            Assert.That(rings, Has.Length.EqualTo(1));
            Assert.That(rings[0].position.x, Is.EqualTo(8));
            Assert.That(Particles("Bullet water spray").Length, Is.GreaterThan(0));
            var trace = root.GetComponentsInChildren<LineRenderer>().Single(l => l.name == "Short bullet trace");
            Assert.That(Vector3.Distance(trace.GetPosition(0), muzzle), Is.LessThan(.01f));
            Assert.That(trace.GetPosition(1), Is.EqualTo(rings[0].position));
            Assert.That(new HookGame().Encode(state), Is.EqualTo(bytes));
        }

        [Test] public void RepeatedConfirmedFramesDoNotDuplicateSplashesAndRestoreClearsThem()
        {
            var consumer = new FishingFeedback(); var identity = new object();
            var receipts = new[] { new ConfirmedGameplayEvent(Guid.NewGuid(), 1, 12, 0, Shot()) };
            void Apply(HookGameplayEvent value) => shots.Apply(value, 0, map, state);
            consumer.Update(identity, 1, 12, state, 0, receipts, 1, true, Apply); Draw();
            var first = Particles("Bullet water ripples");
            consumer.Update(identity, 1, 12, state, 0, receipts, 1.1, true, Apply); Draw();
            Assert.That(shots.PresentedShots, Is.EqualTo(1));
            Assert.That(Particles("Bullet water ripples").Select(p => p.position), Is.EqualTo(first.Select(p => p.position)));
            shots.Reset();
            consumer.Update(new object(), 1, 12, state, 0, Array.Empty<ConfirmedGameplayEvent>(), 2, true, Apply); Draw();
            Assert.That(root.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount), Is.Zero);
            Assert.That(shots.PresentedShots, Is.Zero);
        }

        [Test] public void SpraySettlesBeforeCircularRipplesAndBuffersStayBounded()
        {
            shots.Dispose(); shots = null;
            var settings = Object.Instantiate(FirearmWaterImpactSettings.LoadRequired()); settings.capacity = 8;
            try
            {
                using (var water = new FirearmWaterImpactView(root.transform, settings))
                {
                    water.Add(new Vector3(8, 4, 0), Vector3.right, 10, 42); water.Present(10.08f);
                    Assert.That(Particles("Bullet water spray").Max(p => p.position.y), Is.GreaterThan(water.SurfaceHeight + .08f));
                    Assert.That(Particles("Bullet water crowns").Length, Is.GreaterThan(0));
                    float firstSize = Particles("Bullet water ripples")[0].startSize3D.x;
                    water.Present(10.7f);
                    Assert.That(Particles("Bullet water spray"), Is.Empty);
                    Assert.That(Particles("Bullet water crowns"), Is.Empty);
                    var rings = Particles("Bullet water ripples"); Assert.That(rings, Has.Length.EqualTo(2));
                    Assert.That(rings[0].startSize3D.x, Is.GreaterThan(firstSize));
                    foreach (var ring in rings)
                    {
                        Assert.That(ring.startSize3D.x, Is.EqualTo(ring.startSize3D.z));
                        Assert.That(ring.position.x, Is.EqualTo(8)); Assert.That(ring.position.z, Is.Zero);
                    }
                    water.Present(12); Assert.That(Particles("Bullet water ripples"), Is.Empty);
                    int objects = root.GetComponentsInChildren<Transform>().Length;
                    for (int i = 0; i < 40; i++) water.Add(new Vector3(i, 0, 0), Vector3.forward, 20, (uint)i);
                    water.Present(20.2f);
                    Assert.That(Particles("Bullet water ripples"), Has.Length.EqualTo(16));
                    Assert.That(root.GetComponentsInChildren<Transform>().Length, Is.EqualTo(objects));
                    water.Clear(); Assert.That(root.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount), Is.Zero);
                }
            }
            finally { Object.DestroyImmediate(settings); shots = new FirearmFeedback(root.transform, art); }
        }
    }
}
