using System;
using System.Collections.Generic;
using System.IO;
using FrameSync.Abstractions;
using FrameSync.Protocol.Protobuf;
using FrameSync.Runtime;
using Google.Protobuf;
using ProtoBytes = Google.Protobuf.ByteString;

namespace FrameSync.Sessions
{
    // 这是会话内部的消息分类，不是网络字节中的枚举；网络类型由 proto 的 oneof 表示。
    internal enum MessageKind : byte { Snapshot = 1, Ready = 2, Start = 3, Input = 4, Final = 5, Close = 6, Fault = 7, Progress = 8 }

    /// <summary>完成协议校验的不可变最终帧；不保留可修改的 Protobuf 对象。</summary>
    internal sealed class FinalFrame
    {
        public FinalFrame(InputFrame frame, string hash) { Frame = frame; Hash = hash; }
        public InputFrame Frame { get; }
        public string Hash { get; }

        /// <summary>比较参与模拟的全部字段和状态摘要，允许网络字段顺序或未知扩展字段不同。</summary>
        public bool SameAs(FinalFrame other)
        {
            var a = Frame; var b = other.Frame;
            if (Hash != other.Hash || a.SessionId != b.SessionId || a.Epoch != b.Epoch || a.Tick != b.Tick ||
                a.Inputs.Count != b.Inputs.Count || a.MemberEvents.Count != b.MemberEvents.Count) return false;
            for (int i = 0; i < a.Inputs.Count; i++)
            {
                var x = a.Inputs[i]; var y = b.Inputs[i];
                if (x.Slot != y.Slot || x.Generation != y.Generation || x.Origin != y.Origin ||
                    !x.Payload.Span.SequenceEqual(y.Payload.Span)) return false;
            }
            for (int i = 0; i < a.MemberEvents.Count; i++)
            {
                var x = a.MemberEvents[i]; var y = b.MemberEvents[i];
                if (x.Sequence != y.Sequence || x.Kind != y.Kind || x.Slot != y.Slot || x.Generation != y.Generation) return false;
            }
            return true;
        }
    }

    /// <summary>将领域对象和 Protobuf 消息互相转换；不负责重传、帧推进或游戏输入编码。</summary>
    internal static class Wire
    {
        internal const uint Version = 3;
        // 最长整数编码对应的外壳大小；从生成类型计算，不把旧版固定头长套到 Protobuf 上。
        private static readonly int MaximumEnvelopeSize = Envelope(Guid.Empty, ulong.MaxValue).CalculateSize();
        internal static byte Channel(MessageKind kind) => kind == MessageKind.Snapshot ? (byte)3 :
            (kind == MessageKind.Input || kind == MessageKind.Final ? (byte)0 : (byte)1);
        private static SessionPacket Envelope(Guid id, ulong epoch) => new SessionPacket
        { ProtocolVersion = Version, SessionId = ProtoBytes.CopyFrom(id.ToByteArray()), Epoch = epoch };

        /// <summary>验证外壳及已知消息类型。旧固定布局和不兼容版本直接拒绝，不猜测格式。</summary>
        internal static SessionPacket Decode(ReadOnlySpan<byte> bytes, int maximumBytes, out MessageKind kind, out Guid id, out ulong epoch)
        {
            var packet = ProtobufCodec.Decode(SessionPacket.Parser, bytes, maximumBytes);
            if (packet.ProtocolVersion != Version) throw new InvalidDataException("Unsupported session protocol.");
            if (packet.SessionId.Length != 16 || packet.Epoch == 0) throw new InvalidDataException("Invalid session identity.");
            id = new Guid(packet.SessionId.Span); epoch = packet.Epoch;
            if (id == Guid.Empty) throw new InvalidDataException("Missing session identity.");
            kind = packet.BodyCase switch
            {
                SessionPacket.BodyOneofCase.Snapshot => MessageKind.Snapshot,
                SessionPacket.BodyOneofCase.Ready => MessageKind.Ready,
                SessionPacket.BodyOneofCase.Start => MessageKind.Start,
                SessionPacket.BodyOneofCase.Input => MessageKind.Input,
                SessionPacket.BodyOneofCase.Final => MessageKind.Final,
                SessionPacket.BodyOneofCase.Close => MessageKind.Close,
                SessionPacket.BodyOneofCase.Fault => MessageKind.Fault,
                SessionPacket.BodyOneofCase.Progress => MessageKind.Progress,
                _ => throw new InvalidDataException("Missing or unknown session message kind.")
            };
            return packet;
        }

