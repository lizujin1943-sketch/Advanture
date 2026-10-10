using System;
using System.Collections.ObjectModel;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    /// <summary>Single-owner-thread driver. Stage A accepts complete, consecutive input frames.</summary>
    public sealed partial class SimulationRunner<TState>
    {
        private readonly SimulationModule<TState> _module;
        private SimulationInitialization _initialization;
        private IDeterministicSimulation<TState> _simulation;
        private bool _busy;

        public SimulationRunner(SimulationModule<TState> module, SessionDescriptor session,
            RuntimeLimits? limits = null, ByteString? initialization = null)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Limits = limits ?? new RuntimeLimits();
            if (session.Participants.Count > Limits.MaxParticipants)
                throw new ArgumentException("Participant count exceeds the configured limit.");
            if (initialization != null && initialization.Length > Limits.MaxCheckpointBytes)
                throw new ArgumentException("Initialization data exceeds the configured limit.");
            _initialization = new SimulationInitialization(session, initialization ?? ByteString.Empty);
            _simulation = module.Factory.Create() ?? throw new InvalidOperationException("Factory returned null.");
            _simulation.Initialize(_initialization);
            _history = new System.Collections.Generic.Queue<InputFrame>(Limits.HistoryFrames);
            _missingCounts = new ulong[session.Participants.Count];
        }

        public SessionDescriptor Session { get; }
        public RuntimeLimits Limits { get; }
        public ulong Tick { get; private set; }
        public bool IsFaulted { get; private set; }

        public FrameResult Advance(InputFrame frame)
        {
            EnsureAvailable();
            _busy = true;
            try
            {
                ValidateFrame(frame, checked(Tick + 1));
                ulong persistentTick = checked(Session.BaseTick + frame.Tick);
                var missingCounts = new ulong[_missingCounts.Length];
                for (int i = 0; i < missingCounts.Length; i++)
                    missingCounts[i] = frame.Inputs[i].Origin == InputOrigin.MissingSubstitute
                        ? checked(_missingCounts[i] + 1) : 0;
                var inputs = new SimulationInput[frame.Inputs.Count];
                for (int i = 0; i < inputs.Length; i++)
                    inputs[i] = new SimulationInput(Session.Participants[i], frame.Inputs[i].Payload);
                try
                {
                    var output = _simulation.Step(new FrameContext(frame.Tick, persistentTick, Session.TickRate),
                        new ReadOnlyCollection<SimulationInput>(inputs));
                    if (output == null || output.Count > Limits.MaxEventsPerFrame)
                        throw new InvalidOperationException("Game event count exceeds the configured limit.");
                    var events = new SimulationEvent[output.Count];
                    for (int i = 0; i < events.Length; i++)
                    {
                        var payload = output[i];
                        if (payload == null || payload.Length > Limits.MaxEventBytes)
                            throw new InvalidOperationException("Invalid or oversized game event.");
                        events[i] = new SimulationEvent(Session.SessionId, Session.Epoch, frame.Tick, i,
                            new ByteString(payload.Span));
                    }
                    if (_history.Count == Limits.HistoryFrames) _history.Dequeue();
                    _history.Enqueue(frame); _missingCounts = missingCounts;
                    Tick = frame.Tick;
                    return new FrameResult(Tick, new ReadOnlyCollection<SimulationEvent>(events));
                }
                catch { IsFaulted = true; throw; }
            }
            finally { _busy = false; }
        }

        /// <summary>Returns detached data for inspection, never the live simulation state.</summary>
        public TState InspectState()
        {
            EnsureAvailable();
            _busy = true;
            try { return _module.Codec.Decode(CaptureApplication()); }
            finally { _busy = false; }
        }

        private byte[] CaptureApplication()
        {
            var bytes = _module.Codec.Encode(_simulation.Capture());
            if (bytes == null || bytes.Length > Limits.MaxCheckpointBytes)
                throw new InvalidOperationException("Application state exceeds the checkpoint limit.");
            return bytes;
        }

        private void EnsureAvailable(bool allowFaulted = false)
        {
            if (_busy) throw new InvalidOperationException("Reentrant runtime calls are not allowed.");
            if (IsFaulted && !allowFaulted)
                throw new InvalidOperationException("The simulation is faulted; restore a valid checkpoint before continuing.");
        }

        private void ValidateFrame(InputFrame frame, ulong expectedTick)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (frame.SessionId != Session.SessionId || frame.Epoch != Session.Epoch || frame.Tick != expectedTick)
                throw new ArgumentException("Frame belongs to a different session/epoch or is not consecutive.");
            if (frame.Inputs.Count != Session.Participants.Count)
                throw new ArgumentException("A frame must contain one input per active participant.");
            for (int i = 0; i < frame.Inputs.Count; i++)
            {
                var input = frame.Inputs[i];
                var participant = Session.Participants[i];
                if (input.Slot != participant.Slot || input.Generation != participant.Generation)
                    throw new ArgumentException("Input slot or control generation is invalid.");
                if (input.Payload.Length > Limits.MaxInputBytes)
                    throw new ArgumentException("Input exceeds the configured byte limit.");
                _module.InputValidator.ValidateCanonical(input.Payload.Span);
            }
        }
    }
}
