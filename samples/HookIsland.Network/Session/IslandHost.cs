using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland.Simulation;
using Advanture.HookIsland.Network.Protocol;
using FrameSync.Abstractions;
using FrameSync.Sessions;
using FrameSync.Runtime;
using FrameSync.Persistence;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace Advanture.HookIsland.Network;

/// <summary>1～4 人房主。持有独立服务器模拟；房主本人也通过真实 UDP 客户端加入。</summary>
/// <remarks>仅限可信本机/局域网，所有调用串行进行。系统时间由宿主传入，未引用任何 Unity 类型。</remarks>
public sealed class IslandHost : IDisposable
{
    private readonly SocketDatagramPort _port;
    private readonly UdpNode _node;
    private readonly RoomTransport _transport;
    private readonly IslandMap _map;
    private readonly byte[] _hash;
    private readonly Guid _id = Guid.NewGuid();
    private readonly RoomSeatRegistry _seats;
    private readonly Dictionary<ulong, TimeSpan> _connected = new Dictionary<ulong, TimeSpan>();
    private readonly Dictionary<ulong, byte> _rejected = new Dictionary<ulong, byte>();
    private bool _disposed, _transportStopped;
    private readonly int _capacity;
    private Participant[] _roster = Array.Empty<Participant>();
    private RoomRestartBarrier? _restart;
    private TimeSpan _lastPoll;
    private Checkpoint? _initialCheckpoint;
    private ushort[] _restoreRequired=Array.Empty<ushort>();
    private Guid _saveId=Guid.NewGuid();

