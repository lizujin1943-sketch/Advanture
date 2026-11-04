namespace Advanture.HookIsland.Simulation;

/// <summary>Pending 不是最终回执；出手帧或失败帧才变成 Success／失败原因。</summary>
public enum GrenadeResult : byte {None,Pending,Success,Disabled,Busy,NotOwned,OutOfRange,Blocked,Interrupted,Exhausted}
public enum GrenadePhase : byte {Flying,Falling,Landed}

/// <summary>每人独立的回执与出手动作。即使新请求被拒绝，也不覆盖已经开始的动作。</summary>
public struct GrenadeAction
{
    public uint LastRequest,ActionRequest,Sequence;
    public GrenadeResult Result;
    public int ItemId,TargetX,TargetZ,RemainingTicks,ReturnWeaponId;
    public bool Active=>RemainingTicks>0;
}

/// <summary>投掷物自身的确定性状态；来源物品 ID 永不复用，因此也作为唯一投掷物 ID。</summary>
public struct GrenadeState
{
    public int Id,Owner,SourceItemId;
    public uint Sequence;
    public ulong SpawnTick,LandedTick;
    public GrenadePhase Phase;
    public int StartX,StartY,StartZ,TargetX,TargetY,TargetZ;
    public int X,Y,Z,Age,FlightTicks,ArcHeight,VerticalSpeed;
    public bool Water;
}
