using System.IO;

namespace Advanture.HookIsland.Simulation;

public static class SwimmingStateCodec
{
    public static void Write(BinaryWriter w,SwimmingState s)
    {Validate(s);w.Write(s.InWater?(byte)1:(byte)0);w.Write(s.Active?(byte)1:(byte)0);w.Write(s.DamageRemainder);w.Write(s.DamageTicks);w.Write(s.MoveRemainderX);w.Write(s.MoveRemainderZ);}
    public static SwimmingState Read(BinaryReader r)
    {
        byte water=r.ReadByte(),active=r.ReadByte();if(water>1 || active>1)throw new InvalidDataException("Invalid water flag.");
        var s=new SwimmingState{InWater=water==1,Active=active==1,DamageRemainder=r.ReadInt32(),DamageTicks=r.ReadInt32(),MoveRemainderX=r.ReadInt64(),MoveRemainderZ=r.ReadInt64()};
        Validate(s);return s;
    }
    public static void Validate(SwimmingState s)
    {
        if((s.Active && !s.InWater) || s.DamageRemainder<0 || s.DamageRemainder>=SwimmingSystem.DamageDenominator ||
            s.DamageTicks<0 || s.DamageTicks>=SwimmingSystem.DamageIntervalTicks ||
            s.MoveRemainderX<=-SwimmingSystem.MovementDenominator || s.MoveRemainderX>=SwimmingSystem.MovementDenominator ||
            s.MoveRemainderZ<=-SwimmingSystem.MovementDenominator || s.MoveRemainderZ>=SwimmingSystem.MovementDenominator)
            throw new InvalidDataException("Invalid swimming residuals.");
    }
}
