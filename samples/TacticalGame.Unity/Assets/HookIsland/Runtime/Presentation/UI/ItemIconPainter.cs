using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Fits illustrations to any UI footprint without stretching. Does not own or mutate inventory state.</summary>
    public sealed class ItemIconPainter
    {
        private readonly ItemIconCatalog catalog = ItemIconCatalog.Shared;

        public void Draw(Rect area, ItemKind kind, int type, bool rotated = false, Color? tint = null)
        {
            if (area.width <= 0 || area.height <= 0 || !catalog.TryGet(kind, type, out var icon)) return;
            // Rotate around the final center after fitting in the unrotated available bounds.
            float availableWidth = rotated ? area.height : area.width, availableHeight = rotated ? area.width : area.height;
            float width = Mathf.Min(availableWidth, availableHeight * icon.Aspect), height = width / icon.Aspect;
            var rect = new Rect(area.center.x - width / 2, area.center.y - height / 2, width, height);
            var previousColor = GUI.color; var previousMatrix = GUI.matrix;
            GUI.color = previousColor * (tint ?? Color.white);
            if (rotated) GUIUtility.RotateAroundPivot(90, area.center);
            GUI.DrawTextureWithTexCoords(rect, catalog.TextureFor(kind), icon.Uv);
            GUI.matrix = previousMatrix; GUI.color = previousColor;
        }

        public static int Level(ItemKind kind, int type)
        {
            if (kind == ItemKind.Weapon) return WeaponCatalog.Get(type).Tier;
            if (kind == ItemKind.Rod) return RodCatalog.Get(type).Tier;
            if (kind == ItemKind.Backpack || kind == ItemKind.SwimRing) return type;
            return 0;
        }
        public static Color Accent(ItemKind kind, int type)
        {
            int tier = kind == ItemKind.Weapon ? WeaponCatalog.Get(type).Tier :
                kind == ItemKind.Rod ? RodCatalog.Get(type).Tier :
                kind == ItemKind.Backpack ? InventoryCatalog.Backpack(type).Tier :
                kind == ItemKind.SwimRing ? SwimRingCatalog.Get(type).Tier : 0;
            switch (tier)
            {
                case 1: return new Color(.39f, .81f, .65f);
                case 2: return new Color(.34f, .74f, .88f);
                case 3: return new Color(.61f, .57f, .91f);
                case 4: return new Color(.94f, .65f, .34f);
                case 5: return new Color(1, .84f, .46f);
                default: return new Color(.57f, .69f, .68f);
            }
        }
    }
}
