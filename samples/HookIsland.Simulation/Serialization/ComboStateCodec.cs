using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class ComboStateCodec
{
    internal static void Write(BinaryWriter w,HookState state)
    {
        w.Write(0x31424D43); // CMB1
        foreach(var p in state.Players){w.Write(p.Combo.Count);w.Write(p.Combo.PendingCast);}
    }
    internal static HookState Read(BinaryReader r,HookState state)
    {
        if(r.ReadInt32()!=0x31424D43)throw new InvalidDataException("Combo state version mismatch.");
        var players=state.Players.ToArray();
        for(int i=0;i<players.Length;i++)
        {
            int count=r.ReadInt32();byte pending=r.ReadByte();var p=players[i];
            if(count<0 || pending>1 || pending!=0 && p.Phase==HookPhase.Ready ||
                (state.Tackle?.Config.Combo.Enabled!=true || p.Unavailable || p.Vitals.Downed || p.Revival.Dead) && (count!=0 || pending!=0))
                throw new InvalidDataException("Invalid combo state.");
            players[i].Combo=new ComboState{Count=count,PendingCast=pending!=0};
        }
        return new HookState(state.Steps,players,state.Fish,state.Coins,state.Tiles,state.Driftwood,state.Tackle,state.Enemies,
            state.RoomRules,state.Treasure,state.Commissions,state.Ocean,state.Finale);
    }
}
