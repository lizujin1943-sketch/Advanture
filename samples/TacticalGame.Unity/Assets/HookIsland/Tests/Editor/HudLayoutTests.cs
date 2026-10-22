using NUnit.Framework;
using UnityEngine;
using UnityEditor;

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
            var rects=new[]{layout.Squad(4),layout.Resources(true),layout.Menu,layout.Vitals,layout.Tools,layout.Construction};
            for(int i=0;i<rects.Length;i++)
            {
                Assert.That(rects[i].xMin,Is.GreaterThanOrEqualTo(0));Assert.That(rects[i].yMin,Is.GreaterThanOrEqualTo(0));
                Assert.That(rects[i].xMax,Is.LessThanOrEqualTo(layout.Width));Assert.That(rects[i].yMax,Is.LessThanOrEqualTo(layout.Height));
                for(int j=i+1;j<rects.Length;j++)Assert.That(rects[i].Overlaps(rects[j]),Is.False,$"HUD overlap: {i}, {j}");
            }
            for(int i=0;i<3;i++)Assert.That(layout.Contains(layout.Tool(i).center,4,true,true,false),Is.True,"Visible button must block casting.");
        }
        [Test] public void HiddenConstructionAndOldInstructionStripDoNotBlockCasting()
        {
            var layout=new HudLayout(1280,720);
            Assert.That(layout.Contains(layout.Construction.center,4,true,true,false),Is.False);
            Assert.That(layout.Contains(layout.Construction.center,4,true,true,true),Is.True);
            Assert.That(layout.Contains(new Vector2(360,680),4,true,true,false),Is.False,"Old bottom instruction strip must be playable.");
            Assert.That(layout.Contains(new Vector2(220,210),4,true,true,false),Is.False,"Old large left panel must be playable.");
            Assert.That(layout.Contains(new Vector2(640,360),4,true,true,true),Is.False,"Scene center must stay playable.");
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
    }
}
