using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>城区预设生成卡。槽位身份固定，每次导演重新购买用 Generation 区分，休眠不改变身份。</summary>
public static class TownEncounters
{
    public const int AdultCount=15,Population=19;
    public static readonly IReadOnlyList<EldritchKind> Kinds=Array.AsReadOnly(new[]{
        EldritchKind.DeepOne,EldritchKind.DeepOne,EldritchKind.RiftEye,
        EldritchKind.ReefRam,EldritchKind.DeepOne,
        EldritchKind.ShellWarden,EldritchKind.ChoirPriest,EldritchKind.DeepOne,
        EldritchKind.RiftEye,EldritchKind.BlightSac,EldritchKind.DeepOne,
        EldritchKind.DrownedBell,EldritchKind.RiftStalker,
        EldritchKind.BroodMother,EldritchKind.ChoirPriest});
    public static int Zone(int id)=>id<3?0:id<5?1:id<8?2:id<11?3:id<13?4:5;
    public static EldritchKind Kind(int id)=>id<AdultCount?Kinds[id]:EldritchKind.DeepOne;
    public static bool Minion(int id)=>id>=AdultCount;
    /// <summary>首次清理只需累计这么多次成体击杀；之后持续刷怪不回退或重发目标奖励。</summary>
    public static int ClearTarget(int zone)=>zone==0 || zone==2 || zone==3?3:2;
    public static bool NearZone(int zone,Fisher p,int margin=8000)
    {var a=TownLayout.EncounterAreas[zone];return EldritchSystem.Eligible(p) && !p.Shopping && Math.Abs((long)p.X-a.X)<=a.Width/2+margin && Math.Abs((long)p.Z-a.Z)<=a.Depth/2+margin && !TownLayout.NearService(p.X,p.Z);}
}
