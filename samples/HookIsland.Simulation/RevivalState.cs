namespace Advanture.HookIsland.Simulation;

public enum RespawnResult : byte { None, Success, NotDead, StaleDeath, InsufficientCoins, NoLanding, GameOver, Unavailable }

/// <summary>Downed is health == 0 until Dead. Rescuer is slot + 1; zero means no current helper.</summary>
public struct RevivalState
{
    public bool Dead;
    public int DownedTicks,Rescuer,RescueTicks,DeathCost,CoinsSpent;
    public int DeathBalance;
    public ulong DeathTick;
    public uint Deaths,LastRequest;
    public RespawnResult LastResult;
}
