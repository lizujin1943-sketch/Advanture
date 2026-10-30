using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public enum OceanEvent : byte { None, MiddleSchool, RareSchool, Recovery }
public readonly struct OceanVisit
{
    public OceanVisit(int homeX,int homeZ,ulong until){Active=true;HomeX=homeX;HomeZ=homeZ;Until=until;}
    public bool Active {get;}
    public int HomeX {get;}
    public int HomeZ {get;}
    public ulong Until {get;}
}
public sealed class OceanState
{
    public static OceanState Empty {get;}=new OceanState();
    public OceanState(int rewardedStage=0,ulong lastCatchTick=0,ulong threatWindowStart=0,int threatCount=0,uint handledAttacks=0,
        ulong quietUntil=0,ulong reliefReadyTick=0,uint sequence=0,OceanEvent lastEvent=OceanEvent.None,ulong lastEventTick=0,IEnumerable<OceanVisit>? visits=null)
    {
        RewardedStage=rewardedStage;LastCatchTick=lastCatchTick;ThreatWindowStart=threatWindowStart;ThreatCount=threatCount;HandledAttacks=handledAttacks;
        QuietUntil=quietUntil;ReliefReadyTick=reliefReadyTick;Sequence=sequence;LastEvent=lastEvent;LastEventTick=lastEventTick;
        Visits=Array.AsReadOnly((visits??Array.Empty<OceanVisit>()).ToArray());
    }
    public int RewardedStage {get;}
    public ulong LastCatchTick {get;}
    public ulong ThreatWindowStart {get;}
    public int ThreatCount {get;}
    public uint HandledAttacks {get;}
    public ulong QuietUntil {get;}
    public ulong ReliefReadyTick {get;}
    public uint Sequence {get;}
    public OceanEvent LastEvent {get;}
    public ulong LastEventTick {get;}
    public IReadOnlyList<OceanVisit> Visits {get;}
}
