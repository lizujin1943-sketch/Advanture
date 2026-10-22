using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class RoomRulesStateCodec
{
    internal static void Write(BinaryWriter writer,HookState state)
    {
        writer.Write(0x314C5552); // RUL1
        writer.Write(state.RoomRules.FriendlyPranksEnabled?(byte)1:(byte)0);
        writer.Write(state.RoomRules.LastRequest);
    }
    internal static HookState Read(BinaryReader reader,HookState state)
    {
        if(reader.ReadInt32()!=0x314C5552)throw new InvalidDataException("Room rules state version mismatch.");
        byte enabled=reader.ReadByte();uint request=reader.ReadUInt32();
        if(enabled>1 || (enabled==0 && state.Players.Any(p=>p.PulledBy>=0 && (state.Tackle==null || !p.Swimming.InWater))))
            throw new InvalidDataException("Invalid room rules state.");
        return new HookState(state.Steps,state.Players,state.Fish,state.Coins,state.Tiles,state.Driftwood,state.Tackle,state.Enemies,
            new RoomRulesState(enabled==1,request));
    }
}
