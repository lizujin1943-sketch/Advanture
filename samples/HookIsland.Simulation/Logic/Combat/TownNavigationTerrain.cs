using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>
/// 一次小镇建图期间的精确查询缓存。相邻节点及双向连边会反复检查同一块地面，
/// 这里只复用同一坐标的结果，不降低原来 100 毫米的采样精度。
/// 建图结束后释放，不能跨越木板破坏、捷径解锁或存档恢复使用。
/// </summary>
internal sealed class TownNavigationTerrain
{
    private const int Step=100;
    private const int Columns=TownLayout.Width/Step+1,Rows=TownLayout.Height/Step+1;
    // 0=尚未查询，1=不可通行，2=可通行；两张数组合计约 4 MiB，有明确大小上限。
    private readonly byte[] ground=new byte[Columns*Rows],standing=new byte[Columns*Rows];
    private readonly IReadOnlyList<PlatformTile> tiles;
    private readonly Func<int,int,bool> queryGround;

    internal TownNavigationTerrain(IReadOnlyList<PlatformTile> tiles)
    {this.tiles=tiles;queryGround=OnGround;}

    internal bool CanStand(int x,int z)
    {
        int index=Index(x,z);
        // 非网格坐标直接使用权威公式，不能取整后借用邻近坐标的结果。
        if(index<0)return TownLayout.CanStand(tiles,x,z,CrabConfig.BodyRadius);
        if(standing[index]==0)
            standing[index]=(byte)(TownLayout.CanStand(tiles,x,z,CrabConfig.BodyRadius,queryGround)?2:1);
        return standing[index]==2;
    }

    private bool OnGround(int x,int z)
    {
        int index=Index(x,z);
        if(index<0)return TownLayout.OnGround(tiles,x,z);
        if(ground[index]==0)ground[index]=(byte)(TownLayout.OnGround(tiles,x,z)?2:1);
        return ground[index]==2;
    }

    private static int Index(int x,int z)
    {
        if(!TownLayout.InBounds(x,z) || x%Step!=0 || z%Step!=0)return -1;
        return (z+TownLayout.Height/2)/Step*Columns+(x+TownLayout.Width/2)/Step;
    }
}
