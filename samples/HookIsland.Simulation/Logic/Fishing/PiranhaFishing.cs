namespace Advanture.HookIsland.Simulation;

/// <summary>Ordinary-grade fish economics and stamina, shared with the normal catch settlement.</summary>
public static class PiranhaFishing
{
    public static int Coins(EnemyConfig c,Piranha f)=>FishCatalog.Coins(c.PiranhaCoinsPerKg,f.WeightGrams,1);
    public static int Bones(Piranha f)=>FishCatalog.Bones(f.WeightGrams,1);
    public static int DrainPerTick(EnemyConfig c,Piranha f)=>c.PiranhaEscapeMilli*f.WeightGrams/1000;
}
