using Advanture.HookIsland.Simulation;
using System.Collections.Generic;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Read-only, avatar-anchored health, stamina and damage presentation.</summary>
    public sealed class PlayerVitalsOverlay : System.IDisposable
    {
        private readonly HudPainter ui;
        private readonly StaminaArcPainter arc = new StaminaArcPainter();
        private int generation=-1;
        private readonly HealthBarFeedback healthBars = new HealthBarFeedback();
        private readonly ReelingStaminaFeedback reeling = new ReelingStaminaFeedback();
        private readonly Dictionary<ushort, StaminaDrainFeedback> staminaTrails = new Dictionary<ushort, StaminaDrainFeedback>();
        public PlayerVitalsOverlay(Font font) { ui = new HudPainter(font); }
        public void Reset() { generation=-1; healthBars.Reset(); reeling.Reset(); staminaTrails.Clear(); }
        public void Dispose() { arc.Dispose(); ui.Dispose(); }

        public void Draw(IslandFishingController room, float scale, bool visible)
        {
            var state = room.State;
            float now = Time.unscaledTime, width = Screen.width / scale, height = Screen.height / scale;
            visible &= !state.Won;
            if(generation!=room.Feedback.Generation){reeling.Reset();staminaTrails.Clear();generation=room.Feedback.Generation;}
            healthBars.BeginFrame(room.Client, room.Client?.RoundEpoch ?? 0, state.Steps, now, visible);
            ui.BeginFrame();
            foreach (var player in state.Players)
            {
                if (player.WaitingToJoin) { healthBars.Observe(player.Slot, 0, 0, true); staminaTrails.Remove(player.Slot); continue; }
                // Both vitals and the equipped maximum come from the same confirmed inventory snapshot.
                var stats = FishingRules.Attributes(state, player);
                var healthFrame = healthBars.Observe(player.Slot, player.Vitals.Health, stats.HealthMax);
                var mode = reeling.Observe(player, state.Steps, now);
                float fraction = HudPainter.Fraction(player.Stamina, stats.StaminaMax);
                if (!staminaTrails.TryGetValue(player.Slot, out var trail)) staminaTrails[player.Slot] = trail = new StaminaDrainFeedback();
                float trailing = trail.Sample(fraction, now);
                if (!visible || room.ViewCamera == null || room.Presentation == null) continue;
                if (!room.Presentation.TryGetPlayerHudAnchors(player.Slot, out var head, out var chest, out var halfWidth)) continue;
                var camera = room.ViewCamera;
                var point = camera.WorldToScreenPoint(head);
                if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) continue;
                bool local = player.Slot == room.LocalSlot;
                var health = new Rect(point.x / scale - 30, height - point.y / scale - 20, 60, 7);
                DrawHealth(health, player, stats, healthFrame);
                var body = camera.WorldToScreenPoint(chest);
                var left = camera.WorldToScreenPoint(chest - camera.transform.right * halfWidth);
                float arcHeight = Mathf.Clamp(Mathf.Abs(point.y - body.y) / scale * 1.6f, local ? 84 : 72, 96);
                var stamina = new Rect(left.x / scale - arcHeight / 2 - 5, height - body.y / scale - arcHeight / 2, arcHeight / 2, arcHeight);
                if (!player.Vitals.Downed && (local || mode == ReelingFeedbackMode.Reeling || player.Swimming.Active))
                {
                    if (player.Stamina < stats.StaminaMax) arc.Draw(stamina, fraction, trailing, now);
                    if (local) DrawStatus(stamina, state, player, stats);
                }
                DrawCatchCue(health, state, player, mode);
                DrawDamage(room.Feedback.Damage,health,player.Slot,now);
                DrawHealing(room.Feedback.Healing,health,player.Slot,now);
            }
            if (visible && !room.CameraPointerCaptured) ui.Tooltip(width, height);
        }

        private void DrawHealth(Rect rect, Fisher player, CharacterStats stats, HealthBarFeedback.Frame frame)
        {
            ui.Surface(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), new Color(.03f, .07f, .075f, .85f));
            ui.HealthBar(rect, frame);
            ui.Hint(rect, $"生命 · {player.Vitals.Health} / {stats.HealthMax}");
            var badge = new Rect(rect.x - 21, rect.center.y - 9, 17, 18);
            ui.Surface(badge);
            ui.Text(badge, (player.Slot + 1).ToString(), 10, FishingPresentation.PlayerColors[player.Slot], TextAnchor.MiddleCenter, true);
            var status = new Rect(rect.xMax + 5, rect.center.y - 8, 16, 16);
            if (player.Revival.Dead || player.Vitals.Downed)
                ui.Icon(status, player.Revival.Dead ? HudIcon.Skull : HudIcon.Rescue, HudPainter.Health);
            else if (player.PulledBy >= 0 || player.Shopping || player.ProtectionTicks > 0)
                ui.Icon(status, player.PulledBy >= 0 ? HudIcon.Hook : HudIcon.Shield, player.PulledBy >= 0 ? HudPainter.Gold : HudPainter.Mint);
            if (player.Unavailable) ui.Text(new Rect(rect.x, rect.yMax + 4, rect.width, 16), "断线", 10, HudPainter.Gold, TextAnchor.MiddleCenter);
        }

        private void DrawStatus(Rect rect, HookState state, Fisher player, CharacterStats stats)
        {
            var status = new Rect(rect.center.x - 8, rect.yMax + 2, 16, 16);
            bool resting = player.Phase == HookPhase.Ready && !player.Swimming.Active && player.Stamina < stats.StaminaMax;
            if (resting)
            {
                ui.Icon(status, HudIcon.Recover, player.RegenDelay > 0 ? HudPainter.Muted : StaminaArcPainter.Ochre);
                ui.Hint(status, player.RegenDelay > 0 ? $"{player.RegenDelay / 30f:0.0} 秒后恢复" : "耐力恢复中"); status.y += 19;
            }
            if (player.Swimming.InWater)
            {
                bool ring = SwimRingCatalog.Equipped(state.Tackle.Inventory, player.Slot) != null;
                ui.Icon(status, ring ? HudIcon.SwimRing : HudIcon.Alert, ring ? HudPainter.Mint : HudPainter.Health);
                ui.Hint(status, ring ? "游泳圈保护中" : "落水 · 持续失血，无法出竿"); status.y += 19;
            }
            if (player.Vitals.Hunger < stats.HungerMax * .3f)
            { ui.Icon(status, HudIcon.Hunger, StaminaArcPainter.Ochre); ui.Hint(status, "饥饿值偏低"); }
            if (player.Stamina < stats.StaminaMax)
                ui.Hint(rect, $"耐力 · {player.Stamina / (float)StaminaSystem.UnitsPerPoint:0.#} / {stats.StaminaMax / (float)StaminaSystem.UnitsPerPoint:0.#}");
        }

        private void DrawCatchCue(Rect health, HookState state, Fisher player, ReelingFeedbackMode mode)
        {
            var label = new Rect(health.center.x - 52, health.y - 24, 104, 18);
            if (mode == ReelingFeedbackMode.Escaped)
            { ui.Text(label, "挣脱", 12, HudPainter.Coral, TextAnchor.MiddleCenter, true); return; }
            if (mode != ReelingFeedbackMode.Reeling) return;
            int count = CooperativeFishing.Count(state.Players, player);
            if (count > 1)
            { ui.Surface(label); ui.Text(label, (CooperativeFishing.Lead(state, player) == player.Slot ? "主钓" : "辅助") + " · " + count, 10, HudPainter.White, TextAnchor.MiddleCenter); }
        }

        private void DrawHealing(HealingFeedback healing,Rect health,ushort slot,float now)
        {
            foreach(var notice in healing.Notices)
            {
                if(notice.Slot!=slot)continue;
                float t=(float)((now-notice.StartedAt)/HealingFeedback.Lifetime),alpha=1-Mathf.SmoothStep(.6f,1,t);
                var rect=new Rect(health.center.x-42,health.y-39-t*36,84,28);var tint=HudPainter.Health;tint.a=alpha;
                ui.Text(new Rect(rect.x+1,rect.y+2,rect.width,rect.height),"+"+notice.Amount,23,new Color(.08f,.02f,.02f,alpha),TextAnchor.MiddleCenter,true);
                ui.Text(rect,"+"+notice.Amount,23,tint,TextAnchor.MiddleCenter,true);
            }
        }
        private void DrawDamage(HealthDamageFeedback damage,Rect health, ushort slot, float now)
        {
            foreach (var notice in damage.Notices)
            {
                if (notice.Slot != slot) continue;
                float age = (float)(now - notice.StartedAt), t = age / (float)HealthDamageFeedback.Lifetime;
                float alpha = 1 - Mathf.SmoothStep(0, 1, (t - .6f) / .4f);
                // Small burst, then a steady rise; no screen shake or opaque number backplates.
                float size = 21 + 5 * Mathf.Clamp01(1 - age / .13f);
                float offset = notice.Lane == 1 ? -34 : notice.Lane == 2 ? 34 : 0;
                var rect = new Rect(health.center.x - 44 + offset, health.y - 41 - 38 * t, 88, 32);
                string text = "−" + notice.Amount;
                ui.Text(new Rect(rect.x + 1, rect.y + 2, rect.width, rect.height), text, Mathf.RoundToInt(size), new Color(.12f, .035f, .025f, alpha), TextAnchor.MiddleCenter, true);
                var color = HudPainter.Health; color.a = alpha;
                ui.Text(rect, text, Mathf.RoundToInt(size), color, TextAnchor.MiddleCenter, true);
            }
        }
    }
}
