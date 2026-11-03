using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

/// <summary>
/// 保存一次主动唤醒意图，直到确认状态带回成功或失败回执。
/// 网络丢包只重发同一编号，不把“按一次 E”变成多次挑战请求。
/// </summary>
public sealed class BossRequestBuffer
{
    public uint Request { get; private set; }
    public bool Pending => Request != 0;

    public bool TryQueue(uint lastConfirmedRequest)
    {
        if (Pending || lastConfirmedRequest == uint.MaxValue) return false;
        Request = lastConfirmedRequest + 1;
        return true;
    }

    public bool Observe(BossRequestReceipt receipt, out BossRequestResult result)
    {
        result = BossRequestResult.None;
        if (!Pending || receipt.Request < Request) return false;
        result = receipt.Result;
        Reset();
        return true;
    }

    /// <summary>与库存等事务排队执行；等待时保留编号，不抢占已在交付的操作。</summary>
    public HookInput Compose(HookInput input) => !Pending || input.Upgrade != HookUpgrade.None ||
        input.Build != TileKind.None || input.RodAction != RodAction.None || input.Inventory.Action != InventoryAction.None ||
        !input.Revival.IsEmpty || !input.Treasure.IsEmpty ? input : new HookInput(
        input.MoveX, input.MoveZ, input.AimX, input.AimZ, input.Fire, input.Cancel,
        input.Upgrade, input.PurchaseId, input.Shopping, input.Build, input.BuildX, input.BuildZ, input.BuildId,
        input.RodAction, input.RodTarget, input.RodBatch, input.RodRequestId,
        input.Inventory, input.Revival, input.RoomRules, input.Treasure, input.Reload, input.AimVertical, Request);

    public void Reset() => Request = 0;
}
