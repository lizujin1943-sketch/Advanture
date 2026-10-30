using System;
using System.Collections.Generic;
using System.Net;
using Advanture.HookIsland.Simulation;
using Advanture.HookIsland.Network.Protocol;
using FrameSync.Sessions;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace Advanture.HookIsland.Network;

/// <summary>试玩客户端宿主：握手、固定帧采样、输入冻结、预测和连接清理。独立于引擎。</summary>
public sealed class IslandClient : IDisposable
{
    private readonly SocketDatagramPort _port;
    private readonly UdpNode _node;
    private readonly RoomTransport _transport;
    private readonly ulong _peer;
    private readonly ushort _requestedSlot;
    private ushort _slot = ushort.MaxValue;
    private readonly byte[] _hash;
    private bool _connected, _joined, _acked, _disposed, _checkedInitial, _transportStopped;
    private FixedInputScheduler? _scheduler;
    private TimeSpan? _clockOrigin;
    private ulong _nextTick = 1;
    private byte[]? _pending;
    private Guid _roomId;
    private bool _restartAckPending;
    private readonly RoomResumeTicket? _resumeRequest;
    private RoomResumeTicket? _candidateTicket;
    private readonly byte[] _playerKey;
    private readonly ConfirmedGameplayEventBatch _confirmedEvents=new ConfirmedGameplayEventBatch();

    public IslandClient(IslandMap map, IPAddress host, int port, ushort slot, RoomResumeTicket? ticket = null,byte[]? playerKey=null)
    {
        if (slot != 0 && slot != ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(slot));
        if(playerKey!=null && playerKey.Length!=32)throw new ArgumentException("Invalid local player identity.",nameof(playerKey));
        _playerKey=playerKey==null?IslandPlayerIdentity.Create():(byte[])playerKey.Clone();
        _hash = RoomProtocol.Hash(map); _requestedSlot = ticket?.Slot??slot; _resumeRequest=ticket;
        _port = new SocketDatagramPort(new DatagramAddress(host.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0));
        try { _node = new UdpNode(_port); _peer = _node.Connect(new DatagramAddress(host, port)); _transport = new RoomTransport(_node); }
        catch { _port.Dispose(); throw; }
    }
    public ClientSession<HookState>? Session { get; private set; }
    /// <summary>Ordered confirmed results produced by this Poll only. Consume once after Poll;
    /// the next Poll clears this batch even when disconnected. No speculative events are exposed.</summary>
    public IReadOnlyList<ConfirmedGameplayEvent> ConfirmedEvents => _confirmedEvents.Values;
    public string? Failure { get; private set; }
    public ulong LastSubmittedTick { get; private set; }
    public ushort Slot => _slot;
    public ulong RoundEpoch { get; private set; }
    public RoomResumeTicket? ResumeTicket { get; private set; }
    /// <summary>Proof retained as soon as Welcome arrives, including an unconfirmed first entry.
    /// Only the host decides whether the claim is still valid; this does not grant input control.</summary>
    public RoomResumeTicket? RecoveryTicket => ResumeTicket??_candidateTicket??_resumeRequest;
    public bool Restarting => RoundEpoch > 1 && Session?.Status != SessionStatus.Running && Failure == null;
    /// <summary>Raised once per new round, after replacing the session and before any new input is sampled. Clear local pending actions here.</summary>
    public event Action? RoundChanged;
    /// <summary>本地绑定端口，供连接诊断；结束连接后仍保留该编号。</summary>
    public int LocalPort => _port.LocalAddress.Port;

