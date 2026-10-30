using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class OceanSceneTests
    {
        [TestCase(640,360)] [TestCase(1280,720)] [TestCase(3440,1440)]
        public void RecoveryRowSharesTheVisiblePointerBoundary(int width,int height)
        {
            float scale=HudLayout.Scale(width,height);var layout=new HudLayout(width/scale,height/scale);var panel=layout.CommissionPanel(true);
            var row=new Vector2(panel.center.x,panel.yMax-10);
            Assert.That(layout.Contains(row,4,true,true,true),Is.True);
            Assert.That(layout.Contains(row,4,true,true,false),Is.False);
            foreach(var other in new[]{layout.Squad(4),layout.Wallet,layout.Construction})Assert.That(panel.Overlaps(other),Is.False);
            Assert.That(panel.yMax,Is.LessThanOrEqualTo(layout.Height));
        }
        [Test] public void MissingOceanConfigurationPreservesAllExistingSchools()
        {
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();var platform=ScriptableObject.CreateInstance<PlatformSettings>();
            try
            {
                platform.fishing=fishing;var map=platform.CreateMap();
                Assert.That(map.Fishing.Ocean.Enabled,Is.False);Assert.That(map.FishSpots.Count,Is.EqualTo(24));
            }
            finally{Object.DestroyImmediate(fishing);Object.DestroyImmediate(platform);}
        }
        [Test] public void BoundOceanExportsFixedVisitSlotsAndRecoveryTimings()
        {
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();var platform=ScriptableObject.CreateInstance<PlatformSettings>();
            var ocean=ScriptableObject.CreateInstance<OceanSettings>();var commission=ScriptableObject.CreateInstance<CommissionSettings>();
            try
            {
                fishing.ocean=ocean;fishing.commissions=commission;platform.fishing=fishing;
                var map=IslandMap.Decode(platform.CreateMap().Encode());
                Assert.That(map.Fishing.Ocean.Enabled,Is.True);Assert.That(map.FishSpots.Count,Is.EqualTo(26));
                Assert.That(map.Fishing.Ocean.NoCatchTicks,Is.EqualTo(2700));Assert.That(map.Fishing.Ocean.RecoveryTicks,Is.EqualTo(600));
                ocean.useProgression=false;Assert.That(platform.CreateMap().FishSpots.Count,Is.EqualTo(24));
            }
            finally{Object.DestroyImmediate(fishing);Object.DestroyImmediate(platform);Object.DestroyImmediate(ocean);Object.DestroyImmediate(commission);}
        }
    }
}
