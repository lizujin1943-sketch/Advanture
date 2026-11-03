using System;
using System.IO;

namespace Advanture.HookIsland.Simulation;

internal static class CrabStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        var c=s.Crab;w.Write(0x31425243);w.Write((byte)c.Phase);w.Write(c.Generation);w.Write(c.AttackSerial);
        w.Write(c.X);w.Write(c.Z);w.Write(c.DirectionX);w.Write(c.DirectionZ);w.Write(c.Health);w.Write(c.Timer);w.Write(c.Target);
        w.Write(c.NoTargetTicks);w.Write(c.InterruptProtection);w.Write(c.TravelRemainder);w.Write(c.GoalX);w.Write(c.GoalZ);
        w.Write(c.RouteTicks);w.Write(c.ShoreX);w.Write(c.ShoreZ);w.Write(c.Attackers);w.Write(c.Rewarded);
    }
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x31425243)throw new InvalidDataException("Crab state version mismatch.");
        var c=new CrabState{Phase=(CrabPhase)r.ReadByte(),Generation=r.ReadUInt32(),AttackSerial=r.ReadUInt32(),
            X=r.ReadInt32(),Z=r.ReadInt32(),DirectionX=r.ReadInt32(),DirectionZ=r.ReadInt32(),Health=r.ReadInt32(),Timer=r.ReadInt32(),Target=r.ReadInt32(),
            NoTargetTicks=r.ReadInt32(),InterruptProtection=r.ReadInt32(),TravelRemainder=r.ReadInt32(),GoalX=r.ReadInt32(),GoalZ=r.ReadInt32(),
            RouteTicks=r.ReadInt32(),ShoreX=r.ReadInt32(),ShoreZ=r.ReadInt32(),Attackers=r.ReadByte()};
        byte rewarded=r.ReadByte();if(rewarded>1)throw new InvalidDataException("Invalid crab receipt.");c.Rewarded=rewarded==1;
        var config=s.Tackle?.Config.Crabs??CrabConfig.Disabled;
        if(!config.Enabled)
        {if(!c.Equals(default(CrabState)))throw new InvalidDataException("Disabled crab has state.");}
        else
        {
            int limit=c.Phase switch {CrabPhase.Waiting=>Math.Max(config.InitialDelayTicks,config.RespawnTicks),CrabPhase.Climbing or CrabPhase.Leaving=>config.ClimbTicks,
                CrabPhase.Windup=>config.WindupTicks,CrabPhase.Pinching=>CrabConfig.PinchTicks,CrabPhase.Recovering=>config.RecoveryTicks,CrabPhase.Dead=>config.RespawnTicks,_=>0};
            long direction=(long)c.DirectionX*c.DirectionX+(long)c.DirectionZ*c.DirectionZ;
            bool validDirection=direction>=999998L*999998 && direction<=1000002L*1000002;
            if(c.Phase>CrabPhase.Dead || c.Health<0 || c.Health>config.Health || c.Timer<0 || c.Timer>limit ||
                (limit>0 && c.Timer==0) || c.Target< -1 || c.Target>=s.Players.Count ||
                c.NoTargetTicks<0 || c.NoTargetTicks>config.IdleTicks || c.InterruptProtection<0 || c.InterruptProtection>config.InterruptProtectionTicks ||
                c.TravelRemainder<0 || c.TravelRemainder>=HookGame.Rate || c.RouteTicks<0 || c.RouteTicks>6 ||
                c.Attackers>=(1<<s.Players.Count) || c.Rewarded && c.Phase!=CrabPhase.Dead ||
                !Position(c.X,c.Z) || !Position(c.GoalX,c.GoalZ) || !Position(c.ShoreX,c.ShoreZ) ||
                c.Visible && (c.Generation==0 || !validDirection) ||
                c.Phase!=CrabPhase.Waiting && c.Phase!=CrabPhase.Dead && c.Health==0 ||
                c.Phase==CrabPhase.Dead && (c.Health!=0 || c.Attackers==0 || c.Target!=-1) ||
                c.Health<config.Health && c.Visible && c.Attackers==0 ||
                (c.Phase==CrabPhase.Windup || c.Phase==CrabPhase.Pinching) && c.AttackSerial==0)
                throw new InvalidDataException("Invalid crab state.");
            if(c.Phase==CrabPhase.Waiting)
            {var waiting=new CrabState{Generation=c.Generation,Timer=c.Timer,Target=-1};if(!c.Equals(waiting))throw new InvalidDataException("Invalid waiting crab.");}
            for(int i=0;i<s.Players.Count;i++)if((c.Attackers&(1<<i))!=0 && s.Players[i].WaitingToJoin)throw new InvalidDataException("Unjoined crab contributor.");
        }
        return new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,c);
    }
    internal static void ValidateMap(IslandMap map,HookState s)
    {
        if(!s.Crab.Visible)return;
        var nav=new CrabNavigation(map);nav.Refresh(s.Tiles);
        if(!nav.CanStand(s.Crab.X,s.Crab.Z))throw new InvalidDataException("Crab is outside walkable platform.");
    }
    private static bool Position(int x,int z)=>Math.Abs((long)x)<=50000 && Math.Abs((long)z)<=50000;
}
