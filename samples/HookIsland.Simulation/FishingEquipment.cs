using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed class FishingConfig
{
    public FishingConfig(int staminaBase=100,int regenDelayTicks=60,int regenMilliPerSecond=5000,int reelSpeed=2000,
        int merchantLevel=1,int restockSeconds=600,int seed=7381,CharacterConfig? character=null,SwimmingConfig? swimming=null,EnemyConfig? enemies=null,RevivalConfig? revival=null,int cooperativeSpeedBonusPermille=500,TreasureConfig? treasure=null,CommissionConfig? commissions=null,OceanConfig? ocean=null,FinaleConfig? finale=null,MerchantConfig? merchants=null)
    {
        if(staminaBase<1 || staminaBase>10000 || regenDelayTicks<0 || regenDelayTicks>18000 ||
            regenMilliPerSecond<1 || regenMilliPerSecond>1000000 || reelSpeed<300 || reelSpeed>30000 ||
            merchantLevel<1 || merchantLevel>5 || restockSeconds<1 || restockSeconds>86400 || seed<0 || cooperativeSpeedBonusPermille<0 || cooperativeSpeedBonusPermille>3000)
            throw new InvalidDataException("Invalid fishing configuration.");
        Character=character??new CharacterConfig();Swimming=swimming??new SwimmingConfig();Revival=revival??new RevivalConfig();
        Enemies=enemies??EnemyConfig.Disabled;Treasure=treasure??TreasureConfig.Disabled;Commissions=commissions??CommissionConfig.Disabled;
        Ocean=ocean??OceanConfig.Disabled;
        Finale=finale??FinaleConfig.Disabled;Merchants=merchants??MerchantConfig.Empty;
        if(Ocean.Enabled && (!Commissions.Enabled || Ocean.Unlocks.Max()>Commissions.Definitions.Count))
            throw new InvalidDataException("Ocean progression requires reachable commissions.");
        if(Finale.Enabled && (!Commissions.Enabled || Commissions.Definitions.Sum(d=>d.Bait)<Finale.BaitCost))
            throw new InvalidDataException("Finale requires enough commission bait.");
        StaminaBase=staminaBase;RegenDelayTicks=regenDelayTicks;RegenMilliPerSecond=regenMilliPerSecond;
        ReelSpeed=reelSpeed;MerchantLevel=merchantLevel;RestockSeconds=restockSeconds;Seed=seed;
        CooperativeSpeedBonusPermille=cooperativeSpeedBonusPermille;
    }
    public CharacterConfig Character { get; }
    public SwimmingConfig Swimming { get; }
    public RevivalConfig Revival { get; }
    public EnemyConfig Enemies { get; }
    public TreasureConfig Treasure { get; }
    public CommissionConfig Commissions { get; }
    public OceanConfig Ocean {get;}
    public FinaleConfig Finale {get;}
    public MerchantConfig Merchants {get;}
    public int StaminaBase { get; }
    public int RegenDelayTicks { get; }
    public int RegenMilliPerSecond { get; }
    public int ReelSpeed { get; }
    public int CooperativeSpeedBonusPermille { get; }
    public int MerchantLevel { get; }
    public int RestockSeconds { get; }
    public int Seed { get; }
    internal void Write(BinaryWriter w)
    {w.Write(StaminaBase);w.Write(RegenDelayTicks);w.Write(RegenMilliPerSecond);w.Write(ReelSpeed);w.Write(MerchantLevel);w.Write(RestockSeconds);w.Write(Seed);Character.Write(w);Swimming.Write(w);Enemies.Write(w);Revival.Write(w);w.Write(CooperativeSpeedBonusPermille);Treasure.Write(w);Commissions.Write(w);Ocean.Write(w);Finale.Write(w);Merchants.Write(w);w.Write(CatalogData.Hash());}
    internal static FishingConfig Read(BinaryReader r)
    {
        var c=new FishingConfig(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),CharacterConfig.Read(r),SwimmingConfig.Read(r),EnemyConfig.Read(r),RevivalConfig.Read(r),r.ReadInt32(),TreasureConfig.Read(r),CommissionConfig.Read(r),OceanConfig.Read(r),FinaleConfig.Read(r),MerchantConfig.Read(r));
        if(r.ReadString()!=CatalogData.Hash())throw new InvalidDataException("Fishing catalog version mismatch.");return c;
    }
    public byte[] Encode(){using var s=new MemoryStream();using var w=new BinaryWriter(s);Write(w);return s.ToArray();}
}
public enum RodAction : byte { None, Buy, BuyAndEquip, Equip, Unequip }
public enum RodResult : byte { None, Success, Disabled, TooFar, HookBusy, StaleStock, SoldOut, InsufficientCoins, NoSpace, NotOwned }
public struct RodItem
{
    public int Id,Owner,Type,X,Y;
    public bool Rotated;
    public int Width=>Rotated?RodCatalog.Get(Type).Height:RodCatalog.Get(Type).Width;
    public int Height=>Rotated?RodCatalog.Get(Type).Width:RodCatalog.Get(Type).Height;
}
public struct RodStock
{
    public ItemKind Kind; // 货架可以陈列钓竿、背包、游泳圈或衣装；尺寸和价格取同一物品配置。
    public int Type,X,Y;
    public bool Sold;
}
public sealed class TackleState
{
    public TackleState(FishingConfig config,InventoryState inventory,IEnumerable<RodStock> stock,uint batch,ulong nextRestock)
        :this(config,inventory,new[]{new MerchantState(0,true,0,batch,nextRestock,stock)})
    {
        if(config.Merchants.Definitions.Count!=0)throw new InvalidDataException("Offshore shelves require the full merchant state.");
    }
    public TackleState(FishingConfig config,InventoryState inventory,IEnumerable<MerchantState> merchants)
    {
        Config=config;Inventory=inventory;Merchants=Array.AsReadOnly(merchants.ToArray());
        if(Merchants.Count==0)throw new InvalidDataException("Missing primary merchant.");
        // Equipment identity and material quantities belong only to Inventory.
        Items=Array.AsReadOnly(inventory.Items.Where(i=>i.Kind==ItemKind.Rod).Select(i=>new RodItem
            {Id=i.Id,Owner=i.Owner,Type=i.Type,X=i.X,Y=i.Y,Rotated=i.Rotated}).ToArray());
    }
    public FishingConfig Config { get; }
    public InventoryState Inventory { get; }
    public IReadOnlyList<MerchantState> Merchants {get;}
    public IReadOnlyList<RodItem> Items { get; }
    public IReadOnlyList<RodStock> Stock=>Merchants[0].Stock;
    public uint Batch=>Merchants[0].Batch;
    public ulong NextRestock=>Merchants[0].NextRestock;
    public int NextId=>Inventory.NextId;
}

