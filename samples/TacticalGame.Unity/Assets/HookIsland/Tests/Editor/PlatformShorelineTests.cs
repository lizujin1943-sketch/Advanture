using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class PlatformShorelineTests
    {
        private GameObject water;
        private PlatformShoreline shoreline;
        private MeshRenderer renderer;

        [SetUp] public void SetUp()
        {
            water = new GameObject("Platform shoreline fixture");
            renderer = water.AddComponent<MeshRenderer>();
            water.AddComponent<IslandOcean>();
            shoreline = water.AddComponent<PlatformShoreline>();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(water);

        [Test] public void ContactFollowsConcaveEdgesRoundedCornersAndOpenWaterHoles()
        {
            shoreline.Synchronize(new[]
            {
                new PlatformTile(0, 0, TileKind.Wood), new PlatformTile(1, 0, TileKind.Fishbone),
                new PlatformTile(0, 1, TileKind.Wood)
            });
            Assert.That(Distance(1, 0), Is.LessThan(-.03f), "Shared tile seams must not emit foam.");
            Assert.That(Distance(1.5f, 1.5f), Is.EqualTo(.5f).Within(.04f), "Concave water-facing corner; bilinear rounding stays below half a texel.");
            Assert.That(Distance(3.5f, -.5f), Is.EqualTo(.5f).Within(.02f));
            Assert.That(Distance(3.5f, -1.5f), Is.EqualTo(Mathf.Sqrt(.5f)).Within(.02f), "Outer corners are rounded.");
            var ring = new List<PlatformTile>();
            foreach (var tile in PlatformRules.InitialTiles()) if (tile.X != 0 || tile.Z != 0) ring.Add(tile);
            shoreline.Synchronize(ring);
            Assert.That(Distance(0, 0), Is.EqualTo(1).Within(.08f), "An opening in the deck is water, not an internal seam.");
        }

        [Test] public void ExpansionRestartAndDistantTilesReplaceThePreviousBoundary()
        {
            var initial = PlatformRules.InitialTiles();
            Assert.That(Distance(3.5f, 0), Is.EqualTo(.5f).Within(.02f));
            var expanded = new List<PlatformTile>(initial) { new PlatformTile(2, 0, TileKind.Fishbone) };
            shoreline.Synchronize(expanded);
            Assert.That(Distance(3.5f, 0), Is.LessThan(-.03f), "The old edge is now inside the deck.");
            Assert.That(Distance(5.5f, 0), Is.EqualTo(.5f).Within(.02f));
            shoreline.Synchronize(initial);
            Assert.That(Distance(3.5f, 0), Is.EqualTo(.5f).Within(.02f));
            shoreline.Synchronize(new[] { new PlatformTile(24, -24, TileKind.Wood) });
            Assert.That(Distance(49.5f, -48), Is.EqualTo(.5f).Within(.02f), "Platform edges can extend beyond the island's original 80 m mask.");
            shoreline.Synchronize(new PlatformTile[0]);
            Assert.That(Distance(0, 0), Is.EqualTo(16).Within(.02f), "Empty platform leaves no stale foam.");
        }

        [Test] public void OceanAnimationPreservesContactMaskAndDisablingReleasesIt()
        {
            var ocean = water.GetComponent<IslandOcean>();
            ocean.Apply(42);
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            var mask = block.GetTexture("_ShoreDistance");
            Assert.That(block.GetFloat("_OceanTime"), Is.EqualTo(42));
            Assert.That(Distance(3.5f, 0), Is.EqualTo(.5f).Within(.02f));
            shoreline.enabled = false;
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetVector("_PlatformShore").w, Is.Zero);
            Assert.That(block.GetFloat("_OceanTime"), Is.EqualTo(42));
            Assert.That(mask == null, Is.True, "Generated textures must be released when leaving the scene.");
        }

        private float Distance(float x, float z)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            var texture = (Texture2D)block.GetTexture("_ShoreDistance");
            var bounds = block.GetVector("_ShoreBounds");
            // GetPixelBilinear treats UV zero as the first texel centre. GPU tex2D uses
            // half-texel centres, so offset this CPU probe to match the rendered sample.
            var rg = texture.GetPixelBilinear((x - bounds.x) / bounds.z + .5f - .5f / texture.width,
                (z - bounds.y) / bounds.w + .5f - .5f / texture.height);
            return (rg.r * 65280 + rg.g * 255) / 65535 * 32 - 16;
        }
    }
}
