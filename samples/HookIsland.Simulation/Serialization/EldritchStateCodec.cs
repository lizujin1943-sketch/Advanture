using System;
using System.IO;

namespace Advanture.HookIsland.Simulation;

internal static class EldritchStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        w.Write(0x32524445);w.Write(s.Eldritch.NextSpawn);w.Write(s.Eldritch.Creatures.Count);
        foreach(var c in s.Eldritch.Creatures)
        {
            w.Write(c.Id);w.Write((byte)c.Kind);w.Write((byte)c.Phase);w.Write(c.Generation);w.Write(c.AttackSerial);
            w.Write(c.X);w.Write(c.Z);w.Write(c.DirectionX);w.Write(c.DirectionZ);w.Write(c.Health);w.Write(c.Timer);w.Write(c.Target);
            w.Write(c.EffectX);w.Write(c.EffectZ);w.Write(c.TravelRemainder);w.Write(c.GoalX);w.Write(c.GoalZ);w.Write(c.RouteTicks);
            w.Write(c.NoTargetTicks);w.Write(c.Summons);w.Write(c.RewardOwner);w.Write(c.Ally);w.Write(c.HitMask);w.Write(c.Rewarded);w.Write(c.TownAdult);w.Write(c.Paused);
        }
    }
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x32524445)throw new InvalidDataException("Eldritch state version mismatch.");
        var config=s.Tackle?.Config.Eldritch??EldritchConfig.Disabled;
        bool town=s.Tackle?.Config.TownMode==true;int adults=town?TownEncounters.AdultCount:10;
        int next=r.ReadInt32(),count=r.ReadInt32();
        if(count!=(config.Enabled?(town?TownEncounters.Population:EldritchCatalog.Population):0) || next<0 || next>=adults || !config.Enabled && next!=0)
            throw new InvalidDataException("Invalid eldritch population.");
        var creatures=new EldritchCreature[count];int alive=0;
        for(int i=0;i<count;i++)
        {
            var c=new EldritchCreature{Id=r.ReadInt32(),Kind=(EldritchKind)r.ReadByte(),Phase=(EldritchPhase)r.ReadByte(),Generation=r.ReadUInt32(),AttackSerial=r.ReadUInt32(),
                X=r.ReadInt32(),Z=r.ReadInt32(),DirectionX=r.ReadInt32(),DirectionZ=r.ReadInt32(),Health=r.ReadInt32(),Timer=r.ReadInt32(),Target=r.ReadInt32(),
                EffectX=r.ReadInt32(),EffectZ=r.ReadInt32(),TravelRemainder=r.ReadInt32(),GoalX=r.ReadInt32(),GoalZ=r.ReadInt32(),RouteTicks=r.ReadInt32(),
                NoTargetTicks=r.ReadInt32(),Summons=r.ReadInt32(),RewardOwner=r.ReadInt32(),Ally=r.ReadInt32(),HitMask=r.ReadByte()};
            byte reward=r.ReadByte(),townAdult=r.ReadByte(),paused=r.ReadByte();c.Rewarded=reward==1;c.TownAdult=townAdult==1;c.Paused=paused==1;
            if(c.Id!=i || c.Kind!=(town?TownEncounters.Kind(i):i<10?(EldritchKind)i:EldritchKind.DeepOne) || townAdult>1 || paused>1 || c.TownAdult!=(town && i<adults) || c.Paused && !town || c.Phase>EldritchPhase.Dead || reward>1)
                throw new InvalidDataException("Invalid eldritch identity.");
            var d=EldritchCatalog.Get(c.Kind);
            int limit=c.Phase switch {EldritchPhase.Dormant=>Math.Max(config.RespawnTicks,config.InitialDelayTicks+9*config.StaggerTicks),
                EldritchPhase.Emerging=>EldritchCatalog.EmergenceTicks,EldritchPhase.Windup=>d.WindupTicks,EldritchPhase.Active=>d.ActiveTicks,
                EldritchPhase.Recovering=>d.RecoveryTicks,EldritchPhase.Dead=>config.RespawnTicks,_=>0};
            long direction=(long)c.DirectionX*c.DirectionX+(long)c.DirectionZ*c.DirectionZ;
            if(c.Timer<0 || c.Timer>limit || c.Phase!=EldritchPhase.Dormant && c.Phase!=EldritchPhase.Hunting && c.Timer==0 ||
                c.Health<0 || c.Health>EldritchCatalog.MaxHealth(c) || c.Alive && c.Health==0 ||
                c.Target< -1 || c.Target>=s.Players.Count || c.Ally< -1 || c.Ally>=count || c.Ally==i ||
                c.RewardOwner<0 || c.RewardOwner>s.Players.Count || c.HitMask>=(1<<s.Players.Count) ||
                c.TravelRemainder<0 || c.TravelRemainder>=HookGame.Rate || c.RouteTicks<0 || c.RouteTicks>6 || c.NoTargetTicks<0 || c.NoTargetTicks>=300 ||
                c.Summons<0 || c.Summons>EldritchCatalog.MaxBroodPerGeneration || c.Kind!=EldritchKind.BroodMother && c.Summons!=0 ||
                !Position(c.X,c.Z,town?80000:49000) || !Position(c.GoalX,c.GoalZ,100000) || !Position(c.EffectX,c.EffectZ,100000) ||
                c.Phase!=EldritchPhase.Dormant && (c.Generation==0 || Math.Abs((long)c.DirectionX)>1000000 || Math.Abs((long)c.DirectionZ)>1000000 || direction<999998L*999998 || direction>1000002L*1000002) ||
                c.Phase==EldritchPhase.Dead && (c.Health!=0 || c.RewardOwner==0 || c.Target!=-1 || c.Ally!=-1) ||
                c.Phase!=EldritchPhase.Dead && (c.RewardOwner!=0 || c.Rewarded) ||
                (c.Phase==EldritchPhase.Windup || c.Phase==EldritchPhase.Active) && (c.AttackSerial==0 || c.Target<0) ||
                c.Ally>=0 && (c.Kind!=EldritchKind.ChoirPriest || c.Phase!=EldritchPhase.Windup && c.Phase!=EldritchPhase.Active) ||
                c.Phase!=EldritchPhase.Dormant && !town && (i<10?!config.Includes(i):!config.Includes((int)EldritchKind.BroodMother)))
                throw new InvalidDataException("Invalid eldritch state.");
            if(c.Phase==EldritchPhase.Dormant)
            {
                var expected=new EldritchCreature{Id=i,Kind=c.Kind,TownAdult=c.TownAdult,Generation=c.Generation,Timer=c.Timer,Target=-1,Ally=-1};
                if(!c.Equals(expected) || c.IsMinion && c.Timer!=0)throw new InvalidDataException("Invalid dormant eldritch state.");
            }
            if(c.RewardOwner>0 && s.Players[c.RewardOwner-1].WaitingToJoin)throw new InvalidDataException("Unjoined eldritch reward owner.");
            if(!c.IsMinion && c.Damageable && !c.Paused)alive++;
            creatures[i]=c;
        }
        if(alive>config.MaxAlive)throw new InvalidDataException("Eldritch encounter cap exceeded.");
        return new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,s.Boss,s.Grenades,new EldritchState(creatures,next));
    }
    internal static void ValidateMap(IslandMap map,HookState s)
    {
        var terrain=new EldritchTerrain(map);terrain.Refresh(s.Tiles);
        foreach(var c in s.Eldritch.Creatures)
            if(c.Alive && !terrain.CanStand(EldritchCatalog.Get(c.Kind).Habitat,c.X,c.Z))throw new InvalidDataException("Eldritch creature outside its habitat.");
    }
    private static bool Position(int x,int z,int limit)=>Math.Abs((long)x)<=limit && Math.Abs((long)z)<=limit;
}
