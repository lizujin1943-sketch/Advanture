using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private const int CrabBodyId=1000,PiranhaBodyId=2000,MonsterBodyId=3000,BossBodyId=4000,EldritchBodyId=5000;
        private const int GroundBodies=1,WaterBodies=2;
        private readonly CombatSpacing combatSpacing=new CombatSpacing();
        private readonly List<CombatBody> combatBodies=new List<CombatBody>();
        private Func<CombatBody,Point,Point,bool>? combatSpacingPath;

        /// <summary>唯一帧末入口：所有主动移动、攻击、地形变化与复活结束后，统一处理单位占位。</summary>
        private void ResolveCombatSpacing()
        {
            if(map.Fishing==null)return;
            crabNavigation.Refresh(tiles);combatBodies.Clear();
            foreach(var p in players)
            {
                if(p.WaitingToJoin || p.Revival.Dead)continue;
                combatBodies.Add(new CombatBody(p.Slot,p.X,p.Z,EnemyGeometry.PlayerRadius,p.Swimming.InWater?WaterBodies:GroundBodies,
                    p.Unavailable || p.Vitals.Downed || p.PulledBy>=0));
            }
            for(int i=0;i<crabs.Length;i++)
            {
                var c=crabs[i].Capture();if(!c.Damageable)continue;
                combatBodies.Add(new CombatBody(CrabBodyId+i,c.X,c.Z,CrabConfig.BodyRadius,GroundBodies));
            }
            if(enemies!=null)
            {
                for(int i=0;i<enemies.PiranhaCount;i++)
                {
                    var f=enemies.GetPiranha(i);if(f.Owner>=0 || f.RespawnTicks>0 || f.Escape.Active)continue;
                    combatBodies.Add(new CombatBody(PiranhaBodyId+i,f.X,f.Z,250,WaterBodies));
                }
                var m=enemies.Monster;
                if(m.Phase==MonsterPhase.Windup)combatBodies.Add(new CombatBody(MonsterBodyId,m.X,m.Z,EnemyGeometry.MonsterRadius,WaterBodies,true));
            }
            for(int i=0;i<eldritch.Count;i++)
            {var c=eldritch.Get(i);if(c.Damageable)combatBodies.Add(new CombatBody(EldritchBodyId+i,c.X,c.Z,EldritchCatalog.BodyRadius,EldritchCatalog.Get(c.Kind).Habitat==EldritchHabitat.Water?WaterBodies:GroundBodies));}
            var abyss=boss.Capture();
            if(abyss.Active)
            {
                // 本体保持固定；触手属于攻击轨迹，不把抬起或横扫的触手当作地面墙体。
                var head=BossPoseData.Head(abyss,false);
                combatBodies.Add(new CombatBody(BossBodyId,head.X,head.Z,Math.Max(head.RadiusX,head.RadiusZ),GroundBodies|WaterBodies,true));
            }
            combatSpacing.Resolve(combatBodies,combatSpacingPath??=CombatSpacingCanMove);
            foreach(var body in combatBodies)
            {
                if(body.Fixed)continue;
                if(body.Id<CrabBodyId)
                {
                    var p=players[body.Id];int dx=body.X-p.X,dz=body.Z-p.Z;
                    if(p.Knockback.Active){p.Knockback.OriginX+=dx;p.Knockback.OriginZ+=dz;}
                    p.X=body.X;p.Z=body.Z;players[body.Id]=p;
                }
                else if(body.Id<PiranhaBodyId)crabs[body.Id-CrabBodyId].TryDisplace(tiles,body.X,body.Z);
                else if(body.Id>=EldritchBodyId)eldritch.Displace(body.Id-EldritchBodyId,body.X,body.Z);
                else if(body.Id<MonsterBodyId)enemies!.TryDisplacePiranha(body.Id-PiranhaBodyId,body.X,body.Z);
            }
        }
        private bool CombatSpacingCanMove(CombatBody body,Point from,Point to)
        {
            if(body.Id>=CrabBodyId && body.Id<PiranhaBodyId)return crabNavigation.Segment(from.X,from.Z,to.X,to.Z);
            if(body.Id>=EldritchBodyId)return eldritch.CanDisplace(body.Id-EldritchBodyId,from,to);
            bool player=body.Id<CrabBodyId;
            if(player)
            {
                var p=players[body.Id];
                if(p.Knockback.Active && (!map.InWorldBounds(p.Knockback.OriginX+to.X-p.X,p.Knockback.OriginZ+to.Z-p.Z)))return false;
            }
            int distance=Sqrt(IslandMap.DistanceSquared(from.X,from.Z,to.X,to.Z)),steps=Math.Max(1,(distance+49)/50);
            for(int i=0;i<=steps;i++)
            {
                int x=from.X+(int)((long)(to.X-from.X)*i/steps),z=from.Z+(int)((long)(to.Z-from.Z)*i/steps);
                if(SwimmingTerrain.Obstacle(map,x,z))return false;
                // 水中与甲板上的单位处于不同高度，分离不得跨岸或将水下食人鱼推上甲板。
                bool ground=map.Platform==null?map.CanStand(x,z):crabNavigation.CoversFootprint(x,z,PlatformRules.Footprint);
                if(player && ground!=(body.Layers==GroundBodies))return false;
            }
            return true;
        }
    }
}
