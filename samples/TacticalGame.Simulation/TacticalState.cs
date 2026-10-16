using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.Tactical.Simulation;

/// <summary>一个角色的全部可回滚状态。除法余数也必须保存，否则恢复后移动会慢慢偏移。</summary>
public readonly struct TacticalPlayer
{
    public TacticalPlayer(ushort slot, int x, int z, ushort yaw, short pitch, int remainderX = 0, int remainderZ = 0)
    { Slot = slot; X = x; Z = z; Yaw = yaw; Pitch = pitch; RemainderX = remainderX; RemainderZ = remainderZ; }
    public ushort Slot { get; }
    public int X { get; }
    public int Z { get; }
    public ushort Yaw { get; }
    public short Pitch { get; }
    public int RemainderX { get; }
    public int RemainderZ { get; }
}

/// <summary>独立、不可变的状态副本；按槽位排序，编码顺序不依赖字典遍历。</summary>
public sealed class TacticalState
{
    public TacticalState(ulong steps, IReadOnlyList<TacticalPlayer> players)
    {
        if (players == null || players.Count < 1 || players.Count > 4) throw new InvalidDataException("玩家数量不合法。");
        var copy = players.ToArray();
        for (int i = 0; i < copy.Length; i++)
        {
            var p = copy[i];
            if (p.Slot > 3 || (i > 0 && copy[i - 1].Slot >= p.Slot) || Math.Abs((long)p.X) > 10000000 || Math.Abs((long)p.Z) > 10000000 ||
                p.Yaw >= 36000 || p.Pitch < -8500 || p.Pitch > 8500 || Math.Abs((long)p.RemainderX) >= 1000000 || Math.Abs((long)p.RemainderZ) >= 1000000)
                throw new InvalidDataException("角色状态不合法。");
        }
        Steps = steps;
        Players = Array.AsReadOnly(copy);
    }
    public ulong Steps { get; }
    public IReadOnlyList<TacticalPlayer> Players { get; }
}
