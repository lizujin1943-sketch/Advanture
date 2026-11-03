using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>游戏专用 BOSS 配置；数值编入地图，模型和片段只供 Unity 显示。</summary>
    [CreateAssetMenu(menuName = "Hook Island/Abyssal Watcher Settings")]
    public sealed class BossSettings : ScriptableObject
    {
        public bool enableChallenge = true;
        [Min(1)] public int soloBodyHealth = 2400;
        [Min(1)] public int soloAttackLimbHealth = 300;
        [Min(1)] public int soloSupportLimbHealth = 220;
        [Min(0)] public int coinsPerContributor = 600;
        public GameObject modelPrefab;
        public AnimationClip[] clips = new AnimationClip[0];

        public BossConfig CreateConfig() => new BossConfig(enableChallenge, soloBodyHealth,
            soloAttackLimbHealth, soloSupportLimbHealth, coinsPerContributor);
    }
}
