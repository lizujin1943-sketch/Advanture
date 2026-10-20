using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public enum HookPhase : byte { Ready, Flying, Returning }
public enum FishPhase : byte { Waiting, Warning, Jumping, Hooked }

// Mutable structs are kept only inside the simulation; captures expose independent, read-only arrays.
public struct Fisher
{
    public ushort Slot;
    public int X, Z, AimX, AimZ, RemainderX, RemainderZ;
    public HookPhase Phase;
    public int HookX, HookZ, DirectionX, DirectionZ, Distance, FishId, Cooldown;
    public int Catches, Coins, Casts;
}
public struct Fish
{
    public int Id, X, Z, Y, Timer, Owner;
    public FishPhase Phase;
}
public sealed class HookState
{
    public HookState(ulong steps, IEnumerable<Fisher> players, IEnumerable<Fish> fish, int coins)
    { Steps=steps;Players=Array.AsReadOnly(players.ToArray());Fish=Array.AsReadOnly(fish.ToArray());Coins=coins; }
    public ulong Steps { get; }
    public IReadOnlyList<Fisher> Players { get; }
    public IReadOnlyList<Fish> Fish { get; }
    public int Coins { get; }
}
