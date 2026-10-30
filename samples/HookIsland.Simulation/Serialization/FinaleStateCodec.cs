using System;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class FinaleStateCodec
{
    internal static void Write(BinaryWriter w,HookState state)
    {
        var s=state.Finale;w.Write(0x314E4946); // FIN1 follows all hook target types.
        w.Write((byte)s.Phase);w.Write(s.Completed);w.Write(s.Timer);w.Write(s.Progress);w.Write(s.SpentBait);
        w.Write(s.X);w.Write(s.Z);w.Write(s.PreviousX);w.Write(s.PreviousZ);w.Write(s.Direction);w.Write(s.VictoryTick);
        foreach(var p in state.Players){w.Write(p.HookedFinale?(byte)1:(byte)0);w.Write(p.FinaleDrainRemainder);}
    }
    internal static HookState Read(BinaryReader r,HookState state)
    {
        if(r.ReadInt32()!=0x314E4946)throw new InvalidDataException("Finale state version mismatch.");
        var finale=new FinaleState((FinalePhase)r.ReadByte(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),
            r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadUInt64());
        var players=state.Players.ToArray();
        for(int i=0;i<players.Length;i++)
        {
            byte hooked=r.ReadByte();if(hooked>1)throw new InvalidDataException("Invalid finale line flag.");
            players[i].HookedFinale=hooked==1;players[i].FinaleDrainRemainder=r.ReadInt32();
        }
        var result=new HookState(state.Steps,players,state.Fish,state.Coins,state.Tiles,state.Driftwood,state.Tackle,state.Enemies,
            state.RoomRules,state.Treasure,state.Commissions,state.Ocean,finale);
        Validate(result);return result;
    }
    private static void Validate(HookState state)
    {
        var s=state.Finale;var c=state.Tackle?.Config.Finale??FinaleConfig.Disabled;
        if(s.Phase>FinalePhase.Victory || s.Completed<0 || s.Completed>3 || s.Direction<0 || s.Direction>3 ||
            s.Timer<0 || s.Progress<0 || s.Progress>=c.HoldTicks)
            throw new InvalidDataException("Invalid finale stage.");
        if(s.Phase==FinalePhase.Dormant)
        {
            if(s.Completed!=0 || s.Timer!=0 || s.Progress!=0 || s.SpentBait!=0 || s.X!=0 || s.Z!=0 ||
                s.PreviousX!=0 || s.PreviousZ!=0 || s.Direction!=0 || s.VictoryTick!=0)
                throw new InvalidDataException("Noncanonical dormant finale.");
        }
        else
        {
            if(!c.Enabled || state.Tiles.Count==0 || state.Steps==0 || s.SpentBait!=c.BaitCost || state.Commissions.Bait<s.SpentBait ||
                state.Commissions.Completed!=state.Tackle!.Config.Commissions.Definitions.Count)
                throw new InvalidDataException("Finale lacks its completed commissions and spent bait.");
            if(s.Phase==FinalePhase.Summoning)
            {
                if(s.Completed!=0 || s.Timer>c.SummonTicks || s.Progress!=0 || s.X!=0 || s.Z!=0 ||
                    s.PreviousX!=0 || s.PreviousZ!=0 || s.Direction!=0 || s.VictoryTick!=0)
                    throw new InvalidDataException("Invalid finale summon.");
            }
            else
            {
                var d=FinaleRules.Direction(s.Direction);
                int bankX=s.X-d.X*c.EdgeOffset,bankZ=s.Z-d.Z*c.EdgeOffset;
                if(Math.Abs((long)s.X)>50000-c.BodyRadius || Math.Abs((long)s.Z)>50000-c.BodyRadius ||
                    Math.Abs((long)s.PreviousX)>50000-c.BodyRadius || Math.Abs((long)s.PreviousZ)>50000-c.BodyRadius ||
                    bankX%2000!=0 || bankZ%2000!=0 || !PlatformRules.Contains(state.Tiles,bankX/2000,bankZ/2000) ||
                    (s.X==s.PreviousX && s.Z==s.PreviousZ))
                    throw new InvalidDataException("Invalid finale bank.");
                bool first=s.Phase==FinalePhase.Surfaced?s.Completed==0:s.Completed==1;
                if(first && (s.PreviousX!=0 || s.PreviousZ!=0) || !first && s.PreviousX==0 && s.PreviousZ==0)
                    throw new InvalidDataException("Invalid previous finale bank.");
                if(s.Phase==FinalePhase.Surfaced)
                {
                    var anchor=FinaleRules.Anchor(c,s);
                    if(s.Completed>2 || s.Timer!=0 || s.VictoryTick!=0 || !PlatformRules.CanStand(state.Tiles,anchor.X,anchor.Z) ||
                        state.Tiles.Any(t=>FinaleRules.BlocksBuild(c,s,t.X,t.Z)))
                        throw new InvalidDataException("Invalid surfaced finale.");
                }
                else if(s.Phase==FinalePhase.Submerged)
                {
                    if(s.Completed<1 || s.Completed>2 || s.Timer>c.RelocateTicks || s.Progress!=0 || s.VictoryTick!=0)
                        throw new InvalidDataException("Invalid submerged finale.");
                }
                else if(s.Completed!=3 || s.Timer!=0 || s.Progress!=0 || s.VictoryTick!=state.Steps ||
                    state.Players.Any(p=>p.Phase!=HookPhase.Ready || p.PulledBy>=0))
                    throw new InvalidDataException("Invalid finale victory.");
            }
        }
        foreach(var p in state.Players)
        {
            if(p.FinaleDrainRemainder<0 || p.FinaleDrainRemainder>=12 || !p.HookedFinale && p.FinaleDrainRemainder!=0)
                throw new InvalidDataException("Invalid finale stamina remainder.");
            if(!p.HookedFinale)continue;
            var rod=FishingRules.Equipped(state,p);
            if(!c.Enabled || s.Phase!=FinalePhase.Surfaced || !FinaleRules.CanHold(p) || p.Phase!=HookPhase.Returning ||
                p.HookX!=s.X || p.HookZ!=s.Z || p.FishId>=0 || p.HookedPiranha>0 || p.DriftwoodId>=0 ||
                p.HookedChest>0 || p.HookedPlayer>=0 || rod==null || IslandMap.DistanceSquared(p.X,p.Z,s.X,s.Z)>(long)rod.Range*rod.Range)
                throw new InvalidDataException("Invalid finale hook ownership.");
        }
        if(s.Progress>0 && (FinaleRules.RequiredHooks(c,state.Players)==0 || FinaleRules.Count(state.Players)<FinaleRules.RequiredHooks(c,state.Players)))
            throw new InvalidDataException("Finale progress without enough hooks.");
    }
    internal static void ValidateMap(IslandMap map,HookState state)
    {
        if(state.Finale.Phase==FinalePhase.Surfaced && !FinalePlacement.Water(map,map.Fishing!.Finale,state.Tiles,new Point(state.Finale.X,state.Finale.Z)))
            throw new InvalidDataException("Finale overlaps the platform or reserved cells.");
    }
}
