using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public enum TownBuildingKind : byte { Dock, Armory, Clinic, SwimShop, Workshop, Commission, Pawnshop, Cartography, Engine, Outpost, Archive, Chapel }

/// <summary>Immutable building footprint and south-facing entrance, in authoritative integer millimetres.</summary>
public sealed class TownBuilding
{
    internal TownBuilding(int id,string name,TownBuildingKind kind,int x,int y,int width,int depth,bool interior=false)
    {
        Id=id;Name=name;Kind=kind;var p=TownLayout.FromDesign(x,y);X=p.X;Z=p.Z;
        Width=width*1000;Depth=depth*1000;Enterable=interior;Door=new Point(X,Z-Depth/2);
        Entrance=new Point(X,Z-Depth/2-1000);
    }
    public int Id {get;} public string Name {get;} public TownBuildingKind Kind {get;}
    public int X {get;} public int Z {get;} public int Width {get;} public int Depth {get;}
    public bool Enterable {get;} public Point Door {get;} public Point Entrance {get;}
}

public readonly struct TownRoad
{
    internal TownRoad(Point start,Point end,int halfWidth){Start=start;End=end;HalfWidth=halfWidth;}
    public Point Start {get;} public Point End {get;} public int HalfWidth {get;}
}
public readonly struct TownObstacle
{
    internal TownObstacle(int x,int z,int width,int depth,int height)
    {X=x;Z=z;Width=width;Depth=depth;Height=height;}
    public int X {get;} public int Z {get;} public int Width {get;} public int Depth {get;} public int Height {get;}
    public bool Contains(int x,int z,int margin=0)=>Math.Abs((long)x-X)<=Width/2+margin && Math.Abs((long)z-Z)<=Depth/2+margin;
}
public sealed class TownEncounterArea
{
    internal TownEncounterArea(int id,int x,int y,int width,int depth){Id=id;var p=TownLayout.FromDesign(x,y);X=p.X;Z=p.Z;Width=width*1000;Depth=depth*1000;}
    public int Id {get;} public string Name=>((char)('A'+Id)).ToString();
    public int X {get;} public int Z {get;} public int Width {get;} public int Depth {get;}
    public bool Contains(int x,int z,int margin=0)=>Math.Abs((long)x-X)<=Width/2+margin && Math.Abs((long)z-Z)<=Depth/2+margin;
}
public readonly struct TownSpawnPort
{
    internal TownSpawnPort(int zone,int index,int x,int y,bool water=false){ZoneId=zone;Index=index;var p=TownLayout.FromDesign(x,y);X=p.X;Z=p.Z;Water=water;}
    public int ZoneId {get;} public int Index {get;} public int X {get;} public int Z {get;} public bool Water {get;}
}
public readonly struct TownWoodSegment
{
    internal TownWoodSegment(int band,int segment,Point direction)
    {
        BandId=band;SegmentId=segment;Direction=direction;InnerRadius=12000+segment*2000;
        int radius=InnerRadius+1000;X=TownLayout.CenterX+direction.X*radius/1000;Z=TownLayout.CenterZ+direction.Z*radius/1000;
        CellX=PlatformRules.CellAt(X);CellZ=PlatformRules.CellAt(Z);
    }
    public int BandId {get;} public int SegmentId {get;} public int Band=>BandId; public int Segment=>SegmentId; public Point Direction {get;}
    public int InnerRadius {get;} public int X {get;} public int Z {get;} public int CellX {get;} public int CellZ {get;}
}

