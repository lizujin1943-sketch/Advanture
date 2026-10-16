using System;
namespace Advanture.Tactical.Simulation;

public sealed partial class TacticalGame
{
    private sealed partial class Model
    {
        private const int Range = 100000, EyeHeight = 1650, BodyHeight = 1900;
        /// <summary>把毫秒配置向上取整成逻辑帧；所有端都使用会话的同一帧率。</summary>
        private ulong Duration(uint milliseconds) => Math.Max(1UL, ((ulong)_rate * milliseconds + 999) / 1000);

        /// <summary>先生成全部射击结果，最后同时扣血；遍历玩家的先后顺序不会让先处理者免受还击。</summary>
        private void ResolveCombat(ulong tick, TacticalInput[] inputs, bool[] active, CombatModel[] combat)
        {
            var damage=new int[combat.Length]; var attackers=new int[combat.Length]; Array.Fill(attackers,-1);
            for (int i=0; i<combat.Length; i++)
            {
                var c=combat[i]; var input=inputs[i];
                if (!active[i] || c.Health==0) continue;
                if (input.Reload && c.ReloadUntilTick==0 && c.Ammo<30 && c.Reserve>0)
                    c.ReloadUntilTick=checked(tick+Duration(2400));
                if (!input.Fire || c.ReloadUntilTick>0 || c.Ammo==0 || tick<c.NextFireTick) continue;
                c.Ammo--; c.NextFireTick=checked(tick+Duration(100));
                c.ShotSequence=checked(c.ShotSequence+1); c.LastShotTick=tick;
                int target=Trace(i, active, combat, c);
                c.LastHitSlot=target<0?-1:_players[target].Slot;
                if (target>=0) { c.Hits=checked(c.Hits+1); damage[target]+=25; if(attackers[target]<0) attackers[target]=i; }
            }
            for(int i=0;i<combat.Length;i++)
            {
                var c=combat[i]; if(damage[i]==0 || c.Health==0) continue;
                c.Health=Math.Max(0,c.Health-damage[i]);
                if(c.Health==0)
                {
                    c.Deaths=checked(c.Deaths+1); c.RespawnAtTick=checked(tick+Duration(3000)); c.ReloadUntilTick=0;
                    var killer=combat[attackers[i]]; killer.Kills=checked(killer.Kills+1);
                }
            }
        }

        /// <summary>以眼睛和冻结朝向发出射线。距离参数精度为 0.001 毫米，全过程只做整数运算。</summary>
        private int Trace(int shooter, bool[] active, CombatModel[] combat, CombatModel shot)
        {
            var source=_players[shooter];
            shot.ShotX=source.X; shot.ShotY=EyeHeight; shot.ShotZ=source.Z;
            FixedAim.Direction(source.Yaw,source.Pitch,out int dx,out int dy,out int dz);
            long nearest=Range*1000L; int hit=-1;
            // 地面先参与遮挡，墙体同距离时优先于角色，不能隔着墙边造成伤害。
            if(dy<0) nearest=Math.Min(nearest, ((long)EyeHeight*FixedAim.Scale*1000-dy-1)/(-dy));
            foreach(var wall in _map.Obstacles)
                if(Intersect(source.X,EyeHeight,source.Z,dx,dy,dz,wall.MinX,wall.MinY,wall.MinZ,wall.MaxX,wall.MaxY,wall.MaxZ,nearest,out long distance))
                    nearest=distance;
            for(int i=0;i<_players.Length;i++)
            {
                if(i==shooter || !active[i] || combat[i].Health==0) continue;
                var p=_players[i]; int radius=TacticalMap.PlayerRadius;
                if(Intersect(source.X,EyeHeight,source.Z,dx,dy,dz,p.X-radius,0,p.Z-radius,p.X+radius,BodyHeight,p.Z+radius,nearest,out long distance) && distance<nearest)
                { nearest=distance; hit=i; }
            }
            shot.EndX=source.X+(int)((long)dx*nearest/(FixedAim.Scale*1000L));
            shot.EndY=EyeHeight+(int)((long)dy*nearest/(FixedAim.Scale*1000L));
            shot.EndZ=source.Z+(int)((long)dz*nearest/(FixedAim.Scale*1000L));
            return hit;
        }

        private static bool Intersect(int x,int y,int z,int dx,int dy,int dz,int minX,int minY,int minZ,int maxX,int maxY,int maxZ,long limit,out long distance)
        {
            long enter=0,exit=limit;
            bool hit=Slab(x,dx,minX,maxX,ref enter,ref exit) && Slab(y,dy,minY,maxY,ref enter,ref exit) && Slab(z,dz,minZ,maxZ,ref enter,ref exit);
            distance=enter;return hit;
        }
        /// <summary>将射线进入/离开单个轴区间的时间相交。向内舍入，避免把边界外的射线误判成命中。</summary>
        private static bool Slab(int origin,int direction,int min,int max,ref long enter,ref long exit)
        {
            if(direction==0) return origin>=min && origin<=max;
            long a=((long)min-origin)*FixedAim.Scale*1000, b=((long)max-origin)*FixedAim.Scale*1000;
            long d=direction;
            if(d<0) { long old=a; a=-b; b=-old; d=-d; }
            long lo=a/d+(a%d>0?1:0), hi=b/d-(b%d<0?1:0);
            enter=Math.Max(enter,lo);exit=Math.Min(exit,hi);return enter<=exit;
        }
    }
}
