using System.Reflection;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class TreasureSceneTests
    {
        [Test] public void UnconfiguredFishingSettingsKeepTreasureDisabled()
        {
            var settings=ScriptableObject.CreateInstance<FishingSettings>();
            try
            {
                Assert.That(settings.treasure,Is.Null);
                Assert.That(settings.CreateConfig().Treasure.Enabled,Is.False);
            }
            finally{Object.DestroyImmediate(settings);}
        }
        [Test] public void PlatformSettingsExportConfiguredTreasureRules()
        {
            var settings=AssetDatabase.LoadAssetAtPath<FishingSettings>("Assets/HookIsland/Data/FishingSettings.asset");
            Assert.That(settings.treasure,Is.Not.Null);
            var config=settings.CreateConfig().Treasure;
            Assert.That(config.Count(1),Is.EqualTo(2));Assert.That(config.Count(4),Is.EqualTo(3));
            Assert.That(config.LargeCoins,Is.EqualTo(61));Assert.That(config.LargeReelSpeed,Is.EqualTo(900));
            var asset=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/PlatformMap.bytes");
            var map=IslandMap.Decode(asset.bytes);Assert.That(map.Fishing.Treasure.OpenRange,Is.EqualTo(config.OpenRange));
        }
        [Test] public void OrdinaryInputAdapterPreservesOpeningRequest()
        {
            var go=new GameObject("treasure-input-contract");go.SetActive(false);
            try
            {
                var room=go.AddComponent<IslandFishingController>();
                room.InputProvider=_=>new HookInput(0,0,treasure:new TreasureCommand(3,2,7));
                var sample=typeof(IslandFishingController).GetMethod("Sample",BindingFlags.Instance|BindingFlags.NonPublic);
                var input=(HookInput)sample.Invoke(room,new object[]{1UL});
                var decoded=HookInput.Decode(input.Encode());Assert.That(decoded.Treasure.ChestId,Is.EqualTo(3));
                Assert.That(decoded.Treasure.Generation,Is.EqualTo(2));Assert.That(decoded.Treasure.RequestId,Is.EqualTo(7));
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
