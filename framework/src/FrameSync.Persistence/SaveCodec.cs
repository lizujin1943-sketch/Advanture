using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using FrameSync.Runtime;

namespace FrameSync.Persistence
{
    /// <summary>正式存档的版本化、有界、带完整性校验的二进制编码。</summary>
    /// <remarks>
    /// 字段依次为 FSSV 标识、两字节格式版本、16 字节存档 UUID、四字节检查点长度、
    /// 完整检查点和 32 字节 SHA-256；整数小端，UUID 沿用 .NET 固定字节排列。
    /// 摘要覆盖自身以外的全部字节，只检测内容损坏，不提供签名或加密。
    /// 检查点仍由 CheckpointCodec 验证，存档层不解释具体游戏或成员状态。
    /// </remarks>
    public static class SaveCodec
    {
        private const uint Magic = 0x56535346; // 小端写出 ASCII：FSSV。
        private const int HeaderLength = 26;
        private const int DigestLength = 32;
        /// <summary>当前唯一支持的外壳版本；旧版或未来版本都明确拒绝。</summary>
        public const ushort FormatVersion = 1;
        /// <summary>检查点之外的固定字节开销，包含头部和摘要。</summary>
        public const int EnvelopeOverhead = HeaderLength + DigestLength;

        /// <summary>将存档身份与完整检查点封装为独立的受限字节数组。</summary>
        /// <param name="document">由宿主分配身份并捕获确认检查点的存档。</param>
        /// <param name="limits">允许的检查点容量配置，缺省使用 RuntimeLimits 默认值。</param>
        /// <returns>包含完整头部、检查点及末尾摘要的独立字节数组。</returns>
        /// <exception cref="InvalidOperationException">检查点配置或实际编码超过调用方预算。</exception>
        public static byte[] Encode(SaveDocument document, RuntimeLimits? limits = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var cap = limits ?? new RuntimeLimits();
            // 在编码检查点之前验证其声明，避免先按过大的内部预算分配再拒绝。
            RequireAllowedLimits(document.Checkpoint.Limits, cap);
            byte[] checkpoint = CheckpointCodec.Encode(document.Checkpoint);
            if (checkpoint.Length > GetMaximumEncodedBytes(cap) - EnvelopeOverhead)
                throw new InvalidOperationException("Encoded save exceeds its byte budget.");
            var bytes = new byte[checkpoint.Length + EnvelopeOverhead];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), FormatVersion);
            document.SaveId.TryWriteBytes(bytes.AsSpan(6, 16));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(22), (uint)checkpoint.Length);
            checkpoint.CopyTo(bytes, HeaderLength);
            ComputeDigest(bytes.AsSpan(0, bytes.Length - DigestLength), bytes.AsSpan(bytes.Length - DigestLength));
            return bytes;
        }

        /// <summary>在解析不可信字段前检查总大小和摘要，再验证版本、身份及内部检查点。</summary>
        /// <param name="bytes">完整文件内容，不能包含前缀、后缀或分段数据。</param>
        /// <param name="limits">本次载入允许的容量上限，缺省使用 RuntimeLimits 默认值。</param>
        /// <returns>数据有效的独立存档；是否与当前游戏兼容仍由运行器恢复时检查。</returns>
        /// <exception cref="InvalidDataException">损坏、截断、版本不支持或超过容量限制。</exception>
        public static SaveDocument Decode(ReadOnlySpan<byte> bytes, RuntimeLimits? limits = null)
        {
            var cap = limits ?? new RuntimeLimits();
            if (bytes.Length < EnvelopeOverhead || bytes.Length > GetMaximumEncodedBytes(cap))
                throw new InvalidDataException("Invalid save length.");
            ReadOnlySpan<byte> body = bytes.Slice(0, bytes.Length - DigestLength);
            Span<byte> digest = stackalloc byte[DigestLength];
            ComputeDigest(body, digest);
            if (!CryptographicOperations.FixedTimeEquals(digest, bytes.Slice(bytes.Length - DigestLength)))
                throw new InvalidDataException("Save checksum does not match.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(body) != Magic ||
                BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4)) != FormatVersion)
                throw new InvalidDataException("Unsupported save format.");
            var saveId = new Guid(body.Slice(6, 16));
            if (saveId == Guid.Empty) throw new InvalidDataException("Save identity must not be empty.");
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(22));
            // 必须恰好占满正文尾部；先验证无符号长度，再进行切片或内层分配。
            if (length != (uint)(body.Length - HeaderLength) || length > cap.MaxCheckpointBytes)
                throw new InvalidDataException("Invalid embedded checkpoint length.");
            var checkpoint = CheckpointCodec.Decode(body.Slice(HeaderLength, (int)length), cap);
            return new SaveDocument(saveId, checkpoint);
        }

        /// <summary>计算含外壳的最大编码长度，为存储适配器提供分配前检查的统一预算。</summary>
        /// <param name="limits">检查点容量配置，缺省使用默认值。</param>
        /// <returns>检查点字节上限加 58 字节外壳；总和超过 int 范围时收紧到 int.MaxValue。</returns>
        public static int GetMaximumEncodedBytes(RuntimeLimits? limits = null)
        {
            int limit = (limits ?? new RuntimeLimits()).MaxCheckpointBytes;
            return (int)Math.Min(int.MaxValue, (long)limit + EnvelopeOverhead);
        }

        /// <summary>保证编码后同一容量配置也能解码，防止嵌入超出加载上限的声明。</summary>
        /// <param name="stored">检查点原有配置，不会被改写或放宽。</param>
        /// <param name="cap">当前保存允许的最大配置。</param>
        private static void RequireAllowedLimits(RuntimeLimits stored, RuntimeLimits cap)
        {
            if (stored.MaxParticipants > cap.MaxParticipants || stored.MaxInputBytes > cap.MaxInputBytes ||
                stored.MaxCheckpointBytes > cap.MaxCheckpointBytes || stored.HistoryFrames > cap.HistoryFrames ||
                stored.MaxEventsPerFrame > cap.MaxEventsPerFrame || stored.MaxEventBytes > cap.MaxEventBytes)
                throw new InvalidOperationException("Checkpoint limits exceed the save limits.");
        }

        /// <summary>直接对有界字节计算 SHA-256，避免为摘要再复制整份正文。</summary>
        /// <param name="body">不包含末尾摘要的正文。</param>
        /// <param name="destination">恰为 32 字节的摘要输出区。</param>
        private static void ComputeDigest(ReadOnlySpan<byte> body, Span<byte> destination)
        {
            using var sha = SHA256.Create();
            if (!sha.TryComputeHash(body, destination, out int written) || written != DigestLength)
                throw new CryptographicException("Unable to compute save checksum.");
        }
    }
}
