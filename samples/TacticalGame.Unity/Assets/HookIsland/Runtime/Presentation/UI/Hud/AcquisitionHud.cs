using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Shared acquisition stream: personal rolling feed and visible players' overhead notices.</summary>
    public sealed class AcquisitionHud
    {
        private readonly HudPainter ui;
        private readonly ItemIconPainter icons = new ItemIconPainter();
        private readonly AcquisitionFeedback feedback;
        private readonly Dictionary<long, float> rowPositions = new Dictionary<long, float>();
        private readonly List<long> staleRows = new List<long>();
        private float lastTime;
        public AcquisitionHud(HudPainter painter,AcquisitionFeedback feedback) { ui=painter;this.feedback=feedback; }

        public void Draw(IslandFishingController room, HudLayout layout, float scale, bool worldVisible)
        {
            float now = Time.unscaledTime, dt = Mathf.Max(0, now - lastTime); lastTime = now;
            DrawFeed(room.LocalSlot, layout.Acquisitions, now, dt);
            if (worldVisible && room.Presentation != null && room.ViewCamera != null)
                foreach (var player in room.State.Players)
                {
                    if (player.WaitingToJoin || !room.Presentation.TryGetPlayerHudAnchors(player.Slot, out var head, out _, out _)) continue;
                    var point = room.ViewCamera.WorldToScreenPoint(head);
                    if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) continue;
                    int row = 0;
                    for (int i = feedback.Notices.Count - 1; i >= 0 && row < 3; i--)
                    {
                        var notice = feedback.Notices[i]; float age = now - (float)notice.StartedAt;
                        if (notice.Slot != player.Slot || age >= AcquisitionFeedback.WorldLifetime) continue;
                        float alpha = Fade(age, (float)AcquisitionFeedback.WorldLifetime);
                        var rect = new Rect(Mathf.Clamp(point.x / scale + 42, 8, layout.Width - 104),
                            layout.Height - point.y / scale - 55 - row++ * 28 - 18 * age, 96, 26);
                        DrawNotice(rect, notice, alpha);
                    }
                }
        }
        private void DrawFeed(ushort local, Rect area, float now, float dt)
        {
            staleRows.Clear(); foreach (var id in rowPositions.Keys) staleRows.Add(id);
            int row = 0;
            for (int i = feedback.Notices.Count - 1; i >= 0 && row < 5; i--)
            {
                var notice = feedback.Notices[i]; if (notice.Slot != local) continue;
                float age = now - (float)notice.StartedAt, target = area.yMax - 36 - row++ * 38;
                if (!rowPositions.TryGetValue(notice.Id, out float y)) y = target + 15;
                y = Mathf.Lerp(y, target, 1 - Mathf.Exp(-dt * 16)); rowPositions[notice.Id] = y; staleRows.Remove(notice.Id);
                float alpha = Fade(age, (float)AcquisitionFeedback.Lifetime);
                var rect = new Rect(area.x + 18 * Mathf.Exp(-age * 14), y, area.width, 36);
                DrawNotice(rect, notice, alpha);
            }
            foreach (long id in staleRows) rowPositions.Remove(id);
        }
        private void DrawNotice(Rect rect, AcquisitionFeedback.Notice notice, float alpha)
        {
            var tint = notice.Kind.HasValue ? HudPainter.White : HudPainter.Gold; tint.a = alpha;
            var icon = new Rect(rect.x, rect.center.y - 12, 24, 24);
            if (notice.Kind.HasValue) icons.Draw(icon, notice.Kind.Value, notice.Type, tint: new Color(1, 1, 1, alpha));
            else ui.Icon(icon, HudIcon.Coin, tint);
            var number = new Rect(rect.x + 30, rect.y, rect.width - 30, rect.height);
            string text = "+" + notice.Amount.ToString("N0");
            ui.Text(new Rect(number.x + 1, number.y + 1, number.width, number.height), text, 17, new Color(.025f,.06f,.065f,alpha), bold:true);
            ui.Text(number, text, 17, tint, bold:true);
        }
        private static float Fade(float age, float lifetime) => 1 - Mathf.SmoothStep(0, 1, (age - lifetime + .55f) / .55f);
    }
}
