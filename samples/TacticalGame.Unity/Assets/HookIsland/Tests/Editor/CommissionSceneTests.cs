using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class CommissionSceneTests
    {
        [Test] public void MissingConfigurationKeepsCommissionsDisabled()
        {
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();
            try{Assert.That(fishing.CreateConfig().Commissions.Enabled,Is.False);}
            finally{Object.DestroyImmediate(fishing);}
        }
        [Test] public void ConfiguredObjectivesAndBaitReachSimulation()
        {
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();var settings=ScriptableObject.CreateInstance<CommissionSettings>();
            try
            {
                fishing.commissions=settings;settings.objectives=new[]{new CommissionObjective {fish=8,rare=3,bait=2}};
                var config=fishing.CreateConfig().Commissions;
                Assert.That(config.Enabled,Is.True);Assert.That(config.Definitions.Count,Is.EqualTo(1));
                Assert.That(config.Definitions[0].Fish,Is.EqualTo(8));Assert.That(config.Definitions[0].Rare,Is.EqualTo(3));Assert.That(config.Definitions[0].Bait,Is.EqualTo(2));
                settings.useCommissions=false;Assert.That(fishing.CreateConfig().Commissions.Enabled,Is.False);
            }
            finally{Object.DestroyImmediate(fishing);Object.DestroyImmediate(settings);}
        }
        [TestCase(640,360)] [TestCase(1280,720)] [TestCase(3440,1440)]
        public void CommissionPanelBlocksCastsOnlyWhileVisible(int width,int height)
        {
            float scale=HudLayout.Scale(width,height);var layout=new HudLayout(width/scale,height/scale);var panel=layout.Commissions;
            Assert.That(layout.Contains(panel.center,4,true,true),Is.True);
            Assert.That(layout.Contains(panel.center,4,true,false),Is.False);
            foreach(var other in new[]{layout.Squad(4),layout.Wallet,layout.Construction})Assert.That(panel.Overlaps(other),Is.False);
            Assert.That(panel.xMax,Is.LessThanOrEqualTo(layout.Width));Assert.That(panel.yMax,Is.LessThanOrEqualTo(layout.Height));
        }
    }
}
