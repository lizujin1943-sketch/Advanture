using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>已实现且能进入库存的物品类别；未实现的装备不生成占位商品。</summary>
public enum ItemKind : byte { Wood = 1, Fishbone, Rod, Backpack, SwimRing, Apparel }

/// <summary>角色的八个独立装备槽。只有明确实现的物品类型能占用对应槽。</summary>
public enum EquipmentSlot : byte { None, Hat, LeftHand, RightHand, Clothes, Shoes, Backpack, Accessory, SwimRing }

/// <summary>网格列数与行数；未穿戴背包时两者都为零。</summary>
public readonly struct InventorySize
{
    public InventorySize(int width, int height) { Width = width; Height = height; }
    public int Width { get; }
    public int Height { get; }
}

/// <summary>空背包自身占 2×2；此配置的 Size 是穿戴后才提供的容量。</summary>
public sealed class BackpackDefinition
{
    internal BackpackDefinition(string[] cells)
    {
        Id = cells[0]; Name = cells[1]; Level = Parse(cells[2]);
        Size = new InventorySize(Parse(cells[3]), Parse(cells[4])); Price = Parse(cells[5]); Tier = Parse(cells[6]);
        if (Size.Width < 1 || Size.Width > 8 || Size.Height < 1 || Size.Height > 9 || Price <= 0 || Tier < 1 || Tier > 5)
            throw new InvalidDataException("Invalid backpack catalog.");
    }
    private static int Parse(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    public string Id { get; }
    public string Name { get; }
    public int Level { get; }
    public InventorySize Size { get; }
    public int Price { get; }
    public int Tier { get; }
}

/// <summary>物品的静态规则。运行时实例只保存类型，不重复保存名称、尺寸与价格。</summary>
public readonly struct InventoryDefinition
{
    internal InventoryDefinition(string name, int width, int height, int stackLimit, EquipmentSlot slot, int price, CharacterModifier modifiers = default)
    { Name = name; Width = width; Height = height; StackLimit = stackLimit; Slot = slot; Price = price; Modifiers = modifiers; }
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public int StackLimit { get; }
    public EquipmentSlot Slot { get; }
    public int Price { get; }
    /// <summary>穿戴后才生效的属性加成；仅入包或落地不增加属性。</summary>
    public CharacterModifier Modifiers { get; }
}

/// <summary>库存规则入口；钓竿继续使用原钓竿配置，不在背包里维护第二份属性。</summary>
public static class InventoryCatalog
{
    public const int MaterialStackLimit = 20, PickupDistance = 2000;
    // 预留足够的材料包空间：512 地块最多两种材料包，再加四人网格和装备。
    public const int MaxEquipmentItems = 1024, MaxItems = 4096;
    public static IReadOnlyList<BackpackDefinition> Backpacks { get; } =
        Array.AsReadOnly(CatalogData.Rows("backpacks.csv").Select(c => new BackpackDefinition(c)).ToArray());

    public static BackpackDefinition Backpack(int level) => level >= 1 && level <= Backpacks.Count
        ? Backpacks[level - 1] : throw new ArgumentOutOfRangeException(nameof(level));
    public static bool IsMaterial(ItemKind kind) => kind == ItemKind.Wood || kind == ItemKind.Fishbone;
    public static bool IsValid(ItemKind kind, int type) => kind switch {
        ItemKind.Wood or ItemKind.Fishbone => type == 0,
        ItemKind.Rod => type >= 0 && type < RodCatalog.All.Count,
        ItemKind.Backpack => type >= 1 && type <= Backpacks.Count,
        ItemKind.SwimRing => type >= 1 && type <= SwimRingCatalog.All.Count,
        ItemKind.Apparel => type >= 0 && type < ApparelCatalog.All.Count,
        _ => false
    };
    public static InventoryDefinition Get(ItemKind kind, int type)
    {
        if (!IsValid(kind, type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (IsMaterial(kind)) return new InventoryDefinition(kind == ItemKind.Wood ? "木材" : "鱼骨", 1, 1, MaterialStackLimit, EquipmentSlot.None, 0);
        if (kind == ItemKind.Backpack) return new InventoryDefinition(Backpack(type).Name, 2, 2, 1, EquipmentSlot.Backpack, Backpack(type).Price);
        if (kind == ItemKind.SwimRing) return new InventoryDefinition(SwimRingCatalog.Get(type).Name, 2, 2, 1, EquipmentSlot.SwimRing, SwimRingCatalog.Get(type).Price);
        if (kind == ItemKind.Apparel) return ApparelCatalog.Get(type).Item;
        var rod = RodCatalog.Get(type);
        return new InventoryDefinition(rod.Name, rod.Width, rod.Height, 1, EquipmentSlot.RightHand, rod.Price, new CharacterModifier(stamina: rod.StaminaBonus));
    }
}
