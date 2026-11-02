using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Fishing Settings")]
    public sealed class FishingSettings : ScriptableObject
    {
        public CharacterSettings character;
        public SwimmingSettings swimming;
        public EnemySettings enemies;
        public RevivalSettings revival;
        public TreasureSettings treasure;
        public CommissionSettings commissions;
        public OceanSettings ocean;
        public FinaleSettings finale;
        public MerchantSettings merchants;
        public ComboSettings combo;
        public bool enableMelee=true;
        public bool startWithFryingPan=true;
        [Min(1)] public int piranhaHealth=50;
        [Min(.0334f)] public float meleeStunSeconds=.5f;
        [Min(1)] public int characterBaseStamina=100;
        [Min(0)] public float recoveryDelaySeconds=2;
        [Min(.001f)] public float recoveryPointsPerSecond=5;
        [Min(.3f)] public float fishReelMetresPerSecond=2;
        [Range(0,3)] public float additionalAnglerSpeedBonus=.5f;
        [Range(1,5)] public int merchantLevel=1;
        [Min(1)] public int restockSeconds=600;
        [Min(.0334f)] public float foodCooldownSeconds=8;
        [Min(0)] public int worldSeed=7381;
        public FishingConfig CreateConfig()=>new FishingConfig(characterBaseStamina,
            Mathf.RoundToInt(recoveryDelaySeconds*HookGame.Rate),Mathf.RoundToInt(recoveryPointsPerSecond*1000),
            Mathf.RoundToInt(fishReelMetresPerSecond*1000),merchantLevel,restockSeconds,worldSeed,character!=null?character.CreateConfig():new CharacterConfig(),swimming!=null?swimming.CreateConfig():new SwimmingConfig(),enemies!=null?enemies.CreateConfig():new EnemyConfig(),revival!=null?revival.CreateConfig():new RevivalConfig(),Mathf.RoundToInt(additionalAnglerSpeedBonus*1000),treasure!=null?treasure.CreateConfig():TreasureConfig.Disabled,commissions!=null?commissions.CreateConfig():CommissionConfig.Disabled,ocean!=null?ocean.CreateConfig():OceanConfig.Disabled,finale!=null?finale.CreateConfig():FinaleConfig.Disabled,merchants!=null?merchants.CreateConfig():MerchantConfig.Empty,combo!=null?combo.CreateConfig():new ComboConfig(),new MeleeConfig(enableMelee,enableMelee && startWithFryingPan,piranhaHealth,Mathf.RoundToInt(meleeStunSeconds*HookGame.Rate)),Mathf.RoundToInt(foodCooldownSeconds*HookGame.Rate));
    }
}
