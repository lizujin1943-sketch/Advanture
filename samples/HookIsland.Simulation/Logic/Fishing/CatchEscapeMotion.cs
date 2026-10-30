using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>Saved, deterministic struggle / flight / submersion. Tick zero means inactive.</summary>
public struct CatchEscapeState
{
    public int Tick,OriginX,OriginY,OriginZ,LandingX,LandingZ;
    public bool Active=>Tick>0;
}

public static class CatchEscapeMotion
{
    public const int StruggleTicks=9,FlightTicks=21,SubmergeTicks=12;
    public const int SplashTick=StruggleTicks+FlightTicks+1,TotalTicks=SplashTick+SubmergeTicks;
    private static readonly Point[] Directions={new Point(1000,0),new Point(707,707),new Point(0,1000),new Point(-707,707),
        new Point(-1000,0),new Point(-707,-707),new Point(0,-1000),new Point(707,-707)};

    public static CatchEscapeState Begin(int x,int y,int z,Point landing)=>new CatchEscapeState {
        Tick=1,OriginX=x,OriginY=y,OriginZ=z,LandingX=landing.X,LandingZ=landing.Z
    };
    public static bool Advance(ref CatchEscapeState state)
    {
        if(!state.Active)return false;
        if(state.Tick>=TotalTicks){state=default;return false;}
        state.Tick++;return true;
    }
    public static void Position(CatchEscapeState state,out int x,out int y,out int z)
    {
        int age=state.Tick-1,flight=Math.Max(0,Math.Min(FlightTicks,age-StruggleTicks));
        x=state.OriginX+(int)((long)(state.LandingX-state.OriginX)*flight/FlightTicks);
        z=state.OriginZ+(int)((long)(state.LandingZ-state.OriginZ)*flight/FlightTicks);
        y=state.OriginY*(FlightTicks-flight)/FlightTicks+4*1100*flight*(FlightTicks-flight)/(FlightTicks*FlightTicks);
        if(age>StruggleTicks+FlightTicks)y=-700*(age-StruggleTicks-FlightTicks)/SubmergeTicks;
    }
    public static bool Valid(CatchEscapeState state)
    {
        if(!state.Active)return state.Equals(default(CatchEscapeState));
        return state.Tick<=TotalTicks && Math.Abs((long)state.OriginX)<=100000 && Math.Abs((long)state.OriginZ)<=100000 &&
            state.OriginY>=0 && state.OriginY<=2500 && Math.Abs((long)state.LandingX)<=49500 && Math.Abs((long)state.LandingZ)<=49500;
    }
    public static bool OpenWater(IslandMap map,IReadOnlyList<PlatformTile> tiles,int x,int z)
    {
        if(Math.Abs((long)x)>49500 || Math.Abs((long)z)>49500)return false;
        if(map.Platform!=null)
        {
            foreach(var tile in tiles)
                if(Math.Abs((long)x-tile.X*PlatformRules.TileSize)<PlatformRules.HalfTile+450 &&
                    Math.Abs((long)z-tile.Z*PlatformRules.TileSize)<PlatformRules.HalfTile+450)return false;
            return true;
        }
        for(int dz=-450;dz<=450;dz+=450)for(int dx=-450;dx<=450;dx+=450)
            if(map.OnLand(x+dx,z+dz) || SwimmingTerrain.Obstacle(map,x+dx,z+dz))return false;
        return true;
    }
    public static Point Landing(IslandMap map,IReadOnlyList<PlatformTile> tiles,Point origin,Point owner)
    {
        int dx=origin.X-owner.X,dz=origin.Z-owner.Z,length=HookGame.Sqrt((long)dx*dx+(long)dz*dz);
        if(length==0){dx=0;dz=-1000;length=1000;}
        var preferred=new Point(Clamp(origin.X+dx*2000/length),Clamp(origin.Z+dz*2000/length));
        if(OpenWater(map,tiles,preferred.X,preferred.Z))return preferred;
        // Fixed order handles expanded decks and coastlines identically on all peers.
        for(int radius=500;radius<=150000;radius+=500)
            foreach(var direction in Directions)
            {
                var candidate=new Point(Clamp(origin.X+direction.X*radius/1000),Clamp(origin.Z+direction.Z*radius/1000));
                if(OpenWater(map,tiles,candidate.X,candidate.Z))return candidate;
            }
        throw new InvalidOperationException("Island has no open water for an escaping fish.");
    }
    private static int Clamp(int value)=>Math.Max(-49500,Math.Min(49500,value));
}
