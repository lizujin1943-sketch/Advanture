using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public readonly struct Point
{
    public Point(int x, int z) { X = x; Z = z; }
    public int X { get; }
    public int Z { get; }
}

/// <summary>从美术海岸线导出，多边形与码头取并集。逻辑碰撞不依赖 Unity/PhysX。</summary>
public sealed class IslandMap
{
    public IslandMap(IEnumerable<Point> shore, IEnumerable<Point> pier, IEnumerable<Point> spawns, IEnumerable<Point> fish,PlatformConfig? platform=null,FishingConfig? fishing=null)
    {
        Shore = Array.AsReadOnly(shore.ToArray()); Pier = Array.AsReadOnly(pier.ToArray());
        Spawns = Array.AsReadOnly(spawns.ToArray()); FishSpots = Array.AsReadOnly(fish.ToArray());
        Fishing=fishing;
        if(fishing?.Merchants.Definitions.Count>0)
        {
            if(platform==null)throw new InvalidDataException("Offshore merchants require a platform.");
            var cells=platform.BlockedCells.Concat(fishing.Merchants.Definitions.Select(d=>new Point(d.CellX,d.CellZ)))
                .GroupBy(p=>(p.X,p.Z)).Select(g=>g.First());
            platform=new PlatformConfig(platform.WoodTileCost,platform.FishboneTileCost,cells);
        }
        Platform=platform;
        if(Fishing?.Finale.Enabled==true && Platform==null)throw new InvalidDataException("Finale requires a platform.");
        if (Shore.Count < 3 || Shore.Count > 512 || Pier.Count != 4 || Spawns.Count != 4 || FishSpots.Count < 1 || FishSpots.Count > 32)
            throw new InvalidDataException("Invalid map counts.");
        foreach (var p in Shore.Concat(Pier).Concat(Spawns).Concat(FishSpots))
            if (Math.Abs((long)p.X) > 50000 || Math.Abs((long)p.Z) > 50000) throw new InvalidDataException("Map too large.");
        if(Fishing?.Ocean.Enabled==true && (Platform==null || FishSpots.Count!=Fishing.Ocean.Unlocks.Count+Fishing.Ocean.VisitSlots))
            throw new InvalidDataException("Ocean progression requires a platform and fixed fish slots.");
    }
    public IReadOnlyList<Point> Shore { get; }
    public IReadOnlyList<Point> Pier { get; }
    public IReadOnlyList<Point> Spawns { get; }
    public IReadOnlyList<Point> FishSpots { get; }
    public PlatformConfig? Platform { get; }
    public FishingConfig? Fishing { get; }
    public int HookRange(int level)=>Platform==null?HookUpgrades.Range(level):4000+level*4000;
    public bool OnLand(int x, int z) => Platform!=null?PlatformRules.OnPlatform(PlatformRules.InitialTiles(),x,z):Inside(Shore,x,z) || Inside(Pier,x,z);
    public bool CanStand(HookState state,int x,int z)=>Platform==null?CanStand(x,z):PlatformRules.CanStand(state.Tiles,x,z);
    public static IslandMap CreatePlatform(int woodTileCost=4,int fishboneTileCost=4)
    {
        var edge=new[]{new Point(-3000,-3000),new Point(3000,-3000),new Point(3000,3000),new Point(-3000,3000)};
        var spawns=new[]{new Point(-1400,-1400),new Point(1400,-1400),new Point(1400,1400),new Point(-1400,1400)};
        var directions=new[]{new Point(0,-1000),new Point(707,-707),new Point(1000,0),new Point(707,707),
            new Point(0,1000),new Point(-707,707),new Point(-1000,0),new Point(-707,-707)};
        var schools=directions.SelectMany(d=>new[]{6200,10400,14800}.Select(r=>new Point(d.X*r/1000,d.Z*r/1000)));
        return new IslandMap(edge,edge,spawns,schools,new PlatformConfig(woodTileCost,fishboneTileCost),new FishingConfig());
    }
    public int DistanceToLand(int x,int z)
    {
        if(OnLand(x,z))return 0;
        long closest=long.MaxValue;
        foreach(var polygon in new[]{Shore,Pier})for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
        {
            var a=polygon[j];var b=polygon[i];long dx=b.X-a.X,dz=b.Z-a.Z,len=dx*dx+dz*dz;
            long t=len==0?0:Math.Max(0,Math.Min(len,(x-a.X)*dx+(z-a.Z)*dz));
            int px=a.X+(len==0?0:(int)(dx*t/len)),pz=a.Z+(len==0?0:(int)(dz*t/len));
            closest=Math.Min(closest,DistanceSquared(x,z,px,pz));
        }
        return HookGame.Sqrt(closest);
    }
    public bool CanStand(int x, int z)
    {
        if(Platform!=null)return PlatformRules.CanStand(PlatformRules.InitialTiles(),x,z);
        const int r = 330;
        return OnLand(x-r,z-r) && OnLand(x-r,z+r) && OnLand(x+r,z-r) && OnLand(x+r,z+r)
            && !Blocked(x,z,r);
    }
    public static bool Blocked(int x, int z, int margin = 0) =>
        DistanceSquared(x,z,0,1050) < (long)(2050+margin)*(2050+margin) ||
        DistanceSquared(x,z,-2150,1550) < (long)(390+margin)*(390+margin);
    public static long DistanceSquared(int x, int z, int bx, int bz) => (long)(x-bx)*(x-bx)+(long)(z-bz)*(z-bz);
    private static bool Inside(IReadOnlyList<Point> polygon, int x, int z)
    {
        bool inside = false;
        for (int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
        {
            var a=polygon[i]; var b=polygon[j];
            if ((a.Z>z)==(b.Z>z)) continue;
            long left=(long)(x-a.X)*(b.Z-a.Z),right=(long)(b.X-a.X)*(z-a.Z);
            if (b.Z>a.Z ? left<right : left>right) inside=!inside;
        }
        return inside;
    }
    public byte[] Encode()
    {
        using var stream=new MemoryStream(); using var w=new BinaryWriter(stream);
        w.Write(Fishing!=null?0x484D4948:Platform==null?0x314D4948:0x324D4948);
        if(Fishing!=null)w.Write(Platform!=null);
        foreach(var list in new[]{Shore,Pier,Spawns,FishSpots})
        { w.Write(list.Count); foreach(var p in list){w.Write(p.X);w.Write(p.Z);} }
        if(Platform!=null)
        {
            w.Write(Platform.WoodTileCost);w.Write(Platform.FishboneTileCost);w.Write(Platform.BlockedCells.Count);
            foreach(var p in Platform.BlockedCells){w.Write(p.X);w.Write(p.Z);}
        }
        if(Fishing!=null)Fishing.Write(w);
        return stream.ToArray();
    }
    public static IslandMap Decode(ReadOnlySpan<byte> data)
    {
        if(data.Length<64 || data.Length>8192)throw new InvalidDataException("Invalid island map size.");
        using var r=new BinaryReader(new MemoryStream(data.ToArray(),false));
        int version=r.ReadInt32();
        if(version!=0x314D4948 && version!=0x324D4948 && version!=0x484D4948)throw new InvalidDataException("Map version mismatch.");
        bool platformPresent=version==0x484D4948?r.ReadBoolean():version==0x324D4948;
        var lists=new Point[4][];
        for(int i=0;i<4;i++)
        {
            int count=r.ReadInt32();
            if(count<1 || count>512 || r.BaseStream.Position+count*8>data.Length)throw new InvalidDataException("Invalid polygon.");
            lists[i]=new Point[count];for(int j=0;j<count;j++)lists[i][j]=new Point(r.ReadInt32(),r.ReadInt32());
        }
        PlatformConfig? platform=null;
        if(platformPresent)
        {
            int wood=r.ReadInt32(),bone=r.ReadInt32(),count=r.ReadInt32();
            if(count<0 || count>64)throw new InvalidDataException("Invalid building count.");
            var blocked=new Point[count];for(int i=0;i<count;i++)blocked[i]=new Point(r.ReadInt32(),r.ReadInt32());
            platform=new PlatformConfig(wood,bone,blocked);
        }
        FishingConfig? fishing=version==0x484D4948?FishingConfig.Read(r):null;
        if(r.BaseStream.Position!=data.Length)throw new InvalidDataException("Trailing map bytes.");
        return new IslandMap(lists[0],lists[1],lists[2],lists[3],platform,fishing);
    }
}
