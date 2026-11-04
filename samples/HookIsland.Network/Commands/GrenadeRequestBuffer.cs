using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

/// <summary>松手时冻结目标，之后每次重发完全相同的请求；Pending 回执不是完成，必须等最终结果。</summary>
public sealed class GrenadeRequestBuffer
{
    public GrenadeCommand Command {get;private set;}
    public bool Pending=>!Command.IsEmpty;
    public bool TryQueue(uint lastRequest,int itemId,int targetX,int targetZ)
    {
        if(Pending || lastRequest==uint.MaxValue || itemId<=0)return false;
        var command=new GrenadeCommand(lastRequest+1,itemId,targetX,targetZ);command.Encode();Command=command;return true;
    }
    public bool Observe(GrenadeAction action,out GrenadeResult result)
    {
        result=GrenadeResult.None;
        if(!Pending || action.LastRequest<Command.RequestId || action.LastRequest==Command.RequestId && action.Result==GrenadeResult.Pending)return false;
        result=action.Result;Reset();return true;
    }
    public HookInput Compose(HookInput input)
    {
        if(!Pending || input.Upgrade!=HookUpgrade.None || input.Build!=TileKind.None || input.RodAction!=RodAction.None ||
            input.Inventory.Action!=InventoryAction.None || !input.Revival.IsEmpty || !input.Treasure.IsEmpty || input.BossRequest!=0)return input;
        return new HookInput(input.MoveX,input.MoveZ,input.AimX,input.AimZ,roomRules:input.RoomRules,aimVertical:input.AimVertical,grenade:Command);
    }
    public void Reset()=>Command=default;
}
