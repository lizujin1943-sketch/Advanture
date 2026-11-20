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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForExplicitRun()
        {
            if (!YardLaunchArguments.Has("-yard-smoke")) return;
            var controller = FindObjectOfType<NetworkYardController>();
            if (controller != null && controller.GetComponent<NetworkSmokeDriver>() == null)
                controller.gameObject.AddComponent<NetworkSmokeDriver>();
        }
        private NetworkYardController room;
        private string report;

        private bool combatRun, sawDamage, sawDeath, sawRespawn, sawReload;
        private bool soloRun, sawPlaza, sawYard, sawPlazaReturn;
        private bool sawLocalJump, sawLocalLanding, sawRemoteJump, sawRemoteLanding;

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
            room = GetComponent<NetworkYardController>();
            string role = YardLaunchArguments.Value("-yard-role");
            soloRun = role == "solo";
            slot = (ushort)(role == "join" ? 1 : 0);
            combatRun=YardLaunchArguments.Has("-yard-combat");
            room.InputProvider = Sample; enabledRun = true; started = Time.realtimeSinceStartup;
        }
        private TacticalInput Sample(ulong tick)
        {
            if (soloRun)
            {
                if (tick >= 28 && tick <= 30) return new TacticalInput(0,0,jump:true);
                if (tick > 60 && tick <= 230) return new TacticalInput(0,-1000);
                return tick == 255 ? new TacticalInput(0,0,respawn:true) : default;
            }
            short x = tick <= 20 ? (short)(slot == 0 ? 500 : -500) : (short)0;
            short z = tick > 20 && tick <= 40 ? (short)500 : (short)0;
            ushort yaw = (ushort)((slot == 0 ? 9000 : 27000) + (tick < 90 ? (int)(tick / 15 % 2) * 1500 : 0));
            if(combatRun && tick>=90 && room.Client?.Session?.LocalParticipant!=null)
            {
                var players=room.Client.Session.InspectState().Players;var self=players[slot];var other=players[1-slot];
                yaw=(ushort)((Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(other.X-self.X,other.Z-self.Z)*Mathf.Rad2Deg,360)*100))%36000);
            }
            // Hold through a normal burst even when a background window loses a few input ticks.
            bool fire=combatRun && (slot==0?tick>=100 && tick<135:tick>=220 && tick<255);
            return new TacticalInput(x, z, yaw, tick < 90 ? (short)500 : (short)0, true, fire:fire,
                reload:combatRun && slot==0 && tick>=140 && tick<152, jump:tick>=55 && tick<=58);
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
                    sawDamage |= p.Combat.Health<100;
                    sawDeath |= !p.Combat.IsAlive;
                    sawRespawn |= p.Combat.Deaths>0 && p.Combat.IsAlive;
                    sawReload |= p.Combat.ReloadUntilTick>s.ConfirmedTick;
                    float ground = room.Map.GroundHeight(p.X, p.Z) * .001f;
                    if (p.Slot == slot)
                    {
                        float cameraRise = room.ViewCamera.transform.position.y - ground - 1.65f;
                        sawLocalJump |= p.IsAirborne && cameraRise > .35f;
                        sawLocalLanding |= sawLocalJump && !p.IsAirborne && Mathf.Abs(cameraRise) < .05f;
                        continue;
                    }
                    if (room.RemoteTransform != null)
                    {
                        float visibleRise = room.RemoteTransform.position.y - ground;
                        sawRemoteJump |= p.IsAirborne && visibleRise > .35f;
                        sawRemoteLanding |= sawRemoteJump && !p.IsAirborne && Mathf.Abs(visibleRise) < .05f;
                    }
                    var spawn = room.Map.Spawns[p.Slot];
                    sawMove |= Math.Abs(p.X - spawn.X) > 300 || Math.Abs(p.Z - spawn.Z) > 300;
                    sawTurn |= p.Yaw != spawn.Yaw;
                }
                if (room.RemoteTransform != null)
                {
                    if (!haveRemote) { firstRemote = room.RemoteTransform.position; haveRemote = true; }
                    sawRenderedMove |= Vector3.Distance(firstRemote, room.RemoteTransform.position) > .3f;
                }
                if (soloRun && room.LocalPlayer.HasValue)
                {
                    var p = room.LocalPlayer.Value;
                    var spawn = room.Map.Spawns[0];
                    bool atSpawn = p.X == spawn.X && p.Z == spawn.Z && Mathf.Abs(room.ViewCamera.transform.position.y-3.70f)<.025f;
                    sawPlaza |= s.ConfirmedTick<60 && atSpawn;
                    sawYard |= p.Z < -16000 && room.Map.GroundHeight(p.X,p.Z)==0;
                    sawPlazaReturn |= s.ConfirmedTick>=255 && atSpawn;
                }
                if (!captured && s.ConfirmedTick >= (soloRun?20UL:combatRun?340UL:120UL))
                { Directory.CreateDirectory(Path.GetDirectoryName(report)); ScreenCapture.CaptureScreenshot(report + ".png"); captured = true; }
                if (!closedHost && room.Host?.Session?.ConfirmedTick >= (soloRun?295UL:combatRun?360UL:180UL))
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
                sawPlaza=sawPlaza,sawYard=sawYard,sawPlazaReturn=sawPlazaReturn,
                sawLocalJump=sawLocalJump, sawLocalLanding=sawLocalLanding, sawRemoteJump=sawRemoteJump, sawRemoteLanding=sawRemoteLanding,
                sawRemoteMovement = sawMove, sawRemoteTurning = sawTurn, sawRenderedMovement = sawRenderedMove,
                rollbackCount = room.Client?.Session?.RollbackCount.ToString(),
                sawDamage=sawDamage,sawDeath=sawDeath,sawRespawn=sawRespawn,sawReload=sawReload,
                sawLocalShots=room.LocalWeapon!=null && room.LocalWeapon.PresentedShots>0,
                sawRemoteShots=room.RemoteWeapon!=null && room.RemoteWeapon.PresentedShots>0,
                localWeaponVisible=room.LocalWeapon!=null && room.LocalWeapon.IsVisible, players = new PlayerReport[state.Players.Count] };
            for (int i = 0; i < data.players.Length; i++) { var p = state.Players[i]; data.players[i] = new PlayerReport { slot=p.Slot,x=p.X,z=p.Z,yaw=p.Yaw,pitch=p.Pitch,health=p.Combat.Health,ammo=p.Combat.Ammo,reserve=p.Combat.Reserve,kills=(int)p.Combat.Kills,deaths=(int)p.Combat.Deaths,shots=(int)p.Combat.ShotSequence }; }
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
            public bool sawPlaza,sawYard,sawPlazaReturn;
            public bool sawLocalJump, sawLocalLanding, sawRemoteJump, sawRemoteLanding;
            public bool sawDamage,sawDeath,sawRespawn,sawReload,sawLocalShots,sawRemoteShots,localWeaponVisible;
            public bool sawRemoteMovement, sawRemoteTurning, sawRenderedMovement, remoteInView;
            public PlayerReport[] players;
        }
        [Serializable] private sealed class PlayerReport { public int slot,x,z,yaw,pitch,health,ammo,reserve,kills,deaths,shots; }
#endif
    }
}
