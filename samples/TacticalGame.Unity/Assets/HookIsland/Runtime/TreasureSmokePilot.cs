#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Explicit acceptance map and ordinary movement/cast/open inputs only.</summary>
    public sealed class TreasureSmokePilot
    {
        public int Stage { get; private set; }
        public bool Shared { get; private set; }
        public bool Landed { get; private set; }
        public bool Opened { get; private set; }
        public int MaxParticipants { get; private set; }
        public static IslandMap CreateMap()
        {
            var map=IslandMap.CreatePlatform();
            return new IslandMap(map.Shore,map.Pier,map.Spawns,new[]{new Point(18000,18000)},map.Platform,
                new FishingConfig(treasure:new TreasureConfig(initialDelayTicks:30,slotDelayTicks:30,driftSpeed:0,rareDriftSpeed:0,
                    floatLifetimeTicks:9000,respawnTicks:9000,largeReelSpeed:600)));
        }
        public void Observe(HookState s)
        {
            Shared|=s.Players.Count(p=>p.HookedChest==3)>=2;
            Landed|=s.Treasure.Chests.Any(c=>c.Kind==TreasureKind.Large && c.Phase==TreasurePhase.Landed);
            Opened|=s.Players.Any(p=>p.LastTreasureResult==TreasureResult.Success);
            MaxParticipants=Math.Max(MaxParticipants,s.Players.Count(p=>p.HookedChest==3));
            bool smallDone=s.Treasure.Chests[0].Phase==TreasurePhase.Waiting && s.Treasure.Chests[0].Generation>0;
            bool rareDone=s.Treasure.Chests[1].Phase==TreasurePhase.Waiting && s.Treasure.Chests[1].Generation>0;
            Stage=!smallDone?0:!rareDone?1:s.Players.Count==1 || Opened?3:2;
        }
        public bool Complete(HookState s){Observe(s);return Stage==3 && (s.Players.Count==1 || Shared && Landed && Opened && MaxParticipants==s.Players.Count && s.Players.All(p=>p.TreasuresCollected>0));}
        private static Point Stand(TreasureChest c,int slot,int count)
        {
            int offset=count==1?0:count==2?-600+slot*1200:-1800+slot*1200;
            return Math.Abs(c.DirectionX)>0?new Point(c.DirectionX>0?2600:-2600,offset):
                new Point(offset,c.DirectionZ>0?2600:-2600);
        }
        public HookInput Sample(IslandMap map,HookState s,int slot,ulong requested)
        {
            Observe(s);if(Stage==3)return default;
            var c=s.Treasure.Chests[Stage];var p=s.Players[slot];
            if(p.Phase!=HookPhase.Ready || p.Cooldown>0 || c.Phase==TreasurePhase.Waiting || Stage<2 && slot>0)return default;
            if(c.Phase==TreasurePhase.Landed && slot!=c.Owner)return default;
            int delay=(int)Math.Min(30UL,requested>s.Steps?requested-s.Steps-1:0);
            Point stand=c.Phase==TreasurePhase.Landed?new Point(Math.Max(-2600,Math.Min(2600,c.X)),Math.Max(-2600,Math.Min(2600,c.Z))):Stand(c,slot,Stage<2?1:s.Players.Count);
            int dx=stand.X-p.X,dz=stand.Z-p.Z;
            if(Math.Abs(dx)>60)return new HookInput(Approach(dx,delay),0);
            if(Math.Abs(dz)>60)return new HookInput(0,Approach(dz,delay));
            if(c.Phase==TreasurePhase.Landed)
                return new HookInput(0,0,treasure:new TreasureCommand(c.Id+1,c.Generation,p.LastTreasureRequest+1));
            if(Stage==2)
            {
                foreach(var q in s.Players)
                {var at=Stand(c,q.Slot,s.Players.Count);if(IslandMap.DistanceSquared(q.X,q.Z,at.X,at.Z)>140L*140)return default;}
            }
            return new HookInput(0,0,c.X,c.Z,true);
        }
        private static short Approach(int delta,int delay)=>(short)Math.Max(-1000,Math.Min(1000,delta*10/(2*(delay+1))));
    }
}
#endif
