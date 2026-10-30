using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Enemy Settings")]
    public sealed class EnemySettings:ScriptableObject
    {
        [Header("食人鱼")]
        [Range(0,16)] public int piranhaCount=3;
        [Range(.1f,20)] public float minWeightKg=1,maxWeightKg=4;
        [Range(1,1000)] public int damagePerKg=3;
        [Range(.5f,60)] public float biteIntervalSeconds=1;
        [Range(.03f,10)] public float swimMetresPerSecond=1.92f;
        [Range(1,40)] public float aggroMetres=20;
        [Range(.1f,2)] public float biteMetres=.8f;
        [Range(.03f,10)] public float patrolMetresPerSecond=.96f;
        [Range(1,600)] public float piranhaRespawnSeconds=10;
        [Range(1,1000)] public int piranhaCoinsPerKg=6;
        [Range(.1f,10)] public float piranhaStaminaPerKgSecond=1.2f;
        [Header("水怪与水球")]
        [Range(1,10)] public int monsterLevel=1;
        [Range(1,10)] public float baseKnockbackMetres=4;
        [Range(.001f,4)] public float knockbackMetresPerLevel=2;
        public bool monsterEnabled=true;
        [Range(21,1000)] public float monsterWeightKg=50;
        public Vector2 monsterPositionMetres=new Vector2(0,-6);
        [Range(1,100000)] public int waterballDamage=20;
        [Range(.034f,10)] public float windupSeconds=2;
        [Tooltip("发射后等待此时长，再开始下一次前摇。")]
        [Range(.5f,60)] public float attackIntervalSeconds=6;
        [Range(0,600)] public float initialDelaySeconds=8;
        [Range(.034f,60)] public float interruptDelaySeconds=3;
        [Range(1,30)] public float waterballMetresPerSecond=8;
        [Range(.05f,1)] public float waterballRadiusMetres=.3f;
        [Range(1,40)] public float waterballRangeMetres=24;
        public EnemyConfig CreateConfig()=>new EnemyConfig(piranhaCount,monsterEnabled,damagePerKg,Ticks(biteIntervalSeconds),
            Units(swimMetresPerSecond),Units(aggroMetres),Units(biteMetres),Units(minWeightKg),Units(maxWeightKg),Units(monsterWeightKg),
            waterballDamage,Ticks(windupSeconds),Ticks(attackIntervalSeconds),Ticks(initialDelaySeconds),Ticks(interruptDelaySeconds),
            Units(waterballMetresPerSecond),Units(waterballRadiusMetres),Units(waterballRangeMetres),Units(monsterPositionMetres.x),Units(monsterPositionMetres.y),
            monsterLevel,Units(baseKnockbackMetres),Units(knockbackMetresPerLevel),Ticks(piranhaRespawnSeconds),piranhaCoinsPerKg,
            Units(piranhaStaminaPerKgSecond),Units(patrolMetresPerSecond));
        private static int Units(float value)=>Mathf.RoundToInt(value*1000);
        private static int Ticks(float value)=>Mathf.RoundToInt(value*HookGame.Rate);
    }
}
