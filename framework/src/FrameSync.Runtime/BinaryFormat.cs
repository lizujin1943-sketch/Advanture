using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    /// <summary>按明确的字段宽度和字节顺序写入数据，并限制正文缓冲区大小。</summary>
    /// <remarks>
    /// Canonical 表示约定唯一的编码形式：无符号整数统一小端，文本统一严格 UTF-8。
    /// 所有实际写入都经过 Bytes，因此单个字段与整份正文共享同一字节预算。
    /// </remarks>
    internal sealed class CanonicalWriter : IDisposable
    {
        // 只累积正文；外层编码器预先扣除摘要大小，最后再调用 DataEnvelope.Seal。
        private readonly MemoryStream _stream = new MemoryStream();
        // 正文长度和 MemoryStream 内部容量的上限，不代表整个操作的总内存上限。
        private readonly int _limit;
        /// <summary>建立有大小上限的正文写入器。</summary>
        /// <param name="limit">调用层已计算好的正文最大字节数。</param>
        public CanonicalWriter(int limit) { _limit = limit; }
        /// <summary>直接写入原始字节，不额外写入长度。</summary>
        /// <param name="value">要追加的内容。</param>
        public void Bytes(ReadOnlySpan<byte> value)
        {
            // 先按 long 计算写入后大小，检查通过后才扩容，避免先分配再发现超限。
            long required = _stream.Length + value.Length;
            if (required > _limit) throw new InvalidOperationException("Encoded data exceeds its byte budget.");
            // 通常按倍数扩容减少复制；接近预算时最多扩到上限，不能由默认扩容越过预算。
            if (_stream.Capacity < required)
                _stream.Capacity = (int)Math.Min(_limit, Math.Max(required, Math.Max(256L, (long)_stream.Capacity * 2)));
            _stream.Write(value);
        }
        /// <summary>写入 1 字节无符号整数；使用栈上临时缓冲区避免每个数字都创建数组。</summary>
        /// <param name="value">待写入数值。</param>
        public void U8(byte value) { Span<byte> b = stackalloc byte[1]; b[0] = value; Bytes(b); }
        /// <summary>按小端写入 2 字节无符号整数。</summary>
        /// <param name="value">待写入数值。</param>
        public void U16(ushort value) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, value); Bytes(b); }
        /// <summary>按小端写入 4 字节无符号整数。</summary>
        /// <param name="value">待写入数值。</param>
        public void U32(uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); Bytes(b); }
        /// <summary>按小端写入 8 字节无符号整数。</summary>
        /// <param name="value">待写入数值。</param>
        public void U64(ulong value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, value); Bytes(b); }
        /// <summary>按 .NET Guid.ToByteArray 的固定 16 字节排列写入身份。</summary>
        /// <param name="value">待写入身份。</param>
        /// <remarks>这不是简单地按 Guid 显示字符串顺序排列字节，读取端必须采用相同约定。</remarks>
        public void Guid(Guid value) => Bytes(value.ToByteArray());
        /// <summary>先写 4 字节长度，再写可变长字节内容。</summary>
        /// <param name="value">待写入内容；允许为空。</param>
        public void Blob(ReadOnlySpan<byte> value) { U32((uint)value.Length); Bytes(value); }
        /// <summary>用无字节序标记的严格 UTF-8 编码文本，再按长度加内容写入。</summary>
        /// <param name="value">待写入文本；非法字符序列会被拒绝而不是静默替换。</param>
        public void Text(string value) => Blob(new UTF8Encoding(false, true).GetBytes(value));
        /// <summary>复制当前正文；不包含外层校验摘要。</summary>
        /// <returns>独立的正文数组。</returns>
        public byte[] ToArray() => _stream.ToArray();
        /// <summary>释放正文缓冲流。</summary>
        public void Dispose() => _stream.Dispose();
    }

    /// <summary>按固定格式读取只读字节，每次读取都先检查剩余长度。</summary>
    /// <remarks>
    /// 使用 ref struct 保存字节视图，不复制整份输入。读取后切掉已消费的前缀，
    /// 因而辅助函数必须通过 ref 接收读取器，才能推进调用方的同一个读取位置。
    /// </remarks>
    internal ref struct CanonicalReader
    {
        // 尚未消费的输入视图；数据本身仍由调用层持有。
        private ReadOnlySpan<byte> _remaining;
        /// <summary>从数据起点开始读取，不复制原始字节。</summary>
        /// <param name="data">外层已经完成总大小和摘要检查的正文。</param>
        public CanonicalReader(ReadOnlySpan<byte> data) { _remaining = data; }
        /// <summary>还没有被读取的字节数。</summary>
        public int Remaining => _remaining.Length;
        /// <summary>读取指定数量的原始字节，并推进读取位置。</summary>
        /// <param name="count">要读取的字节数，不能为负或超过剩余长度。</param>
        /// <returns>原数据上的只读切片，不分配新数组。</returns>
        public ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || count > _remaining.Length) throw new InvalidDataException("Truncated data.");
            var result = _remaining.Slice(0, count); _remaining = _remaining.Slice(count); return result;
        }
        /// <summary>读取 1 字节无符号整数。</summary>
        public byte U8() => Bytes(1)[0];
        /// <summary>按小端读取 2 字节无符号整数。</summary>
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));
        /// <summary>按小端读取 4 字节无符号整数。</summary>
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        /// <summary>按小端读取 8 字节无符号整数。</summary>
        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(8));
        /// <summary>按 .NET 的字节排列从 16 字节还原 Guid，与写入器对应。</summary>
        public Guid Guid() => new Guid(Bytes(16));
        /// <summary>读取 4 字节长度或数量，在转换为 int 和分配数据前检查上限。</summary>
        /// <param name="maximum">此字段允许的最大值，由外层有效配置提供。</param>
        /// <returns>已经过范围检查、可用于后续读取或分配的数量。</returns>
        public int Count(int maximum)
        {
            uint count = U32();
            if (count > maximum) throw new InvalidDataException("Declared length/count exceeds its limit.");
            return (int)count;
        }
        /// <summary>读取长度前缀和对应字节，先限制声明长度，再检查实际剩余数据。</summary>
        /// <param name="maximum">该内容允许的最大字节数。</param>
        /// <returns>原数据上的只读内容视图。</returns>
        public ReadOnlySpan<byte> Blob(int maximum) => Bytes(Count(maximum));
        /// <summary>读取最多 128 个 UTF-8 字节的文本；无效编码直接失败。</summary>
        public string Text() => new UTF8Encoding(false, true).GetString(Blob(128));
        /// <summary>要求数据恰好读完，拒绝额外尾随字节，避免同一内容出现多种编码。</summary>
        public void Finish()
        { if (!_remaining.IsEmpty) throw new InvalidDataException("Unexpected trailing data."); }
    }

    /// <summary>在编码正文后附加或验证 SHA-256 摘要。</summary>
    /// <remarks>
    /// 摘要用于发现损坏和比较内容，不是加密或身份认证；知道格式的人也能重新计算摘要。
    /// 网络发送者认证属于后续通信层，不能把摘要校验通过理解为数据来源可信。
    /// </remarks>
    internal static class DataEnvelope
    {
        /// <summary>SHA-256 摘要固定占 32 字节。</summary>
        public const int DigestLength = 32;
        /// <summary>计算正文摘要，输出“正文 + 摘要”的独立数组。</summary>
        /// <param name="body">已由上层写入器限制大小的编码正文。</param>
        /// <returns>包含末尾校验摘要的完整数据。</returns>
        public static byte[] Seal(byte[] body)
        {
            using var sha = SHA256.Create();
            var digest = sha.ComputeHash(body);
            var output = new byte[checked(body.Length + DigestLength)];
            body.CopyTo(output, 0); digest.CopyTo(output, body.Length);
            return output;
        }
        /// <summary>先检查完整数据大小，再核对摘要，最后返回正文视图。</summary>
        /// <param name="bytes">包含正文和末尾摘要的完整数据。</param>
        /// <param name="maximum">完整数据允许的最大字节数，包含摘要。</param>
        /// <returns>去掉末尾摘要后的原数据视图。</returns>
        public static ReadOnlySpan<byte> Open(ReadOnlySpan<byte> bytes, int maximum)
        {
            // 最短正文至少包含 4 字节格式标识和 2 字节版本号；先检查再复制正文做哈希。
            if (bytes.Length > maximum || bytes.Length < DigestLength + 6)
                throw new InvalidDataException("Invalid envelope length.");
            var body = bytes.Slice(0, bytes.Length - DigestLength);
            using var sha = SHA256.Create();
            var expected = sha.ComputeHash(body.ToArray());
            if (!bytes.Slice(body.Length).SequenceEqual(expected))
                throw new InvalidDataException("Integrity checksum mismatch.");
            return body;
        }
        /// <summary>将字节转换为无分隔符的小写十六进制，便于报告和跨进程比较。</summary>
        /// <param name="bytes">通常为 SHA-256 摘要字节。</param>
        /// <returns>每字节两个十六进制字符的文本。</returns>
        public static string Hex(ReadOnlySpan<byte> bytes) => BitConverter.ToString(bytes.ToArray()).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>检查点历史和输入录制共用的一帧输入格式，避免两处编码规则分叉。</summary>
    /// <remarks>会话和运行编号由外层统一保存，每帧只保存帧号与参与者输入。</remarks>
    internal static class InputFormat
    {
        /// <summary>按帧号、人数和位置顺序写入一帧输入。</summary>
        /// <param name="writer">外层共享的正文写入器。</param>
        /// <param name="frame">已经按 Slot 排序的输入帧。</param>
        public static void Write(CanonicalWriter writer, InputFrame frame)
        {
            writer.U64(frame.Tick); writer.U16((ushort)frame.Inputs.Count);
            foreach (var input in frame.Inputs)
            {
                writer.U16(input.Slot); writer.U64(input.Generation);
                writer.U8((byte)input.Origin); writer.Blob(input.Payload.Span);
            }
        }
        /// <summary>按会话名单读取完整一帧，拒绝人数、顺序、控制权或来源不匹配。</summary>
        /// <param name="reader">通过引用传递的读取位置，读取后调用方继续从下一字段处理。</param>
        /// <param name="session">外层保存的会话配置，用于补回身份信息并验证输入位置。</param>
        /// <param name="limits">人数和单条输入大小上限。</param>
        /// <returns>结构合法的输入帧；游戏内容格式仍由游戏模块检查。</returns>
        public static InputFrame Read(ref CanonicalReader reader, SessionDescriptor session, RuntimeLimits limits)
        {
            ulong tick = reader.U64(); int count = reader.U16();
            if (count != session.Participants.Count || count > limits.MaxParticipants)
                throw new InvalidDataException("Invalid frame participant count.");
            var inputs = new RecordedInput[count];
            for (int i = 0; i < count; i++)
            {
                ushort slot = reader.U16(); ulong generation = reader.U64(); byte origin = reader.U8();
                // 必须检查文件中的原始顺序，不能依赖 InputFrame 构造时排序来修正坏编码。
                if (slot != session.Participants[i].Slot || generation != session.Participants[i].Generation || origin > 1)
                    throw new InvalidDataException("Noncanonical input order, control generation, or source.");
                inputs[i] = new RecordedInput(slot, generation, reader.Blob(limits.MaxInputBytes), (InputOrigin)origin);
            }
            return new InputFrame(session.SessionId, session.Epoch, tick, inputs);
        }
        /// <summary>计算一帧输入在当前格式中的精确字节数，不实际分配编码缓冲区。</summary>
        /// <param name="frame">待估算输入帧。</param>
        /// <returns>帧头、各输入记录头和内容的总字节数。</returns>
        public static long EncodedSize(InputFrame frame)
        {
            // 帧头：Tick 8 + 人数 2；每条记录头：Slot 2 + Generation 8 + Origin 1 + 长度 4。
            long bytes = 10;
            foreach (var input in frame.Inputs) bytes = checked(bytes + 15 + input.Payload.Length);
            return bytes;
        }
    }
}
