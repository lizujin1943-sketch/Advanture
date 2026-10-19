using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using FrameSync.Abstractions;
using FrameSync.Protocol.Protobuf;
using Google.Protobuf;
using ProtoBytes = Google.Protobuf.ByteString;
using FrameSync.Runtime;
using FrameSync.Sessions;

namespace FrameSync.Tests
{
    // 这些测试走真实会话收发路径，防止协议扩展或合法的等价编码被当成帧冲突。
    internal static class ProtobufSessionTests
    {
        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("protobuf.session.unknown_input_metadata_keeps_game_payload", () => {
                var f = new SyncFixture(); f.Start();
                byte[] input = Wire.Input(Fixture.SessionId, 1, 1, Fixture.Input(0, 5), 900);
                SessionBoundaryTests.Send(f.ATransport, 1, MessageKind.Input, Extend(input));
                f.Server.Poll(TimeSpan.Zero); f.Finish(1);
                f.AssertEqual(1, 5);
            });
            yield return ("protobuf.session.equivalent_confirmed_final_is_not_a_conflict", () => {
                var f = new SyncFixture(); f.Start(); f.Finish(1);
                byte[] final = Wire.Final(f.Server.LastFrame!, CheckpointCodec.Hash(f.Server.CaptureCheckpoint()), 900);
                SessionBoundaryTests.Send(f.ServerTransport, 3, MessageKind.Final, Extend(final));
                Assert.Equal(0, f.B.Poll().Count);
                Assert.Equal(SessionStatus.Running, f.B.Status);
                f.AssertEqual(1, 0);
            });
            yield return ("protobuf.session.equivalent_buffered_final_waits_for_gap", () => {
                var f = new SyncFixture(); f.Start(); f.Server.Poll(TimeSpan.FromMilliseconds(100));
                Assert.True(f.BTransport.TryReceive(out var first));
                f.Server.Poll(TimeSpan.FromMilliseconds(200));
                byte[] final = Wire.Final(f.Server.LastFrame!, CheckpointCodec.Hash(f.Server.CaptureCheckpoint()), 900);
                SessionBoundaryTests.Send(f.ServerTransport, 3, MessageKind.Final, Extend(final));
                f.B.Poll(); Assert.Equal(SessionStatus.Running, f.B.Status); Assert.Equal(0UL, f.B.ConfirmedTick);
                SessionBoundaryTests.Send(f.ServerTransport, 3, MessageKind.Final, first!.Payload.ToArray());
                f.B.Poll(); Assert.Equal(2UL, f.B.ConfirmedTick);
                Assert.Bytes(CheckpointCodec.Encode(f.Server.CaptureCheckpoint()), CheckpointCodec.Encode(f.B.CaptureCheckpoint()));
            });
            yield return ("protobuf.session.field_order_does_not_change_final", () => {
                var f = new SyncFixture(); f.Start(); f.Finish(1);
                var p = SessionPacket.Parser.ParseFrom(Wire.Final(f.Server.LastFrame!, CheckpointCodec.Hash(f.Server.CaptureCheckpoint()), 900));
                // 反向写外壳字段，模拟另一种合法的序列化顺序，而非运行库默认顺序。
                using var stream = new MemoryStream();
                using (var w = new CodedOutputStream(stream, leaveOpen: true))
                {
                    w.WriteTag(14, WireFormat.WireType.LengthDelimited); w.WriteMessage(p.Final);
                    w.WriteTag(3, WireFormat.WireType.Varint); w.WriteUInt64(p.Epoch);
                    w.WriteTag(2, WireFormat.WireType.LengthDelimited); w.WriteBytes(p.SessionId);
                    w.WriteTag(1, WireFormat.WireType.Varint); w.WriteUInt32(p.ProtocolVersion);
                }
                SessionBoundaryTests.Send(f.ServerTransport, 3, MessageKind.Final, stream.ToArray());
                Assert.Equal(0, f.B.Poll().Count); Assert.Equal(SessionStatus.Running, f.B.Status); f.AssertEqual(1, 0);
            });
            yield return ("protobuf.session.game_payload_is_opaque", () => {
                byte[] payload = { 0, 255, 128, 0, 10, 13, 127, 254, 1 };
                var bytes = Wire.Input(Fixture.SessionId, ulong.MaxValue, ulong.MaxValue, new RecordedInput(65535, ulong.MaxValue, payload), 900);
                var packet = Wire.Decode(bytes, 900, out var kind, out var id, out var epoch);
                var input = Wire.ReadInput(packet.Input, 9, out var tick);
                Assert.Equal(MessageKind.Input, kind); Assert.Equal(Fixture.SessionId, id); Assert.Equal(ulong.MaxValue, epoch);
                Assert.Equal(ulong.MaxValue, tick); Assert.Equal((ushort)65535, input.Slot); Assert.Equal(ulong.MaxValue, input.Generation);
                Assert.Bytes(payload, input.Payload.ToArray());
            });
            yield return ("protobuf.session.checkpoint_chunk_bytes_are_unchanged", () => {
                var f = new SyncFixture(); byte[] checkpoint = CheckpointCodec.Encode(f.Server.CaptureCheckpoint());
                int offset = 3, count = checkpoint.Length - offset;
                var packet = Wire.Decode(Wire.Snapshot(Fixture.SessionId, 1, 0, checkpoint, offset, count),
                    Wire.MaxSnapshotMessageBytes(count), out _, out _, out _);
                Assert.Equal((uint)checkpoint.Length, packet.Snapshot.TotalLength); Assert.Equal((uint)offset, packet.Snapshot.Offset);
                Assert.Bytes(checkpoint.AsSpan(offset).ToArray(), packet.Snapshot.Data.ToByteArray());
            });
            yield return ("protobuf.session.variable_length_budget_covers_boundaries", () => {
                foreach (int size in new[] { 1, 127, 128, 16383, 16384 })
                {
                    var inputs = Enumerable.Range(0, 4).Select(i => new RecordedInput((ushort)(65532 + i), ulong.MaxValue, new byte[size], InputOrigin.MissingSubstitute)).ToArray();
                    var events = Enumerable.Range(0, 4).Select(i => new MemberEvent((ulong)i + 1, MemberEventKind.Rebind, (ushort)(65532 + i), ulong.MaxValue)).ToArray();
                    var frame = new InputFrame(Fixture.SessionId, ulong.MaxValue, ulong.MaxValue, inputs, events);
                    int maximum = Wire.MaxFinalMessageBytes(4, size);
                    var bytes = Wire.Final(frame, new string('f', 64), maximum);
                    Assert.True(bytes.Length <= maximum);
                    Assert.Throws<InvalidDataException>(() => Wire.Final(frame, new string('f', 64), bytes.Length - 1));
                    var snap = Wire.Snapshot(Fixture.SessionId, ulong.MaxValue, ushort.MaxValue, new byte[size], 0, size);
                    Assert.True(snap.Length <= Wire.MaxSnapshotMessageBytes(size));
                }
            });
            yield return ("protobuf.session.malformed_or_legacy_envelope_rejected", () => {
                var original = SessionPacket.Parser.ParseFrom(Wire.Terminal(MessageKind.Progress, Fixture.SessionId, 1, 0));
                foreach (var mutate in new Action<SessionPacket>[] {
                    p => p.ProtocolVersion = 2, p => p.SessionId = ProtoBytes.Empty, p => p.Epoch = 0,
                    p => p.SessionId = ProtoBytes.CopyFrom(new byte[16]), p => p.ClearBody() })
                {
                    var p = original.Clone(); mutate(p);
                    Assert.Throws<InvalidDataException>(() => Wire.Decode(p.ToByteArray(), 900, out _, out _, out _));
                }
                Assert.Throws<InvalidDataException>(() => Wire.Decode(new byte[] { 0x46, 0x53, 0x42, 0x31, 2, 0 }, 900, out _, out _, out _));
                var bad = original.ToByteArray().Concat(new byte[] { 0x80 }).ToArray();
                Assert.Throws<InvalidDataException>(() => Wire.Decode(bad, 900, out _, out _, out _));
                Assert.Throws<InvalidDataException>(() => Wire.Decode(new byte[901], 900, out _, out _, out _));
            });
            foreach (var bad in new (string Name, Action<FinalFrameMessage> Mutate)[] {
                ("origin_overflow", p => p.Inputs[0].Origin = (InputSource)257),
                ("generation_zero", p => p.Inputs[0].Generation = 0),
                ("slot_overflow", p => p.Inputs[0].Slot = 65536),
                ("unordered_slots", p => { var a=p.Inputs[0]; p.Inputs[0]=p.Inputs[1]; p.Inputs[1]=a; }),
                ("duplicate_slot", p => p.Inputs[1].Slot = p.Inputs[0].Slot),
                ("input_too_large", p => p.Inputs[0].Payload = ProtoBytes.CopyFrom(new byte[129])),
                ("too_many_inputs", p => { while(p.Inputs.Count < 5) p.Inputs.Add(p.Inputs[0].Clone()); }),
                ("hash_truncated", p => p.StateHash = ProtoBytes.CopyFrom(new byte[31])),
                ("member_kind_overflow", p => p.MemberEvents.Add(new MemberChange { Sequence=1, Kind=(MemberChangeKind)257, Generation=1 })),
                ("too_many_events", p => { for(int i=0;i<5;i++) p.MemberEvents.Add(new MemberChange { Sequence=(ulong)i+1, Kind=MemberChangeKind.Deactivate, Slot=(uint)i, Generation=1 }); })
            })
                yield return ("protobuf.session.rejects_" + bad.Name, () => {
                    var f = new SyncFixture(); f.Start(); f.Server.Poll(TimeSpan.FromMilliseconds(100));
                    Assert.True(f.BTransport.TryReceive(out var packet));
                    var p = SessionPacket.Parser.ParseFrom(packet!.Payload); bad.Mutate(p.Final);
                    SessionBoundaryTests.Send(f.ServerTransport, 3, MessageKind.Final, p.ToByteArray());
                    f.B.Poll(); Assert.Equal(SessionStatus.Failed, f.B.Status); Assert.Equal(0UL, f.B.ConfirmedTick);
                });
            foreach (var change in new (string Name, Action<FinalFrameMessage> Mutate)[] {
                ("origin", p => p.Inputs[0].Origin = InputSource.Submitted),
                ("generation", p => p.Inputs[0].Generation++),
                ("payload", p => p.Inputs[0].Payload = ProtoBytes.CopyFrom(new byte[] { 1 })),
                ("member_event", p => p.MemberEvents.Add(new MemberChange { Sequence=1, Kind=MemberChangeKind.Deactivate, Generation=1 }))
            })
                yield return ("protobuf.session.conflicting_duplicate_" + change.Name, () => {
                    var f = new SyncFixture(); f.Start(); f.Finish(1);
                    var p = SessionPacket.Parser.ParseFrom(Wire.Final(f.Server.LastFrame!, CheckpointCodec.Hash(f.Server.CaptureCheckpoint()), 900));
                    change.Mutate(p.Final); SessionBoundaryTests.Send(f.ServerTransport, 3, MessageKind.Final, p.ToByteArray());
                    f.B.Poll(); Assert.Equal(SessionStatus.Failed, f.B.Status); Assert.Equal(1UL, f.B.ConfirmedTick);
                });
        }
        // Protobuf 字段 127，varint 值 1。新旧消息业务含义完全相同。
        private static byte[] Extend(byte[] bytes) => bytes.Concat(new byte[] { 0xf8, 0x07, 0x01 }).ToArray();
    }
}
