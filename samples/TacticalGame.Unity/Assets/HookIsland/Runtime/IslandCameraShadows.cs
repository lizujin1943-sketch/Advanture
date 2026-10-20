using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>The orthographic camera sits far away; scope its shadow range to its own render.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class IslandCameraShadows : MonoBehaviour
    {
        [Min(1)] public float requiredDistance = 130f;
        private float previousDistance;
        private bool overridden;

        private void OnPreCull()
        {
            if (overridden) return;
            previousDistance = QualitySettings.shadowDistance;
            QualitySettings.shadowDistance = Mathf.Max(previousDistance, requiredDistance);
            overridden = true;
        }

        private void OnPostRender() => Restore();
        private void OnDisable() => Restore();

        private void Restore()
        {
            if (!overridden) return;
            QualitySettings.shadowDistance = previousDistance;
            overridden = false;
        }
    }
}
