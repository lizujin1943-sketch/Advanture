using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Swimming Settings")]
    public sealed class SwimmingSettings : ScriptableObject
    {
        [Range(.001f,1000)] public float waterHealthLossPerSecond=5;
        [Range(.001f,10)] public float swimMetresPerSecond=1;
        [Range(.001f,1000)] public float swimStaminaPerSecond=10;
        public SwimmingConfig CreateConfig()=>new SwimmingConfig(Mathf.RoundToInt(waterHealthLossPerSecond*1000),
            Mathf.RoundToInt(swimMetresPerSecond*1000),Mathf.RoundToInt(swimStaminaPerSecond*1000));
    }
}
