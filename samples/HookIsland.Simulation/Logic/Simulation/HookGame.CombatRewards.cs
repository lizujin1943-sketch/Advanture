namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        // Settle after every weapon has committed its player copy, before enemies can respawn.
        private void SettlePiranhaKills()
        {
            if(enemies==null)return;
            for(int i=0;i<enemies.PiranhaCount;i++)
            {
                var fish=enemies.GetPiranha(i);
                if(fish.RewardOwner==0)continue;
                int slot=fish.RewardOwner-1;var p=players[slot];
                int gold=PiranhaFishing.Coins(map.Fishing!.Enemies,fish);
                if(coins>int.MaxValue-gold || p.Coins>int.MaxValue-gold || p.CoinsEarned>int.MaxValue-gold)continue;
                coins+=gold;p.Coins+=gold;p.CoinsEarned+=gold;players[slot]=p;
                enemies.MarkKillRewarded(i);
                Record(HookGameplayEventKind.CoinsReceived,p,amount:gold);
            }
        }
    }
}
