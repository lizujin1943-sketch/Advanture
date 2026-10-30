using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Persistence;
using FrameSync.Runtime;

namespace Advanture.HookIsland.Network;

/// <summary>Immutable confirmed world and seat ownership; loading never touches a running room.</summary>
public sealed class IslandSave
{
    private readonly byte[][] profiles;
    private readonly bool[] entered;
    internal IslandSave(SaveDocument document,DateTime savedAtUtc,IReadOnlyList<byte[]> profiles,IReadOnlyList<bool> entered)
    {
        Document=document??throw new ArgumentNullException(nameof(document));
        if(savedAtUtc.Kind!=DateTimeKind.Utc)throw new ArgumentException("Save time must be UTC.");
        SavedAtUtc=savedAtUtc;var cp=document.Checkpoint;int count=cp.Session.Participants.Count;
        if(count<1 || count>4 || profiles.Count!=count || entered.Count!=count || !entered[0])throw new InvalidDataException("Invalid saved seats.");
        this.profiles=profiles.Select(p=>p.ToArray()).ToArray();this.entered=entered.ToArray();
        for(int i=0;i<count;i++)
            if(cp.Session.Participants[i].Slot!=i || this.profiles[i].Length!=(entered[i]?32:0) ||
                entered[i] && this.profiles.Take(i).Any(p=>p.AsSpan().SequenceEqual(this.profiles[i])))
                throw new InvalidDataException("Invalid saved ownership.");
        if(cp.Session.TickRate!=HookGame.Rate)throw new InvalidDataException("Unsupported saved tick rate.");
        Map=IslandMap.Decode(cp.InitializationData.Span);
        // Framework envelope validation alone cannot prove gameplay compatibility or restore fidelity.
        try
        {
            var runner=new SimulationRunner<HookState>(HookGame.CreateModule(),cp.Session,cp.Limits,cp.InitializationData);runner.Restore(cp);
            var state=runner.InspectState();
            if(state.Players.Count!=count || state.Players.Where((p,i)=>p.WaitingToJoin==entered[i]).Any())
                throw new InvalidDataException("Saved character ownership disagrees with the world.");
        }
        catch(ArgumentException e){throw new InvalidDataException("This save is incompatible with the current game version.",e);}
    }
    public SaveDocument Document { get; }
    public DateTime SavedAtUtc { get; }
    public IslandMap Map { get; }
    public int Capacity=>entered.Length;
    internal IReadOnlyList<byte[]> Profiles=>profiles.Select(p=>(byte[])p.Clone()).ToArray();
    internal IReadOnlyList<bool> Entered=>Array.AsReadOnly(entered);
}
