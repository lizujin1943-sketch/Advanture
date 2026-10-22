#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    public sealed class OceanSmokePilot
    {
        private int trackedVisit=-1;
        private uint generation;
        private long catchesAtHook;
        public bool RecoverySeen {get;private set;}
        public bool VisitCaught {get;private set;}
        public static IslandMap CreateMap(bool defaults=false)
        {
            var basis=IslandMap.CreatePlatform();
            if(defaults)return OceanRules.CreateMap(basis,new FishingConfig(commissions:new CommissionConfig(),ocean:new OceanConfig()));
            var layout=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,new[]{new Point(0,-5200),new Point(0,-8400),new Point(0,-12000)},basis.Platform);
            return OceanRules.CreateMap(layout,new FishingConfig(commissions:new CommissionConfig(new[]{new CommissionDefinition(2,1),new CommissionDefinition(2,1),new CommissionDefinition(6,2)}),
                ocean:new OceanConfig(new[]{0,1,2},noCatchTicks:180,recoveryTicks:60,recoveryCooldownTicks:480,visitTicks:180)));
        }
        public bool Complete(HookState state)
        {Observe(state);return state.Ocean.RewardedStage==2 && RecoverySeen && VisitCaught;}
        private void Observe(HookState state)
        {
            if(state.Tackle==null)return;
            if(state.Ocean.RewardedStage==2 && state.Ocean.LastEvent==OceanEvent.Recovery)RecoverySeen=true;
            if(!RecoverySeen)return;
            int first=state.Tackle.Config.Ocean.Unlocks.Count;
            if(trackedVisit>=0 && !state.Ocean.Visits[trackedVisit-first].Active)
            {
                VisitCaught|=state.Players.Sum(p=>(long)p.Catches)>catchesAtHook && state.Fish[trackedVisit].Generation!=generation;
                trackedVisit=-1;
            }
            if(trackedVisit<0)
                for(int i=first;i<state.Fish.Count;i++)
                    if(state.Ocean.Visits[i-first].Active && state.Fish[i].Phase==FishPhase.Hooked)
                    {trackedVisit=i;generation=state.Fish[i].Generation;catchesAtHook=state.Players.Sum(p=>(long)p.Catches);break;}
        }
        public HookInput Sample(IslandMap map,HookState state,int slot,ulong tick)
        {
            if(Complete(state) || state.Tackle==null)return default;
            var p=state.Players[slot];if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
            int id=0;
            if(state.Commissions.Completed>=2)
            {
                if(state.Ocean.RewardedStage<2 || !RecoverySeen)return default;
                id=-1;int first=state.Tackle.Config.Ocean.Unlocks.Count;
                for(int i=first;i<state.Fish.Count;i++)if(state.Ocean.Visits[i-first].Active){id=i;break;}
                if(id<0)return default;
            }
            var home=OceanRules.Home(map,state,id);var fish=state.Fish[id];
            int offset=(slot*2-state.Players.Count+1)*300;
            var stand=Math.Abs(home.X)>Math.Abs(home.Z)?new Point(Math.Sign(home.X)*2600,Clamp(home.Z,-1200,1200)+offset):
                new Point(Clamp(home.X,-1200,1200)+offset,Math.Sign(home.Z)*2600);
            int delay=(int)Math.Min(30UL,tick>state.Steps?tick-state.Steps-1:0),dx=stand.X-p.X,dz=stand.Z-p.Z;
            if(Math.Abs(dx)>70)return new HookInput(Approach(dx,delay),0);
            if(Math.Abs(dz)>70)return new HookInput(0,Approach(dz,delay));
            int speed=FishingRules.Speed(state,p),range=FishingRules.Range(map,state,p);if(speed==0 || range==0)return default;
            if(fish.Owner>=0)
            {
                var lead=state.Players[fish.Owner];int length=HookGame.Sqrt(IslandMap.DistanceSquared(lead.X,lead.Z,fish.X,fish.Z));
                int frames=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,fish.X,fish.Z))*30/speed+delay;
                int reel=CooperativeFishing.Speed(state.Tackle.Config.ReelSpeed,CooperativeFishing.Count(state.Players,lead),state.Tackle.Config.CooperativeSpeedBonusPermille);
                int travel=Math.Min(length,frames*reel/30);
                return new HookInput(0,0,fish.X+(length==0?0:(lead.X-fish.X)*travel/length),fish.Z+(length==0?0:(lead.Z-fish.Z)*travel/length),true);
            }
            if(p.Stamina<FishingRules.MaxStamina(state,p)*9/10)return default;
            var future=FishMotion.Predict(fish,home,delay);
            for(int frames=1;frames<=Math.Min(120,range*30/speed+1);frames++)
            {
                FishMotion.Advance(ref future,home);if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                int distance=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                if(distance<=range && Math.Abs(distance-frames*speed/30)<speed/60+40)return new HookInput(0,0,future.X,future.Z,true);
            }
            return default;
        }
        private static int Clamp(int value,int min,int max)=>Math.Max(min,Math.Min(max,value));
        private static short Approach(int delta,int delay)=>(short)Clamp(delta*10/(2*(delay+1)),-1000,1000);
    }
}
#endif
