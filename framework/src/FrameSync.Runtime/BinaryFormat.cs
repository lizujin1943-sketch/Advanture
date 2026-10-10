using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    internal sealed class CanonicalWriter : IDisposable
    {
        private readonly MemoryStream _stream = new MemoryStream();
        private readonly int _limit;
        public CanonicalWriter(int limit) { _limit = limit; }
        public void Bytes(ReadOnlySpan<byte> value)
        {
            long required = _stream.Length + value.Length;
            if (required > _limit) throw new InvalidOperationException("Encoded data exceeds its byte budget.");
            if (_stream.Capacity < required)
                _stream.Capacity = (int)Math.Min(_limit, Math.Max(required, Math.Max(256L, (long)_stream.Capacity * 2)));
            _stream.Write(value);
        }
        public void U8(byte value) { Span<byte> b = stackalloc byte[1]; b[0] = value; Bytes(b); }
        public void U16(ushort value) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, value); Bytes(b); }
        public void U32(uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); Bytes(b); }
        public void U64(ulong value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, value); Bytes(b); }
        public void Guid(Guid value) => Bytes(value.ToByteArray());
        public void Blob(ReadOnlySpan<byte> value) { U32((uint)value.Length); Bytes(value); }
        public void Text(string value) => Blob(new UTF8Encoding(false, true).GetBytes(value));
        public byte[] ToArray() => _stream.ToArray();
        public void Dispose() => _stream.Dispose();
    }

    internal ref struct CanonicalReader
    {
        private ReadOnlySpan<byte> _remaining;
        public CanonicalReader(ReadOnlySpan<byte> data) { _remaining = data; }
        public int Remaining => _remaining.Length;
        public ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || count > _remaining.Length) throw new InvalidDataException("Truncated data.");
            var result = _remaining.Slice(0, count); _remaining = _remaining.Slice(count); return result;
        }
        public byte U8() => Bytes(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(8));
        public Guid Guid() => new Guid(Bytes(16));
        public int Count(int maximum)
        {
            uint count = U32();
            if (count > maximum) throw new InvalidDataException("Declared length/count exceeds its limit.");
            return (int)count;
        }
        public ReadOnlySpan<byte> Blob(int maximum) => Bytes(Count(maximum));
        public string Text() => new UTF8Encoding(false, true).GetString(Blob(128));
        public void Finish()
        { if (!_remaining.IsEmpty) throw new InvalidDataException("Unexpected trailing data."); }
    }

    internal static class DataEnvelope
    {
        public const int DigestLength = 32;
        public static byte[] Seal(byte[] body)
        {
            using var sha = SHA256.Create();
            var digest = sha.ComputeHash(body);
            var output = new byte[checked(body.Length + DigestLength)];
            body.CopyTo(output, 0); digest.CopyTo(output, body.Length);
            return output;
        }
        public static ReadOnlySpan<byte> Open(ReadOnlySpan<byte> bytes, int maximum)
        {
            if (bytes.Length > maximum || bytes.Length < DigestLength + 6)
                throw new InvalidDataException("Invalid envelope length.");
            var body = bytes.Slice(0, bytes.Length - DigestLength);
            using var sha = SHA256.Create();
            var expected = sha.ComputeHash(body.ToArray());
            if (!bytes.Slice(body.Length).SequenceEqual(expected))
                throw new InvalidDataException("Integrity checksum mismatch.");
            return body;
        }
        public static string Hex(ReadOnlySpan<byte> bytes) => BitConverter.ToString(bytes.ToArray()).Replace("-", "").ToLowerInvariant();
    }

    internal static class InputFormat
    {
        public static void Write(CanonicalWriter writer, InputFrame frame)
        {
            writer.U64(frame.Tick); writer.U16((ushort)frame.Inputs.Count);
            foreach (var input in frame.Inputs)
            {
                writer.U16(input.Slot); writer.U64(input.Generation);
                writer.U8((byte)input.Origin); writer.Blob(input.Payload.Span);
            }
        }
        public static InputFrame Read(ref CanonicalReader reader, SessionDescriptor session, RuntimeLimits limits)
        {
            ulong tick = reader.U64(); int count = reader.U16();
            if (count != session.Participants.Count || count > limits.MaxParticipants)
                throw new InvalidDataException("Invalid frame participant count.");
            var inputs = new RecordedInput[count];
            for (int i = 0; i < count; i++)
            {
                ushort slot = reader.U16(); ulong generation = reader.U64(); byte origin = reader.U8();
                if (slot != session.Participants[i].Slot || generation != session.Participants[i].Generation || origin > 1)
                    throw new InvalidDataException("Noncanonical input order, control generation, or source.");
                inputs[i] = new RecordedInput(slot, generation, reader.Blob(limits.MaxInputBytes), (InputOrigin)origin);
            }
            return new InputFrame(session.SessionId, session.Epoch, tick, inputs);
        }
        public static long EncodedSize(InputFrame frame)
        {
            long bytes = 10;
            foreach (var input in frame.Inputs) bytes = checked(bytes + 15 + input.Payload.Length);
            return bytes;
        }
    }
}