    public IslandHost(IslandMap map, int port = 7778, int capacity = 2)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        if (capacity < 1 || capacity > 4) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _seats=new RoomSeatRegistry(_id,capacity);
        _hash = RoomProtocol.Hash(map);
        _port = new SocketDatagramPort(new DatagramAddress(IPAddress.Any, port));
        try { _node = new UdpNode(_port); _node.Listen(); _transport = new RoomTransport(_node); }
        catch { _port.Dispose(); throw; }
    }
    public IslandHost(IslandSave save,int port=7778,byte[]? ownerKey=null):this(save?.Map??throw new ArgumentNullException(nameof(save)),port,save.Capacity)
    {
        try
        {
            _initialCheckpoint=CheckpointRestart.Create(save.Document.Checkpoint,_id);
            _restoreRequired=save.Document.Checkpoint.Membership.Participants.Where(p=>p.IsActive).Select(p=>p.Slot).ToArray();
            _saveId=save.Document.SaveId;_seats.Restore(save,ownerKey);
        }
        catch{Dispose();throw;}
    }
    public bool Restoring=>_initialCheckpoint!=null;
    public int RestoreRequiredCount=>_restoreRequired.Length;
    public bool CanRequestSave=>!_disposed && Failure==null && _restart==null && Session?.Status==SessionStatus.Running && _seats.Connected.Any(s=>s.Slot==0 && s.Entered);
    public bool CanSave=>CanRequestSave && IslandSaveSafety.IsSafe(Session!.InspectState());
    public IslandSave CaptureSave()
    {
        if(!CanSave)throw new InvalidOperationException("The room is not ready to save.");
        return new IslandSave(new SaveDocument(_saveId,Session!.CaptureCheckpoint()),DateTime.UtcNow,_seats.CaptureProfiles(),_seats.CaptureEntered());
    }
    public int Port => _port.LocalAddress.Port;
    public int JoinedCount => _seats.Connected.Count();
    public int Capacity => _capacity;
    public bool CanStart => !_disposed && Failure==null && Session==null && _seats.Connected.Any(s=>s.Slot==0 && s.Ready);
    public bool TryStart()
    {
        if(!CanStart)return false;
        _roster=_initialCheckpoint?.Session.Participants.ToArray()??Enumerable.Range(0,_capacity).Select(i=>new Participant((ushort)i,Guid.NewGuid(),isActive:false)).ToArray();
        CreateSession();return true;
    }
    public ServerSession<HookState>? Session { get; private set; }
    public string? Failure { get; private set; }
    public ulong RoundEpoch { get; private set; } = 1;
    public bool Restarting => _restart != null || RoundEpoch > 1 && Session?.Status == SessionStatus.Preparing;
    public bool CanRestart => !_disposed && Failure == null && _restart == null && RoundEpoch < ulong.MaxValue &&
        Session?.Status == SessionStatus.Running && _seats.Connected.Any(s=>s.Slot==0 && s.Entered) &&
        _seats.Connected.All(s=>s.Bound && Session.CaptureCheckpoint().Membership.Find(s.Slot).IsActive) &&
        Session.InspectState().RoundFinished;

    /// <summary>Only the room owner has this capability. Keep connections/seats; replace the finished simulation after every peer acknowledges.</summary>
    public bool TryRestart()
    {
        if (!CanRestart) return false;
        _restart = new RoomRestartBarrier(RoundEpoch + 1, _seats.Connected.Select(s=>s.Connection!.Value), _lastPoll);
        return true;
    }

    /// <summary>人数到齐并确认欢迎信息才创建会话，等待好友不会消耗开局超时。</summary>
    public void Poll(TimeSpan now)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(IslandHost));
        if (Failure != null) return;
        _lastPoll = now;
        if (_restart != null) _transport.DiscardSessionMessages();
        _node.Poll(now); _transport.Pump();
        while (_node.TryGetEvent(out var e))
        {
            if (e!.Kind == ConnectionEventKind.Connected) _connected[e.ConnectionId] = now;
            else if(!Disconnect(e.ConnectionId,e.Reason))return;
        }
        // CloseAck can wait for transport allowance. Stop frame delivery as soon as the
        // peer stops accepting application messages, before its final resource-release event.
        foreach(var connection in _connected.Keys.ToArray())
            if(!_node.IsConnected(connection) && !Disconnect(connection,"Transport closing"))return;
        while (_transport.Controls.Count > 0)
        {
            var message = _transport.Controls.Dequeue();
            if (!_connected.ContainsKey(message.ConnectionId)) continue;
            if (!RoomProtocol.TryDecode(message.Payload, out var packet, out byte rejection))
            { _rejected[message.ConnectionId] = rejection; continue; }
            if (packet!.BodyCase == RoomPacket.BodyOneofCase.Join)
            {
                if (!packet.Join.MapHash.Span.SequenceEqual(_hash)) { _rejected[message.ConnectionId] = 1; continue; }
                var room=packet.Join.RoomId.IsEmpty?Guid.Empty:new Guid(packet.Join.RoomId.Span);
                if(_restart!=null || !_seats.TryClaim(message.ConnectionId,(ushort)packet.Join.Slot,room,packet.Join.ResumeToken.Span,
                    Session==null || !Session.InspectState().RoundFinished,out _,packet.Join.PlayerKey.Span))
                    _rejected[message.ConnectionId]=2;
            }
            else if (packet.BodyCase == RoomPacket.BodyOneofCase.Ack && _seats.Find(message.ConnectionId)?.Welcomed==true)
                _seats.Find(message.ConnectionId)!.Ready=true;
            else if (packet.BodyCase == RoomPacket.BodyOneofCase.RestartAck && _seats.Find(message.ConnectionId)!=null &&
                new Guid(packet.RestartAck.SessionId.Span) == _id)
            {
                // Late acknowledgements from an already committed epoch are harmless.
                if (packet.RestartAck.Epoch > RoundEpoch &&
                    !(_restart?.Acknowledge(message.ConnectionId, packet.RestartAck.Epoch) ?? false))
                    _rejected[message.ConnectionId] = 3;
            }
            else _rejected[message.ConnectionId] = 3;
        }
        foreach (var pair in _rejected.ToArray())
            if (RoomProtocol.Send(_node, pair.Key, RoomProtocol.RejectBytes(pair.Value))) _rejected.Remove(pair.Key);
        // 未确认 Welcome 的候选席位也有期限；准备完成后等待房主开局不限时。
        foreach (var pair in _connected.ToArray())
        {
            var seat=_seats.Find(pair.Key);
            if((seat==null || !seat.Ready || Session!=null && !seat.Bound) && now-pair.Value>TimeSpan.FromSeconds(10))
                CloseCandidate(pair.Key,"Room handshake expired");
        }
        foreach(var seat in _seats.Connected)
            if(!seat.Welcomed && RoomProtocol.Send(_node,seat.Connection!.Value,RoomProtocol.WelcomeBytes(_id,seat.Slot,_hash,RoundEpoch,seat.Token)))seat.Welcomed=true;
        if(Session==null && _seats.Connected.All(s=>s.Ready) &&
            (Restoring?_seats.Connected.Any(s=>s.Slot==0) && _restoreRequired.All(slot=>_seats.Connected.Any(s=>s.Slot==slot)):
                JoinedCount==_capacity))TryStart();
        if (_restart != null)
        {
            _transport.DiscardSessionMessages();
            foreach (ulong peer in _restart.PendingSends)
                if (RoomProtocol.Send(_node, peer, RoomProtocol.RestartBytes(_id, _restart.Epoch, _hash))) _restart.MarkSent(peer);
            if (_restart.Complete)
            {
                RoundEpoch = _restart.Epoch; _restart = null;
                CreateSession();
            }
            else if (_restart.Expired(now)) { Fail("重新开始失败：等待队友确认超时。"); return; }
        }
        if (_restart == null)
        {
            if(Session?.Status==SessionStatus.Running)
                foreach(var seat in _seats.Connected.Where(s=>s.Ready && !s.Bound).ToArray())
                {
                    if(!seat.Entered && Session.InspectState().RoundFinished){CloseCandidate(seat.Connection!.Value,"Round has ended");continue;}
                    seat.Bound=Session.TryBind(new PeerBinding(seat.Connection!.Value,seat.Slot));
                }
            Session?.Poll(now);
            if(Session?.Status==SessionStatus.Running)
            {
                var checkpoint=Session.CaptureCheckpoint();var finished=Session.InspectState().RoundFinished;
                foreach(var seat in _seats.Connected.Where(s=>s.Bound).ToArray())
                {
                    if(checkpoint.Membership.Find(seat.Slot).IsActive)seat.Entered=true;
                    if(!Session.IsConnected(seat.Connection!.Value) || !seat.Entered && finished)
                        CloseCandidate(seat.Connection.Value,"Synchronization expired");
                }
            }
        }
        if (Session?.Status == SessionStatus.Failed) Fail("服务器会话失败：" + Session.Failure);
        if (Failure == null) _node.Poll(now);
    }

    private void CreateSession()
    {
        bool friendlyPranks=Session?.InspectState().RoomRules.FriendlyPranksEnabled??true;
        var ready=_seats.Connected.Where(s=>s.Ready).ToArray();
        Session = new ServerSession<HookState>(HookGame.CreateModule(friendlyPranks), _initialCheckpoint?.Session??new SessionDescriptor(_id, RoundEpoch, RoomProtocol.Rate, _roster), _transport,
            ready.Select(s => new PeerBinding(s.Connection!.Value, s.Slot)).ToArray(), _ => RoomProtocol.Neutral, RoomProtocol.Limits, new ByteString(_map.Encode()),initialCheckpoint:_initialCheckpoint,allowUnboundParticipants:true);
        _initialCheckpoint=null;_restoreRequired=Array.Empty<ushort>();
        foreach(var seat in ready)seat.Bound=true;
    }

    private bool Disconnect(ulong connection,string reason)
    {
        _connected.Remove(connection);_rejected.Remove(connection);
        var seat=_seats.Find(connection);if(seat==null)return true;
        if(_restart!=null || seat.Bound && Session?.Status==SessionStatus.Preparing)
        {Fail("开始新一局时玩家断开连接："+reason);return false;}
        if(Session?.IsConnected(connection)==true)Session.Deactivate(connection);
        _seats.Release(connection);return true;
    }

    private void CloseCandidate(ulong connection,string reason)
    {
        if(Session?.IsConnected(connection)==true)Session.Deactivate(connection);
        _seats.Release(connection);_node.Close(connection,reason);
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
