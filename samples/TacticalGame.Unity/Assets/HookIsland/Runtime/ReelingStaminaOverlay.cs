using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>World-anchored, read-only stamina bars for each player's attached catch.</summary>
    public sealed class ReelingStaminaOverlay : System.IDisposable
    {
        private readonly HudPainter ui;
        private readonly ReelingStaminaFeedback feedback = new ReelingStaminaFeedback();
        public ReelingStaminaOverlay(Font font) { ui = new HudPainter(font); }
        public void Reset() { feedback.Reset(); }
        public void Dispose() { ui.Dispose(); }

        // The caller supplies the same scaled GUI coordinate system as the other HUD panels.
        public void Draw(IslandFishingController room, float scale, bool visible)
        {
            var state = room.State;
            float width = Screen.width / scale, height = Screen.height / scale;
            foreach (var player in state.Players)
            {
                if(player.WaitingToJoin)continue;
                var mode = feedback.Observe(player, state.Steps, Time.unscaledTime);
                // Observe even while a menu covers the scene; reopening it must not replay old escapes.
                if (!visible || mode == ReelingFeedbackMode.Hidden || room.ViewCamera == null || room.Presentation == null) continue;
                var position = room.Presentation.PlayerPosition(player.Slot) + Vector3.up * (room.Map.Platform == null ? 2.2f : 1.2f);
                var point = room.ViewCamera.WorldToScreenPoint(position);
                if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) continue;
                var rect = new Rect(Mathf.Clamp(point.x / scale - 70, 8, width - 148),
                    Mathf.Clamp(height - point.y / scale - 62, 8, height - 46), 140, 34);
                DrawBar(rect, player, FishingRules.MaxStamina(state, player), mode, player.Slot == room.LocalSlot);
                if(mode==ReelingFeedbackMode.Reeling)
                {
                    int count=CooperativeFishing.Count(state.Players,player);
                    if(count>1)
                    {
                        var label=new Rect(rect.x,rect.y-20,rect.width,20);ui.Surface(label);
                        ui.Text(label,(CooperativeFishing.Lead(state,player)==player.Slot?"主钓":"辅助")+" · "+count+" 人",11,HudPainter.White);
                    }
                }
            }
        }

        private void DrawBar(Rect rect, Fisher player, int maximum, ReelingFeedbackMode mode, bool local)
        {
            bool escaped = mode == ReelingFeedbackMode.Escaped;
            float remaining = escaped ? 0 : HudPainter.Fraction(player.Stamina,maximum);
            ui.Surface(rect);
            ui.Icon(new Rect(rect.x+8,rect.y+7,20,20),escaped?HudIcon.Fish:HudIcon.Stamina,escaped?HudPainter.Coral:HudPainter.StaminaColor(remaining));
            if(escaped)ui.Text(new Rect(rect.x+36,rect.y+3,95,28),"挣脱",13,HudPainter.Coral);
            else ui.Bar(new Rect(rect.x+36,rect.y+12,94,10),remaining,HudPainter.StaminaColor(remaining),true);
            ui.Surface(new Rect(rect.xMax-6,rect.y+3,3,3),local?HudPainter.White:FishingPresentation.PlayerColors[player.Slot]);
        }
    }
}
