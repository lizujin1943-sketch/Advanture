using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Crab Settings")]
    public sealed class CrabSettings : ScriptableObject
    {
        public bool enabledInGame=true;
        [Min(1)] public float firstSpawnSeconds=90;
        [Min(1)] public float respawnSeconds=60;
        [Min(1)] public int health=100;
        [Min(.1f)] public float speed=2;
        [Min(.04f)] public float windupSeconds=.6f;
        [Min(.04f)] public float recoverySeconds=1;
        [Min(1)] public int damage=12;
        [Min(.001f)] public float knockbackMetres=.5f;
        [Min(.04f)] public float climbSeconds=1.5f;
        [Min(.04f)] public float noTargetSeconds=3;
        [Min(.04f)] public float interruptProtectionSeconds=3;
        public CrabConfig CreateConfig()=>new CrabConfig(enabledInGame,Ticks(firstSpawnSeconds),Ticks(respawnSeconds),health,
            Mathf.RoundToInt(speed*1000),Ticks(windupSeconds),Ticks(recoverySeconds),damage,Mathf.RoundToInt(knockbackMetres*1000),
            Ticks(climbSeconds),Ticks(noTargetSeconds),Ticks(interruptProtectionSeconds));
        private static int Ticks(float seconds)=>Mathf.RoundToInt(seconds*HookGame.Rate);
    }
}
