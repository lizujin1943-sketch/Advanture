using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FrameSync.Abstractions;

namespace Advanture.Tactical.Simulation;

/// <summary>仓库试玩的游戏接入层。框架只看到四个接口，不认识角色、地图、毫米或角度。</summary>
public sealed class TacticalGame : IStateCodec<TacticalState>, ISimulationFactory<TacticalState>, IInputValidator
{
    public static SimulationModule<TacticalState> CreateModule()
    {
        var game = new TacticalGame();
        return new SimulationModule<TacticalState>(new CompatibilityManifest("advanture-yard", determinismHash: "integer-mm-sweep-v1"), game, game, game);
    }
    public IDeterministicSimulation<TacticalState> Create() => new Model();
    public void ValidateCanonical(ReadOnlySpan<byte> bytes) => TacticalInput.Decode(bytes);

    public byte[] Encode(TacticalState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var bytes = new byte[10 + 22 * state.Players.Count];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, state.Steps);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)state.Players.Count);
        for (int i = 0; i < state.Players.Count; i++)
        {
            var p = state.Players[i]; var b = bytes.AsSpan(10 + i * 22);
            BinaryPrimitives.WriteUInt16LittleEndian(b, p.Slot);
            BinaryPrimitives.WriteInt32LittleEndian(b.Slice(2), p.X); BinaryPrimitives.WriteInt32LittleEndian(b.Slice(6), p.Z);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(10), p.Yaw); BinaryPrimitives.WriteInt16LittleEndian(b.Slice(12), p.Pitch);
            BinaryPrimitives.WriteInt32LittleEndian(b.Slice(14), p.RemainderX); BinaryPrimitives.WriteInt32LittleEndian(b.Slice(18), p.RemainderZ);
        }
        return bytes;
    }

    public TacticalState Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10) throw new InvalidDataException("状态头部被截断。");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8));
        if (count < 1 || count > 4 || bytes.Length != 10 + count * 22) throw new InvalidDataException("状态长度不合法。");
        var players = new TacticalPlayer[count];
        for (int i = 0; i < count; i++)
        {
            var b = bytes.Slice(10 + i * 22);
            players[i] = new TacticalPlayer(BinaryPrimitives.ReadUInt16LittleEndian(b), BinaryPrimitives.ReadInt32LittleEndian(b.Slice(2)),
                BinaryPrimitives.ReadInt32LittleEndian(b.Slice(6)), BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(10)), BinaryPrimitives.ReadInt16LittleEndian(b.Slice(12)),
                BinaryPrimitives.ReadInt32LittleEndian(b.Slice(14)), BinaryPrimitives.ReadInt32LittleEndian(b.Slice(18)));
        }
        return new TacticalState(BinaryPrimitives.ReadUInt64LittleEndian(bytes), players);
    }

    private sealed class Model : IDeterministicSimulation<TacticalState>
    {
        private TacticalMap _map = null!;
        private TacticalPlayer[] _players = Array.Empty<TacticalPlayer>();
        private int _denominator;
        private ulong _steps;

        public void Initialize(SimulationInitialization initialization)
        {
            _map = TacticalMap.Decode(initialization.Data.Span);
            if (initialization.Session.TickRate > 1000) throw new InvalidDataException("试玩最多支持 1000 Hz。");
            _denominator = checked((int)initialization.Session.TickRate * 1000);
            _players = new TacticalPlayer[initialization.Session.Participants.Count];
            for (int i = 0; i < _players.Length; i++)
            {
                ushort slot = initialization.Session.Participants[i].Slot;
                if (slot >= _map.Spawns.Count) throw new InvalidDataException("缺少玩家出生点。");
                _players[i] = Spawn(slot);
            }
            _steps = 0;
        }

        public IReadOnlyList<ByteString> Step(FrameContext context, IReadOnlyList<SimulationInput> inputs)
        {
            foreach (var item in inputs)
            {
                int index = Array.FindIndex(_players, p => p.Slot == item.Participant.Slot);
                var p = _players[index];
                var input = TacticalInput.Decode(item.Payload.Span);
                if (input.Respawn) { _players[index] = Spawn(p.Slot); continue; }
                int speed = input.Sprint ? 7000 : 4500;
                long numeratorX = (long)input.MoveX * speed + p.RemainderX;
                long numeratorZ = (long)input.MoveZ * speed + p.RemainderZ;
                int dx = (int)(numeratorX / _denominator), dz = (int)(numeratorZ / _denominator);
                int x = Sweep(p.X, p.Z, dx, true);
                int z = Sweep(p.Z, x, dz, false);
                // 被墙截断的速度余数直接丢弃，不把撞墙时积累的距离带到下一帧。
                _players[index] = new TacticalPlayer(p.Slot, x, z, input.HasLook ? input.Yaw : p.Yaw, input.HasLook ? input.Pitch : p.Pitch,
                    x == p.X + dx ? (int)(numeratorX % _denominator) : 0, z == p.Z + dz ? (int)(numeratorZ % _denominator) : 0);
            }
            _steps = checked(_steps + 1);
            return Array.Empty<ByteString>();
        }

        /// <summary>先扫 X 再扫 Z，碰到扩张后的矩形就停在边缘。扫整个路径，低帧率也不会穿墙。</summary>
        private int Sweep(int from, int other, int delta, bool xAxis)
        {
            int radius = TacticalMap.PlayerRadius;
            int min = (xAxis ? _map.Area.MinX : _map.Area.MinZ) + radius;
            int max = (xAxis ? _map.Area.MaxX : _map.Area.MaxZ) - radius;
            int target = Math.Max(min, Math.Min(max, from + delta));
            foreach (var o in _map.Obstacles)
            {
                int otherMin = (xAxis ? o.MinZ : o.MinX) - radius, otherMax = (xAxis ? o.MaxZ : o.MaxX) + radius;
                if (other <= otherMin || other >= otherMax) continue;
                int near = (xAxis ? o.MinX : o.MinZ) - radius, far = (xAxis ? o.MaxX : o.MaxZ) + radius;
                if (delta > 0 && from <= near && target > near) target = near;
                if (delta < 0 && from >= far && target < far) target = far;
            }
            return target;
        }

        private TacticalPlayer Spawn(ushort slot)
        { var s = _map.Spawns[slot]; return new TacticalPlayer(slot, s.X, s.Z, s.Yaw, 0); }
        public TacticalState Capture() => new TacticalState(_steps, _players);
        public void Restore(TacticalState state)
        {
            if (state.Players.Count != _players.Length) throw new InvalidDataException("恢复角色数量不匹配。");
            var copy = new TacticalPlayer[_players.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                var p = state.Players[i];
                if (p.Slot != _players[i].Slot || !_map.CanStand(p.X, p.Z) || Math.Abs((long)p.RemainderX) >= _denominator || Math.Abs((long)p.RemainderZ) >= _denominator)
                    throw new InvalidDataException("恢复状态与地图或帧率不匹配。");
                copy[i] = p;
            }
            _players = copy; _steps = state.Steps;
        }
    }
}
