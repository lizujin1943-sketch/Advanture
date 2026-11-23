using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.Tactical.Simulation;

public static class CompoundDefense
{
    // 多个独立槽位允许同种怪物同时从不同破洞出现。四个召唤物槽位单独保留。
    private static readonly EldritchKind[] Squad = {
        EldritchKind.DeepOne, EldritchKind.DeepOne, EldritchKind.ReefRam, EldritchKind.RiftEye,
        EldritchKind.DeepOne, EldritchKind.ShellWarden, EldritchKind.BlightSac, EldritchKind.RiftStalker,
        EldritchKind.ChoirPriest, EldritchKind.DeepOne, EldritchKind.ReefRam, EldritchKind.BroodMother
    };
    public static readonly EncounterSchedule Waves = new EncounterSchedule(
        Enumerable.Range(0, 3).SelectMany(_ => Squad), initialCredits: 220, maxCredits: 400, income: 18,
        initialWait: 45, spacing: 20, retry: 8,
        waveSize: (tier, players) => 12 + (players > 1 ? 4 : 0) + 2 * tier,
        rest: tier => 180 - 12 * tier, soloLimit: 24, cooperativeLimit: 32);
}