        /// <summary>检查点仅套分块外壳，原始 data 字节、校验和及存档格式保持不变。</summary>
        internal static byte[] Snapshot(Guid id, ulong epoch, ushort slot, byte[] snapshot, int offset, int length)
        {
            var p = Envelope(id, epoch);
            p.Snapshot = new SnapshotMessage { Slot = slot, TotalLength = (uint)snapshot.Length, Offset = checked((uint)offset),
                Data = ProtoBytes.CopyFrom(snapshot, offset, length) };
            return ProtobufCodec.Encode(p, MaxSnapshotMessageBytes(length));
        }
        internal static byte[] HashMessage(MessageKind kind, Guid id, ulong epoch, string hash)
        {
            if (kind != MessageKind.Start) throw new ArgumentOutOfRangeException(nameof(kind));
            var p = Envelope(id, epoch); p.Start = new StartMessage { InitialHash = HashBytes(hash) };
            return ProtobufCodec.Encode(p, 128);
        }
        // Ready 声明客户端的未来帧缓存窗口，服务器按双方较小值限制领先量。
        internal static byte[] Ready(Guid id, ulong epoch, string hash, int receiveWindow)
        {
            var p = Envelope(id, epoch);
            p.Ready = new ReadyMessage { InitialHash = HashBytes(hash), ReceiveWindow = checked((uint)receiveWindow) };
            return ProtobufCodec.Encode(p, 128);
        }
        internal static byte[] Terminal(MessageKind kind, Guid id, ulong epoch, ulong tick)
        {
            var p = Envelope(id, epoch); var terminal = new TerminalMessage { Tick = tick };
            switch (kind)
            {
                case MessageKind.Close: p.Close = terminal; break;
                case MessageKind.Fault: p.Fault = terminal; break;
                case MessageKind.Progress: p.Progress = terminal; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return ProtobufCodec.Encode(p, 128);
        }
        internal static byte[] Input(Guid id, ulong epoch, ulong tick, RecordedInput input, int maxBytes)
        {
            var p = Envelope(id, epoch);
            p.Input = new PlayerInputMessage { Tick = tick, Slot = input.Slot, Generation = input.Generation,
                Payload = ProtoBytes.CopyFrom(input.Payload.Span) };
            return ProtobufCodec.Encode(p, maxBytes);
        }
        /// <summary>bytes 是游戏规范输入的原始内容；范围验证完成后复制到不可变领域对象。</summary>
        internal static RecordedInput ReadInput(PlayerInputMessage input, int maxBytes, out ulong tick)
        {
            tick = input.Tick;
            if (tick == 0 || input.Payload.Length > maxBytes) throw new InvalidDataException("Invalid player input size or tick.");
            return new RecordedInput(ReadSlot(input.Slot), input.Generation, input.Payload.Span);
        }
        internal static byte[] Final(InputFrame frame, string hash, int maxBytes)
        {
            var p = Envelope(frame.SessionId, frame.Epoch);
            var final = new FinalFrameMessage { Tick = frame.Tick, StateHash = HashBytes(hash) };
            foreach (var input in frame.Inputs)
                final.Inputs.Add(new InputRecord { Slot = input.Slot, Generation = input.Generation,
                    Origin = (InputSource)input.Origin, Payload = ProtoBytes.CopyFrom(input.Payload.Span) });
            foreach (var e in frame.MemberEvents)
                final.MemberEvents.Add(new MemberChange { Sequence = e.Sequence, Kind = (MemberChangeKind)e.Kind, Slot = e.Slot, Generation = e.Generation });
            p.Final = final; return ProtobufCodec.Encode(p, maxBytes);
        }
        /// <summary>Protobuf 只保证能解码；人数、枚举、位置顺序和输入大小仍须在进入模拟前验证。</summary>
        internal static FinalFrame ReadFinal(FinalFrameMessage final, Guid id, ulong epoch, RuntimeLimits limits)
        {
            int maximum = Math.Min(4, limits.MaxParticipants);
            if (final.MemberEvents.Count > maximum || final.Inputs.Count > maximum)
                throw new InvalidDataException("Too many frame inputs or member events.");
            var events = new MemberEvent[final.MemberEvents.Count];
            for (int i = 0; i < events.Length; i++)
            {
                var e = final.MemberEvents[i];
                // 先检查 int 范围再转 byte 枚举，避免 257 之类的数值被截断成合法值 1。
                if ((int)e.Kind < 1 || (int)e.Kind > 3) throw new InvalidDataException("Unknown member event kind.");
                events[i] = new MemberEvent(e.Sequence, (MemberEventKind)e.Kind, ReadSlot(e.Slot), e.Generation);
            }
            var inputs = new List<RecordedInput>(final.Inputs.Count); int previous = -1;
            foreach (var input in final.Inputs)
            {
                ushort slot = ReadSlot(input.Slot);
                if (slot <= previous || (int)input.Origin < 0 || (int)input.Origin > 1 || input.Payload.Length > limits.MaxInputBytes)
                    throw new InvalidDataException("Invalid frame input order, source or size.");
                previous = slot;
                inputs.Add(new RecordedInput(slot, input.Generation, input.Payload.Span, (InputOrigin)input.Origin));
            }
            return new FinalFrame(new InputFrame(id, epoch, final.Tick, inputs, events), ReadHash(final.StateHash));
        }
        internal static ushort ReadSlot(uint slot)
        { if (slot > ushort.MaxValue) throw new InvalidDataException("Slot exceeds supported range."); return (ushort)slot; }
        internal static string ReadHash(ProtoBytes bytes)
        {
            if (bytes.Length != 32) throw new InvalidDataException("Expected 32-byte SHA-256 digest.");
            var chars = new char[64]; const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++) { chars[2 * i] = hex[bytes[i] >> 4]; chars[2 * i + 1] = hex[bytes[i] & 15]; }
            return new string(chars);
        }
        private static ProtoBytes HashBytes(string hash)
        {
            if (hash.Length != 64) throw new ArgumentException("Expected SHA-256 hex digest.");
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hash.Substring(2 * i, 2), 16);
            return ProtoBytes.CopyFrom(bytes);
        }

