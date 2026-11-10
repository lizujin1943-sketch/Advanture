using System;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class ItemIconAssetTests
    {
        [Test] public void GrenadeHasAStablePresentationId()
        {
            // 手雷购买回执使用 Throwable/0，不能在已合法的物品类型上抛异常。
            Assert.That(InventoryCatalog.IsValid(ItemKind.Throwable, 0), Is.True);
            Assert.That(ItemIconCatalog.Id(ItemKind.Throwable, 0), Is.EqualTo("throwable_grenade"));
        }

        [Test] public void EveryInventoryKindHasAnIconId()
        {
            // 直接枚举物品种类，避免新增类型后忘记同步图标目录和手写的资源检查清单。
            foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
            {
                int firstType = kind == ItemKind.Backpack || kind == ItemKind.SwimRing ? 1 : 0;
                Assert.That(InventoryCatalog.IsValid(kind, firstType), Is.True, kind.ToString());
                Assert.That(ItemIconCatalog.Id(kind, firstType), Is.Not.Null.And.Not.Empty, kind.ToString());
            }
        }

        [TestCase((ItemKind)0, 0)]
        [TestCase((ItemKind)255, 0)]
        [TestCase(ItemKind.Throwable, -1)]
        [TestCase(ItemKind.Throwable, 1)]
        public void InvalidItemsHaveNoIconWithoutThrowing(ItemKind kind, int type)
        {
            // 无效查询返回 false，由 UI 跳过绘制，不打断同一帧的其他 HUD。
            Assert.That(ItemIconCatalog.Shared.TryGet(kind, type, out _), Is.False);
        }

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
            for(int i=0;i<FoodCatalog.All.Count;i++)Check(ItemKind.Food,i);
            Check(ItemKind.Wood, 0); Check(ItemKind.Fishbone, 0);
            for (int i = 0; i < RodCatalog.All.Count; i++) Check(ItemKind.Rod, i);
            for (int i = 1; i <= InventoryCatalog.Backpacks.Count; i++) Check(ItemKind.Backpack, i);
            for (int i = 1; i <= SwimRingCatalog.All.Count; i++) Check(ItemKind.SwimRing, i);
            for (int i = 0; i < ApparelCatalog.All.Count; i++) Check(ItemKind.Apparel, i);
            for(int i=0;i<WeaponCatalog.All.Count;i++)Check(ItemKind.Weapon,i);
            Check(ItemKind.Throwable, 0);
            Assert.That(atlas.Count, Is.EqualTo(count), "Atlas contains missing or stale catalog art.");
        }
        [Test] public void AtlasImportPreservesAlphaResolutionAndCellBoundaries()
        {
            const string path = "Assets/HookIsland/Resources/UI/ItemIcons.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null); Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.mipmapEnabled, Is.False); Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(texture.width, Is.EqualTo(1024)); Assert.That(texture.height, Is.EqualTo(1024));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        }

        [Test] public void GrenadeIconPreservesTransparencyAndSmallSizeEdges()
        {
            const string path = "Assets/HookIsland/Resources/UI/GrenadeIcon.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(texture, Is.SameAs(ItemIconCatalog.Shared.TextureFor(ItemKind.Throwable)));
            Assert.That(texture.width, Is.EqualTo(128));
            Assert.That(texture.height, Is.EqualTo(128));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        }
    }
}
