using FrameSync.Abstractions;

namespace FrameSync.Sessions
{
    // 收到但尚未确认执行的帧。保留原始字节，用于识别相互冲突的重复消息。
    internal sealed class BufferedFrame
    {
        public BufferedFrame(FinalFrame record, ByteString bytes) { Record = record; Bytes = bytes; }
        public FinalFrame Record { get; }
        public ByteString Bytes { get; }
    }
}
