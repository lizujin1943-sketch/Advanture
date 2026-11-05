using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Eldritch Enemies")]
    public sealed class EldritchSettings : ScriptableObject
    {
        public bool enableEncounters=true;
        [Min(.034f)] public float initialDelaySeconds=30;
        [Min(0)] public float speciesStaggerSeconds=10;
        [Min(1)] public float respawnSeconds=90;
        [Range(1,10)] public int maxAlive=4;
        [Tooltip("Bits 0..9 select the ten species in EldritchCatalog order.")]
        [Range(1,1023)] public int speciesMask=1023;
        public EldritchConfig CreateConfig()=>new EldritchConfig(enableEncounters,Mathf.RoundToInt(initialDelaySeconds*HookGame.Rate),
            Mathf.RoundToInt(speciesStaggerSeconds*HookGame.Rate),Mathf.RoundToInt(respawnSeconds*HookGame.Rate),maxAlive,speciesMask);
    }
}
