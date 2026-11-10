using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>
/// 固定地面的空间索引。按 8 米分区筛选可能覆盖该点的地面，最后仍用原来的整数公式精确判断。
/// 索引只包含不变的街道、建筑基座和战斗区；可破坏木板及解锁桥梁继续读取当前地格。
/// </summary>
internal sealed class TownGroundIndex
{
    private const int Size=8000,Columns=TownLayout.Width/Size,Rows=TownLayout.Height/Size;
    private readonly Region[] regions=new Region[Columns*Rows];

    private readonly struct Rectangle
    {
        internal Rectangle(int x,int z,int halfWidth,int halfDepth)
        {X=x;Z=z;HalfWidth=halfWidth;HalfDepth=halfDepth;}
        internal readonly int X,Z,HalfWidth,HalfDepth;
        internal bool Contains(int x,int z)=>Math.Abs((long)x-X)<=HalfWidth && Math.Abs((long)z-Z)<=HalfDepth;
    }
    private sealed class Region
    {
        internal readonly Rectangle[] Rectangles;
        internal readonly TownRoad[] Roads;
        internal Region(Rectangle[] rectangles,TownRoad[] roads){Rectangles=rectangles;Roads=roads;}
    }

    internal TownGroundIndex()
    {
        var rectangles=new List<Rectangle>();
        foreach(var a in TownLayout.EncounterAreas)rectangles.Add(new Rectangle(a.X,a.Z,a.Width/2,a.Depth/2));
        foreach(var b in TownLayout.Buildings)rectangles.Add(new Rectangle(b.X,b.Z,b.Width/2+1500,b.Depth/2+1500));
        for(int row=0;row<Rows;row++)for(int column=0;column<Columns;column++)
        {
            int left=column*Size-TownLayout.Width/2,bottom=row*Size-TownLayout.Height/2;
            var nearbyRectangles=new List<Rectangle>();var nearbyRoads=new List<TownRoad>();
            foreach(var r in rectangles)
                if(Overlaps(left,bottom,r.X-r.HalfWidth,r.Z-r.HalfDepth,r.X+r.HalfWidth,r.Z+r.HalfDepth))nearbyRectangles.Add(r);
            foreach(var road in TownLayout.Roads)
            {
                // 最近点的整数截断最多偏移不足 1 毫米；外扩 1 毫米使粗筛始终保守。
                int margin=road.HalfWidth+1;
                if(Overlaps(left,bottom,Math.Min(road.Start.X,road.End.X)-margin,Math.Min(road.Start.Z,road.End.Z)-margin,
                    Math.Max(road.Start.X,road.End.X)+margin,Math.Max(road.Start.Z,road.End.Z)+margin))nearbyRoads.Add(road);
            }
            regions[row*Columns+column]=new Region(nearbyRectangles.ToArray(),nearbyRoads.ToArray());
        }
    }

    private static bool Overlaps(int left,int bottom,int minX,int minZ,int maxX,int maxZ)
        =>maxX>=left && minX<=left+Size && maxZ>=bottom && minZ<=bottom+Size;

    internal bool Contains(int x,int z)
    {
        if(!TownLayout.InBounds(x,z))return false;
        // 地图最右/最上边界仍属于最后一个分区，不能索引到数组外。
        int column=Math.Min(Columns-1,(x+TownLayout.Width/2)/Size),row=Math.Min(Rows-1,(z+TownLayout.Height/2)/Size);
        var region=regions[row*Columns+column];
        foreach(var r in region.Rectangles)if(r.Contains(x,z))return true;
        foreach(var road in region.Roads)
            if(TownLayout.DistanceToSegmentSquared(x,z,road.Start,road.End)<=(long)road.HalfWidth*road.HalfWidth)return true;
        return false;
    }
}
