using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Character/equipment view. Reads confirmed snapshots and submits inventory intents only.</summary>
    public sealed class CharacterPanel
    {
        private readonly Font font;
        private GUIStyle title, body, small, button, centered, slotText;
        private readonly ItemIconPainter icons = new ItemIconPainter();
        private int selectedId, dragId;
        private Vector2 scroll, dragStart;
        private bool dragging;
        private ulong previewKey;
        private bool previewValid;
        private CharacterStats preview;
        private InventoryResult previewResult;
        private readonly Rect[] slotRects = new Rect[8];
        private static readonly Color Mint = new Color(.36f, .88f, .73f);
        private static readonly Color Muted = new Color(.66f, .79f, .82f);
        public CharacterPanel(Font font) { this.font = font; }
        public void Reset() { selectedId = dragId = 0; dragging = previewValid = false; scroll = Vector2.zero; }
        private GUIStyle Text(int size, Color color) => new GUIStyle(GUI.skin.label) {
            font = font, fontSize = size, wordWrap = true, normal = { textColor = color }
        };
        private static void Fill(Rect rect, Color color)
        { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }

        public void Draw(Rect area, CharacterConfig config, int staminaBase, InventoryState inventory,
            Fisher player, CharacterVitals vitals, bool pending, Action<InventoryCommand> request, SwimmingConfig swimming=null)
        {
            // IMGUI buttons consume drag/up events, including rawType. Keep the incoming
            // type before drawing controls so a drag can finish outside its source button.
            var inputType = Event.current.type;
            if (title == null)
            {
                title = Text(22, Mint); body = Text(17, Color.white); small = Text(14, Muted);
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16, wordWrap = true };
                centered = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
                slotText = Text(12, Color.white);
            }
            var current = CharacterEquipment.Calculate(config, staminaBase, inventory, player.Slot);
            var equipped = inventory.Items.Where(i => i.Owner == player.Slot && i.Location == ItemLocation.Equipped).ToArray();
            var stored = inventory.Items.Where(i => i.Owner == player.Slot && i.Location == ItemLocation.Backpack && i.Definition.Slot != EquipmentSlot.None).OrderBy(i => i.Id).ToArray();
            var selection = inventory.Items.FirstOrDefault(i => i.Owner == player.Slot && i.Location != ItemLocation.Ground && i.Id == selectedId);
            if (selection.Id == 0) selectedId = 0;
            bool hookBusy = player.Phase != HookPhase.Ready || player.Melee.Active, beingPulled = player.PulledBy >= 0;
            var intent = selection.Id == 0 ? default : selection.Location == ItemLocation.Equipped ?
                new InventoryCommand(InventoryAction.Unequip, (int)selection.Definition.Slot) : new InventoryCommand(InventoryAction.Equip, selection.Id);
            if (selection.Id != 0) UpdatePreview(config, staminaBase, inventory, player.Slot, intent, hookBusy, beingPulled, player.Swimming.InWater);
            GUI.BeginGroup(area);
            GUI.Label(new Rect(0, 0, 340, 32), $"P{player.Slot + 1}  ·  {(vitals.Downed ? "倒地" : player.Swimming.InWater ? "水中" : "岛上渔人")}", title);
            GUI.Label(new Rect(400, 0, 260, 32), "角色属性", title);
            GUI.Label(new Rect(695, 0, 340, 32), "背包中的装备", title);
            DrawPortrait(new Vector2(194, 177), player.Slot, equipped.Any(i => i.Definition.Slot == EquipmentSlot.Backpack), vitals.Downed);
            for (int n = 0; n < CharacterEquipment.Slots.Count; n++)
            {
                var slot = CharacterEquipment.Slots[n]; float x = n % 2 == 0 ? 0 : 252, y = 46 + n / 2 * 77;
                var rect = new Rect(x, y, 132, 65); slotRects[n] = rect;
                var item = equipped.FirstOrDefault(i => i.Definition.Slot == slot);
                bool compatible = dragging && selection.Id != 0 && CharacterEquipment.CanEquipTo(selection, slot);
                if (compatible) Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), previewResult == InventoryResult.Success ? Mint : new Color(1, .55f, .26f));
                else if (dragging) Fill(rect, new Color(.35f, .13f, .12f));
                if (item.Id != 0) BeginDrag(rect, item.Id);
                if (GUI.Button(rect, new GUIContent("", item.Id == 0 ? SlotName(slot) : item.Definition.Name), button))
                { selectedId = item.Id; previewValid = false; }
                if (item.Id != 0) icons.Draw(new Rect(rect.x + 5, rect.y + 10, 35, 45), item.Kind, item.Type);
                GUI.Label(new Rect(rect.x + 46, rect.y + 5, 82, 20), SlotName(slot), small);
                GUI.Label(new Rect(rect.x + 46, rect.y + 27, 82, 36), item.Id == 0 ? "空槽" : item.Definition.Name, slotText);
            }
            GUI.Label(new Rect(0, 365, 384, 56), "点击装备查看详情；把备用装备拖到对应槽位。\n默认服饰是外观，空槽不会提供属性。", small);
            DrawResource(400, 48, "生命", vitals.Health, current.HealthMax, HudPainter.Health);
            int normalSpeed=player.Swimming.InWater ? SwimmingSystem.Speed(swimming??new SwimmingConfig(),SwimRingCatalog.Equipped(inventory,player.Slot)) : current.MoveSpeed;
            int moveSpeed=vitals.Downed ? 0 : normalSpeed*CharacterMovement.HookSpeedPermille(player)/1000;
            GUI.Label(new Rect(400, 116, 270, 28), $"移速   {moveSpeed / 1000f:0.###} 米/秒", body);
            GUI.Label(new Rect(400, 158, 270, 28), $"防御   {current.Armor}", body);
            GUI.Label(new Rect(400, 188, 270, 25), $"攻击减伤 {100f * current.Armor / (CharacterDamage.DefenseScale + current.Armor):0.#}% · 不消耗", small);
            DrawResource(400, 221, "饥饿", vitals.Hunger, current.HungerMax, new Color(1, .73f, .31f));
            DrawResource(400, 284, "耐力", player.Stamina / (float)StaminaSystem.UnitsPerPoint, current.StaminaMax / StaminaSystem.UnitsPerPoint, StaminaArcPainter.Ochre);
            GUI.Label(new Rect(400, 343, 270, 50), $"耐力上限 = 基础 {staminaBase} + 装备 {current.StaminaMax / StaminaSystem.UnitsPerPoint - staminaBase}\n" +
                (vitals.Downed ? "倒地 · 停止恢复" : player.Swimming.Active ? "游泳中 · 松开方向键可休息" : hookBusy ? "钓竿使用中 · 停止恢复" : player.RegenDelay > 0 ? $"休息等待 {player.RegenDelay / 30f:0.0} 秒" : player.Stamina < current.StaminaMax ? "休息中 · 正在恢复" : "耐力已满"), small);
            GUI.Label(new Rect(400, 399, 270, 42), "换装增加上限时保留当前值；\n上限降低时裁切超出部分。", small);

            var bagArea = new Rect(695, 43, 340, 148);
            Fill(bagArea, new Color(.045f, .15f, .18f));
            scroll = GUI.BeginScrollView(bagArea, scroll, new Rect(0, 0, 318, Math.Max(144, stored.Length * 43)));
            if (stored.Length == 0) GUI.Label(new Rect(10, 18, 295, 60), "暂无备用装备\n可在商人处购买，或在背包页整理物品。", small);
            for (int n = 0; n < stored.Length; n++)
            {
                var item = stored[n]; var row = new Rect(3, n * 43 + 3, 309, 38);
                BeginDrag(row, item.Id);
                if (GUI.Button(row, new GUIContent("", item.Definition.Name), button))
                { selectedId = item.Id; previewValid = false; }
                icons.Draw(new Rect(row.x + 6, row.y + 3, 32, 32), item.Kind, item.Type);
                GUI.Label(new Rect(row.x + 46, row.y + 7, row.width - 50, 28), item.Definition.Name + "  ·  " + SlotName(item.Definition.Slot), small);
            }
            GUI.EndScrollView();
            DrawSelection(selection, current, intent, pending || vitals.Downed, request);
            EndDrag(inputType, selection, bagArea, pending || vitals.Downed || previewResult != InventoryResult.Success, request);
            GUI.EndGroup();
        }
        private void DrawSelection(InventoryItem item, CharacterStats current, InventoryCommand intent, bool blocked, Action<InventoryCommand> request)
        {
            if (item.Id == 0) { GUI.Label(new Rect(695, 210, 340, 80), "选择身上或背包中的装备，\n查看装卸后的属性变化。", body); return; }
            icons.Draw(new Rect(695, 205, 42, 42), item.Kind, item.Type);
            GUI.Label(new Rect(748, 205, 287, 48), item.Definition.Name, title);
            if (item.Kind == ItemKind.Rod)
            {
                var rod = RodCatalog.Get(item.Type);
                GUI.Label(new Rect(695, 251, 340, 42), $"线长 {rod.Range / 1000f:0.#} 米  ·  出钩 {rod.Speed / 1000f:0.#} 米/秒\n耐力加成 +{rod.StaminaBonus}", small);
            }
            else if(item.Kind==ItemKind.Weapon)
            {
                var weapon=WeaponCatalog.Get(item.Type);
                GUI.Label(new Rect(695,251,340,42),$"伤害 {weapon.Damage} · 范围 {weapon.Range/1000f:0.#} 米\n耐力消耗 {weapon.StaminaCost} · 间隔 {weapon.CooldownTicks/30f:0.#} 秒",small);
            }
            else if (item.Kind == ItemKind.Backpack)
            {
                var size = InventoryCatalog.Backpack(item.Type).Size;
                GUI.Label(new Rect(695, 251, 340, 42), $"容量 {size.Width} × {size.Height}\n背包只增加容量，不提供角色属性加成。", small);
            }
            else if (item.Kind == ItemKind.SwimRing)
            {
                var ring=SwimRingCatalog.Get(item.Type);
                GUI.Label(new Rect(695,251,340,42),$"游速 ×{ring.SpeedPermille/1000f:0.##} · 游泳消耗 ×{ring.StaminaPermille/1000f:0.##}\n免落水掉血 · 水中可出竿",small);
            }
            else if (item.Kind == ItemKind.Apparel)
                GUI.Label(new Rect(695, 251, 340, 42), ApparelDisplay.Bonus(item.Definition.Modifiers), small);
            bool equipped = item.Location == ItemLocation.Equipped;
            string changes = previewResult == InventoryResult.Success ? Changes(current, preview) : "";
            GUI.Label(new Rect(695, 302, 340, 45), string.IsNullOrEmpty(changes) ? "角色属性不变" : changes, body);
            bool previous = GUI.enabled; GUI.enabled = previous && !blocked && previewResult == InventoryResult.Success;
            string action = equipped ? item.Definition.Slot == EquipmentSlot.Backpack ? "卸下空背包 · 明确放到平台" : "卸下装备 · 放入背包" : "装备到" + SlotName(item.Definition.Slot);
            if (GUI.Button(new Rect(695, 353, 340, 38), action, button)) request(intent);
            GUI.enabled = previous;
            GUI.Label(new Rect(695, 398, 340, 50), previewResult == InventoryResult.Success ?
                "操作由房间确认；查看角色不会暂停世界。" : Explain(previewResult), small);
        }
        private void UpdatePreview(CharacterConfig config, int staminaBase, InventoryState inventory, int owner, InventoryCommand intent, bool hookBusy, bool beingPulled, bool inWater)
        {
            ulong key = 1469598103934665603UL;
            unchecked
            {
                foreach (var item in inventory.Items.Where(i => i.Owner == owner))
                { key = (key ^ (uint)item.Id) * 1099511628211UL; key = (key ^ (uint)(item.X + 16 * item.Y + 256 * (int)item.Location + 2048 * item.Type + (item.Rotated ? 1048576 : 0) + item.Count * 2097152)) * 1099511628211UL; }
                key = (key ^ (uint)intent.Target) * 1099511628211UL; key = (key ^ (uint)intent.Action) * 1099511628211UL;
                key = (key ^ (uint)((hookBusy ? 1 : 0) + (beingPulled ? 2 : 0) + (inWater ? 4 : 0))) * 1099511628211UL;
            }
            if (previewValid && previewKey == key) return;
            if(inWater && intent.Action==InventoryAction.Unequip && intent.Target==(int)EquipmentSlot.Backpack)previewResult=InventoryResult.InvalidPlacement;
            else previewResult = CharacterEquipment.Preview(config, staminaBase, inventory, owner, intent, hookBusy, beingPulled, out preview);
            previewKey = key; previewValid = true;
        }
        private void BeginDrag(Rect rect, int id)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            { selectedId = dragId = id; dragStart = GUIUtility.GUIToScreenPoint(e.mousePosition); dragging = false; previewValid = false; }
        }
        private void EndDrag(EventType inputType, InventoryItem selection, Rect bagArea, bool blocked, Action<InventoryCommand> request)
        {
            var e = Event.current;
            if (dragId != 0 && inputType == EventType.MouseDrag && (GUIUtility.GUIToScreenPoint(e.mousePosition) - dragStart).sqrMagnitude > 64)
            { dragging = true; e.Use(); }
            if (dragging && selection.Id != 0)
            {
                icons.Draw(new Rect(e.mousePosition.x + 12, e.mousePosition.y - 16, 32, 40), selection.Kind, selection.Type);
                GUI.Label(new Rect(e.mousePosition.x + 52, e.mousePosition.y - 12, 250, 35), selection.Definition.Name, body);
                if (inputType == EventType.MouseUp)
                {
                    if (!blocked)
                    {
                        for (int i = 0; i < slotRects.Length; i++)
                            if (slotRects[i].Contains(e.mousePosition) && CharacterEquipment.CanEquipTo(selection, CharacterEquipment.Slots[i]) && selection.Location == ItemLocation.Backpack)
                                request(new InventoryCommand(InventoryAction.Equip, selection.Id));
                        if (bagArea.Contains(e.mousePosition) && selection.Location == ItemLocation.Equipped && selection.Definition.Slot != EquipmentSlot.Backpack)
                            request(new InventoryCommand(InventoryAction.Unequip, (int)selection.Definition.Slot));
                    }
                    dragId = 0; dragging = false; e.Use();
                }
            }
            else if (inputType == EventType.MouseUp) dragId = 0;
        }
        private void DrawResource(float x, float y, string label, float value, int max, Color color)
        {
            var previous = body.normal.textColor; body.normal.textColor = color;
            GUI.Label(new Rect(x, y, 270, 27), $"{label}   {value:0.#} / {max}", body);
            body.normal.textColor = previous;
            Fill(new Rect(x, y + 32, 268, 7), new Color(.1f, .24f, .27f));
            if (max > 0) Fill(new Rect(x, y + 32, 268 * Mathf.Clamp01(value / max), 7), color);
        }
        private void DrawPortrait(Vector2 center, int slot, bool backpack, bool downed)
        {
            var coat = FishingPresentation.PlayerColors[slot]; float x = center.x, y = center.y;
            Fill(new Rect(x - 38, y + 64, 76, 7), new Color(.04f, .12f, .14f));
            if (backpack) Fill(new Rect(x - 32, y - 23, 64, 59), new Color(.5f, .3f, .14f));
            Fill(new Rect(x - 24, y - 22, 48, 61), coat);
            Fill(new Rect(x - 25, y - 62, 50, 39), new Color(1, .74f, .5f));
            Fill(new Rect(x - 35, y - 66, 70, 9), coat);
            Fill(new Rect(x - 23, y - 83, 46, 20), coat);
            Fill(new Rect(x - 18, y - 48, 6, 7), Color.black); Fill(new Rect(x + 12, y - 48, 6, 7), Color.black);
            Fill(new Rect(x - 22, y + 37, 18, 28), new Color(.15f, .22f, .26f)); Fill(new Rect(x + 4, y + 37, 18, 28), new Color(.15f, .22f, .26f));
            GUI.Label(new Rect(x - 52, y + 78, 104, 33), downed ? "倒地" : "P" + (slot + 1), centered);
        }
        private static string Changes(CharacterStats a, CharacterStats b)
        {
            string result = "";
            if (a.HealthMax != b.HealthMax) result += $"生命 {a.HealthMax} → {b.HealthMax}  ";
            if (a.MoveSpeed != b.MoveSpeed) result += $"移速 {a.MoveSpeed / 1000f:0.###} → {b.MoveSpeed / 1000f:0.###}  ";
            if (a.Armor != b.Armor) result += $"防御 {a.Armor} → {b.Armor}  ";
            if (a.HungerMax != b.HungerMax) result += $"饥饿 {a.HungerMax} → {b.HungerMax}  ";
            if (a.StaminaMax != b.StaminaMax) result += $"耐力上限 {a.StaminaMax / 30000} → {b.StaminaMax / 30000}";
            return result;
        }
        public static string SlotName(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Hat: return "帽子"; case EquipmentSlot.LeftHand: return "左手";
                case EquipmentSlot.RightHand: return "右手"; case EquipmentSlot.Clothes: return "衣服";
                case EquipmentSlot.Shoes: return "鞋子"; case EquipmentSlot.Backpack: return "背包";
                case EquipmentSlot.Accessory: return "饰品"; case EquipmentSlot.SwimRing: return "游泳圈";
                default: return "未指定";
            }
        }
        private static string Explain(InventoryResult result)
        {
            switch (result)
            {
                case InventoryResult.NoSpace: return "背包没有足够空间，原装备保持不变。";
                case InventoryResult.InventoryNotEmpty: return "先清空背包内容，才能卸下背包。";
                case InventoryResult.HookBusy: return "被拖拽时不能装卸；更换右手装备还需先收回钓竿。";
                case InventoryResult.InvalidPlacement: return "先上岸，再将物品放到平台。";
                case InventoryResult.NotOwned: return "只能操作自己的装备。";
                default: return "当前无法完成装卸，请在背包页检查物品。";
            }
        }
    }
}
