using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Explicit asset-only preview; no live room, reward injection or gameplay components.</summary>
    public sealed class ItemIconPreviewWindow : EditorWindow
    {
        private struct Sample { public ItemKind Kind; public int Type; }
        private readonly List<Sample> samples = new List<Sample>();
        private ItemIconPainter icons;
        private HudPainter painter;
        private Font font;
        private Vector2 scroll;

        [MenuItem("Tools/Hook Island/Preview Item Icons")]
        public static void Open()
        {
            var window = GetWindow<ItemIconPreviewWindow>();
            window.titleContent = new GUIContent("物品图标");
            window.minSize = new Vector2(1080, 740); window.Show();
        }
        private void OnEnable()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Microsoft YaHei", "Arial" }, 20);
            icons = new ItemIconPainter(); painter = new HudPainter(font); samples.Clear();
            samples.Add(new Sample { Kind = ItemKind.Wood }); samples.Add(new Sample { Kind = ItemKind.Fishbone });
            for (int i = 0; i < RodCatalog.All.Count; i++) samples.Add(new Sample { Kind = ItemKind.Rod, Type = i });
            for (int i = 1; i <= InventoryCatalog.Backpacks.Count; i++) samples.Add(new Sample { Kind = ItemKind.Backpack, Type = i });
            for (int i = 1; i <= SwimRingCatalog.All.Count; i++) samples.Add(new Sample { Kind = ItemKind.SwimRing, Type = i });
            for (int i = 0; i < ApparelCatalog.All.Count; i++) samples.Add(new Sample { Kind = ItemKind.Apparel, Type = i });
        }
        private void OnDisable() { painter?.Dispose(); if (font != null) DestroyImmediate(font); }
        private void OnGUI()
        {
            HudPainter.Fill(new Rect(0, 0, position.width, position.height), new Color(.035f, .10f, .13f));
            painter.Text(new Rect(24, 12, 800, 31), "海上杂货  /  " + samples.Count + " 种物品图标", 21, HudPainter.White, bold: true);
            painter.Text(new Rect(24, 46, 980, 24), "木材 · 鱼骨 · 31 款钓竿 · 10 级背包 · 10 级泳圈 · 帽子 / 外套 / 鞋子", 13, HudPainter.Muted);
            const float cellWidth = 128, cellHeight = 96;
            scroll = GUI.BeginScrollView(new Rect(15, 80, position.width - 30, position.height - 90), scroll,
                new Rect(0, 0, 8 * cellWidth, Mathf.Ceil(samples.Count / 8f) * cellHeight));
            for (int i = 0; i < samples.Count; i++)
            {
                var sample = samples[i]; var definition = InventoryCatalog.Get(sample.Kind, sample.Type);
                var rect = new Rect(i % 8 * cellWidth + 3, i / 8 * cellHeight + 2, cellWidth - 8, cellHeight - 6);
                painter.Surface(rect, new Color(.075f, .17f, .2f));
                icons.Draw(new Rect(rect.x + 22, rect.y + 5, rect.width - 44, 62), sample.Kind, sample.Type);
                painter.Text(new Rect(rect.x + 3, rect.yMax - 23, rect.width - 6, 20), definition.Name, 11,
                    HudPainter.White, TextAnchor.MiddleCenter);
                int level = ItemIconPainter.Level(sample.Kind, sample.Type);
                if (level > 0) painter.Text(new Rect(rect.x + 6, rect.y + 5, 30, 18), level.ToString(), 11, ItemIconPainter.Accent(sample.Kind, sample.Type));
                if (InventoryCatalog.IsMaterial(sample.Kind)) painter.Text(new Rect(rect.xMax - 30, rect.y + 45, 26, 19), InventoryCatalog.MaterialStackLimit.ToString(), 12, HudPainter.Gold, TextAnchor.MiddleRight);
            }
            GUI.EndScrollView();
        }
    }
}
