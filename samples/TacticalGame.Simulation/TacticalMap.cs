using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.Tactical.Simulation;

/// <summary>世界 XZ 平面的轴对齐矩形，所有坐标都是整数毫米。</summary>
public readonly struct MapRect
{
    public MapRect(int minX, int minZ, int maxX, int maxZ)
    { MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; }
    public int MinX { get; }
    public int MinZ { get; }
    public int MaxX { get; }
    public int MaxZ { get; }
    internal void Validate()
    {
        if (MinX >= MaxX || MinZ >= MaxZ || MinX < -10000000 || MinZ < -10000000 || MaxX > 10000000 || MaxZ > 10000000)
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
    private const uint Magic = 0x31504D54; // TMP1：地图格式第一版。
    public TacticalMap(MapRect area, IReadOnlyList<PlayerSpawn> spawns, IReadOnlyList<MapRect> obstacles)
    {
        area.Validate();
        if (spawns == null || spawns.Count < 1 || spawns.Count > 4 || obstacles == null || obstacles.Count > 128)
            throw new InvalidDataException("出生点或碰撞体数量不合法。");
        Area = area;
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

    /// <summary>用 0.7 米宽的方形脚印保守近似角色。刚好接触障碍边缘允许站立和滑动。</summary>
    public bool CanStand(int x, int z)
    {
        if (x < Area.MinX + PlayerRadius || x > Area.MaxX - PlayerRadius || z < Area.MinZ + PlayerRadius || z > Area.MaxZ - PlayerRadius)
            return false;
        foreach (var o in Obstacles)
            if (x > o.MinX - PlayerRadius && x < o.MaxX + PlayerRadius && z > o.MinZ - PlayerRadius && z < o.MaxZ + PlayerRadius)
                return false;
        return true;
    }

    public byte[] Encode()
    {
        var bytes = new byte[23 + Spawns.Count * 10 + Obstacles.Count * 16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        WriteRect(bytes.AsSpan(4), Area);
        bytes[20] = (byte)Spawns.Count;
        int offset = 21;
        foreach (var spawn in Spawns)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), spawn.X);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4), spawn.Z);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 8), spawn.Yaw);
            offset += 10;
        }
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), (ushort)Obstacles.Count);
        offset += 2;
        foreach (var o in Obstacles) { WriteRect(bytes.AsSpan(offset), o); offset += 16; }
        return bytes;
    }

    public static TacticalMap Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 23 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic || bytes[20] < 1 || bytes[20] > 4)
            throw new InvalidDataException("地图头部不合法。");
        int count = bytes[20], offset = 21 + count * 10;
        if (bytes.Length < offset + 2) throw new InvalidDataException("地图被截断。");
        int obstacles = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset));
        if (obstacles > 128 || bytes.Length != offset + 2 + obstacles * 16) throw new InvalidDataException("地图长度不合法。");
        var spawns = new PlayerSpawn[count];
        for (int i = 0; i < count; i++)
        {
            var part = bytes.Slice(21 + i * 10);
            spawns[i] = new PlayerSpawn(BinaryPrimitives.ReadInt32LittleEndian(part), BinaryPrimitives.ReadInt32LittleEndian(part.Slice(4)), BinaryPrimitives.ReadUInt16LittleEndian(part.Slice(8)));
        }
        var rects = new MapRect[obstacles];
        for (int i = 0; i < obstacles; i++) rects[i] = ReadRect(bytes.Slice(offset + 2 + i * 16));
        return new TacticalMap(ReadRect(bytes.Slice(4)), spawns, rects);
    }

    private static void WriteRect(Span<byte> b, MapRect r)
    {
        BinaryPrimitives.WriteInt32LittleEndian(b, r.MinX); BinaryPrimitives.WriteInt32LittleEndian(b.Slice(4), r.MinZ);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(8), r.MaxX); BinaryPrimitives.WriteInt32LittleEndian(b.Slice(12), r.MaxZ);
    }
    private static MapRect ReadRect(ReadOnlySpan<byte> b) => new MapRect(BinaryPrimitives.ReadInt32LittleEndian(b), BinaryPrimitives.ReadInt32LittleEndian(b.Slice(4)),
        BinaryPrimitives.ReadInt32LittleEndian(b.Slice(8)), BinaryPrimitives.ReadInt32LittleEndian(b.Slice(12)));
}
