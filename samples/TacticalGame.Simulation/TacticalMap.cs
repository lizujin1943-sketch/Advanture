using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.Tactical.Simulation;

/// <summary>世界 XZ 平面的轴对齐矩形，所有坐标都是整数毫米。</summary>
public readonly struct MapRect
{
    public MapRect(int minX, int minZ, int maxX, int maxZ, int minY = 0, int maxY = 3000)
    { MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; MinY = minY; MaxY = maxY; }
    public int MinX { get; }
    public int MinZ { get; }
    public int MaxX { get; }
    public int MaxZ { get; }
    /// <summary>子弹遮挡使用的高度区间；角色移动仍只检查 XZ 平面。</summary>
    public int MinY { get; }
    public int MaxY { get; }
    internal void Validate()
    {
        if (MinY >= MaxY || MinY < -10000000 || MaxY > 10000000 || MinX >= MaxX || MinZ >= MaxZ || MinX < -10000000 || MinZ < -10000000 || MaxX > 10000000 || MaxZ > 10000000)
            throw new InvalidDataException("地图矩形范围不合法。");
    }
}

public readonly struct PlayerSpawn
{
    public PlayerSpawn(int x, int z, ushort yaw) { X = x; Z = z; Yaw = yaw; }
    public int X { get; }
    public int Z { get; }
    public ushort Yaw { get; }
}

/// <summary>由编辑器导出的碰撞数据。没有 Unity 对象；房主随初始快照把这些字节发给两端。</summary>
public sealed class TacticalMap
{
    public const int PlayerRadius = 350;
    private const uint Magic = 0x32504D54; // TMP2：加入障碍物的真实高度。
    private const uint HeightMagic = 0x33504D54;
    private const uint ClearanceMagic = 0x34504D54;
    private readonly TacticalHeightField? terrain;
    public TacticalMap(MapRect area, IReadOnlyList<PlayerSpawn> spawns, IReadOnlyList<MapRect> obstacles)
        : this(area, spawns, obstacles, null) { }

    private TacticalMap(MapRect area, IReadOnlyList<PlayerSpawn> spawns, IReadOnlyList<MapRect> obstacles, TacticalHeightField? terrain)
    {
        area.Validate();
        if (spawns == null || spawns.Count < 1 || spawns.Count > 4 || obstacles == null || obstacles.Count > 128)
            throw new InvalidDataException("出生点或碰撞体数量不合法。");
        Area = area;
        this.terrain = terrain;
        Spawns = Array.AsReadOnly(spawns.ToArray());
        Obstacles = Array.AsReadOnly(obstacles.ToArray());
        foreach (var obstacle in Obstacles) obstacle.Validate();
        foreach (var spawn in Spawns)
            if (spawn.Yaw >= 36000 || !CanStand(spawn.X, spawn.Z))
                throw new InvalidDataException("出生点位于障碍内或地图外。");
    }
    public MapRect Area { get; }
    public IReadOnlyList<PlayerSpawn> Spawns { get; }
    public IReadOnlyList<MapRect> Obstacles { get; }

    public bool HasElevation => terrain != null;
    public int GroundHeight(int x, int z) => terrain != null && terrain.TryGround(x, z, out int y) ? y : 0;
    public int FeetHeight(TacticalPlayer player) => GroundHeight(player.X, player.Z) + player.HeightAboveGround;
    internal int Headroom(int x, int z)
    {
        int ceiling = terrain?.CeilingAt(x, z) ?? int.MaxValue;
        return ceiling == int.MaxValue ? int.MaxValue : ceiling - GroundHeight(x, z) - VerticalMotion.BodyHeight;
    }
    internal int SweepTerrain(int from, int other, int target, bool xAxis, int? airborneFeet) =>
        terrain?.Sweep(from, other, target, xAxis, airborneFeet) ?? target;
    internal long TraceTerrain(int x, int y, int z, int dx, int dy, int dz, long limit) => terrain?.Trace(x, y, z, dx, dy, dz, limit) ?? limit;

