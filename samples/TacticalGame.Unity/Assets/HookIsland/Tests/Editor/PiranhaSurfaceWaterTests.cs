using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class PiranhaSurfaceWaterTests
    {
        [Test] public void SurfaceSwimmerIsHalfImmersedAndWaterUsesOnlyTheConfirmedFrame()
        {
            var root = new GameObject("Surface piranha test");
            var camera = new GameObject("Surface piranha camera").AddComponent<Camera>();
            try
            {
                using (var view = new EnemyPresentation(root.transform, camera, Shader.Find("Hook Island/Fishing Line"), new EnemyConfig(piranhaCount: 1, monsterEnabled: false)))
                {
                    var fish = FreeFish(); view.Synchronize(State(fish, 34));
                    fish.X += 200; var state = State(fish, 40);
                    byte[] before = new HookGame().Encode(state);
                    view.Synchronize(state);
                    var shown = root.transform.Find("Water enemies (confirmed)/Piranha 0");
                    Assert.That(Mathf.Abs(shown.position.y), Is.LessThan(.05f), "The body pivot must sit at the waterline, rather than float above it.");
                    var skin = shown.GetComponentInChildren<SkinnedMeshRenderer>();
                    Assert.That(skin.bounds.min.y, Is.LessThan(-.1f));
                    Assert.That(skin.bounds.max.y, Is.GreaterThan(.1f));
                    var systems = root.GetComponentsInChildren<ParticleSystem>();
                    Assert.That(systems.Single(s => s.name == "Body ripples").particleCount, Is.GreaterThan(0));
                    Assert.That(systems.Single(s => s.name == "Tail wake").particleCount, Is.GreaterThan(0));
                    Assert.That(systems.Single(s => s.name == "Tail spray").particleCount, Is.GreaterThan(0));
                    var pose = shown.position; var heading = shown.rotation;
                    var first = Samples(systems);
                    view.Synchronize(state);
                    Assert.That(Samples(systems), Is.EqualTo(first), "Repeated confirmed frames must not emit or age water particles.");
                    Assert.That(shown.position, Is.EqualTo(pose));
                    Assert.That(shown.rotation, Is.EqualTo(heading));
                    Assert.That(new HookGame().Encode(state), Is.EqualTo(before));
                    Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
                    var trail = systems.Single(s => s.name == "Tail wake");
                    var oldRings = new ParticleSystem.Particle[trail.particleCount]; trail.GetParticles(oldRings);
                    fish.Z += 200; view.Synchronize(State(fish, 46));
                    var newRings = new ParticleSystem.Particle[trail.particleCount]; trail.GetParticles(newRings);
                    Assert.That(newRings.Any(p => Vector3.Distance(p.position, oldRings[0].position) < .001f), Is.True,
                        "An existing ripple stays at its emission point when the fish changes direction.");
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); }
        }

        [Test] public void HookingEscapingRespawningAndClearingRemoveSurfaceWater()
        {
            var root = new GameObject("Surface piranha lifecycle");
            var camera = new GameObject("Surface piranha camera").AddComponent<Camera>();
            try
            {
                using (var view = new EnemyPresentation(root.transform, camera, Shader.Find("Hook Island/Fishing Line"), new EnemyConfig(piranhaCount: 1, monsterEnabled: false)))
                {
                    var fish = FreeFish(); view.Synchronize(State(fish, 34));
                    fish.X += 200; view.Synchronize(State(fish, 40));
                    var systems = root.GetComponentsInChildren<ParticleSystem>();
                    Assert.That(systems.Sum(s => s.particleCount), Is.GreaterThan(0));
                    fish.Owner = 0; view.Synchronize(State(fish, 41));
                    Assert.That(systems.Sum(s => s.particleCount), Is.Zero);
                    fish.Owner = -1; fish.Escape = new CatchEscapeState { Tick = 1 };
                    view.Synchronize(State(fish, 42));
                    Assert.That(systems.Sum(s => s.particleCount), Is.Zero);
                    fish.Escape = default; fish.RespawnTicks = 20; view.Synchronize(State(fish, 43));
                    Assert.That(systems.Sum(s => s.particleCount), Is.Zero);
                    fish.RespawnTicks = 0; view.Synchronize(State(fish, 1));
                    Assert.That(systems.Sum(s => s.particleCount), Is.GreaterThan(0));
                    view.Clear();
                    Assert.That(systems.Sum(s => s.particleCount), Is.Zero);
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); }
        }

        private static Piranha FreeFish() => new Piranha { Id = 0, X = -4800, Z = 0, WeightGrams = 2000, Owner = -1, Target = -1 };
        private static HookState State(Piranha fish, ulong step) => new HookState(step,
            new[] { new Fisher { Slot = 0, Vitals = new CharacterVitals { Health = 100, Hunger = 100 } } },
            Array.Empty<Fish>(), 0, enemies: new EnemyState(new[] { fish }, default, Array.Empty<Waterball>()));
        private static string[] Samples(ParticleSystem[] systems) => systems.SelectMany(system =>
        {
            var values = new ParticleSystem.Particle[system.particleCount]; system.GetParticles(values);
            return values.Select(p => p.position.ToString("F5") + p.GetCurrentSize3D(system).ToString("F5") + p.startColor.ToString());
        }).ToArray();
    }
}
