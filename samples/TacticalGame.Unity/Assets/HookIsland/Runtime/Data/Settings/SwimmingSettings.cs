using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Swimming Settings")]
    public sealed class SwimmingSettings : ScriptableObject
    {
        [Tooltip("每隔一秒按最大生命值扣除的百分比；1 表示 1%。")]
        [Range(.01f,100)] public float waterHealthLossPercentPerSecond=1;
        [Range(.001f,10)] public float swimMetresPerSecond=1;
        [Range(.001f,1000)] public float swimStaminaPerSecond=10;
        public SwimmingConfig CreateConfig()=>new SwimmingConfig(Mathf.RoundToInt(waterHealthLossPercentPerSecond*100),
            Mathf.RoundToInt(swimMetresPerSecond*1000),Mathf.RoundToInt(swimStaminaPerSecond*1000));
    }
}
