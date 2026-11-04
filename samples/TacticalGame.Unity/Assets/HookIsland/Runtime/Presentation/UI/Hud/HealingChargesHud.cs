using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Read-only charge display above the local health bar.</summary>
    public sealed class HealingChargesHud
    {
        private readonly HudPainter ui;
        public HealingChargesHud(HudPainter painter){ui=painter;}
        public void Draw(Rect health,Fisher player,HealingConfig config)
        {
            string text=$"V  回血 {player.HealingCharges}/{config.Charges}";
            bool available=player.HealingCharges>0 && !player.Vitals.Downed && !player.Revival.Dead;
            ui.FloatingText(new Rect(health.x+4,health.y-27,health.width,23),text,15,available?HudPainter.Mint:HudPainter.Muted);
        }
    }
}
