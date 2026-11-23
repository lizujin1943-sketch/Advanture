using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>固定名册与刷怪节奏。随机序列、预算、波次进度仍由导演快照持有。</summary>
public sealed class EncounterSchedule
{
    private readonly EldritchKind[] adults;
    private readonly Func<int,int,int> waveSize;
    private readonly Func<int,int> rest;
    private readonly int soloLimit, cooperativeLimit;

    public EncounterSchedule(IEnumerable<EldritchKind> adults, int initialCredits, int maxCredits, int income,
        int initialWait, int spacing, int retry, Func<int,int,int> waveSize, Func<int,int> rest,
        int soloLimit = 0, int cooperativeLimit = 0)
    {
        this.adults = adults.ToArray();
        if (this.adults.Length == 0 || initialCredits < 0 || initialCredits > maxCredits ||
            income <= 0 || initialWait < 0 || spacing <= 0 || retry <= 0 || soloLimit < 0 ||
            soloLimit > cooperativeLimit || cooperativeLimit > this.adults.Length)
            throw new ArgumentException("Invalid encounter schedule.");
        InitialCredits = initialCredits;
        MaxCredits = maxCredits;
        Income = income;
        InitialWait = initialWait;
        Spacing = spacing;
        Retry = retry;
        this.waveSize = waveSize;
        this.rest = rest;
        this.soloLimit = soloLimit;
        this.cooperativeLimit = cooperativeLimit;
        MaxWait = Math.Max(initialWait,Math.Max(spacing,retry));
        for (int tier=0; tier<=TownDirectorRules.MaxTier; tier++)
        {
            MaxWait = Math.Max(MaxWait,rest(tier));
            for (int players=1; players<=4; players++)
                MaxWaveSize = Math.Max(MaxWaveSize,waveSize(tier,players));
        }
    }

    public static readonly EncounterSchedule Town = new EncounterSchedule(TownEncounters.Kinds,
        TownDirectorRules.InitialCredits, TownDirectorRules.MaxCredits, 2, TownDirectorRules.InitialWaitTicks,
        TownDirectorRules.SpawnSpacingTicks, TownDirectorRules.RetryTicks, (tier, players) => tier >= 2 ? 3 : 2,
        TownDirectorRules.RestTicks);
    public int InitialCredits { get; }
    public int MaxCredits { get; }
    public int Income { get; }
    public int InitialWait { get; }
    public int Spacing { get; }
    public int Retry { get; }
    internal int MaxWait { get; }
    internal int MaxWaveSize { get; }
    public int AdultCount => adults.Length;
    public int Population => AdultCount + (TownEncounters.Population - TownEncounters.AdultCount);
    public EldritchKind Kind(int id) => id < AdultCount ? adults[id] : EldritchKind.DeepOne;
    public int WaveSize(int tier, int participants) => waveSize(tier, participants);
    public int RestTicks(int tier) => rest(tier);
    public int AdultLimit(int fallback, int participants) => soloLimit == 0 ? fallback : participants > 1 ? cooperativeLimit : soloLimit;
}
