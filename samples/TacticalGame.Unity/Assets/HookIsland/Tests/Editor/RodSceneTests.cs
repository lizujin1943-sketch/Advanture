using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class RodSceneTests
    {
        [Test] public void PlatformShipsWithEquipmentPanelsMerchantAndSavedFishingConfiguration()
        {
            EditorSceneManager.OpenScene("Assets/HookIsland/Scenes/HookPlatform.unity");
            var game=Object.FindObjectOfType<IslandFishingController>();
            Assert.That(game.GetComponent<FishingEquipmentController>(),Is.Not.Null);
            Assert.That(game.GetComponent<FishingEquipmentOverlay>(),Is.Not.Null);
            Assert.That(GameObject.Find("Platform Merchant"),Is.Not.Null);
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset");
            Assert.That(settings.fishing,Is.Not.Null);
            var map=settings.CreateMap();Assert.That(map.Fishing.StaminaBase,Is.EqualTo(100));
            Assert.That(map.Fishing.RegenDelayTicks,Is.EqualTo(60));Assert.That(map.Fishing.RegenMilliPerSecond,Is.EqualTo(5000));
            var saved=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/PlatformMap.bytes");
            Assert.That(saved.bytes.SequenceEqual(map.Encode()),Is.True,"Packaged map differs from scene configuration.");
            Assert.That(Object.FindObjectsOfType<RodSmokeDriver>().Length,Is.Zero,"Acceptance input must not run in normal gameplay.");
        }
        [Test] public void FishingSettingsChangesArePartOfMapCompatibility()
        {
            var settings=ScriptableObject.CreateInstance<FishingSettings>();
            try
            {
                var initial=settings.CreateConfig().Encode();settings.recoveryDelaySeconds=3;
                Assert.That(settings.CreateConfig().Encode().SequenceEqual(initial),Is.False);
                settings.merchantLevel=5;Assert.That(settings.CreateConfig().MerchantLevel,Is.EqualTo(5));
            }
            finally{Object.DestroyImmediate(settings);}
        }
    }
}
