using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;
using FrameSync.Transport;
using FrameSync.Samples.Counter;
using FrameSync.Tools.UdpHarness;

namespace FrameSync.Tools.UdpHarnessTests
{
    internal static class InputBufferTests
    {
        internal static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("InputBuffer.IdleFramesAreFrozenOnce", () => {
                var sampler = new CounterInputBuffer(true, 2, 0);
                var accepted = new List<(ulong, int)>();
                bool Submit(ulong tick, ByteString input) { accepted.Add((tick, Amount(input))); return true; }
                sampler.SubmitThrough(0, 3, Submit); sampler.SubmitThrough(0, 3, Submit);
                Require(sampler.TryEnqueue(5), "queue failed"); sampler.SubmitThrough(0, 3, Submit);
                // 同一个时钟目标重复调用，不能把新操作塞进已冻结的中性帧，也不能产生新帧。
                Require(accepted.SequenceEqual(new[] {(1UL,0),(2UL,0),(3UL,0)}), "Idle frames were missing, repeated or overwritten");
                sampler.SubmitThrough(0, 4, Submit);
                Require(accepted.Last() == (4UL,5) && sampler.FrozenInputCount == 4 && sampler.FrozenNeutralInputCount == 3,
                    "New command did not wait for the next sampling tick");
            });
            yield return ("InputBuffer.StartupGapUsesNeutralBeforeCurrentSample", () => {
                var sampler = new CounterInputBuffer(true, 2, 0); sampler.TryEnqueue(5);
                var accepted = new List<(ulong, int)>();
                sampler.SubmitThrough(0, 3, (tick, input) => { accepted.Add((tick, Amount(input))); return true; });
                Require(accepted.SequenceEqual(new[] {(1UL,0),(2UL,0),(3UL,5)}), "Startup input was assigned to an old gap frame");
            });
            yield return ("InputBuffer.BackpressureKeepsOriginalTickAndBytes", () => {
                var sampler = new CounterInputBuffer(true, 2, 0); sampler.TryEnqueue(5);
                var attempts = new List<(ulong, int)>();
                sampler.SubmitThrough(0, 1, (tick, input) => { attempts.Add((tick, Amount(input))); return false; });
                Require(sampler.FrozenInputCount == 0, "Rejected input counted as successfully frozen by the session");
                sampler.TryEnqueue(7);
                sampler.SubmitThrough(0, 2, (tick, input) => { attempts.Add((tick, Amount(input))); return true; });
                Require(attempts.SequenceEqual(new[] {(1UL,5),(1UL,5),(2UL,7)}), "Backpressure changed the sampled frame or consumed new input on retry");
            });
            yield return ("InputBuffer.ExpiredSampleIsDiscardedWithoutReassignment", () => {
                var sampler = new CounterInputBuffer(true, 2, 0); sampler.TryEnqueue(5);
                sampler.SubmitThrough(0, 1, (_, _) => false); sampler.TryEnqueue(7);
                var accepted = new List<(ulong, int)>();
                sampler.SubmitThrough(1, 2, (tick, input) => { accepted.Add((tick, Amount(input))); return true; });
                Require(accepted.SequenceEqual(new[] {(2UL,7)}) && sampler.ExpiredInputCount == 1,
                    "Expired input was silently moved to a new frame");
            });
            yield return ("InputBuffer.AutomaticScriptFillsEveryOtherFrameWithNeutral", () => {
                var sampler = new CounterInputBuffer(false, 2, 1);
                var accepted = new List<(ulong, int)>();
                sampler.SubmitThrough(0, 10, (tick, input) => { accepted.Add((tick, Amount(input))); return true; });
                Require(accepted.Count == 10 && accepted[4] == (5UL,0) && accepted[9] == (10UL,3),
                    "Automatic client did not submit every frame or acted for another player");
                Require(sampler.FrozenNeutralInputCount == 9, "Idle frames were not counted");
            });
            yield return ("InputBuffer.CloseBeforeFinalStopsSamplingWhileClockAdvances", () => {
                var network = new InMemoryNetwork(); var serverTransport = network.CreateEndpoint(1);
                var clientTransport = network.CreateEndpoint(2);
                var id = Guid.Parse("42000000-0000-0000-0000-000000000001");
                var session = new SessionDescriptor(id, 1, 10, new[] {new Participant(0, Guid.Parse("42000000-0000-0000-0000-000000000002"))});
                var server = new ServerSession<CounterState>(CounterGame.CreateModule(), session, serverTransport,
                    new[] {new PeerBinding(2,0)}, _ => new ByteString(CounterGame.NeutralInput()));
                var client = new ClientSession<CounterState>(CounterGame.CreateModule(), id, 1, clientTransport, 1,
                    prediction: new PredictionOptions(), neutralInput: _ => new ByteString(CounterGame.NeutralInput()));
                for(int i=0;i<20;i++) { server.Poll(TimeSpan.Zero); client.Poll(); }
                Require(client.Status == SessionStatus.Running, "Client did not start");
                var scheduler = new FixedInputScheduler(10); scheduler.Start(TimeSpan.Zero);
                var sampler = new CounterInputBuffer(true, 1, 0);
                sampler.SubmitThrough(client, scheduler.InputTick(TimeSpan.Zero,0)); client.Poll(3);
                server.Poll(TimeSpan.Zero); server.Poll(TimeSpan.FromMilliseconds(100));
                // 通道 0 的 Final 人为延迟，通道 1 的 Close 先交付；不伪造会话数据或检查点。
                Require(clientTransport.TryReceive(out var final) && final!.Channel == 0, "Expected a real final frame");
                server.Close(); server.Poll(TimeSpan.FromMilliseconds(100)); client.Poll();
                Require(client.Status == SessionStatus.Running && client.ConfirmedTick == 0, "Client must drain the delayed final before closing");
                sampler.TryEnqueue(9); // 关闭后又读到的操作也不能再采样或提交。
                for(int ms=200;ms<=400;ms+=100)
                {
                    ulong target = scheduler.InputTick(TimeSpan.FromMilliseconds(ms),client.ConfirmedTick);
                    sampler.SubmitThrough(client,target); client.Poll(target);
                }
                Require(sampler.FrozenInputCount == 3, "Closing client continued freezing new input");
                Require(serverTransport.TrySend(2,final!.Channel,final.Delivery,final.Payload), "Failed to release the final frame");
                for(int i=0;i<5;i++) client.Poll();
                Require(client.Status == SessionStatus.Closed && client.ConfirmedTick == 1 && client.PredictedTick == 1,
                    "Client failed to drain final confirmation and close");
                Require(CheckpointCodec.Hash(client.CaptureCheckpoint()) == CheckpointCodec.Hash(server.CaptureCheckpoint()),
                    "Closing client did not verify the complete final state");
            });
            yield return ("InputBuffer.CommandsStayBounded", () => {
                var sampler = new CounterInputBuffer(true, 2, 0);
                for(int i=0;i<128;i++) Require(sampler.TryEnqueue(1), "Queue filled too early");
                Require(!sampler.TryEnqueue(2), "Command queue grew beyond its limit");
                sampler.SubmitThrough(0, 1, (_, _) => true);
                Require(sampler.TryEnqueue(2), "Accepted sampling did not release command space");
            });
        }
        private static int Amount(ByteString input) => BinaryPrimitives.ReadInt32LittleEndian(input.Span.Slice(1));
        private static void Require(bool condition, string message) { if(!condition) throw new Exception(message); }
    }
}
