using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Room-owned opportunities. It never credits inventory or changes commission progress.</summary>
public sealed class OceanSystem
{
    private readonly OceanConfig config;
    private readonly OceanVisit[] visits;
    private int rewardedStage,threatCount;
    private ulong lastCatchTick,threatWindowStart,quietUntil,reliefReadyTick,lastEventTick;
    private uint handledAttacks,sequence;
    private OceanEvent lastEvent;
    public OceanSystem(OceanConfig config,OceanState? state=null)
    {
        this.config=config;
        if(state==null){visits=new OceanVisit[config.VisitSlots];return;}
        visits=state.Visits.ToArray();rewardedStage=state.RewardedStage;threatCount=state.ThreatCount;
        lastCatchTick=state.LastCatchTick;threatWindowStart=state.ThreatWindowStart;quietUntil=state.QuietUntil;reliefReadyTick=state.ReliefReadyTick;
        handledAttacks=state.HandledAttacks;sequence=state.Sequence;lastEvent=state.LastEvent;lastEventTick=state.LastEventTick;
    }
    public OceanState Capture()=>new OceanState(rewardedStage,lastCatchTick,threatWindowStart,threatCount,handledAttacks,
        quietUntil,reliefReadyTick,sequence,lastEvent,lastEventTick,visits);
    public bool AllowNewAttacks(ulong now)=>!config.PauseMonster || now>quietUntil;
    public bool Available(int id,int completed)=>id<config.Unlocks.Count?config.Unlocks[id]<=OceanRules.Stage(completed):visits[id-config.Unlocks.Count].Active;
    public Point Home(IslandMap map,int id)
    {
        if(id>=config.Unlocks.Count){var v=visits[id-config.Unlocks.Count];if(v.Active)return new Point(v.HomeX,v.HomeZ);}
        return map.FishSpots[id];
    }
    public void RecordCatch(int fishId,ulong now)
    {lastCatchTick=now;EndVisit(fishId);}
    public void EndVisit(int fishId)
    {if(fishId>=config.Unlocks.Count && fishId<config.Unlocks.Count+visits.Length)visits[fishId-config.Unlocks.Count]=default;}
    public void BeforeStep(IslandMap map,IReadOnlyList<PlatformTile> tiles,Fish[] fish,ulong now)
    {
        for(int i=0;i<visits.Length;i++)
        {
            var v=visits[i];int id=config.Unlocks.Count+i;
            if(!v.Active || fish[id].Phase==FishPhase.Hooked || fish[id].Phase==FishPhase.Escaping)continue;
            if(now>=v.Until || !OceanPlacement.LaneWater(map,tiles,new Point(v.HomeX,v.HomeZ)))
            {visits[i]=default;fish[id]=OceanRules.Dormant(id,map.FishSpots[id],fish[id].Generation);}
        }
    }
    public void Advance(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,Fish[] fish,int completed,EnemyState enemies,ulong now)
    {
        uint total=unchecked((uint)enemies.Shots+(uint)enemies.Interruptions),delta=total-handledAttacks;handledAttacks=total;
        if(threatCount>0 && now-threatWindowStart>=(ulong)config.PressureWindowTicks){threatCount=0;threatWindowStart=0;}
        if(delta>0)
        {
            if(threatCount==0)threatWindowStart=now;
            threatCount=(int)Math.Min((long)config.PressureCount,threatCount+(long)delta);
        }
        if(!players.Any(p=>!p.Unavailable && !p.Vitals.Downed))return;
        bool recovery=now>=reliefReadyTick && (now-lastCatchTick>=(ulong)config.NoCatchTicks || threatCount>=config.PressureCount);
        if(recovery)
        {
            quietUntil=After(now,config.RecoveryTicks);reliefReadyTick=After(now,config.RecoveryCooldownTicks);
            threatCount=0;threatWindowStart=0;Event(OceanEvent.Recovery,now);
            Spawn(map,tiles,players,fish,config.RecoveryVisit,now);return;
        }
        int stage=OceanRules.Stage(completed);
        if(now<=quietUntil || stage<=rewardedStage)return;
        if(Spawn(map,tiles,players,fish,stage==1?config.MiddleVisit:config.RareVisit,now)>0)
        {rewardedStage=stage;Event(stage==1?OceanEvent.MiddleSchool:OceanEvent.RareSchool,now);}
    }
    private int Spawn(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,Fish[] fish,FishSchoolProfile profile,ulong now)
    {
        var homes=visits.Where(v=>v.Active).Select(v=>new Point(v.HomeX,v.HomeZ)).ToList();int count=0;
        for(int i=0;i<visits.Length;i++)
        {
            int id=config.Unlocks.Count+i;
            if(visits[i].Active || fish[id].Phase==FishPhase.Hooked || fish[id].Phase==FishPhase.Escaping ||
                !OceanPlacement.Find(map,tiles,players,homes,sequence,out var home))continue;
            visits[i]=new OceanVisit(home.X,home.Z,After(now,config.VisitTicks));homes.Add(home);
            fish[id]=OceanRules.Spawn(id,home,0,unchecked(fish[id].Generation+1),map.Fishing!.Seed,profile);
            FishMotion.Advance(ref fish[id],home);count++;
        }
        return count;
    }
    private void Event(OceanEvent kind,ulong now)
    {if(sequence<uint.MaxValue)sequence++;lastEvent=kind;lastEventTick=now;}
    internal static ulong After(ulong now,int duration)=>now>ulong.MaxValue-(ulong)duration?ulong.MaxValue:now+(ulong)duration;
}
