using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    public readonly struct ItemIconRegion
    {
        public ItemIconRegion(Rect uv, float aspect) { Uv = uv; Aspect = aspect; }
        public Rect Uv { get; }
        public float Aspect { get; }
    }

    /// <summary>按稳定物品标识查询显示资源，不依赖背包实例编号，也不修改联机状态。</summary>
    public sealed class ItemIconCatalog
    {
        [Serializable] private sealed class AtlasData { public int width, height; public Entry[] entries; }
        [Serializable] private sealed class Entry { public string id; public int x, y, width, height; }
        private readonly Dictionary<string, ItemIconRegion> regions = new Dictionary<string, ItemIconRegion>();
        private static ItemIconCatalog shared;
        public static ItemIconCatalog Shared => shared ?? (shared = new ItemIconCatalog());
        public Texture2D Texture { get; }
        private readonly Texture2D weaponTexture;
        private readonly Texture2D grenadeTexture;
        public int Count => regions.Count + (weaponTexture != null ? WeaponCatalog.All.Count : 0) +
            (grenadeTexture != null ? 1 : 0);

        /// <summary>图集和独立图标使用同一查询入口，背包与获得提示无需各自判断手雷类型。</summary>
        public Texture2D TextureFor(ItemKind kind)
        {
            if (kind == ItemKind.Weapon) return weaponTexture;
            if (kind == ItemKind.Throwable) return grenadeTexture;
            return Texture;
        }

        private ItemIconCatalog()
        {
            Texture = Resources.Load<Texture2D>("UI/ItemIcons");
            weaponTexture=Resources.Load<Texture2D>("UI/FryingPanIcon");
            grenadeTexture = Resources.Load<Texture2D>("UI/GrenadeIcon");
            var source = Resources.Load<TextAsset>("UI/ItemIconMap");
            if (Texture == null || source == null) { Debug.LogError("Item icon atlas or manifest is missing."); return; }
            var data = JsonUtility.FromJson<AtlasData>(source.text);
            if (data == null || data.entries == null || data.width != Texture.width || data.height != Texture.height)
            { Debug.LogError("Item icon atlas dimensions do not match its manifest."); return; }
            foreach (var entry in data.entries)
            {
                if (string.IsNullOrEmpty(entry.id) || regions.ContainsKey(entry.id) || entry.width <= 0 || entry.height <= 0 ||
                    entry.x < 0 || entry.y < 0 || entry.x + entry.width > data.width || entry.y + entry.height > data.height)
                { Debug.LogError("Invalid item icon region: " + entry.id); continue; }
                var uv = new Rect(entry.x / (float)data.width, 1 - (entry.y + entry.height) / (float)data.height,
                    entry.width / (float)data.width, entry.height / (float)data.height);
                regions.Add(entry.id, new ItemIconRegion(uv, entry.width / (float)entry.height));
            }
        }

        public bool TryGet(ItemKind kind, int type, out ItemIconRegion region)
        {
            region = default;
            if (!InventoryCatalog.IsValid(kind, type)) return false;
            // 目前仅有 Throwable/0 这一种手雷；先由物品目录验证类型，再取独立纹理。
            // 缺失资源与非法物品都返回 false，不能在购买回执的 HUD 绘制中抛异常。
            if (kind == ItemKind.Weapon || kind == ItemKind.Throwable)
            {
                var texture = TextureFor(kind);
                if (texture == null) return false;
                region = new ItemIconRegion(new Rect(0, 0, 1, 1), texture.width / (float)texture.height);
                return true;
            }
            return regions.TryGetValue(Id(kind, type), out region);
        }
        public static string Id(ItemKind kind, int type)
        {
            switch (kind)
            {
                case ItemKind.Firearm: return FirearmCatalog.Get(type).Id;
                case ItemKind.Ammunition: return AmmunitionCatalog.Get(type).Id;
                case ItemKind.Food: return FoodCatalog.Get(type).Id;
                case ItemKind.Wood: return "material_wood";
                case ItemKind.Fishbone: return "material_fishbone";
                case ItemKind.Rod: return RodCatalog.Get(type).Id;
                case ItemKind.Backpack: return InventoryCatalog.Backpack(type).Id;
                case ItemKind.SwimRing: return SwimRingCatalog.Get(type).Id;
                case ItemKind.Apparel: return ApparelCatalog.Get(type).Id;
                case ItemKind.Weapon: return WeaponCatalog.Get(type).Id;
                case ItemKind.Throwable: return type == 0 ? "throwable_grenade" : throw new ArgumentOutOfRangeException(nameof(type));
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
