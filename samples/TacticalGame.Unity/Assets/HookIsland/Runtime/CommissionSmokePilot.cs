#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    // Explicit development fixture: progress and income must come from ordinary fishing inputs.
    public sealed class CommissionSmokePilot
    {
        public static IslandMap CreateMap(bool defaults=false)
        {
            var map=IslandMap.CreatePlatform();
            var commissions=defaults?new CommissionConfig():new CommissionConfig(new[]{new CommissionDefinition(2,1),new CommissionDefinition(2,1),new CommissionDefinition(2,1)});
            return new IslandMap(map.Shore,map.Pier,map.Spawns,new[]{new Point(0,-5200)},map.Platform,new FishingConfig(commissions:commissions));
        }
        public bool Complete(HookState s)=>s.Commissions.Completed==3;
        public HookInput Sample(IslandMap map,HookState s,int slot,ulong tick)
        {
            if(Complete(s) || s.Tackle==null)return default;
            var p=s.Players[slot];if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
            int delay=(int)Math.Min(30UL,tick>s.Steps?tick-s.Steps-1:0);
            var stand=new Point((slot*2-s.Players.Count+1)*450,-2600);
            int dx=stand.X-p.X,dz=stand.Z-p.Z;
            if(Math.Abs(dx)>70)return new HookInput(Approach(dx,delay),0);
            if(Math.Abs(dz)>70)return new HookInput(0,Approach(dz,delay));
            var fish=s.Fish[0];int speed=FishingRules.Speed(s,p),range=FishingRules.Range(map,s,p);
            if(fish.Owner>=0)
            {
                var lead=s.Players[fish.Owner];int length=HookGame.Sqrt(IslandMap.DistanceSquared(lead.X,lead.Z,fish.X,fish.Z));
                int frames=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,fish.X,fish.Z))*30/speed+delay;
                int count=CooperativeFishing.Count(s.Players,lead);
                int reel=CooperativeFishing.Speed(s.Tackle.Config.ReelSpeed,count,s.Tackle.Config.CooperativeSpeedBonusPermille);
                int travel=Math.Min(length,frames*reel/30);
                return new HookInput(0,0,fish.X+(length==0?0:(lead.X-fish.X)*travel/length),fish.Z+(length==0?0:(lead.Z-fish.Z)*travel/length),true);
            }
            // Rest using the game's normal recovery, so repeat captures don't depend on a free stamina refill.
            if(p.Stamina<FishingRules.MaxStamina(s,p)*9/10)return default;
            var future=FishMotion.Predict(fish,map.FishSpots[0],delay);
            for(int ticks=1;ticks<=Math.Min(120,range*30/speed+1);ticks++)
            {
                FishMotion.Advance(ref future,map.FishSpots[0]);if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                int distance=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                if(distance<=range && Math.Abs(distance-ticks*speed/30)<speed/60+40)return new HookInput(0,0,future.X,future.Z,true);
            }
            return default;
        }
        private static short Approach(int delta,int delay)=>(short)Math.Max(-1000,Math.Min(1000,delta*10/(2*(delay+1))));
    }
}
#endif
