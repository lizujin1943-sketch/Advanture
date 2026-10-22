using System;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Enemy and external movement receipts are part of the authoritative checkpoint.</summary>
internal static class EnemyStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        w.Write(0x324D4E45);
        foreach(var p in s.Players)
        {
            w.Write(p.HookedPiranha);w.Write(p.Unavailable?(byte)1:(byte)0);var k=p.Knockback;w.Write(k.Active?(byte)1:(byte)0);
            w.Write(k.OriginX);w.Write(k.OriginZ);w.Write(k.DirectionX);w.Write(k.DirectionZ);w.Write(k.Distance);w.Write(k.TravelRemainder);w.Write(k.TotalDistance);
        }
        var e=s.Enemies;w.Write(e.Serial);w.Write(e.Bites);w.Write(e.Shots);w.Write(e.Interruptions);w.Write(e.WaterHits);w.Write(e.Captures);
        w.Write(e.Piranhas.Count);
        foreach(var f in e.Piranhas)
        {w.Write(f.Id);w.Write(f.X);w.Write(f.Z);w.Write(f.WeightGrams);w.Write(f.Target);w.Write(f.Cooldown);w.Write(f.TravelRemainder);w.Write(f.MovementRemainderX);w.Write(f.MovementRemainderZ);w.Write(f.Owner);w.Write(f.RespawnTicks);w.Write(f.PatrolPoint);}
        var m=e.Monster;w.Write((byte)m.Phase);w.Write(m.Timer);w.Write(m.Target);w.Write(m.DirectionX);w.Write(m.DirectionZ);w.Write(m.X);w.Write(m.Z);w.Write(m.Level);w.Write(m.DiveTicks);w.Write(m.Cycle);
        w.Write(e.Waterballs.Count);
        foreach(var b in e.Waterballs)
        {w.Write(b.Id);w.Write(b.X);w.Write(b.Z);w.Write(b.OriginX);w.Write(b.OriginZ);w.Write(b.DirectionX);w.Write(b.DirectionZ);w.Write(b.Distance);w.Write(b.TravelRemainder);w.Write(b.KnockbackDistance);}
    }
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x324D4E45)throw new InvalidDataException("Enemy state version mismatch.");
        var config=s.Tackle?.Config.Enemies??EnemyConfig.Disabled;var players=s.Players.ToArray();
        for(int i=0;i<players.Length;i++)
        {
            players[i].HookedPiranha=r.ReadInt32();byte unavailable=r.ReadByte(),active=r.ReadByte();
            var k=new KnockbackState {Active=active==1,OriginX=r.ReadInt32(),OriginZ=r.ReadInt32(),DirectionX=r.ReadInt32(),DirectionZ=r.ReadInt32(),Distance=r.ReadInt32(),TravelRemainder=r.ReadInt32(),TotalDistance=r.ReadInt32()};
            if(unavailable>1 || active>1 || !Position(k.OriginX,k.OriginZ,50000) || k.Distance<0 || k.Distance>k.TotalDistance || (k.TotalDistance!=0 && k.TotalDistance!=config.KnockbackDistance) || !Remainder(k.TravelRemainder) ||
                ((k.Active || k.Distance>0) && !Direction(k.DirectionX,k.DirectionZ)) ||
                (k.Active && (k.Distance==k.TotalDistance || !config.MonsterEnabled || players[i].PulledBy>=0 || players[i].HookedPlayer>=0 || players[i].Shopping)) ||
                (k.Active && (players[i].X!=k.OriginX+(int)((long)k.DirectionX*k.Distance/EnemyGeometry.DirectionScale) ||
                    players[i].Z!=k.OriginZ+(int)((long)k.DirectionZ*k.Distance/EnemyGeometry.DirectionScale))))
                throw new InvalidDataException("Invalid external movement state.");
            players[i].Knockback=k;players[i].Unavailable=unavailable==1;
        }
        uint serial=r.ReadUInt32();int bites=r.ReadInt32(),shots=r.ReadInt32(),interruptions=r.ReadInt32(),hits=r.ReadInt32(),captures=r.ReadInt32();
        int count=r.ReadInt32();if(count!=config.PiranhaCount || bites<0 || shots<0 || interruptions<0 || hits<0 || hits>shots || captures<0 ||
            (shots<int.MaxValue && serial!=(uint)shots) || (count==0 && (bites!=0 || captures!=0)))
            throw new InvalidDataException("Invalid enemy counters.");
        var fish=new Piranha[count];
        for(int i=0;i<count;i++)
        {
            var f=new Piranha {Id=r.ReadInt32(),X=r.ReadInt32(),Z=r.ReadInt32(),WeightGrams=r.ReadInt32(),Target=r.ReadInt32(),Cooldown=r.ReadInt32(),TravelRemainder=r.ReadInt32(),MovementRemainderX=r.ReadInt32(),MovementRemainderZ=r.ReadInt32(),Owner=r.ReadInt32(),RespawnTicks=r.ReadInt32(),PatrolPoint=r.ReadInt32()};
            if(f.Id!=i || !Position(f.X,f.Z,50000) || f.WeightGrams<config.MinWeightGrams || f.WeightGrams>config.MaxWeightGrams ||
                !Target(f.Target,players.Length) || !Target(f.Owner,players.Length) || f.RespawnTicks<0 || f.RespawnTicks>config.PiranhaRespawnTicks || f.PatrolPoint<0 || f.PatrolPoint>3 ||
                (f.RespawnTicks>0 && (f.Owner>=0 || f.Target>=0)) || (f.Owner>=0 && f.Target>=0) || f.Cooldown<0 || f.Cooldown>config.BiteIntervalTicks || !Remainder(f.TravelRemainder) ||
                Math.Abs((long)f.MovementRemainderX)>=EnemyGeometry.DirectionScale || Math.Abs((long)f.MovementRemainderZ)>=EnemyGeometry.DirectionScale)throw new InvalidDataException("Invalid piranha.");
            fish[i]=f;
        }
        var m=new WaterMonster {Phase=(MonsterPhase)r.ReadByte(),Timer=r.ReadInt32(),Target=r.ReadInt32(),DirectionX=r.ReadInt32(),DirectionZ=r.ReadInt32(),X=r.ReadInt32(),Z=r.ReadInt32(),Level=r.ReadInt32(),DiveTicks=r.ReadInt32(),Cycle=r.ReadUInt32()};
        if(m.Phase>MonsterPhase.Windup || m.Timer<0 || m.Timer>Math.Max(30,Math.Max(config.InitialDelayTicks,Math.Max(config.AttackIntervalTicks,Math.Max(config.InterruptDelayTicks+1,config.WindupTicks)))) ||
            !Target(m.Target,players.Length) || (config.MonsterEnabled && (m.Level!=config.MonsterLevel || !Position(m.X,m.Z,49000))) ||
            m.DiveTicks<0 || m.DiveTicks>16 || (m.Phase==MonsterPhase.Windup && m.DiveTicks!=0) || (m.Phase==MonsterPhase.Windup && (m.Target<0 || m.Timer==0 || m.Timer>config.WindupTicks || !Direction(m.DirectionX,m.DirectionZ))) ||
            (!config.MonsterEnabled && (!m.Equals(default(WaterMonster)) || shots!=0 || hits!=0 || interruptions!=0)))throw new InvalidDataException("Invalid monster.");
        int n=r.ReadInt32();
        // Fastest legal fire cadence / longest legal flight produces at most 76 concurrent projectiles.
        if(n<0 || n>128 || n>shots || (!config.MonsterEnabled && n!=0))throw new InvalidDataException("Invalid waterball count.");
        var balls=new Waterball[n];
        uint previousAge=uint.MaxValue;
        for(int i=0;i<n;i++)
        {
            var b=new Waterball {Id=r.ReadUInt32(),X=r.ReadInt32(),Z=r.ReadInt32(),OriginX=r.ReadInt32(),OriginZ=r.ReadInt32(),DirectionX=r.ReadInt32(),DirectionZ=r.ReadInt32(),Distance=r.ReadInt32(),TravelRemainder=r.ReadInt32(),KnockbackDistance=r.ReadInt32()};
            // Compare modular ages so uint wrap remains valid, while rewound/future IDs and order changes do not.
            uint age=unchecked(serial-b.Id);
            long progress=(long)b.Distance*HookGame.Rate+b.TravelRemainder;
            long flightFrames=progress/config.WaterballSpeed;
            if(age>=128 || age>=previousAge || (shots<int.MaxValue && (b.Id==0 || b.Id>serial)) ||
                progress%config.WaterballSpeed!=0 || (long)age*(config.AttackIntervalTicks+config.WindupTicks)>flightFrames-1 ||
                !Direction(b.DirectionX,b.DirectionZ) || !Position(b.OriginX,b.OriginZ,49000) || b.KnockbackDistance!=config.KnockbackDistance || b.Distance<=0 || b.Distance>=config.WaterballRange ||
                !Remainder(b.TravelRemainder) || b.X!=b.OriginX+(int)((long)b.DirectionX*b.Distance/EnemyGeometry.DirectionScale) ||
                b.Z!=b.OriginZ+(int)((long)b.DirectionZ*b.Distance/EnemyGeometry.DirectionScale))throw new InvalidDataException("Invalid waterball.");
            balls[i]=b;previousAge=age;
        }
        foreach(var p in players)
        {
            if(p.HookedPiranha<0 || p.HookedPiranha>count)throw new InvalidDataException("Invalid piranha hook identifier.");
            if(p.HookedPiranha==0)continue;
            var f=fish[p.HookedPiranha-1];
            if(p.Phase!=HookPhase.Returning || p.FishId>=0 || p.DriftwoodId>=0 || p.HookedPlayer>=0 || p.Unavailable || p.Vitals.Downed ||
                f.Owner<0 || f.RespawnTicks!=0 || f.X!=p.HookX || f.Z!=p.HookZ)throw new InvalidDataException("Invalid piranha hook ownership.");
        }
        foreach(var f in fish)if(f.Owner>=0 && players[f.Owner].HookedPiranha!=f.Id+1)throw new InvalidDataException("Orphan piranha owner.");
        return new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,new EnemyState(fish,m,balls,serial,bites,shots,interruptions,hits,captures));
    }
    private static bool Position(int x,int z,int limit)=>Math.Abs((long)x)<=limit && Math.Abs((long)z)<=limit;
    private static bool Remainder(int value)=>value>=0 && value<HookGame.Rate;
    private static bool Target(int value,int count)=>value>=-1 && value<count;
    private static bool Direction(int x,int z)
    {
        long square=(long)x*x+(long)z*z;
        return Position(x,z,EnemyGeometry.DirectionScale) && square>=999998L*999998 && square<=1000002L*1000002;
    }
}
