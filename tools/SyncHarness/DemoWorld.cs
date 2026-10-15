using System;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;
using FrameSync.Transport;

namespace FrameSync.Tools.SyncHarness
{
    /// <summary>演示宿主：在同一进程驱动多个独立会话。这里负责测试时钟，不属于同步框架。</summary>
    internal sealed class DemoWorld<TState>
    {
        private readonly ServerSession<TState> _server;
        private readonly ClientSession<TState>[] _clients;
        private TimeSpan _now;
        public DemoWorld(SimulationModule<TState> module, int count, ByteString neutral)
        {
            var network = new InMemoryNetwork(); var transport = network.CreateEndpoint(1);
            var members = new Participant[count]; var bindings = new PeerBinding[count]; _clients = new ClientSession<TState>[count];
            Guid sessionId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
            for (int i = 0; i < count; i++)
            {
                members[i] = new Participant((ushort)i, Guid.Parse("aaaaaaaa-0000-0000-0000-" + (i + 1).ToString("D12")));
                bindings[i] = new PeerBinding((ulong)i + 2, (ushort)i);
                _clients[i] = new ClientSession<TState>(module, sessionId, 1, network.CreateEndpoint((ulong)i + 2), 1);
            }
            Members = members;
            _server = new ServerSession<TState>(module, new SessionDescriptor(sessionId, 1, 10, members), transport, bindings, _ => neutral);
            for (int i = 0; i < 100 && !AllRunning(); i++) { _server.Poll(_now); foreach (var client in _clients) client.Poll(); }
            Require(AllRunning(), "Initial handshake did not finish."); Verify();
        }
        public Participant[] Members { get; }
        public ulong Tick => _server.ConfirmedTick;
        public TState ServerState => _server.InspectState();
        public TState ClientState(int index) => _clients[index].InspectState();
        public InputFrame LastClientFrame(int index) => _clients[index].LastFrame ?? throw new InvalidOperationException("No confirmed frame.");
        public string Hash => CheckpointCodec.Hash(_server.CaptureCheckpoint());

        /// <summary>先在截止前送出输入，再把虚拟时间推进到本帧截止点；绝不直接调用任何客户端 Step。</summary>
        public void Step(int actor, byte[] input)
        {
            ulong tick = checked(Tick + 1);
            Require(_clients[actor].TrySubmit(tick, input), "Client outbox is full.");
            foreach (var client in _clients) client.Poll();
            _server.Poll(_now); // 输入到达时间严格早于本帧截止。
            _now = TimeSpan.FromTicks(checked((long)tick * TimeSpan.TicksPerSecond / 10));
            IReadOnlyList<FrameResult> serverResults = _server.Poll(_now);
            Require(serverResults.Count == 1, "Server must confirm exactly one frame: " + _server.Failure);
            foreach (var client in _clients)
            {
                IReadOnlyList<FrameResult> results = client.Poll();
                Require(results.Count == 1, "Client must execute exactly one frame: " + client.Failure);
                CompareEvents(serverResults[0], results[0]);
            }
            _server.Poll(_now); // 收取客户端已执行进度，允许后续帧进入窗口。
            Verify();
        }
        public void Close()
        {
            _server.Close();
            for (int i = 0; i < 20; i++) { _server.Poll(_now); foreach (var client in _clients) client.Poll(); }
            foreach (var client in _clients) Require(client.Status == SessionStatus.Closed, "Client did not close.");
            Verify();
        }
        private bool AllRunning()
        {
            if (_server.Status != SessionStatus.Running) return false;
            foreach (var client in _clients) if (client.Status != SessionStatus.Running) return false;
            return true;
        }
        private void Verify()
        {
            Require(_server.Status != SessionStatus.Failed, _server.Failure ?? "Server failed.");
            byte[] expected = CheckpointCodec.Encode(_server.CaptureCheckpoint());
            foreach (var client in _clients)
            {
                Require(client.Status != SessionStatus.Failed, client.Failure ?? "Client failed.");
                Require(client.ConfirmedTick == Tick, "Confirmed ticks differ.");
                Require(expected.AsSpan().SequenceEqual(CheckpointCodec.Encode(client.CaptureCheckpoint())), "Complete checkpoint bytes differ.");
            }
        }
        private static void CompareEvents(FrameResult expected, FrameResult actual)
        {
            Require(expected.Tick == actual.Tick && expected.Events.Count == actual.Events.Count, "Frame event counts differ.");
            for (int i = 0; i < expected.Events.Count; i++)
            {
                var a = expected.Events[i]; var b = actual.Events[i];
                Require(a.SessionId == b.SessionId && a.Epoch == b.Epoch && a.Tick == b.Tick && a.Index == b.Index &&
                    a.Payload.Span.SequenceEqual(b.Payload.Span), "Frame event identity or bytes differ.");
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
