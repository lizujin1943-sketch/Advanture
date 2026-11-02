using System;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class PlatformBuildPreviewTests
    {
        [Test] public void DraftShowsGreenAndRedTilesWithoutWalkableCollidersAndReusesGhosts()
        {
            var root=new GameObject("construction-preview-contract");
            var type=typeof(PlatformConstruction).Assembly.GetType("Advanture.HookIsland.PlatformBuildPreviewPresentation");
            var view=Activator.CreateInstance(type,new object[]{root.transform});
            try
            {
                var cells=new[]{
                    new PlatformBuildPreview(new PlatformTile(2,0,TileKind.Wood),BuildResult.Success),
                    new PlatformBuildPreview(new PlatformTile(3,0,TileKind.Wood),BuildResult.InsufficientMaterials),
                    new PlatformBuildPreview(new PlatformTile(0,0,TileKind.Wood),BuildResult.Occupied)};
                type.GetMethod("ShowDraft").Invoke(view,new object[]{cells});
                var ghosts=root.GetComponentsInChildren<MeshRenderer>();
                Assert.That(ghosts.Length,Is.EqualTo(3));
                Assert.That(ghosts[0].sharedMaterial.color.g,Is.GreaterThan(ghosts[0].sharedMaterial.color.r));
                Assert.That(ghosts.Skip(1).All(g=>g.sharedMaterial.color.r>g.sharedMaterial.color.g),Is.True);
                Assert.That(root.GetComponentsInChildren<Collider>().Any(c=>c.enabled),Is.False,"Ghosts must not create traversable platforms before a confirmed build.");
                Assert.That(ghosts.All(g=>g.transform.position.y>PlatformGeometry.DeckHeight),Is.True,"Occupied previews must remain visible above existing tiles.");
                type.GetMethod("Hide").Invoke(view,null);
                Assert.That(root.GetComponentsInChildren<MeshRenderer>().Length,Is.Zero);
                type.GetMethod("ShowDraft").Invoke(view,new object[]{cells});
                Assert.That(root.GetComponentsInChildren<MeshRenderer>(true).Length,Is.EqualTo(3),"Redrawing allocated another set of ghosts.");
            }
            finally{((IDisposable)view).Dispose();Object.DestroyImmediate(root);}
        }

        [Test] public void ClosingBuildModeCancelsDraftAndUnsentBatch()
        {
            var root=new GameObject("construction-close-contract");root.SetActive(false);
            try
            {
                var build=root.AddComponent<PlatformConstruction>();
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var draft=(PlatformBuildDraft)typeof(PlatformConstruction).GetField("draft",flags).GetValue(build);
                var batch=(PlatformBuildBatch)typeof(PlatformConstruction).GetField("requests",flags).GetValue(build);
                draft.Begin(TileKind.Wood,2,0);
                batch.TryStart(new[]{new PlatformBuildPreview(new PlatformTile(2,0,TileKind.Wood),BuildResult.Success)});
                build.Close();
                Assert.That(build.Dragging || build.Pending || build.Open,Is.False);
                Assert.That(batch.Cancelled,Is.EqualTo(1));
            }
            finally{Object.DestroyImmediate(root);}
        }
    }
}
