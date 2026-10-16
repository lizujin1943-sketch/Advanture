using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FrameSync.Abstractions;

namespace Advanture.Tactical.Simulation;

/// <summary>仓库试玩的游戏接入层。框架只看到四个接口，不认识角色、地图、毫米或角度。</summary>
public sealed partial class TacticalGame : IStateCodec<TacticalState>, ISimulationFactory<TacticalState>, IInputValidator
{
    public static SimulationModule<TacticalState> CreateModule()
    {
        var game = new TacticalGame();
        return new SimulationModule<TacticalState>(new CompatibilityManifest("advanture-yard", rulesVersion: "2", inputFormatVersion: "2", stateFormatVersion: "2", contentHash: "ak47-30-90-25-100ms-2400ms-3000ms-v1", determinismHash: "integer-mm-ray-v2"), game, game, game);
    }
    public IDeterministicSimulation<TacticalState> Create() => new Model();
    public void ValidateCanonical(ReadOnlySpan<byte> bytes) => TacticalInput.Decode(bytes);

    /// <summary>版本 2 固定小端格式；武器计时与最近射击也参与状态哈希。</summary>
    public byte[] Encode(TacticalState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0x32434154U); writer.Write(state.Steps); writer.Write((ushort)state.Players.Count);
        foreach (var p in state.Players)
        {
            writer.Write(p.Slot); writer.Write(p.X); writer.Write(p.Z); writer.Write(p.Yaw); writer.Write(p.Pitch);
            writer.Write(p.RemainderX); writer.Write(p.RemainderZ); p.Combat.Write(writer);
        }
        return stream.ToArray();
    }

    public TacticalState Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 14) throw new InvalidDataException("状态头部被截断。");
        using var reader = new BinaryReader(new MemoryStream(bytes.ToArray(), false));
        if (reader.ReadUInt32() != 0x32434154U) throw new InvalidDataException("游戏状态版本不匹配。");
        ulong steps = reader.ReadUInt64(); int count = reader.ReadUInt16();
        if (count < 1 || count > 4 || bytes.Length != 14 + count * (22 + CombatState.ByteCount)) throw new InvalidDataException("状态长度不合法。");
        var players = new TacticalPlayer[count];
        for (int i=0; i<count; i++) players[i] = new TacticalPlayer(reader.ReadUInt16(), reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadUInt16(), reader.ReadInt16(), reader.ReadInt32(), reader.ReadInt32(), CombatState.Read(reader));
        return new TacticalState(steps, players);
    }

    private sealed partial class Model : IDeterministicSimulation<TacticalState>
    {
        private TacticalMap _map = null!;
        private TacticalPlayer[] _players = Array.Empty<TacticalPlayer>();
        private int _denominator;
        private uint _rate;
        private ulong _steps;

        public void Initialize(SimulationInitialization initialization)
        {
            _map = TacticalMap.Decode(initialization.Data.Span);
            if (initialization.Session.TickRate > 1000) throw new InvalidDataException("试玩最多支持 1000 Hz。");
            _rate = initialization.Session.TickRate;
            _denominator = checked((int)_rate * 1000);
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
            ulong tick = checked(_steps + 1);
            var commands = new TacticalInput[_players.Length];
            var active = new bool[_players.Length];
            foreach (var input in inputs)
            {
                int i = Array.FindIndex(_players, p => p.Slot == input.Participant.Slot);
                commands[i] = TacticalInput.Decode(input.Payload.Span); active[i] = true;
            }
            var combat = new CombatModel[_players.Length];
            for (int i=0; i<_players.Length; i++)
            {
                var p = _players[i]; var c = combat[i] = new CombatModel(p.Combat);
                if (!active[i]) continue;
                if (c.Health == 0)
                {
                    if (tick >= c.RespawnAtTick)
                    {
                        // 重生补满装备，战绩与射击序号保留，防止画面重复播放旧枪声。
                        c.Health=100; c.Ammo=30; c.Reserve=90; c.NextFireTick=0; c.ReloadUntilTick=0; c.RespawnAtTick=0;
                        _players[i] = Spawn(p.Slot); commands[i] = default;
                    }
                    continue; // 重生当帧也不消费旧的移动或开火意图。
                }
                if (c.ReloadUntilTick > 0 && tick >= c.ReloadUntilTick)
                { int amount=Math.Min(30-c.Ammo,c.Reserve); c.Ammo+=amount; c.Reserve-=amount; c.ReloadUntilTick=0; }
                var input = commands[i];
                if (input.Respawn) { _players[i] = Spawn(p.Slot); continue; } // 保留原控制台位置复位命令，不恢复生命/弹药。
                int speed = input.Sprint ? 7000 : 4500;
                long numeratorX = (long)input.MoveX * speed + p.RemainderX;
                long numeratorZ = (long)input.MoveZ * speed + p.RemainderZ;
                int dx=(int)(numeratorX/_denominator), dz=(int)(numeratorZ/_denominator);
                int x=Sweep(p.X,p.Z,dx,true), z=Sweep(p.Z,x,dz,false);
                _players[i] = new TacticalPlayer(p.Slot,x,z,input.HasLook?input.Yaw:p.Yaw,input.HasLook?input.Pitch:p.Pitch,
                    x==p.X+dx?(int)(numeratorX%_denominator):0,z==p.Z+dz?(int)(numeratorZ%_denominator):0);
            }
            ResolveCombat(tick, commands, active, combat);
            for (int i=0; i<_players.Length; i++)
            {
                var p=_players[i]; _players[i]=new TacticalPlayer(p.Slot,p.X,p.Z,p.Yaw,p.Pitch,p.RemainderX,p.RemainderZ,combat[i].Capture());
            }
            _steps=tick;
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
                var c=p.Combat;
                if (c.NextFireTick > state.Steps + Duration(100) || c.ReloadUntilTick > state.Steps + Duration(2400) ||
                    c.RespawnAtTick > state.Steps + Duration(3000) || (c.Health==0 && c.RespawnAtTick<=state.Steps) ||
                    (c.ReloadUntilTick>0 && c.ReloadUntilTick<=state.Steps)) throw new InvalidDataException("恢复的战斗计时不合法。");
                copy[i] = p;
            }
            _players = copy; _steps = state.Steps;
        }
    }
}
