using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace FrameSync.PredictionHarness
{
    /// <summary>把真实可靠 UDP、会话、预测、录制和逐帧状态核对连接成一个有界验收用例。</summary>
    internal sealed class PredictionScenario<TState>
    {
        internal const uint TickRate = 30;
        internal const int StepMilliseconds = 5;
        internal const int PredictionLead = 6;
        internal const int InputLead = 24;
        internal const int MaxNetworkMilliseconds = 60000;
        private readonly string _game, _output;
        private readonly int _count, _frames;
        private readonly bool _faults;
        private readonly SimulationModule<TState> _module;
        private readonly ByteString _neutral;
        private readonly Func<ulong, ushort, byte[]> _action;
        private readonly SimulatedNetwork _network;
        private readonly UdpNode _serverNode;
        private readonly UdpNode[] _clientNodes;
        private readonly ulong[] _serverConnections, _clientConnections, _nextInputTicks;
        private readonly bool[] _clientConnected, _initialChecked;
        private readonly ClientSession<TState>[] _clients;
        private readonly HashSet<(ulong Tick, int Index)>[] _confirmedEvents;
        private readonly Dictionary<ulong, Evidence> _evidence = new Dictionary<ulong, Evidence>();
        private readonly CaseReport _report;
        private readonly IDatagramPort _serverPort;
        private ServerSession<TState>? _server;
        private ReplayRecorder? _recorder;
        private Checkpoint? _initial;
        private string _initialHash = "";
        private int _connectingSlot;
        private long _serverMilliseconds;
        private bool _closingTransport;

        internal PredictionScenario(string game, int clients, int frames, bool faults, string output,
            SimulationModule<TState> module, byte[] neutral, Func<ulong, ushort, byte[]> action)
        {
            _game = game; _count = clients; _frames = frames; _faults = faults; _output = output;
            _module = module; _neutral = new ByteString(neutral); _action = action;
            _network = new SimulatedNetwork(faults); _serverPort = _network.Create(45000);
            _serverNode = new UdpNode(_serverPort); _clientNodes = new UdpNode[clients];
            _serverConnections = new ulong[clients]; _clientConnections = new ulong[clients];
            _nextInputTicks = new ulong[clients]; _clientConnected = new bool[clients]; _initialChecked = new bool[clients];
            _clients = new ClientSession<TState>[clients]; _confirmedEvents = new HashSet<(ulong, int)>[clients];
            string name = game + "-" + clients + "-" + (faults ? "chaos" : "clean");
            _report = new CaseReport { Name = name, Game = game, ClientCount = clients, Frames = frames };
            for (int i = 0; i < clients; i++)
            {
                _clientNodes[i] = new UdpNode(_network.Create(45001 + i));
                _nextInputTicks[i] = 1;
                _confirmedEvents[i] = new HashSet<(ulong, int)>();
                _report.Clients.Add(new ClientReport { Slot = i });
            }
        }

        internal CaseReport Run()
        {
            var clock = Stopwatch.StartNew(); long allocated = GC.GetTotalAllocatedBytes(true);
            _report.ManagedHeapAtStartBytes = GC.GetTotalMemory(false); SampleMemory();
            try
            {
                Connect(); CreateSessions(); Prepare();
                // 初态与 Start 已经可靠交付后，给首批未来输入留出 500ms 网络预热时间。
                // 服务器逻辑时间仍为零；网络时钟持续前进，UDP 的重传并未暂停。
                _report.Phase = "InputWarmup";
                long warmupEnd = _network.NowMilliseconds + 500;
                while (_network.NowMilliseconds < warmupEnd)
                {
                    PumpTransport(); SubmitInputs(0); PollClients(PredictionLead); PollServer(0); SampleMemory();
                }
                _report.Phase = "Running";
                long simulationStart = _network.NowMilliseconds;
                while (_server!.ConfirmedTick < (ulong)_frames)
                {
                    PumpTransport(); long elapsed = _network.NowMilliseconds - simulationStart;
                    ulong logicalTick = checked((ulong)(elapsed * TickRate / 1000));
                    SubmitInputs(logicalTick);
                    PollClients(Math.Min((ulong)_frames + PredictionLead, logicalTick + PredictionLead));
                    PollServer(elapsed); SampleMemory();
                }
                _server.Close(); _report.Phase = "SessionDrain";
                long drainDeadline = _network.NowMilliseconds + 10000;
                while (_clients.Any(c => c.Status != SessionStatus.Closed))
                {
                    Require(_network.NowMilliseconds < drainDeadline, "closing sessions did not drain within 10 simulated seconds");
                    PumpTransport(); PollServer(_serverMilliseconds); PollClients(null); SampleMemory();
                }
                ValidateFinal(); WriteRecording(); CaptureTransportStats();
                // 先核对游戏结果和会话关闭，再关闭传输，不能让提前断线伪装成完成。
                _report.Phase = "TransportDrain"; _closingTransport = true;
                foreach (ulong connection in _serverConnections) _serverNode.Close(connection);
                long closeDeadline = _network.NowMilliseconds + 3000;
                while (_serverNode.ConnectionCount != 0 || _clientNodes.Any(n => n.ConnectionCount != 0))
                {
                    Require(_network.NowMilliseconds < closeDeadline, "UDP close did not finish within 3 simulated seconds");
                    PumpTransport(); SampleMemory();
                }
                _report.AllTransportConnectionsClosed = true; _report.Phase = "Complete"; _report.Passed = true;
            }
            catch (Exception error)
            {
                _report.Failure = $"{error.GetType().Name}: {error.Message}; phase={_report.Phase}; " +
                    $"serverTick={_server?.ConfirmedTick ?? 0}; clients=" +
                    string.Join(",", _clients.Select((c, i) => $"{i}:{c?.ConfirmedTick ?? 0}/{c?.PredictedTick ?? 0}"));
                CaptureTransportStats();
            }
            finally
            {
                if (_server != null)
                {
                    _report.ServerConfirmedTick = _server.ConfirmedTick;
                    _report.ServerRejectedMessages = _server.RejectedMessages;
                    _report.ServerFinalHash = CheckpointCodec.Hash(_server.CaptureCheckpoint());
                }
                for (int i = 0; i < _count; i++) if (_clients[i] != null) UpdateClientReport(i);
                _report.Network = new NetworkReport {
                    Profile = _faults ? "chaos-v1" : "clean-v1", SimulatedMilliseconds = _network.NowMilliseconds,
                    SentDatagrams = _network.SentDatagrams, SentBytes = _network.SentBytes,
                    DeliveredDatagrams = _network.DeliveredDatagrams, DroppedDatagrams = _network.DroppedDatagrams,
                    DuplicateCopies = _network.DuplicateCopies, ReorderedDeliveries = _network.ReorderedDeliveries,
                    CapacityBackpressure = _network.CapacityBackpressure, PeakQueuedDatagrams = _network.PeakQueuedDatagrams,
                    LargestDatagramBytes = _network.LargestDatagramBytes
                };
                SampleMemory(); _report.ManagedAllocatedBytesDuringRun = GC.GetTotalAllocatedBytes(true) - allocated;
                clock.Stop(); _report.ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds;
                _serverNode.Shutdown(); foreach (var node in _clientNodes) node.Shutdown();
            }
            return _report;
        }

        private void Connect()
        {
            _report.Phase = "Handshake"; _serverNode.Listen();
            // 逐个握手使节点公开连接事件与固定玩家位置明确对应，不猜测随机线协议连接 ID。
            for (int i = 0; i < _count; i++)
            {
                _connectingSlot = i; _clientConnections[i] = _clientNodes[i].Connect(_serverPort.LocalAddress);
                long deadline = _network.NowMilliseconds + 10000;
                while (_serverConnections[i] == 0 || !_clientConnected[i])
                {
                    Require(_network.NowMilliseconds < deadline, "handshake timed out for slot " + i); PumpTransport();
                }
            }
        }
        private void CreateSessions()
        {
            var participants = Enumerable.Range(0, _count).Select(i => new Participant((ushort)i,
                Guid.Parse("b0000000-0000-0000-0000-" + (i + 1).ToString("D12")))).ToArray();
            int gameCode = _game == "counter" ? 1 : 2;
            var id = Guid.Parse("51000000-0000-0000-" + gameCode.ToString("D4") + "-" + _count.ToString("D12"));
            var descriptor = new SessionDescriptor(id, 1, TickRate, participants);
            // 每次只确认一帧，才能在外部逐帧获取对应的 LastFrame 和完整检查点做独立核对。
            var options = new SessionOptions(maxFramesPerPoll: 1);
            _server = new ServerSession<TState>(_module, descriptor, new UdpTransport(_serverNode),
                _serverConnections.Select((connection, i) => new PeerBinding(connection, (ushort)i)).ToArray(),
                _ => _neutral, options: options);
            _initial = _server.CaptureCheckpoint(); _initialHash = CheckpointCodec.Hash(_initial);
            _recorder = new ReplayRecorder(_initial, new ReplayLimits(maxFrames: _frames));
            _report.InitialCheckpointFile = Path.Combine(_output, _report.Name + "-initial.fscp");
            File.WriteAllBytes(_report.InitialCheckpointFile, CheckpointCodec.Encode(_initial));
            for (int i = 0; i < _count; i++)
                _clients[i] = new ClientSession<TState>(_module, id, 1, new UdpTransport(_clientNodes[i]),
                    _clientConnections[i], options: options, prediction: new PredictionOptions(), neutralInput: _ => _neutral);
        }
        private void Prepare()
        {
            _report.Phase = "Preparing"; long deadline = _network.NowMilliseconds + 10000;
            while (_server!.Status != SessionStatus.Running || _clients.Any(c => c.Status != SessionStatus.Running))
            {
                Require(_network.NowMilliseconds < deadline, "initial state preparation timed out");
                PumpTransport(); PollServer(0); PollClients(0); SampleMemory();
            }
        }
        private void PumpTransport()
        {
            Require(_network.NowMilliseconds < MaxNetworkMilliseconds, "case exceeded its 60-second logical network budget");
            _network.AdvanceTo(_network.NowMilliseconds + StepMilliseconds);
            var now = TimeSpan.FromMilliseconds(_network.NowMilliseconds);
            _serverNode.Poll(now); foreach (var node in _clientNodes) node.Poll(now);
            while (_serverNode.TryGetEvent(out var e))
            {
                if (e!.Kind == ConnectionEventKind.Connected)
                {
                    Require(_serverConnections[_connectingSlot] == 0, "duplicate server connection event");
                    _serverConnections[_connectingSlot] = e.ConnectionId;
                }
                else Require(_closingTransport, "server transport disconnected early: " + e.Reason);
            }
            for (int i = 0; i < _count; i++)
                while (_clientNodes[i].TryGetEvent(out var e))
                {
                    if (e!.Kind == ConnectionEventKind.Connected)
                    {
                        Require(e.ConnectionId == _clientConnections[i], "client connection identity changed");
                        _clientConnected[i] = true;
                    }
                    else Require(_closingTransport, "client " + i + " transport disconnected early: " + e.Reason);
                }
        }
        private void SubmitInputs(ulong logicalTick)
        {
            ulong horizon = Math.Min((ulong)_frames, logicalTick + InputLead);
            for (int i = 0; i < _count; i++)
                while (_nextInputTicks[i] <= horizon)
                {
                    ulong tick = _nextInputTicks[i];
                    Require(tick > _clients[i].ConfirmedTick, "script submission fell behind confirmation at tick " + tick);
                    // 自动脚本逐帧完成本地采样；未安排操作时也冻结并发送中性输入。
                    bool active = tick % 5 == 0 && (tick / 5 - 1) % (ulong)_count == (ulong)i;
                    ReadOnlySpan<byte> input = active ? _action(tick, (ushort)i) : _neutral.Span;
                    if (!_clients[i].TrySubmit(tick, input)) break;
                    _nextInputTicks[i]++;
                    _report.Clients[i].FrozenInputs++;
                    if (!active) _report.Clients[i].FrozenNeutralInputs++;
                }
        }
        private void PollServer(long milliseconds)
        {
            _serverMilliseconds = milliseconds;
            var results = _server!.Poll(TimeSpan.FromMilliseconds(milliseconds));
            Require(_server.Status != SessionStatus.Failed, "server failed: " + _server.Failure);
            Require(results.Count <= 1, "server exceeded the one-frame observation budget");
            if (results.Count == 0) return;
            ulong tick = results[0].Tick; var frame = _server.LastFrame!;
            Require(tick == (ulong)_evidence.Count + 1 && tick <= (ulong)_frames, "unexpected server tick " + tick);
            Require(frame.MemberEvents.Count == 0 && frame.Inputs.Count == _count, "unexpected roster change at tick " + tick);
            foreach (var input in frame.Inputs)
            {
                bool active = tick % 5 == 0 && input.Slot == (tick / 5 - 1) % (ulong)_count;
                ReadOnlySpan<byte> expected = active ? _action(tick, input.Slot) : _neutral.Span;
                Require(input.Payload.Span.SequenceEqual(expected) && input.Origin == InputOrigin.Submitted,
                    $"scripted input missed or changed at tick {tick}, slot {input.Slot}; server rejection: {_server.LastRejection}");
                if (active) _report.AcceptedScriptedActions++; else _report.AcceptedNeutralInputs++;
            }
            _recorder!.Append(frame);
            _evidence.Add(tick, new Evidence(frame, CheckpointCodec.Hash(_server.CaptureCheckpoint()), results[0]));
        }
        private void PollClients(ulong? target)
        {
            for (int i = 0; i < _count; i++)
            {
                var client = _clients[i]; var report = _report.Clients[i];
                var results = target.HasValue ? client.Poll(target.Value) : client.Poll();
                Require(client.Status != SessionStatus.Failed, $"client {i} failed before tick {client.ConfirmedTick + 1}: {client.Failure}");
                Require(results.Count <= 1, "client exceeded the one-frame observation budget");
                if (client.LocalParticipant != null && !_initialChecked[i])
                {
                    Require(client.ConfirmedTick == 0 && CheckpointCodec.Hash(client.CaptureCheckpoint()) == _initialHash,
                        "client initial checkpoint differs at slot " + i);
                    _initialChecked[i] = true;
                }
                if (results.Count == 1)
                {
                    ulong tick = results[0].Tick;
                    Require(tick == report.ConfirmedFramesChecked + 1 && client.ConfirmedTick == tick,
                        $"client {i} skipped or duplicated confirmation at tick {tick}");
                    Require(_evidence.TryGetValue(tick, out var expected), $"client {i} confirmed unpublished server tick {tick}");
                    Require(CheckpointCodec.Hash(client.CaptureCheckpoint()) == expected!.Hash,
                        $"client {i} checkpoint mismatch at tick {tick}");
                    Require(SameFrame(client.LastFrame!, expected.Frame), $"client {i} final input mismatch at tick {tick}");
                    Require(SameEvents(results[0], expected.Result), $"client {i} confirmed event mismatch at tick {tick}");
                    report.ConfirmedFramesChecked++;
                }
                else Require(client.ConfirmedTick == report.ConfirmedFramesChecked, "confirmation advanced without a frame result");
                Require(client.PredictedTick < _nextInputTicks[i], $"client {i} predicted beyond its frozen local inputs");
                Require(client.PredictedTick >= client.ConfirmedTick && client.PredictedTick - client.ConfirmedTick <= 12,
                    $"client {i} exceeded the prediction window at tick {client.ConfirmedTick}");
                foreach (var e in client.LastPredictionEvents)
                {
                    if (e.Kind == PredictionEventKind.Predicted) report.PredictedEvents++;
                    else if (e.Kind == PredictionEventKind.Invalidated) report.InvalidatedEvents++;
                    else
                    {
                        Require(_confirmedEvents[i].Add((e.Event.Tick, e.Event.Index)),
                            $"client {i} duplicated confirmed event {e.Event.Tick}/{e.Event.Index}");
                        report.ConfirmedEvents++;
                    }
                }
                if (client.IsReplaying) report.ReplayingPolls++;
                UpdateClientReport(i);
            }
        }
        private void ValidateFinal()
        {
            string expected = CheckpointCodec.Hash(_server!.CaptureCheckpoint());
            _report.FinalState = _server.InspectState();
            foreach (var pair in _clients.Select((client, i) => (client, i)))
            {
                var client = pair.client; var report = _report.Clients[pair.i];
                Require(client.ConfirmedTick == (ulong)_frames && client.PredictedTick == client.ConfirmedTick &&
                    client.PresentedTick == client.ConfirmedTick && !client.IsReplaying && client.Status == SessionStatus.Closed,
                    "client did not discard its unconfirmed tail at slot " + pair.i);
                Require(report.ConfirmedFramesChecked == (ulong)_frames && report.FinalHash == expected,
                    "final checkpoint differs at slot " + pair.i);
                Require(report.ConfirmedEvents == _evidence.Values.Sum(x => x.Result.Events.Count),
                    "confirmed event notification count differs at slot " + pair.i);
                Require(report.MaximumPredictionLead > 0, "client never predicted before confirmation at slot " + pair.i);
            }
            Require(_report.AcceptedScriptedActions == _frames / 5, "not all scheduled actions were accepted");
            Require(_report.AcceptedNeutralInputs + _report.AcceptedScriptedActions == (long)_frames * _count,
                "not every participant explicitly submitted an input for every frame");
            Require(_report.Clients.All(c => c.FrozenInputs == (ulong)_frames), "local sampling omitted some frames");
            Require(_network.CapacityBackpressure == 0, "fault network reached its capacity during the scenario");
            if (_faults)
            {
                Require(_network.DroppedDatagrams > 0 && _network.DuplicateCopies > 0 && _network.ReorderedDeliveries > 0,
                    "scenario did not exercise all network disturbances");
                if (_count > 1) Require(_clients.Any(c => c.RollbackCount > 0), "remote inputs did not cause any rollback");
            }
        }
        private void WriteRecording()
        {
            _report.ReplayFile = Path.Combine(_output, _report.Name + ".fsrp");
            _report.HashFile = Path.Combine(_output, _report.Name + ".sha256");
            File.WriteAllBytes(_report.ReplayFile, ReplayCodec.Encode(_recorder!.Build()));
            File.WriteAllText(_report.HashFile, CheckpointCodec.Hash(_server!.CaptureCheckpoint()) + "\n");
        }
        private void UpdateClientReport(int i)
        {
            var client = _clients[i]; var report = _report.Clients[i];
            bool checkpointChanged = report.FinalHash.Length == 0 || report.ConfirmedTick != client.ConfirmedTick;
            report.Status = client.Status.ToString(); report.ConfirmedTick = client.ConfirmedTick;
            report.PredictedTick = client.PredictedTick; report.PresentedTick = client.PresentedTick;
            report.MaximumPredictedTick = Math.Max(report.MaximumPredictedTick, client.PredictedTick);
            if (client.PredictedTick >= client.ConfirmedTick)
                report.MaximumPredictionLead = Math.Max(report.MaximumPredictionLead, client.PredictedTick - client.ConfirmedTick);
            report.RollbackCount = client.RollbackCount; report.LastRestoreTick = client.LastRestoreTick;
            if (checkpointChanged && client.LocalParticipant != null) report.FinalHash = CheckpointCodec.Hash(client.CaptureCheckpoint());
        }
        private void CaptureTransportStats()
        {
            for (int i = 0; i < _count; i++)
            {
                try { _report.Clients[i].UdpRetransmissions = _clientNodes[i].GetStatistics(_clientConnections[i]).Retransmissions; }
                catch (ArgumentException) { }
            }
            long total = 0;
            foreach (ulong connection in _serverConnections)
            {
                try { total += _serverNode.GetStatistics(connection).Retransmissions; }
                catch (ArgumentException) { }
            }
            _report.ServerUdpRetransmissions = total;
        }
        private void SampleMemory() => _report.MaximumSampledManagedHeapBytes =
            Math.Max(_report.MaximumSampledManagedHeapBytes, GC.GetTotalMemory(false));
        private void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(_report.Name + ": " + message); }
        private static bool SameFrame(InputFrame a, InputFrame b)
        {
            if (a.Tick != b.Tick || a.SessionId != b.SessionId || a.Epoch != b.Epoch ||
                a.Inputs.Count != b.Inputs.Count || a.MemberEvents.Count != b.MemberEvents.Count) return false;
            for (int i = 0; i < a.Inputs.Count; i++)
            {
                var x = a.Inputs[i]; var y = b.Inputs[i];
                if (x.Slot != y.Slot || x.Generation != y.Generation || x.Origin != y.Origin || !x.Payload.Span.SequenceEqual(y.Payload.Span)) return false;
            }
            for (int i = 0; i < a.MemberEvents.Count; i++)
            {
                var x = a.MemberEvents[i]; var y = b.MemberEvents[i];
                if (x.Sequence != y.Sequence || x.Kind != y.Kind || x.Slot != y.Slot || x.Generation != y.Generation) return false;
            }
            return true;
        }
        private static bool SameEvents(FrameResult a, FrameResult b)
        {
            if (a.Tick != b.Tick || a.Events.Count != b.Events.Count) return false;
            for (int i = 0; i < a.Events.Count; i++)
            {
                var x = a.Events[i]; var y = b.Events[i];
                if (x.SessionId != y.SessionId || x.Epoch != y.Epoch || x.Tick != y.Tick || x.Index != y.Index ||
                    !x.Payload.Span.SequenceEqual(y.Payload.Span)) return false;
            }
            return true;
        }
        private sealed class Evidence
        {
            internal Evidence(InputFrame frame, string hash, FrameResult result) { Frame = frame; Hash = hash; Result = result; }
            internal readonly InputFrame Frame;
            internal readonly string Hash;
            internal readonly FrameResult Result;
        }
    }
}
