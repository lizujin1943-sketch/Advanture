using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class CatchEscapeStateCodec
{
    internal static void Write(BinaryWriter writer,HookState state)
    {
        writer.Write(0x31534345);
        foreach(var fish in state.Fish)Write(writer,fish.Escape);
        foreach(var fish in state.Enemies.Piranhas)Write(writer,fish.Escape);
    }
    private static void Write(BinaryWriter writer,CatchEscapeState state)
    {writer.Write(state.Tick);writer.Write(state.OriginX);writer.Write(state.OriginY);writer.Write(state.OriginZ);writer.Write(state.LandingX);writer.Write(state.LandingZ);}
    private static CatchEscapeState Read(BinaryReader reader)
    {
        var state=new CatchEscapeState {Tick=reader.ReadInt32(),OriginX=reader.ReadInt32(),OriginY=reader.ReadInt32(),OriginZ=reader.ReadInt32(),LandingX=reader.ReadInt32(),LandingZ=reader.ReadInt32()};
        if(!CatchEscapeMotion.Valid(state))throw new InvalidDataException("Invalid catch escape trajectory.");
        return state;
    }
    internal static HookState Read(BinaryReader reader,HookState state)
    {
        if(reader.ReadInt32()!=0x31534345)throw new InvalidDataException("Catch escape state version mismatch.");
        var fish=state.Fish.ToArray();
        for(int i=0;i<fish.Length;i++)
        {
            var f=fish[i];f.Escape=Read(reader);
            if((f.Phase==FishPhase.Escaping)!=f.Escape.Active)throw new InvalidDataException("Invalid fish escape phase.");
            if(f.Escape.Active)
            {
                CatchEscapeMotion.Position(f.Escape,out int x,out int y,out int z);
                if(f.Owner!=-1 || f.Timer!=0 || f.X!=x || f.Y!=y || f.Z!=z)throw new InvalidDataException("Invalid escaping fish position/owner.");
            }
            fish[i]=f;
        }
        var enemies=state.Enemies;var piranhas=enemies.Piranhas.ToArray();
        for(int i=0;i<piranhas.Length;i++)
        {
            var f=piranhas[i];f.Escape=Read(reader);
            if(f.Escape.Active)
            {
                CatchEscapeMotion.Position(f.Escape,out int x,out _,out int z);
                if(f.Owner!=-1 || f.Target!=-1 || f.RespawnTicks!=0 || f.X!=x || f.Z!=z)throw new InvalidDataException("Invalid escaping piranha position/owner.");
            }
            piranhas[i]=f;
        }
        return new HookState(state.Steps,state.Players,fish,state.Coins,state.Tiles,state.Driftwood,state.Tackle,
            new EnemyState(piranhas,enemies.Monster,enemies.Waterballs,enemies.Serial,enemies.Bites,enemies.Shots,enemies.Interruptions,enemies.WaterHits,enemies.Captures));
    }
}
