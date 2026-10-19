using System;
using System.IO;
using Google.Protobuf;

namespace FrameSync.Protocol.Protobuf
{
    /// <summary>有容量限制的 Protobuf 编解码入口；不包含连接、模拟或游戏逻辑。</summary>
    public static class ProtobufCodec
    {
        /// <summary>先检查完整消息大小再分配输出缓冲区，长度包括外壳和嵌套字段。</summary>
        public static byte[] Encode(IMessage message, int maximumBytes)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            if (message.CalculateSize() > maximumBytes) throw new InvalidDataException("Message byte budget exceeded.");
            return message.ToByteArray();
        }

        /// <summary>先限制原始包大小，再解析；允许未知扩展字段，但不保留它们参与模拟或重复帧比较。</summary>
        /// <remarks>采用运行库的有限递归深度；字段范围、必需内容及业务状态仍由上层逐项验证。</remarks>
        public static T Decode<T>(MessageParser<T> parser, ReadOnlySpan<byte> bytes, int maximumBytes) where T : IMessage<T>
        {
            if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            if (bytes.Length == 0 || bytes.Length > maximumBytes) throw new InvalidDataException("Invalid protobuf message size.");
            try { return parser.WithDiscardUnknownFields(true).ParseFrom(bytes); }
            catch (InvalidProtocolBufferException error) { throw new InvalidDataException("Malformed protobuf message.", error); }
        }
    }
}
