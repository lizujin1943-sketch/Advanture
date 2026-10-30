using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Network;

/// <summary>One round transition: reliable notice delivery and acknowledgements from the fixed room roster.</summary>
internal sealed class RoomRestartBarrier
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly HashSet<ulong> peers;
    private readonly HashSet<ulong> sent = new HashSet<ulong>(), acknowledged = new HashSet<ulong>();
    private readonly TimeSpan started;
    internal RoomRestartBarrier(ulong epoch, IEnumerable<ulong> peers, TimeSpan started)
    {
        if (epoch < 2) throw new ArgumentOutOfRangeException(nameof(epoch));
        Epoch = epoch; this.peers = new HashSet<ulong>(peers); this.started = started;
        if (this.peers.Count < 1 || this.peers.Count > 4) throw new ArgumentException("Invalid room roster.", nameof(peers));
    }
    internal ulong Epoch { get; }
    internal bool Complete => acknowledged.Count == peers.Count;
    internal ulong[] PendingSends => peers.Where(p => !sent.Contains(p)).ToArray();
    internal void MarkSent(ulong peer) { if (peers.Contains(peer)) sent.Add(peer); }
    internal bool Acknowledge(ulong peer, ulong epoch)
    {
        if (epoch != Epoch || !sent.Contains(peer)) return false;
        acknowledged.Add(peer); return true;
    }
    internal bool Expired(TimeSpan now) => !Complete && now - started >= Timeout;
}
