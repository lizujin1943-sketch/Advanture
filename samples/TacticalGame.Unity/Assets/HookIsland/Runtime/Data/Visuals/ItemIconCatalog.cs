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

    /// <summary>Presentation assets keyed by stable catalog IDs, independent of inventory instance IDs and network state.</summary>
    public sealed class ItemIconCatalog
    {
        [Serializable] private sealed class AtlasData { public int width, height; public Entry[] entries; }
        [Serializable] private sealed class Entry { public string id; public int x, y, width, height; }
        private readonly Dictionary<string, ItemIconRegion> regions = new Dictionary<string, ItemIconRegion>();
        private static ItemIconCatalog shared;
        public static ItemIconCatalog Shared => shared ?? (shared = new ItemIconCatalog());
        public Texture2D Texture { get; }
        private readonly Texture2D weaponTexture;
        public int Count => regions.Count+(weaponTexture!=null?WeaponCatalog.All.Count:0);
        public Texture2D TextureFor(ItemKind kind)=>kind==ItemKind.Weapon?weaponTexture:Texture;

        private ItemIconCatalog()
        {
            Texture = Resources.Load<Texture2D>("UI/ItemIcons");
            weaponTexture=Resources.Load<Texture2D>("UI/FryingPanIcon");
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
            if(kind==ItemKind.Weapon){region=new ItemIconRegion(new Rect(0,0,1,1),1);return weaponTexture!=null;}
            return regions.TryGetValue(Id(kind, type), out region);
        }
        public static string Id(ItemKind kind, int type)
        {
            switch (kind)
            {
                case ItemKind.Wood: return "material_wood";
                case ItemKind.Fishbone: return "material_fishbone";
                case ItemKind.Rod: return RodCatalog.Get(type).Id;
                case ItemKind.Backpack: return InventoryCatalog.Backpack(type).Id;
                case ItemKind.SwimRing: return SwimRingCatalog.Get(type).Id;
                case ItemKind.Apparel: return ApparelCatalog.Get(type).Id;
                case ItemKind.Weapon: return WeaponCatalog.Get(type).Id;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
