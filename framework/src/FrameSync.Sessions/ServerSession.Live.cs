using System;
using System.Collections.Generic;
using System.Linq;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Transport;

namespace FrameSync.Sessions
{
    public sealed partial class ServerSession<TState>
    {
        /// <summary>为已停用的预留席位接入经过宿主认证的新连接；完整同步后才在统一帧启用。</summary>
        /// <remarks>不会替宿主验证身份。仍活跃、已经绑定或不在名单中的席位返回 false；不改其他连接。若服务器仍有到期帧积压也暂时返回 false，宿主可在后续 Poll 追帧后重试。</remarks>
        public bool TryBind(PeerBinding binding)
        {
            EnsureNotBusy();
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (Status != SessionStatus.Running || _connections.ContainsKey(binding.ConnectionId)) return false;
            // A disconnected peer may have stalled flow control for seconds. Finish the server's
            // bounded catch-up first, or the new peer would exceed its window before receiving a snapshot.
            if (_hasTime && _lastTime >= Deadline(checked(ConfirmedTick + 1))) return false;
            int index = Array.FindIndex(_peers, p => p.Participant.Slot == binding.Slot);
            if (index < 0 || _peers[index].Connected) return false;
            var participant = _confirmed.Membership.Find(binding.Slot);
            if (participant.IsActive || participant.Generation == ulong.MaxValue) return false;
            var snapshot = CheckpointCodec.Encode(_confirmed);
            var peer = new Peer(binding.ConnectionId, participant)
            {
                Connected = true, RebindPending = true, Snapshot = snapshot, SnapshotLength = snapshot.Length,
                SnapshotHash = CheckpointCodec.Hash(_confirmed), ExecutedTick = ConfirmedTick, SentTick = ConfirmedTick,
                PrepareAt = _lastTime
            };
            _connections.Remove(_peers[index].ConnectionId);
            _peers[index] = peer; _connections.Add(peer.ConnectionId, peer);
            return true;
        }

        /// <summary>连接是否仍被会话持有；同步超限后为 false，宿主应关闭传输连接并允许显式重试。</summary>
        public bool IsConnected(ulong connectionId) => _connections.TryGetValue(connectionId, out var peer) && peer.Connected;

        private void SendSnapshot(Peer peer, ref int budget)
        {
            while (budget > 0 && peer.Offset < peer.SnapshotLength)
            {
                int length = Math.Min(_options.SnapshotChunkBytes, peer.SnapshotLength - peer.Offset);
                byte[] bytes = Wire.Snapshot(_runner.Session.SessionId, _runner.Session.Epoch, peer.Participant.Slot,
                    peer.Snapshot, peer.Offset, length);
                budget--;
                if (!_transport.TrySend(peer.ConnectionId, 3, DeliveryMode.ReliableOrdered, bytes)) break;
                peer.Offset += length;
            }
        }

        private void SendLiveBootstrap(ref int budget, long now)
        {
            foreach (var peer in _peers)
            {
                if (!peer.Connected || peer.Live) continue;
                if (now - peer.PrepareAt >= _options.PrepareTimeout.Ticks)
                { DisconnectPeer(peer); continue; }
                SendSnapshot(peer, ref budget);
                if (!peer.Ready) continue;
                if (!peer.StartSent)
                {
                    if (budget <= 0) continue;
                    budget--;
                    var start = Wire.HashMessage(MessageKind.Start, _runner.Session.SessionId, _runner.Session.Epoch, peer.SnapshotHash);
                    if (!_transport.TrySend(peer.ConnectionId, Wire.Channel(MessageKind.Start), DeliveryMode.ReliableOrdered, start)) continue;
                    peer.StartSent = true; peer.Snapshot = Array.Empty<byte>();
                }
                while (budget > 0 && peer.Catchup.Count > 0)
                {
                    var next = peer.Catchup.Peek();
                    if (next.Tick - peer.ExecutedTick > (ulong)peer.ReceiveWindow) break;
                    budget--;
                    if (!_transport.TrySend(peer.ConnectionId, Wire.Channel(MessageKind.Final), DeliveryMode.ReliableOrdered, next.Bytes)) break;
                    peer.Catchup.Dequeue(); peer.CatchupBytes -= next.Bytes.Length; peer.SentTick = next.Tick;
                }
                // Receiving bytes is not enough: the client must have executed and verified all catch-up frames.
                if (peer.Catchup.Count == 0 && peer.ExecutedTick == ConfirmedTick) peer.Live = true;
            }
        }

        private void BufferLiveFrame(ulong tick, byte[] bytes)
        {
            foreach (var peer in _peers)
            {
                if (!peer.Connected || peer.Live) continue;
                if (tick - peer.ExecutedTick > (ulong)_options.MaxFutureFrames ||
                    peer.Catchup.Count >= _options.MaxOutboxMessages || bytes.Length > _options.MaxOutboxBytes - peer.CatchupBytes)
                { DisconnectPeer(peer); continue; }
                peer.Catchup.Enqueue(new CatchupFrame(tick, bytes)); peer.CatchupBytes += bytes.Length;
            }
        }

        private void DisconnectPeer(Peer peer)
        {
            peer.Connected = false; peer.Live = false; peer.RebindPending = false;
            peer.Snapshot = Array.Empty<byte>(); peer.Catchup.Clear(); peer.CatchupBytes = 0;
            _outbox.RemoveDestination(peer.ConnectionId);
            foreach (var pending in _pending.Values) pending.Remove(peer.Participant.Slot);
        }

        private sealed class Peer
        {
            public Peer(ulong connection, Participant participant) { ConnectionId = connection; Participant = participant; }
            public readonly ulong ConnectionId;
            public readonly Participant Participant;
            public int Offset, SnapshotLength, ReceiveWindow, CatchupBytes;
            public ulong ExecutedTick, SentTick;
            public long PrepareAt;
            public bool Ready, TerminalQueued, Connected, Live, RebindPending, StartSent;
            public byte[] Snapshot = Array.Empty<byte>();
            public string SnapshotHash = string.Empty;
            public readonly Queue<CatchupFrame> Catchup = new Queue<CatchupFrame>();
        }
        private sealed class CatchupFrame
        {
            public CatchupFrame(ulong tick, byte[] bytes) { Tick = tick; Bytes = bytes; }
            public readonly ulong Tick;
            public readonly byte[] Bytes;
        }
    }
}
