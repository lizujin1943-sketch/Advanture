using System;

namespace Advanture.HookIsland.Simulation;

public enum HookUpgrade : byte { None, Range, Speed }
public enum PurchaseResult : byte { None, Success, TooFar, InsufficientCoins, MaxLevel, HookBusy }

/// <summary>Shared shop rules. Costs and hook stats are identical in simulation and UI.</summary>
public static class HookUpgrades
{
    public const int MaxLevel=3;
    public const int ShopX=0,ShopZ=1050,VisitRadius=4200;
    public static bool IsNearShop(int x,int z)=>
        IslandMap.DistanceSquared(x,z,ShopX,ShopZ)<=(long)VisitRadius*VisitRadius;
    public static int Range(int level) { ValidateLevel(level);return 8000+level*4000; }
    public static int SpeedPerTick(int level) { ValidateLevel(level);return 300+level*100; }
    public const int ReturnPerTick=400;
    public static int Cost(int level)
    {
        ValidateLevel(level);
        return level==0?50:level==1?100:level==2?180:0;
    }
    public static int TotalSpent(int rangeLevel,int speedLevel)=>Spent(rangeLevel)+Spent(speedLevel);
    public static PurchaseResult CheckPurchase(Fisher player,int coins,HookUpgrade upgrade)
    {
        if(upgrade!=HookUpgrade.Range && upgrade!=HookUpgrade.Speed)throw new ArgumentOutOfRangeException(nameof(upgrade));
        if(!IsNearShop(player.X,player.Z))return PurchaseResult.TooFar;
        if(player.Phase!=HookPhase.Ready || player.PulledBy>=0)return PurchaseResult.HookBusy;
        int level=upgrade==HookUpgrade.Range?player.RangeLevel:player.SpeedLevel;
        if(level>=MaxLevel)return PurchaseResult.MaxLevel;
        return coins>=Cost(level)?PurchaseResult.Success:PurchaseResult.InsufficientCoins;
    }
    private static int Spent(int level)
    {
        ValidateLevel(level);
        return level==0?0:level==1?50:level==2?150:330;
    }
    private static void ValidateLevel(int level)
    { if(level<0 || level>MaxLevel)throw new ArgumentOutOfRangeException(nameof(level)); }
}
