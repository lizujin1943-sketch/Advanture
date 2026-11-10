using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Eldritch Enemies")]
    public sealed class EldritchSettings : ScriptableObject
    {
        public bool enableEncounters=true;
        [Tooltip("仅非城镇地图使用；沉钟镇由 TownDirectorRules 控制首次投放。")]
        [Min(.034f)] public float initialDelaySeconds=30;
        [Tooltip("仅非城镇地图使用；沉钟镇使用预算和分批投放。")]
        [Min(0)] public float speciesStaggerSeconds=10;
        [Tooltip("死亡奖励结算后回收的等待时间；沉钟镇在区域活跃时最多等待8秒，重新投放仍要消耗导演预算。")]
        [Min(1)] public float respawnSeconds=90;
        [Tooltip("同时战斗的成体硬上限；母巢子代另有全局4只上限。")]
        [Range(1,10)] public int maxAlive=4;
        [Tooltip("仅非城镇地图按位选择物种；沉钟镇使用 TownEncounters 中的城区怪物池。")]
        [Range(1,1023)] public int speciesMask=1023;
        public EldritchConfig CreateConfig()=>new EldritchConfig(enableEncounters,Mathf.RoundToInt(initialDelaySeconds*HookGame.Rate),
            Mathf.RoundToInt(speciesStaggerSeconds*HookGame.Rate),Mathf.RoundToInt(respawnSeconds*HookGame.Rate),maxAlive,speciesMask);
    }
}
