#if UNITY_EDITOR || DEVELOPMENT_BUILD || HOOK_ISLAND_TESTS
using Advanture.HookIsland.Simulation;
using System.Linq;

namespace Advanture.HookIsland
{
    /// <summary>Explicit development acceptance: only ordinary movement and cast inputs, no state edits.</summary>
    public static class EnemySmokePilot
    {
        public static HookInput Sample(HookState state,int slot)
        {
            if(state.Tackle==null)return default;
            var p=state.Players[slot];if(p.Vitals.Downed || p.Knockback.Active)return default;
            var config=state.Tackle.Config.Enemies;
            if(state.Enemies.Captures==0)
            {
                if(slot!=0)return default;
                if(p.Phase!=HookPhase.Ready)return default;
                var fish=state.Enemies.Piranhas[0];
                // Predict a short patrol segment on a private copy; only cast inputs reach the room.
                int frames=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,fish.X,fish.Z))*HookGame.Rate/FishingRules.Speed(state,p);
                var preview=new EnemySystem(config,state.Enemies);
                for(int i=0;i<frames;i++)preview.Advance(state.Players,_=>{});
                fish=preview.GetPiranha(0);
                return new HookInput(0,0,fish.X,fish.Z,true);
            }
            if(state.Enemies.Interruptions==0)
            {
                if(slot!=0)return default;
                if(p.X< -100)return new HookInput(1000,0);
                if(p.Z> -2400)return new HookInput(0,-1000);
                var m=state.Enemies.Monster;
                if(m.Phase==MonsterPhase.Windup)return new HookInput(0,0,m.X,m.Z,true);
            }
            // Stand near the edge along the incoming direction, so even Lv1's 4 m pushes across the lip.
            else if(!p.Swimming.InWater && state.Enemies.Monster.Phase==MonsterPhase.Windup && state.Enemies.Monster.Target==slot)
            {
                var m=state.Enemies.Monster;
                int dx=m.DirectionX/1000,dz=m.DirectionZ/1000;
                if(System.Math.Abs(p.X+dx/10)>2400 || System.Math.Abs(p.Z+dz/10)>2400)return default;
                return new HookInput((short)dx,(short)dz);
            }
            return default;
        }
        public static bool Complete(HookState state)=>state.Enemies.Captures>0 && state.Enemies.Monster.Cycle>1 && state.Enemies.Interruptions>0 && state.Enemies.Shots>0 &&
            state.Enemies.WaterHits>0 && state.Enemies.Bites>0 && state.Players.Any(p=>p.Swimming.InWater && p.Vitals.Health<100);
    }
}
#endif
