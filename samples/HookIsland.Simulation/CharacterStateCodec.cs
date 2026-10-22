using System;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Character checkpoint data and invariants; derived maximums are recalculated from saved equipment.</summary>
public static class CharacterStateCodec
{
    public static void Write(BinaryWriter writer, CharacterVitals vitals, int moveRemainderX, int moveRemainderZ)
    {
        writer.Write(vitals.Health); writer.Write(vitals.Hunger);
        writer.Write(moveRemainderX); writer.Write(moveRemainderZ);
    }
    public static CharacterVitals Read(BinaryReader reader, out int moveRemainderX, out int moveRemainderZ)
    {
        var vitals = new CharacterVitals { Health = reader.ReadInt32(), Hunger = reader.ReadInt32() };
        moveRemainderX = reader.ReadInt32(); moveRemainderZ = reader.ReadInt32(); return vitals;
    }
    public static void Validate(CharacterVitals vitals, int moveRemainderX, int moveRemainderZ, CharacterStats stats)
    {
        if (vitals.Health < 0 || vitals.Health > stats.HealthMax ||
            vitals.Hunger < 0 || vitals.Hunger > stats.HungerMax ||
            Math.Abs((long)moveRemainderX) >= CharacterMovement.Denominator || Math.Abs((long)moveRemainderZ) >= CharacterMovement.Denominator)
            throw new InvalidDataException("Invalid character resources or movement remainder.");
    }
}