/// <summary>Fixed town terrain; dynamic state contains only the sixteen explicitly destructible wood segments.
/// No floating point trigonometry, Unity collision or mutable global geometry participates in authority.</summary>
public static class TownLayout
{
    public const int Width=160000,Height=128000,CenterX=-8000,CenterZ=-12000,BeaconX=2000,BeaconZ=5000;
    /// <summary>
    /// 服务门口三米范围不参与城区占用和刷怪压力。以所有端共有的位置判定，
    /// 不依赖客户端是否打开 UI，也不复用旧海岛商人的 Shopping 授权几何。
    /// 这不是伤害免疫：已经释放的攻击仍按原规则结算。
    /// </summary>
    public static bool NearService(int x,int z)
    {
        for(int i=0;i<Buildings.Count;i++)
        {
            var entrance=Buildings[i].Entrance;
            if(IslandMap.DistanceSquared(x,z,entrance.X,entrance.Z)<=3000L*3000)return true;
        }
        return false;
    }
    public static bool InArena(Fisher p)=>IslandMap.DistanceSquared(p.X,p.Z,CenterX,CenterZ)<=26000L*26000;
    public const int InnerRadius=12000,OuterRadius=22000,StoneOuterInnerRadius=20000;
    private static readonly Point[] Compass={new Point(1000,0),new Point(924,383),new Point(707,707),new Point(383,924),new Point(0,1000),new Point(-383,924),new Point(-707,707),new Point(-924,383),new Point(-1000,0),new Point(-924,-383),new Point(-707,-707),new Point(-383,-924),new Point(0,-1000),new Point(383,-924),new Point(707,-707),new Point(924,-383)};
    private static readonly Point[] WoodDirections={new Point(866,-500),new Point(342,-940),new Point(-342,-940),new Point(-866,-500)};
    private static readonly IReadOnlyList<PlatformTile> FullWood;
    private static readonly PlatformTile[][] Shortcuts;
    private static readonly PlatformTile[] ReturnBridge;
    private static readonly HashSet<(int,int)> BridgeCells;
    private static readonly TownGroundIndex FixedGround;
    public static IReadOnlyList<TownBuilding> Buildings {get;}=Array.AsReadOnly(new[]{
        new TownBuilding(1,"归航码头",TownBuildingKind.Dock,25,111,8,6),new TownBuilding(2,"铁鳞军火铺",TownBuildingKind.Armory,20,85,6,6),
        new TownBuilding(3,"盐灯诊所",TownBuildingKind.Clinic,17,64,8,8,true),new TownBuilding(4,"潜潮船具店",TownBuildingKind.SwimShop,31,31,7,6),
        new TownBuilding(5,"震雷工坊",TownBuildingKind.Workshop,60,19,8,6),new TownBuilding(6,"海雾委托所",TownBuildingKind.Commission,87,20,8,6),
        new TownBuilding(7,"海雾当铺",TownBuildingKind.Pawnshop,113,28,7,6),new TownBuilding(8,"潮汐测绘站",TownBuildingKind.Cartography,139,46,7,6),
        new TownBuilding(9,"双闸机房",TownBuildingKind.Engine,141,77,8,8),new TownBuilding(10,"归潮驿站",TownBuildingKind.Outpost,129,104,7,6),
        new TownBuilding(11,"禁书档案馆",TownBuildingKind.Archive,101,114,10,10,true),new TownBuilding(12,"沉钟礼拜堂",TownBuildingKind.Chapel,86,47,10,12,true)});
    public static IReadOnlyList<TownEncounterArea> EncounterAreas {get;}=Array.AsReadOnly(new[]{
        new TownEncounterArea(0,29,92,20,18),new TownEncounterArea(1,29,51,24,20),new TownEncounterArea(2,61,35,26,22),
        new TownEncounterArea(3,111,49,28,22),new TownEncounterArea(4,129,82,24,20),new TownEncounterArea(5,105,98,24,18)});
    public static IReadOnlyList<TownSpawnPort> SpawnPorts {get;}=Array.AsReadOnly(new[]{
        new TownSpawnPort(0,0,30,80),new TownSpawnPort(0,1,40,94),new TownSpawnPort(0,2,18,96),
        new TownSpawnPort(1,0,24,39),new TownSpawnPort(1,1,42,52),new TownSpawnPort(1,2,14,52),
        new TownSpawnPort(2,0,49,25),new TownSpawnPort(2,1,75,33),new TownSpawnPort(2,2,60,48),
        new TownSpawnPort(3,0,99,38),new TownSpawnPort(3,1,126,40),new TownSpawnPort(3,2,127,54),
        new TownSpawnPort(4,0,145,85,true),new TownSpawnPort(4,1,132,68),new TownSpawnPort(4,2,116,82),
        new TownSpawnPort(5,0,91,97),new TownSpawnPort(5,1,109,86),new TownSpawnPort(5,2,118,108)});
    public static IReadOnlyList<TownRoad> Roads {get;}
    public static IReadOnlyList<TownObstacle> Obstacles {get;}
    public static IReadOnlyList<TownWoodSegment> WoodSegments {get;}
    public static IReadOnlyList<Point> ShootingPlatforms {get;}=Array.AsReadOnly(new[]{new Point(-1570,-19660),new Point(-8000,-22000),new Point(-14430,-19660)});
    static TownLayout()
    {
        var roads=new List<TownRoad>();
        void Road(int ax,int ay,int bx,int by,int half=3000)=>roads.Add(new TownRoad(FromDesign(ax,ay),FromDesign(bx,by),half));
        var ring=new[]{(25,111),(29,92),(29,51),(31,37),(45,31),(61,35),(60,24),(87,25),(113,33),(111,49),(139,52),(129,82),(129,109),(118,113),(110,122),(91,122),(25,117),(25,111)};
        for(int i=1;i<ring.Length;i++)Road(ring[i-1].Item1,ring[i-1].Item2,ring[i].Item1,ring[i].Item2);
        // Free archive-to-chapel route follows the authored east-side bridge; no arena shortcut unlock is required.
        var entry=new[]{(101,120),(101,106),(98,103),(98,65),(94,59),(86,55),(82,59)};
        for(int i=1;i<entry.Length;i++)Road(entry[i-1].Item1,entry[i-1].Item2,entry[i].Item1,entry[i].Item2,2750);
        foreach(var b in Buildings)
        {
            var nearest=roads.OrderBy(r=>DistanceToSegmentSquared(b.Entrance.X,b.Entrance.Z,r.Start,r.End)).First();
            var junction=Nearest(b.Entrance.X,b.Entrance.Z,nearest.Start,nearest.End);
            roads.Add(new TownRoad(b.Entrance,junction,2500));
            roads.Add(new TownRoad(b.Entrance,b.Door,2500));
        }
        foreach(var port in SpawnPorts.Where(p=>!p.Water))
        {var a=EncounterAreas[port.ZoneId];roads.Add(new TownRoad(new Point(port.X,port.Z),new Point(a.X,a.Z),3000));}
        Roads=Array.AsReadOnly(roads.ToArray());
        var obstacles=new List<TownObstacle>();
        foreach(var b in Buildings)
        {
            if(b.Kind==TownBuildingKind.Dock)continue; // open boat desk, not a solid house.
            if(!b.Enterable){obstacles.Add(new TownObstacle(b.X,b.Z,b.Width,b.Depth,5000));continue;}
            obstacles.Add(new TownObstacle(b.X-b.Width/2,b.Z,500,b.Depth,5000));
            obstacles.Add(new TownObstacle(b.X+b.Width/2,b.Z,500,b.Depth,5000));
            obstacles.Add(new TownObstacle(b.X,b.Z+b.Depth/2,b.Width,500,5000));
            int wing=(b.Width-2800)/2;
            obstacles.Add(new TownObstacle(b.X-1400-wing/2,b.Z-b.Depth/2,wing,500,5000));
            obstacles.Add(new TownObstacle(b.X+1400+wing/2,b.Z-b.Depth/2,wing,500,5000));
        }
        void Cover(int x,int y,int w,int d,int h=1800){var p=FromDesign(x,y);obstacles.Add(new TownObstacle(p.X,p.Z,w*1000,d*1000,h));}
        Cover(26,89,3,2);Cover(33,96,3,2);Cover(29,51,4,7,3000);Cover(61,34,4,4,3500);
        Cover(106,46,4,2);Cover(116,53,4,2);Cover(105,98,3,3,3000);
        // Physical cargo screens and breakwaters hide emergence, with two body-wide routes around each.
        foreach(var port in SpawnPorts)
        {
            var a=EncounterAreas[port.ZoneId];long dx=(long)a.X-port.X,dz=(long)a.Z-port.Z;
            int length=HookGame.Sqrt(dx*dx+dz*dz),cx=port.X+(int)(dx*3000/length),cz=port.Z+(int)(dz*3000/length);
            bool horizontal=Math.Abs(dx)>Math.Abs(dz);
            obstacles.Add(new TownObstacle(cx,cz,horizontal?1500:3000,horizontal?3000:1500,3000));
        }
        Obstacles=Array.AsReadOnly(obstacles.ToArray());
        var wood=new List<TownWoodSegment>();for(int b=0;b<4;b++)for(int s=0;s<4;s++)wood.Add(new TownWoodSegment(b,s,WoodDirections[b]));
        WoodSegments=Array.AsReadOnly(wood.ToArray());
        FullWood=Array.AsReadOnly(wood.Select(s=>new PlatformTile(s.CellX,s.CellZ,TileKind.Wood)).ToArray());
        Shortcuts=new[]{Bridge((40,52),(58,48)),Bridge((73,35),(78,35),(86,55))};
        ReturnBridge=Bridge((57,91),(54,101),(25,111));
        BridgeCells=new HashSet<(int,int)>(Shortcuts.SelectMany(t=>t).Concat(ReturnBridge).Select(t=>(t.X,t.Z)));
        FixedGround=new TownGroundIndex();
    }
    public static bool Enabled(IslandMap map)=>map.IsTown;
    public static Point FromDesign(int xMetres,int yMetres)=>new Point((xMetres-80)*1000,(64-yMetres)*1000);
    /// <summary>Inverse world transform, returning design coordinates in millimetres.</summary>
    public static Point ToDesign(int x,int z)=>new Point(x+80000,64000-z);
    public static bool InBounds(int x,int z,int margin=0)=>Math.Abs((long)x)<=Width/2-margin && Math.Abs((long)z)<=Height/2-margin;
    public static PlatformTile[] InitialTiles()=>FullWood.ToArray();
    public static int BandForTile(int cellX,int cellZ)
    {foreach(var s in WoodSegments)if(s.CellX==cellX && s.CellZ==cellZ)return s.BandId;return -1;}
    public static PlatformTile[] ShortcutTiles(int shortcutIndex)=>Shortcuts[shortcutIndex>=0 && shortcutIndex<2?shortcutIndex:throw new ArgumentOutOfRangeException(nameof(shortcutIndex))].ToArray();
    public static PlatformTile[] ReturnBridgeTiles()=>ReturnBridge.ToArray();
    public static bool IsBridgeTile(int cellX,int cellZ)=>BridgeCells.Contains((cellX,cellZ));
    public static bool IsDynamicTile(int cellX,int cellZ)=>Destructible(cellX,cellZ) || IsBridgeTile(cellX,cellZ);
    private static PlatformTile[] Bridge(params (int x,int y)[] route)
    {
        var tiles=new List<PlatformTile>();
        for(int z=-31;z<=31;z++)for(int x=-39;x<=39;x++)
        {
            bool inside=false;
            for(int i=1;i<route.Length;i++)
                if(DistanceToSegmentSquared(x*2000,z*2000,FromDesign(route[i-1].x,route[i-1].y),FromDesign(route[i].x,route[i].y))<=2000L*2000){inside=true;break;}
            if(inside && !Destructible(x,z))tiles.Add(new PlatformTile(x,z,TileKind.Wood));
        }
        return tiles.ToArray();
    }
    public static bool Destructible(int cellX,int cellZ)=>BandForTile(cellX,cellZ)>=0;
    public static bool Blocked(int x,int z,int margin=0)
    {
        // 碰撞查询每帧调用很多次：索引遍历避免 LINQ 闭包及接口枚举器的临时分配。
        for(int i=0;i<Obstacles.Count;i++)if(Obstacles[i].Contains(x,z,margin))return true;
        return false;
    }
    public static bool OnGround(IReadOnlyList<PlatformTile> tiles,int x,int z)
    {
        if(!InBounds(x,z))return false;
        if(ArenaGround(tiles,x,z))return true;
        int cellX=PlatformRules.CellAt(x),cellZ=PlatformRules.CellAt(z);
        if(IsBridgeTile(cellX,cellZ) && PlatformRules.Contains(tiles,cellX,cellZ))return true;
        return FixedGround.Contains(x,z);
    }
    public static bool OnGround(int x,int z)=>OnGround(FullWood,x,z);
    public static bool CanStand(IReadOnlyList<PlatformTile> tiles,int x,int z,int radius=PlatformRules.Footprint)
        =>CanStand(tiles,x,z,radius,null);
    /// <summary>寻路建图可复用精确地面采样；边界、障碍和身体覆盖规则仍由同一实现负责。</summary>
    internal static bool CanStand(IReadOnlyList<PlatformTile> tiles,int x,int z,int radius,Func<int,int,bool>? ground)
    {
        if(!InBounds(x,z,radius) || Blocked(x,z,radius))return false;
        // A complete 100mm grid across the body catches small concave corners and destroyed strip seams.
        int steps=Math.Max(1,(radius*2+99)/100);
        for(int iz=0;iz<=steps;iz++)for(int ix=0;ix<=steps;ix++)
        {
            int px=x-radius+radius*2*ix/steps,pz=z-radius+radius*2*iz/steps;
            if(!(ground==null?OnGround(tiles,px,pz):ground(px,pz)))return false;
        }
        return true;
    }
    public static int DistanceToGround(int x,int z)
    {
        if(OnGround(x,z))return 0;
        int radius=HookGame.Sqrt(IslandMap.DistanceSquared(x,z,CenterX,CenterZ));
        int best=radius<12000?12000-radius:radius>22000?radius-22000:0;
        foreach(var road in Roads)best=Math.Min(best,Math.Max(0,HookGame.Sqrt(DistanceToSegmentSquared(x,z,road.Start,road.End))-road.HalfWidth));
        foreach(var a in EncounterAreas)
        {
            long dx=Math.Max(0,Math.Abs((long)x-a.X)-a.Width/2),dz=Math.Max(0,Math.Abs((long)z-a.Z)-a.Depth/2);
            best=Math.Min(best,HookGame.Sqrt(dx*dx+dz*dz));
        }
        foreach(var b in Buildings)
        {
            long dx=Math.Max(0,Math.Abs((long)x-b.X)-b.Width/2-1500),dz=Math.Max(0,Math.Abs((long)z-b.Z)-b.Depth/2-1500);
            best=Math.Min(best,HookGame.Sqrt(dx*dx+dz*dz));
        }
        // Include the fixed stone connector and all low landing lips in near-shore distance queries.
        long rx=(long)x-CenterX,rz=(long)z-CenterZ;
        if(-rz*819>=Math.Abs(rx)*574)best=Math.Min(best,Math.Max(0,Math.Max(9000-radius,radius-12000)));
        foreach(int sign in new[]{-1,1})
            best=Math.Min(best,HookGame.Sqrt(DistanceToSegmentSquared(x,z,new Point(CenterX+sign*7371,CenterZ-5166),new Point(CenterX+sign*9828,CenterZ-6888))));
        foreach(var d in Compass)
            best=Math.Min(best,Math.Max(0,HookGame.Sqrt(DistanceToSegmentSquared(x,z,new Point(CenterX+d.X*21,CenterZ+d.Z*21),new Point(CenterX+d.X*24,CenterZ+d.Z*24)))-1000));
        return best;
    }
    public static bool ArenaContains(int x,int z)=>IslandMap.DistanceSquared(x,z,CenterX,CenterZ)<=25000L*25000 && OnGround(x,z);
    private static bool ArenaGround(IReadOnlyList<PlatformTile> tiles,int x,int z)
    {
        long rx=x-CenterX,rz=z-CenterZ,r2=rx*rx+rz*rz;
        // 包含 24 米登陆唇及 1 米宽度的保守外界；城镇远处无需检查全部 16 个登陆唇。
        if(r2>26000L*26000)return false;
        if(r2>=20000L*20000 && r2<=22000L*22000)return true;
        // Fixed southern stone connector: angles 35..145 degrees, radii 9..12m.
        if(r2>=9000L*9000 && r2<=12000L*12000 && -rz*819>=Math.Abs(rx)*574)return true;
        if(r2>=12000L*12000 && r2<20000L*20000)
        {
            for(int i=0;i<WoodSegments.Count;i++)
            {
                var s=WoodSegments[i];
                long along=(rx*s.Direction.X+rz*s.Direction.Z)/1000,across=(rx*s.Direction.Z-rz*s.Direction.X)/1000;
                if(along>=s.InnerRadius && along<s.InnerRadius+2000 && Math.Abs(across)<=1000)
                    return PlatformRules.Contains(tiles,s.CellX,s.CellZ);
            }
            return true;
        }
        foreach(var d in Compass)
        {
            var a=new Point(CenterX+d.X*21,CenterZ+d.Z*21);var b=new Point(CenterX+d.X*24,CenterZ+d.Z*24);
            if(DistanceToSegmentSquared(x,z,a,b)<=1000L*1000)return true;
        }
        return false;
    }
    private static Point Nearest(int x,int z,Point a,Point b)
    {
        long dx=b.X-a.X,dz=b.Z-a.Z,len=dx*dx+dz*dz;
        long dot=len==0?0:Math.Max(0,Math.Min(len,((long)x-a.X)*dx+((long)z-a.Z)*dz));
        return new Point(a.X+(len==0?0:(int)(dx*dot/len)),a.Z+(len==0?0:(int)(dz*dot/len)));
    }
    internal static long DistanceToSegmentSquared(int x,int z,Point a,Point b)
    {var p=Nearest(x,z,a,b);return IslandMap.DistanceSquared(x,z,p.X,p.Z);}
}
