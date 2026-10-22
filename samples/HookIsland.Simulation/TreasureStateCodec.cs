using System;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class TreasureStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        w.Write(0x31525354); // TSR1
        foreach(var p in s.Players)
        {w.Write(p.HookedChest);w.Write(p.ChestJoinOrder);w.Write(p.TreasuresCollected);w.Write(p.LastTreasureRequest);w.Write((byte)p.LastTreasureResult);}
        w.Write(s.Treasure.Chests.Count);
        foreach(var c in s.Treasure.Chests)
        {
            w.Write(c.Id);w.Write((byte)c.Kind);w.Write((byte)c.Phase);w.Write(c.Generation);w.Write(c.X);w.Write(c.Z);
            w.Write(c.DirectionX);w.Write(c.DirectionZ);w.Write(c.TravelRemainder);w.Write(c.Timer);w.Write(c.Owner);
            w.Write(c.RewardMembers);w.Write(c.Coins);w.Write(c.Wood);w.Write(c.Bones);
        }
    }
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x31525354)throw new InvalidDataException("Treasure state version mismatch.");
        var players=s.Players.ToArray();
        for(int i=0;i<players.Length;i++)
        {
            players[i].HookedChest=r.ReadInt32();players[i].ChestJoinOrder=r.ReadInt32();players[i].TreasuresCollected=r.ReadInt32();
            players[i].LastTreasureRequest=r.ReadUInt32();players[i].LastTreasureResult=(TreasureResult)r.ReadByte();
        }
        int count=r.ReadInt32();var config=s.Tackle?.Config.Treasure;
        if(count<0 || count>16 || count!=(s.Tiles.Count==0 || config==null?0:config.Count(players.Length)))
            throw new InvalidDataException("Invalid treasure count.");
        var chests=new TreasureChest[count];
        for(int i=0;i<count;i++)chests[i]=new TreasureChest {
            Id=r.ReadInt32(),Kind=(TreasureKind)r.ReadByte(),Phase=(TreasurePhase)r.ReadByte(),Generation=r.ReadUInt32(),X=r.ReadInt32(),Z=r.ReadInt32(),
            DirectionX=r.ReadInt32(),DirectionZ=r.ReadInt32(),TravelRemainder=r.ReadInt32(),Timer=r.ReadInt32(),Owner=r.ReadInt32(),
            RewardMembers=r.ReadInt32(),Coins=r.ReadInt32(),Wood=r.ReadInt32(),Bones=r.ReadInt32()};
        foreach(var p in players)
        {
            if(p.HookedChest<0 || p.HookedChest>count || p.ChestJoinOrder<0 || p.ChestJoinOrder>=players.Length || p.TreasuresCollected<0 ||
                p.LastTreasureResult>TreasureResult.LimitReached || (p.LastTreasureRequest==0)!=(p.LastTreasureResult==TreasureResult.None) ||
                (p.HookedChest==0 && p.ChestJoinOrder!=0))throw new InvalidDataException("Invalid treasure player metadata.");
            if(p.HookedChest==0)continue;
            var c=chests[p.HookedChest-1];var rod=FishingRules.Equipped(s,p);
            if(c.Phase!=TreasurePhase.Hooked || p.Phase!=HookPhase.Returning || p.Unavailable || p.Vitals.Downed || p.Knockback.Active ||
                p.FishId>=0 || p.HookedPiranha>0 || p.HookedPlayer>=0 || p.DriftwoodId>=0 || p.HookX!=c.X || p.HookZ!=c.Z || rod==null ||
                IslandMap.DistanceSquared(p.X,p.Z,c.X,c.Z)>(long)rod.Range*rod.Range)throw new InvalidDataException("Invalid treasure line.");
        }
        for(int i=0;i<count;i++)
        {
            var c=chests[i];var members=players.Where(p=>p.HookedChest==i+1).OrderBy(p=>p.ChestJoinOrder).ToArray();
            if(c.Id!=i || c.Kind!=config!.Kind(i) || c.Phase>TreasurePhase.Landed || c.Owner< -1 || c.Owner>=players.Length ||
                Math.Abs((long)c.X)>SwimmingTerrain.WorldLimit || Math.Abs((long)c.Z)>SwimmingTerrain.WorldLimit ||
                c.Timer<0 || c.Timer>45000 || c.TravelRemainder<0 || c.TravelRemainder>=HookGame.Rate ||
                c.RewardMembers<0 || c.RewardMembers>=(1<<players.Length) ||
                !members.Select(p=>p.ChestJoinOrder).SequenceEqual(Enumerable.Range(0,members.Length)))
                throw new InvalidDataException("Invalid treasure state.");
            if(c.Phase==TreasurePhase.Waiting)
            {
                if(c.Owner!=-1 || members.Length!=0 || c.X!=0 || c.Z!=0 || c.DirectionX!=0 || c.DirectionZ!=0 || c.TravelRemainder!=0 ||
                    c.RewardMembers!=0 || c.Coins!=0 || c.Wood!=0 || c.Bones!=0)throw new InvalidDataException("Invalid waiting treasure.");
                continue;
            }
            TreasureMotion.Rewards(config,c.Kind,c.Id,c.Generation,s.Tackle!.Config.Seed,out int coins,out int wood,out int bones);
            if(c.Generation==0 || c.Timer<1 || c.Timer>config.FloatLifetimeTicks || c.Coins!=coins || c.Wood!=wood || c.Bones!=bones ||
                !(Math.Abs((long)c.DirectionX)==1000 && c.DirectionZ==0 || Math.Abs((long)c.DirectionZ)==1000 && c.DirectionX==0))
                throw new InvalidDataException("Invalid treasure contents/motion.");
            if(c.Phase==TreasurePhase.Hooked)
            {
                if(members.Length==0 || c.Owner!=members[0].Slot || c.RewardMembers!=0 || (c.Kind!=TreasureKind.Large && members.Length!=1))
                    throw new InvalidDataException("Invalid hooked treasure.");
            }
            else if(c.Phase==TreasurePhase.Landed)
            {
                if(c.Kind!=TreasureKind.Large || members.Length!=0 || c.Owner<0 || (c.RewardMembers&(1<<c.Owner))==0 ||
                    c.TravelRemainder!=0 || !PlatformRules.CanStand(s.Tiles,c.X,c.Z))throw new InvalidDataException("Invalid landed treasure.");
            }
            else if(c.Owner!=-1 || members.Length!=0 || c.RewardMembers!=0)throw new InvalidDataException("Invalid floating treasure.");
        }
        return new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,new TreasureState(chests));
    }
}
