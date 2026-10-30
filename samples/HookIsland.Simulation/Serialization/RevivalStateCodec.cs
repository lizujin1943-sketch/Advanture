using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Canonical life-cycle checkpoint fields and cross-player rescue invariants.</summary>
public static class RevivalStateCodec
{
    public static void Write(BinaryWriter w,RevivalState s)
    {w.Write(s.Dead?(byte)1:(byte)0);w.Write(s.DownedTicks);w.Write(s.Rescuer);w.Write(s.RescueTicks);w.Write(s.DeathCost);w.Write(s.CoinsSpent);w.Write(s.DeathBalance);w.Write(s.DeathTick);w.Write(s.Deaths);w.Write(s.LastRequest);w.Write((byte)s.LastResult);w.Write(s.DownedId);}
    public static RevivalState Read(BinaryReader r)
    {
        byte dead=r.ReadByte();if(dead>1)throw new InvalidDataException("Invalid death flag.");
        return new RevivalState{Dead=dead==1,DownedTicks=r.ReadInt32(),Rescuer=r.ReadInt32(),RescueTicks=r.ReadInt32(),DeathCost=r.ReadInt32(),CoinsSpent=r.ReadInt32(),DeathBalance=r.ReadInt32(),DeathTick=r.ReadUInt64(),Deaths=r.ReadUInt32(),LastRequest=r.ReadUInt32(),LastResult=(RespawnResult)r.ReadByte(),DownedId=r.ReadUInt32()};
    }
    public static void Validate(HookState state,Fisher p)
    {
        var c=state.Tackle!.Config.Revival;var s=p.Revival;
        if(s.DownedTicks<0 || s.DownedTicks>c.DownedTicks || s.DeathCost<0 || s.CoinsSpent<0 ||
            s.Rescuer<0 || s.Rescuer>state.Players.Count || s.Rescuer==p.Slot+1 ||
            s.RescueTicks<0 || s.RescueTicks>=c.RescueTicks || (s.Rescuer==0)!=(s.RescueTicks==0) ||
            s.LastResult>RespawnResult.Unavailable || (s.LastRequest==0)!=(s.LastResult==RespawnResult.None) ||
            (!p.Vitals.Downed && (s.Dead || s.DownedTicks!=0 || s.RescueTicks!=0)) ||
            (s.DeathBalance<0 || s.DeathTick>state.Steps) ||
            s.Deaths>s.DownedId ||
            (s.HasQuote && (!p.Vitals.Downed || s.DownedId==0 || s.DeathCost!=c.Cost(s.DeathTick,s.DeathBalance))) ||
            (!s.HasQuote && (s.Dead || s.DeathCost!=0 || s.DeathBalance!=0)) ||
            (s.Dead && (p.Knockback.Active || p.Swimming.Active || s.Deaths==0 || s.DownedTicks!=c.DownedTicks || s.RescueTicks!=0 || p.Phase!=HookPhase.Ready || p.PulledBy>=0 || p.HookedPlayer>=0 || p.Shopping)) ||
            (!s.Dead && s.DownedTicks>=c.DownedTicks))throw new InvalidDataException("Invalid revival state.");
        if(s.Rescuer>0)
        {
            foreach(var other in state.Players)
                if(other.Slot!=p.Slot && other.Revival.Rescuer==s.Rescuer)throw new InvalidDataException("One helper cannot rescue two targets.");
            var h=state.Players[s.Rescuer-1];
            if(h.Unavailable || p.WaitingToJoin || h.Knockback.Active || p.Knockback.Active || h.Vitals.Downed || h.Revival.Dead || h.Swimming.InWater || p.Swimming.InWater || h.PulledBy>=0 || p.PulledBy>=0 ||
                h.Phase!=HookPhase.Ready || h.Shopping || IslandMap.DistanceSquared(h.X,h.Z,p.X,p.Z)>(long)c.RescueRange*c.RescueRange)
                throw new InvalidDataException("Invalid rescue owner.");
        }
    }
}
