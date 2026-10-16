using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.Tactical.Simulation;
using FrameSync.Abstractions;
using FrameSync.Sessions;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace Advanture.Tactical.Network;

/// <summary>双人试玩房主。持有独立服务器模拟；房主本人也必须通过 TacticalClient 加入。</summary>
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

    public TacticalHost(TacticalMap map, int port = 7777)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        if (map.Spawns.Count != 2) throw new ArgumentException("试玩房间需要两个出生点。", nameof(map));
        _hash = RoomProtocol.Hash(map);
        _port = new SocketDatagramPort(new DatagramAddress(IPAddress.Any, port));
        try { _node = new UdpNode(_port); _node.Listen(); _transport = new RoomTransport(_node); }
        catch { _port.Dispose(); throw; }
    }
    public int Port => _port.LocalAddress.Port;
    public int JoinedCount => _peers.Count;
    public ServerSession<TacticalState>? Session { get; private set; }
    public string? Failure { get; private set; }

    /// <summary>驱动网络和服务器。两人都确认欢迎信息后才创建会话，因此等待好友不会消耗开局超时。</summary>
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
            var message = _transport.Controls.Dequeue(); var b = message.Payload;
            if (!_connected.ContainsKey(message.ConnectionId)) continue;
            if (b.Length == 35 && b[0] == RoomProtocol.Join)
            {
                ushort slot = b[2];
                if (b[1] != RoomProtocol.Version || !b.Slice(3).SequenceEqual(_hash)) { _rejected[message.ConnectionId] = 1; continue; }
                if (Session != null || slot > 1 || (_peers.TryGetValue(slot, out var old) && old != message.ConnectionId) ||
                    _peers.Any(p => p.Key != slot && p.Value == message.ConnectionId)) { _rejected[message.ConnectionId] = 2; continue; }
                _peers[slot] = message.ConnectionId;
            }
            else if (b.Length == 1 && b[0] == RoomProtocol.Ack && _welcomed.Contains(message.ConnectionId)) _ready.Add(message.ConnectionId);
            else _rejected[message.ConnectionId] = 3;
        }
        foreach (var pair in _rejected.ToArray())
            if (RoomProtocol.Send(_node, pair.Key, new[] { RoomProtocol.Reject, pair.Value })) _rejected.Remove(pair.Key);
        // 未通过房间握手的连接不能无限占用 UDP 名额；已入房等待另一人则允许持续等待。
        foreach (var pair in _connected)
            if (!_peers.ContainsValue(pair.Key) && now - pair.Value > TimeSpan.FromSeconds(10)) _node.Close(pair.Key, "Room handshake expired");
        if (Session == null)
        {
            foreach (var pair in _peers)
                if (!_welcomed.Contains(pair.Value) && RoomProtocol.Send(_node, pair.Value, RoomProtocol.WelcomeBytes(_id, pair.Key, _hash))) _welcomed.Add(pair.Value);
            if (_peers.Count == 2 && _ready.Count == 2)
            {
                var roster = new[] { new Participant(0, Guid.NewGuid(), isActive: false), new Participant(1, Guid.NewGuid(), isActive: false) };
                Session = new ServerSession<TacticalState>(TacticalGame.CreateModule(), new SessionDescriptor(_id, 1, RoomProtocol.Rate, roster), _transport,
                    _peers.Select(p => new PeerBinding(p.Value, p.Key)).ToArray(), _ => RoomProtocol.Neutral, RoomProtocol.Limits, new ByteString(_map.Encode()));
            }
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
