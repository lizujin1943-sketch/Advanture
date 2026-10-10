using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    public sealed class ReplayLimits
    {
        public ReplayLimits(int maxFrames = 100000, int maxBytes = 64 * 1024 * 1024)
        {
            if (maxFrames < 0 || maxBytes < 256) throw new ArgumentOutOfRangeException(nameof(maxFrames));
            MaxFrames = maxFrames; MaxBytes = maxBytes;
        }
        public int MaxFrames { get; }
        public int MaxBytes { get; }
    }

    public sealed class ReplayRecording
    {
        internal ReplayRecording(Checkpoint initial, IReadOnlyList<InputFrame> frames, ReplayLimits limits)
        {
            Initial = initial; Limits = limits;
            Frames = new ReadOnlyCollection<InputFrame>(new List<InputFrame>(frames));
        }
        public Checkpoint Initial { get; }
        public IReadOnlyList<InputFrame> Frames { get; }
        public ReplayLimits Limits { get; }
    }

    /// <summary>Bounded recording builder. Append only after Advance successfully completes.</summary>
    public sealed class ReplayRecorder
    {
        private readonly Checkpoint _initial;
        private readonly ReplayLimits _limits;
        private readonly List<InputFrame> _frames = new List<InputFrame>();
        private long _encodedBytes;

        public ReplayRecorder(Checkpoint initial, ReplayLimits? limits = null)
        {
            _initial = initial ?? throw new ArgumentNullException(nameof(initial));
            _limits = limits ?? new ReplayLimits();
            _encodedBytes = 6L + 4 + CheckpointCodec.Encode(initial).Length + 4 + DataEnvelope.DigestLength;
            if (_encodedBytes > _limits.MaxBytes)
                throw new InvalidOperationException("Initial checkpoint exceeds the recording budget.");
        }

        public void Append(InputFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (_frames.Count >= _limits.MaxFrames) throw new InvalidOperationException("Recording frame limit reached.");
            ulong expected = checked(_initial.Tick + (ulong)_frames.Count + 1);
            var session = _initial.Session; var limits = _initial.Limits;
            if (frame.SessionId != session.SessionId || frame.Epoch != session.Epoch || frame.Tick != expected ||
                frame.Inputs.Count != session.Participants.Count)
                throw new ArgumentException("Recording requires consecutive frames from its initial session.");
            for (int i = 0; i < frame.Inputs.Count; i++)
                if (frame.Inputs[i].Slot != session.Participants[i].Slot ||
                    frame.Inputs[i].Generation != session.Participants[i].Generation ||
                    frame.Inputs[i].Payload.Length > limits.MaxInputBytes)
                    throw new ArgumentException("Input is incompatible with the recorded roster or byte limit.");
            _ = checked(session.BaseTick + frame.Tick);
            long nextBytes = checked(_encodedBytes + InputFormat.EncodedSize(frame));
            if (nextBytes > _limits.MaxBytes) throw new InvalidOperationException("Recording byte limit reached.");
            _frames.Add(frame); _encodedBytes = nextBytes;
        }

        public ReplayRecording Build() => new ReplayRecording(_initial, _frames, _limits);
    }
}
