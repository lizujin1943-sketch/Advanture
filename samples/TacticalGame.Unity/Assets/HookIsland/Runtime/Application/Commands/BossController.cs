using System;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>可选挑战的交互入口。只发送请求、读取确认态，不在 Unity 中扣血或发奖励。</summary>
    public sealed class BossController : IDisposable
    {
        private readonly IslandFishingController room;
        private readonly BossRequestBuffer requests = new BossRequestBuffer();
        private readonly BossPresentation view;
        private string message;
        private float messageUntil;
        public bool Pending => requests.Pending;

        public BossController(IslandFishingController room, Shader shader, BossSettings settings)
        {
            this.room = room;
            if (settings == null || settings.modelPrefab == null)
                throw new InvalidOperationException("缺少深渊凝视者资源，请执行 Install Abyssal Watcher Challenge。");
            view = new BossPresentation(room.transform, shader, settings);
        }

        public bool NearBeacon => room.TryGetLocalPlayer(out var p) && (room.Map.IsTown?IslandMap.DistanceSquared(p.X,p.Z,TownLayout.BeaconX,TownLayout.BeaconZ)<=2500L*2500:BossArena.NearBeacon(p));

        /// <summary>附近的 E 优先用于挑战入口；不让同一次按键再打开商店或宝箱。</summary>
        public bool TryAwakenNearest()
        {
            if (!NearBeacon || room.State == null) return false;
            if(room.Map.IsTown)return room.Town?.TryInteract()==true;
            // 开战和击败后入口不再消费 E，附近正常落岸的宝箱、商店仍能交互。
            if (room.State.Boss.Phase != BossPhase.Dormant) return false;
            if (!room.Playing || room.Incapacitated || room.State.RoundFinished || Pending ||
                !room.TryGetLocalPlayer(out var p) || p.Unavailable || p.Swimming.InWater ||
                room.Equipment?.InventoryOpen == true || room.Construction?.Open == true ||
                room.Equipment?.Pending == true || room.Construction?.Pending == true || room.PurchasePending ||
                room.Treasures?.Pending == true || room.Revival?.Pending == true) return true;
            var receipts = room.State.Boss.Receipts;
            requests.TryQueue(p.Slot < receipts.Count ? receipts[p.Slot].Request : 0);
            return true;
        }

        public HookInput Compose(HookInput input) => requests.Compose(input);

        public void Synchronize(HookState state)
        {
            // 血量、断肢、奖励提示都只读取确认态。预测回滚不能提前显示胜利或复活触手。
            view.Synchronize(state);
            if (room.LocalSlot < state.Boss.Receipts.Count && requests.Observe(state.Boss.Receipts[room.LocalSlot], out var result))
            {
                message = result == BossRequestResult.Success ? "深渊凝视者已被唤醒" :
                    result == BossRequestResult.TooFar ? "请站在南侧唤醒标记附近再试" :
                    result == BossRequestResult.Defeated ? "本局挑战已完成" : "当前无法唤醒，请收钩并站稳后重试";
                messageUntil = Time.unscaledTime + 4;
            }
        }

        public void Draw(float width, float height, GUIStyle body, GUIStyle small)
        {
            if (room.State == null || !room.TryGetLocalPlayer(out var p)) return;
            var s = room.State.Boss;
            if (s.Active && IslandMap.DistanceSquared(p.X, p.Z, s.CenterX, s.CenterZ) <= 25000L * 25000)
            {
                var rect = new Rect(width / 2 - 220, 20, 440, 76);
                Fill(rect, new Color(.015f, .055f, .065f, .9f));
                GUI.Label(new Rect(rect.x + 12, rect.y + 4, 420, 26),
                    "深渊凝视者" + (s.Enraged ? " · 狂暴" : "") + $"    {s.Health} / {s.MaxHealth}", body);
                Fill(new Rect(rect.x + 12, rect.y + 35, 416, 12), new Color(.13f, .18f, .19f));
                Fill(new Rect(rect.x + 12, rect.y + 35, 416 * (s.MaxHealth > 0 ? (float)s.Health / s.MaxHealth : 0), 12),
                    s.Enraged ? new Color(.89f, .34f, .19f) : new Color(.19f, .83f, .66f));
                int alive = 0; foreach (var limb in s.Limbs) if (limb.Id < 4 && limb.Health > 0) alive++;
                string hint = s.Phase == BossPhase.Emerge ? "登场中 · 内圈攻击，外圈躲避" :
                    s.Phase == BossPhase.Dying ? "深渊正在沉寂…" : $"攻击触手 {alive}/4 · 主眼伤害 ×1.5 · 红色预警区域会毁板";
                GUI.Label(new Rect(rect.x + 12, rect.y + 50, 416, 22), hint, small);
            }
            if(room.Map.IsTown)return;
            string prompt = null;
            if (Time.unscaledTime < messageUntil) prompt = message;
            else if (Pending) prompt = "正在唤醒…";
            else if (s.Phase == BossPhase.Defeated && NearBeacon) prompt = "深渊凝视者已击败 · 可以继续捕鱼";
            // 尚未靠近入口时不常驻显示挑战长提示；接近后的交互提示和开战反馈继续保留。
            else if (s.Phase == BossPhase.Dormant && NearBeacon) prompt = "[E] 唤醒深渊凝视者 · 可选挑战";
            if (prompt != null)
            {
                var r = new Rect(width / 2 - 235, height - 202, 470, 32);
                Fill(r, new Color(.015f, .055f, .065f, .86f));
                GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 20, 28), prompt, small);
            }
        }

        private static void Fill(Rect rect, Color color)
        { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }
        public void Clear() { requests.Reset(); message = null; messageUntil = 0; view.Clear(); }
        public void Dispose() { requests.Reset(); view.Dispose(); }
    }
}
