using System;
using System.IO;
namespace Advanture.Tactical.Simulation;

/// <summary>不可变战斗快照。所有会影响后续射击、伤害或表现去重的数据都随状态保存。</summary>
public sealed class CombatState
{
    internal const int ByteCount = 92;
    public CombatState(int health = 100, int ammo = 30, int reserve = 90, ulong nextFireTick = 0, ulong reloadUntilTick = 0, ulong respawnAtTick = 0, ulong shotSequence = 0, ulong lastShotTick = 0, uint hits = 0, uint kills = 0, uint deaths = 0, int lastHitSlot = -1, int shotX = 0, int shotY = 0, int shotZ = 0, int endX = 0, int endY = 0, int endZ = 0)
    {
        if (health < 0 || health > 100 || ammo < 0 || ammo > 30 || reserve < 0 || reserve > 90 ||
            (health == 0) != (respawnAtTick > 0) || (health == 0 && reloadUntilTick != 0) || lastHitSlot < -1 || lastHitSlot > 3 ||
            (shotSequence == 0) != (lastShotTick == 0) || hits > shotSequence)
            throw new InvalidDataException("战斗状态不合法。");
        foreach (int coordinate in new[]{shotX,shotY,shotZ,endX,endY,endZ})
            if (Math.Abs((long)coordinate) > 10200000) throw new InvalidDataException("射线坐标超出范围。");
        Health = health;
        Ammo = ammo;
        Reserve = reserve;
        NextFireTick = nextFireTick;
        ReloadUntilTick = reloadUntilTick;
        RespawnAtTick = respawnAtTick;
        ShotSequence = shotSequence;
        LastShotTick = lastShotTick;
        Hits = hits;
        Kills = kills;
        Deaths = deaths;
        LastHitSlot = lastHitSlot;
        ShotX = shotX;
        ShotY = shotY;
        ShotZ = shotZ;
        EndX = endX;
        EndY = endY;
        EndZ = endZ;
    }
    /// <summary>生命；零表示死亡。</summary>
    public int Health { get; }
    /// <summary>当前弹匣。</summary>
    public int Ammo { get; }
    /// <summary>剩余备弹。</summary>
    public int Reserve { get; }
    /// <summary>最早允许下一次射击的逻辑帧。</summary>
    public ulong NextFireTick { get; }
    /// <summary>换弹完成帧；零表示没有换弹。</summary>
    public ulong ReloadUntilTick { get; }
    /// <summary>死亡后的重生帧；活着时为零。</summary>
    public ulong RespawnAtTick { get; }
    /// <summary>累计射击序号，跨死亡保留，用于表现去重。</summary>
    public ulong ShotSequence { get; }
    /// <summary>最近一次射击的逻辑帧。</summary>
    public ulong LastShotTick { get; }
    /// <summary>累计有效命中数。</summary>
    public uint Hits { get; }
    /// <summary>累计击杀数。</summary>
    public uint Kills { get; }
    /// <summary>累计死亡数。</summary>
    public uint Deaths { get; }
    /// <summary>最近一枪命中的角色槽位；未命中角色时为 -1。</summary>
    public int LastHitSlot { get; }
    /// <summary>最近射线起点 X，毫米。</summary>
    public int ShotX { get; }
    /// <summary>最近射线起点 Y，毫米。</summary>
    public int ShotY { get; }
    /// <summary>最近射线起点 Z，毫米。</summary>
    public int ShotZ { get; }
    /// <summary>最近射线终点 X，毫米。</summary>
    public int EndX { get; }
    /// <summary>最近射线终点 Y，毫米。</summary>
    public int EndY { get; }
    /// <summary>最近射线终点 Z，毫米。</summary>
    public int EndZ { get; }
    public bool IsAlive => Health > 0;
    internal void Write(BinaryWriter writer)
    {
        writer.Write(Health);
        writer.Write(Ammo);
        writer.Write(Reserve);
        writer.Write(NextFireTick);
        writer.Write(ReloadUntilTick);
        writer.Write(RespawnAtTick);
        writer.Write(ShotSequence);
        writer.Write(LastShotTick);
        writer.Write(Hits);
        writer.Write(Kills);
        writer.Write(Deaths);
        writer.Write(LastHitSlot);
        writer.Write(ShotX);
        writer.Write(ShotY);
        writer.Write(ShotZ);
        writer.Write(EndX);
        writer.Write(EndY);
        writer.Write(EndZ);
    }
    internal static CombatState Read(BinaryReader reader) => new CombatState(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
}

/// <summary>仅模拟内部使用的可变工作副本；Capture 产生独立快照，不泄漏给画面层修改。</summary>
internal sealed class CombatModel
{
    internal int Health;
    internal int Ammo;
    internal int Reserve;
    internal ulong NextFireTick;
    internal ulong ReloadUntilTick;
    internal ulong RespawnAtTick;
    internal ulong ShotSequence;
    internal ulong LastShotTick;
    internal uint Hits;
    internal uint Kills;
    internal uint Deaths;
    internal int LastHitSlot;
    internal int ShotX;
    internal int ShotY;
    internal int ShotZ;
    internal int EndX;
    internal int EndY;
    internal int EndZ;
    internal CombatModel(CombatState state)
    {
        Health = state.Health;
        Ammo = state.Ammo;
        Reserve = state.Reserve;
        NextFireTick = state.NextFireTick;
        ReloadUntilTick = state.ReloadUntilTick;
        RespawnAtTick = state.RespawnAtTick;
        ShotSequence = state.ShotSequence;
        LastShotTick = state.LastShotTick;
        Hits = state.Hits;
        Kills = state.Kills;
        Deaths = state.Deaths;
        LastHitSlot = state.LastHitSlot;
        ShotX = state.ShotX;
        ShotY = state.ShotY;
        ShotZ = state.ShotZ;
        EndX = state.EndX;
        EndY = state.EndY;
        EndZ = state.EndZ;
    }
    internal CombatState Capture() => new CombatState(Health, Ammo, Reserve, NextFireTick, ReloadUntilTick, RespawnAtTick, ShotSequence, LastShotTick, Hits, Kills, Deaths, LastHitSlot, ShotX, ShotY, ShotZ, EndX, EndY, EndZ);
}
