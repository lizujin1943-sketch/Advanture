using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.Tactical.Simulation;
using Advanture.Tactical.Network.Protocol;
using FrameSync.Abstractions;
using FrameSync.Sessions;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace Advanture.Tactical.Network;

/// <summary>战术地图房主。房间容量受地图出生点数量限制，房主也占一个席位。</summary>
/// <remarks>仅限可信本机/局域网，所有调用串行进行。系统时间由宿主传入，未引用任何 Unity 类型。</remarks>
public sealed class TacticalHost : IDisposable
{
    private readonly SocketDatagramPort _port;
    private readonly UdpNode _node;
    private readonly RoomTransport _transport;
    private readonly TacticalMap _map;
    private readonly byte[] _hash;
    private readonly Guid _id = Guid.NewGuid();
    private readonly Dictionary<ushort, ulong> _peers = new Dictionary<ushort, ulong>();
    private readonly Dictionary<ulong, TimeSpan> _connected = new Dictionary<ulong, TimeSpan>();
    private readonly HashSet<ulong> _welcomed = new HashSet<ulong>(), _ready = new HashSet<ulong>();
    private readonly Dictionary<ulong, byte> _rejected = new Dictionary<ulong, byte>();
    private bool _disposed, _transportStopped;

    public TacticalHost(TacticalMap map, int port = 7777) : this(map, port, map?.Spawns.Count ?? 0) { }

    public TacticalHost(TacticalMap map, int port, int participants)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        if (participants < 1 || participants > map.Spawns.Count || participants > RoomProtocol.Limits.MaxParticipants)
            throw new ArgumentOutOfRangeException(nameof(participants));
        Capacity = participants;
        _hash = RoomProtocol.Hash(map);
        _port = new SocketDatagramPort(new DatagramAddress(participants == 1 ? IPAddress.Loopback : IPAddress.Any, port));
        try { _node = new UdpNode(_port); _node.Listen(); _transport = new RoomTransport(_node); }
        catch { _port.Dispose(); throw; }
    }
    public int Port => _port.LocalAddress.Port;
    public int Capacity { get; }
    public int JoinedCount => _peers.Count;
    public bool CanStart => !_disposed && Failure == null && Session == null && _peers.ContainsKey(0) &&
        JoinedCount >= Math.Min(2, Capacity) && _ready.Count == JoinedCount;
    public ServerSession<TacticalState>? Session { get; private set; }
    public string? Failure { get; private set; }

    /// <summary>房主提前开局时只为已经就绪的玩家创建角色，保留他们在大厅分配到的席位。</summary>
    public bool TryStart()
    {
        if (!CanStart) return false;
        var roster = _peers.Keys.OrderBy(slot => slot).Select(slot => new Participant(slot, Guid.NewGuid(), isActive: false)).ToArray();
        Session = new ServerSession<TacticalState>(TacticalGame.CreateModule(_map), new SessionDescriptor(_id, 1, RoomProtocol.Rate, roster), _transport,
            _peers.Select(p => new PeerBinding(p.Value, p.Key)).ToArray(), _ => RoomProtocol.Neutral, RoomProtocol.Limits, new ByteString(_map.Encode()));
        return true;
    }

    private bool TrySeat(ulong connection, ushort requested)
    {
        foreach (var seat in _peers)
            if (seat.Value == connection)
                return requested == seat.Key || (requested == RoomProtocol.AnyGuest && seat.Key > 0);
        if (Session != null) return false;

        ushort slot = requested;
        if (slot == RoomProtocol.AnyGuest)
        {
            slot = 1;
            while (slot < Capacity && _peers.ContainsKey(slot)) slot++;
        }
        if (slot >= Capacity || _peers.ContainsKey(slot)) return false;
        _peers.Add(slot, connection);
        return true;
    }

    /// <summary>握手就绪后满员自动开局，等待好友期间不消耗会话准备时间。</summary>
    public void Poll(TimeSpan now)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TacticalHost));
        if (Failure != null) return;
        _node.Poll(now); _transport.Pump();
        while (_node.TryGetEvent(out var e))
        {
            if (e!.Kind == ConnectionEventKind.Connected) _connected[e.ConnectionId] = now;
            else
            {
                _connected.Remove(e.ConnectionId); _rejected.Remove(e.ConnectionId);
                var occupied = _peers.Where(p => p.Value == e.ConnectionId).ToArray();
                if (occupied.Length == 0) continue;
                if (Session != null) { Fail("玩家已断开连接，房间结束：" + e.Reason); return; }
                foreach (var p in occupied) _peers.Remove(p.Key);
                _welcomed.Remove(e.ConnectionId); _ready.Remove(e.ConnectionId);
            }
        }
        while (_transport.Controls.Count > 0)
        {
            var message = _transport.Controls.Dequeue();
            if (!_connected.ContainsKey(message.ConnectionId)) continue;
            if (!RoomProtocol.TryDecode(message.Payload, out var packet, out byte rejection))
            { _rejected[message.ConnectionId] = rejection; continue; }
            if (packet!.BodyCase == RoomPacket.BodyOneofCase.Join)
            {
                ushort slot = (ushort)packet.Join.Slot;
                if (!packet.Join.MapHash.Span.SequenceEqual(_hash)) { _rejected[message.ConnectionId] = 1; continue; }
                if (!TrySeat(message.ConnectionId, slot)) _rejected[message.ConnectionId] = 2;
            }
            else if (packet.BodyCase == RoomPacket.BodyOneofCase.Ack && _welcomed.Contains(message.ConnectionId)) _ready.Add(message.ConnectionId);
            else _rejected[message.ConnectionId] = 3;
        }
        foreach (var pair in _rejected.ToArray())
            if (RoomProtocol.Send(_node, pair.Key, RoomProtocol.RejectBytes(pair.Value))) _rejected.Remove(pair.Key);
        // 已入房的玩家可以一直等待队友，未完成入房握手的连接会超时。
        foreach (var pair in _connected)
            if (!_peers.ContainsValue(pair.Key) && now - pair.Value > TimeSpan.FromSeconds(10)) _node.Close(pair.Key, "Room handshake expired");
        if (Session == null)
        {
            foreach (var pair in _peers)
                if (!_welcomed.Contains(pair.Value) && RoomProtocol.Send(_node, pair.Value, RoomProtocol.WelcomeBytes(_id, pair.Key, _hash))) _welcomed.Add(pair.Value);
            if (JoinedCount == Capacity) TryStart();
        }
        Session?.Poll(now);
        if (Session?.Status == SessionStatus.Failed) Fail("服务器会话失败：" + Session.Failure);
        if (Failure == null) _node.Poll(now);
    }

    private void Fail(string reason)
    { Failure = reason; StopTransport(); }

    /// <summary>停止网络并释放端口，保留 Session/Failure 供界面和日志读取。失败与主动离开共用此路径。</summary>
    private void StopTransport()
    {
        if (_transportStopped) return;
        _transportStopped = true;
        try { _node.Shutdown(); } finally { _port.Dispose(); }
    }

    /// <summary>释放监听端口。退出和重新开房各调用一次即可；重复释放安全。</summary>
    public void Dispose()
    { if (_disposed) return; _disposed = true; StopTransport(); }
}
