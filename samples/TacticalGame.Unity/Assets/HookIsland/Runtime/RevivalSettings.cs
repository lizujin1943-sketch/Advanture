using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Revival Settings")]
    public sealed class RevivalSettings : ScriptableObject
    {
        [Min(.034f)] public float downedSeconds=60;
        [Min(.034f)] public float rescueSeconds=3;
        [Range(.4f,5)] public float rescueRangeMetres=1.5f;
        [Range(.001f,1)] public float rescueHealthFraction=.3f;
        [Min(0)] public int baseFee=10;
        [Min(0)] public int growthFee=5;
        [Min(.034f)] public float growthIntervalSeconds=300;
        [Range(0,1)] public float balanceFeeFraction=.1f;
        [Range(.001f,1)] public float respawnHealthFraction=1;
        [Range(0,1)] public float respawnStaminaFraction=1;
        public RevivalConfig CreateConfig()=>new RevivalConfig(Mathf.RoundToInt(downedSeconds*30),Mathf.RoundToInt(rescueSeconds*30),
            Mathf.RoundToInt(rescueRangeMetres*1000),Mathf.RoundToInt(rescueHealthFraction*1000),baseFee,growthFee,
            Mathf.RoundToInt(growthIntervalSeconds*30),Mathf.RoundToInt(balanceFeeFraction*1000),
            Mathf.RoundToInt(respawnHealthFraction*1000),Mathf.RoundToInt(respawnStaminaFraction*1000));
    }
}
