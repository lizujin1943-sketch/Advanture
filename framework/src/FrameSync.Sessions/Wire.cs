using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace FrameSync.Sessions
{
    // 协议细节不作为游戏接入 API。整数统一小端；Guid 采用 .NET ToByteArray 的固定顺序。
    internal enum MessageKind : byte { Snapshot = 1, Ready = 2, Start = 3, Input = 4, Final = 5, Close = 6, Fault = 7, Progress = 8 }

    internal sealed class FinalFrame
    {
        public FinalFrame(InputFrame frame, string hash) { Frame = frame; Hash = hash; }
        public InputFrame Frame { get; }
        public string Hash { get; }
    }

    /// <summary>阶段 B 的显式二进制格式：版本、会话与运行编号在每个消息头内。</summary>
    internal static class Wire
    {
        internal const int HeaderBytes = 31, ChunkOverhead = 45, FinalOverhead = 75;
        private const uint Magic = 0x31425346; // "FSB1"
        private const ushort Version = 2;
        internal static byte Channel(MessageKind kind) => kind == MessageKind.Snapshot ? (byte)3 :
            (kind == MessageKind.Input || kind == MessageKind.Final ? (byte)0 : (byte)1);

        internal static PacketWriter Begin(MessageKind kind, Guid sessionId, ulong epoch, int capacity)
        {
            var writer = new PacketWriter(capacity); writer.U32(Magic); writer.U16(Version); writer.U8((byte)kind);
            writer.Raw(sessionId.ToByteArray()); writer.U64(epoch); return writer;
        }
        internal static MessageKind ReadHeader(ref PacketReader reader, out Guid sessionId, out ulong epoch)
        {
            if (reader.U32() != Magic || reader.U16() != Version) throw new InvalidDataException("Unsupported session protocol.");
            byte kind = reader.U8();
            if (kind < 1 || kind > 8) throw new InvalidDataException("Unknown message kind.");
            sessionId = new Guid(reader.Raw(16)); epoch = reader.U64(); return (MessageKind)kind;
        }
        internal static byte[] Snapshot(Guid id, ulong epoch, ushort slot, byte[] snapshot, int offset, int length)
        {
            var w = Begin(MessageKind.Snapshot, id, epoch, checked(ChunkOverhead + length));
            w.U16(slot); w.U32((uint)snapshot.Length); w.U32((uint)offset); w.Blob(snapshot.AsSpan(offset, length)); return w.Finish();
        }
        internal static byte[] HashMessage(MessageKind kind, Guid id, ulong epoch, string hash)
        { var w = Begin(kind, id, epoch, HeaderBytes + 32); WriteHash(w, hash); return w.Finish(); }
        // Ready 同时声明客户端能缓存的未来帧数，服务器按双方较小值限制领先量。
        internal static byte[] Ready(Guid id, ulong epoch, string hash, int receiveWindow)
        {
            var w = Begin(MessageKind.Ready, id, epoch, HeaderBytes + 34);
            WriteHash(w, hash); w.U16(checked((ushort)receiveWindow)); return w.Finish();
        }
        internal static byte[] Terminal(MessageKind kind, Guid id, ulong epoch, ulong tick)
        { var w = Begin(kind, id, epoch, HeaderBytes + 8); w.U64(tick); return w.Finish(); }
        internal static byte[] Input(Guid id, ulong epoch, ulong tick, RecordedInput input, int maxBytes)
        {
            var w = Begin(MessageKind.Input, id, epoch, maxBytes); w.U64(tick); w.U16(input.Slot);
            w.U64(input.Generation); w.Blob(input.Payload.Span); return w.Finish();
        }
        internal static RecordedInput ReadInput(ref PacketReader r, int maxBytes, out ulong tick)
        { tick = r.U64(); ushort slot = r.U16(); ulong generation = r.U64(); return new RecordedInput(slot, generation, r.Blob(maxBytes)); }
        internal static byte[] Final(InputFrame frame, string hash, int maxBytes)
        {
            var w = Begin(MessageKind.Final, frame.SessionId, frame.Epoch, maxBytes);
            w.U64(frame.Tick); w.U16((ushort)frame.MemberEvents.Count);
            foreach (var e in frame.MemberEvents) { w.U64(e.Sequence); w.U8((byte)e.Kind); w.U16(e.Slot); w.U64(e.Generation); }
            w.U16((ushort)frame.Inputs.Count);
            foreach (var input in frame.Inputs)
            { w.U16(input.Slot); w.U64(input.Generation); w.U8((byte)input.Origin); w.Blob(input.Payload.Span); }
            WriteHash(w, hash); return w.Finish();
        }
        internal static FinalFrame ReadFinal(ref PacketReader r, Guid id, ulong epoch, RuntimeLimits limits)
        {
            ulong tick = r.U64(); int eventCount = r.U16();
            if (eventCount > Math.Min(4, limits.MaxParticipants)) throw new InvalidDataException("Too many member events.");
            var events = new MemberEvent[eventCount];
            for (int i = 0; i < events.Length; i++) events[i] = new MemberEvent(r.U64(), (MemberEventKind)r.U8(), r.U16(), r.U64());
            int count = r.U16();
            if (count > Math.Min(4, limits.MaxParticipants)) throw new InvalidDataException("Invalid frame participant count.");
            var inputs = new List<RecordedInput>(count); int previous = -1;
            for (int i = 0; i < count; i++)
            {
                ushort slot = r.U16(); ulong generation = r.U64(); byte origin = r.U8();
                if (slot <= previous || origin > (byte)InputOrigin.MissingSubstitute) throw new InvalidDataException("Noncanonical input order or source.");
                previous = slot; inputs.Add(new RecordedInput(slot, generation, r.Blob(limits.MaxInputBytes), (InputOrigin)origin));
            }
            return new FinalFrame(new InputFrame(id, epoch, tick, inputs, events), ReadHash(ref r));
        }
        internal static string ReadHash(ref PacketReader r)
        {
            ReadOnlySpan<byte> bytes = r.Raw(32); var chars = new char[64]; const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++) { chars[2 * i] = hex[bytes[i] >> 4]; chars[2 * i + 1] = hex[bytes[i] & 15]; }
            return new string(chars);
        }
        private static void WriteHash(PacketWriter w, string hash)
        {
            if (hash.Length != 64) throw new ArgumentException("Expected SHA-256 hex digest.");
            for (int i = 0; i < 64; i += 2) w.U8(Convert.ToByte(hash.Substring(i, 2), 16));
        }
    }

    // 使用固定上限缓冲区。写前检查剩余空间，不依赖 MemoryStream 无限制扩容。
    internal sealed class PacketWriter
    {
        private readonly byte[] _bytes;
        private int _offset;
        public PacketWriter(int capacity) { _bytes = new byte[capacity]; }
        public void Raw(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > _bytes.Length - _offset) throw new InvalidDataException("Message byte budget exceeded.");
            bytes.CopyTo(_bytes.AsSpan(_offset)); _offset += bytes.Length;
        }
        public void U8(byte value) { Span<byte> b = stackalloc byte[1]; b[0] = value; Raw(b); }
        public void U16(ushort value) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, value); Raw(b); }
        public void U32(uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); Raw(b); }
        public void U64(ulong value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, value); Raw(b); }
        public void Blob(ReadOnlySpan<byte> bytes) { U32((uint)bytes.Length); Raw(bytes); }
        public byte[] Finish() => _bytes.AsSpan(0, _offset).ToArray();
    }

    // 读长度后先校验上限与剩余字节，再切片；外部声明的长度不能直接用于分配数组。
    internal ref struct PacketReader
    {
        private ReadOnlySpan<byte> _remaining;
        public PacketReader(ReadOnlySpan<byte> bytes) { _remaining = bytes; }
        public ReadOnlySpan<byte> Raw(int length)
        {
            if (length < 0 || length > _remaining.Length) throw new InvalidDataException("Truncated session message.");
            var bytes = _remaining.Slice(0, length); _remaining = _remaining.Slice(length); return bytes;
        }
        public byte U8() => Raw(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Raw(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Raw(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Raw(8));
        public int Length(int maximum)
        { uint n = U32(); if (n > (uint)maximum) throw new InvalidDataException("Declared size exceeds capacity."); return (int)n; }
        public ReadOnlySpan<byte> Blob(int maximum) => Raw(Length(maximum));
        public void End() { if (!_remaining.IsEmpty) throw new InvalidDataException("Trailing bytes in session message."); }
    }
}
