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
        private static HookInput Move(Fisher p,int x,int z,int lead)
        {
            // 水中接触会同时扰动两个坐标，不能一直修正 X 而不开始修正 Z。组合输入后归一化，保持正常限速。
            if(Math.Abs(p.X-x)<=80 && Math.Abs(p.Z-z)<=80)return default;
            int dx=(x-p.X)*5/(lead+1),dz=(z-p.Z)*5/(lead+1);long square=(long)dx*dx+(long)dz*dz;
            if(square>1000000)
            {
                int length=HookGame.Sqrt(square);if((long)length*length<square)length++;
                dx=(int)((long)dx*1000/length);dz=(int)((long)dz*1000/length);
            }
            return new HookInput((short)dx,(short)dz);
        }
        public HookInput Sample(IslandMap map,HookState s,int slot,ulong tick)
        {
            Observe(s);var p=s.Players[slot];int lead=(int)Math.Min(30,tick>s.Steps?tick-s.Steps-1:0);
            if(slot==2 && p.Vitals.Downed && p.Revival.HasQuote && p.Coins>=p.Revival.DeathCost)
                return new HookInput(0,0,revival:new RevivalCommand(respawnRequest:p.Revival.LastRequest+1,deathId:p.Revival.DownedId));
            if(p.Vitals.Downed || p.PulledBy>=0 || p.Knockback.Active)return default;
            if(slot==0)
            {
                // 地面绕行器不处理游泳；被水球击落后，先取消空钩并游回岸上再继续救人。
                if(p.Swimming.InWater)
                    return p.Phase==HookPhase.Ready?(p.Stamina<15000?default:Move(p,-2200,-400,lead)):new HookInput(0,0,cancel:true);
                var target=s.Players[1];
                if(target.Vitals.Downed && !target.Revival.Dead)
                {
                    if(target.Swimming.InWater && p.Phase==HookPhase.Ready)
                        return new HookInput(0,0,target.X,target.Z,true);
                    if(RevivalSystem.CanHelp(map,s.Tiles,p,target))return new HookInput(0,0,revival:new RevivalCommand(rescueSlot:1));
                }
                // 救援者从侧岸等待，给同伴保留向西落水的通道，再按真实拖拽和帮助规则救援。
                if(!FreeRescueSeen && SmokeNavigation.Toward(map,s,slot,new Point(-2600,-400),lead,out var move))return move;
            }
            // 水球可能改变两个坐标，持续游向约定水点，避免同伴被冲走后超出救援者的射程。
            if(slot==1 && !FreeRescueSeen)return Move(p,-3900,-1400,lead);
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
