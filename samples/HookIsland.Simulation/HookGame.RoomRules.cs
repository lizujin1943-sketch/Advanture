namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private RoomRulesState roomRules;
        private void ApplyRoomRules(RoomRulesCommand command)
        {
            // Slot zero owns the room. Incapacitation does not revoke room-management authority.
            if(players[0].Unavailable || command.IsEmpty || command.RequestId<=roomRules.LastRequest)return;
            roomRules=new RoomRulesState(command.Enabled,command.RequestId);
            if(command.Enabled)return;
            for(int i=0;i<players.Length;i++)
                if(players[i].HookedPlayer>=0 && (equipment==null || !players[players[i].HookedPlayer].Swimming.InWater))
                    ReleasePlayer(ref players[i]);
        }
    }
}