    /// <summary>每次渲染循环调用。sample 只为尚未冻结的逻辑帧调用；无操作也必须返回 default。</summary>
    public void Poll(TimeSpan now, Func<ulong, HookInput> sample)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(IslandClient));
        if (sample == null) throw new ArgumentNullException(nameof(sample));
        _confirmedEvents.BeginPoll();
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
        if (_connected && !_joined) _joined = RoomProtocol.Send(_node, _peer, RoomProtocol.JoinBytes(_requestedSlot, _hash,_resumeRequest,_playerKey));
        while (_transport.Controls.Count > 0)
        {
            var m = _transport.Controls.Dequeue();
            if (m.ConnectionId != _peer) continue;
            if (!RoomProtocol.TryDecode(m.Payload, out var packet, out byte rejection))
            { Fail(rejection == 1 ? "房间协议版本不同，请使用同一份试玩。" : "收到不合法的房间消息。"); return; }
            if (packet!.BodyCase == RoomPacket.BodyOneofCase.Welcome && packet.Welcome.Slot < 4 &&
                (_requestedSlot == ushort.MaxValue ? packet.Welcome.Slot > 0 : packet.Welcome.Slot == _requestedSlot) &&
                packet.Welcome.MapHash.Span.SequenceEqual(_hash))
            {
                var id = new Guid(packet.Welcome.SessionId.Span);
                if (_resumeRequest!=null && (_resumeRequest.RoomId!=id || !_resumeRequest.Token.SequenceEqual(packet.Welcome.ResumeToken.Span)))
                { Fail("恢复的房间或席位身份不匹配。"); return; }
                if(Session==null)
                {
                    _slot = (ushort)packet.Welcome.Slot; _roomId = id; RoundEpoch = packet.Welcome.Epoch;
                    _candidateTicket=new RoomResumeTicket(id,_slot,packet.Welcome.ResumeToken.Span);CreateSession();
                }
                else if(id!=_roomId || packet.Welcome.Epoch!=RoundEpoch || packet.Welcome.Slot!=_slot ||
                    !_candidateTicket!.Token.SequenceEqual(packet.Welcome.ResumeToken.Span))
                {Fail("重复的房间信息不一致。");return;}
            }
            else if (packet.BodyCase == RoomPacket.BodyOneofCase.RestartRound && Session != null &&
                new Guid(packet.RestartRound.SessionId.Span) == _roomId && packet.RestartRound.MapHash.Span.SequenceEqual(_hash))
            {
                ulong epoch = packet.RestartRound.Epoch;
                if (epoch < RoundEpoch) continue;
                if (epoch != RoundEpoch)
                {
                    if (RoundEpoch == ulong.MaxValue || epoch != RoundEpoch + 1)
                    { Fail("重新开始的局号不连续。"); return; }
                    // The server cannot send the new snapshot until our acknowledgement. Only discard here, never on a duplicate notice.
                    _transport.DiscardSessionMessages();
                    RoundEpoch = epoch; _scheduler = null; _nextTick = 1; _pending = null;
                    LastSubmittedTick = 0; _checkedInitial = false;
                    CreateSession(); RoundChanged?.Invoke();
                }
                _restartAckPending = true;
            }
            else if (packet.BodyCase == RoomPacket.BodyOneofCase.Reject)
            { Fail(packet.Reject.Reason == RejectReason.Incompatible ? "地图或协议版本不同，请使用同一份试玩。" : "席位不可用、恢复凭据不符或房间当前不能接入。"); return; }
            else { Fail("收到不合法的房间消息。"); return; }
        }
        if (Session != null)
        {
            if (!_acked) _acked = RoomProtocol.Send(_node, _peer, RoomProtocol.AckBytes());
            if (_restartAckPending && RoomProtocol.Send(_node, _peer, RoomProtocol.RestartAckBytes(_roomId, RoundEpoch))) _restartAckPending = false;
            _confirmedEvents.Add(Session.Poll()); // 先收确认帧/初态；之前预测过的帧已经有冻结的本地输入。
            if (!_checkedInitial && Session.LocalParticipant != null)
            {
                if (Session.LocalParticipant.Slot != _slot || !RoomProtocol.Hash(IslandMap.Decode(Session.CaptureCheckpoint().InitializationData.Span)).AsSpan().SequenceEqual(_hash))
                { Fail("服务器下发的初始地图或角色不匹配。"); return; }
                _checkedInitial = true;
            }
            if (Session.Status == SessionStatus.Running && Session.LocalParticipant?.IsActive==true && !Session.IsClosing)
            {
                ResumeTicket=_candidateTicket;
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
                _confirmedEvents.Add(Session.Poll(horizon));
            }
            if (Session.Status == SessionStatus.Failed) Fail("同步失败：" + Session.Failure);
            else if (Session.Status == SessionStatus.Closed) Fail("房主已结束房间。");
        }
        if (Failure == null) _node.Poll(now);
    }
    private void CreateSession()
    {
        _confirmedEvents.Reset(_roomId,RoundEpoch);
        Session = new ClientSession<HookState>(HookGame.CreateModule(), _roomId, RoundEpoch, _transport, _peer, RoomProtocol.Limits,
            prediction: new PredictionOptions(maxPredictionFrames: 12), neutralInput: _ => RoomProtocol.Neutral,allowLiveBootstrap:true);
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
