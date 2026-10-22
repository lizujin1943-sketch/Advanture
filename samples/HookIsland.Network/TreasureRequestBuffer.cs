using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

public sealed class TreasureRequestBuffer
{
    public TreasureCommand Command { get; private set; }
    public bool Pending=>!Command.IsEmpty;
    public bool TryQueue(uint last,int chestId,uint generation)
    {
        if(Pending || last==uint.MaxValue || chestId<1 || chestId>16 || generation==0)return false;
        Command=new TreasureCommand(chestId,generation,last+1);return true;
    }
    public bool Observe(uint confirmed,TreasureResult result,out TreasureResult receipt)
    {
        receipt=TreasureResult.None;if(!Pending || confirmed<Command.RequestId)return false;
        receipt=result;Reset();return true;
    }
    public HookInput Compose(HookInput input)
    {
        if(!Pending || input.Build!=TileKind.None || input.Upgrade!=HookUpgrade.None || input.RodAction!=RodAction.None ||
            input.Inventory.Action!=InventoryAction.None || !input.Revival.IsEmpty)return input;
        return new HookInput(input.MoveX,input.MoveZ,input.AimX,input.AimZ,shopping:input.Shopping,roomRules:input.RoomRules,treasure:Command);
    }
    public void Reset()=>Command=default;
}
