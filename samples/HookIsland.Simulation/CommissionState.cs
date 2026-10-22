using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public readonly struct CommissionProgress
{
    public CommissionProgress(int fish,int rare){Fish=fish;Rare=rare;}
    public int Fish { get; }
    public int Rare { get; }
}
public sealed class CommissionState
{
    public static CommissionState Empty { get; }=new CommissionState(0,0,Array.Empty<CommissionProgress>());
    public CommissionState(int completed,int bait,IEnumerable<CommissionProgress> entries)
    {Completed=completed;Bait=bait;Entries=Array.AsReadOnly(entries.ToArray());}
    public int Completed { get; }
    public int Bait { get; }
    public IReadOnlyList<CommissionProgress> Entries { get; }
}
