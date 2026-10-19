namespace FrameSync.Sessions
{
    // 收到但尚未确认执行的帧。只保留已验证的不可变领域数据，重复消息按含义比较。
    // Protobuf 的字段顺序和未知字段允许变化，不能把原始网络字节当成帧身份。
    internal sealed class BufferedFrame
    {
        public BufferedFrame(FinalFrame record) { Record = record; }
        public FinalFrame Record { get; }
    }
}
