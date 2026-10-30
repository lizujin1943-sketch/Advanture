#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using System;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Opt-in normal-input scenario: free land rescue after towing, then an earned paid respawn.</summary>
    public sealed class RevivalSmokePilot
    {
        public bool FreeRescueSeen { get; private set; }
        public bool PaidRespawnSeen { get; private set; }
        private void Observe(HookState s)
        {
            if(s.Players.Count!=4)return;
            var p=s.Players[1];FreeRescueSeen|=p.TimesHooked>0 && p.Vitals.Health>0 && p.Revival.Deaths==0;
            p=s.Players[2];PaidRespawnSeen|=p.Revival.LastResult==RespawnResult.Success && p.Revival.CoinsSpent>0 && !p.Vitals.Downed;
        }
        public bool Complete(HookState s){Observe(s);return FreeRescueSeen && PaidRespawnSeen;}
        private static short Axis(int delta,int lead)=>(short)Math.Max(-1000,Math.Min(1000,delta*5/(lead+1)));
        private static HookInput Move(Fisher p,int x,int z,int lead)
        {
            if(Math.Abs(p.X-x)>80)return new HookInput(Axis(x-p.X,lead),0);
            if(Math.Abs(p.Z-z)>80)return new HookInput(0,Axis(z-p.Z,lead));return default;
        }
        public HookInput Sample(IslandMap map,HookState s,int slot,ulong tick)
        {
            Observe(s);var p=s.Players[slot];int lead=(int)Math.Min(30,tick>s.Steps?tick-s.Steps-1:0);
            if(slot==2 && p.Vitals.Downed && p.Revival.HasQuote && p.Coins>=p.Revival.DeathCost)
                return new HookInput(0,0,revival:new RevivalCommand(respawnRequest:p.Revival.LastRequest+1,deathId:p.Revival.DownedId));
            if(p.Vitals.Downed || p.PulledBy>=0 || p.Knockback.Active)return default;
            if(slot==0)
            {
                var target=s.Players[1];
                if(target.Vitals.Downed && !target.Revival.Dead)
                {
                    if(target.Swimming.InWater && p.Phase==HookPhase.Ready)
                        return new HookInput(0,0,target.X,target.Z,true);
                    if(RevivalSystem.CanHelp(map,s.Tiles,p,target))return new HookInput(0,0,revival:new RevivalCommand(rescueSlot:1));
                }
                if(!FreeRescueSeen)return Move(p,-2600,-1400,lead);
            }
            if(slot==1 && !FreeRescueSeen)return p.X> -3750?new HookInput(-1000,0):default;
            if(slot==2 && !PaidRespawnSeen)
            {
                // A waterball now detaches the catch immediately. Recover from an accidental
                // fall before resuming earnings; only the funded scenario deliberately stays at sea.
                if(p.Swimming.InWater && p.CoinsEarned<60)
                    return p.Phase==HookPhase.Ready?Move(p,1400,-2200,lead):new HookInput(0,0,cancel:true);
                if(p.CoinsEarned>=60)
                {if(p.Phase!=HookPhase.Ready)return default;return p.X<4100?new HookInput(1000,0):default;}
                if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
                if(p.Z> -2400)return Move(p,p.X,-2500,lead);
                int speed=FishingRules.Speed(s,p),range=FishingRules.Range(map,s,p);
                foreach(var fish in s.Fish)
                {
                    if(fish.Owner>=0)continue;var future=FishMotion.Predict(fish,map.FishSpots[fish.Id],lead);
                    for(int n=1;n<=Math.Min(120,range*30/speed+1);n++)
                    {
                        FishMotion.Advance(ref future,map.FishSpots[fish.Id]);if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                        int d=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                        if(d<=range && Math.Abs(d-n*speed/30)<speed/60+40 && (long)FishCatalog.DrainPerTick(fish)*(d*30/s.Tackle!.Config.ReelSpeed+4)<p.Stamina)
                            return new HookInput(0,0,future.X,future.Z,true);
                    }
                }
            }
            return default;
        }
    }
}
#endif
