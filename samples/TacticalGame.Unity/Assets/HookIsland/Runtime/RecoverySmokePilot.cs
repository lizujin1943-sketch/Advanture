#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Opt-in recovery fixture: normal catches and purchases, with combat disabled to isolate preserved state.</summary>
    public sealed class RecoverySmokePilot
    {
        private readonly RodSmokePilot fishing=new RodSmokePilot();
        private static readonly Point[] Parking={new Point(-1000,-1000),new Point(1000,-1000),new Point(1000,1000),new Point(-1000,1000)};
        public static IslandMap CreateMap()
        {
            var b=IslandMap.CreatePlatform();
            return new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,new FishingConfig(enemies:EnemyConfig.Disabled));
        }
        public static bool AllEntered(HookState state)=>state.Players.All(p=>!p.WaitingToJoin);
        public static bool Purchased(HookState state)=>AllEntered(state) && state.Players.All(p=>p.LastRodRequest>0 && p.LastRodResult==RodResult.Success);
        public static bool Parked(HookState state)=>state.Players.All(p=>p.Phase==HookPhase.Ready && !p.Swimming.InWater &&
            Math.Abs(p.X-Parking[p.Slot].X)<=400 && Math.Abs(p.Z-Parking[p.Slot].Z)<=400);
        public static HookInput Park(Fisher p)
        {
            if(p.Phase!=HookPhase.Ready)return default;
            var goal=Parking[p.Slot];
            if(Math.Abs(p.X-goal.X)>200)return new HookInput((short)(Math.Sign(goal.X-p.X)*1000),0);
            if(Math.Abs(p.Z-goal.Z)>200)return new HookInput(0,(short)(Math.Sign(goal.Z-p.Z)*1000));
            return default;
        }
        public HookInput Sample(IslandMap map,HookState state,int slot,ulong tick)=>fishing.Sample(map,state,slot,tick);
    }
}
#endif
