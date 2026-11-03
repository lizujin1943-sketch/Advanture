using System;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class BossStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        var b=s.Boss;w.Write(0x31535741);w.Write((byte)b.Phase);w.Write(b.Health);w.Write(b.MaxHealth);w.Write(b.ParticipantCount);w.Write(b.Attackers);w.Write(b.Rewarded);
        w.Write(b.PhaseTicks);w.Write(b.Sequence);w.Write(b.InkTicks);w.Write(b.InkStartTick);w.Write(b.IdleTicks);w.Write(b.InkWindupTicks);w.Write(b.InkCooldownTicks);w.Write(b.NoTargetTicks);w.Write(b.Enraged);
        w.Write(b.Limbs.Count);foreach(var l in b.Limbs){w.Write(l.Id);w.Write(l.Health);w.Write(l.MaxHealth);w.Write((byte)l.Attack);w.Write(l.AttackTicks);w.Write(l.SeverTicks);w.Write(l.TargetSlot);w.Write(l.HitPlayers);}
        w.Write(b.Receipts.Count);foreach(var r in b.Receipts){w.Write(r.Request);w.Write((byte)r.Result);}
        w.Write(b.DestroyedTiles.Count);foreach(var t in b.DestroyedTiles){w.Write(t.X);w.Write(t.Z);w.Write((byte)t.Kind);w.Write(t.Builder);w.Write(t.Paid);}
    }
    private static bool Flag(BinaryReader r){byte b=r.ReadByte();if(b>1)throw new InvalidDataException("Invalid boss flag.");return b==1;}
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x31535741)throw new InvalidDataException("Boss state version mismatch.");
        var phase=(BossPhase)r.ReadByte();int health=r.ReadInt32(),max=r.ReadInt32(),party=r.ReadInt32();byte attackers=r.ReadByte();bool rewarded=Flag(r);
        int phaseTicks=r.ReadInt32();uint sequence=r.ReadUInt32();int ink=r.ReadInt32();ulong inkStart=r.ReadUInt64();int idle=r.ReadInt32(),animation=r.ReadInt32(),cooldown=r.ReadInt32(),absent=r.ReadInt32();bool enraged=Flag(r);
        int count=r.ReadInt32();if(count!=0 && count!=8)throw new InvalidDataException("Invalid boss limb count.");var limbs=new BossLimbState[count];
        for(int i=0;i<count;i++)limbs[i]=new BossLimbState(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),(BossAttack)r.ReadByte(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadByte());
        count=r.ReadInt32();if(count<0 || count>s.Players.Count)throw new InvalidDataException("Invalid boss receipts.");var receipts=new BossRequestReceipt[count];
        for(int i=0;i<count;i++)receipts[i]=new BossRequestReceipt(r.ReadUInt32(),(BossRequestResult)r.ReadByte());
        count=r.ReadInt32();if(count<0 || count>2048)throw new InvalidDataException("Invalid boss loss ledger.");var lost=new BossTileLoss[count];
        for(int i=0;i<count;i++)lost[i]=new BossTileLoss(r.ReadInt32(),r.ReadInt32(),(TileKind)r.ReadByte(),r.ReadInt32(),r.ReadInt64());
        var boss=new BossState(phase,health,max,party,attackers,rewarded,phaseTicks,sequence,ink,inkStart,limbs,receipts,lost,idle,animation,cooldown,absent,enraged);
        var result=new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,boss);
        Validate(result);return result;
    }
    internal static void Validate(HookState s)
    {
        var c=s.Tackle?.Config.Boss??BossConfig.Disabled;var b=s.Boss;int mask=(1<<s.Players.Count)-1;
        if(b.Phase>BossPhase.Defeated || b.PhaseTicks<0 || b.ParticipantCount<0 || b.ParticipantCount>s.Players.Count || (b.Attackers&~mask)!=0 ||
            b.InkTicks<0 || b.InkTicks>BossConfig.InkDurationTicks || b.InkStartTick>s.Steps || b.InkWindupTicks<0 || b.InkWindupTicks>=BossConfig.InkFinishTicks ||
            b.IdleTicks<0 || b.IdleTicks>90 || b.InkCooldownTicks<0 || b.InkCooldownTicks>360 || b.NoTargetTicks<0 || b.NoTargetTicks>=300 ||
            (b.InkTicks>0 && (b.InkStartTick==0 || s.Steps-b.InkStartTick>180)) || b.Rewarded && b.Phase!=BossPhase.Defeated)
            throw new InvalidDataException("Invalid boss clock or receipt.");
        if(b.Receipts.Count!=0 && b.Receipts.Count!=s.Players.Count || b.Receipts.Any(r=>r.Result>BossRequestResult.Exhausted || (r.Request==0)!=(r.Result==BossRequestResult.None)))
            throw new InvalidDataException("Invalid boss request receipt.");
        if(!c.Enabled)
        {
            if(b.Phase!=BossPhase.Dormant || b.Health!=0 || b.MaxHealth!=0 || b.ParticipantCount!=0 || b.Attackers!=0 || b.Rewarded || b.Sequence!=0 || b.PhaseTicks!=0 ||
                b.Limbs.Count!=0 || b.DestroyedTiles.Count!=0 || b.Enraged || b.InkTicks!=0 || b.InkWindupTicks!=0 || b.InkCooldownTicks!=0 || b.InkStartTick!=0 || b.IdleTicks!=0 || b.NoTargetTicks!=0 || b.Receipts.Any(r=>r.Result!=BossRequestResult.None && r.Result!=BossRequestResult.Disabled))
                throw new InvalidDataException("Disabled boss has live state.");
            return;
        }
        if(b.Receipts.Count!=s.Players.Count || b.Limbs.Count!=8 || b.MaxHealth!=c.ScaledBody(Math.Max(1,b.ParticipantCount)) || b.Health<0 || b.Health>b.MaxHealth ||
            (b.Phase!=BossPhase.Dormant && (b.ParticipantCount==0 || b.Sequence==0)) || (b.Phase==BossPhase.Dormant && (b.ParticipantCount!=0 || b.Health!=b.MaxHealth || b.Attackers!=0 || b.Enraged || b.PhaseTicks!=0)) ||
            ((b.Phase==BossPhase.Dying || b.Phase==BossPhase.Defeated)!=(b.Health==0)) || (b.Health==0 && b.Attackers==0) ||
            (b.Phase==BossPhase.Emerge && b.PhaseTicks>=120) || (b.Phase==BossPhase.Enrage && (!b.Enraged || b.PhaseTicks>=90)) ||
            (b.Phase==BossPhase.Dying && b.PhaseTicks>=180) || (b.Phase==BossPhase.Defeated && b.PhaseTicks!=0))throw new InvalidDataException("Invalid boss phase or health.");
        bool inactive=b.Phase!=BossPhase.Combat;
        if(inactive && (b.InkTicks!=0 || b.InkWindupTicks!=0) || b.Limbs.Count(l=>l.Attack!=BossAttack.None)>(b.Enraged?2:1))throw new InvalidDataException("Invalid boss attack concurrency.");
        for(int i=0;i<8;i++)
        {
            var l=b.Limbs[i];int expected=c.ScaledLimb(i,Math.Max(1,b.ParticipantCount));
            if(l.Id!=i || l.MaxHealth!=expected || l.Health<0 || l.Health>expected || l.Attack>BossAttack.Sweep || l.AttackTicks<0 || l.SeverTicks<0 || l.SeverTicks>78 ||
                l.TargetSlot< -1 || l.TargetSlot>=s.Players.Count || (l.HitPlayers&~mask)!=0 || l.Health>0 && l.SeverTicks!=0 ||
                (b.Phase==BossPhase.Dormant && l.Health!=l.MaxHealth) || (l.Attack!=BossAttack.None && (i>=4 || l.Health==0 || inactive || l.TargetSlot<0)) ||
                (l.Attack==BossAttack.None && (l.AttackTicks!=0 || l.TargetSlot!=-1 || l.HitPlayers!=0)) ||
                l.Attack==BossAttack.Slam && (l.AttackTicks>=90 || l.AttackTicks<45 && l.HitPlayers!=0) ||
                l.Attack==BossAttack.Sweep && (l.AttackTicks>=108 || l.AttackTicks<36 && l.HitPlayers!=0))throw new InvalidDataException("Invalid boss limb.");
        }
        foreach(var player in s.Players)if(player.WaitingToJoin && (b.Attackers&(1<<player.Slot))!=0)throw new InvalidDataException("Unjoined boss contributor.");
        if(b.DestroyedTiles.Count>0 && b.Sequence==0)throw new InvalidDataException("Boss damage has no attempt.");
        var initial=b.DestroyedTiles.Count==0?Array.Empty<PlatformTile>():BossArena.InitialTiles(s.Tackle?.Config);
        var keys=new System.Collections.Generic.HashSet<(int,int,TileKind,int)>();
        foreach(var loss in b.DestroyedTiles)
            if(!BossArena.Destructible(loss.X,loss.Z) || !PlatformRules.InBounds(loss.X,loss.Z) || loss.Kind<TileKind.Wood || loss.Kind>TileKind.Fishbone || loss.Builder< -1 || loss.Builder>=s.Players.Count || loss.Paid<0 || loss.Paid>4L*int.MaxValue ||
                (loss.Builder==-1)!=(loss.Paid==0) || loss.Builder==-1 && (!initial.Any(t=>t.X==loss.X && t.Z==loss.Z) || loss.Kind!=TileKind.Wood) || !keys.Add((loss.X,loss.Z,loss.Kind,loss.Builder)))
                throw new InvalidDataException("Invalid boss destroyed tile ledger.");
    }
}
