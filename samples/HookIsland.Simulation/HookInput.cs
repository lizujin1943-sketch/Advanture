using System;
using System.Buffers.Binary;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>毫米坐标、千分比移动方向。射击边沿只消费一次；取消也是确定性输入。</summary>
public readonly struct HookInput
{
    public const int ByteCount = 13;
    public HookInput(short moveX, short moveZ, int aimX = 0, int aimZ = 0, bool fire = false, bool cancel = false)
    { MoveX = moveX; MoveZ = moveZ; AimX = aimX; AimZ = aimZ; Fire = fire; Cancel = cancel; }
    public short MoveX { get; }
    public short MoveZ { get; }
    public int AimX { get; }
    public int AimZ { get; }
    public bool Fire { get; }
    public bool Cancel { get; }
    public byte[] Encode()
    {
        var b = new byte[ByteCount];
        BinaryPrimitives.WriteInt16LittleEndian(b, MoveX); BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(2), MoveZ);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), AimX); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), AimZ);
        b[12] = (byte)((Fire ? 1 : 0) | (Cancel ? 2 : 0));
        Decode(b); return b;
    }
    public static HookInput Decode(ReadOnlySpan<byte> b)
    {
        if (b.Length != ByteCount || b[12] > 3) throw new InvalidDataException("Invalid hook input.");
        short x = BinaryPrimitives.ReadInt16LittleEndian(b), z = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(2));
        int ax = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(4)), az = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(8));
        if ((long)x*x + (long)z*z > 1000000 || Math.Abs((long)ax) > 100000 || Math.Abs((long)az) > 100000)
            throw new InvalidDataException("Input outside supported range.");
        return new HookInput(x, z, ax, az, (b[12] & 1) != 0, (b[12] & 2) != 0);
    }
}
