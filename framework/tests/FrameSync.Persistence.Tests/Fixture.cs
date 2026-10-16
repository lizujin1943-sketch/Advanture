using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace FrameSync.Persistence.Tests
{
    /// <summary>使用独立整数模拟构建真实检查点，避免把格式测试绑定到游戏示例。</summary>
    internal static class Fixture
    {
        /// <summary>由宿主分配的存档身份，与测试会话身份不同。</summary>
        public static readonly Guid SaveId = Guid.Parse("99999999-1234-5678-9012-123456789abc");
        /// <summary>产生一份已完成两帧、累计帧为 102 的检查点。</summary>
        public static Checkpoint Checkpoint(RuntimeLimits? limits = null)
        {
            var module = new IntegerModule();
            var session = new SessionDescriptor(Guid.Parse("11111111-2222-3333-4444-555555555555"), 1, 30,
                new[] { new Participant(0, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")) }, 100);
            var runner = new SimulationRunner<long>(module.Module(), session, limits, new ByteString(new byte[] { 3, 4 }));
            runner.Advance(new InputFrame(session.SessionId, session.Epoch, 1,
                new[] { new RecordedInput(0, 1, new byte[] { 7 }, InputOrigin.Submitted) }));
            runner.Advance(new InputFrame(session.SessionId, session.Epoch, 2,
                new[] { new RecordedInput(0, 1, new byte[] { 0 }, InputOrigin.MissingSubstitute) }));
            return runner.CaptureCheckpoint();
        }
        /// <summary>生成保存身份固定的正式存档。</summary>
        public static SaveDocument Document(RuntimeLimits? limits = null) => new SaveDocument(SaveId, Checkpoint(limits));

        /// <summary>输入为单字节整数、状态为小端有符号整数的确定性模型。</summary>
        private sealed class IntegerModule : ISimulationFactory<long>, IStateCodec<long>, IInputValidator
        {
            /// <summary>构造游戏接口集合。</summary>
            public SimulationModule<long> Module() => new SimulationModule<long>(
                new CompatibilityManifest("persistence-test", "1"), this, this, this);
            /// <summary>每次返回独立模拟，恢复不能污染已有实例。</summary>
            public IDeterministicSimulation<long> Create() => new IntegerSimulation();
            /// <summary>单字节即唯一输入格式。</summary>
            public void ValidateCanonical(ReadOnlySpan<byte> bytes)
            { if (bytes.Length != 1) throw new ArgumentException("测试输入必须为单字节。"); }
            /// <summary>将整数状态写为固定宽度小端字节。</summary>
            public byte[] Encode(long state)
            {
                var bytes = new byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(bytes, state);
                return bytes;
            }
            /// <summary>严格读取八字节状态。</summary>
            public long Decode(ReadOnlySpan<byte> bytes)
            {
                if (bytes.Length != 8) throw new ArgumentException("测试状态必须为八字节。");
                return BinaryPrimitives.ReadInt64LittleEndian(bytes);
            }
        }

        /// <summary>持有独立整数值的测试模拟。</summary>
        private sealed class IntegerSimulation : IDeterministicSimulation<long>
        {
            private long _value;
            /// <summary>新会话从零开始。</summary>
            public void Initialize(SimulationInitialization initialization) { _value = 0; }
            /// <summary>累加每位参与者输入，不产生外部副作用。</summary>
            public IReadOnlyList<ByteString> Step(FrameContext context, IReadOnlyList<SimulationInput> inputs)
            {
                foreach (var input in inputs) _value += input.Payload.Span[0];
                return Array.Empty<ByteString>();
            }
            /// <summary>值类型捕获不会共享可变状态。</summary>
            public long Capture() => _value;
            /// <summary>恢复精确整数值。</summary>
            public void Restore(long state) { _value = state; }
        }
    }
}
