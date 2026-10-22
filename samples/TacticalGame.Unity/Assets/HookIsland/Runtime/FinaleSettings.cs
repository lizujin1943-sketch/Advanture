using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Finale Settings")]
    public sealed class FinaleSettings:ScriptableObject
    {
        public bool useFinale=true;
        [Min(1)] public int baitCost=3;
        [Min(.034f)] public float summonSeconds=10,submergedSeconds=6,holdSeconds=5;
        [Min(.001f)] public float sharedStaminaPerSecond=12;
        public int[] requiredHooks={1,2,2,3};
        [Range(.4f,2)] public float bodyRadiusMetres=1.2f;
        [Range(1.5f,4)] public float edgeOffsetMetres=3.1f;
        [Min(1)] public float preferredShiftMetres=4;
        public FinaleConfig CreateConfig()=>useFinale?new FinaleConfig(baitCost,Mathf.RoundToInt(summonSeconds*HookGame.Rate),
            Mathf.RoundToInt(submergedSeconds*HookGame.Rate),Mathf.RoundToInt(holdSeconds*HookGame.Rate),Mathf.RoundToInt(sharedStaminaPerSecond*1000),
            requiredHooks,Mathf.RoundToInt(bodyRadiusMetres*1000),Mathf.RoundToInt(edgeOffsetMetres*1000),Mathf.RoundToInt(preferredShiftMetres*1000)):FinaleConfig.Disabled;
    }
}
