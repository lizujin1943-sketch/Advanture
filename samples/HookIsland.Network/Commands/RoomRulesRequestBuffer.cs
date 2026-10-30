using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

/// <summary>Retain one policy click until its authoritative receipt, independent of gameplay transactions.</summary>
public sealed class RoomRulesRequestBuffer
{
    public RoomRulesCommand Command { get; private set; }
    public bool Pending=>!Command.IsEmpty;
    public bool TryQueue(RoomRulesState confirmed,bool enabled)
    {
        if(Pending || confirmed.LastRequest==uint.MaxValue || confirmed.FriendlyPranksEnabled==enabled)return false;
        Command=new RoomRulesCommand(enabled,confirmed.LastRequest+1);return true;
    }
    public void Observe(RoomRulesState confirmed)
    {if(Pending && confirmed.LastRequest>=Command.RequestId)Reset();}
    public HookInput Compose(HookInput input)=>!Pending?input:new HookInput(input.MoveX,input.MoveZ,input.AimX,input.AimZ,
        input.Fire,input.Cancel,input.Upgrade,input.PurchaseId,input.Shopping,input.Build,input.BuildX,input.BuildZ,input.BuildId,
        input.RodAction,input.RodTarget,input.RodBatch,input.RodRequestId,input.Inventory,input.Revival,Command,input.Treasure);
    public void Reset()=>Command=default;
}
