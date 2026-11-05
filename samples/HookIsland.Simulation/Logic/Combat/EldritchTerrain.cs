using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>Derived navigation and visibility, never serialized or shared across simulation models.</summary>
internal sealed class EldritchTerrain
{
    private readonly IslandMap map;
    private readonly CrabNavigation ground;
    private IReadOnlyList<PlatformTile> tiles=Array.Empty<PlatformTile>();
    private static readonly Point[] Directions={new Point(1000,0),new Point(707,707),new Point(0,1000),new Point(-707,707),new Point(-1000,0),new Point(-707,-707),new Point(0,-1000),new Point(707,-707)};
    internal EldritchTerrain(IslandMap map){this.map=map;ground=new CrabNavigation(map);}
    internal void Refresh(IReadOnlyList<PlatformTile> value){tiles=value;ground.Refresh(value);}
    internal bool CanStand(EldritchHabitat habitat,int x,int z)
    {
        if(Math.Abs((long)x)>(map.IsTown?79000:49000) || Math.Abs((long)z)>(map.IsTown?63000:49000) || GrenadeRules.Solid(map,x,600,z,EldritchCatalog.BodyRadius))return false;
        if(habitat==EldritchHabitat.Ground)return map.Platform==null?map.CanStand(x,z):ground.CanStand(x,z);
        if(habitat==EldritchHabitat.Hover)return true;
        for(int dz=-400;dz<=400;dz+=400)for(int dx=-400;dx<=400;dx+=400)
            if(SwimmingTerrain.Ground(map,tiles,x+dx,z+dz))return false;
        return true;
    }
    internal bool Segment(EldritchHabitat habitat,int ax,int az,int bx,int bz)
    {
        int n=Math.Max(1,(HookGame.Sqrt(IslandMap.DistanceSquared(ax,az,bx,bz))+49)/50);
        for(int i=0;i<=n;i++)if(!CanStand(habitat,ax+(int)((long)(bx-ax)*i/n),az+(int)((long)(bz-az)*i/n)))return false;
        return true;
    }
    internal bool Visible(int ax,int az,int bx,int bz)=>GrenadeRules.Visible(map,ax,700,az,bx,700,bz);
    internal int Target(EldritchCreature c,IReadOnlyList<Fisher> players,out Point goal)
    {
        if(EldritchCatalog.Get(c.Kind).Habitat==EldritchHabitat.Ground && map.Platform!=null)
            return ground.Target(players,c.X,c.Z,out goal);
        goal=new Point(c.X,c.Z);int target=-1;long best=(long)EldritchCatalog.AggroRange*EldritchCatalog.AggroRange+1;
        foreach(var p in players)
        {
            if(!EldritchSystem.Eligible(p) || EldritchCatalog.Get(c.Kind).Habitat==EldritchHabitat.Ground && p.Swimming.InWater)continue;
            long d=IslandMap.DistanceSquared(c.X,c.Z,p.X,p.Z);if(d>=best)continue;
            best=d;target=p.Slot;goal=new Point(p.X,p.Z);
        }
        return target;
    }
    internal bool SpawnTown(EldritchCreature c,IReadOnlyList<Fisher> players,IReadOnlyList<EldritchCreature> creatures,out Point point)
    {
        point=default;int zone=TownEncounters.Zone(c.Id);var habitat=EldritchCatalog.Get(c.Kind).Habitat;
        foreach(var port in TownLayout.SpawnPorts)
        {
            if(port.ZoneId!=zone || port.Water!=(habitat==EldritchHabitat.Water) || !CanStand(habitat,port.X,port.Z))continue;
            bool clear=true;
            foreach(var p in players)if(!p.WaitingToJoin && !p.Unavailable &&
                (IslandMap.DistanceSquared(port.X,port.Z,p.X,p.Z)<8000L*8000 || Visible(p.X,p.Z,port.X,port.Z))){clear=false;break;}
            foreach(var other in creatures)if(other.Damageable && !other.Paused && IslandMap.DistanceSquared(port.X,port.Z,other.X,other.Z)<1000L*1000){clear=false;break;}
            if(clear){point=new Point(port.X,port.Z);return true;}
        }
        return false;
    }
    internal bool Spawn(EldritchCreature c,IReadOnlyList<Fisher> players,IReadOnlyList<EldritchCreature> creatures,out Point point)
    {
        point=default;var habitat=EldritchCatalog.Get(c.Kind).Habitat;
        for(int i=0;i<48;i++)
        {
            Point candidate;
            // Exploration encounters begin on reachable island terrain. Hovering changes
            // traversal after arrival, not the island-first placement of ranged enemies.
            if(habitat!=EldritchHabitat.Water && map.Platform!=null)
            {if(!ground.TrySpawn(players,unchecked(c.Generation+(uint)i),out candidate,out _,c.Id,EldritchCatalog.Population))continue;}
            else
            {
                var player=players[(c.Id+i/8)%players.Count];if(!EldritchSystem.Eligible(player))continue;
                var direction=Directions[(i+c.Id+(int)(c.Generation%8))%8];int radius=4500+(i/8)*600;
                candidate=new Point(player.X+direction.X*radius/1000,player.Z+direction.Z*radius/1000);
            }
            if(!CanStand(habitat,candidate.X,candidate.Z))continue;
            bool clear=true;
            foreach(var p in players)if(!p.WaitingToJoin && IslandMap.DistanceSquared(candidate.X,candidate.Z,p.X,p.Z)<1600L*1600){clear=false;break;}
            foreach(var other in creatures)if(other.Damageable && IslandMap.DistanceSquared(candidate.X,candidate.Z,other.X,other.Z)<1000L*1000){clear=false;break;}
            if(clear){point=candidate;return true;}
        }
        return false;
    }
}
