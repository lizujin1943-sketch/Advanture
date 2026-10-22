using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class FinaleSceneTests
    {
        [Test] public void MissingFinaleKeepsLegacyScenesDisabled()
        {
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();
            try{Assert.That(fishing.CreateConfig().Finale.Enabled,Is.False);}
            finally{Object.DestroyImmediate(fishing);}
        }
        [Test] public void FinaleSettingsReachTheSerializedMap()
        {
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();var platform=ScriptableObject.CreateInstance<PlatformSettings>();
            var finale=ScriptableObject.CreateInstance<FinaleSettings>();var commission=ScriptableObject.CreateInstance<CommissionSettings>();
            try
            {
                fishing.finale=finale;fishing.commissions=commission;platform.fishing=fishing;
                finale.holdSeconds=7;finale.sharedStaminaPerSecond=9;
                var config=IslandMap.Decode(platform.CreateMap().Encode()).Fishing.Finale;
                Assert.That(config.Enabled,Is.True);Assert.That(config.HoldTicks,Is.EqualTo(210));Assert.That(config.StaminaMilliPerSecond,Is.EqualTo(9000));
                Assert.That(config.RequiredHooks,Is.EqualTo(new[]{1,2,2,3}));Assert.That(config.BaitCost,Is.EqualTo(3));
                finale.useFinale=false;Assert.That(platform.CreateMap().Fishing.Finale.Enabled,Is.False);
            }
            finally{Object.DestroyImmediate(fishing);Object.DestroyImmediate(platform);Object.DestroyImmediate(finale);Object.DestroyImmediate(commission);}
        }
        [Test] public void FormalSceneBindsAndExportsFinale()
        {
            var settings=AssetDatabase.LoadAssetAtPath<FishingSettings>("Assets/HookIsland/Data/FishingSettings.asset");
            Assert.That(settings.finale,Is.Not.Null);
            var map=IslandMap.Decode(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/PlatformMap.bytes").bytes);
            Assert.That(map.Fishing.Finale.Enabled,Is.True);Assert.That(map.Fishing.Finale.HoldTicks,Is.EqualTo(150));
            Assert.That(map.Fishing.Encode(),Is.EqualTo(settings.CreateConfig().Encode()));
        }
        [TestCase(640,360)] [TestCase(1280,720)] [TestCase(3440,1440)]
        public void ActiveFinaleUsesItsWholePointerBoundaryWithoutOcean(int width,int height)
        {
            float scale=HudLayout.Scale(width,height);var layout=new HudLayout(width/scale,height/scale);
            var row=new Vector2(layout.Finale.center.x,layout.Finale.yMax-8);
            Assert.That(layout.Contains(row,4,true,true,false,true,false,true),Is.True);
            Assert.That(layout.Contains(row,4,true,true,false,true,false,false),Is.False);
            Assert.That(layout.Victory.xMin,Is.GreaterThanOrEqualTo(0));Assert.That(layout.Victory.yMin,Is.GreaterThanOrEqualTo(0));
            Assert.That(layout.Victory.xMax,Is.LessThanOrEqualTo(layout.Width));Assert.That(layout.Victory.yMax,Is.LessThanOrEqualTo(layout.Height));
        }
        [TestCase(0,"→")] [TestCase(90,"↑")] [TestCase(180,"←")] [TestCase(270,"↓")]
        public void OffscreenBankGuidanceUsesTheLocalViewAndDistance(int yaw,string arrow)
        {
            var go=new GameObject("Finale guidance camera");var camera=go.AddComponent<Camera>();
            try
            {
                camera.orthographic=true;camera.orthographicSize=8;camera.aspect=1;camera.transform.SetPositionAndRotation(new Vector3(0,20,0),Quaternion.Euler(90,yaw,0));
                var giant=new FinaleState(FinalePhase.Surfaced,x:22400,z:0,direction:1);
                var projected=camera.WorldToViewportPoint(new Vector3(20,0,0));
                Assert.That(projected.x<0 || projected.x>1 || projected.y<0 || projected.y>1,Is.True,"Fixture must put the bank outside the camera.");
                var text=FinaleHud.Guidance(camera,new FinaleConfig(),giant,new Fisher{X=0,Z=0});
                StringAssert.Contains(arrow,text);StringAssert.Contains("20.0 m",text);StringAssert.Contains("岸边",text);
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test] public void SubmergedGuidanceDoesNotSendPlayersBackToTheOldBank()
        {
            var s=new FinaleState(FinalePhase.Submerged,completed:1,x:0,z:-5100);
            var text=FinaleHud.Guidance(null,new FinaleConfig(),s,default);
            StringAssert.Contains("东岸",text);StringAssert.Contains("优先",text);
        }
    }
}
