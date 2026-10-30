using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Treasure Settings")]
    public sealed class TreasureSettings : ScriptableObject
    {
        [Range(0,16)] public int smallCount=1,rareCount=1,largeCount=1;
        [Min(0)] public float initialDelaySeconds=4,slotDelaySeconds=6;
        [Min(1)] public float respawnSeconds=30,floatLifetimeSeconds=45;
        [Min(0)] public float driftMetresPerSecond=.2f,rareDriftMetresPerSecond=.5f;
        [Min(.03f)] public float smallReelMetresPerSecond=1.8f,largeReelMetresPerSecond=.9f;
        [Min(.3f)] public float openRangeMetres=1.6f;
        [Min(0)] public int smallCoins=18,smallWood=4,smallBones=2,rareCoins=45,rareWood=6,rareBones=6,largeCoins=61,largeWood=13,largeBones=9;
        public TreasureConfig CreateConfig()=>new TreasureConfig(smallCount,rareCount,largeCount,
            Mathf.RoundToInt(initialDelaySeconds*HookGame.Rate),Mathf.RoundToInt(slotDelaySeconds*HookGame.Rate),
            Mathf.RoundToInt(respawnSeconds*HookGame.Rate),Mathf.RoundToInt(driftMetresPerSecond*1000),Mathf.RoundToInt(rareDriftMetresPerSecond*1000),
            Mathf.RoundToInt(floatLifetimeSeconds*HookGame.Rate),Mathf.RoundToInt(smallReelMetresPerSecond*1000),Mathf.RoundToInt(largeReelMetresPerSecond*1000),
            Mathf.RoundToInt(openRangeMetres*1000),smallCoins,smallWood,smallBones,rareCoins,rareWood,rareBones,largeCoins,largeWood,largeBones);
    }
}
