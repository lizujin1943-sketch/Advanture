using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Distinguishes an unused roster seat from an entered character whose connection is absent.</summary>
internal static class PresenceStateCodec
{
    internal static void Write(BinaryWriter w,HookState state)
    {
        w.Write(0x31535250); // PRS1
        foreach(var p in state.Players)w.Write(p.WaitingToJoin?(byte)1:(byte)0);
    }
    internal static HookState Read(BinaryReader r,HookState state)
    {
        if(r.ReadInt32()!=0x31535250)throw new InvalidDataException("Presence state version mismatch.");
        var players=state.Players.ToArray();
        for(int i=0;i<players.Length;i++)
        {
            byte waiting=r.ReadByte();if(waiting>1)throw new InvalidDataException("Invalid waiting flag.");
            players[i].WaitingToJoin=waiting==1;var p=players[i];
            if(p.WaitingToJoin && (!p.Unavailable || p.Phase!=HookPhase.Ready || p.PulledBy>=0 || p.HookedPlayer>=0 ||
                p.Knockback.Active || p.Swimming.Active || p.Shopping || p.Revival.Rescuer>0 || p.Revival.Dead ||
                p.Revival.DownedTicks>0 || p.HookedFinale || p.HookedChest>0 || CooperativeFishing.Attached(p)))
                throw new InvalidDataException("Reserved seat participates in the world.");
        }
        return new HookState(state.Steps,players,state.Fish,state.Coins,state.Tiles,state.Driftwood,state.Tackle,state.Enemies,
            state.RoomRules,state.Treasure,state.Commissions,state.Ocean,state.Finale);
    }
}
