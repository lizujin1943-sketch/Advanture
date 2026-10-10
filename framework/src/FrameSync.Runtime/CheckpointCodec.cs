using System;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    /// <summary>Stage A checkpoint format: little endian, versioned, canonical, and SHA-256 checked.</summary>
    public static class CheckpointCodec
    {
        private const uint Magic = 0x50435346; // FSCP
        public static byte[] Encode(Checkpoint checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            using var w = new CanonicalWriter(checkpoint.Limits.MaxCheckpointBytes - DataEnvelope.DigestLength);
            w.U32(Magic); w.U16(1);
            var m = checkpoint.Manifest;
            w.Text(m.ApplicationId); w.Text(m.RulesVersion); w.Text(m.InputFormatVersion);
            w.Text(m.StateFormatVersion); w.Text(m.ContentHash); w.Text(m.DeterminismHash);
            var s = checkpoint.Session;
            w.Guid(s.SessionId); w.U64(s.Epoch); w.U32(s.TickRate); w.U64(s.BaseTick); w.U64(checkpoint.Tick);
            WriteLimits(w, checkpoint.Limits);
            w.U16((ushort)s.Participants.Count);
            foreach (var p in s.Participants) { w.U16(p.Slot); w.Guid(p.ParticipantId); w.U64(p.Generation); }
            foreach (ulong count in checkpoint.MissingInputCounts) w.U64(count);
            w.U32((uint)checkpoint.InputHistory.Count);
            foreach (var frame in checkpoint.InputHistory) InputFormat.Write(w, frame);
            w.Blob(checkpoint.InitializationData.Span);
            w.Blob(checkpoint.ApplicationState.Span);
            return DataEnvelope.Seal(w.ToArray());
        }

        public static Checkpoint Decode(ReadOnlySpan<byte> bytes, RuntimeLimits? limits = null)
        {
            var cap = limits ?? new RuntimeLimits();
            var r = new CanonicalReader(DataEnvelope.Open(bytes, cap.MaxCheckpointBytes));
            try
            {
                if (r.U32() != Magic || r.U16() != 1) throw new InvalidDataException("Unsupported checkpoint format.");
                var manifest = new CompatibilityManifest(r.Text(), r.Text(), r.Text(), r.Text(), r.Text(), r.Text());
                var sessionId = r.Guid(); ulong epoch = r.U64(); uint tickRate = r.U32();
                ulong baseTick = r.U64(); ulong tick = r.U64();
                _ = checked(baseTick + tick);
                var stored = ReadLimits(ref r, cap);
                if (bytes.Length > stored.MaxCheckpointBytes)
                    throw new InvalidDataException("Checkpoint exceeds its own declared byte budget.");
                int memberCount = r.U16();
                if (memberCount < 1 || memberCount > stored.MaxParticipants)
                    throw new InvalidDataException("Invalid participant count.");
                var participants = new Participant[memberCount];
                for (int i = 0; i < memberCount; i++)
                {
                    participants[i] = new Participant(r.U16(), r.Guid(), r.U64());
                    if (i > 0 && participants[i].Slot <= participants[i - 1].Slot)
                        throw new InvalidDataException("Participants are not in canonical order.");
                }
                var session = new SessionDescriptor(sessionId, epoch, tickRate, participants, baseTick);
                var missing = new ulong[memberCount];
                for (int i = 0; i < memberCount; i++)
                {
                    missing[i] = r.U64();
                    if (missing[i] > tick) throw new InvalidDataException("Impossible missing-input count.");
                }
                int historyCount = r.Count(stored.HistoryFrames);
                if ((ulong)historyCount != Math.Min(tick, (ulong)stored.HistoryFrames))
                    throw new InvalidDataException("Incomplete input history.");
                var history = new InputFrame[historyCount];
                for (int i = 0; i < historyCount; i++)
                {
                    history[i] = InputFormat.Read(ref r, session, stored);
                    if (history[i].Tick != tick - (ulong)historyCount + 1 + (ulong)i)
                        throw new InvalidDataException("Nonconsecutive history.");
                }
                ValidateMissing(missing, history);
                var initialization = new ByteString(r.Blob(stored.MaxCheckpointBytes));
                byte[] state = r.Blob(stored.MaxCheckpointBytes).ToArray();
                r.Finish();
                return new Checkpoint(manifest, session, stored, tick, history, missing, state, initialization);
            }
            catch (ArgumentException e) { throw new InvalidDataException("Invalid checkpoint fields.", e); }
            catch (OverflowException e) { throw new InvalidDataException("Checkpoint integer overflow.", e); }
        }

        public static string Hash(Checkpoint checkpoint)
        {
            byte[] bytes = Encode(checkpoint);
            return DataEnvelope.Hex(bytes.AsSpan(bytes.Length - DataEnvelope.DigestLength));
        }

        private static void ValidateMissing(ulong[] missing, InputFrame[] history)
        {
            for (int slotIndex = 0; slotIndex < missing.Length; slotIndex++)
            {
                ulong suffix = 0;
                for (int i = history.Length - 1; i >= 0; i--)
                {
                    if (history[i].Inputs[slotIndex].Origin == InputOrigin.Submitted) break;
                    suffix++;
                }
                if (suffix < (ulong)history.Length ? missing[slotIndex] != suffix : missing[slotIndex] < suffix)
                    throw new InvalidDataException("Missing-input counter disagrees with history.");
            }
        }

        internal static void WriteLimits(CanonicalWriter w, RuntimeLimits limits)
        {
            w.U32((uint)limits.MaxParticipants); w.U32((uint)limits.MaxInputBytes);
            w.U32((uint)limits.MaxCheckpointBytes); w.U32((uint)limits.HistoryFrames);
            w.U32((uint)limits.MaxEventsPerFrame); w.U32((uint)limits.MaxEventBytes);
        }
        private static RuntimeLimits ReadLimits(ref CanonicalReader r, RuntimeLimits cap) =>
            new RuntimeLimits(r.Count(cap.MaxParticipants), r.Count(cap.MaxInputBytes),
                r.Count(cap.MaxCheckpointBytes), r.Count(cap.HistoryFrames),
                r.Count(cap.MaxEventsPerFrame), r.Count(cap.MaxEventBytes));

        internal static bool SameLimits(RuntimeLimits a, RuntimeLimits b) =>
            a.MaxParticipants == b.MaxParticipants && a.MaxInputBytes == b.MaxInputBytes &&
            a.MaxCheckpointBytes == b.MaxCheckpointBytes && a.HistoryFrames == b.HistoryFrames &&
            a.MaxEventsPerFrame == b.MaxEventsPerFrame && a.MaxEventBytes == b.MaxEventBytes;

        internal static bool SameSession(SessionDescriptor a, SessionDescriptor b) =>
            a.SessionId == b.SessionId && a.Epoch == b.Epoch && a.TickRate == b.TickRate &&
            a.BaseTick == b.BaseTick && a.Participants.Count == b.Participants.Count &&
            a.Participants.Zip(b.Participants, (x, y) =>
                x.Slot == y.Slot && x.ParticipantId == y.ParticipantId && x.Generation == y.Generation).All(x => x);
    }
}
