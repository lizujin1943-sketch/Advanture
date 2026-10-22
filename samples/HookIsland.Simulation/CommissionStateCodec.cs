using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class CommissionStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        w.Write(0x31534D43); // CMS1
        w.Write(s.Commissions.Completed);w.Write(s.Commissions.Bait);w.Write(s.Commissions.Entries.Count);
        foreach(var e in s.Commissions.Entries){w.Write(e.Fish);w.Write(e.Rare);}
    }
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x31534D43)throw new InvalidDataException("Commission state version mismatch.");
        int completed=r.ReadInt32(),bait=r.ReadInt32(),count=r.ReadInt32();
        if(count<0 || count>16 || count!=(s.Tackle?.Config.Commissions.Definitions.Count??0))throw new InvalidDataException("Invalid commission state count.");
        var entries=new CommissionProgress[count];
        for(int i=0;i<count;i++)entries[i]=new CommissionProgress(r.ReadInt32(),r.ReadInt32());
        var progress=new CommissionState(completed,bait,entries);
        CommissionSystem.Validate(s.Tackle?.Config.Commissions??CommissionConfig.Disabled,progress);
        if(entries.Sum(e=>(long)e.Fish)>s.Players.Sum(p=>(long)p.Catches))throw new InvalidDataException("Commission progress exceeds successful captures.");
        return new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,progress);
    }
}
