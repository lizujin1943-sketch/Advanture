using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Tests
{
    public sealed class HudLayoutTests
    {
        [TestCase(640,360)]
        [TestCase(1280,720)]
        [TestCase(1280,800)]
        [TestCase(1920,1080)]
        [TestCase(3440,1440)]
        public void VisibleControlsFitAndDoNotOverlapAtSupportedWindowSizes(int width,int height)
        {
            float scale=HudLayout.Scale(width,height);var layout=new HudLayout(width/scale,height/scale);
            var rects=new[]{layout.ObjectiveSummary,layout.CommissionPanel(true),layout.Squad(3,1,true),layout.Wallet,layout.Construction,layout.Acquisitions,layout.Combo,layout.FirearmAmmo,layout.LocalHealing,layout.LocalHealth};
            for(int i=0;i<rects.Length;i++)
            {
                Assert.That(rects[i].xMin,Is.GreaterThanOrEqualTo(0));Assert.That(rects[i].yMin,Is.GreaterThanOrEqualTo(0));
                Assert.That(rects[i].xMax,Is.LessThanOrEqualTo(layout.Width));Assert.That(rects[i].yMax,Is.LessThanOrEqualTo(layout.Height));
                for(int j=i+1;j<rects.Length;j++)Assert.That(rects[i].Overlaps(rects[j]),Is.False,$"HUD overlap: {i}, {j}");
            }
            Assert.That(layout.Contains(layout.LocalHealth.center,4,false),Is.True,"点击固定血条不能穿透成出钩或开火。");
            Assert.That(layout.Contains(layout.LocalHealing.center,4,false),Is.True,"医疗罐和次数区域不能穿透成出钩或开火。");
            Assert.That(layout.LocalHealing.xMin,Is.EqualTo(32));
            Assert.That(layout.LocalHealing.xMax,Is.LessThan(layout.LocalHealth.xMin),"医疗罐应位于血条左侧，互不覆盖。");
            Assert.That(layout.Height-layout.LocalHealing.yMax,Is.EqualTo(32));
            Assert.That(layout.Height-layout.LocalHealth.yMax,Is.EqualTo(32),"血条始终锚定左下角。");
        }
        [Test] public void CollapsedTaskDrawerOnlyBlocksItsVisibleHandleAndCanBeReversed()
        {
            var layout=new HudLayout(1280,720);var motion=new ObjectivePanelMotion(true);
            Assert.That(motion.Reveal(0),Is.Zero,"常驻摘要不应默认展开详细任务面板。");
            Assert.That(layout.Contains(layout.Commissions.center,0,false,true,true,false,motion.Reveal(0)),Is.False);
            motion.Toggle(1);float middle=motion.Reveal(1.11f);
            Assert.That(middle,Is.InRange(.45f,.55f));
            Assert.That(layout.Contains(layout.Commissions.center,0,false,true,true,false,motion.Reveal(2)),Is.True);
            Assert.That(layout.Contains(layout.ObjectiveTab(0).center,1,false,true,true,false,0),Is.True);
            Assert.That(layout.Contains(layout.Acquisitions.center,1,false,true,true),Is.False,"Passive notices must not swallow casts.");
            motion.Toggle(2);Assert.That(motion.Reveal(3),Is.Zero);
            Assert.That(layout.Contains(layout.Commissions.center,0,false,true,true,false,motion.Reveal(3)),Is.False);
        }
        [Test] public void HiddenConstructionAndOldInstructionStripDoNotBlockCasting()
        {
            var layout=new HudLayout(1280,720);
            Assert.That(layout.Contains(new Vector2(640,650),4,false),Is.False,"Removed vitals panel must not block casting.");
            Assert.That(layout.Contains(layout.Construction.center,4,false),Is.False);
            Assert.That(layout.Contains(layout.Construction.center,4,true),Is.True);
            Assert.That(layout.Contains(new Vector2(440,680),4,false),Is.False,"生命与医疗罐以外的旧操作提示区域仍允许操作。");
            Assert.That(layout.Contains(new Vector2(220,210),4,false),Is.False,"Old large left panel must be playable.");
            Assert.That(layout.Contains(new Vector2(640,360),4,true),Is.False,"Scene center must stay playable.");
            Assert.That(layout.Contains(layout.FirearmAmmo.center,0,false),Is.False,"只读弹药和已移除的右下按钮不能拦截开火。");
            Assert.That(layout.Contains(new Vector2(1200,680),0,false),Is.False,"旧工具按钮区域不再遮挡操作。");
        }
        [Test] public void DisabledTaskDisplayDoesNotBlockItsSummaryTabOrFormerDrawer()
        {
            var layout=new HudLayout(1280,720);
            foreach(var point in new[]{layout.ObjectiveSummary.center,layout.ObjectiveTab().center,layout.CommissionPanel(true).center})
                Assert.That(layout.Contains(point,0,false,true,true,true,1,showObjectives:false),Is.False,
                    "即使委托或终局规则正在运行，隐藏的任务区域也不能拦截射击或出钩。");
            Assert.That(layout.Contains(layout.Squad(1).center,1,false,true,true,true,1,showObjectives:false),Is.True,
                "任务隐藏后队友不能保留已展开抽屉造成的偏移。");
        }
        [TestCase(-10,100,0f)]
        [TestCase(150,100,1f)]
        [TestCase(20,0,0f)]
        [TestCase(25,100,.25f)]
        public void ProgressIsSafeForEmptyOrChangedEquipment(int current,int maximum,float expected)
        {Assert.That(HudPainter.Fraction(current,maximum),Is.EqualTo(expected));}

        [Test] public void IconAtlasShipsAtNativeSizeWithTransparentUncompressedUiSettings()
        {
            var texture=Resources.Load<Texture2D>("UI/HudIcons");Assert.That(texture,Is.Not.Null);
            Assert.That(texture.width,Is.EqualTo(512));Assert.That(texture.height,Is.EqualTo(192));
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            Assert.That(importer.mipmapEnabled,Is.False);Assert.That(importer.alphaIsTransparency,Is.True);
            Assert.That(importer.textureCompression,Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(texture.wrapMode,Is.EqualTo(TextureWrapMode.Clamp));
        }

        [Test] public void SquadExcludesLocalAndPendingSeatsButKeepsDisconnectedTeammates()
        {
            var state=new HookState(0,new[]{new Fisher{Slot=0},new Fisher{Slot=1,Unavailable=true},new Fisher{Slot=2,WaitingToJoin=true}},System.Array.Empty<Fish>(),0);
            Assert.That(GameplayHud.TeammateCount(state,0),Is.EqualTo(1));
            Assert.That(GameplayHud.TeammateCount(state,1),Is.EqualTo(1));
            var solo=new HookState(0,new[]{new Fisher{Slot=0}},System.Array.Empty<Fish>(),0);
            Assert.That(GameplayHud.TeammateCount(solo,0),Is.Zero);
            var layout=new HudLayout(1280,720);
            Assert.That(layout.Squad(0).width,Is.Zero);
            Assert.That(layout.Contains(layout.Squad(1).center,0,false),Is.False,"单人时不能保留隐形的队伍点击区。");
            Assert.That(layout.Contains(layout.Squad(1).center,1,false),Is.True);
        }
    }
}
