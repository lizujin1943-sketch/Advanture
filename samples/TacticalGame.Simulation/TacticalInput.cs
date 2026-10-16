using System;
using System.Buffers.Binary;
using System.IO;

namespace Advanture.Tactical.Simulation;

/// <summary>某位玩家一帧的完整输入。方向已经转成世界坐标，模拟不读取鼠标或相机。</summary>
public readonly struct TacticalInput
{
    public const int ByteCount = 9;
    public TacticalInput(short x, short z, ushort yaw = 0, short pitch = 0,
        bool hasLook = false, bool sprint = false, bool respawn = false)
    { MoveX = x; MoveZ = z; Yaw = yaw; Pitch = pitch; HasLook = hasLook; Sprint = sprint; Respawn = respawn; }
    /// <summary>世界 X/Z 方向，1000 表示满速；向量长度不能超过 1000。</summary>
    public short MoveX { get; }
    public short MoveZ { get; }
    /// <summary>朝向和俯仰，单位为 0.01 度。只有 HasLook 为 true 时才替换旧朝向。</summary>
    public ushort Yaw { get; }
    public short Pitch { get; }
    public bool HasLook { get; }
    public bool Sprint { get; }
    public bool Respawn { get; }

    /// <summary>固定小端格式；default 编码为九个零，即保持朝向并停止移动的中性输入。</summary>
    public byte[] Encode()
    {
        Validate();
        var bytes = new byte[ByteCount];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, MoveX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), MoveZ);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), Yaw);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), Pitch);
        bytes[8] = (byte)((HasLook ? 1 : 0) | (Sprint ? 2 : 0) | (Respawn ? 4 : 0));
        return bytes;
    }

    public static TacticalInput Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteCount || (bytes[8] & ~7) != 0)
            throw new InvalidDataException("输入长度或标志不合法。");
        var input = new TacticalInput(BinaryPrimitives.ReadInt16LittleEndian(bytes),
            BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(2)), BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4)),
            BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(6)), (bytes[8] & 1) != 0, (bytes[8] & 2) != 0, (bytes[8] & 4) != 0);
        input.Validate();
        return input;
    }

    private void Validate()
    {
        if ((long)MoveX * MoveX + (long)MoveZ * MoveZ > 1000000 || Yaw >= 36000 || Pitch < -8500 || Pitch > 8500 ||
            (!HasLook && (Yaw != 0 || Pitch != 0)))
            throw new InvalidDataException("输入方向、角度或中性格式不合法。");
    }
}
