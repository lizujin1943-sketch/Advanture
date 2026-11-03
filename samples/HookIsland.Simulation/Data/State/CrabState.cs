namespace Advanture.HookIsland.Simulation;

public enum CrabPhase : byte { Waiting, Climbing, Chasing, Windup, Pinching, Recovering, Retreating, Leaving, Dead }

/// <summary>Value snapshot, including attack/reward receipts. Navigation caches contain no gameplay state.</summary>
public struct CrabState
{
    public CrabPhase Phase;
    public uint Generation,AttackSerial;
    public int X,Z,DirectionX,DirectionZ,Health,Timer,Target,NoTargetTicks,InterruptProtection;
    public int TravelRemainder,GoalX,GoalZ,RouteTicks,ShoreX,ShoreZ;
    public byte Attackers;
    public bool Rewarded;
    public bool Visible=>Phase!=CrabPhase.Waiting;
    public bool Damageable=>Visible && Phase!=CrabPhase.Dead && Phase!=CrabPhase.Leaving;
}
