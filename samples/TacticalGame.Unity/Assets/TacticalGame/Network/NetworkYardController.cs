using System;
using System.Diagnostics;
using System.Net;
using System.IO;
using Advanture.HookIsland;
using Advanture.HookIsland.Simulation;
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
        [SerializeField] private string mapTitle = "七号转运站";
        [SerializeField] private bool allowSolo;
        public TacticalMap Map => map;
        public string MapTitle => mapTitle;
        public bool AllowSolo => allowSolo;
        private bool solo;
        private readonly Stopwatch clock = new Stopwatch();
        private TacticalMap map;
        private NetworkInputSource input;
        private NetworkRoomOverlay overlay;
        private FortifiedCompoundOverlay compoundOverlay;
        private NetworkAvatar[] remotes = Array.Empty<NetworkAvatar>();
        private EldritchPresentation encounters;
        private Func<int,int,float> groundHeight;
        private Ak47View localWeapon;
        private bool localWasAlive=true;
        public TacticalPlayer? LocalPlayer { get; private set; }
        public TacticalState ConfirmedState { get; private set; }
        public Ak47View LocalWeapon => localWeapon;
        // 双人验收工具使用第一个队友；实际表现按席位分别更新。
        private NetworkAvatar FirstRemote => Array.Find(remotes, avatar => avatar != null);
        public Ak47View RemoteWeapon => FirstRemote?.Weapon;
        private bool cameraPlaced;
        private Vector3 renderedEye;
        private const float StandingEyeHeight=1.65f;
        private string error;
        private YardDiagnostics diagnostics;
        private TimeSpan diagnosticStarted;
        private bool diagnosticWarningReported, failureReported;
        private string diagnosticSetupError;
        public string DiagnosticWarning => diagnosticSetupError ?? diagnostics?.Error;
        public TacticalHost Host { get; private set; }
        public TacticalClient Client { get; private set; }
        public Camera ViewCamera => viewCamera;
        public Transform RemoteTransform => FirstRemote?.transform;
        public NetworkInputSource Input => input;
        public bool InRoom => Client != null;
        public bool Playing => Client?.Session?.Status == SessionStatus.Running && Failure == null;
        public string Failure => error ?? Client?.Failure ?? Host?.Failure;
        // 可替换输入来源供手柄、录像或开发验收使用；接口仍走同一个冻结和网络流程。
        public Func<ulong, TacticalInput> InputProvider { get; set; }
        public string StatusText => Failure ?? (!InRoom ? "创建房间，或输入好友的局域网地址" :
            Playing && solo ? "单人探索" :
            Playing && LocalPlayer.HasValue ? $"联机中 · 你是 {(char)('A' + LocalPlayer.Value.Slot)}" :
            Host != null ? $"等待开局 · {Host.JoinedCount}/{Host.Capacity} 人" : "正在连接，等待房主开局…");

        public void Configure(TextAsset data, Camera camera, string title = "七号转运站", bool enableSolo = false)
        { mapData = data; viewCamera = camera; mapTitle = title; allowSolo = enableSolo; }
        private void Awake()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            input = GetComponent<NetworkInputSource>(); overlay = GetComponent<NetworkRoomOverlay>();
            compoundOverlay=GetComponent<FortifiedCompoundOverlay>();clock.Start();
            try { map = TacticalMap.Decode(mapData.bytes); groundHeight = (x,z) => map.GroundHeight(x,z)*.001f; }
            catch (Exception e) { error = "地图数据无法读取：" + e.Message; }
            if(map?.HasDefenseWaves==true)input.UseTownCamera(viewCamera,StandingEyeHeight);
        }
        public void HostRoom(int port) => HostRoom(port, map?.Spawns.Count ?? 0);
        public void PlaySolo() { if (allowSolo) HostRoom(0, 1); }
        public void HostRoom(int port, int players)
        {
            LeaveRoom(); if (map == null) { error = "缺少地图数据。"; return; }
            StartDiagnostics(true);
            try { solo = players == 1; Host = new TacticalHost(map, port, players); Client = new TacticalClient(map, IPAddress.Loopback, Host.Port, 0); }
            catch (Exception e) { EndRoom("ConnectionFailed", e.ToString()); error = "无法创建房间：" + e.Message; }
        }
        public void JoinRoom(string address, int port)
        {
            LeaveRoom(); if (map == null) { error = "缺少地图数据。"; return; }
            StartDiagnostics(false);
            try { Client = new TacticalClient(map, IPAddress.Parse(address), port); }
            catch (Exception e) { EndRoom("ConnectionFailed", e.ToString()); error = "无法加入房间：" + e.Message; }
        }
        private void Update()
        {
            compoundOverlay?.CaptureShortcuts();
            if(map?.HasDefenseWaves==true)
                input.CaptureDevice(Playing && LocalPlayer?.Combat.IsAlive==true && InputProvider==null,compoundOverlay==null || !compoundOverlay.BlocksPlay);
            else input.CaptureDevice(Playing, overlay == null || !overlay.PointerOverLeaveButton);
            if (Client == null || error != null) return;
            try
            {
                Host?.Poll(clock.Elapsed);
                Client.Poll(clock.Elapsed, InputProvider ?? input.Sample);
                ObserveDiagnostics(); // 必须在失败提前返回前读取 Desync，不能等到退出窗口再记录。
                if (Failure != null) { input.Release(); return; }
                var session = Client.Session;
                if (session?.LocalParticipant == null) return;
                ushort localSlot = session.LocalParticipant.Slot;
                if (!LocalPlayer.HasValue)
                {
                    input.InitializeLook(map.Spawns[localSlot].Yaw / 100f);
                    remotes = new NetworkAvatar[map.Spawns.Count];
                }
                TacticalState confirmed = session.InspectState(), predicted = session.InspectPredictedState();
                ConfirmedState=confirmed;
                float delta = Time.unscaledDeltaTime;
                if (confirmed.Monsters.Creatures.Count > 0)
                {
                    if (encounters == null) encounters = new EldritchPresentation(transform,Shader.Find("Sprites/Default"),new EldritchConfig(),true,map.SpawnSchedule);
                    encounters.Synchronize(confirmed.Monsters,confirmed.Steps,groundHeight,delta);
                }
                foreach (var p in confirmed.Players)
                {
                    if (p.Slot != localSlot) continue;
                    LocalPlayer = p;
                    if (localWeapon == null) localWeapon = Ak47View.Create(viewCamera.transform, true);
                    if (!localWasAlive && p.Combat.IsAlive) { cameraPlaced = false; input.InitializeLook(p.Yaw / 100f); }
                    localWasAlive = p.Combat.IsAlive;
                }
                foreach (var p in predicted.Players)
                {
                    if (p.Slot != localSlot) continue;
                    Vector3 target = new Vector3(p.X / 1000f, map.FeetHeight(p)/1000f + (p.Combat.IsAlive?StandingEyeHeight:.65f), p.Z / 1000f);
                    renderedEye = cameraPlaced ? Vector3.Lerp(renderedEye, target, 1 - Mathf.Exp(-25 * delta)) : target;
                    if(map.HasDefenseWaves)input.FollowTownCamera(renderedEye-Vector3.up*StandingEyeHeight,p,InputProvider!=null,delta);
                    else viewCamera.transform.SetPositionAndRotation(renderedEye,input.HasControl ?
                        Quaternion.Euler(input.Pitch, input.Yaw, 0) : Quaternion.Euler(p.Pitch / 100f, p.Yaw / 100f, 0));
                    cameraPlaced = true;
                    // Locomotion follows the displayed camera. Shots, impacts and ammo still come from confirmed state.
                    localWeapon?.SampleMovement(viewCamera.transform, p.IsAirborne, delta);
                }
                if (LocalPlayer.HasValue)
                {
                    int shots=localWeapon.PresentedShots;
                    localWeapon.Present(LocalPlayer.Value.Combat,session.ConfirmedTick,delta);
                    if(localWeapon.PresentedShots!=shots)input.ConfirmShot();
                }
                foreach (var p in confirmed.Players)
                {
                    if (p.Slot == localSlot) continue;
                    if (remotes[p.Slot] == null)
                    {
                        remotes[p.Slot] = NetworkAvatar.Create(p.Slot, viewCamera,map.HasDefenseWaves);
                        remotes[p.Slot].transform.SetParent(transform, true);
                    }
                    remotes[p.Slot].Present(p, viewCamera, delta, session.ConfirmedTick, map.FeetHeight(p)/1000f);
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
                diagnostics = new YardDiagnostics(root, hosting, "Unity " + Application.unityVersion + " / game " + Application.version + " / " + Environment.OSVersion, map.Spawns.Count);
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
            encounters?.Dispose(); encounters = null;
            solo = false; LocalPlayer = null; ConfirmedState=null; localWasAlive = true; cameraPlaced = false;
            if (localWeapon != null) { localWeapon.gameObject.SetActive(false); Destroy(localWeapon.gameObject); }
            localWeapon = null;
            if (input != null) input.ResetSession();
            compoundOverlay?.ResetSession();
            foreach (var avatar in remotes)
                if (avatar != null) { avatar.gameObject.SetActive(false); Destroy(avatar.gameObject); }
            remotes = Array.Empty<NetworkAvatar>();
        }
        private void OnDestroy() => EndRoom("SceneDestroyed");
        private void OnApplicationQuit() => EndRoom("ApplicationQuit");
    }
}
