using System;

namespace FrameSync.Abstractions
{
    /// <summary>拥有独立副本的不可变字节数据，用于输入、事件和保存的状态。</summary>
    /// <remarks>
    /// 构造时复制传入内容，防止调用者修改原数组时连带改变历史记录。
    /// 正常读取使用只读的 Span；需要可修改数组时使用 ToArray 获取新副本。
    /// </remarks>
    public sealed class ByteString
    {
        // 数组只在本对象内部持有，不通过公开接口暴露可写引用。
        private readonly byte[] _bytes;
        /// <summary>可共享的空字节对象；其内容不能被调用者修改。</summary>
        public static ByteString Empty { get; } = new ByteString(ReadOnlySpan<byte>.Empty);
        /// <summary>复制输入字节，建立与原始缓冲区独立的数据。</summary>
        /// <param name="bytes">待保存的内容；允许为空。</param>
        public ByteString(ReadOnlySpan<byte> bytes) { _bytes = bytes.ToArray(); }
        /// <summary>内容的字节数。</summary>
        public int Length => _bytes.Length;
        /// <summary>直接读取内部内容的只读视图；访问此属性不复制数组。</summary>
        public ReadOnlySpan<byte> Span => _bytes;
        /// <summary>生成可由调用者修改的数组副本。</summary>
        /// <returns>与本对象内容相同、但不共享可写存储的新数组。</returns>
        public byte[] ToArray() => (byte[])_bytes.Clone();
    }
}
