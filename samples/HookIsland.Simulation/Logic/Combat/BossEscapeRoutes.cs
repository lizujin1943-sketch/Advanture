using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Conservative time-labelled walking search through authoritative floor and player footprint.
/// Checks the complete pending damage windows, future broken strips, and actual ink movement speed.</summary>
public static class BossEscapeRoutes
{
    public static bool CanEscape(BossState b,IReadOnlyList<PlatformTile> tiles,Fisher player)
    {
        if(!b.TownArena)return true;
        int horizon=0;
        foreach(var l in b.Limbs)if(l.Attack!=BossAttack.None)
            horizon=Math.Max(horizon,(l.Attack==BossAttack.Slam?45:60)-l.AttackTicks);
        if(b.InkWindupTicks>0 && b.InkWindupTicks<60)horizon=Math.Max(horizon,60-b.InkWindupTicks);
        if(horizon<=0)return true;
        bool Unsafe(int x,int z,int t)
        {
            foreach(var l in b.Limbs)
            {
                if(l.Attack==BossAttack.None || (l.HitPlayers&(1<<player.Slot))!=0)continue;
                int clock=l.AttackTicks+t;
                if(l.Attack==BossAttack.Slam && clock==45 && BossGeometry.SlamContainsXZ(b,l.Id,x,z,200) ||
                    l.Attack==BossAttack.Sweep && BossGeometry.SweepContainsXZ(b,l.Id,clock,x,z,200))return true;
            }
            if(t>0 && b.InkTicks>=t && (180-b.InkTicks+t)%30==0 && BossGeometry.InkContainsXZ(b,x,z,200))return true;
            return b.InkWindupTicks>0 && b.InkWindupTicks<60 && b.InkWindupTicks+t>=60 && BossGeometry.InkContainsXZ(b,x,z,200);
        }
        var floors=new Dictionary<int,IReadOnlyList<PlatformTile>>();
        bool SafeUntilEnd(int x,int z,int t)
        {
            for(int at=t;at<=horizon;at++)
                if(Unsafe(x,z,at) || !TownLayout.CanStand(Floor(at),x,z,200))return false;
            return true;
        }
        if(SafeUntilEnd(player.X,player.Z,0))return true;
        IReadOnlyList<PlatformTile> Floor(int at)
        {
            int mask=0;foreach(var l in b.Limbs)if(l.Attack==BossAttack.Slam && l.AttackTicks+at>=45)
                foreach(var segment in TownLayout.WoodSegments)if(BossGeometry.SlamContainsXZ(b,l.Id,segment.X,segment.Z))mask|=1<<(segment.BandId*4+segment.SegmentId);
            if(!floors.TryGetValue(mask,out var floor))
            {floor=tiles.Where(t=>{foreach(var s in TownLayout.WoodSegments)if(s.CellX==t.X && s.CellZ==t.Z)return (mask&(1<<(s.BandId*4+s.SegmentId)))==0;return true;}).ToArray();floors[mask]=floor;}
            return floor;
        }
        const int step=500;
        var best=new Dictionary<(int,int),int>();var queue=new SortedSet<(int,int,int)>();
        best[(player.X,player.Z)]=0;queue.Add((0,player.X,player.Z));
        var directions=new[]{new Point(step,0),new Point(-step,0),new Point(0,step),new Point(0,-step)};
        while(queue.Count>0)
        {
            var node=queue.Min;queue.Remove(node);int time=node.Item1,x=node.Item2,z=node.Item3;
            if(best[(x,z)]!=time)continue;
            if(SafeUntilEnd(x,z,time))return true;
            foreach(var d in directions)
            {
                int at=time;bool valid=true;
                // Each tick travels at most 100mm (3m/s), or 65mm while ink-slowed.
                int progress=0;
                while(progress<step)
                {
                    int px=x+d.X*progress/step,pz=z+d.Z*progress/step;
                    bool slow=b.InkTicks>at && BossGeometry.InkContainsXZ(b,px,pz);
                    progress=Math.Min(step,progress+(slow?65:100));at++;
                    px=x+d.X*progress/step;pz=z+d.Z*progress/step;
                    if(at>horizon || Unsafe(px,pz,at) || !TownLayout.CanStand(Floor(at),px,pz,200)){valid=false;break;}
                }
                int nx=x+d.X,nz=z+d.Z;
                if(!valid || best.TryGetValue((nx,nz),out int previous) && previous<=at)continue;
                best[(nx,nz)]=at;queue.Add((at,nx,nz));
            }
        }
        return false;
    }
}
