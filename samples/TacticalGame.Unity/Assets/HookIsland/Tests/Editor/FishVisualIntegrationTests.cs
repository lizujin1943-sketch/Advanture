using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class FishVisualIntegrationTests
    {
        [TestCase(false)] [TestCase(true)]
        public void HookedFishFacesConfirmedOwnerOnFirstAndRepeatedSnapshots(bool piranha)
        {
            var map = UnityEditor.AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset").CreateMap();
            var root = new GameObject("Hooked fish facing test");
            var camera = new GameObject("Hooked fish facing camera").AddComponent<Camera>();
            try
            {
                using (var view = new FishingPresentation(root.transform, camera, Shader.Find("Hook Island/Fishing Line"), map))
                using (var enemies = new EnemyPresentation(root.transform, camera, Shader.Find("Hook Island/Fishing Line"), map.Fishing.Enemies))
                {
                    var fish = Enumerable.Range(0, map.FishSpots.Count).Select(i => new Fish
                    {
                        Id = i, Species = 0, Grade = 1, WeightGrams = 1000, Heading = 1,
                        Phase = !piranha && i == 0 ? FishPhase.Hooked : FishPhase.Waiting,
                        Owner = !piranha && i == 0 ? 0 : -1, X = 6000, Z = 2000, Y = 1200
                    }).ToArray();
                    var hooked = new Piranha { Id = 0, Owner = 0, Target = -1, X = 6000, Z = 2000, WeightGrams = 2000 };
                    var players = new[] { new Fisher { Slot = 0, X = -2000, Z = -2000, Phase = HookPhase.Returning,
                        FishId = piranha ? -1 : 0, HookedPiranha = piranha ? 1 : 0, DriftwoodId = -1, HookedPlayer = -1,
                        PulledBy = -1, DirectionX = 1000, HookX = 6000, HookZ = 2000,
                        Vitals = new CharacterVitals { Health = 100, Hunger = 100 } } };
                    var ocean = new OceanState(visits: new OceanVisit[map.Fishing.Ocean.VisitSlots]);
                    var enemyState = new EnemyState(piranha ? new[] { hooked } : Array.Empty<Piranha>(), default, Array.Empty<Waterball>());
                    for (int sample = 0; sample < 3; sample++)
                    {
                        // Repeat the first snapshot, then move only its confirmed owner.
                        if (sample == 2) { players[0].X = 9000; players[0].Z = 3000; }
                        var state = new HookState((ulong)(100 + sample / 2), players, fish, 0, ocean: ocean, enemies: enemyState);
                        var before = new HookGame().Encode(state);
                        view.Present(state, state, 0, Vector3.zero, false); enemies.Synchronize(state);
                        var shown = root.transform.Find(piranha ? "Water enemies (confirmed)/Piranha 0" : "Fishing visuals/Jumping fish 0");
                        var towardOwner = new Vector3(players[0].X / 1000f - shown.position.x, 0, players[0].Z / 1000f - shown.position.z).normalized;
                        Assert.That(Vector3.Dot(shown.forward, towardOwner), Is.GreaterThan(.999f),
                            "The fish must face its confirmed angler instead of its patrol heading or previous displayed position.");
                        Assert.That(new HookGame().Encode(state), Is.EqualTo(before));
                    }
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); }
        }

        // Catches a disconnected art catalogue, a stale species instance after respawn,
        // or animation/presentation accidentally writing back to confirmed state.
        [Test] public void ConfirmedSpeciesUsesTexturedSkinAndRespawnReplacesItWithoutChangingState()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset");
            var map = settings.CreateMap();
            var root = new GameObject("Fish integration test");
            var camera = new GameObject("Fish integration camera").AddComponent<Camera>();
            try
            {
                using (var view = new FishingPresentation(root.transform, camera, Shader.Find("Hook Island/Fishing Line"), map))
                {
                    var fish = Enumerable.Range(0, map.FishSpots.Count).Select(i => new Fish
                    {
                        Id = i, Species = 0, Grade = 1, WeightGrams = 1000,
                        Phase = i == 0 ? FishPhase.Jumping : FishPhase.Waiting,
                        X = 9000, Z = 4000, Y = 1200, Timer = 8, Heading = 1, Owner = -1
                    }).ToArray();
                    var ocean = new OceanState(visits: new OceanVisit[map.Fishing.Ocean.VisitSlots]);
                    var state = new HookState(8, Array.Empty<Fisher>(), fish, 0, ocean: ocean);
                    byte[] before = new HookGame().Encode(state);
                    view.Present(state, state, 0, Vector3.zero, false);
                    var shown = root.transform.Find("Fishing visuals/Jumping fish 0");
                    var first = shown.GetComponentInChildren<SkinnedMeshRenderer>();
                    Assert.That(first, Is.Not.Null, "The game must render the authored fish, not the old primitive body.");
                    Assert.That(first.sharedMaterial.mainTexture, Is.Not.Null);
                    Assert.That(first.sharedMaterial.mainTexture.filterMode, Is.EqualTo(FilterMode.Point));
                    var firstMesh = first.sharedMesh;
                    Assert.That(Vector3.Distance(shown.position, new Vector3(9, 1.32f, 4)), Is.LessThan(.001f));
                    Assert.That(new HookGame().Encode(state), Is.EqualTo(before));

                    fish[0].Species = 7; fish[0].Generation = 1;
                    var respawn = new HookState(16, Array.Empty<Fisher>(), fish, 0, ocean: ocean);
                    before = new HookGame().Encode(respawn);
                    view.Present(respawn, respawn, 0, Vector3.zero, false);
                    var changed = shown.GetComponentInChildren<SkinnedMeshRenderer>();
                    Assert.That(changed.sharedMesh, Is.Not.SameAs(firstMesh), "A respawned lionfish must not keep the previous mullet mesh.");
                    Assert.That(new HookGame().Encode(respawn), Is.EqualTo(before));
                    Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
                    view.ClearPlayers();
                    Assert.That(shown.gameObject.activeSelf, Is.False);
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); }
        }

        [Test] public void PiranhaUsesAuthoredSkinAndConfirmedBiteMovesJawWithoutMovingWorldRoot()
        {
            var config = new EnemyConfig(piranhaCount: 1, monsterEnabled: false);
            var root = new GameObject("Piranha integration test");
            var camera = new GameObject("Piranha integration camera").AddComponent<Camera>();
            try
            {
                using (var view = new EnemyPresentation(root.transform, camera, Shader.Find("Hook Island/Fishing Line"), config))
                {
                    var fish = new Piranha { Id = 0, X = 6000, Z = 2000, WeightGrams = 2000, Owner = -1, Target = 0, Cooldown = config.BiteIntervalTicks };
                    var player = new Fisher { Slot = 0, X = 8000, Z = 2000, Vitals = new CharacterVitals { Health = 100, Hunger = 100 } };
                    var state = new HookState(30, new[] { player }, Array.Empty<Fish>(), 0, enemies: new EnemyState(new[] { fish }, default, Array.Empty<Waterball>()));
                    byte[] before = new HookGame().Encode(state);
                    view.Synchronize(state);
                    var shown = root.transform.Find("Water enemies (confirmed)/Piranha 0");
                    Assert.That(shown.GetComponentInChildren<SkinnedMeshRenderer>(), Is.Not.Null, "Enemy fish must use the new piranha rig.");
                    var jaw = shown.GetComponentsInChildren<Transform>().Single(t => t.name == "Jaw");
                    var closed = jaw.localRotation; var position = shown.position;
                    fish.Cooldown -= 7;
                    var biting = new HookState(37, new[] { player }, Array.Empty<Fish>(), 0, enemies: new EnemyState(new[] { fish }, default, Array.Empty<Waterball>()));
                    view.Synchronize(biting);
                    Assert.That(Quaternion.Angle(closed, jaw.localRotation), Is.GreaterThan(10), "Confirmed bite cooldown must visibly open the authored jaw.");
                    Assert.That((shown.position - position).sqrMagnitude, Is.LessThan(.01f));
                    Assert.That(new HookGame().Encode(state), Is.EqualTo(before));
                    Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); }
        }
    }
}