        // 长度前缀也是变长整数；预算必须覆盖它跨越 127、16383 等边界时增加的字节。
        private static int LengthDelimitedSize(int field, int bytes) => checked(CodedOutputStream.ComputeTagSize(field) +
            CodedOutputStream.ComputeLengthSize(bytes) + bytes);
        internal static int MaxSnapshotMessageBytes(int chunkBytes)
        {
            if (chunkBytes < 1) throw new ArgumentOutOfRangeException(nameof(chunkBytes));
            int body = new SnapshotMessage { Slot = ushort.MaxValue, TotalLength = uint.MaxValue, Offset = uint.MaxValue }.CalculateSize();
            return checked(MaximumEnvelopeSize + LengthDelimitedSize(10, checked(body + LengthDelimitedSize(4, chunkBytes))));
        }
        /// <summary>按最宽帧号/版本和每人一次成员事件预算，无须为最大输入分配大数组。</summary>
        internal static int MaxFinalMessageBytes(int participants, int maxInputBytes)
        {
            int input = new InputRecord { Slot = ushort.MaxValue, Generation = ulong.MaxValue, Origin = InputSource.MissingSubstitute }.CalculateSize();
            if (maxInputBytes > 0) input = checked(input + LengthDelimitedSize(4, maxInputBytes));
            int change = new MemberChange { Sequence = ulong.MaxValue, Kind = MemberChangeKind.Rebind, Slot = ushort.MaxValue, Generation = ulong.MaxValue }.CalculateSize();
            int body = new FinalFrameMessage { Tick = ulong.MaxValue, StateHash = ProtoBytes.CopyFrom(new byte[32]) }.CalculateSize();
            body = checked(body + participants * checked(LengthDelimitedSize(2, input) + LengthDelimitedSize(3, change)));
            return checked(MaximumEnvelopeSize + LengthDelimitedSize(14, body));
        }
    }
}
