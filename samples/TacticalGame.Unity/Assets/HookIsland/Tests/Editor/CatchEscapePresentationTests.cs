using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class CatchEscapePresentationTests
    {
        [Test] public void SameConfirmedTickRestoresIdenticalPoseAndWaterEffects()
        {
            var first = new GameObject("First peer"); var second = new GameObject("Restored peer");
            var body = Body(first.transform); var restoredBody = Body(second.transform);
            var shader = Shader.Find("Hook Island/Fishing Line");
            var view = new CatchEscapePresentation(first.transform, shader);
            var restored = new CatchEscapePresentation(second.transform, shader);
            try
            {
                var escape = CatchEscapeMotion.Begin(2100, 1250, -1400, new Point(3500, -3800));
                for (int tick = 1; tick <= CatchEscapeMotion.TotalTicks; tick++)
                {
                    escape.Tick = tick;
                    view.Present(0, escape, body);
                    // A peer can join directly at any animation tick without playing earlier frames.
                    restored.Present(0, escape, restoredBody);
                    CatchEscapeMotion.Position(escape, out int x, out int y, out int z);
                    Assert.That(body.position, Is.EqualTo(new Vector3(x / 1000f, y / 1000f + .12f, z / 1000f)));
                    Assert.That(restoredBody.position, Is.EqualTo(body.position));
                    Assert.That(restoredBody.rotation, Is.EqualTo(body.rotation));
                    Assert.That(restoredBody.Find("Tail").localRotation, Is.EqualTo(body.Find("Tail").localRotation));
                    var ring = first.GetComponentInChildren<LineRenderer>();
                    var otherRing = second.GetComponentInChildren<LineRenderer>();
                    Assert.That(ring != null, Is.EqualTo(tick >= CatchEscapeMotion.SplashTick));
                    if (ring == null) continue;
                    Assert.That(otherRing.transform.position, Is.EqualTo(ring.transform.position));
                    Assert.That(otherRing.GetPosition(0), Is.EqualTo(ring.GetPosition(0)));
                    Assert.That(otherRing.startColor, Is.EqualTo(ring.startColor));
                    Assert.That(ring.transform.childCount, Is.EqualTo(8));
                    for (int i = 0; i < 8; i++)
                        Assert.That(otherRing.transform.GetChild(i).position, Is.EqualTo(ring.transform.GetChild(i).position));
                }
                Assert.That(first.GetComponentsInChildren<Collider>(true), Is.Empty);
                var mesh = first.GetComponentInChildren<MeshFilter>().sharedMesh;
                for (int i = 0; i < mesh.vertexCount; i++)
                    Assert.That(Vector3.Dot(mesh.vertices[i], mesh.normals[i]), Is.GreaterThan(0));
            }
            finally { view.Dispose(); restored.Dispose(); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test] public void RestartAndNextCatchDoNotLeaveSplashOrTailPoseBehind()
        {
            var root = new GameObject("Escape restart"); var body = Body(root.transform);
            var view = new CatchEscapePresentation(root.transform, Shader.Find("Hook Island/Fishing Line"));
            try
            {
                var escape = CatchEscapeMotion.Begin(0, 1250, 0, new Point(2000, 0));
                escape.Tick = CatchEscapeMotion.SplashTick + 3;
                view.Present(7, escape, body);
                Assert.That(root.GetComponentsInChildren<LineRenderer>().Length, Is.EqualTo(1));
                view.Clear();
                Assert.That(root.GetComponentsInChildren<LineRenderer>(), Is.Empty);
                view.Present(7, default, body);
                Assert.That(body.Find("Tail").localRotation, Is.EqualTo(Quaternion.identity));
                escape.Tick = 1; view.Present(7, escape, body);
                Assert.That(root.GetComponentsInChildren<LineRenderer>(), Is.Empty);
                escape.Tick = CatchEscapeMotion.SplashTick; view.Present(7, escape, body);
                Assert.That(root.GetComponentsInChildren<LineRenderer>(true).Length, Is.EqualTo(1), "Effects should be reused.");
            }
            finally { view.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test] public void EscapingPiranhaRemainsVisibleWithoutChangingSimulation()
        {
            var map = AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset").CreateMap();
            var enemy = new EnemySystem(map.Fishing.Enemies);
            Assert.That(enemy.TryHook(0, 0), Is.True);
            enemy.Release(0, 0, new Point(5000, -5000));
            var root = new GameObject("Piranha escape"); var camera = new GameObject("Test camera").AddComponent<Camera>();
            var view = new EnemyPresentation(root.transform, camera, Shader.Find("Hook Island/Fishing Line"), map.Fishing.Enemies);
            try
            {
                var state = new HookState(1, new[] { new Fisher { Slot = 0 } }, Array.Empty<Fish>(), 0, enemies: enemy.Capture());
                var bytes = new HookGame().Encode(state);
                view.Synchronize(state);
                var body = root.transform.Find("Water enemies (confirmed)/Piranha 0");
                Assert.That(body.gameObject.activeInHierarchy, Is.True);
                Assert.That(body.position.y, Is.EqualTo(1.25f));
                Assert.That(bytes.SequenceEqual(new HookGame().Encode(state)), Is.True);
                view.Clear(); Assert.That(view.RenderedPiranhas, Is.Zero);
                Assert.That(root.GetComponentsInChildren<Collider>(), Is.Empty);
            }
            finally { view.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); }
        }

        private static Transform Body(Transform parent)
        {
            var body = new GameObject("Fish").transform; body.SetParent(parent, false);
            new GameObject("Tail").transform.SetParent(body, false); return body;
        }
    }
}
