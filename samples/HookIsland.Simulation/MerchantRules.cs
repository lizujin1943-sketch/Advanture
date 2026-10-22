using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Shared integer geometry for activation, purchases, movement and presentation.</summary>
public static class MerchantRules
{
    public const int InteractionRange=3000,BodyHalfSize=800;
    public static bool Exists(FishingConfig config,int id)=>id>=0 && id<=config.Merchants.Definitions.Count;
    public static Point Position(FishingConfig config,int id)
    {
        if(!Exists(config,id))throw new ArgumentOutOfRangeException(nameof(id));
        return id==0?new Point(0,1000):new Point(config.Merchants.Definitions[id-1].X,config.Merchants.Definitions[id-1].Z);
    }
    public static int Level(FishingConfig config,int id)
    {
        if(!Exists(config,id))throw new ArgumentOutOfRangeException(nameof(id));
        return id==0?config.MerchantLevel:config.Merchants.Definitions[id-1].Level;
    }
    public static bool CanInteract(Fisher p)=>!p.Unavailable && !p.Vitals.Downed && !p.Swimming.InWater &&
        !p.Knockback.Active && p.PulledBy<0 && p.HookedPlayer<0;
    public static bool Near(IslandMap map,IReadOnlyList<PlatformTile> tiles,Fisher player,int id)
    {
        return map.Fishing!=null && Near(map.Fishing,tiles,player,id);
    }
    public static bool Near(FishingConfig config,IReadOnlyList<PlatformTile> tiles,Fisher player,int id)
    {
        if(!Exists(config,id))return false;
        if(tiles.Count==0)return id==0 && HookUpgrades.IsNearShop(player.X,player.Z);
        var center=Position(config,id);
        return !player.Swimming.InWater && PlatformRules.CanStand(tiles,player.X,player.Z) &&
            IslandMap.DistanceSquared(player.X,player.Z,center.X,center.Z)<=(long)InteractionRange*InteractionRange;
    }
    public static int Nearest(IslandMap map,IReadOnlyList<PlatformTile> tiles,Fisher player)
    {
        int selected=-1;long closest=long.MaxValue;
        if(map.Fishing==null)return selected;
        for(int id=0;id<=map.Fishing.Merchants.Definitions.Count;id++)
        {
            if(!Near(map,tiles,player,id))continue;
            var center=Position(map.Fishing,id);long distance=IslandMap.DistanceSquared(player.X,player.Z,center.X,center.Z);
            if(distance>=closest)continue;
            selected=id;closest=distance;
        }
        return selected;
    }
    public static bool Obstacle(FishingConfig config,int x,int z)=>config.Merchants.Definitions.Any(d=>
        Math.Abs((long)x-d.X)<BodyHalfSize+PlatformRules.Footprint &&
        Math.Abs((long)z-d.Z)<BodyHalfSize+PlatformRules.Footprint);
}
