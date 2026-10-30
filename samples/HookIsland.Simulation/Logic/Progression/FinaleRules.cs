using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Shared integer rules for the dedicated finale target, not ordinary catch ownership.</summary>
public static class FinaleRules
{
    public static Point Direction(int direction)=>direction switch
    {0=>new Point(0,-1),1=>new Point(1,0),2=>new Point(0,1),3=>new Point(-1,0),_=>throw new ArgumentOutOfRangeException(nameof(direction))};
    public static int RequiredHooks(FinaleConfig config,IReadOnlyList<Fisher> players)
    {
        int count=players.Count(p=>!p.Unavailable && !p.Revival.Dead);
        return count==0?0:config.RequiredHooks[count-1];
    }
    public static int Count(IReadOnlyList<Fisher> players)=>players.Count(p=>p.HookedFinale);
    public static bool CanHold(Fisher p)=>!p.Unavailable && !p.Vitals.Downed && !p.Revival.Dead &&
        !p.Swimming.InWater && !p.Knockback.Active && p.PulledBy<0 && !p.Shopping && p.Stamina>0;
    public static void ClearLine(ref Fisher p)
    {
        if(p.HookedFinale)p.Phase=HookPhase.Returning;
        p.HookedFinale=false;p.FinaleDrainRemainder=0;
    }
    public static Point Anchor(FinaleConfig config,FinaleState state)
    {
        var direction=Direction(state.Direction);int distance=config.EdgeOffset-700;
        return new Point(state.X-direction.X*distance,state.Z-direction.Z*distance);
    }
    public static bool BlocksBuild(FinaleConfig config,FinaleState state,int cellX,int cellZ)=>
        config.Enabled && state.Phase==FinalePhase.Surfaced &&
        BodyOverlapsCell(config,new Point(state.X,state.Z),cellX,cellZ);
    // Tangent contact is legal. Generation, construction and restore must use this same boundary.
    internal static bool BodyOverlapsCell(FinaleConfig config,Point position,int cellX,int cellZ)=>
        Math.Abs((long)cellX*PlatformRules.TileSize-position.X)<PlatformRules.HalfTile+config.BodyRadius &&
        Math.Abs((long)cellZ*PlatformRules.TileSize-position.Z)<PlatformRules.HalfTile+config.BodyRadius;
}
