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
                // 与击杀金币在同一帧提交；只有未结算成体才计入首次清理，子代/回放不重复推进。
                if(map.IsTown && !e.IsMinion)
                {
                    int zone=TownEncounters.Zone(e.Id),target=TownEncounters.ClearTarget(zone);
                    townZones[zone].DefeatedAdults=System.Math.Min(target,townZones[zone].DefeatedAdults+1);
                    townZones[zone].Cleared=townZones[zone].DefeatedAdults==target;
                }
                Record(HookGameplayEventKind.CoinsReceived,p,amount:gold);
            }
            eldritch.Advance(tiles,players,ApplyEnemyHit,ocean?.AllowNewAttacks(tick+1)??true);
        }
    }
}
