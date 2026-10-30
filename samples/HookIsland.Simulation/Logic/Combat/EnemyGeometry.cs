using System;

namespace Advanture.HookIsland.Simulation;

public static class EnemyGeometry
{
    public const int DirectionScale=1000000,MonsterRadius=900,PlayerRadius=330;
    public static void Direction(int dx,int dz,out int x,out int z)
    {
        if(Math.Abs((long)dx)>DirectionScale || Math.Abs((long)dz)>DirectionScale)throw new ArgumentOutOfRangeException(nameof(dx));
        int precision=Math.Min(DirectionScale,1000000000/Math.Max(1,Math.Max(Math.Abs(dx),Math.Abs(dz))));
        int length=HookGame.Sqrt(((long)dx*dx+(long)dz*dz)*precision*precision);
        x=length==0?0:(int)((long)dx*DirectionScale*precision/length);z=length==0?DirectionScale:(int)((long)dz*DirectionScale*precision/length);
    }
    // Exact entry order for bounded (<= 1.2 m) frame segments. All products fit signed 64-bit.
    public static int HitFraction(int ax,int az,int bx,int bz,int x,int z,int radius)
    {
        long dx=bx-ax,dz=bz-az,ox=x-ax,oz=z-az,len=dx*dx+dz*dz;
        long c=ox*ox+oz*oz-(long)radius*radius;if(c<=0)return 0;
        long dot=ox*dx+oz*dz;if(len==0 || dot<=0)return int.MaxValue;
        long discriminant=dot*dot-len*c;if(discriminant<0)return int.MaxValue;
        long entry=dot-HookGame.Sqrt(discriminant);
        return entry<=len?(int)(entry*DirectionScale/len):int.MaxValue;
    }
}
