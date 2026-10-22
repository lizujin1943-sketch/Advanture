using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class CommissionExportTests
    {
        [Test] public void FormalMapAndSettingsUseTheSameSharedObjectives()
        {
            var settings=AssetDatabase.LoadAssetAtPath<FishingSettings>("Assets/HookIsland/Data/FishingSettings.asset");
            Assert.That(settings.commissions,Is.Not.Null);
            var config=settings.CreateConfig();
            var asset=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/PlatformMap.bytes");
            var map=IslandMap.Decode(asset.bytes);
            Assert.That(map.Fishing.Commissions.Enabled,Is.True);
            Assert.That(map.Fishing.Encode(),Is.EqualTo(config.Encode()));
            Assert.That(map.Fishing.Commissions.Definitions.Count,Is.EqualTo(3));
        }
    }
}
