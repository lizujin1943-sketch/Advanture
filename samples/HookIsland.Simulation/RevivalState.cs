namespace Advanture.HookIsland.Simulation;

public enum RespawnResult : byte { None, Success, NotDead, StaleDeath, InsufficientCoins, NoLanding, GameOver, Unavailable }

/// <summary>Downed is health == 0 until Dead. DownedId identifies an incapacitation, including free rescues.</summary>
public struct RevivalState
{
    public bool Dead;
    public int DownedTicks,Rescuer,RescueTicks,DeathCost,CoinsSpent;
    public int DeathBalance;
    public ulong DeathTick;
    public uint Deaths,LastRequest,DownedId;
    public RespawnResult LastResult;
    public bool HasQuote=>DeathTick!=0;
}
