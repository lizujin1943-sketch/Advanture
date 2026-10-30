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
            var stand=Stand(state,slot);int dx=stand.X-p.X,dz=stand.Z-p.Z;
            if(Math.Abs(dx)>70)return new HookInput(Approach(dx,delay),0);
            if(Math.Abs(dz)>70)return new HookInput(0,Approach(dz,delay));
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
            int offset=(slot*2-state.Players.Count+1)*300;
            while(true)
            {
                var p=new Point(anchor.X-d.Z*offset,anchor.Z+d.X*offset);
                if(PlatformRules.CanStand(state.Tiles,p.X,p.Z) || offset==0)return p;
                offset-=Math.Sign(offset)*Math.Min(100,Math.Abs(offset));
            }
        }
        private static short Approach(int delta,int delay)=>(short)Math.Max(-1000,Math.Min(1000,delta*10/(2*(delay+1))));
    }
}
#endif
