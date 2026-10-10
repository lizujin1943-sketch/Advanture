using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FrameSync.Abstractions
{
    public sealed class Participant
    {
        public Participant(ushort slot, Guid participantId, ulong generation = 1)
        {
            if (participantId == Guid.Empty) throw new ArgumentException("Participant identity must be nonempty.");
            if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
            Slot = slot; ParticipantId = participantId; Generation = generation;
        }
        public ushort Slot { get; }
        public Guid ParticipantId { get; }
        public ulong Generation { get; }
    }

    /// <summary>Stage A uses a fixed roster, with no network connection identifiers.</summary>
    public sealed class SessionDescriptor
    {
        public SessionDescriptor(Guid sessionId, ulong epoch, uint tickRate,
            IReadOnlyList<Participant> participants, ulong baseTick = 0)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("Session identity must be nonempty.");
            if (epoch == 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (tickRate == 0 || tickRate > 1000) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (participants == null) throw new ArgumentNullException(nameof(participants));
            if (participants.Count < 1 || participants.Count > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(participants));
            var copy = new Participant[participants.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = participants[i] ?? throw new ArgumentException("Null participant.");
            Array.Sort(copy, (a, b) => a.Slot.CompareTo(b.Slot));
            if (copy.Select(p => p.Slot).Distinct().Count() != copy.Length ||
                copy.Select(p => p.ParticipantId).Distinct().Count() != copy.Length)
                throw new ArgumentException("Slots and participant identities must be unique.");
            SessionId = sessionId; Epoch = epoch; TickRate = tickRate; BaseTick = baseTick;
            Participants = new ReadOnlyCollection<Participant>(copy);
        }
        public Guid SessionId { get; }
        public ulong Epoch { get; }
        public uint TickRate { get; }
        public ulong BaseTick { get; }
        public IReadOnlyList<Participant> Participants { get; }
    }

    public sealed class SimulationInitialization
    {
        public SimulationInitialization(SessionDescriptor session, ByteString data)
        { Session = session; Data = data; }
        public SessionDescriptor Session { get; }
        public ByteString Data { get; }
    }
}
