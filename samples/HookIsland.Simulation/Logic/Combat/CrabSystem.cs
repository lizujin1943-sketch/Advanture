using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

public sealed class CrabSystem
{
    private readonly CrabConfig config;
    private readonly CrabNavigation navigation;
    private readonly int slot;
    private CrabState state;
    public CrabSystem(IslandMap map,CrabState? restored=null,int slot=0)
    {
        config=map.Fishing!.Crabs;navigation=new CrabNavigation(map);
        if(slot<0 || slot>=config.Count)throw new ArgumentOutOfRangeException(nameof(slot));
        this.slot=slot;
        state=restored??new CrabState{Timer=config.InitialDelayTicks+slot*config.SpawnStaggerTicks,Target=-1};
    }
    public CrabState Capture()=>state;
    public bool ReachableHit(IReadOnlyList<PlatformTile> tiles,int x,int z)
    {navigation.Refresh(tiles);return navigation.AttackLine(x,z,state.X,state.Z);}
    public static bool Eligible(Fisher p)=>!p.Unavailable && !p.WaitingToJoin && !p.Vitals.Downed && !p.Revival.Dead && !p.Swimming.InWater;
    public bool Strike(int slot,int damage)
    {
        if(slot<0 || slot>=4 || damage<=0 || !state.Damageable)return false;
        state.Attackers|=(byte)(1<<slot);state.Health=Math.Max(0,state.Health-damage);
        if(state.Health==0){state.Phase=CrabPhase.Dead;state.Timer=config.RespawnTicks;state.Target=-1;state.RouteTicks=state.NoTargetTicks=0;}
        return true;
    }
    public bool Interrupt()
    {
        if(state.Phase!=CrabPhase.Windup || state.InterruptProtection>0)return false;
        state.Phase=CrabPhase.Recovering;state.Timer=config.RecoveryTicks+1;
        state.InterruptProtection=config.InterruptProtectionTicks+1;return true;
    }
    public void MarkRewarded()
    {if(state.Phase!=CrabPhase.Dead || state.Rewarded)throw new InvalidOperationException("Crab reward is not pending.");state.Rewarded=true;}
    public static bool Contains(CrabState c,int x,int z)
    {
        long dx=x-c.X,dz=z-c.Z,square=dx*dx+dz*dz;
        if(square>(long)CrabConfig.AttackRange*CrabConfig.AttackRange)return false;
        if(square==0)return true;
        long dot=dx*c.DirectionX+dz*c.DirectionZ;
        return dot>=0 && dot>=(long)HookGame.Sqrt(square)*707107; // 90 degree sector
    }
    public void Advance(IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,Action<EnemyHit> hit)
    {
        if(!config.Enabled)return;
        navigation.Refresh(tiles);
        if(state.InterruptProtection>0)state.InterruptProtection--;
        if(state.Phase==CrabPhase.Dead)
        {if(state.Rewarded && --state.Timer<=0)Reset(1);return;}
        if(state.Phase==CrabPhase.Waiting)
        {
            if(--state.Timer>0)return;
            if(navigation.TrySpawn(players,state.Generation,out var land,out var water,slot,config.Count))
            {
                if(state.Generation==uint.MaxValue)throw new InvalidOperationException("Crab generation exhausted.");
                state=new CrabState{Generation=state.Generation+1,Phase=CrabPhase.Climbing,Health=config.Health,
                    X=land.X,Z=land.Z,ShoreX=water.X,ShoreZ=water.Z,Timer=config.ClimbTicks,Target=-1};
                Face(land.X-water.X,land.Z-water.Z);
            }
            else state.Timer=30;
            return;
        }
        if(state.Phase==CrabPhase.Climbing || state.Phase==CrabPhase.Leaving)
        {
            if(--state.Timer>0)return;
            if(state.Phase==CrabPhase.Leaving){Reset(config.RespawnTicks);return;}
            state.Phase=CrabPhase.Chasing;return;
        }
        if(state.Phase==CrabPhase.Windup)
        {
            if(--state.Timer>0)return;
            // Cross the impact boundary once, before callbacks. Restoring a post-hit state never replays damage.
            state.Phase=CrabPhase.Pinching;state.Timer=CrabConfig.PinchTicks;
            foreach(var p in players)
                if(Eligible(p) && Contains(state,p.X,p.Z) && navigation.AttackLine(state.X,state.Z,p.X,p.Z))
                    hit(new EnemyHit(p.Slot,config.Damage,true,state.DirectionX,state.DirectionZ,config.Knockback));
            return;
        }
        if(state.Phase==CrabPhase.Pinching || state.Phase==CrabPhase.Recovering)
        {
            if(--state.Timer>0)return;
            if(state.Phase==CrabPhase.Pinching){state.Phase=CrabPhase.Recovering;state.Timer=config.RecoveryTicks;}
            else {state.Phase=CrabPhase.Chasing;state.RouteTicks=0;}
            return;
        }
        if(state.RouteTicks>0)state.RouteTicks--;
        if(state.RouteTicks==0 || state.Target>=0 && !Eligible(players[state.Target]))
        {
            state.Target=navigation.Target(players,state.X,state.Z,out var goal);state.GoalX=goal.X;state.GoalZ=goal.Z;state.RouteTicks=6;
        }
        if(state.Target>=0)
        {
            state.NoTargetTicks=0;state.Phase=CrabPhase.Chasing;var p=players[state.Target];
            long distance=IslandMap.DistanceSquared(state.X,state.Z,p.X,p.Z);
            if(distance<=(long)CrabConfig.AttackDistance*CrabConfig.AttackDistance && navigation.AttackLine(state.X,state.Z,p.X,p.Z))
            {
                Face(p.X-state.X,p.Z-state.Z);state.Phase=CrabPhase.Windup;state.Timer=config.WindupTicks;
                if(state.AttackSerial==uint.MaxValue)throw new InvalidOperationException("Crab attack serial exhausted.");
                state.AttackSerial++;return;
            }
            Move(state.GoalX,state.GoalZ);return;
        }
        state.NoTargetTicks=Math.Min(config.IdleTicks,state.NoTargetTicks+1);
        if(state.NoTargetTicks<config.IdleTicks)return;
        state.Phase=CrabPhase.Retreating;
        if(!navigation.Retreat(state.X,state.Z,out var exit,out var sea))return;
        Move(exit.X,exit.Z);
        if(state.X==exit.X && state.Z==exit.Z && IslandMap.DistanceSquared(state.X,state.Z,sea.X,sea.Z)<=1000000)
        {state.Phase=CrabPhase.Leaving;state.Timer=config.ClimbTicks;state.ShoreX=sea.X;state.ShoreZ=sea.Z;}
    }
    private void Move(int x,int z)
    {
        int dx=x-state.X,dz=z-state.Z,length=HookGame.Sqrt((long)dx*dx+(long)dz*dz);if(length==0)return;
        int distance=Math.Min(length,FishingRules.Travel(config.Speed,ref state.TravelRemainder));
        int nextX=state.X+(int)((long)dx*distance/length),nextZ=state.Z+(int)((long)dz*distance/length);
        if(navigation.Segment(state.X,state.Z,nextX,nextZ)){Face(dx,dz);state.X=nextX;state.Z=nextZ;}
        else state.RouteTicks=0;
    }
    private void Face(int x,int z){if(x!=0 || z!=0)EnemyGeometry.Direction(x,z,out state.DirectionX,out state.DirectionZ);}
    private void Reset(int delay){uint generation=state.Generation;state=new CrabState{Generation=generation,Timer=delay,Target=-1};}
}
