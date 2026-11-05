using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Bounded half-metre graph derived from confirmed tiles. Stable node/edge order, no engine navigation.</summary>
public sealed class CrabNavigation
{
    private readonly IslandMap map;
    // Rollback creates new navigation instances. These bounded immutable graphs are derived
    // solely from town geometry and confirmed tile coordinates, never from actors or timers.
    private sealed class TownGraph
    {
        internal TownGraph(Point[] nodes,int[][] edges,(Point land,Point water)[] shores)
        {Nodes=nodes;Edges=edges;Shores=shores;}
        internal readonly Point[] Nodes;internal readonly int[][] Edges;
        internal readonly (Point land,Point water)[] Shores;
    }
    private static readonly object TownGraphLock=new object();
    private static readonly Dictionary<string,TownGraph> TownGraphs=new Dictionary<string,TownGraph>();
    private static readonly Queue<string> TownGraphOrder=new Queue<string>();
    private readonly HashSet<(int,int)> cells=new HashSet<(int,int)>();
    private readonly Dictionary<(int,int),int> indices=new Dictionary<(int,int),int>();
    private Point[] nodes=Array.Empty<Point>();
    private int[][] edges=Array.Empty<int[]>();
    private readonly List<(Point land,Point water)> shores=new List<(Point,Point)>();
    private Point[] terrain=Array.Empty<Point>();
    private bool graphReady;
    private IReadOnlyList<PlatformTile> confirmedTiles=Array.Empty<PlatformTile>();
    private bool refreshed;
    private int Spacing=>map.IsTown?1000:500;
    private static readonly Point[] Directions={new Point(0,-1),new Point(1,0),new Point(0,1),new Point(-1,0)};
    public CrabNavigation(IslandMap map){this.map=map;}
    public void Refresh(IReadOnlyList<PlatformTile> tiles)
    {
        // A rollback or a destroyed/rebuilt tile can change coordinates without changing the count.
        bool unchanged=refreshed && terrain.Length==tiles.Count;
        for(int i=0;unchanged && i<tiles.Count;i++)
            unchanged=terrain[i].X==tiles[i].X && terrain[i].Z==tiles[i].Z;
        if(unchanged)return;
        refreshed=true;confirmedTiles=tiles.ToArray();terrain=new Point[tiles.Count];cells.Clear();graphReady=false;
        for(int i=0;i<tiles.Count;i++)
        {var t=tiles[i];terrain[i]=new Point(t.X,t.Z);cells.Add((t.X,t.Z));}
    }
    private void EnsureGraph()
    {
        if(graphReady)return;
        string? townKey=map.IsTown?string.Join(";",terrain.OrderBy(p=>p.Z).ThenBy(p=>p.X).Select(p=>p.X+","+p.Z)):null;
        if(townKey!=null)
        {
            lock(TownGraphLock)if(TownGraphs.TryGetValue(townKey,out var cached))
            {
                nodes=cached.Nodes;edges=cached.Edges;indices.Clear();shores.Clear();
                for(int i=0;i<nodes.Length;i++)indices.Add((nodes[i].X,nodes[i].Z),i);
                shores.AddRange(cached.Shores);graphReady=true;return;
            }
        }
        indices.Clear();shores.Clear();
        var points=new List<Point>();
        if(map.IsTown)
        {
            for(int z=-64000;z<=64000;z+=Spacing)for(int x=-80000;x<=80000;x+=Spacing)
                if(CanStand(x,z))points.Add(new Point(x,z));
        }
        else foreach(var t in terrain.OrderBy(t=>t.Z).ThenBy(t=>t.X))
            for(int z=-500;z<=1000;z+=500)for(int x=-500;x<=1000;x+=500)
            {var p=new Point(t.X*2000+x,t.Z*2000+z);if(CanStand(p.X,p.Z))points.Add(p);}
        nodes=points.ToArray();
        for(int i=0;i<nodes.Length;i++)indices.Add((nodes[i].X,nodes[i].Z),i);
        edges=new int[nodes.Length][];
        for(int i=0;i<nodes.Length;i++)
        {
            var p=nodes[i];var adjacent=new List<int>();
            foreach(var d in Directions)
                if(indices.TryGetValue((p.X+d.X*Spacing,p.Z+d.Z*Spacing),out int n) && Segment(p.X,p.Z,nodes[n].X,nodes[n].Z))adjacent.Add(n);
            edges[i]=adjacent.ToArray();
        }
        if(map.IsTown)
        {
            foreach(var p in nodes)foreach(var d in Directions)
            {
                var water=new Point(p.X+d.X*1000,p.Z+d.Z*1000);
                if(!TownLayout.OnGround(confirmedTiles,water.X,water.Z) && !SwimmingTerrain.Obstacle(map,water.X,water.Z))shores.Add((p,water));
            }
        }
        else foreach(var t in terrain.OrderBy(t=>t.Z).ThenBy(t=>t.X))foreach(var d in Directions)
        {
            if(cells.Contains((t.X+d.X,t.Z+d.Z)))continue;
            var land=new Point(t.X*2000+d.X*500,t.Z*2000+d.Z*500);
            var water=new Point(t.X*2000+d.X*1500,t.Z*2000+d.Z*1500);
            if(CanStand(land.X,land.Z) && !SwimmingTerrain.Obstacle(map,water.X,water.Z))shores.Add((land,water));
        }
        graphReady=true;
        if(townKey!=null)lock(TownGraphLock)
        {
            if(!TownGraphs.ContainsKey(townKey))
            {
                while(TownGraphOrder.Count>=8)TownGraphs.Remove(TownGraphOrder.Dequeue());
                TownGraphs.Add(townKey,new TownGraph(nodes,edges,shores.ToArray()));TownGraphOrder.Enqueue(townKey);
            }
        }
    }
    public bool CanStand(int x,int z)
    {
        if(map.IsTown)return TownLayout.CanStand(confirmedTiles,x,z,CrabConfig.BodyRadius);
        if(Math.Abs((long)x)>49000 || Math.Abs((long)z)>49000)return false;
        if(!CoversFootprint(x,z,CrabConfig.BodyRadius))return false;
        if(map.Fishing!=null)foreach(var merchant in map.Fishing.Merchants.Definitions)
            if(Math.Abs((long)x-merchant.X)<MerchantRules.BodyHalfSize+CrabConfig.BodyRadius && Math.Abs((long)z-merchant.Z)<MerchantRules.BodyHalfSize+CrabConfig.BodyRadius)return false;
        return true;
    }
    /// <summary>共享已确认地格的哈希，不为每次单位分离重新线性扫描整座平台。</summary>
    internal bool CoversFootprint(int x,int z,int radius)
    {
        if(map.IsTown)return TownLayout.CanStand(confirmedTiles,x,z,radius);
        for(int cz=PlatformRules.CellAt(z-radius);cz<=PlatformRules.CellAt(z+radius);cz++)
            for(int cx=PlatformRules.CellAt(x-radius);cx<=PlatformRules.CellAt(x+radius);cx++)
                if(!cells.Contains((cx,cz)))return false;
        return true;
    }
    public bool Segment(int x,int z,int toX,int toZ)
    {
        int length=HookGame.Sqrt(IslandMap.DistanceSquared(x,z,toX,toZ)),steps=Math.Max(1,(length+99)/100);
        for(int i=0;i<=steps;i++)if(!CanStand(x+(int)((long)(toX-x)*i/steps),z+(int)((long)(toZ-z)*i/steps)))return false;
        return true;
    }
    public bool AttackLine(int x,int z,int toX,int toZ)
    {
        int steps=Math.Max(1,(HookGame.Sqrt(IslandMap.DistanceSquared(x,z,toX,toZ))+99)/100);
        for(int i=0;i<=steps;i++)
        {
            int px=x+(int)((long)(toX-x)*i/steps),pz=z+(int)((long)(toZ-z)*i/steps);
            if((map.IsTown?!TownLayout.OnGround(confirmedTiles,px,pz):!cells.Contains((PlatformRules.CellAt(px),PlatformRules.CellAt(pz)))) || SwimmingTerrain.Obstacle(map,px,pz))return false;
        }
        return true;
    }
    public bool TrySpawn(IReadOnlyList<Fisher> players,uint generation,out Point land,out Point water,int slot=0,int count=1)
    {
        EnsureGraph();
        land=water=default;
        if(shores.Count==0)return false;
        int start=(int)((generation+(ulong)slot*(uint)shores.Count/(uint)count)%(uint)shores.Count);
        for(int i=0;i<shores.Count;i++)
        {
            var candidate=shores[(start+i)%shores.Count];
            if(players.Any(p=>!p.WaitingToJoin && IslandMap.DistanceSquared(p.X,p.Z,candidate.land.X,candidate.land.Z)<900000))continue;
            var reached=Search(candidate.land.X,candidate.land.Z);
            if(!players.Any(p=>CrabSystem.Eligible(p) && IslandMap.DistanceSquared(p.X,p.Z,candidate.land.X,candidate.land.Z)<=(long)CrabConfig.AggroRange*CrabConfig.AggroRange && Endpoint(p.X,p.Z,reached)>=0))continue;
            land=candidate.land;water=candidate.water;return true;
        }
        return false;
    }
    public int Target(IReadOnlyList<Fisher> players,int x,int z,out Point goal)
    {
        EnsureGraph();
        goal=new Point(x,z);int target=-1;long best=long.MaxValue;var route=Search(x,z);
        foreach(var p in players)
        {
            long distance=IslandMap.DistanceSquared(x,z,p.X,p.Z);
            if(!CrabSystem.Eligible(p) || distance>=best || distance>(long)CrabConfig.AggroRange*CrabConfig.AggroRange)continue;
            int end=Endpoint(p.X,p.Z,route);if(end<0)continue;
            target=p.Slot;best=distance;goal=Segment(x,z,p.X,p.Z)?new Point(p.X,p.Z):Next(route,end,x,z);
        }
        return target;
    }
    public bool Retreat(int x,int z,out Point goal,out Point water)
    {
        EnsureGraph();
        goal=water=default;var route=Search(x,z);long best=long.MaxValue;int selected=-1;
        foreach(var shore in shores)
        {
            int end=Endpoint(shore.land.X,shore.land.Z,route);long distance=IslandMap.DistanceSquared(x,z,shore.land.X,shore.land.Z);
            if(end<0 || distance>=best)continue;
            best=distance;selected=end;water=shore.water;goal=Segment(x,z,shore.land.X,shore.land.Z)?shore.land:Next(route,end,x,z);
        }
        return selected>=0;
    }
    private int[] Search(int x,int z)
    {
        var previous=Enumerable.Repeat(-2,nodes.Length).ToArray();int start=-1;long best=1000001;
        for(int i=0;i<nodes.Length;i++)
        {var p=nodes[i];long d=IslandMap.DistanceSquared(x,z,p.X,p.Z);if(d<best && Segment(x,z,p.X,p.Z)){best=d;start=i;}}
        if(start<0)return previous;
        var queue=new Queue<int>();queue.Enqueue(start);previous[start]=-1;
        while(queue.Count>0){int i=queue.Dequeue();foreach(int n in edges[i])if(previous[n]==-2){previous[n]=i;queue.Enqueue(n);}}
        return previous;
    }
    private int Endpoint(int x,int z,int[] route)
    {
        int selected=-1;long best=1000001;
        for(int i=0;i<nodes.Length;i++)
        {var p=nodes[i];long d=IslandMap.DistanceSquared(x,z,p.X,p.Z);if(route[i]!=-2 && d<best && AttackLine(p.X,p.Z,x,z)){best=d;selected=i;}}
        return selected;
    }
    private Point Next(int[] route,int end,int x,int z)
    {
        while(route[end]>=0 && route[route[end]]>=0)end=route[end];
        return Segment(x,z,nodes[end].X,nodes[end].Z) || route[end]<0?nodes[end]:nodes[route[end]];
    }
}
