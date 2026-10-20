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
    public IslandMap(IEnumerable<Point> shore, IEnumerable<Point> pier, IEnumerable<Point> spawns, IEnumerable<Point> fish)
    {
        Shore = Array.AsReadOnly(shore.ToArray()); Pier = Array.AsReadOnly(pier.ToArray());
        Spawns = Array.AsReadOnly(spawns.ToArray()); FishSpots = Array.AsReadOnly(fish.ToArray());
        if (Shore.Count < 3 || Shore.Count > 512 || Pier.Count != 4 || Spawns.Count != 4 || FishSpots.Count < 1 || FishSpots.Count > 32)
            throw new InvalidDataException("Invalid map counts.");
        foreach (var p in Shore.Concat(Pier).Concat(Spawns).Concat(FishSpots))
            if (Math.Abs((long)p.X) > 50000 || Math.Abs((long)p.Z) > 50000) throw new InvalidDataException("Map too large.");
    }
    public IReadOnlyList<Point> Shore { get; }
    public IReadOnlyList<Point> Pier { get; }
    public IReadOnlyList<Point> Spawns { get; }
    public IReadOnlyList<Point> FishSpots { get; }
    public bool OnLand(int x, int z) => Inside(Shore,x,z) || Inside(Pier,x,z);
    public bool CanStand(int x, int z)
    {
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
        w.Write(0x314D4948);
        foreach(var list in new[]{Shore,Pier,Spawns,FishSpots})
        { w.Write(list.Count); foreach(var p in list){w.Write(p.X);w.Write(p.Z);} }
        return stream.ToArray();
    }
    public static IslandMap Decode(ReadOnlySpan<byte> data)
    {
        if(data.Length<64 || data.Length>8192)throw new InvalidDataException("Invalid island map size.");
        using var r=new BinaryReader(new MemoryStream(data.ToArray(),false));
        if(r.ReadInt32()!=0x314D4948)throw new InvalidDataException("Map version mismatch.");
        var lists=new Point[4][];
        for(int i=0;i<4;i++)
        {
            int count=r.ReadInt32();
            if(count<1 || count>512 || r.BaseStream.Position+count*8>data.Length)throw new InvalidDataException("Invalid polygon.");
            lists[i]=new Point[count];for(int j=0;j<count;j++)lists[i][j]=new Point(r.ReadInt32(),r.ReadInt32());
        }
        if(r.BaseStream.Position!=data.Length)throw new InvalidDataException("Trailing map bytes.");
        return new IslandMap(lists[0],lists[1],lists[2],lists[3]);
    }
}
