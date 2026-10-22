using System.IO;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

/// <summary>
/// 把界面点击保留成同一请求，重复放入尚未冻结的未来帧。
/// 只依据确认帧的回执停止发送，预测成功不是结算依据；失败回执也立即结束，需玩家重新点击。
/// </summary>
public sealed class InventoryRequestBuffer
{
    public InventoryCommand Command { get; private set; }
    public bool Pending => Command.RequestId != 0;
    public bool TryQueue(uint lastConfirmedRequest, InventoryCommand intent)
    {
        if (Pending || lastConfirmedRequest == uint.MaxValue || intent.Action == InventoryAction.None) return false;
        var candidate = intent.WithRequest(lastConfirmedRequest + 1);
        try { candidate.Validate(); } catch (InvalidDataException) { return false; }
        Command = candidate; return true;
    }
    public bool Observe(uint confirmedRequest, InventoryResult confirmedResult, out InventoryResult result)
    {
        result = InventoryResult.None;
        if (!Pending || confirmedRequest < Command.RequestId) return false;
        result = confirmedResult; Reset(); return true;
    }
    public void Reset() => Command = default;
}
