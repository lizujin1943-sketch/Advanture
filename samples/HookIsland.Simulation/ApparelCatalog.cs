using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Wearable content supplies the same modifiers as tools; no per-slot stat authority.</summary>
public sealed class ApparelDefinition
{
    internal ApparelDefinition(string[] cells)
    {
        if (cells.Length != 9 || string.IsNullOrWhiteSpace(cells[0]) || string.IsNullOrWhiteSpace(cells[1]) ||
            !Enum.TryParse(cells[2], out EquipmentSlot slot) ||
            (slot != EquipmentSlot.Hat && slot != EquipmentSlot.Clothes && slot != EquipmentSlot.Shoes))
            throw new InvalidDataException("Invalid apparel definition.");
        Id = cells[0]; int width = Parse(cells[3]), height = Parse(cells[4]), price = Parse(cells[5]);
        int health = Parse(cells[6]), speed = Parse(cells[7]), armor = Parse(cells[8]);
        if (width < 1 || width > 8 || height < 1 || height > 6 || price < 1 || price > 1000000 ||
            health < 0 || health > 10000 || speed < 0 || speed > CharacterConfig.MaximumSpeed || armor < 0 || armor > 10000)
            throw new InvalidDataException("Invalid apparel values.");
        Item = new InventoryDefinition(cells[1], width, height, 1, slot, price,
            new CharacterModifier(health: health, moveSpeed: speed, armor: armor));
    }
    private static int Parse(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    public string Id { get; }
    public InventoryDefinition Item { get; }
}

public static class ApparelCatalog
{
    public static IReadOnlyList<ApparelDefinition> All { get; } = Load();
    private static IReadOnlyList<ApparelDefinition> Load()
    {
        var items = CatalogData.Rows("apparel.csv").Select(row => new ApparelDefinition(row)).ToArray();
        if (items.Length == 0 || items.Select(item => item.Id).Distinct().Count() != items.Length)
            throw new InvalidDataException("Empty or duplicate apparel catalog.");
        return Array.AsReadOnly(items);
    }
    public static ApparelDefinition Get(int type) => type >= 0 && type < All.Count ? All[type] :
        throw new ArgumentOutOfRangeException(nameof(type));
}
