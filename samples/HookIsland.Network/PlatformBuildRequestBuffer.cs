using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

/// <summary>
/// Retains one confirmed user action until its authoritative receipt arrives. Sampling a new
/// input frame never invents a new transaction ID, even when that frame expires in transit.
/// </summary>
public sealed class PlatformBuildRequestBuffer
{
    private uint id;
    private TileKind kind;
    private short x, z;
    public bool Pending => id != 0;

    public bool TryQueue(uint lastProcessedId, TileKind tile, int cellX, int cellZ)
    {
        if (Pending || lastProcessedId == uint.MaxValue || tile < TileKind.Wood || tile > TileKind.Fishbone ||
            !PlatformRules.InBounds(cellX, cellZ)) return false;
        id = lastProcessedId + 1;
        kind = tile;
        x = (short)cellX;
        z = (short)cellZ;
        return true;
    }

    public bool Observe(Fisher player, out BuildResult result)
    {
        result = BuildResult.None;
        if (!Pending || player.LastBuildId < id) return false;
        result = player.LastBuildResult;
        Reset();
        return true;
    }

    public HookInput Compose(HookInput input, bool buildMode)
    {
        if (!Pending && !buildMode) return input;
        // A committed build has priority over a simultaneous equipment/shop transaction.
        // Those systems keep their own receipt and can retry once construction is acknowledged.
        return new HookInput(input.MoveX, input.MoveZ, input.AimX, input.AimZ,
            buildMode ? false : input.Fire, buildMode ? false : input.Cancel,
            Pending ? HookUpgrade.None : input.Upgrade, Pending ? 0 : input.PurchaseId, input.Shopping,
            Pending ? kind : input.Build, Pending ? x : input.BuildX, Pending ? z : input.BuildZ,
            Pending ? id : input.BuildId,
            Pending ? RodAction.None : input.RodAction, Pending ? 0 : input.RodTarget,
            Pending ? 0 : input.RodBatch, Pending ? 0 : input.RodRequestId,
            // 仅打开建造面板不能吞掉已有库存请求；建造回执到达后应继续重试原意图。
            Pending ? default : input.Inventory,revival:Pending?default:input.Revival,roomRules:input.RoomRules,treasure:Pending?default:input.Treasure);
    }

    public void Reset()
    {
        id = 0;
        kind = TileKind.None;
        x = z = 0;
    }
}
