using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class CooperativeFishingStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        w.Write(0x31504F43); // COP1
        foreach(var p in s.Players){w.Write(p.FishingJoinOrder);w.Write(p.FishingDrainRemainder);}
    }
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x31504F43)throw new InvalidDataException("Cooperative fishing state version mismatch.");
        var players=s.Players.ToArray();
        for(int i=0;i<players.Length;i++)
        {
            players[i].FishingJoinOrder=r.ReadInt32();players[i].FishingDrainRemainder=r.ReadInt32();var p=players[i];
            if(p.FishingJoinOrder<0 || p.FishingJoinOrder>=players.Length || p.FishingDrainRemainder<0 || p.FishingDrainRemainder>=12 ||
                ((!CooperativeFishing.Attached(p) || s.Tackle==null) && (p.FishingJoinOrder!=0 || p.FishingDrainRemainder!=0)))
                throw new InvalidDataException("Invalid cooperative line metadata.");
        }
        var state=new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies);
        if(s.Tackle==null)return state;
        foreach(var p in players.Where(CooperativeFishing.Attached))
        {
            if(p.FishId>=s.Fish.Count || p.HookedPiranha>s.Enemies.Piranhas.Count ||
                p.Phase!=HookPhase.Returning || p.Unavailable || p.Vitals.Downed || p.Knockback.Active || p.DriftwoodId>=0 || p.HookedPlayer>=0 ||
                (p.FishId>=0 && p.HookedPiranha>0))throw new InvalidDataException("Invalid cooperative connection.");
            int lead=CooperativeFishing.Lead(state,p);
            if(lead<0 || !CooperativeFishing.SameTarget(players[lead],p))throw new InvalidDataException("Missing cooperative lead.");
            int x=p.FishId>=0?s.Fish[p.FishId].X:s.Enemies.Piranhas[p.HookedPiranha-1].X;
            int z=p.FishId>=0?s.Fish[p.FishId].Z:s.Enemies.Piranhas[p.HookedPiranha-1].Z;
            if(p.HookX!=x || p.HookZ!=z)throw new InvalidDataException("Cooperative hook position differs from target.");
            var ranks=players.Where(q=>CooperativeFishing.SameTarget(q,p)).Select(q=>q.FishingJoinOrder).OrderBy(v=>v).ToArray();
            if(!ranks.SequenceEqual(Enumerable.Range(0,ranks.Length)))throw new InvalidDataException("Invalid cooperative join order.");
        }
        return state;
    }
}
