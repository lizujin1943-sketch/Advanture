using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public readonly struct CommissionDefinition
{
    public CommissionDefinition(int fish,int rare,int bait=1)
    {
        if(fish<1 || fish>1000 || rare<1 || rare>1000 || bait<1 || bait>1000)
            throw new InvalidDataException("Invalid commission target or reward.");
        Fish=fish;Rare=rare;Bait=bait;
    }
    public int Fish { get; }
    public int Rare { get; }
    public int Bait { get; }
}
public sealed class CommissionConfig
{
    public static CommissionConfig Disabled { get; }=new CommissionConfig(enabled:false);
    public CommissionConfig(IEnumerable<CommissionDefinition>? definitions=null,bool enabled=true)
    {
        var entries=(definitions??(enabled?new[]{new CommissionDefinition(6,2),new CommissionDefinition(10,3),new CommissionDefinition(14,4)}:Array.Empty<CommissionDefinition>())).ToArray();
        if(entries.Length>(enabled?16:0) || (enabled && entries.Length==0))throw new InvalidDataException("Invalid commission count.");
        foreach(var d in entries)_=new CommissionDefinition(d.Fish,d.Rare,d.Bait);
        Enabled=enabled;Definitions=Array.AsReadOnly(entries);
    }
    public bool Enabled { get; }
    public IReadOnlyList<CommissionDefinition> Definitions { get; }
    internal void Write(BinaryWriter w)
    {
        w.Write(Enabled?(byte)1:(byte)0);w.Write(Definitions.Count);
        foreach(var d in Definitions){w.Write(d.Fish);w.Write(d.Rare);w.Write(d.Bait);}
    }
    internal static CommissionConfig Read(BinaryReader r)
    {
        byte enabled=r.ReadByte();int count=r.ReadInt32();
        if(enabled>1 || count<0 || count>16)throw new InvalidDataException("Invalid commission configuration.");
        var entries=new CommissionDefinition[count];
        for(int i=0;i<count;i++)entries[i]=new CommissionDefinition(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
        return new CommissionConfig(entries,enabled==1);
    }
}
