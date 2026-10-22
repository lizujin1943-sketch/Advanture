using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>Bounded deterministic placement, with world/terrain checks supplied by the owning room.</summary>
public static class EnemyPlacement
{
    private static readonly Point[] Directions={new Point(1000,0),new Point(707,707),new Point(0,1000),new Point(-707,707),
        new Point(-1000,0),new Point(-707,-707),new Point(0,-1000),new Point(707,-707)};
    public static Point PiranhaHome(int id)
    {
        int side=id%4,ring=id/4;
        return new Point(side==0?-4800-ring*900:side==1?4800+ring*900:0,
            side==2?4800+ring*900:side==3?-9000-ring*900:0);
    }
    public static Point PatrolGoal(Piranha f)
    {
        var home=PiranhaHome(f.Id);var direction=Directions[(f.PatrolPoint*2+1)%8];
        return new Point(home.X+direction.X*1200/1000,home.Z+direction.Z*1200/1000);
    }
    internal static bool Surface(EnemyConfig config,IReadOnlyList<Fisher> players,ref WaterMonster monster,Func<int,int,bool>? water)
    {
        // Advance even when terrain blocks the attempt; checkpoint recovery preserves this progress.
        uint cycle=monster.Cycle;monster.Cycle=unchecked(cycle+1);
        uint random=FishCatalog.Mix(unchecked(cycle*2891336453u+0xAC8312Fu));
        int first=(int)(random%(uint)players.Count);
        for(int i=0;i<players.Count;i++)
        {
            int slot=(first+i)%players.Count;var p=players[slot];if(p.WaitingToJoin || p.Vitals.Downed)continue;
            if(cycle==0 && Valid(config.MonsterX,config.MonsterZ,p,config.WaterballRange,water))
            {Choose(ref monster,config.MonsterX,config.MonsterZ,slot);return true;}
            for(int attempt=0;attempt<64;attempt++)
            {
                int max=Math.Min(6000,config.WaterballRange-100),min=Math.Min(3500,max);
                random=FishCatalog.Mix(unchecked(random+0x9E3779B9u));int dx=(int)(random%(uint)(2*max+1))-max;
                random=FishCatalog.Mix(unchecked(random+0x9E3779B9u));int dz=(int)(random%(uint)(2*max+1))-max;
                long distance=(long)dx*dx+(long)dz*dz;if(distance<(long)min*min || distance>(long)max*max)continue;
                int x=p.X+dx,z=p.Z+dz;
                if(IslandMap.DistanceSquared(x,z,monster.X,monster.Z)<1000000 || !Valid(x,z,p,config.WaterballRange,water))continue;
                Choose(ref monster,x,z,slot);return true;
            }
            // Cover every grid-cell centre within range, including water beyond a large expanded deck.
            // This bounded fallback cannot miss a free platform cell because of the random directions.
            int reach=(config.WaterballRange+PlatformRules.TileSize-1)/PlatformRules.TileSize;
            int cx=PlatformRules.CellAt(p.X),cz=PlatformRules.CellAt(p.Z),width=reach*2+1,count=width*width;
            int start=(int)(random%(uint)count);
            for(int j=0;j<count;j++)
            {
                int index=(start+j)%count,x=(cx+index%width-reach)*PlatformRules.TileSize,z=(cz+index/width-reach)*PlatformRules.TileSize;
                if(IslandMap.DistanceSquared(x,z,monster.X,monster.Z)<1000000 || !Valid(x,z,p,config.WaterballRange,water))continue;
                Choose(ref monster,x,z,slot);return true;
            }
        }
        return false;
    }
    private static bool Valid(int x,int z,Fisher target,int range,Func<int,int,bool>? water)=>
        Math.Abs((long)x)<=49000 && Math.Abs((long)z)<=49000 && IslandMap.DistanceSquared(x,z,target.X,target.Z)<=(long)range*range &&
        (water==null || water(x,z));
    private static void Choose(ref WaterMonster m,int x,int z,int target)
    {m.X=x;m.Z=z;m.Target=target;}
}
