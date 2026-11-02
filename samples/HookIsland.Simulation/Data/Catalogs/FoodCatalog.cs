using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed class FoodDefinition
{
    internal FoodDefinition(string[] cells)
    {
        Id=cells[0];Name=cells[1];Health=Parse(cells[2]);Price=Parse(cells[3]);StackLimit=Parse(cells[4]);Stock=Parse(cells[5]);
        if(Health<1 || Health>10000 || Price<1 || StackLimit<1 || StackLimit>99 || Stock<1 || Stock>99)
            throw new InvalidDataException("Invalid food configuration.");
    }
    private static int Parse(string value)=>int.Parse(value,CultureInfo.InvariantCulture);
    public string Id {get;}
    public string Name {get;}
    public int Health {get;}
    public int Price {get;}
    public int StackLimit {get;}
    public int Stock {get;}
}

public static class FoodCatalog
{
    // Separate shelf: ordinary equipment retains indices 0–7.
    public const int OfferOffset=8;
    public static IReadOnlyList<FoodDefinition> All {get;}=Array.AsReadOnly(CatalogData.Rows("foods.csv").Select(c=>new FoodDefinition(c)).ToArray());
    public static FoodDefinition Get(int type)=>type>=0 && type<All.Count?All[type]:throw new ArgumentOutOfRangeException(nameof(type));
}
