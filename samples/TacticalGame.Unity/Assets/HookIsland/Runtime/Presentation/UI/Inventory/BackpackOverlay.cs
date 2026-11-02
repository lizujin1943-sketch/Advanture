using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>背包和货架的只读视图。所有按钮只产生操作意图；预览使用库存副本，不修改房间状态。</summary>
    internal sealed partial class BackpackOverlay
    {
        private readonly GUIStyle title, text, small, button, quantity, priceText;
        private readonly ItemIconPainter icons = new ItemIconPainter();
        private int itemId, placing, amount = 1;
        private readonly MerchantOfferSelection selection=new MerchantOfferSelection();
        private MerchantState shelf=>selection.Shelf;
        private int offer {get=>selection.Offer;set=>selection.Select(value);}
        private bool rotated, dropOld;
        private int groundPage;
        private readonly InventoryEquipmentPreview equipmentPreview = new InventoryEquipmentPreview();
        private readonly bool[,] shopOccupied = new bool[8, 8];
        private readonly Color accent = new Color(.38f, .88f, .75f), gold = new Color(1, .8f, .38f);
        internal BackpackOverlay(Font font)
        {
            GUIStyle Label(int size, Color color) => new GUIStyle(GUI.skin.label) { font = font, fontSize = size, wordWrap = true, normal = { textColor = color } };
            title = Label(23, gold); text = Label(16, Color.white); small = Label(13, new Color(.73f, .86f, .85f));
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15, wordWrap = true };
            quantity = new GUIStyle(GUI.skin.label) { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
                fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight,
                wordWrap = false, richText = false, padding = new RectOffset(), clipping = TextClipping.Clip };
            priceText = new GUIStyle(quantity) { fontSize = 15 };
        }
        private static void Fill(Rect rect, Color color)
        { var previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous; }
        internal void ResetShopSelection(){selection.Bind(null);dropOld=false;}
        internal void Draw(HookState state, Fisher player, bool shop, bool pending, string message,
            float width, float height, Func<InventoryCommand, bool> request, Action close,int merchantId=0)
        {
            var inventory = state.Tackle.Inventory;
            selection.Bind(shop?state.Tackle.Merchants.FirstOrDefault(m=>m.Id==merchantId):null);
            if (placing != 0 && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.R)
            { if (placing == 1) rotated = !rotated; Event.current.Use(); }
            float x = (width - 1100) / 2, y = Mathf.Max(120, (height - 560) / 2);
            Fill(new Rect(x, y, 1100, 560), new Color(.026f, .082f, .11f, .99f)); Fill(new Rect(x, y, 1100, 3), accent);
            GUI.Label(new Rect(x + 24, y + 16, 760, 38), shop ? MerchantHud.Name(state.Tackle.Config,merchantId) : "我的背包", title);
            if (GUI.Button(new Rect(x + 982, y + 18, 92, 31), "关闭", button)) { placing = 0; close(); }
            ulong remaining=shelf!=null && shelf.NextRestock>state.Steps?(shelf.NextRestock-state.Steps)/(ulong)HookGame.Rate:0;
            string clock = !shop?" · 已穿戴装备不占背包格子":shelf?.Active==true?$" · {remaining/60:00}:{remaining%60:00} 后整批换货":" · 正在准备货架";
            GUI.Label(new Rect(x + 24, y + 60, 1040, 28), $"我的金币  {player.Coins:N0} G{clock}", text);
            // The overlay receives the confirmed local player; prices follow that player's settled wallet.
            if (shop) DrawShop(player.Coins, x + 24, y + 105);
            else DrawSlots(inventory, player, x + 24, y + 105);
            if (!shop) DrawNearby(inventory, player, x + 24, y + 364, pending, request);

            var size = inventory.Size(player.Slot); var bag = inventory.Equipped(player.Slot, EquipmentSlot.Backpack);
            GUI.Label(new Rect(x + 330, y + 104, 300, 30), bag.HasValue ? $"Lv.{bag.Value.Type}  {size.Width} × {size.Height} · {size.Width * size.Height} 格" : "未装备背包 · 0 格", text);
            int cell = size.Width == 0 ? 40 : Math.Min(50, Math.Min(288 / size.Width, 310 / size.Height));
            var area = new Rect(x + 330, y + 142, size.Width * cell, size.Height * cell);
            DrawGrid(inventory, player.Slot, area, cell, pending, request);
            if (size.Width == 0) GUI.Label(new Rect(x + 330, y + 150, 285, 90), "先从地面拾取并穿戴背包，或在商店购买并装备。身上的钓竿仍然保留。", text);
            GUI.Label(new Rect(x + 330, y + 463, 294, 48), placing == 0 ? "选择物品查看操作\n移动：选择目标格，R 旋转" : placing == 1 ? "点击目标格完成移动 · R 旋转" : $"点击空格拆出 {amount} 个材料", small);
            if (placing != 0 && GUI.Button(new Rect(x + 330, y + 495, 294, 24), "取消摆放", button)) placing = 0;
            DrawDetails(state, player, x + 658, y + 104, shop, pending, request);
            GUI.Label(new Rect(x + 24, y + 521, 1050, 28), message ?? "物品只在确认后转移 · Tab / I 关闭背包 · 金币各自所有", small);
        }
        private void DrawShop(int coins, float x, float y)
        {
            GUI.Label(new Rect(x, y, 286, 28), "共享货架", text); y += 37;
            Array.Clear(shopOccupied, 0, shopOccupied.Length);
            for (int row = 0; row < 8; row++) for (int col = 0; col < 8; col++) Fill(new Rect(x + col * 34 + 1, y + row * 34 + 1, 32, 32), new Color(.08f, .17f, .2f));
            if(shelf==null)return;
            for (int n = 0; n < shelf.Stock.Count; n++)
            {
                var stock = shelf.Stock[n]; var def = InventoryCatalog.Get(stock.Kind, stock.Type);
                // Keep sold positions occupied so buying never moves the remaining offers.
                for (int row = stock.Y; row < stock.Y + def.Height; row++)
                    for (int col = stock.X; col < stock.X + def.Width; col++) shopOccupied[col, row] = true;
                var rect = new Rect(x + stock.X * 34 + 2, y + stock.Y * 34 + 2, def.Width * 34 - 4, def.Height * 34 - 4);
                if (DrawItem(rect, stock.Kind, stock.Type, def.Name, stock.Sold ? 0 : 1, itemId == 0 && offer == n, stock.Sold, price: def.Price, coins: coins))
                { offer = n; itemId = 0; placing = 0; dropOld = false; }
            }
            for (int type = 0; type < shelf.FoodStock.Count; type++)
            {
                // A readable display card uses 2 x 2 shelf cells; backpack footprint remains 1 x 1.
                // Fit into the same shelf without changing authoritative stock positions or offer IDs.
                var cell = FindShopSpace();
                var rect = new Rect(x + cell.x * 34 + 2, y + cell.y * 34 + 2, 64, 64);
                var food = FoodCatalog.Get(type); int index = FoodCatalog.OfferOffset + type;
                if (DrawItem(rect, ItemKind.Food, type, food.Name, shelf.FoodStock[type], itemId == 0 && offer == index,
                    shelf.FoodStock[type] == 0, price: food.Price, coins: coins))
                { offer = index; itemId = 0; placing = 0; dropOld = false; }
            }
        }
        private Vector2Int FindShopSpace()
        {
            // Equipment occupies at most the original 8 x 6 area. The two extra rows guarantee
            // space for all three food offers even when no earlier 2 x 2 gap is available.
            for (int row = 0; row < 7; row++) for (int col = 0; col < 7; col++)
            {
                if (shopOccupied[col, row] || shopOccupied[col + 1, row] || shopOccupied[col, row + 1] || shopOccupied[col + 1, row + 1]) continue;
                shopOccupied[col, row] = shopOccupied[col + 1, row] = shopOccupied[col, row + 1] = shopOccupied[col + 1, row + 1] = true;
                return new Vector2Int(col, row);
            }
            throw new InvalidOperationException("The merchant display shelf has no free offer space.");
        }
        private void DrawSlots(InventoryState state, Fisher player, float x, float y)
        {
            GUI.Label(new Rect(x, y, 280, 28), "穿戴装备", text); y += 38;
            var slots = new[] { EquipmentSlot.Hat, EquipmentSlot.RightHand, EquipmentSlot.Clothes, EquipmentSlot.LeftHand, EquipmentSlot.Shoes, EquipmentSlot.Backpack, EquipmentSlot.Accessory, EquipmentSlot.SwimRing };
            var names = new[] { "帽子", "右手", "衣服", "左手", "鞋子", "背包", "饰品", "游泳圈" };
            for (int n = 0; n < slots.Length; n++)
            {
                var equipped = state.Equipped(player.Slot, slots[n]); bool before = GUI.enabled; GUI.enabled = equipped.HasValue;
                var rect = new Rect(x + n % 2 * 140, y + n / 2 * 50, 133, 44);
                if (GUI.Button(rect, new GUIContent("", equipped.HasValue ? equipped.Value.Definition.Name : names[n]), button)) Select(equipped.Value);
                if (equipped.HasValue) icons.Draw(new Rect(rect.x + 5, rect.y + 4, 35, 36), equipped.Value.Kind, equipped.Value.Type);
                GUI.Label(new Rect(rect.x + 46, rect.y + 10, rect.width - 49, 25), names[n], small);
                GUI.enabled = before;
            }
        }
        private void DrawNearby(InventoryState state, Fisher player, float x, float y, bool pending, Func<InventoryCommand, bool> request)
        {
            var ground = GroundPickupSelection.Nearby(state, player);
            int pages = Math.Max(1, (ground.Length + 2) / 3); groundPage = Math.Min(groundPage, pages - 1);
            GUI.Label(new Rect(x, y, 280, 25), $"附近可拾取  {ground.Length}", small);
            for (int n = 0; n < 3 && groundPage * 3 + n < ground.Length; n++)
            {
                var item = ground[groundPage * 3 + n]; bool before = GUI.enabled; GUI.enabled = !pending;
                var command = GroundPickupSelection.Command(state, player, item);
                var row = new Rect(x, y + 29 + n * 34, 280, 29);
                if (GUI.Button(row, new GUIContent("", item.Definition.Name), button)) request(command);
                icons.Draw(new Rect(row.x + 5, row.y + 2, 26, 25), item.Kind, item.Type);
                GUI.Label(new Rect(row.x + 39, row.y + 2, row.width - 44, 25), $"{(command.Action == InventoryAction.PickupAndEquip ? "穿上" : "拾取")}  {item.Definition.Name} ×{item.Count}", small);
                GUI.enabled = before;
            }
            if (pages > 1)
            {
                // 翻页不提交命令，挂起交易期间也可查看，不能用截断列表隐藏后面的背包。
                if (GUI.Button(new Rect(x, y + 134, 72, 24), "上一页", button)) groundPage = (groundPage + pages - 1) % pages;
                GUI.Label(new Rect(x + 88, y + 134, 100, 24), $"{groundPage + 1} / {pages}", small);
                if (GUI.Button(new Rect(x + 208, y + 134, 72, 24), "下一页", button)) groundPage = (groundPage + 1) % pages;
            }
        }

        private void DrawGrid(InventoryState state, int owner, Rect area, int cell, bool pending, Func<InventoryCommand, bool> request)
        {
            var size = state.Size(owner);
            for (int y = 0; y < size.Height; y++) for (int x = 0; x < size.Width; x++) Fill(new Rect(area.x + x * cell + 1, area.y + y * cell + 1, cell - 2, cell - 2), new Color(.075f, .16f, .2f));
            var selected = state.Items.FirstOrDefault(i => i.Id == itemId && i.Owner == owner);
            if (placing != 0 && selected.Id == 0) placing = 0;
            if (placing != 0 && area.Contains(Event.current.mousePosition))
            {
                int x = (int)((Event.current.mousePosition.x - area.x) / cell), y = (int)((Event.current.mousePosition.y - area.y) / cell);
                var preview = new InventorySystem(state);
                var result = placing == 1 ? preview.Move(owner, itemId, x, y, rotated) : preview.Split(owner, itemId, amount, x, y);
                int w = placing == 2 ? 1 : rotated ? selected.Definition.Height : selected.Definition.Width;
                int h = placing == 2 ? 1 : rotated ? selected.Definition.Width : selected.Definition.Height;
                Fill(new Rect(area.x + x * cell, area.y + y * cell, w * cell, h * cell), result == InventoryResult.Success ? new Color(.22f, .7f, .48f, .6f) : new Color(.8f, .25f, .2f, .6f));
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    if (!pending && result == InventoryResult.Success && request(new InventoryCommand(placing == 1 ? InventoryAction.Move : InventoryAction.Split, itemId,
                        amount: placing == 2 ? amount : 0, x: x, y: y, rotated: placing == 1 && rotated))) placing = 0;
                    Event.current.Use();
                }
            }
            foreach (var item in state.Items.Where(i => i.Owner == owner && i.Location == ItemLocation.Backpack))
            {
                var rect = new Rect(area.x + item.X * cell + 2, area.y + item.Y * cell + 2, item.Width * cell - 4, item.Height * cell - 4);
                if (DrawItem(rect, item.Kind, item.Type, item.Definition.Name, item.Count, item.Id == itemId, false, item.Rotated) && placing == 0) Select(item);
            }
        }
        private void Select(InventoryItem item) { itemId = item.Id; offer = -1; placing = 0; amount = 1; rotated = item.Rotated; dropOld = false; }
        private void DrawDetails(HookState state, Fisher player, float x, float y, bool shop, bool pending, Func<InventoryCommand, bool> request)
        {
            var inventory = state.Tackle.Inventory;
            bool buying = shop && shelf!=null && itemId == 0 && offer >= 0 && (offer < shelf.Stock.Count || offer>=FoodCatalog.OfferOffset && offer<FoodCatalog.OfferOffset+shelf.FoodStock.Count);
            var item = inventory.Items.FirstOrDefault(i => i.Id == itemId && i.Owner == player.Slot);
            if (!buying && item.Id == 0) { GUI.Label(new Rect(x, y + 30, 410, 110), "选择物品或商店商品\n查看属性、数量及操作。\n材料与备用装备共用背包格子。", text); return; }
            bool foodOffer=buying && offer>=FoodCatalog.OfferOffset;
            ItemKind kind = buying ? foodOffer?ItemKind.Food:shelf.Stock[offer].Kind : item.Kind; int type = buying ? foodOffer?offer-FoodCatalog.OfferOffset:shelf.Stock[offer].Type : item.Type;
            var definition = InventoryCatalog.Get(kind, type);
            icons.Draw(new Rect(x, y, 52, 52), kind, type);
            GUI.Label(new Rect(x + 64, y, 346, 55), definition.Name, title);
            GUI.Label(new Rect(x, y + 53, 410, 28), $"占格 {definition.Width} × {definition.Height}" + (buying ? $"    售价 {definition.Price:N0} G" : $"    数量 {item.Count}"), text);
            if(kind==ItemKind.Food){DrawFoodDetails(state,player,item,type,buying,x,y,pending,request);return;}
            if (kind == ItemKind.Rod)
            {
                var rod = RodCatalog.Get(type); var current = FishingRules.Equipped(state, player);
                GUI.Label(new Rect(x, y + 86, 410, 59), $"线长 {rod.Range / 1000f:0.#}m ({(rod.Range - (current?.Range ?? 0)) / 1000f:+0.#;-0.#;0}) · 出钩 {rod.Speed / 1000f:0.#}m/s ({(rod.Speed - (current?.Speed ?? 0)) / 1000f:+0.#;-0.#;0})\n耐力 +{rod.StaminaBonus} ({rod.StaminaBonus - (current?.StaminaBonus ?? 0):+0;-0;0}) · 换装不补满", text);
            }
            if (kind == ItemKind.Backpack)
            {
                var bag = InventoryCatalog.Backpack(type); var current = inventory.Size(player.Slot);
                GUI.Label(new Rect(x, y + 86, 410, 59), $"穿戴容量 {bag.Size.Width} × {bag.Size.Height} = {bag.Size.Width * bag.Size.Height} 格\n当前 {current.Width * current.Height} 格（{bag.Size.Width * bag.Size.Height - current.Width * current.Height:+0;-0;0}） · 空包不扩容", text);
            }
            if (kind == ItemKind.SwimRing)
            {
                var ring = SwimRingCatalog.Get(type);var config=state.Tackle.Config.Swimming;
                GUI.Label(new Rect(x, y + 86, 410, 59), $"Lv{ring.Level}  ·  游速 {SwimmingSystem.Speed(config,ring)/1000f:0.##} 米/秒\n游泳消耗 {SwimmingSystem.DrainPerTick(config,ring)/1000f:0.##}/秒 · 免落水掉血 · 可出竿", text);
            }
            if(kind==ItemKind.Weapon)
            {
                var weapon=WeaponCatalog.Get(type);
                GUI.Label(new Rect(x,y+86,410,59),$"伤害 {weapon.Damage} · 范围 {weapon.Range/1000f:0.#} 米\n间隔 {weapon.CooldownTicks/30f:0.#} 秒 · 耐力消耗 {weapon.StaminaCost}",text);
            }
            if (kind == ItemKind.Apparel)
            {
                GUI.Label(new Rect(x, y + 86, 410, 59), ApparelDisplay.Bonus(definition.Modifiers) + "\n" +
                    ApparelDisplay.Comparison(state.Tackle.Config.Character, state.Tackle.Config.StaminaBase, inventory, player.Slot, definition), text);
            }
            bool enabled = GUI.enabled; GUI.enabled = !pending;
            if (!buying && InventoryCatalog.IsMaterial(kind))
            {
                amount = Mathf.Clamp(amount, 1, item.Count);
                GUI.Label(new Rect(x, y + 96, 410, 28), $"数量：{amount} / {item.Count}", text);
                amount = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x, y + 133, 395, 20), amount, 1, item.Count));
                if (GUI.Button(new Rect(x, y + 175, 198, 36), "移动整堆", button)) { placing = 1; rotated = false; }
                GUI.enabled = !pending && amount < item.Count;
                if (GUI.Button(new Rect(x + 210, y + 175, 198, 36), "拆分到空格", button)) placing = 2;
                GUI.enabled = !pending && !player.Swimming.InWater;
                if (GUI.Button(new Rect(x, y + 224, 408, 36), $"放到平台上 ×{amount}", button)) request(new InventoryCommand(InventoryAction.Drop, itemId, amount: amount));
                GUI.Label(new Rect(x, y + 282, 410, 85), $"每格最多 {InventoryCatalog.MaterialStackLimit} 个。拖到同类堆叠可合并。平台材料包没有自动销毁时间，队友可以主动拾取。", small);
                GUI.enabled = enabled; return;
            }
            bool equipped = !buying && item.Location == ItemLocation.Equipped;
            if (player.Swimming.InWater) dropOld=false;
            if (!equipped && !player.Swimming.InWater) dropOld = GUI.Toggle(new Rect(x, y + 150, 408, 28), dropOld, "明确将替换下来的旧装备放到平台", text);
            InventoryResult previewResult = InventoryResult.Success; InventoryState previewState = null;
            if (!equipped)
            {
                var preview = equipmentPreview.Evaluate(inventory, player.Slot, player.Coins, new Point(player.X, player.Z), kind, type, itemId, buying, dropOld);
                previewResult = preview.Result; previewState = preview.State;
            }
            bool busyRod = definition.Slot==EquipmentSlot.RightHand && (player.Phase != HookPhase.Ready || player.Melee.Active || player.PulledBy >= 0);
            GUI.enabled = !pending && !busyRod && (!buying || !shelf.Stock[offer].Sold);
            if (buying)
            {
                if (GUI.Button(new Rect(x, y + 190, 408, 37), shelf.Stock[offer].Sold ? "已被买走" : $"购买并装备 · {definition.Price} G", button))
                    request(selection.Command(true,dropOld));
                if (GUI.Button(new Rect(x, y + 237, 408, 33), "购买并放入背包", button)) request(selection.Command(false));
            }
            else if (equipped)
            {
                string label = kind == ItemKind.Backpack ? "清空后卸下 · 放到平台上" : "卸下装备 · 放回背包";
                GUI.enabled &= !(player.Swimming.InWater && kind == ItemKind.Backpack);
                if (GUI.Button(new Rect(x, y + 190, 408, 37), label, button)) request(new InventoryCommand(InventoryAction.Unequip, (int)definition.Slot));
            }
            else
            {
                if (GUI.Button(new Rect(x, y + 190, 408, 37), "穿戴这件装备", button)) request(new InventoryCommand(InventoryAction.Equip, itemId, dropOld: dropOld));
                GUI.enabled = !pending;
                if (GUI.Button(new Rect(x, y + 237, 198, 33), "移动 / R 旋转", button)) { placing = 1; rotated = item.Rotated; }
                GUI.enabled = !pending && !player.Swimming.InWater;
                if (GUI.Button(new Rect(x + 210, y + 237, 198, 33), "放到平台上", button)) request(new InventoryCommand(InventoryAction.Drop, itemId, amount: 1));
            }
            GUI.enabled = enabled;
            if (busyRod) GUI.Label(new Rect(x, y + 281, 408, 30), "请先结束挥击或收回钓竿再换装", small);
            else if (!equipped) GUI.Label(new Rect(x, y + 281, 408, 46), previewResult == InventoryResult.Success ? "换装预览：原有物品均可保留" : "换装预览：" + FishingEquipmentController.Explain(previewResult), small);
            if (kind == ItemKind.Backpack && previewState != null) DrawPreview(previewState, player.Slot, x, y + 316);
            if(!buying)DrawQuickBindings(inventory,player.Slot,item,x,y+372,pending,request);
        }
        private static void DrawPreview(InventoryState state, int owner, float x, float y)
        {
            var size = state.Size(owner); const int cell = 5;
            Fill(new Rect(x, y, size.Width * cell, size.Height * cell), new Color(.08f, .2f, .23f));
            foreach (var item in state.Items.Where(i => i.Owner == owner && i.Location == ItemLocation.Backpack))
                Fill(new Rect(x + item.X * cell + 1, y + item.Y * cell + 1, item.Width * cell - 2, item.Height * cell - 2), ColorFor(item.Kind));
        }
        private static Color ColorFor(ItemKind kind) => kind == ItemKind.Apparel ? new Color(.35f,.37f,.55f) : kind == ItemKind.SwimRing ? new Color(.68f,.38f,.15f) : kind == ItemKind.Wood ? new Color(.48f, .3f, .16f) : kind == ItemKind.Fishbone ? new Color(.65f, .68f, .56f) : kind == ItemKind.Backpack ? new Color(.24f, .42f, .39f) : new Color(.27f, .34f, .48f);
        private bool DrawItem(Rect rect, ItemKind kind, int type, string name, int count, bool selected, bool sold, bool rotated = false, int? price = null, int coins = 0)
        {
            var rim = selected ? accent : ItemIconPainter.Accent(kind, type);
            Fill(rect, rim * new Color(1, 1, 1, selected ? 1 : .45f));
            Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), selected ? new Color(.12f, .28f, .29f) : new Color(.06f, .14f, .18f));
            var priceTag = new Rect(); GUIContent priceContent = null;
            if (price.HasValue)
            {
                priceContent = new GUIContent(price.Value.ToString()); priceText.fontSize = 15;
                while (priceText.fontSize > 11 && priceText.CalcSize(priceContent).x > rect.width - 4) priceText.fontSize--;
                float priceWidth = Mathf.Min(rect.width - 4, priceText.CalcSize(priceContent).x);
                priceTag = new Rect(rect.xMax - priceWidth - 2, rect.y + 2, priceWidth, 19);
            }
            float top = price.HasValue ? 23 : 3;
            icons.Draw(new Rect(rect.x + 3, rect.y + top, rect.width - 6, rect.height - top - 4), kind, type, rotated);
            if (sold)
            {
                Fill(rect, new Color(0, 0, 0, .62f));
                GUI.Label(new Rect(rect.x, rect.center.y - 12, rect.width, 24), "售罄", small);
            }
            if (count != 1)
            {
                var tag = new Rect(rect.x + 3, rect.yMax - 22, rect.width - 6, 20);
                DrawItemNumber(tag, new GUIContent(count.ToString()), quantity, Color.white);
            }
            if (price.HasValue)
            {
                var color = coins >= price.Value ? new Color(.62f, 1, .62f) : new Color(1, .57f, .47f);
                DrawItemNumber(priceTag, priceContent, priceText, color);
            }
            return GUI.Button(rect, new GUIContent("", price.HasValue ? $"{name} · {price.Value:N0} G" : name), GUIStyle.none);
        }
        private static void DrawItemNumber(Rect rect, GUIContent content, GUIStyle style, Color color)
        {
            // Rasterize at the displayed pixel size instead of scaling already-small glyphs.
            var matrix = GUI.matrix; int size = style.fontSize; var previousColor = style.normal.textColor;
            var position = matrix.MultiplyPoint3x4(new Vector3(rect.x, rect.y, 0));
            var end = matrix.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMax, 0));
            var pixels = Rect.MinMaxRect(Mathf.Round(position.x), Mathf.Round(position.y), Mathf.Round(end.x), Mathf.Round(end.y));
            try
            {
                GUI.matrix = Matrix4x4.identity;
                style.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * Mathf.Abs(matrix.m11)));
                while (style.fontSize > 1 && style.CalcSize(content).x > pixels.width) style.fontSize--;
                style.normal.textColor = HudPainter.DisplayColor(new Color(.025f, .06f, .07f, .95f));
                // A one-pixel glyph outline leaves the space around the digits transparent.
                GUI.Label(new Rect(pixels.x - 1, pixels.y, pixels.width, pixels.height), content, style);
                GUI.Label(new Rect(pixels.x + 1, pixels.y, pixels.width, pixels.height), content, style);
                GUI.Label(new Rect(pixels.x, pixels.y - 1, pixels.width, pixels.height), content, style);
                GUI.Label(new Rect(pixels.x, pixels.y + 1, pixels.width, pixels.height), content, style);
                style.normal.textColor = HudPainter.DisplayColor(color);
                GUI.Label(pixels, content, style);
            }
            finally { GUI.matrix = matrix; style.fontSize = size; style.normal.textColor = previousColor; }
        }
    }
}
