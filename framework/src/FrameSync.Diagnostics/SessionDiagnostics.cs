using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using FrameSync.Runtime;
using FrameSync.Sessions;

namespace FrameSync.Diagnostics
{
    /// <summary>可选的会话文件诊断器。只观察已完成的 Poll，不改变输入、模拟、回滚或连接状态。</summary>
    /// <remarks>
    /// 独立于游戏和引擎。宿主在同一线程于 Poll 后调用 Observe，在清理会话前调用 End。
    /// 每秒写一次进度，状态变化和结束立即写；正常预测回滚不是不同步。
    /// 每个实例创建独立目录；I/O 失败通过 Error 暴露，不传播到游戏循环。
    /// </remarks>
    public sealed class SessionDiagnostics : IDisposable
    {
        private readonly DiagnosticEntry _entry;
        private StreamWriter? _stream;
        private Checkpoint? _checkpoint;
        private TimeSpan _lastWrite;
        private bool _observed, _finished;

        /// <summary>建立一次运行的记录。root 由宿主指定，role 仅作为文字记录，不参与路径拼接。</summary>
        public SessionDiagnostics(string root, string role, string environment = "")
        {
            string id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
            _entry = new DiagnosticEntry { runId = id, role = role, environment = environment };
            DirectoryPath = root;
            Safe(() =>
            {
                DirectoryPath = Path.GetFullPath(Path.Combine(root, id));
                Directory.CreateDirectory(DirectoryPath);
                _stream = new StreamWriter(new FileStream(Path.Combine(DirectoryPath, "progress.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                Persist("started");
            });
        }

        /// <summary>本实例的独立目录；写入失败时配合 Error 查看，不能仅凭目录存在判断成功。</summary>
        public string DirectoryPath { get; private set; }
        /// <summary>首次诊断写入错误；宿主应提示一次。错误后停写，避免每帧重复异常。</summary>
        public string? Error { get; private set; }

        /// <summary>观察客户端最新状态。没有初态也能记录等待连接或握手失败。</summary>
        public void ObserveClient<TState>(ClientSession<TState>? session, TimeSpan elapsed)
        {
            if (_finished) return;
            Safe(() => Observe(session?.Status, elapsed,
                session?.LocalParticipant == null ? null : session.CaptureCheckpoint(),
                session?.PredictedTick ?? 0, session?.RollbackCount ?? 0, session?.LastRestoreTick ?? 0,
                session?.RejectedMessages ?? 0, session?.LocalParticipant?.Slot ?? -1,
                session?.Failure, session?.Desync));
        }

        /// <summary>观察服务器确认状态。服务器没有客户端预测，其回滚字段始终为零。</summary>
        public void ObserveServer<TState>(ServerSession<TState>? session, TimeSpan elapsed)
        {
            if (_finished) return;
            Safe(() => Observe(session?.Status, elapsed, session?.CaptureCheckpoint(),
                session?.ConfirmedTick ?? 0, 0, 0, session?.RejectedMessages ?? 0, -1, session?.Failure, null));
        }

        private void Observe(SessionStatus? status, TimeSpan elapsed, Checkpoint? checkpoint,
            ulong predicted, ulong rollbacks, ulong restored, ulong rejected, int slot, string? failure, DesyncReport? desync)
        {
            string state = status?.ToString() ?? "Connecting";
            bool changed = _entry.sessionStatus != state;
            _entry.sessionStatus = state; _entry.elapsedMilliseconds = (long)elapsed.TotalMilliseconds;
            _entry.confirmedTick = checkpoint?.Tick ?? 0; _entry.predictedTick = predicted;
            _entry.rollbackCount = rollbacks; _entry.lastRestoreTick = restored;
            _entry.rejectedMessages = rejected; _entry.localSlot = slot; _checkpoint = checkpoint;
            if (checkpoint != null)
            {
                _entry.sessionId = checkpoint.Session.SessionId.ToString(); _entry.epoch = checkpoint.Session.Epoch;
                _entry.tickRate = checkpoint.Session.TickRate; _entry.applicationId = checkpoint.Manifest.ApplicationId;
                _entry.rulesVersion = checkpoint.Manifest.RulesVersion;
            }
            if (desync != null)
            {
                // 先完整导出证据，再发布已结束摘要。候选状态从未经过确认，不能当作有效存档。
                _entry.desyncDetected = true; _entry.problemTick = desync.Frame.Tick;
                SaveDesync(desync); Finish("Desync", failure); return;
            }
            if (status == SessionStatus.Failed) { Finish("SessionFailed", failure); return; }
            if (status == SessionStatus.Closed) { Finish("SessionClosed", null); return; }
            if (!_observed || changed || elapsed - _lastWrite >= TimeSpan.FromSeconds(1))
            {
                Persist("progress"); _lastWrite = elapsed; _observed = true;
            }
        }

        /// <summary>结束记录并保存最后观察到的确认快照。先 Observe 再 End，不能先销毁会话。</summary>
        /// <remarks>结束原因保留第一次：随后 OnDestroy/Dispose 不能覆盖不同步或断线原因。</remarks>
        public void End(string endState, string? reason = null)
        { if (!_finished) Safe(() => Finish(endState, reason)); }

        private void Finish(string endState, string? reason)
        {
            if (_finished) return;
            _finished = true; _entry.finished = true; _entry.endState = endState; _entry.reason = reason;
            if (_checkpoint != null) WriteBytes("final.checkpoint", CheckpointCodec.Encode(_checkpoint));
            Persist("finished"); _stream?.Dispose(); _stream = null;
        }

        private void Persist(string kind)
        {
            _entry.recordKind = kind; _entry.utc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            // 哈希只在落盘时重算，避免每个渲染帧都重新编码。核心仍逐个最终帧检查一致性。
            _entry.confirmedHash = _checkpoint == null ? null : CheckpointCodec.Hash(_checkpoint);
            if (_checkpoint != null) WriteBytes("latest.checkpoint", CheckpointCodec.Encode(_checkpoint));
            byte[] bytes = JsonBytes(_entry);
            _stream!.WriteLine(Encoding.UTF8.GetString(bytes));
            WriteBytes("summary.json", bytes);
        }

        private void SaveDesync(DesyncReport desync)
        {
            Directory.CreateDirectory(Path.Combine(DirectoryPath, "desync"));
            WriteBytes("desync/last-confirmed.checkpoint", CheckpointCodec.Encode(desync.LastConfirmed));
            WriteBytes("desync/candidate.checkpoint", CheckpointCodec.Encode(desync.Candidate));
            var frame = desync.Frame;
            var inputs = frame.Inputs.Select(x => new DiagnosticInput { slot = x.Slot, generation = x.Generation,
                origin = x.Origin.ToString(), payloadBase64 = Convert.ToBase64String(x.Payload.ToArray()) }).ToArray();
            var events = frame.MemberEvents.Select(x => new DiagnosticMemberEvent { sequence = x.Sequence,
                kind = x.Kind.ToString(), slot = x.Slot, generation = x.Generation }).ToArray();
            WriteBytes("desync/frame.json", JsonBytes(new DiagnosticFrame { sessionId = frame.SessionId.ToString(),
                epoch = frame.Epoch, tick = frame.Tick, inputs = inputs, memberEvents = events }));
            // 复用现有录制格式：恢复最后正确状态后，只需执行紧接着的这一帧即可重现分歧。
            var recorder = new ReplayRecorder(desync.LastConfirmed);
            recorder.Append(frame); WriteBytes("desync/problem.fsrp", ReplayCodec.Encode(recorder.Build()));
            WriteBytes("desync/report.json", JsonBytes(new DiagnosticDesync { sessionId = frame.SessionId.ToString(),
                epoch = frame.Epoch, problemTick = frame.Tick, lastConfirmedTick = desync.LastConfirmed.Tick,
                expectedHash = desync.ExpectedHash, actualHash = desync.ActualHash }));
        }

        // 同目录临时文件 + 原子替换：退出中断时保留上一份完整摘要/快照，不留下半个 JSON。
        private void WriteBytes(string relative, byte[] bytes)
        {
            string destination = Path.Combine(DirectoryPath, relative); string temporary = destination + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        private static byte[] JsonBytes<T>(T value)
        {
            using var stream = new MemoryStream(); new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); return stream.ToArray();
        }
        private void Safe(Action action)
        {
            if (Error != null) return;
            try { action(); }
            catch (Exception e)
            {
                Error = e.GetType().Name + ": " + e.Message;
                try { _stream?.Dispose(); } catch (Exception) { /* 保留首次写入错误，诊断清理不得打断游戏。 */ }
                _stream = null;
            }
        }
        /// <summary>兜底结束。宿主正常清理时应先 End，写明真实退出原因。</summary>
        public void Dispose() => End("Disposed");
    }
}
