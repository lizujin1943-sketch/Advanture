using System;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class OceanStateCodec
{
    internal static void Write(BinaryWriter w,HookState state)
    {
        var s=state.Ocean;w.Write(0x314E434F); // OCN1
        w.Write(s.RewardedStage);w.Write(s.LastCatchTick);w.Write(s.ThreatWindowStart);w.Write(s.ThreatCount);w.Write(s.HandledAttacks);
        w.Write(s.QuietUntil);w.Write(s.ReliefReadyTick);w.Write(s.Sequence);w.Write((byte)s.LastEvent);w.Write(s.LastEventTick);w.Write(s.Visits.Count);
        foreach(var v in s.Visits){w.Write(v.Active?(byte)1:(byte)0);w.Write(v.HomeX);w.Write(v.HomeZ);w.Write(v.Until);}
    }
    internal static HookState Read(BinaryReader r,HookState state)
    {
        if(r.ReadInt32()!=0x314E434F)throw new InvalidDataException("Ocean state version mismatch.");
        int stage=r.ReadInt32();ulong last=r.ReadUInt64(),start=r.ReadUInt64();int threat=r.ReadInt32();uint attacks=r.ReadUInt32();
        ulong quiet=r.ReadUInt64(),ready=r.ReadUInt64();uint sequence=r.ReadUInt32();var kind=(OceanEvent)r.ReadByte();ulong eventTick=r.ReadUInt64();int count=r.ReadInt32();
        if(count<0 || count>4)throw new InvalidDataException("Invalid ocean visit count.");
        var visits=new OceanVisit[count];
        for(int i=0;i<count;i++)
        {
            byte active=r.ReadByte();int x=r.ReadInt32(),z=r.ReadInt32();ulong until=r.ReadUInt64();
            if(active>1 || (active==0 && (x!=0 || z!=0 || until!=0)))throw new InvalidDataException("Invalid ocean visit flag.");
            visits[i]=active==1?new OceanVisit(x,z,until):default;
        }
        var ocean=new OceanState(stage,last,start,threat,attacks,quiet,ready,sequence,kind,eventTick,visits);
        var result=new HookState(state.Steps,state.Players,state.Fish,state.Coins,state.Tiles,state.Driftwood,state.Tackle,state.Enemies,state.RoomRules,state.Treasure,state.Commissions,ocean);
        Validate(result);return result;
    }
    private static void Validate(HookState state)
    {
        var c=state.Tackle?.Config.Ocean??OceanConfig.Disabled;var s=state.Ocean;ulong now=state.Steps;
        if(!c.Enabled)
        {
            if(s.RewardedStage!=0 || s.LastCatchTick!=0 || s.ThreatWindowStart!=0 || s.ThreatCount!=0 || s.HandledAttacks!=0 ||
                s.QuietUntil!=0 || s.ReliefReadyTick!=0 || s.Sequence!=0 || s.LastEvent!=OceanEvent.None || s.LastEventTick!=0 || s.Visits.Count!=0)
                throw new InvalidDataException("Disabled ocean has active state.");
            return;
        }
        if(state.Tiles.Count==0 || s.Visits.Count!=c.VisitSlots || state.Fish.Count!=c.Unlocks.Count+c.VisitSlots ||
            s.RewardedStage<0 || s.RewardedStage>OceanRules.Stage(state.Commissions.Completed) || s.LastCatchTick>now || s.ThreatWindowStart>now ||
            s.ThreatCount<0 || s.ThreatCount>c.PressureCount || (s.ThreatCount==0)!=(s.ThreatWindowStart==0) ||
            (s.ThreatCount>0 && now-s.ThreatWindowStart>=(ulong)c.PressureWindowTicks) ||
            s.HandledAttacks!=unchecked((uint)state.Enemies.Shots+(uint)state.Enemies.Interruptions) ||
            s.QuietUntil>OceanSystem.After(now,c.RecoveryTicks) || s.ReliefReadyTick>OceanSystem.After(now,c.RecoveryCooldownTicks) ||
            (s.QuietUntil==0)!=(s.ReliefReadyTick==0) || s.QuietUntil>s.ReliefReadyTick ||
            s.LastEvent>OceanEvent.Recovery || s.LastEventTick>now || s.Sequence>now ||
            (s.Sequence==0)!=(s.LastEvent==OceanEvent.None) || (s.Sequence==0)!=(s.LastEventTick==0) ||
            (s.Sequence==0 && (s.RewardedStage!=0 || s.QuietUntil!=0)) ||
            (s.LastEvent==OceanEvent.MiddleSchool && s.RewardedStage<1) || (s.LastEvent==OceanEvent.RareSchool && s.RewardedStage!=2) ||
            (s.LastEvent==OceanEvent.Recovery && s.QuietUntil!=OceanSystem.After(s.LastEventTick,c.RecoveryTicks)))
            throw new InvalidDataException("Invalid ocean schedule.");
        for(int i=0;i<c.Unlocks.Count;i++)
            if(c.Unlocks[i]>OceanRules.Stage(state.Commissions.Completed) && !Dormant(state.Fish[i]))
                throw new InvalidDataException("Locked ocean school is active.");
        for(int i=0;i<s.Visits.Count;i++)
        {
            var v=s.Visits[i];var f=state.Fish[c.Unlocks.Count+i];
            if(!v.Active)
            {
                if(!Dormant(f) || f.Species!=0 || f.WeightGrams!=1000 || f.Grade!=1)
                    throw new InvalidDataException("Inactive ocean visit contains a fish.");
            }
            else if(s.Sequence==0 || Math.Abs((long)v.HomeX)>50000 || Math.Abs((long)v.HomeZ)>50000 || v.Until==0 ||
                v.Until>OceanSystem.After(now,c.VisitTicks) || f.Phase==FishPhase.Waiting ||
                (v.Until<=now && f.Phase!=FishPhase.Hooked && f.Phase!=FishPhase.Escaping))
                throw new InvalidDataException("Invalid active ocean visit.");
        }
    }
    private static bool Dormant(Fish f)=>f.Phase==FishPhase.Waiting && f.Timer==0 && f.Owner==-1 && !f.Escape.Active && f.Y==-700 && f.Heading==(f.Id%2==0?1:-1);
    internal static void ValidateMap(IslandMap map,HookState state)
    {
        var c=map.Fishing?.Ocean;if(c?.Enabled!=true)return;
        for(int i=0;i<state.Fish.Count;i++)
        {
            bool hidden=i<c.Unlocks.Count?c.Unlocks[i]>OceanRules.Stage(state.Commissions.Completed):!state.Ocean.Visits[i-c.Unlocks.Count].Active;
            if(!hidden)continue;
            var expected=OceanRules.Dormant(i,map.FishSpots[i],state.Fish[i].Generation);
            if(state.Fish[i].X!=expected.X || state.Fish[i].Z!=expected.Z)throw new InvalidDataException("Inactive fish moved from its school.");
        }
    }
}
