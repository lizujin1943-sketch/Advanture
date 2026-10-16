using System;
using System.Collections.Generic;
using System.IO;
using FrameSync.Runtime;

namespace FrameSync.Persistence.Tests
{
    /// <summary>验证服务通过存储边界读写完整存档，并在提交之前拒绝无效数据。</summary>
    internal static class SaveServiceTests
    {
        /// <summary>测试不假设文件系统存在，证明核心只依赖 ISaveStore。</summary>
        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("service.saves_and_loads_complete_confirmed_checkpoint", () => {
                var store = new MemoryStore(); var service = new SaveService(store);
                var checkpoint = Fixture.Checkpoint();
                var saved = service.Save(Fixture.SaveId, checkpoint);
                var loaded = service.Load();
                Assert.Equal(Fixture.SaveId, saved.SaveId);
                Assert.Equal(Fixture.SaveId, loaded.SaveId);
                Assert.Equal(102UL, loaded.Checkpoint.PersistentTick);
                Assert.Bytes(CheckpointCodec.Encode(checkpoint), CheckpointCodec.Encode(loaded.Checkpoint));
            });
            yield return ("service.validates_before_replacing_existing_save", () => {
                var service = new SaveService(new MemoryStore());
                service.Save(Fixture.SaveId, Fixture.Checkpoint());
                var larger = Fixture.Checkpoint(new RuntimeLimits(maxCheckpointBytes: 16 * 1024 * 1024));
                Assert.Throws<InvalidOperationException>(() => service.Save(Guid.NewGuid(), larger));
                Assert.Throws<ArgumentException>(() => service.Save(Guid.Empty, Fixture.Checkpoint()));
                Assert.Equal(Fixture.SaveId, service.Load().SaveId);
                Assert.Equal(102UL, service.Load().Checkpoint.PersistentTick);
            });
            yield return ("service.passes_exact_read_budget_to_storage", () => {
                var limits = new RuntimeLimits(maxCheckpointBytes: 2048);
                var store = new MemoryStore(expectedReadLimit: 2106);
                var service = new SaveService(store, limits);
                service.Save(Fixture.SaveId, Fixture.Checkpoint(limits));
                Assert.Equal(Fixture.SaveId, service.Load().SaveId);
            });
            yield return ("service.rejects_corrupt_storage_content", () => {
                byte[] broken = SaveCodec.Encode(Fixture.Document()); broken[40] ^= 1;
                var service = new SaveService(new MemoryStore(broken));
                Assert.Throws<InvalidDataException>(() => service.Load());
            });
            yield return ("service.rechecks_size_from_nonconforming_storage", () => {
                var service = new SaveService(new OversizedStore(), new RuntimeLimits(maxCheckpointBytes: 256));
                Assert.Throws<InvalidDataException>(() => service.Load());
            });
            yield return ("service.rejects_missing_store", () => {
                Assert.Throws<ArgumentNullException>(() => new SaveService(null!));
            });
        }

        /// <summary>真实持有字节副本的内存适配器，让服务不需要依赖磁盘也能完成存读档。</summary>
        private sealed class MemoryStore : ISaveStore
        {
            private byte[]? _bytes;
            private readonly int? _expectedReadLimit;
            /// <summary>可装入外部损坏字节，用于验证服务的输入边界。</summary>
            public MemoryStore(byte[]? bytes = null, int? expectedReadLimit = null)
            { _bytes = bytes?.AsSpan().ToArray(); _expectedReadLimit = expectedReadLimit; }
            /// <summary>遵循读上限后复制；测试也能要求特定预算，检查参数没有在边界上遗失。</summary>
            public byte[] Read(int maxBytes)
            {
                if (_expectedReadLimit.HasValue && maxBytes != _expectedReadLimit.Value)
                    throw new InvalidOperationException("存储没有收到预期的读取预算。");
                if (_bytes == null) throw new FileNotFoundException();
                if (_bytes.Length > maxBytes) throw new InvalidDataException();
                return (byte[])_bytes.Clone();
            }
            /// <summary>先检查大小再整体交换独立副本，维持原子写约定。</summary>
            public void WriteAtomic(ReadOnlySpan<byte> bytes, int maxBytes)
            {
                if (bytes.Length > maxBytes) throw new InvalidDataException();
                _bytes = bytes.ToArray();
            }
        }

        /// <summary>故意违反读取约定的外部适配器，证明服务不会把超限字节视为可信。</summary>
        private sealed class OversizedStore : ISaveStore
        {
            /// <summary>返回比预算大一字节的数据。</summary>
            public byte[] Read(int maxBytes) => new byte[maxBytes + 1];
            /// <summary>此适配器只用于载入失败路径。</summary>
            public void WriteAtomic(ReadOnlySpan<byte> bytes, int maxBytes) => throw new NotSupportedException();
        }
    }
}
