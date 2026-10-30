#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Explicit development scenario. Only ordinary input commands; never mutates a running game.</summary>
    public sealed class CooperativeFishingSmokePilot
    {
        private ulong observed=ulong.MaxValue;
        private int stage,previousOwner=-1;
        public bool SharedFish { get; private set; }
        public bool StolenFish { get; private set; }
        public bool SharedPiranha { get; private set; }
        public int Stage=>stage;
        public static IslandMap CreateMap()
        {
            var map=IslandMap.CreatePlatform();
            // A close shoal and slow reel leave time to observe handovers; no free money or injected catches.
            return new IslandMap(map.Shore,map.Pier,map.Spawns,new[]{new Point(0,-5200)},map.Platform,
                new FishingConfig(reelSpeed:600,enemies:new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30)));
        }
        public void Observe(HookState s)
        {
            if(observed==s.Steps)return;observed=s.Steps;
            int count=s.Players.Count(p=>p.FishId==0),owner=s.Fish[0].Owner;
            SharedFish|=count>=2;SharedPiranha|=s.Players.Count(p=>p.HookedPiranha==1)>=2;
            if(stage==1 && previousOwner>=0 && owner>=0 && owner!=previousOwner && count==1 && s.Players[previousOwner].FishId<0)StolenFish=true;
            previousOwner=owner;
            int active=Math.Min(2,s.Players.Count);
            if(stage==0 && s.Players.Take(active).All(p=>p.Catches>0) && (active==1 || SharedFish))stage=active==1?2:1;
            if(stage==1 && StolenFish && s.Players.Sum(p=>p.Catches)>=active+1)stage=2;
            if(stage==2 && s.Enemies.Captures>0 && (active==1 || SharedPiranha))stage=3;
        }
        public bool Complete(HookState s){Observe(s);return stage==3;}
        private Point Stand(int slot)=>stage<2?(slot==0?new Point(-1800,-2600):new Point(1800,-2600)):
            (slot==0?new Point(-2600,-1000):new Point(-2600,1000));
        public HookInput Sample(IslandMap map,HookState s,int slot,ulong requested)
        {
            Observe(s);if(stage==3 || slot>=2)return default;
            var p=s.Players[slot];if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
            int leadFrames=(int)Math.Min(30UL,requested>s.Steps?requested-s.Steps-1:0);
            var stand=Stand(slot);int dx=stand.X-p.X,dz=stand.Z-p.Z;
            // Commands arrive a few frames later over UDP. Ease into a stand so queued movement
            // cannot perpetually overshoot it and prevent the other angler from casting.
            if(Math.Abs(dx)>70)return new HookInput(Approach(dx,leadFrames),0);
            if(Math.Abs(dz)>70)return new HookInput(0,Approach(dz,leadFrames));
            int active=Math.Min(2,s.Players.Count);
            for(int i=0;i<active;i++)
            {var at=Stand(i);if(IslandMap.DistanceSquared(s.Players[i].X,s.Players[i].Z,at.X,at.Z)>150L*150)return default;}
            int speed=FishingRules.Speed(s,p),range=FishingRules.Range(map,s,p);
            if(stage==2)
            {
                var f=s.Enemies.Piranhas[0];if(f.RespawnTicks>0 || f.Escape.Active)return default;
                return new HookInput(0,0,f.X,f.Z,true);
            }
            var fish=s.Fish[0];
            if(fish.Owner>=0)
            {
                if(slot==0 || stage==1 && fish.Owner==1)return default;
                var lead=s.Players[fish.Owner];
                bool opposite=CooperativeFishing.IsOpposite(new Point(fish.X,fish.Z),new Point(lead.X,lead.Z),new Point(p.X,p.Z));
                if(stage==1?!opposite:opposite)return default;
                int frames=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,fish.X,fish.Z))*30/speed+leadFrames;
                int length=HookGame.Sqrt(IslandMap.DistanceSquared(lead.X,lead.Z,fish.X,fish.Z));
                int travel=Math.Min(length,frames*s.Tackle!.Config.ReelSpeed/30);
                int x=fish.X+(length==0?0:(lead.X-fish.X)*travel/length),z=fish.Z+(length==0?0:(lead.Z-fish.Z)*travel/length);
                return new HookInput(0,0,x,z,true);
            }
            if(stage==1 && slot!=0)return default;
            var future=FishMotion.Predict(fish,map.FishSpots[0],leadFrames);
            for(int ticks=1;ticks<=Math.Min(120,range*30/speed+1);ticks++)
            {
                FishMotion.Advance(ref future,map.FishSpots[0]);if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                int distance=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                if(distance<=range && Math.Abs(distance-ticks*speed/30)<speed/60+40)
                    return new HookInput(0,0,future.X,future.Z,true);
            }
            return default;
        }
        private static short Approach(int delta,int leadFrames)=>(short)Math.Max(-1000,Math.Min(1000,delta*10/(2*(leadFrames+1))));
    }
}
#endif
