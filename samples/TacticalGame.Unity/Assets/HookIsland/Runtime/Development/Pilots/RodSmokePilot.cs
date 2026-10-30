#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Acceptance-only normal player inputs. No direct state changes or resource grants.</summary>
    public sealed class RodSmokePilot
    {
        private static readonly Point[] Stands={new Point(-1400,-2600),new Point(2600,-1400),new Point(1400,2600),new Point(-2600,1400)};
        private readonly int[] boughtAt={-1,-1,-1,-1};
        public bool Complete(HookState state)
        {
            Observe(state);return state.Players.All(p=>boughtAt[p.Slot]>=0 && p.Catches>boughtAt[p.Slot] && p.Phase==HookPhase.Ready);
        }
        private void Observe(HookState state)
        {
            foreach(var p in state.Players)
                if(p.EquippedRod>state.Players.Count && boughtAt[p.Slot]<0)boughtAt[p.Slot]=p.Catches;
        }
        public HookInput Sample(IslandMap map,HookState s,int slot,ulong requestedTick)
        {
            Observe(s);var p=s.Players[slot];
            if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
            if(boughtAt[slot]>=0 && p.Catches>boughtAt[slot])return default;
            var t=s.Tackle!;
            
            int offer=Enumerable.Range(0,t.Stock.Count).Where(i=>!t.Stock[i].Sold && t.Stock[i].Kind==ItemKind.Rod).OrderBy(i=>RodCatalog.Get(t.Stock[i].Type).Price).DefaultIfEmpty(-1).First();
            if(boughtAt[slot]<0 && p.Catches>0 && offer>=0 && p.Coins>=RodCatalog.Get(t.Stock[offer].Type).Price)
            {
                var move=Move(p,new Point(-900+slot*600,0));if(move.MoveX!=0 || move.MoveZ!=0)return move;
                return new HookInput(0,0,shopping:true,rodAction:RodAction.BuyAndEquip,rodTarget:offer,rodBatch:t.Batch,rodRequestId:p.LastRodRequest+1);
            }
            var walking=Move(p,Stands[slot]);if(walking.MoveX!=0 || walking.MoveZ!=0)return walking;
            int lead=(int)Math.Min(30,requestedTick>s.Steps?requestedTick-s.Steps-1:0);
            int speed=FishingRules.Speed(s,p),range=FishingRules.Range(map,s,p);
            foreach(var fish in s.Fish)
            {
                if(fish.Owner>=0)continue;
                var future=FishMotion.Predict(fish,map.FishSpots[fish.Id],lead);
                for(int ticks=1;ticks<=Math.Min(120,range*HookGame.Rate/speed+1);ticks++)
                {
                    FishMotion.Advance(ref future,map.FishSpots[fish.Id]);
                    if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                    int d=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                    int reelTicks=d*HookGame.Rate/t.Config.ReelSpeed+4;
                    if(d<=range && Math.Abs(d-ticks*speed/HookGame.Rate)<speed/HookGame.Rate/2+40 &&
                        (long)FishCatalog.DrainPerTick(fish)*reelTicks<p.Stamina)
                        return new HookInput(0,0,future.X,future.Z,true);
                }
            }
            return default;
        }
        private static HookInput Move(Fisher p,Point target)
        {
            int dx=target.X-p.X,dz=target.Z-p.Z;
            if(Math.Abs(dx)>100)return new HookInput((short)(Math.Sign(dx)*1000),0);
            if(Math.Abs(dz)>100)return new HookInput(0,(short)(Math.Sign(dz)*1000));
            return default;
        }
    }
}
#endif
