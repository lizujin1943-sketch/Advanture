using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FrameSync.Runtime;

namespace FrameSync.Persistence.Tests
{
    /// <summary>验证正式存档的外壳格式、完整性和解码资源边界。</summary>
    internal static class SaveCodecTests
    {
        /// <summary>每项测试针对会造成坏档被接受或完整状态遗失的实际回归。</summary>
        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("save.roundtrip_preserves_identity_and_complete_checkpoint", () => {
                var source = Fixture.Document();
                byte[] bytes = SaveCodec.Encode(source);
                var loaded = SaveCodec.Decode(bytes);
                Assert.Equal(Fixture.SaveId, loaded.SaveId);
                Assert.Equal(2UL, loaded.Checkpoint.Tick);
                Assert.Equal(102UL, loaded.Checkpoint.PersistentTick);
                Assert.Equal(7L, BinaryPrimitives.ReadInt64LittleEndian(loaded.Checkpoint.ApplicationState.Span));
                Assert.Equal(1UL, loaded.Checkpoint.MissingInputCounts[0]);
                Assert.Bytes(new byte[] { 3, 4 }, loaded.Checkpoint.InitializationData.Span);
                Assert.Bytes(CheckpointCodec.Encode(source.Checkpoint), CheckpointCodec.Encode(loaded.Checkpoint));
                Assert.Bytes(new byte[] { 0x46, 0x53, 0x53, 0x56, 1, 0 }, bytes.AsSpan(0, 6));
                Assert.Bytes(bytes, SaveCodec.Encode(loaded));
            });
            yield return ("save.rejects_corruption_in_identity_payload_and_digest", () => {
                byte[] bytes = SaveCodec.Encode(Fixture.Document());
                foreach (int index in new[] { 6, 40, bytes.Length - 1 })
                {
                    byte[] bad = (byte[])bytes.Clone(); bad[index] ^= 1;
                    Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(bad));
                }
            });
            yield return ("save.rejects_truncation_and_trailing_bytes", () => {
                byte[] bytes = SaveCodec.Encode(Fixture.Document());
                foreach (int length in new[] { 0, 6, 26, 57, bytes.Length - 1 })
                    Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(bytes.AsSpan(0, length)));
                byte[] extended = new byte[bytes.Length + 1]; bytes.CopyTo(extended, 0);
                Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(extended));
            });
            yield return ("save.rejects_unsupported_version_with_valid_digest", () => {
                byte[] bytes = SaveCodec.Encode(Fixture.Document()); bytes[4] = 99; Resign(bytes);
                Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(bytes));
            });
            yield return ("save.rejects_forged_lengths_and_empty_identity_with_valid_digest", () => {
                byte[] bytes = SaveCodec.Encode(Fixture.Document());
                foreach (uint length in new[] { 0U, uint.MaxValue, (uint)(bytes.Length - 59), (uint)(bytes.Length - 57) })
                {
                    byte[] bad = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(22), length); Resign(bad);
                    Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(bad));
                }
                Array.Clear(bytes, 6, 16); Resign(bytes);
                Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(bytes));
            });
            yield return ("save.validates_inner_checkpoint_even_when_outer_digest_is_valid", () => {
                byte[] bytes = SaveCodec.Encode(Fixture.Document()); bytes[40] ^= 1; Resign(bytes);
                Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(bytes));
            });
            yield return ("save.rejects_oversized_outer_data_before_parsing", () => {
                var cap = new RuntimeLimits(maxCheckpointBytes: 256);
                Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(new byte[315], cap));
                Assert.Equal(314, SaveCodec.GetMaximumEncodedBytes(cap));
                Assert.Equal(int.MaxValue, SaveCodec.GetMaximumEncodedBytes(new RuntimeLimits(maxCheckpointBytes: int.MaxValue)));
            });
            yield return ("save.enforces_checkpoint_limits_for_encode_and_decode", () => {
                var source = Fixture.Document();
                var cap = new RuntimeLimits(historyFrames: 1);
                Assert.Throws<InvalidOperationException>(() => SaveCodec.Encode(source, cap));
                Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(SaveCodec.Encode(source), cap));
            });
            yield return ("save.rejects_invalid_document_arguments", () => {
                Assert.Throws<ArgumentException>(() => new SaveDocument(Guid.Empty, Fixture.Checkpoint()));
                Assert.Throws<ArgumentNullException>(() => new SaveDocument(Fixture.SaveId, null!));
                Assert.Throws<ArgumentNullException>(() => SaveCodec.Encode(null!));
            });
        }

        /// <summary>重新签出外层摘要，让结构破坏测试不会只命中摘要错误。</summary>
        internal static void Resign(byte[] bytes)
        {
            byte[] digest = SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32));
            digest.CopyTo(bytes, bytes.Length - 32);
        }
    }
}
