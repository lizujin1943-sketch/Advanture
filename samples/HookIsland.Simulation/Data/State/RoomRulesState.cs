namespace Advanture.HookIsland.Simulation;

/// <summary>Confirmed room policy and the host's last applied request, included in checkpoints.</summary>
public readonly struct RoomRulesState
{
    public RoomRulesState(bool friendlyPranksEnabled,uint lastRequest=0)
    {FriendlyPranksEnabled=friendlyPranksEnabled;LastRequest=lastRequest;}
    public bool FriendlyPranksEnabled { get; }
    public uint LastRequest { get; }
}
