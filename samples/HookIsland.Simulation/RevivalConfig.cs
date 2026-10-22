using System;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Fixed-tick revival rules. Prices are quoted at death using session simulation time.</summary>
public sealed class RevivalConfig
{
    public RevivalConfig(int downedTicks=1800,int rescueTicks=90,int rescueRange=1500,int rescueHealthPermille=300,
        int baseFee=10,int growthFee=5,int growthIntervalTicks=9000,int balanceFeePermille=100,
        int respawnHealthPermille=1000,int respawnStaminaPermille=1000)
    {
        if(downedTicks<1 || downedTicks>108000 || rescueTicks<1 || rescueTicks>18000 || rescueRange<400 || rescueRange>5000 ||
            rescueHealthPermille<1 || rescueHealthPermille>1000 || baseFee<0 || baseFee>1000000 || growthFee<0 || growthFee>1000000 ||
            growthIntervalTicks<1 || growthIntervalTicks>2592000 || balanceFeePermille<0 || balanceFeePermille>1000 ||
            respawnHealthPermille<1 || respawnHealthPermille>1000 || respawnStaminaPermille<0 || respawnStaminaPermille>1000)
            throw new InvalidDataException("Invalid revival configuration.");
        DownedTicks=downedTicks;RescueTicks=rescueTicks;RescueRange=rescueRange;RescueHealthPermille=rescueHealthPermille;
        BaseFee=baseFee;GrowthFee=growthFee;GrowthIntervalTicks=growthIntervalTicks;BalanceFeePermille=balanceFeePermille;
        RespawnHealthPermille=respawnHealthPermille;RespawnStaminaPermille=respawnStaminaPermille;
    }
    public int DownedTicks { get; }
    public int RescueTicks { get; }
    public int RescueRange { get; }
    public int RescueHealthPermille { get; }
    public int BaseFee { get; }
    public int GrowthFee { get; }
    public int GrowthIntervalTicks { get; }
    public int BalanceFeePermille { get; }
    public int RespawnHealthPermille { get; }
    public int RespawnStaminaPermille { get; }
    public int Cost(ulong tick,int balance)
    {
        if(balance<0)throw new ArgumentOutOfRangeException(nameof(balance));
        long fee=BaseFee+(long)balance*BalanceFeePermille/1000;
        if(fee>=int.MaxValue)return int.MaxValue;
        ulong blocks=tick/(uint)GrowthIntervalTicks;
        // Bound before multiplication, including arbitrarily long restored simulation times.
        if(GrowthFee>0 && blocks>(ulong)((int.MaxValue-fee)/GrowthFee))return int.MaxValue;
        return (int)Math.Min(int.MaxValue,fee+(GrowthFee==0?0:(long)blocks*GrowthFee));
    }
    internal void Write(BinaryWriter w)
    {w.Write(DownedTicks);w.Write(RescueTicks);w.Write(RescueRange);w.Write(RescueHealthPermille);w.Write(BaseFee);w.Write(GrowthFee);w.Write(GrowthIntervalTicks);w.Write(BalanceFeePermille);w.Write(RespawnHealthPermille);w.Write(RespawnStaminaPermille);}
    internal static RevivalConfig Read(BinaryReader r)=>new RevivalConfig(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
}
