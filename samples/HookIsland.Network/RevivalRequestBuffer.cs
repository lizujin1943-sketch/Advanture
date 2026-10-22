using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

/// <summary>Retransmit a death-scoped payment intent until its confirmed receipt, including failures.</summary>
public sealed class RevivalRequestBuffer
{
    public RevivalCommand Command { get; private set; }
    public bool Pending=>Command.RespawnRequest!=0;
    public bool TryQueue(RevivalState life)
    {
        if(Pending || !life.Dead || life.Deaths==0 || life.LastRequest==uint.MaxValue)return false;
        Command=new RevivalCommand(respawnRequest:life.LastRequest+1,deathId:life.Deaths);return true;
    }
    public bool Observe(RevivalState life,out RespawnResult result)
    {
        result=RespawnResult.None;if(!Pending || life.LastRequest<Command.RespawnRequest)return false;
        result=life.LastResult;Reset();return true;
    }
    public void Reset()=>Command=default;
}
