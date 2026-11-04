#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    public sealed class FinaleSmokePilot
    {
        private readonly CommissionSmokePilot commissions=new CommissionSmokePilot();
        public static IslandMap CreateMap(bool defaults=false)
        {
            var b=CommissionSmokePilot.CreateMap(defaults);
            var config=new FishingConfig(commissions:b.Fishing!.Commissions,finale:defaults?new FinaleConfig():new FinaleConfig(summonTicks:30,relocateTicks:30,holdTicks:30),
                ocean:defaults?new OceanConfig():OceanConfig.Disabled);
            if(defaults)return OceanRules.CreateMap(IslandMap.CreatePlatform(),config);
            return new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,config);
        }
        public bool Complete(HookState state)=>state.Won;
        public HookInput Sample(IslandMap map,HookState state,int slot,ulong tick)
        {
            if(state.Won || state.Tackle==null)return default;
            if(state.Finale.Phase==FinalePhase.Dormant)return commissions.Sample(map,state,slot,tick);
            var p=state.Players[slot];
            if(p.HookedFinale)return default;
            if(p.Phase!=HookPhase.Ready)return (state.Finale.Phase!=FinalePhase.Surfaced && p.Phase==HookPhase.Flying) || CooperativeFishing.Attached(p)?new HookInput(0,0,cancel:true):default;
            if(state.Finale.Phase!=FinalePhase.Surfaced || p.Cooldown>0)return default;
            int delay=(int)Math.Min(30UL,tick>state.Steps?tick-state.Steps-1:0);
            var stand=Stand(state,slot);
            if(SmokeNavigation.Toward(map,state,slot,stand,delay,out var move))return move;
            // Wait for everyone to reach the bank and recover through normal idle regeneration.
            foreach(var other in state.Players)
            {
                if(other.Unavailable || other.Revival.Dead)continue;
                if(other.HookedFinale)continue;
                var destination=Stand(state,other.Slot);
                if(other.Phase!=HookPhase.Ready || other.Cooldown>0 || !FinaleRules.CanHold(other) ||
                    Math.Abs(other.X-destination.X)>70 || Math.Abs(other.Z-destination.Z)>70 ||
                    other.Stamina<FishingRules.MaxStamina(state,other)*9/10)return default;
            }
            return new HookInput(0,0,state.Finale.X,state.Finale.Z,true);
        }
        private static Point Stand(HookState state,int slot)
        {
            var anchor=FinaleRules.Anchor(state.Tackle!.Config.Finale,state.Finale);var d=FinaleRules.Direction(state.Finale.Direction);
            // 玩家占位直径为 660 毫米；预留间隙，并整体平移队列，不能把岸角外的多个目标各自压回同一位置。
            for(int shift=0;shift<=6000;shift+=100)
                for(int sign=0;sign<(shift==0?1:2);sign++)
                {
                    int center=sign==0?shift:-shift;bool fits=true;
                    for(int member=0;member<state.Players.Count;member++)
                    {
                        int offset=center+(member*2-state.Players.Count+1)*360;
                        if(!PlatformRules.CanStand(state.Tiles,anchor.X-d.Z*offset,anchor.Z+d.X*offset)){fits=false;break;}
                    }
                    if(fits)
                    {
                        int offset=center+(slot*2-state.Players.Count+1)*360;
                        return new Point(anchor.X-d.Z*offset,anchor.Z+d.X*offset);
                    }
                }
            int fallback=(slot*2-state.Players.Count+1)*360;
            while(true)
            {
                var p=new Point(anchor.X-d.Z*fallback,anchor.Z+d.X*fallback);
                if(PlatformRules.CanStand(state.Tiles,p.X,p.Z) || fallback==0)return p;
                fallback-=Math.Sign(fallback)*Math.Min(100,Math.Abs(fallback));
            }
        }
    }
}
#endif
