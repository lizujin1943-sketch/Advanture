using System;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private BossSystem boss=null!;
        private void AdvanceBoss()
        {
            int before=tiles.Count;boss.Advance(tick+1,players,tiles,ApplyEnemyHit);
            if(tiles.Count!=before)
            {
                equipment!.Inventory.ReconcileGround(tiles);
                foreach(var crab in crabs)crab.ReconcileTerrain(tiles);
                for(int i=0;i<chests.Length;i++)
                {
                    if(chests[i].Phase!=TreasurePhase.Landed || PlatformRules.CanStand(tiles,chests[i].X,chests[i].Z))continue;
                    var chest=chests[i];var bank=tiles.OrderBy(t=>IslandMap.DistanceSquared(chest.X,chest.Z,t.X*2000,t.Z*2000)).ThenBy(t=>t.Z).ThenBy(t=>t.X).First();
                    // 已登岸奖励归属不重新计算，仅将宝箱搬至最近的剩余地格。
                    chest.X=bank.X*2000;chest.Z=bank.Z*2000;chests[i]=chest;
                }
                // 同一权威帧完成地形与人物水态转换，落水取消购物和不允许的动作，存档永远自洽。
                for(int i=0;i<players.Length;i++)
                {
                    if(players[i].WaitingToJoin)continue;
                    var p=players[i];bool water=!SwimmingTerrain.Ground(map,tiles,p.X,p.Z);
                    if(water && !p.Swimming.InWater)ComboRules.Interrupt(ref p.Combo);
                    p.Swimming.InWater=water;p.Swimming.Active=false;
                    if(water){p.Shopping=false;if(equipment.Ring(p)==null)RetractHook(ref p);if(!MeleeRules.CanAct(p,equipment.Ring(p)!=null))MeleeRules.Cancel(ref p);if(!FirearmRules.CanMaintain(p,equipment.Ring(p)!=null))FirearmRules.CancelActions(ref p);}
                    players[i]=p;
                }
                finale?.ReconcileTerrain(map,tiles,players);
            }
            SettleBoss();
        }
        private void AdvanceBossWhileFinished()
        {
            if(finale?.Capture().Phase==FinalePhase.Victory)return;
            var b=boss.Capture();if(!b.Active)return;
            // 终章及全灭仍冻结常规世界，但失败挑战的退场时钟必须能完成。
            boss.AdvanceFinished();
            if(boss.Capture().Phase==BossPhase.Defeated)SettleBoss();
        }
        private void SettleBoss()
        {
            var b=boss.Capture();if(b.Phase!=BossPhase.Defeated || b.Rewarded)return;
            int reward=map.Fishing!.Boss.RewardCoins,count=players.Count(p=>(b.Attackers&(1<<p.Slot))!=0);
            if(coins>int.MaxValue-(long)reward*count)return;
            foreach(var p in players)if((b.Attackers&(1<<p.Slot))!=0 && (p.Coins>int.MaxValue-reward || p.CoinsEarned>int.MaxValue-reward))return;
            coins+=reward*count;
            for(int i=0;i<players.Length;i++)if((b.Attackers&(1<<i))!=0)
            {players[i].Coins+=reward;players[i].CoinsEarned+=reward;Record(HookGameplayEventKind.CoinsReceived,players[i],amount:reward);}
            boss.MarkRewarded();
        }
    }
}
