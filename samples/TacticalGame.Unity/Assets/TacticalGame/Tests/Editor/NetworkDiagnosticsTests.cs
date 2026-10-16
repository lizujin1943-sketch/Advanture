using System;
using System.IO;
using System.Net;
using Advanture.Tactical.Network;
using Advanture.Tactical.Simulation;
using Advanture.TacticalGame.NetworkView;
using FrameSync.Runtime;
using NUnit.Framework;
using UnityEngine;
namespace Advanture.TacticalGame.Tests
{
    public sealed class NetworkDiagnosticsTests
    {
        private static string Root() => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../../artifacts/tactical-network/unity-diagnostics-tests", Guid.NewGuid().ToString("N")));
        private static TacticalMap Map() => TacticalMap.Decode(UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/TacticalGame/Network/Yard07Map.bytes").bytes);
        [Test] public void LeavingRecordsFreshStateForServerAndBothClients()
        {
            var map = Map(); using var host = new TacticalHost(map, 0);
            using var a = new TacticalClient(map, IPAddress.Loopback, host.Port, 0);
            using var b = new TacticalClient(map, IPAddress.Loopback, host.Port, 1);
            string root = Root(); using var da = new YardDiagnostics(root, true, "Unity EditMode"); using var db = new YardDiagnostics(root, false, "Unity EditMode");
            for (int ms = 0; ms < 800; ms++)
            {
                var now = TimeSpan.FromMilliseconds(ms); host.Poll(now); a.Poll(now, t => default); b.Poll(now, t => default);
                da.Observe(a, host, now); db.Observe(b, null, now);
            }
            da.End("LeaveRoom"); db.End("ApplicationQuit");
            Assert.That(da.Error, Is.Null); Assert.That(db.Error, Is.Null);
            foreach (var path in new[] { da.ClientDirectory, da.ServerDirectory, db.ClientDirectory })
            {
                Assert.That(File.Exists(Path.Combine(path, "summary.json")), Is.True, "离开房间时没有保存摘要");
                var data = JsonUtility.FromJson<Summary>(File.ReadAllText(Path.Combine(path, "summary.json")));
                Assert.That(data.finished, Is.True); Assert.That(data.desyncDetected, Is.False); Assert.That(data.confirmedTick, Is.GreaterThan(10));
                var checkpoint = CheckpointCodec.Decode(File.ReadAllBytes(Path.Combine(path, "final.checkpoint")));
                Assert.That(checkpoint.Tick, Is.EqualTo((ulong)data.confirmedTick)); Assert.That(CheckpointCodec.Hash(checkpoint), Is.EqualTo(data.confirmedHash));
            }
            Assert.That(JsonUtility.FromJson<Summary>(File.ReadAllText(Path.Combine(db.ClientDirectory, "summary.json"))).endState, Is.EqualTo("ApplicationQuit"));
        }
        [Test] public void RoomFailureBeforeSessionIsRecordedAndNotOverwrittenByLeave()
        {
            var map = Map(); using var host = new TacticalHost(map, 0);
            var other = new TacticalMap(map.Area, new[] { new PlayerSpawn(-3000, -8000, 9000), map.Spawns[1] }, map.Obstacles);
            using var client = new TacticalClient(other, IPAddress.Loopback, host.Port, 0);
            using var log = new YardDiagnostics(Root(), false, "Unity EditMode");
            for (int ms = 0; ms < 2000 && client.Failure == null; ms++)
            { var now = TimeSpan.FromMilliseconds(ms); host.Poll(now); client.Poll(now, t => default); log.Observe(client, null, now); }
            Assert.That(client.Failure, Is.Not.Null); Assert.That(client.Session, Is.Null);
            log.End("LeaveRoom"); Assert.That(File.Exists(Path.Combine(log.ClientDirectory, "summary.json")), Is.True);
            var data = JsonUtility.FromJson<Summary>(File.ReadAllText(Path.Combine(log.ClientDirectory, "summary.json")));
            Assert.That(data.finished, Is.True); Assert.That(data.endState, Is.EqualTo("ConnectionFailed"));
            Assert.That(data.reason, Does.Contain("地图或协议版本不同")); Assert.That(data.desyncDetected, Is.False);
        }
        [Serializable] private sealed class Summary
        {
            public bool finished, desyncDetected;
            public long confirmedTick;
            public string confirmedHash, endState, reason;
        }
    }
}
