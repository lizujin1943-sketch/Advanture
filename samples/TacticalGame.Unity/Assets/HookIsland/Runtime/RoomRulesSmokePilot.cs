#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Acceptance via ordinary movement, casts and host commands; never modifies simulation state.</summary>
    public sealed class RoomRulesSmokePilot
    {
        public int Stage { get; private set; }
        public bool DisabledSeen { get; private set; }
        public bool EnabledSeen { get; private set; }
        public bool BlockedLandCast { get; private set; }
        public bool LandPullSeen { get; private set; }
        public bool ReleasedLandPull { get; private set; }
        public bool Rescued { get; private set; }
        public static IslandMap CreateMap()
        {
            var map=IslandMap.CreatePlatform();
            return new IslandMap(map.Shore,map.Pier,new[]{new Point(-1900,-2000),new Point(1900,-2000),new Point(1400,1400),new Point(-1400,1400)},
                map.FishSpots,map.Platform,new FishingConfig(enemies:EnemyConfig.Disabled));
        }
        public void Observe(HookState state)
        {
            var rules=state.RoomRules;DisabledSeen|=rules.LastRequest>=1 && !rules.FriendlyPranksEnabled;
            EnabledSeen|=rules.LastRequest==2 && rules.FriendlyPranksEnabled;
            if(state.Players.Count==1){if(rules.LastRequest==3 && !rules.FriendlyPranksEnabled)Stage=7;return;}
            var host=state.Players[0];var guest=state.Players[1];
            if(Stage==0 && rules.LastRequest==1 && !rules.FriendlyPranksEnabled)Stage=1;
            if(Stage==1 && guest.Casts>=1 && guest.Phase==HookPhase.Ready && guest.PlayerHooks==0){BlockedLandCast=true;Stage=2;}
            if(Stage==2 && rules.LastRequest==2 && rules.FriendlyPranksEnabled)Stage=3;
            if(Stage==3 && host.PulledBy==1){LandPullSeen=true;Stage=4;}
            if(Stage==4 && rules.LastRequest==3 && !rules.FriendlyPranksEnabled && host.PulledBy<0){ReleasedLandPull=true;Stage=5;}
            if(Stage==5 && host.Swimming.InWater && guest.Phase==HookPhase.Ready)Stage=6;
            if(Stage==6 && guest.PlayerHooks>=2 && !host.Swimming.InWater && host.PulledBy<0){Rescued=true;Stage=7;}
        }
        public bool Complete(HookState state)=>Stage==7 && DisabledSeen && EnabledSeen && !state.RoomRules.FriendlyPranksEnabled &&
            (state.Players.Count==1 || (BlockedLandCast && LandPullSeen && ReleasedLandPull && Rescued));
        public HookInput Sample(HookState state,int slot)
        {
            Observe(state);if(Complete(state))return default;
            var rules=state.RoomRules;
            if(state.Players.Count==1)return rules.LastRequest>=3?default:Set(rules.LastRequest==1,rules.LastRequest+1);
            if(slot==0)
            {
                if(Stage==0)return Set(false,1);
                if(Stage==2)return Set(true,2);
                if(Stage==4)return Set(false,3);
                if(Stage==5 && !state.Players[0].Swimming.InWater)return new HookInput(0,-500);
            }
            if(slot==1)
            {
                var host=state.Players[0];var guest=state.Players[1];
                if((Stage==1 && guest.Casts==0) || (Stage==3 && guest.PlayerHooks==0) || (Stage==6 && guest.PlayerHooks==1))
                    return new HookInput(0,0,host.X,host.Z,true);
            }
            return default;
        }
        private static HookInput Set(bool enabled,uint request)=>new HookInput(0,0,roomRules:new RoomRulesCommand(enabled,request));
    }
}
#endif
