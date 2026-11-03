using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private CrabSystem[] crabs=Array.Empty<CrabSystem>();
        private void AdvanceCrabs()
        {
            foreach(var crab in crabs)
            {
                var c=crab.Capture();
                // No partial money/material credit and no respawn before the receipt commits.
                if(c.Phase==CrabPhase.Dead && !c.Rewarded)SettleCrab(crab,c);
                crab.Advance(tiles,players,ApplyEnemyHit);
            }
        }
        private void SettleCrab(CrabSystem crab,CrabState c)
        {
            int count=0;for(int slot=0;slot<players.Length;slot++)if((c.Attackers&(1<<slot))!=0)count++;
            int gold=CrabConfig.CoinsPerAttacker(count),bones=CrabConfig.BonesPerAttacker;
            if(coins>int.MaxValue-count*gold)return;
            var credits=new List<MaterialCredit>();
            foreach(var p in players)if((c.Attackers&(1<<p.Slot))!=0)
            {
                if(p.Coins>int.MaxValue-gold || p.CoinsEarned>int.MaxValue-gold || p.FishbonesEarned>int.MaxValue-bones)return;
                credits.Add(new MaterialCredit(p.Slot,ItemKind.Fishbone,bones,new Point(c.X,c.Z)));
            }
            if(!equipment!.Inventory.CreditMaterials(credits))return;
            coins+=count*gold;
            for(int i=0;i<players.Length;i++)if((c.Attackers&(1<<i))!=0)
            {
                var p=players[i];p.Coins+=gold;p.CoinsEarned+=gold;p.FishbonesEarned+=bones;
                PersonalMaterialInventory.Refresh(ref p,equipment.Inventory);players[i]=p;
                Record(HookGameplayEventKind.CoinsReceived,p,amount:gold);
            }
            crab.MarkRewarded();
        }
    }
}
