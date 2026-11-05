namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private EldritchSystem eldritch=null!;
        private void AdvanceEldritch()
        {
            // All weapon updates have committed their player copies before this transaction.
            for(int i=0;i<eldritch.Count;i++)
            {
                var e=eldritch.Get(i);if(e.Phase!=EldritchPhase.Dead || e.Rewarded)continue;
                int slot=e.RewardOwner-1,gold=EldritchSystem.Reward(e);var p=players[slot];
                if(coins>int.MaxValue-gold || p.Coins>int.MaxValue-gold || p.CoinsEarned>int.MaxValue-gold)continue;
                coins+=gold;p.Coins+=gold;p.CoinsEarned+=gold;players[slot]=p;eldritch.MarkRewarded(i);
                Record(HookGameplayEventKind.CoinsReceived,p,amount:gold);
            }
            eldritch.Advance(tiles,players,ApplyEnemyHit,ocean?.AllowNewAttacks(tick+1)??true);
        }
    }
}
