namespace Advanture.HookIsland.Simulation;

/// <summary>
/// 城镇导演的完整确定性状态。这里保存会影响下一次投放的全部信息，不能把随机数或计时器藏在 Unity 中。
/// 全零表示尚未开始探索；结构体按值复制，预测回滚不会共享可变状态。
/// </summary>
public struct TownDirectorState
{
    /// <summary>有效探索逻辑帧数，达到最高压力后封顶；安全区和 BOSS 战不计时。</summary>
    public ulong ElapsedTicks;
    /// <summary>预算单位为 1/30 点；每帧增加每秒收入值，避免浮点运算。</summary>
    public int CreditUnits;
    /// <summary>人数倍率除以 5 后留下的余数，下帧继续累计。</summary>
    public int IncomeRemainder;
    /// <summary>距离下一次尝试的帧数，出生失败也必须等待。</summary>
    public int WaitTicks;
    /// <summary>待购买槽位编号加一；0 表示未选卡。买不起时保留，避免贵怪永久饿死。</summary>
    public int PendingCard;
    /// <summary>当前批次还可成功生成几只，失败不消耗批次。</summary>
    public int BurstRemaining;
    /// <summary>独立于其他玩法的随机数状态，0 仅用于尚未启动的全零状态。</summary>
    public uint RandomState;
}
