using System;

namespace Advanture.HookIsland.Simulation;

/// <summary>沉钟镇刷怪数值。修改这些规则会改变模拟结果，必须同步升级玩法兼容版本。</summary>
public static class TownDirectorRules
{
    public const int InitialCredits=20,MaxCredits=80,InitialWaitTicks=90;
    public const int SpawnSpacingTicks=45,RetryTicks=30,CorpseTicks=240;
    public const int TierTicks=120*HookGame.Rate,MaxTier=5;
    public const ulong MaxElapsedTicks=TierTicks*MaxTier;
    // 与 EldritchKind 的十种现有敌人一一对应；实际可选卡由城区槽位限定。
    private static readonly int[] Costs={8,16,24,16,18,14,18,28,20,30};
    private static readonly int[] Weights={8,4,2,5,4,5,3,2,3,1};
    public static int Cost(EldritchKind kind)=>Costs[(int)kind];
    public static int Tier(ulong ticks)=>(int)Math.Min((ulong)MaxTier,ticks/TierTicks);
    public static int Weight(EldritchKind kind,int tier)=>Weights[(int)kind]*(Cost(kind)>=18?1+tier:1);
    public static int RestTicks(int tier)=>Math.Max(90,150-tier*12);
}
