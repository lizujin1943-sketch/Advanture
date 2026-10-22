#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Opt-in acceptance inputs: earns a ring, swims/casts, rescues a friend. Never mutates state.</summary>
    public sealed class SwimmingSmokePilot
    {
        private ulong waterCastTick;
        public bool WaterCastSeen=>waterCastTick>0;
        private void Observe(HookState s)
        {
            var p=s.Players[0];
            if(waterCastTick==0 && p.Swimming.InWater && p.Phase!=HookPhase.Ready)waterCastTick=s.Steps;
        }
        public bool Complete(HookState s)
        {
            Observe(s);if(s.Players.Count!=4)return false;
            return WaterCastSeen && s.Players[0].Swimming.InWater && s.Players[0].Phase==HookPhase.Ready &&
                SwimRingCatalog.Equipped(s.Tackle!.Inventory,0)!=null && s.Players[0].Vitals.Health==100 &&
                s.Players[2].TimesHooked>0 && !s.Players[2].Swimming.InWater && s.Players[2].Vitals.Health<100 &&
                s.Players[3].Swimming.InWater && s.Players[3].Vitals.Health<100;
        }
        private static HookInput MoveZ(Fisher p,int target,int lead)=>
            new HookInput(0,(short)Math.Max(-1000,Math.Min(1000,(target-p.Z)*5/(lead+1))));
        public HookInput Sample(IslandMap map,HookState s,int slot,ulong tick)
        {
            Observe(s);var p=s.Players[slot];var t=s.Tackle!;
            int lead=(int)Math.Min(30,tick>s.Steps?tick-s.Steps-1:0);
            if(slot==1)
            {
                if(p.Z> -2200)return MoveZ(p,-2300,lead);
                var victim=s.Players[2];
                if(victim.Z< -3700 && victim.TimesHooked==0 && p.Phase==HookPhase.Ready)
                    return new HookInput(0,0,victim.X,victim.Z,true);
                return default;
            }
            if(slot==2)return p.TimesHooked==0 && p.Z> -3900?new HookInput(0,-1000):default;
            if(slot==3)return SwimRingCatalog.Equipped(t.Inventory,0)!=null && p.X> -4000?new HookInput(-1000,0):default;
            if(SwimRingCatalog.Equipped(t.Inventory,0)!=null)
            {
                if(WaterCastSeen)
                    return p.Phase==HookPhase.Ready?default:new HookInput(1000,0,cancel:s.Steps-waterCastTick>=30);
                if(p.Z> -1300)return new HookInput(0,-1000);
                if(p.X> -4100)return new HookInput(-1000,0);
                return new HookInput(1000,0,p.X-4000,p.Z,true);
            }
            if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
            int offer=Enumerable.Range(0,t.Stock.Count).Where(i=>!t.Stock[i].Sold && t.Stock[i].Kind==ItemKind.SwimRing)
                .OrderBy(i=>SwimRingCatalog.Get(t.Stock[i].Type).Price).DefaultIfEmpty(-1).First();
            if(offer>=0 && p.Coins>=SwimRingCatalog.Get(t.Stock[offer].Type).Price)
            {
                if(p.Z< -100)return MoveZ(p,0,lead);
                return new HookInput(0,0,shopping:true,inventory:new InventoryCommand(InventoryAction.BuyAndEquip,offer,batch:t.Batch,requestId:p.LastInventoryRequest+1));
            }
            if(p.Z> -2400)return MoveZ(p,-2500,lead);
            int speed=FishingRules.Speed(s,p),range=FishingRules.Range(map,s,p);
            foreach(var fish in s.Fish)
            {
                if(fish.Owner>=0)continue;var future=FishMotion.Predict(fish,map.FishSpots[fish.Id],lead);
                for(int n=1;n<=Math.Min(120,range*30/speed+1);n++)
                {
                    FishMotion.Advance(ref future,map.FishSpots[fish.Id]);if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                    int d=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                    if(d<=range && Math.Abs(d-n*speed/30)<speed/60+40 && (long)FishCatalog.DrainPerTick(fish)*(d*30/t.Config.ReelSpeed+4)<p.Stamina)
                        return new HookInput(0,0,future.X,future.Z,true);
                }
            }
            return default;
        }
    }
}
#endif
