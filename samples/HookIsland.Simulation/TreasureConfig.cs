using System;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Immutable chest rules; millimetres, 30 Hz ticks and integer rewards.</summary>
public sealed class TreasureConfig
{
    public static TreasureConfig Disabled { get; }=new TreasureConfig(0,0,0);
    public TreasureConfig(int smallCount=1,int rareCount=1,int largeCount=1,int initialDelayTicks=120,
        int slotDelayTicks=180,int respawnTicks=900,int driftSpeed=200,int rareDriftSpeed=500,
        int floatLifetimeTicks=1350,int smallReelSpeed=1800,int largeReelSpeed=900,int openRange=1600,
        int smallCoins=18,int smallWood=4,int smallBones=2,int rareCoins=45,int rareWood=6,int rareBones=6,
        int largeCoins=61,int largeWood=13,int largeBones=9)
    {
        if(smallCount<0 || rareCount<0 || largeCount<0 || (long)smallCount+rareCount+largeCount>16 ||
            initialDelayTicks<0 || initialDelayTicks>18000 || slotDelayTicks<0 || slotDelayTicks>1800 ||
            respawnTicks<30 || respawnTicks>18000 || driftSpeed<0 || driftSpeed>3000 || rareDriftSpeed<0 || rareDriftSpeed>3000 ||
            floatLifetimeTicks<30 || floatLifetimeTicks>18000 || smallReelSpeed<30 || smallReelSpeed>30000 ||
            largeReelSpeed<30 || largeReelSpeed>30000 || openRange<300 || openRange>5000)
            throw new InvalidDataException("Invalid treasure configuration.");
        foreach(int reward in new[]{smallCoins,smallWood,smallBones,rareCoins,rareWood,rareBones,largeCoins,largeWood,largeBones})
            if(reward<0 || reward>1000000)throw new InvalidDataException("Invalid treasure reward.");
        SmallCount=smallCount;RareCount=rareCount;LargeCount=largeCount;InitialDelayTicks=initialDelayTicks;
        SlotDelayTicks=slotDelayTicks;RespawnTicks=respawnTicks;DriftSpeed=driftSpeed;RareDriftSpeed=rareDriftSpeed;
        FloatLifetimeTicks=floatLifetimeTicks;SmallReelSpeed=smallReelSpeed;LargeReelSpeed=largeReelSpeed;OpenRange=openRange;
        SmallCoins=smallCoins;SmallWood=smallWood;SmallBones=smallBones;RareCoins=rareCoins;RareWood=rareWood;RareBones=rareBones;
        LargeCoins=largeCoins;LargeWood=largeWood;LargeBones=largeBones;
    }
    public bool Enabled=>SmallCount+RareCount+LargeCount>0;
    public int SmallCount { get; }
    public int RareCount { get; }
    public int LargeCount { get; }
    public int InitialDelayTicks { get; }
    public int SlotDelayTicks { get; }
    public int RespawnTicks { get; }
    public int DriftSpeed { get; }
    public int RareDriftSpeed { get; }
    public int FloatLifetimeTicks { get; }
    public int SmallReelSpeed { get; }
    public int LargeReelSpeed { get; }
    public int OpenRange { get; }
    public int SmallCoins { get; }
    public int SmallWood { get; }
    public int SmallBones { get; }
    public int RareCoins { get; }
    public int RareWood { get; }
    public int RareBones { get; }
    public int LargeCoins { get; }
    public int LargeWood { get; }
    public int LargeBones { get; }
    public int Count(int players)=>SmallCount+RareCount+(players>1?LargeCount:0);
    public TreasureKind Kind(int id)=>id<SmallCount?TreasureKind.Small:id<SmallCount+RareCount?TreasureKind.Rare:TreasureKind.Large;
    internal void Write(BinaryWriter w)
    {
        foreach(int value in new[]{SmallCount,RareCount,LargeCount,InitialDelayTicks,SlotDelayTicks,RespawnTicks,DriftSpeed,RareDriftSpeed,
            FloatLifetimeTicks,SmallReelSpeed,LargeReelSpeed,OpenRange,SmallCoins,SmallWood,SmallBones,RareCoins,RareWood,RareBones,LargeCoins,LargeWood,LargeBones})w.Write(value);
    }
    internal static TreasureConfig Read(BinaryReader r)=>new TreasureConfig(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),
        r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),
        r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
}
