using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Advanture.HookIsland.Simulation;

public sealed class RodDefinition
{
    internal RodDefinition(string[] c)
    { Id=c[0];Name=c[1];Tier=int.Parse(c[2],CultureInfo.InvariantCulture);Range=int.Parse(c[3]);Speed=int.Parse(c[4]);StaminaBonus=int.Parse(c[5]);Price=int.Parse(c[6]);Width=int.Parse(c[7]);Height=int.Parse(c[8]); }
    public string Id { get; }
    public string Name { get; }
    public int Tier { get; }
    public int Range { get; }
    public int Speed { get; }
    public int StaminaBonus { get; }
    public int Price { get; }
    public int Width { get; }
    public int Height { get; }
}
public static class RodCatalog
{
    public static IReadOnlyList<RodDefinition> All { get; }=Array.AsReadOnly(CatalogData.Rows("rods.csv").Select(c=>new RodDefinition(c)).ToArray());
    public static RodDefinition Get(int type)=>type>=0 && type<All.Count?All[type]:throw new ArgumentOutOfRangeException(nameof(type));
}
public enum FishRarity : byte { Common, Uncommon, Rare, Epic, Legendary }
public sealed class FishDefinition
{
    internal FishDefinition(string[] c)
    {
        Id=c[0];Name=c[1];Weight=int.Parse(c[2]);Price=int.Parse(c[3]);EscapeMilli=int.Parse(c[4]);
        if(c.Length!=6 || !Enum.TryParse<FishRarity>(c[5],out var rarity) || !Enum.IsDefined(typeof(FishRarity),rarity))
            throw new InvalidDataException("Invalid species rarity.");
        Rarity=rarity;
    }
    public string Id { get; }
    public string Name { get; }
    public int Weight { get; }
    public int Price { get; }
    public int EscapeMilli { get; }
    public FishRarity Rarity { get; }
}
public static class FishCatalog
{
    public static IReadOnlyList<FishDefinition> All { get; }=Array.AsReadOnly(CatalogData.Rows("fish.csv").Select(c=>new FishDefinition(c)).ToArray());
    public static int Coins(Fish fish)=>Coins(All[fish.Species].Price,fish.WeightGrams,fish.Grade);
    public static int Bones(Fish fish)=>Bones(fish.WeightGrams,fish.Grade);
    public static int Coins(int pricePerKg,int weightGrams,int grade)=>pricePerKg*weightGrams/1000*grade;
    public static int Bones(int weightGrams,int grade)=>(weightGrams+999)/1000*2*grade;
    // 30,000 units per point makes 30 Hz costs exact for 100 g weights and the configured K values.
    public static int DrainPerTick(Fish fish)=>All[fish.Species].EscapeMilli*fish.WeightGrams*fish.Grade/1000;
    public static Fish Spawn(int id,Point home,int delay,uint generation,int seed)
    {
        var f=FishMotion.Create(id,home,delay);f.Generation=generation;
        uint random=Mix(unchecked((uint)seed+(uint)id*747796405u+generation*2891336453u));
        int roll=(int)(random%10000),species=0;
        while(species<All.Count-1 && roll>=All[species].Weight){roll-=All[species].Weight;species++;}
        random=Mix(random);f.Species=species;f.WeightGrams=1000+(int)(random%91)*100;
        random=Mix(random);int grade=(int)(random%100);f.Grade=grade<90?1:grade<99?2:3;
        // Every third near-shore respawn supplies an ordinary light fish, keeping the starter loop viable.
        if(id%3==0 && generation%3==0){f.Species=id%2;f.WeightGrams=1000+(int)(random%21)*100;f.Grade=1;}
        return f;
    }
    internal static uint Mix(uint x){unchecked{x^=x>>16;x*=0x7feb352d;x^=x>>15;x*=0x846ca68b;x^=x>>16;return x;}}
}
internal static class CatalogData
{
    private static string Read(string file)
    {
        var assembly=typeof(CatalogData).Assembly;
        var name=assembly.GetManifestResourceNames().Single(n=>n.EndsWith("."+file,StringComparison.Ordinal));
        using var stream=assembly.GetManifestResourceStream(name)!;using var reader=new StreamReader(stream,Encoding.UTF8);return reader.ReadToEnd();
    }
    internal static IEnumerable<string[]> Rows(string file)=>Read(file).Split('\n').Skip(1).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Trim().Split(','));
    internal static string Hash()
    {using var sha=SHA256.Create();return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(Read("rods.csv")+Read("fish.csv")+Read("backpacks.csv")+Read("swim-rings.csv")+Read("apparel.csv")+Read("weapons.csv")+Read("foods.csv"))));}
}