    /// <summary>用 0.7 米宽的方形脚印保守近似角色。刚好接触障碍边缘允许站立和滑动。</summary>
    public bool CanStand(int x, int z)
    {
        if (x < Area.MinX + PlayerRadius || x > Area.MaxX - PlayerRadius || z < Area.MinZ + PlayerRadius || z > Area.MaxZ - PlayerRadius)
            return false;
        foreach (var o in Obstacles)
            if (x > o.MinX - PlayerRadius && x < o.MaxX + PlayerRadius && z > o.MinZ - PlayerRadius && z < o.MaxZ + PlayerRadius)
                return false;
        return terrain == null || terrain.TryGround(x, z, out _);
    }

    public byte[] Encode()
    {
        var bytes = new byte[31 + Spawns.Count * 10 + Obstacles.Count * 24 + (terrain == null ? 0 : terrain.Encoded.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, terrain == null ? Magic : terrain.HasCeiling ? ClearanceMagic : HeightMagic);
        WriteRect(bytes.AsSpan(4), Area);
        bytes[28] = (byte)Spawns.Count;
        int offset = 29;
        foreach (var spawn in Spawns)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), spawn.X);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4), spawn.Z);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 8), spawn.Yaw);
            offset += 10;
        }
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), (ushort)Obstacles.Count);
        offset += 2;
        foreach (var o in Obstacles) { WriteRect(bytes.AsSpan(offset), o); offset += 24; }
        if (terrain != null) terrain.Encoded.CopyTo(bytes.AsSpan(offset));
        return bytes;
    }

    public static TacticalMap Decode(ReadOnlySpan<byte> bytes)
    {
        uint version = bytes.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : 0;
        if (bytes.Length < 31 || (version != Magic && version != HeightMagic && version != ClearanceMagic) || bytes[28] < 1 || bytes[28] > 4)
            throw new InvalidDataException("地图头部不合法。");
        int count = bytes[28], offset = 29 + count * 10;
        if (bytes.Length < offset + 2) throw new InvalidDataException("地图被截断。");
        int obstacles = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset));
        int end = offset + 2 + obstacles * 24;
        if (obstacles > 128 || bytes.Length < end || (version == Magic && bytes.Length != end)) throw new InvalidDataException("地图长度不合法。");
        var spawns = new PlayerSpawn[count];
        for (int i = 0; i < count; i++)
        {
            var part = bytes.Slice(29 + i * 10);
            spawns[i] = new PlayerSpawn(BinaryPrimitives.ReadInt32LittleEndian(part), BinaryPrimitives.ReadInt32LittleEndian(part.Slice(4)), BinaryPrimitives.ReadUInt16LittleEndian(part.Slice(8)));
        }
        var rects = new MapRect[obstacles];
        for (int i = 0; i < obstacles; i++) rects[i] = ReadRect(bytes.Slice(offset + 2 + i * 24));
        var area = ReadRect(bytes.Slice(4));
        return new TacticalMap(area, spawns, rects, version == Magic ? null : new TacticalHeightField(bytes.Slice(end), area, version == ClearanceMagic));
    }

    private static void WriteRect(Span<byte> b, MapRect r)
    {
        BinaryPrimitives.WriteInt32LittleEndian(b, r.MinX); BinaryPrimitives.WriteInt32LittleEndian(b.Slice(4), r.MinZ);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(8), r.MaxX); BinaryPrimitives.WriteInt32LittleEndian(b.Slice(12), r.MaxZ);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(16), r.MinY); BinaryPrimitives.WriteInt32LittleEndian(b.Slice(20), r.MaxY);
    }
    private static MapRect ReadRect(ReadOnlySpan<byte> b) => new MapRect(BinaryPrimitives.ReadInt32LittleEndian(b), BinaryPrimitives.ReadInt32LittleEndian(b.Slice(4)),
        BinaryPrimitives.ReadInt32LittleEndian(b.Slice(8)), BinaryPrimitives.ReadInt32LittleEndian(b.Slice(12)), BinaryPrimitives.ReadInt32LittleEndian(b.Slice(16)), BinaryPrimitives.ReadInt32LittleEndian(b.Slice(20)));
}