public static class FishingRules
{
    public const int UnitsPerPoint=StaminaSystem.UnitsPerPoint, BagWidth=4,BagHeight=4;
    public static RodDefinition? Equipped(HookState s,Fisher p)=>s.Tackle==null || p.EquippedRod==0?null:
        RodCatalog.Get(s.Tackle.Items.First(i=>i.Id==p.EquippedRod && i.Owner==p.Slot).Type);
    public static int Range(IslandMap map,HookState s,Fisher p)=>s.Tackle==null?map.HookRange(p.RangeLevel):Equipped(s,p)?.Range??0;
    public static int Speed(HookState s,Fisher p)=>s.Tackle==null?HookUpgrades.SpeedPerTick(p.SpeedLevel)*HookGame.Rate:Equipped(s,p)?.Speed??0;
    public static CharacterStats Attributes(HookState s,Fisher p)=>CharacterEquipment.Calculate(s.Tackle!.Config.Character,s.Tackle.Config.StaminaBase,s.Tackle.Inventory,p.Slot);
    public static int MaxStamina(HookState s,Fisher p)=>Attributes(s,p).StaminaMax;
    public static bool NearShop(IslandMap map,IReadOnlyList<PlatformTile> tiles,Fisher p)=>map.Fishing!=null?
        MerchantRules.Nearest(map,tiles,p)>=0:HookUpgrades.IsNearShop(p.X,p.Z);
    public static int Travel(int speed,ref int remainder)
    {int total=speed+remainder;remainder=total%HookGame.Rate;return total/HookGame.Rate;}
}
