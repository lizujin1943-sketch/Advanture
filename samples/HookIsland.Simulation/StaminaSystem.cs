using System;

namespace Advanture.HookIsland.Simulation;

/// <summary>Shared character stamina pool. All amounts are fixed point; activity owners supply costs.</summary>
public static class StaminaSystem
{
    public const int UnitsPerPoint=30000;
    public static bool Spend(ref Fisher player,int amount)
    {
        if(amount<0)throw new ArgumentOutOfRangeException(nameof(amount));
        player.Stamina=Math.Max(0,player.Stamina-amount);return player.Stamina>0;
    }
    public static void Recover(ref Fisher player,FishingConfig config,int maximum,bool active)
    {
        if(active){player.RegenDelay=config.RegenDelayTicks;return;}
        if(player.RegenDelay>0){player.RegenDelay--;return;}
        player.Stamina=Math.Min(maximum,player.Stamina+config.RegenMilliPerSecond);
    }
}
