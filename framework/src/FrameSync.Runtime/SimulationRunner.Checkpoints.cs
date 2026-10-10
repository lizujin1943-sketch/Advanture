using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    public sealed partial class SimulationRunner<TState>
    {
        private Queue<InputFrame> _history = null!;
        private ulong[] _missingCounts = null!;

        public Checkpoint CaptureCheckpoint()
        {
            EnsureAvailable();
            _busy = true;
            try
            {
                var checkpoint = new Checkpoint(_module.Manifest, Session, Limits, Tick,
                    _history.ToArray(), _missingCounts, CaptureApplication(), _initialization.Data);
                _ = CheckpointCodec.Encode(checkpoint); // Enforce the complete envelope budget, not only game bytes.
                return checkpoint;
            }
            finally { _busy = false; }
        }

        /// <summary>Validate on an isolated candidate; failures cannot leave the live instance half restored.</summary>
        public void Restore(Checkpoint checkpoint)
        {
            EnsureAvailable(allowFaulted: true);
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            _busy = true;
            try
            {
                if (!_module.Manifest.Equals(checkpoint.Manifest) ||
                    !CheckpointCodec.SameSession(Session, checkpoint.Session) ||
                    !CheckpointCodec.SameLimits(Limits, checkpoint.Limits))
                    throw new ArgumentException("Checkpoint is incompatible with this module or session.");
                foreach (var frame in checkpoint.InputHistory) ValidateFrame(frame, frame.Tick);
                _ = CheckpointCodec.Encode(checkpoint); // Check the complete byte budget.
                var state = _module.Codec.Decode(checkpoint.ApplicationState.Span);
                if (!_module.Codec.Encode(state).AsSpan().SequenceEqual(checkpoint.ApplicationState.Span))
                    throw new InvalidDataException("Application state is not canonically encoded.");
                var candidate = _module.Factory.Create() ?? throw new InvalidOperationException("Factory returned null.");
                if (ReferenceEquals(candidate, _simulation))
                    throw new InvalidOperationException("Factory must create an isolated simulation.");
                var initialization = new SimulationInitialization(Session, checkpoint.InitializationData);
                candidate.Initialize(initialization);
                candidate.Restore(state);
                if (!_module.Codec.Encode(candidate.Capture()).AsSpan().SequenceEqual(checkpoint.ApplicationState.Span))
                    throw new InvalidDataException("Restore did not reproduce the complete application state.");
                var history = new Queue<InputFrame>(Limits.HistoryFrames);
                foreach (var frame in checkpoint.InputHistory) history.Enqueue(frame);
                var counts = checkpoint.MissingInputCounts.ToArray();
                _simulation = candidate; _history = history; _missingCounts = counts; _initialization = initialization;
                Tick = checkpoint.Tick; IsFaulted = false;
            }
            finally { _busy = false; }
        }
    }
}
