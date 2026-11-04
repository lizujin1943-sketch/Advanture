using System.Globalization;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>首种投掷物的固定规则；从共享 CSV 加载，配置内容进入兼容哈希。</summary>
public sealed class GrenadeConfig
{
    public static GrenadeConfig Default {get;}=new GrenadeConfig(CatalogData.Rows("grenades.csv").Single());
    private GrenadeConfig(string[] row)
    {
        int N(int i)=>int.Parse(row[i],CultureInfo.InvariantCulture);
        MinRange=N(1);MaxRange=N(2);Scatter=N(3);MinFlightTicks=N(4);MaxFlightTicks=N(5);
        MinArc=N(6);MaxArc=N(7);FuseTicks=N(8);Radius=N(9);FullDamageRadius=N(10);Damage=N(11);EdgeDamage=N(12);WindupTicks=N(13);ActionTicks=N(14);
        if(MinRange<1 || MaxRange>30000 || MinRange>=MaxRange || Scatter<0 || Scatter>1000 || MinFlightTicks<1 || MaxFlightTicks<MinFlightTicks || MaxFlightTicks>120 || MinArc<0 || MaxArc<MinArc || MaxArc>10000 || FuseTicks<1 || FuseTicks>300 || Radius<1 || Radius>10000 || FullDamageRadius<0 || FullDamageRadius>=Radius || Damage<EdgeDamage || EdgeDamage<1 || WindupTicks<1 || ActionTicks<=WindupTicks || ActionTicks>90)
            throw new InvalidDataException("Invalid grenade configuration.");
    }
    public int MinRange {get;} public int MaxRange {get;} public int Scatter {get;}
    public int MinFlightTicks {get;} public int MaxFlightTicks {get;}
    public int MinArc {get;} public int MaxArc {get;} public int FuseTicks {get;}
    public int Radius {get;} public int FullDamageRadius {get;} public int Damage {get;} public int EdgeDamage {get;}
    public int WindupTicks {get;} public int ActionTicks {get;}
}
