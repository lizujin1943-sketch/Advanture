using System.Collections.Generic;
using System.Collections.ObjectModel;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    /// <summary>Complete immutable state after Tick, including Stage A's fixed roster and input context.</summary>
    public sealed class Checkpoint
    {
        internal Checkpoint(CompatibilityManifest manifest, SessionDescriptor session, RuntimeLimits limits,
            ulong tick, IReadOnlyList<InputFrame> history, IReadOnlyList<ulong> missingCounts, byte[] state, ByteString initializationData)
        {
            Manifest = manifest; Session = session; Limits = limits; Tick = tick;
            InputHistory = new ReadOnlyCollection<InputFrame>(new List<InputFrame>(history));
            MissingInputCounts = new ReadOnlyCollection<ulong>(new List<ulong>(missingCounts));
            ApplicationState = new ByteString(state);
            InitializationData = new ByteString(initializationData.Span);
        }
        public CompatibilityManifest Manifest { get; }
        public SessionDescriptor Session { get; }
        public RuntimeLimits Limits { get; }
        public ulong Tick { get; }
        public ulong PersistentTick => checked(Session.BaseTick + Tick);
        public IReadOnlyList<InputFrame> InputHistory { get; }
        public IReadOnlyList<ulong> MissingInputCounts { get; }
        public ByteString ApplicationState { get; }
        public ByteString InitializationData { get; }
    }
}
