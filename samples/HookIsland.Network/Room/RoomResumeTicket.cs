using System;

namespace Advanture.HookIsland.Network;

/// <summary>Opaque, application-local proof of a room seat claim; the host validates its current ownership.</summary>
public sealed class RoomResumeTicket
{
    private readonly byte[] token;
    internal RoomResumeTicket(Guid roomId,ushort slot,ReadOnlySpan<byte> token)
    {
        if(roomId==Guid.Empty || slot>3 || token.Length!=32)throw new ArgumentException("Invalid room resume ticket.");
        RoomId=roomId;Slot=slot;this.token=token.ToArray();
    }
    public Guid RoomId { get; }
    public ushort Slot { get; }
    internal ReadOnlySpan<byte> Token=>token;
}
