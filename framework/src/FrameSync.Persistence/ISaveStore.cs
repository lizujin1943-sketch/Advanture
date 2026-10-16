using System;

namespace FrameSync.Persistence
{
    /// <summary>存档字节的受限读取与整体原子替换约定；核心不接触文件路径或平台 API。</summary>
    /// <remarks>
    /// 实现必须在分配读取缓冲区前检查 maxBytes，不得截断超限内容后当作正常数据返回。
    /// WriteAtomic 成功后新内容完整可见；在提交前失败时旧内容仍完整可用。
    /// 宿主必须为同一位置选择明确的并发策略，不能通过先删除旧内容再写入实现原子性。
    /// </remarks>
    public interface ISaveStore
    {
        /// <summary>读取完整独立字节，缺失、损坏的读取或大小超限时抛异常。</summary>
        /// <param name="maxBytes">允许的最大字节数；必须为正数。</param>
        /// <returns>完整内容的独立数组，不能是共享可变缓冲区。</returns>
        byte[] Read(int maxBytes);
        /// <summary>校验写入预算后原子提交整份内容，不允许暴露部分新内容。</summary>
        /// <param name="bytes">已由 SaveCodec 校验的完整字节；调用期间不得并发修改底层数组。</param>
        /// <param name="maxBytes">允许的最大字节数；必须为正数。</param>
        void WriteAtomic(ReadOnlySpan<byte> bytes, int maxBytes);
    }
}
