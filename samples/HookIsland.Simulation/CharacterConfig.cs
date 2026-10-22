using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Room-owned character base values. Speed is millimetres/second; resources are whole points.</summary>
public sealed class CharacterConfig
{
    public const int MaximumPoints = 20000, MaximumSpeed = 30000;
    public CharacterConfig(int health = 100, int moveSpeed = 3000, int armor = 0, int hunger = 100)
    {
        if (health < 1 || health > 10000 || moveSpeed < 1 || moveSpeed > MaximumSpeed ||
            armor < 0 || armor > 10000 || hunger < 1 || hunger > 10000)
            throw new InvalidDataException("Invalid character configuration.");
        Health = health; MoveSpeed = moveSpeed; Armor = armor; Hunger = hunger;
    }
    public int Health { get; }
    public int MoveSpeed { get; }
    public int Armor { get; }
    public int Hunger { get; }
    public void Write(BinaryWriter writer)
    { writer.Write(Health); writer.Write(MoveSpeed); writer.Write(Armor); writer.Write(Hunger); }
    public static CharacterConfig Read(BinaryReader reader) =>
        new CharacterConfig(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
}
