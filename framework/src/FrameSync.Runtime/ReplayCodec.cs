using System;
using System.IO;

namespace FrameSync.Runtime
{
    public static class ReplayCodec
    {
        private const uint Magic = 0x50525346; // FSRP

        public static byte[] Encode(ReplayRecording recording)
        {
            if (recording == null) throw new ArgumentNullException(nameof(recording));
            using var w = new CanonicalWriter(recording.Limits.MaxBytes - DataEnvelope.DigestLength);
            w.U32(Magic); w.U16(1);
            w.Blob(CheckpointCodec.Encode(recording.Initial));
            w.U32((uint)recording.Frames.Count);
            foreach (var frame in recording.Frames) InputFormat.Write(w, frame);
            return DataEnvelope.Seal(w.ToArray());
        }

        public static ReplayRecording Decode(ReadOnlySpan<byte> bytes, ReplayLimits? limits = null,
            RuntimeLimits? checkpointLimits = null)
        {
            var cap = limits ?? new ReplayLimits(); var stateCap = checkpointLimits ?? new RuntimeLimits();
            var r = new CanonicalReader(DataEnvelope.Open(bytes, cap.MaxBytes));
            try
            {
                if (r.U32() != Magic || r.U16() != 1) throw new InvalidDataException("Unsupported recording format.");
                var initial = CheckpointCodec.Decode(r.Blob(stateCap.MaxCheckpointBytes), stateCap);
                int count = r.Count(cap.MaxFrames);
                long minimumFrameBytes = 10L + initial.Session.Participants.Count * 15L;
                if (count > r.Remaining / minimumFrameBytes)
                    throw new InvalidDataException("Frame count cannot fit in the remaining data.");
                var recorder = new ReplayRecorder(initial, cap);
                for (int i = 0; i < count; i++) recorder.Append(InputFormat.Read(ref r, initial.Session, initial.Limits));
                r.Finish();
                return recorder.Build();
            }
            catch (ArgumentException e) { throw new InvalidDataException("Invalid recording fields.", e); }
            catch (OverflowException e) { throw new InvalidDataException("Recording integer overflow.", e); }
            catch (InvalidOperationException e) { throw new InvalidDataException("Recording exceeds its limits.", e); }
        }
    }
}
