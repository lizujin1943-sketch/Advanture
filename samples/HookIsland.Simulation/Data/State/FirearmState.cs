using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

public readonly struct FirearmSpreadState
{
    public FirearmSpreadState(int type,int angle,int remainder=0){Type=type;Angle=angle;Remainder=remainder;}
    public int Type {get;}
    public int Angle {get;}
    public int Remainder {get;}
}

/// <summary>Player-owned action clocks and immutable spread memory; copying a snapshot never shares mutable arrays.</summary>
public struct FirearmState
{
    public uint ShotSequence;
    public int WeaponId,DrawTicks,RecoveryTicks,CooldownTicks,ReloadTicks,ReloadWeaponId,MoveSpeed;
    private IReadOnlyList<FirearmSpreadState>? spreads;
    public IReadOnlyList<FirearmSpreadState> Spreads=>spreads??Array.Empty<FirearmSpreadState>();
    public bool Reloading=>ReloadTicks>0;
    public bool Busy=>DrawTicks>0 || RecoveryTicks>0 || Reloading;
    public void SetSpreads(IEnumerable<FirearmSpreadState> values)=>spreads=Array.AsReadOnly(new List<FirearmSpreadState>(values).ToArray());
    public int Spread(int type){foreach(var s in Spreads)if(s.Type==type)return s.Angle;return FirearmSpread.Target(MoveSpeed,FirearmCatalog.Get(type).Spread);}
}
