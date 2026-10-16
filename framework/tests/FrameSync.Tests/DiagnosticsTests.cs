using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FrameSync.Diagnostics;
using FrameSync.Runtime;
using FrameSync.Sessions;

namespace FrameSync.Tests
{
    // 这些测试运行真实会话与文件写入：如果漏记退出、误报回滚、保存了错误帧或吞掉磁盘错误，就会失败。
    internal static class DiagnosticsTests
    {
        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("diagnostics.progress_rollback_and_final_snapshot", Progress);
            yield return ("diagnostics.desync_bundle_replays_problem_frame", () => Desync(false));
            yield return ("diagnostics.prediction_desync_bundle_replays_problem_frame", () => Desync(true));
            yield return ("diagnostics.connection_failure_before_initial_state", ConnectingFailure);
            yield return ("diagnostics.session_failure_is_not_desync", SessionFailure);
            yield return ("diagnostics.end_is_idempotent_and_runs_are_separate", SeparateRuns);
            yield return ("diagnostics.io_failure_does_not_stop_simulation", IoFailure);
            yield return ("diagnostics.server_final_and_unfinished_marker", ServerEnd);
        }
        private static string Root()
        {
            string path = Path.GetFullPath(Path.Combine("artifacts", "diagnostics-tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(path); return path;
        }
        private static JsonDocument Read(SessionDiagnostics log, string file = "summary.json")
        {
            string path = Path.Combine(log.DirectoryPath, file);
            Assert.True(File.Exists(path), "诊断文件未生成：" + file);
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        private static byte[] Bytes(SessionDiagnostics log, string file) => File.ReadAllBytes(Path.Combine(log.DirectoryPath, file));
        private static void Progress()
        {
            var f = new SyncFixture(prediction: new PredictionOptions()); f.Start();
            using var log = new SessionDiagnostics(Root(), "client-A"); log.ObserveClient(f.A, TimeSpan.Zero);
            f.A.TrySubmit(1, new byte[] {0}); f.A.Poll(1);
            f.B.TrySubmit(1, new byte[] {7}); f.Accept(); f.Finish(1);
            for (int i = 0; i < 10; i++) f.A.Poll();
            Assert.Equal(1UL, f.A.RollbackCount); f.AssertEqual(1, 7);
            log.ObserveClient(f.A, TimeSpan.FromSeconds(1));
            for (int i = 1; i < 50; i++) log.ObserveClient(f.A, TimeSpan.FromMilliseconds(1000 + i));
            int before = File.ReadAllLines(Path.Combine(log.DirectoryPath, "progress.jsonl")).Length;
            Assert.Equal(3, before); // started、首次观察、1 秒采样；不是渲染每帧刷盘。
            log.End("ApplicationQuit");
            using var summary = Read(log); var s = summary.RootElement;
            Assert.Equal(1UL, s.GetProperty("confirmedTick").GetUInt64());
            Assert.Equal(1UL, s.GetProperty("rollbackCount").GetUInt64());
            Assert.Equal("ApplicationQuit", s.GetProperty("endState").GetString());
            Assert.True(s.GetProperty("finished").GetBoolean()); Assert.True(!s.GetProperty("desyncDetected").GetBoolean());
            var final = CheckpointCodec.Decode(Bytes(log, "final.checkpoint"));
            Assert.Equal(CheckpointCodec.Hash(f.Server.CaptureCheckpoint()), CheckpointCodec.Hash(final));
            Assert.Equal(CheckpointCodec.Hash(final), s.GetProperty("confirmedHash").GetString());
            foreach (string line in File.ReadAllLines(Path.Combine(log.DirectoryPath, "progress.jsonl"))) using (JsonDocument.Parse(line)) { }
            Assert.True(!Directory.Exists(Path.Combine(log.DirectoryPath, "desync")), "正常回滚不能生成不同步报告");
        }
        private static void Desync(bool prediction)
        {
            var bad = new SyncTestGame { Bias = 1 };
            var f = new SyncFixture(prediction: prediction ? new PredictionOptions() : null, clientAGame: bad); f.Start();
            using var log = new SessionDiagnostics(Root(), "client-A"); log.ObserveClient(f.A, TimeSpan.Zero);
            f.A.TrySubmit(1, new byte[] {5}); f.B.TrySubmit(1, new byte[] {0});
            if (prediction) f.A.Poll(1);
            f.Accept(); f.Finish(1); for (int i = 0; i < 10; i++) f.A.Poll();
            Assert.Equal(SessionStatus.Failed, f.A.Status); Assert.True(f.A.Desync != null);
            log.ObserveClient(f.A, TimeSpan.FromMilliseconds(100)); // 未到心跳时间也必须立即落盘。
            using var summary = Read(log); Assert.Equal("Desync", summary.RootElement.GetProperty("endState").GetString());
            Assert.Equal(0UL, summary.RootElement.GetProperty("confirmedTick").GetUInt64());
            using var report = Read(log, "desync/report.json"); var r = report.RootElement;
            Assert.Equal(1UL, r.GetProperty("problemTick").GetUInt64());
            Assert.Equal(CheckpointCodec.Hash(f.Server.CaptureCheckpoint()), r.GetProperty("expectedHash").GetString());
            using var input = Read(log, "desync/frame.json");
            Assert.Equal(1UL, input.RootElement.GetProperty("tick").GetUInt64());
            Assert.Equal("BQ==", input.RootElement.GetProperty("inputs")[0].GetProperty("payloadBase64").GetString());
            var replay = ReplayCodec.Decode(Bytes(log, "desync/problem.fsrp"));
            Assert.Equal(0UL, replay.Initial.Tick); Assert.Equal(1, replay.Frames.Count);
            Assert.Bytes(new byte[] {5}, replay.Frames[0].Inputs[0].Payload.ToArray());
            var healthyRunner = new SimulationRunner<long>(new SyncTestGame().Module(), replay.Initial.Session, replay.Initial.Limits, replay.Initial.InitializationData);
            var faultyRunner = new SimulationRunner<long>(bad.Module(), replay.Initial.Session, replay.Initial.Limits, replay.Initial.InitializationData);
            Assert.Equal(r.GetProperty("expectedHash").GetString(), CheckpointCodec.Hash(ReplayPlayer.Play(healthyRunner, replay)));
            Assert.Equal(r.GetProperty("actualHash").GetString(), CheckpointCodec.Hash(ReplayPlayer.Play(faultyRunner, replay)));
            Assert.Equal(0UL, CheckpointCodec.Decode(Bytes(log, "desync/last-confirmed.checkpoint")).Tick);
            Assert.Equal(1UL, CheckpointCodec.Decode(Bytes(log, "desync/candidate.checkpoint")).Tick);
            Assert.Equal(r.GetProperty("actualHash").GetString(), CheckpointCodec.Hash(CheckpointCodec.Decode(Bytes(log, "desync/candidate.checkpoint"))));
            log.End("ApplicationQuit"); using var unchanged = Read(log);
            Assert.Equal("Desync", unchanged.RootElement.GetProperty("endState").GetString());
        }
        private static void ConnectingFailure()
        {
            using var log = new SessionDiagnostics(Root(), "client-B");
            log.ObserveClient<long>(null, TimeSpan.FromSeconds(2));
            string reason = "连接失败：\"地址\"\n第二行"; log.End("ConnectionFailed", reason);
            using var doc = Read(log); var s = doc.RootElement;
            Assert.Equal(reason, s.GetProperty("reason").GetString());
            Assert.Equal("Connecting", s.GetProperty("sessionStatus").GetString());
            Assert.True(s.GetProperty("confirmedHash").ValueKind == JsonValueKind.Null);
            Assert.True(!File.Exists(Path.Combine(log.DirectoryPath, "final.checkpoint")));
        }
        private static void SessionFailure()
        {
            var f = new SyncFixture(serverGame: new SyncTestGame { ThrowOnStep = true }); f.Start();
            using var log = new SessionDiagnostics(Root(), "server"); log.ObserveServer(f.Server, TimeSpan.Zero);
            f.Finish(1); log.ObserveServer(f.Server, TimeSpan.FromMilliseconds(100));
            using var doc = Read(log); var s = doc.RootElement;
            Assert.Equal("SessionFailed", s.GetProperty("endState").GetString());
            Assert.True(!s.GetProperty("desyncDetected").GetBoolean());
            Assert.True(s.GetProperty("reason").GetString()!.Contains("Intentional Step failure", StringComparison.Ordinal));
        }
        private static void SeparateRuns()
        {
            string root = Root(); using var a = new SessionDiagnostics(root, "client"); using var b = new SessionDiagnostics(root, "client");
            Assert.True(a.DirectoryPath != b.DirectoryPath);
            a.End("LeaveRoom", "第一次退出"); string text = File.ReadAllText(Path.Combine(a.DirectoryPath, "summary.json"));
            a.End("ApplicationQuit"); a.Dispose(); a.ObserveClient<long>(null, TimeSpan.FromSeconds(2));
            Assert.Equal(text, File.ReadAllText(Path.Combine(a.DirectoryPath, "summary.json")));
            using var second = Read(b); Assert.True(!second.RootElement.GetProperty("finished").GetBoolean());
        }
        private static void IoFailure()
        {
            string blocked = Path.Combine(Root(), "occupied"); File.WriteAllText(blocked, "keep");
            using var log = new SessionDiagnostics(blocked, "client");
            Assert.True(!string.IsNullOrEmpty(log.Error), "诊断写入失败必须暴露错误，不能假装已保存");
            var f = new SyncFixture(); f.Start(); f.Finish(1); log.ObserveClient(f.A, TimeSpan.FromSeconds(1)); log.End("ApplicationQuit");
            f.AssertEqual(1, 0); Assert.Equal("keep", File.ReadAllText(blocked));
        }
        private static void ServerEnd()
        {
            var f = new SyncFixture(); f.Start(); using var log = new SessionDiagnostics(Root(), "server");
            log.ObserveServer(f.Server, TimeSpan.Zero);
            using (var running = Read(log)) Assert.True(!running.RootElement.GetProperty("finished").GetBoolean());
            f.Finish(1); log.ObserveServer(f.Server, TimeSpan.FromMilliseconds(100)); log.End("LeaveRoom");
            using var summary = Read(log); var s = summary.RootElement;
            Assert.Equal(1UL, s.GetProperty("confirmedTick").GetUInt64());
            Assert.Equal("Running", s.GetProperty("sessionStatus").GetString());
            Assert.Equal(f.Server.CaptureCheckpoint().Session.SessionId.ToString(), s.GetProperty("sessionId").GetString());
            Assert.Equal(CheckpointCodec.Hash(f.Server.CaptureCheckpoint()), CheckpointCodec.Hash(CheckpointCodec.Decode(Bytes(log, "final.checkpoint"))));
        }
    }
}
