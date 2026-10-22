using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

/// <summary>One user transaction survives dropped input frames until an authoritative receipt arrives.</summary>
public sealed class RodRequestBuffer
{
    private uint id,batch;
    private int target;
    private RodAction action;
    public bool Pending=>id!=0;
    public bool TryQueue(uint last,RodAction next,int item,uint stockBatch)
    {
        if(Pending || last==uint.MaxValue || next==RodAction.None)return false;
        new HookInput(0,0,rodAction:next,rodTarget:item,rodBatch:stockBatch,rodRequestId:last+1).Encode();
        id=last+1;action=next;target=item;batch=stockBatch;return true;
    }
    public bool Observe(Fisher player,out RodResult result)
    {
        result=RodResult.None;if(!Pending || player.LastRodRequest<id)return false;
        result=player.LastRodResult;Reset();return true;
    }
    public HookInput Compose(HookInput input)
    {
        if(!Pending)return input;
        return new HookInput(input.MoveX,input.MoveZ,input.AimX,input.AimZ,false,false,shopping:input.Shopping,
            rodAction:action,rodTarget:target,rodBatch:batch,rodRequestId:id,roomRules:input.RoomRules);
    }
    public void Reset(){id=batch=0;target=0;action=RodAction.None;}
}
