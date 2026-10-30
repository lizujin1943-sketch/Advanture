using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Combo Settings")]
    public sealed class ComboSettings : ScriptableObject
    {
        public bool comboEnabled=true;
        [Min(1)] public int displayStartCount=3;
        public int[] goldBonusPercentByCount={0,0,10,15,20,25,30,35,40,50};
        public ComboConfig CreateConfig()=>new ComboConfig(comboEnabled,displayStartCount,goldBonusPercentByCount);
    }
}
