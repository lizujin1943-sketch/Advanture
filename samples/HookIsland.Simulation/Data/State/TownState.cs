using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public struct TownPersonalState
{
    public uint LastRequest,GrantedAttempt;
    public TownResult LastResult;
    public bool InnRegistered;
    public ulong NextEmergencyTick;
    public int EmergencyReserve,BoundReserve,BoundLoaded,BoundWeaponId,ServiceSpent,SoldValue;
}
public struct TownZoneState
{
    public bool Started,Cleared,Paused;
    public int IdleTicks;
    public ulong TriggerTick; // First inner-boundary entry for the current admission queue. Zero means unqueued.
}
public sealed class TownState
{
    public static TownState Empty {get;}=new TownState(false);
    public TownState(bool enabled,IEnumerable<TownPersonalState>? players=null,IEnumerable<TownZoneState>? zones=null,
        int goals=0,int shortcuts=0,int ready=0,int finish=0,int mapKnown=0,int contracts=0,int contractsPaid=0,
        uint attempt=0,int members=0,bool finaleCompleted=false,bool finished=false)
    {Enabled=enabled;Players=Array.AsReadOnly((players??Array.Empty<TownPersonalState>()).ToArray());Zones=Array.AsReadOnly((zones??Array.Empty<TownZoneState>()).ToArray());Goals=goals;Shortcuts=shortcuts;ReadyMask=ready;FinishMask=finish;MapKnownMask=mapKnown;ContractMask=contracts;ContractsPaidMask=contractsPaid;Attempt=attempt;Members=members;FinaleCompleted=finaleCompleted;Finished=finished;}
    public bool Enabled {get;}
    public IReadOnlyList<TownPersonalState> Players {get;}
    public IReadOnlyList<TownZoneState> Zones {get;}
    public int Goals {get;} public int Shortcuts {get;} public int ReadyMask {get;} public int FinishMask {get;}
    public int MapKnownMask {get;} public int ContractMask {get;} public int ContractsPaidMask {get;}
    public uint Attempt {get;} public int Members {get;} public bool FinaleCompleted {get;} public bool Finished {get;}
}
