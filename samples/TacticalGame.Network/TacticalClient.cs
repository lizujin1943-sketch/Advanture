using System;
using System.Net;
using Advanture.Tactical.Simulation;
using Advanture.Tactical.Network.Protocol;
using FrameSync.Sessions;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace Advanture.Tactical.Network;

/// <summary>试玩客户端宿主：握手、固定帧采样、输入冻结、预测和连接清理。独立于引擎。</summary>
public sealed class TacticalClient : IDisposable
{
    private readonly SocketDatagramPort _port;
    private readonly UdpNode _node;
    private readonly RoomTransport _transport;
    private readonly ulong _peer;
    private readonly ushort _slot;
    private readonly byte[] _hash;
    private bool _connected, _joined, _acked, _disposed, _checkedInitial, _transportStopped;
    private FixedInputScheduler? _scheduler;
    private TimeSpan? _clockOrigin;
    private ulong _nextTick = 1;
    private byte[]? _pending;

    public TacticalClient(TacticalMap map, IPAddress host, int port, ushort slot)
    {
        if (slot > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        _hash = RoomProtocol.Hash(map); _slot = slot;
        _port = new SocketDatagramPort(new DatagramAddress(host.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0));
        try { _node = new UdpNode(_port); _peer = _node.Connect(new DatagramAddress(host, port)); _transport = new RoomTransport(_node); }
        catch { _port.Dispose(); throw; }
    }
    public ClientSession<TacticalState>? Session { get; private set; }
    public string? Failure { get; private set; }
    public ulong LastSubmittedTick { get; private set; }
    /// <summary>本地绑定端口，供连接诊断；结束连接后仍保留该编号。</summary>
    public int LocalPort => _port.LocalAddress.Port;

    /// <summary>每次渲染循环调用。sample 只为尚未冻结的逻辑帧调用；无操作也必须返回 default。</summary>
    public void Poll(TimeSpan now, Func<ulong, TacticalInput> sample)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TacticalClient));
        if (sample == null) throw new ArgumentNullException(nameof(sample));
        if (Failure != null) return;
        // 每个连接使用自己的经过时间。新客户端可能在程序运行很久以后才创建，
        // 不能把应用已运行的时间误算成 UDP 握手等待时间。
        if (!_clockOrigin.HasValue) _clockOrigin = now;
        now -= _clockOrigin.Value;
        _node.Poll(now); _transport.Pump();
        while (_node.TryGetEvent(out var e))
        {
            if (e!.ConnectionId != _peer) continue;
            if (e.Kind == ConnectionEventKind.Connected) _connected = true;
            else { Fail("与房主连接已断开：" + e.Reason); return; }
        }
        if (_connected && !_joined) _joined = RoomProtocol.Send(_node, _peer, RoomProtocol.JoinBytes(_slot, _hash));
        while (_transport.Controls.Count > 0)
        {
            var m = _transport.Controls.Dequeue();
            if (m.ConnectionId != _peer) continue;
            if (!RoomProtocol.TryDecode(m.Payload, out var packet, out byte rejection))
            { Fail(rejection == 1 ? "房间协议版本不同，请使用同一份试玩。" : "收到不合法的房间消息。"); return; }
            if (packet!.BodyCase == RoomPacket.BodyOneofCase.Welcome && packet.Welcome.Slot == _slot &&
                packet.Welcome.MapHash.Span.SequenceEqual(_hash) && Session == null)
            {
                var id = new Guid(packet.Welcome.SessionId.Span);
                if (id == Guid.Empty) { Fail("房间身份不合法。"); return; }
                Session = new ClientSession<TacticalState>(TacticalGame.CreateModule(), id, 1, _transport, _peer, RoomProtocol.Limits,
                    prediction: new PredictionOptions(maxPredictionFrames: 12), neutralInput: _ => RoomProtocol.Neutral);
            }
            else if (packet.BodyCase == RoomPacket.BodyOneofCase.Reject)
            { Fail(packet.Reject.Reason == RejectReason.Incompatible ? "地图或协议版本不同，请使用同一份试玩。" : "房间已满、已经开局或请求不合法。"); return; }
            else { Fail("收到不合法的房间消息。"); return; }
        }
        if (Session != null)
        {
            if (!_acked) _acked = RoomProtocol.Send(_node, _peer, RoomProtocol.AckBytes());
            Session.Poll(); // 先收确认帧/初态；之前预测过的帧已经有冻结的本地输入。
            if (!_checkedInitial && Session.LocalParticipant != null)
            {
                if (Session.LocalParticipant.Slot != _slot || !RoomProtocol.Hash(TacticalMap.Decode(Session.CaptureCheckpoint().InitializationData.Span)).AsSpan().SequenceEqual(_hash))
                { Fail("服务器下发的初始地图或角色不匹配。"); return; }
                _checkedInitial = true;
            }
            if (Session.Status == SessionStatus.Running && !Session.IsClosing)
            {
                if (_scheduler == null) { _scheduler = new FixedInputScheduler(RoomProtocol.Rate); _scheduler.Start(now, Session.ConfirmedTick); }
                ulong horizon = Math.Min(Session.ConfirmedTick + 128, _scheduler.InputTick(now, Session.ConfirmedTick));
                if (_nextTick <= Session.ConfirmedTick) { _nextTick = Session.ConfirmedTick + 1; _pending = null; }
                for (int budget = 0; _nextTick <= horizon && budget < 128; budget++)
                {
                    // 背压时保留相同字节重试，不能再次采样鼠标、重复消费重生按钮或改写已冻结输入。
                    if (_pending == null) _pending = sample(_nextTick).Encode();
                    if (!Session.TrySubmit(_nextTick, _pending)) break;
                    LastSubmittedTick = _nextTick++; _pending = null;
                }
                // 此时本地帧都已明确冻结，框架仅预测其他玩家；缺少本地帧时核心自己暂停。
                Session.Poll(horizon);
            }
            if (Session.Status == SessionStatus.Failed) Fail("同步失败：" + Session.Failure);
            else if (Session.Status == SessionStatus.Closed) Fail("房主已结束房间。");
        }
        if (Failure == null) _node.Poll(now);
    }
    private void Fail(string reason) { Failure = reason; StopTransport(); }
    /// <summary>自然掉线和主动离开都立即归还 Socket；后续仍可查看最终状态或重复 Dispose。</summary>
    private void StopTransport()
    {
        if (_transportStopped) return;
        _transportStopped = true;
        try { _node.Shutdown(); } finally { _port.Dispose(); }
    }
    public void Dispose()
    { if (_disposed) return; _disposed = true; StopTransport(); }
}
