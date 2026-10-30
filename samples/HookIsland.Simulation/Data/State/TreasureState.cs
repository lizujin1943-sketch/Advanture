using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public enum TreasureKind : byte { Small, Rare, Large }
public enum TreasurePhase : byte { Waiting, Floating, Hooked, Landed }
public enum TreasureResult : byte { None, Success, Disabled, NotFound, NotLanded, Busy, TooFar, LimitReached }

public struct TreasureChest
{
    public int Id,X,Z,DirectionX,DirectionZ,TravelRemainder,Timer,Owner;
    public uint Generation;
    public TreasureKind Kind;
    public TreasurePhase Phase;
    // Frozen at landing, independent of the person who subsequently opens a large chest.
    public int RewardMembers,Coins,Wood,Bones;
}

public sealed class TreasureState
{
    public static TreasureState Empty { get; }=new TreasureState(Array.Empty<TreasureChest>());
    public TreasureState(IEnumerable<TreasureChest> chests){Chests=Array.AsReadOnly(chests.ToArray());}
    public IReadOnlyList<TreasureChest> Chests { get; }
}
