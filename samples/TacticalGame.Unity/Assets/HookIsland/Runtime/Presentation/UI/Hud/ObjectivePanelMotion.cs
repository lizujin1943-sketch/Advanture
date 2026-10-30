using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local-only drawer animation shared by rendering and pointer hit testing.</summary>
    public sealed class ObjectivePanelMotion
    {
        private float from = 1, changedAt;
        public bool Collapsed { get; private set; }
        public float Reveal(float now) => Mathf.Lerp(from, Collapsed ? 0 : 1, Mathf.SmoothStep(0, 1, (now - changedAt) / .22f));
        public void Toggle(float now) { from = Reveal(now); changedAt = now; Collapsed = !Collapsed; }
    }
}
