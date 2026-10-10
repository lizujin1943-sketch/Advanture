using System;
using System.Collections.Generic;

namespace FrameSync.Abstractions
{
    public interface ISimulationFactory<TState>
    {
        /// <summary>Return a new, isolated instance for every call.</summary>
        IDeterministicSimulation<TState> Create();
    }

    public interface IDeterministicSimulation<TState>
    {
        void Initialize(SimulationInitialization initialization);
        /// <summary>Pure deterministic advancement; never read devices or perform external effects.</summary>
        IReadOnlyList<ByteString> Step(FrameContext context, IReadOnlyList<SimulationInput> inputs);
        /// <summary>Return a complete independent state, including any random generator state.</summary>
        TState Capture();
        /// <summary>Copy state into this instance; never mutate or retain mutable aliases to it.</summary>
        void Restore(TState state);
    }

    public interface IStateCodec<TState>
    {
        byte[] Encode(TState state);
        TState Decode(ReadOnlySpan<byte> bytes);
    }

    /// <summary>Stage A's format contract; prediction and missing-input policies are added in B/C.</summary>
    public interface IInputValidator
    {
        void ValidateCanonical(ReadOnlySpan<byte> bytes);
    }

    public sealed class SimulationModule<TState>
    {
        public SimulationModule(CompatibilityManifest manifest, ISimulationFactory<TState> factory,
            IStateCodec<TState> codec, IInputValidator inputValidator)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            Codec = codec ?? throw new ArgumentNullException(nameof(codec));
            InputValidator = inputValidator ?? throw new ArgumentNullException(nameof(inputValidator));
        }
        public CompatibilityManifest Manifest { get; }
        public ISimulationFactory<TState> Factory { get; }
        public IStateCodec<TState> Codec { get; }
        public IInputValidator InputValidator { get; }
    }
}
