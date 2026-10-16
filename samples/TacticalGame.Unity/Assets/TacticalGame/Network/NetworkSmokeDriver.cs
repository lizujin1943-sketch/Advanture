using System;
using System.IO;
using Advanture.Tactical.Simulation;
using FrameSync.Runtime;
using FrameSync.Sessions;
using UnityEngine;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>只在开发构建中启用的双进程验收驱动。输入仍经过普通冻结、UDP、组帧和回滚路径。</summary>
    public sealed class NetworkSmokeDriver : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private NetworkYardController room;
        private string report;
        private bool enabledRun, closedHost, written, captured, sawMove, sawTurn, sawRenderedMove;
        private float started, finished;
        private Vector3 firstRemote;
        private bool haveRemote;
        private ushort slot;
        private void Awake()
        {
            if (!YardLaunchArguments.Has("-yard-smoke")) return;
            report = YardLaunchArguments.Value("-yard-report");
            if (string.IsNullOrEmpty(report)) { Debug.LogError("-yard-smoke requires -yard-report"); return; }
            room = GetComponent<NetworkYardController>(); slot = (ushort)(YardLaunchArguments.Value("-yard-role") == "host" ? 0 : 1);
            room.InputProvider = Sample; enabledRun = true; started = Time.realtimeSinceStartup;
        }
        private TacticalInput Sample(ulong tick)
        {
            short x = tick <= 20 ? (short)(slot == 0 ? 500 : -500) : (short)0;
            short z = tick > 20 && tick <= 40 ? (short)500 : (short)0;
            ushort yaw = (ushort)((slot == 0 ? 9000 : 27000) + (tick < 90 ? (int)(tick / 15 % 2) * 1500 : 0));
            return new TacticalInput(x, z, yaw, tick < 90 ? (short)500 : (short)0, true);
        }
        private void LateUpdate()
        {
            if (!enabledRun) return;
            if (written) { if (Time.realtimeSinceStartup - finished > 1.5f) Application.Quit(); return; }
            var s = room.Client?.Session;
            if (s?.LocalParticipant != null)
            {
                foreach (var p in s.InspectState().Players)
                {
                    if (p.Slot == slot) continue;
                    int startX = p.Slot == 0 ? -4000 : 4000;
                    sawMove |= Math.Abs(p.X - startX) > 300 || Math.Abs(p.Z + 8000) > 300;
                    sawTurn |= p.Yaw != (p.Slot == 0 ? 9000 : 27000);
                }
                if (room.RemoteTransform != null)
                {
                    if (!haveRemote) { firstRemote = room.RemoteTransform.position; haveRemote = true; }
                    sawRenderedMove |= Vector3.Distance(firstRemote, room.RemoteTransform.position) > .3f;
                }
                if (!captured && s.ConfirmedTick >= 120)
                { Directory.CreateDirectory(Path.GetDirectoryName(report)); ScreenCapture.CaptureScreenshot(report + ".png"); captured = true; }
                if (!closedHost && room.Host?.Session?.ConfirmedTick >= 180)
                {
                    // 使用正式的保存并关闭入口，固定最后确认状态；两端会排空剩余最终帧再关闭。
                    room.Host.Session.SaveAndClose(c => WriteCheckpoint(report + ".server.json", c, -1)); closedHost = true;
                }
                if (s.Status == SessionStatus.Closed)
                {
                    WriteCheckpoint(report, s.CaptureCheckpoint(), slot);
                    written = true; finished = Time.realtimeSinceStartup; return;
                }
            }
            if (room.Failure != null || Time.realtimeSinceStartup - started > 45)
            {
                var failure = new Report { error = room.Failure ?? "45 秒内没有完成联机验收" };
                Directory.CreateDirectory(Path.GetDirectoryName(report)); File.WriteAllText(report, JsonUtility.ToJson(failure, true));
                enabledRun = false; Debug.LogError(failure.error); Application.Quit(1);
            }
        }
        private void WriteCheckpoint(string path, Checkpoint checkpoint, int who)
        {
            var state = new Advanture.Tactical.Simulation.TacticalGame().Decode(checkpoint.ApplicationState.Span);
            var data = new Report { slot = who, tick = checkpoint.Tick.ToString(), hash = CheckpointCodec.Hash(checkpoint),
                sawRemoteMovement = sawMove, sawRemoteTurning = sawTurn, sawRenderedMovement = sawRenderedMove,
                rollbackCount = room.Client?.Session?.RollbackCount.ToString(), players = new PlayerReport[state.Players.Count] };
            for (int i = 0; i < data.players.Length; i++) { var p = state.Players[i]; data.players[i] = new PlayerReport { slot=p.Slot,x=p.X,z=p.Z,yaw=p.Yaw,pitch=p.Pitch }; }
            if (room.RemoteTransform != null)
            {
                var point = room.ViewCamera.WorldToViewportPoint(room.RemoteTransform.position + Vector3.up);
                data.remoteInView = point.z > 0 && point.x > 0 && point.x < 1 && point.y > 0 && point.y < 1;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(data, true));
            File.WriteAllBytes(path + ".checkpoint", CheckpointCodec.Encode(checkpoint)); Debug.Log("YARD_NETWORK_REPORT " + path + " tick=" + checkpoint.Tick + " hash=" + data.hash);
        }
        [Serializable] private sealed class Report
        {
            public string error, tick, hash, rollbackCount;
            public int slot;
            public bool sawRemoteMovement, sawRemoteTurning, sawRenderedMovement, remoteInView;
            public PlayerReport[] players;
        }
        [Serializable] private sealed class PlayerReport { public int slot,x,z,yaw,pitch; }
#endif
    }
}
