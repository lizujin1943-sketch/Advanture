using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class ItemIconAssetTests
    {
        [Test] public void EveryShippingInventoryItemHasAnInBoundsIcon()
        {
            var atlas = ItemIconCatalog.Shared; Assert.That(atlas.Texture, Is.Not.Null);
            int count = 0;
            void Check(ItemKind kind, int type)
            {
                Assert.That(atlas.TryGet(kind, type, out var region), Is.True, ItemIconCatalog.Id(kind, type));
                Assert.That(atlas.TextureFor(kind),Is.Not.Null);
                Assert.That(region.Uv.xMin, Is.GreaterThanOrEqualTo(0)); Assert.That(region.Uv.yMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(region.Uv.xMax, Is.LessThanOrEqualTo(1)); Assert.That(region.Uv.yMax, Is.LessThanOrEqualTo(1));
                Assert.That(region.Aspect, Is.GreaterThan(0)); count++;
            }
            Check(ItemKind.Wood, 0); Check(ItemKind.Fishbone, 0);
            for (int i = 0; i < RodCatalog.All.Count; i++) Check(ItemKind.Rod, i);
            for (int i = 1; i <= InventoryCatalog.Backpacks.Count; i++) Check(ItemKind.Backpack, i);
            for (int i = 1; i <= SwimRingCatalog.All.Count; i++) Check(ItemKind.SwimRing, i);
            for (int i = 0; i < ApparelCatalog.All.Count; i++) Check(ItemKind.Apparel, i);
            for(int i=0;i<WeaponCatalog.All.Count;i++)Check(ItemKind.Weapon,i);
            Assert.That(atlas.Count, Is.EqualTo(count), "Atlas contains missing or stale catalog art.");
        }
        [Test] public void AtlasImportPreservesAlphaResolutionAndCellBoundaries()
        {
            const string path = "Assets/HookIsland/Resources/UI/ItemIcons.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null); Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.mipmapEnabled, Is.False); Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(texture.width, Is.EqualTo(1024)); Assert.That(texture.height, Is.EqualTo(896));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        }
    }
}
