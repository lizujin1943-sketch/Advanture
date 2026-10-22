using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [Serializable] public sealed class OceanFishProfile
    {
        public FishRarity minimumRarity,maximumRarity;
        [Range(1000,10000)] public int minimumWeightGrams=1000,maximumWeightGrams=2000;
        [Range(1,3)] public int maximumGrade=1;
        public FishSchoolProfile CreateProfile()=>new FishSchoolProfile(minimumRarity,maximumRarity,minimumWeightGrams,maximumWeightGrams,maximumGrade);
    }
    [CreateAssetMenu(menuName="Hook Island/Ocean Settings")]
    public sealed class OceanSettings:ScriptableObject
    {
        public bool useProgression=true;
        public bool pauseNewMonsterAttacks=true;
        public int[] schoolUnlocks=Enumerable.Range(0,24).Select(i=>i%3).ToArray();
        [Range(1,4)] public int visitSlots=2;
        [Min(.034f)] public float noCatchSeconds=90,pressureWindowSeconds=30,recoverySeconds=20,recoveryIntervalSeconds=60,visitSeconds=30;
        [Range(1,100)] public int handledAttacksForRecovery=3;
        public OceanFishProfile opening=new OceanFishProfile {maximumWeightGrams=3000};
        public OceanFishProfile middle=new OceanFishProfile {maximumRarity=FishRarity.Rare,minimumWeightGrams=2000,maximumWeightGrams=6000,maximumGrade=3};
        public OceanFishProfile far=new OceanFishProfile {minimumRarity=FishRarity.Rare,maximumRarity=FishRarity.Legendary,minimumWeightGrams=2000,maximumWeightGrams=6000,maximumGrade=3};
        public OceanFishProfile recoveryVisit=new OceanFishProfile();
        public OceanFishProfile middleVisit=new OceanFishProfile {minimumRarity=FishRarity.Uncommon,maximumRarity=FishRarity.Uncommon};
        public OceanFishProfile rareVisit=new OceanFishProfile {minimumRarity=FishRarity.Rare,maximumRarity=FishRarity.Rare};
        public OceanConfig CreateConfig()=>useProgression?new OceanConfig(schoolUnlocks,visitSlots,Ticks(noCatchSeconds),Ticks(pressureWindowSeconds),handledAttacksForRecovery,
            Ticks(recoverySeconds),Ticks(recoveryIntervalSeconds),Ticks(visitSeconds),opening.CreateProfile(),middle.CreateProfile(),far.CreateProfile(),
            recoveryVisit.CreateProfile(),middleVisit.CreateProfile(),rareVisit.CreateProfile(),pauseNewMonsterAttacks):OceanConfig.Disabled;
        private static int Ticks(float seconds)=>Mathf.RoundToInt(seconds*HookGame.Rate);
    }
}
