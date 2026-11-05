using System.IO;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

/// <summary>Keep one service intention until its confirmed receipt, including a failed receipt.</summary>
public sealed class TownRequestBuffer
{
    public TownCommand Command {get;private set;}
    public bool Pending=>!Command.IsEmpty;

    public bool TryQueue(uint lastConfirmedRequest,TownAction action,int target=0,uint attempt=0)
    {
        if(Pending || lastConfirmedRequest==uint.MaxValue || action==TownAction.None)return false;
        var command=new TownCommand(action,lastConfirmedRequest+1,target,attempt);
        try{command.Encode();}catch(InvalidDataException){return false;}
        Command=command;return true;
    }

    public bool Observe(TownPersonalState confirmed,out TownResult result)
    {
        result=TownResult.None;
        if(!Pending || confirmed.LastRequest<Command.RequestId)return false;
        result=confirmed.LastResult;Reset();return true;
    }

    // Other transaction buffers deliver first. Combat flags are suppressed only on a service frame.
    public HookInput Compose(HookInput input)
    {
        if(!Pending || !input.Town.IsEmpty || input.Upgrade!=HookUpgrade.None || input.Build!=TileKind.None ||
            input.RodAction!=RodAction.None || input.Inventory.Action!=InventoryAction.None || !input.Revival.IsEmpty ||
            !input.Treasure.IsEmpty || input.BossRequest!=0 || !input.Grenade.IsEmpty)return input;
        return new HookInput(input.MoveX,input.MoveZ,input.AimX,input.AimZ,cancel:input.Cancel,
            roomRules:input.RoomRules,aimVertical:input.AimVertical,town:Command);
    }

    public void Reset()=>Command=default;
}
