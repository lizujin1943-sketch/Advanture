using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FrameSync.Abstractions;

namespace FrameSync.Samples.Counter
{
    public sealed class CounterGame : ISimulationFactory<CounterState>, IStateCodec<CounterState>, IInputValidator
    {
        private const int OperationBytes = 5;
        private const int BatchHeaderBytes = 2;
        private const byte BatchCommand = 2;

        /// <summary>单个玩家一帧最多提交 16 项操作，完整批次最多占 82 字节。</summary>
        public const int MaxOperationsPerFrame = 16;

        public static SimulationModule<CounterState> CreateModule()
        {
            var game = new CounterGame();
            return new SimulationModule<CounterState>(new CompatibilityManifest("sample-counter", inputFormatVersion: "2", determinismHash: "xorshift32-v1"), game, game, game);
        }
        /// <summary>保留原单操作编码：0 表示 Add，1 表示 RandomAdd，后续由输入检查器校验参数。</summary>
        /// <remarks>单参数 Input(1) 仍表示随机操作；Add(amount) 应调用 Input(0, amount)。</remarks>
        public static byte[] Input(byte command, int amount = 0)
        {
            var bytes = new byte[5]; bytes[0] = command;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), amount); return bytes;
        }
        /// <summary>把 1..16 个合法的单操作按调用者给出的顺序封装为本帧输入。</summary>
        /// <param name="operations">由 Input 或命名辅助方法生成的单操作；不接受空项或嵌套批次。</param>
        /// <returns>独立字节数组；单项仍使用原来的五字节编码，避免同一单项出现两种格式。</returns>
        /// <remarks>不会合并、排序或丢弃 Add(0)。空批次应由显式 NeutralInput 代替。</remarks>
        public static byte[] InputBatch(params byte[][] operations)
        {
            if (operations == null) throw new ArgumentNullException(nameof(operations));
            if (operations.Length == 0 || operations.Length > MaxOperationsPerFrame)
                throw new ArgumentException("Counter batches require 1..16 operations.", nameof(operations));
            foreach (var operation in operations)
            {
                if (operation == null) throw new ArgumentException("Null Counter operation.", nameof(operations));
                ValidateOperation(operation);
            }
            if (operations.Length == 1) return (byte[])operations[0].Clone();
            var bytes = new byte[BatchHeaderBytes + operations.Length * OperationBytes];
            bytes[0] = BatchCommand; bytes[1] = (byte)operations.Length;
            for (int i = 0; i < operations.Length; i++)
                operations[i].CopyTo(bytes, BatchHeaderBytes + i * OperationBytes);
            return bytes;
        }

        /// <summary>创建一次随机加法；随机进度只在实际执行这一操作时前进。</summary>
        public static byte[] RandomInput() => Input(1);
        /// <summary>创建中性输入 Add(0)：本帧正常推进，不改变总数或随机进度。</summary>
        public static byte[] NeutralInput() => Input(0);

        public IDeterministicSimulation<CounterState> Create() => new Model();

        /// <summary>验证完整输入，包括所有批次项；拒绝截断、尾部字节、嵌套及非规范单项包装。</summary>
        public void ValidateCanonical(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == OperationBytes)
            {
                ValidateOperation(bytes); return;
            }
            if (bytes.Length < BatchHeaderBytes || bytes[0] != BatchCommand)
                throw new ArgumentException("Expected a Counter operation or batch.");
            int count = bytes[1];
            // 只允许 2..16 项的批次头；一项必须直接使用旧版五字节操作编码。
            if (count < 2 || count > MaxOperationsPerFrame || bytes.Length != BatchHeaderBytes + count * OperationBytes)
                throw new ArgumentException("Counter batch count or length is not canonical.");
            for (int i = 0; i < count; i++)
                ValidateOperation(bytes.Slice(BatchHeaderBytes + i * OperationBytes, OperationBytes));
        }

        private static void ValidateOperation(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != OperationBytes) throw new ArgumentException("Counter operations must contain five bytes.");
            int amount = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(1));
            if (bytes[0] > 1 || (bytes[0] == 1 && amount != 0) || amount < -1000 || amount > 1000)
                throw new ArgumentException("Expected Add(-1000..1000) or RandomAdd(0).");
        }
        public byte[] Encode(CounterState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.RandomState == 0) throw new InvalidDataException("Random state cannot be zero.");
            var bytes = new byte[20];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, state.Total);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), state.RandomState);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12), state.Steps);
            return bytes;
        }
        public CounterState Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 20) throw new InvalidDataException("Counter state must contain twenty bytes.");
            uint random = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8));
            if (random == 0) throw new InvalidDataException("Random state cannot be zero.");
            return new CounterState(BinaryPrimitives.ReadInt64LittleEndian(bytes), random,
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(12)));
        }
        private sealed class Model : IDeterministicSimulation<CounterState>
        {
            private long _total;
            private uint _random;
            private ulong _steps;
            public void Initialize(SimulationInitialization initialization)
            {
                if (initialization.Data.Length != 0 && initialization.Data.Length != 4)
                    throw new ArgumentException("Initialization must be empty or a four-byte seed.");
                _random = initialization.Data.Length == 0 ? 0x12345678U : BinaryPrimitives.ReadUInt32LittleEndian(initialization.Data.Span);
                if (_random == 0) throw new ArgumentException("Seed cannot be zero.");
                _total = 0; _steps = 0;
            }
            public IReadOnlyList<ByteString> Step(FrameContext context, IReadOnlyList<SimulationInput> inputs)
            {
                long before = _total;
                foreach (var input in inputs)
                {
                    // 运行器先按 Slot 排序并校验所有输入；玩家内部按原始批次顺序逐项执行。
                    var payload = input.Payload.Span;
                    int offset = payload.Length == OperationBytes ? 0 : BatchHeaderBytes;
                    for (; offset < payload.Length; offset += OperationBytes)
                        ApplyOperation(payload.Slice(offset, OperationBytes));
                }
                _steps = checked(_steps + 1);
                if (_total == before) return Array.Empty<ByteString>();
                var bytes = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(bytes, _total);
                return new[] { new ByteString(bytes) };
            }
            private void ApplyOperation(ReadOnlySpan<byte> operation)
            {
                if (operation[0] == 0)
                    _total = checked(_total + BinaryPrimitives.ReadInt32LittleEndian(operation.Slice(1)));
                else
                {
                    // 显式 uint 运算固定随机序列；Add(0) 不会消耗随机数。
                    _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
                    _total = checked(_total + _random % 10);
                }
            }
            public CounterState Capture() => new CounterState(_total, _random, _steps);
            public void Restore(CounterState state)
            {
                if (state.RandomState == 0) throw new InvalidDataException("Random state cannot be zero.");
                _total = state.Total; _random = state.RandomState; _steps = state.Steps;
            }
        }
    }
}
