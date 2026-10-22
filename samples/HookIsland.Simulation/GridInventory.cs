using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>Pure rectangle placement; independent of equipment, currency, players and rendering.</summary>
public readonly struct GridRect
{
    public GridRect(int x,int y,int width,int height){X=x;Y=y;Width=width;Height=height;}
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public bool Overlaps(GridRect other)=>(long)X< (long)other.X+other.Width && (long)X+Width>other.X && (long)Y<(long)other.Y+other.Height && (long)Y+Height>other.Y;
    public bool Fits(int width,int height)=>X>=0 && Y>=0 && Width>0 && Height>0 && (long)X+Width<=width && (long)Y+Height<=height;
}
public static class GridInventory
{
    public static bool TryPlace(int width,int height,int itemWidth,int itemHeight,IReadOnlyList<GridRect> occupied,bool rotate,out GridRect result,out bool rotated)
    {
        if(width<1 || height<1 || itemWidth<1 || itemHeight<1)throw new ArgumentOutOfRangeException(nameof(width));
        for(int turn=0;turn<(rotate?2:1);turn++)
        {
            int w=turn==0?itemWidth:itemHeight,h=turn==0?itemHeight:itemWidth;
            for(int y=0;y<=height-h;y++)for(int x=0;x<=width-w;x++)
            {
                var rect=new GridRect(x,y,w,h);bool free=true;
                foreach(var other in occupied)if(rect.Overlaps(other)){free=false;break;}
                if(free){result=rect;rotated=turn==1;return true;}
            }
        }
        result=default;rotated=false;return false;
    }
}
