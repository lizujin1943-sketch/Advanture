using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Editor
{
    /// <summary>
    /// 显式启用的编辑器试玩检查。使用正式地图和普通移动／装备／射击输入，绝不修改世界快照或注入资源。
    /// 此文件只编入 Editor，不挂在正式场景，不进入玩家构建。
    /// </summary>
    public static class BossChallengeSmoke
    {
        private static IslandFishingController game;
        private static Func<ulong, HookInput> previousInput;
        private static bool emergence, attack, ink;
        private static int waypoint;
        private static ulong start;
        // 先直行通过窄桥，再绕开原地图北侧商船；不走窄桥边角的水面捷径。
        private static readonly Vector2Int[] Route = { new Vector2Int(0, 10000), new Vector2Int(4000, 10000), new Vector2Int(4000, 16000) };
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../artifacts/abyssal-watcher/integration/unity-play"));

        public static string Start()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("先在 HookPlatform 中进入运行模式。");
            if (game != null) throw new InvalidOperationException("检查已经运行，请先 Stop。");
            game = Object.FindObjectOfType<IslandFishingController>();
            if (game == null || game.Boss == null) throw new InvalidOperationException("当前地图没有安装 BOSS。");
            previousInput = game.InputProvider;
            if (!game.InRoom) game.HostRoom(17981, 1);
            game.InputProvider = Sample;
            emergence = attack = ink = false; waypoint = 0; start = game.State?.Steps ?? 0;
            Directory.CreateDirectory(Output);
            EditorApplication.update += Observe;
            return "BOSS 正式地图输入检查已启动";
        }

        private static HookInput Walk(Fisher p, int x, int z)
        {
            int dx = x - p.X, dz = z - p.Z;
            int distance = HookGame.Sqrt((long)dx * dx + (long)dz * dz);
            return distance <= 100 ? default : new HookInput((short)(dx * 1000L / (distance + 1)),
                (short)(dz * 1000L / (distance + 1)), aimZ: BossConfig.CenterZ);
        }

        private static HookInput Sample(ulong tick)
        {
            if (game == null || !game.Playing || !game.TryGetLocalPlayer(out var p) || game.State.RoundFinished) return default;
            var s = game.State;
            if (s.Boss.Phase == BossPhase.Dormant)
                return BossArena.NearBeacon(p) ? new HookInput(0, 0, bossRequest: 1) : Walk(p, 0, 7200);
            if (FirearmRules.Equipped(s, p) == null)
            {
                var gun = s.Tackle.Inventory.Items.First(i => i.Owner == p.Slot && i.Kind == ItemKind.Firearm);
                return new HookInput(0, 0, inventory: new InventoryCommand(InventoryAction.Equip, gun.Id, requestId: 1));
            }
            if (waypoint < Route.Length)
            {
                var target = Route[waypoint];
                if (IslandMap.DistanceSquared(p.X, p.Z, target.x, target.y) < 180L * 180) waypoint++;
                else return Walk(p, target.x, target.y);
            }
            if (!s.Boss.Damageable) return default;
            var eye = BossGeometry.Proxies(s.Boss).Single(v => v.Eye);
            int x = eye.X - p.X, y = eye.Y - BossGeometry.MuzzleHeight(p), z = eye.Z - p.Z;
            int length = HookGame.Sqrt((long)x * x + (long)y * y + (long)z * z);
            return new HookInput(0, 0, eye.X, eye.Z, fire: true,
                reload: s.Tackle.Inventory.Equipped(p.Slot, EquipmentSlot.RightHand).Value.LoadedAmmo == 0,
                aimVertical: (short)Mathf.Clamp((int)(y * 1000L / Math.Max(1, length)), -995, 995));
        }

        private static void Observe()
        {
            if (!Application.isPlaying || game == null) { Stop(); return; }
            if (game.Failure != null) { Debug.LogError("BOSS_SMOKE_FAILURE: " + game.Failure); Stop(); return; }
            var s = game.State; if (s == null) return;
            if (!emergence && s.Boss.Phase == BossPhase.Emerge && s.Boss.PhaseTicks >= 80)
            { Capture("01-emergence", new Vector3(0, 4, 9), new Vector3(0, 4.5f, 28)); emergence = true; }
            if (!attack && s.Boss.Limbs.Any(l => l.Attack != BossAttack.None && l.AttackTicks >= 28))
            {
                Capture("02-first-attack", new Vector3(0, 4, 9), new Vector3(0, 4.5f, 28));
                Capture("03-arena-overview", new Vector3(28, 35, -2), new Vector3(0, 0, 26)); attack = true;
            }
            if (!ink && s.Boss.InkTicks > 0)
            { Capture("04-ink", new Vector3(-7, 8, 9), new Vector3(0, 3, 28)); ink = true; }
            if (s.RoundFinished || s.Steps - start >= 1800 || s.Boss.Phase == BossPhase.Defeated) Stop();
        }

        public static void Capture(string name, Vector3 position, Vector3 target)
        {
            if (game == null) throw new InvalidOperationException("检查未启动。");
            var camera = game.ViewCamera; var oldPosition = camera.transform.position; var oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView; var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var render = new RenderTexture(1440, 900, 24); var image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position = position; camera.transform.LookAt(target); camera.fieldOfView = 58;
                camera.targetTexture = render; camera.Render(); RenderTexture.active = render;
                image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(Output, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation); camera.fieldOfView = oldFov;
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                Object.DestroyImmediate(image); Object.DestroyImmediate(render);
            }
        }

        public static void Stop()
        {
            EditorApplication.update -= Observe;
            if (game != null)
            {
                var s = game.State;
                string report = "BOSS_SMOKE " + (s == null ? "no state" :
                    $"tick={s.Steps} phase={s.Boss.Phase} hp={s.Boss.Health}/{s.Boss.MaxHealth} tiles={s.Tiles.Count} " +
                    $"shots={s.Players[game.LocalSlot].Firearm.ShotSequence} damage_contributors={s.Boss.Attackers} " +
                    $"emergence={emergence} attack={attack} ink={ink} failure={game.Failure ?? "none"}");
                Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "report.txt"), report);
                Debug.Log(report); game.InputProvider = previousInput;
            }
            game = null; previousInput = null;
        }
    }
}
