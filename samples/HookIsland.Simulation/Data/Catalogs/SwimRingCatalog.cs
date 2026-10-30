using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed class SwimRingDefinition
{
    internal SwimRingDefinition(string[] cells)
    {
        if(cells.Length!=7)throw new InvalidDataException("Invalid swim ring columns.");
        Id=cells[0];Name=cells[1];Level=Parse(cells[2]);SpeedPermille=Parse(cells[3]);
        StaminaPermille=Parse(cells[4]);Price=Parse(cells[5]);Tier=Parse(cells[6]);
        if(Level<1 || Level>10 || SpeedPermille<=1000 || SpeedPermille>3000 || StaminaPermille<1 || StaminaPermille>=1000 ||
            Price<1 || Tier<1 || Tier>5)throw new InvalidDataException("Invalid swim ring values.");
    }
    private static int Parse(string value)=>int.Parse(value,CultureInfo.InvariantCulture);
    public string Id { get; }
    public string Name { get; }
    public int Level { get; }
    public int SpeedPermille { get; }
    public int StaminaPermille { get; }
    public int Price { get; }
    public int Tier { get; }
}

public static class SwimRingCatalog
{
    public static IReadOnlyList<SwimRingDefinition> All { get; }=Load();
    private static IReadOnlyList<SwimRingDefinition> Load()
    {
        var all=CatalogData.Rows("swim-rings.csv").Select(c=>new SwimRingDefinition(c)).ToArray();
        if(all.Length!=10 || all.Where((r,i)=>r.Level!=i+1).Any() || all.Select(r=>r.Id).Distinct().Count()!=10)
            throw new InvalidDataException("Invalid swim ring levels.");
        return Array.AsReadOnly(all);
    }
    public static SwimRingDefinition Get(int level)=>level>=1 && level<=All.Count?All[level-1]:throw new ArgumentOutOfRangeException(nameof(level));
    // Only the equipped slot contributes; a spare item in the bag has no water effects.
    public static SwimRingDefinition? Equipped(InventoryState inventory,int owner)
    {
        var item=inventory.Equipped(owner,EquipmentSlot.SwimRing);
        return item.HasValue?Get(item.Value.Type):null;
    }
}
