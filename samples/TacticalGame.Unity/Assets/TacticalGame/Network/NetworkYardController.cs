using System;
using System.Diagnostics;
using System.Net;
using System.IO;
using Advanture.Tactical.Network;
using Advanture.Tactical.Simulation;
using FrameSync.Sessions;
using UnityEngine;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>Unity 组合入口。持有房间生命周期、相机和角色视图，不计算游戏移动或改写模拟状态。</summary>
    [RequireComponent(typeof(NetworkInputSource))]
    public sealed class NetworkYardController : MonoBehaviour
    {
        [SerializeField] private TextAsset mapData;
        [SerializeField] private Camera viewCamera;
        private readonly Stopwatch clock = new Stopwatch();
        private TacticalMap map;
        private NetworkInputSource input;
        private NetworkRoomOverlay overlay;
        private NetworkAvatar remote;
        private ushort localSlot;
        private bool cameraPlaced;
        private string error;
        private YardDiagnostics diagnostics;
        private TimeSpan diagnosticStarted;
        private bool diagnosticWarningReported, failureReported;
        private string diagnosticSetupError;
        public string DiagnosticWarning => diagnosticSetupError ?? diagnostics?.Error;
        public TacticalHost Host { get; private set; }
        public TacticalClient Client { get; private set; }
        public Camera ViewCamera => viewCamera;
        public Transform RemoteTransform => remote != null ? remote.transform : null;
        public NetworkInputSource Input => input;
        public bool InRoom => Client != null;
        public bool Playing => Client?.Session?.Status == SessionStatus.Running && Failure == null;
        public string Failure => error ?? Client?.Failure ?? Host?.Failure;
        // 可替换输入来源供手柄、录像或开发验收使用；接口仍走同一个冻结和网络流程。
        public Func<ulong, TacticalInput> InputProvider { get; set; }
        public string StatusText => Failure ?? (!InRoom ? "创建房间，或输入好友的局域网地址" :
            Playing ? (localSlot == 0 ? "你是蓝色 A · 好友是橙色 B" : "你是橙色 B · 好友是蓝色 A") : "等待另一位玩家连接…");

        public void Configure(TextAsset data, Camera camera) { mapData = data; viewCamera = camera; }
        private void Awake()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            input = GetComponent<NetworkInputSource>(); overlay = GetComponent<NetworkRoomOverlay>(); clock.Start();
            try { map = TacticalMap.Decode(mapData.bytes); }
            catch (Exception e) { error = "地图数据无法读取：" + e.Message; }
        }
        public void HostRoom(int port)
        {
            LeaveRoom(); if (map == null) { error = "缺少地图数据。"; return; }
            StartDiagnostics(true);
            try { Host = new TacticalHost(map, port); Connect(IPAddress.Loopback, Host.Port, 0); }
            catch (Exception e) { EndRoom("ConnectionFailed", e.ToString()); error = "无法创建房间：" + e.Message; }
        }
        public void JoinRoom(string address, int port)
        {
            LeaveRoom(); if (map == null) { error = "缺少地图数据。"; return; }
            StartDiagnostics(false);
            try { Connect(IPAddress.Parse(address), port, 1); }
            catch (Exception e) { EndRoom("ConnectionFailed", e.ToString()); error = "无法加入房间：" + e.Message; }
        }
        private void Connect(IPAddress address, int port, ushort slot)
        {
            Client = new TacticalClient(map, address, port, slot); localSlot = slot; cameraPlaced = false;
            input.InitializeLook(map.Spawns[slot].Yaw / 100f);
        }
        private void Update()
        {
            input.CaptureDevice(Playing, overlay == null || !overlay.PointerOverLeaveButton);
            if (Client == null || error != null) return;
            try
            {
                Host?.Poll(clock.Elapsed);
                Client.Poll(clock.Elapsed, InputProvider ?? input.Sample);
                ObserveDiagnostics(); // 必须在失败提前返回前读取 Desync，不能等到退出窗口再记录。
                if (Failure != null) { input.Release(); return; }
                var session = Client.Session;
                if (session?.LocalParticipant == null) return;
                TacticalState confirmed = session.InspectState(), predicted = session.InspectPredictedState();
                foreach (var p in confirmed.Players)
                {
                    if (p.Slot == localSlot) continue;
                    if (remote == null) remote = NetworkAvatar.Create(p.Slot);
                    remote.Present(p, viewCamera, Time.unscaledDeltaTime);
                }
                foreach (var p in predicted.Players)
                {
                    if (p.Slot != localSlot) continue;
                    Vector3 target = new Vector3(p.X / 1000f, 1.65f, p.Z / 1000f);
                    viewCamera.transform.position = cameraPlaced ? Vector3.Lerp(viewCamera.transform.position, target, 1 - Mathf.Exp(-25 * Time.unscaledDeltaTime)) : target;
                    // 鼠标视角立即响应；共享角色朝向由已冻结输入推进，远端展示确认结果。
                    viewCamera.transform.rotation = input.HasControl ? Quaternion.Euler(input.Pitch, input.Yaw, 0) : Quaternion.Euler(p.Pitch / 100f, p.Yaw / 100f, 0);
                    cameraPlaced = true;
                }
            }
            catch (Exception e)
            {
                error = "联机已停止：" + e.Message; input.Release();
                ObserveDiagnostics(); diagnostics?.End("RuntimeException", e.ToString());
                CheckDiagnosticError(); UnityEngine.Debug.LogException(e);
            }
        }
        private void StartDiagnostics(bool hosting)
        {
            diagnostics = null; diagnosticSetupError = null;
            diagnosticStarted = clock.Elapsed; diagnosticWarningReported = false; failureReported = false;
            try
            {
                string root = DiagnosticRoot();
                diagnostics = new YardDiagnostics(root, hosting, "Unity " + Application.unityVersion + " / game " + Application.version + " / " + Environment.OSVersion);
                UnityEngine.Debug.Log("YARD_DIAGNOSTICS client=" + diagnostics.ClientDirectory + (hosting ? " server=" + diagnostics.ServerDirectory : ""));
            }
            catch (Exception e) { diagnosticSetupError = e.Message; }
            CheckDiagnosticError();
        }
        private static string DiagnosticRoot()
        {
            string requested = YardLaunchArguments.Value("-yard-diagnostics");
            if (!string.IsNullOrEmpty(requested)) return requested;
            // 在仓库里的编辑器/构建统一写入 artifacts；发布到其他目录时使用应用可写数据目录。
            for (var directory = new DirectoryInfo(Application.dataPath); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "framework/FrameSync.sln")))
                    return Path.Combine(directory.FullName, "artifacts/tactical-network/diagnostics");
            return Path.Combine(Application.persistentDataPath, "diagnostics");
        }
        private void ObserveDiagnostics()
        {
            diagnostics?.Observe(Client, Host, clock.Elapsed - diagnosticStarted);
            CheckDiagnosticError();
            if (Failure != null && !failureReported)
            {
                failureReported = true;
                UnityEngine.Debug.Log("YARD_SESSION_STOP " + Failure);
            }
        }
        private void CheckDiagnosticError()
        {
            if (DiagnosticWarning == null || diagnosticWarningReported) return;
            diagnosticWarningReported = true;
            UnityEngine.Debug.LogWarning("YARD_DIAGNOSTICS_WRITE_FAILED " + DiagnosticWarning);
        }
        public void LeaveRoom() => EndRoom("LeaveRoom");
        private void EndRoom(string state, string reason = null)
        {
            // 在 Dispose 和清空引用前采集最新帧；OnApplicationQuit 后的 OnDestroy 不会覆盖首次结束状态。
            ObserveDiagnostics(); diagnostics?.End(state, reason); CheckDiagnosticError(); diagnostics?.Dispose();
            Client?.Dispose(); Host?.Dispose(); Client = null; Host = null; error = null;
            if (input != null) input.Release(); if (remote != null) Destroy(remote.gameObject); remote = null;
        }
        private void OnDestroy() => EndRoom("SceneDestroyed");
        private void OnApplicationQuit() => EndRoom("ApplicationQuit");
    }
}
