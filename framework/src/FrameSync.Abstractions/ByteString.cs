using System;

namespace FrameSync.Abstractions
{
    /// <summary>Owned immutable bytes. Construction and ToArray copy; Span is read-only.</summary>
    public sealed class ByteString
    {
        private readonly byte[] _bytes;
        public static ByteString Empty { get; } = new ByteString(ReadOnlySpan<byte>.Empty);
        public ByteString(ReadOnlySpan<byte> bytes) { _bytes = bytes.ToArray(); }
        public int Length => _bytes.Length;
        public ReadOnlySpan<byte> Span => _bytes;
        public byte[] ToArray() => (byte[])_bytes.Clone();
    }
}
