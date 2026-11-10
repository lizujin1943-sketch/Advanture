using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    /// <summary>检查导入后的真实网格，防止美术封住门洞或覆盖可毁木带。</summary>
    public sealed class TownArtTests
    {
        private const string PrefabPath = "Assets/HookIsland/Prefabs/DrownedBellTownArt.prefab";

        [Test]
        public void ImportedArtKeepsDoorsAndDestructibleCutsClear()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var root = Object.Instantiate(prefab);
            try
            {
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty,
                    "正式美术不能增加玩法碰撞体；下方 Collider 仅用于测试网格。");
                Assert.That(root.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Preview_")), Is.False);
                var filters = root.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(filters.Count(f => f.name.StartsWith("Details_")), Is.EqualTo(12));
                Assert.That(filters.Count(f => f.name.StartsWith("Cover_")), Is.EqualTo(25));
                foreach (var filter in filters)
                {
                    var materials = filter.GetComponent<MeshRenderer>().sharedMaterials;
                    Assert.That(materials.Length, Is.EqualTo(filter.sharedMesh.subMeshCount), filter.name);
                    Assert.That(materials.All(m => m != null && m.shader != null), Is.True, filter.name);
                }
                var colliders = filters.Select(f =>
                {
                    var collider = f.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = f.sharedMesh;
                    return collider;
                }).ToArray();
                Physics.SyncTransforms();
                // 正向控制：确保临时网格射线确实生效，避免「全部射线失效」也通过空洞检查。
                Assert.That(colliders.Any(c => c.Raycast(new Ray(new Vector3(-8.1f, 50, -22.1f), Vector3.down), out _, 60)),
                    Is.True, "射线应击中既有南侧石台");
                var solid = TownLayout.Buildings.First(b => !b.Enterable && b.Kind != TownBuildingKind.Dock);
                Assert.That(colliders.Any(c => c.Raycast(new Ray(new Vector3(solid.X / 1000f, 50, solid.Z / 1000f), Vector3.down), out _, 60)),
                    Is.True, "射线应击中实体房屋屋顶");
                foreach (var building in TownLayout.Buildings.Where(b => b.Enterable))
                    foreach (float offset in new[] { -1.05f, 0, 1.05f })
                    {
                        var from = new Vector3(building.X / 1000f + offset, 1.6f,
                            (building.Z - building.Depth / 2) / 1000f - 2);
                        AssertClear(new Ray(from, Vector3.forward), 4, "门洞 " + building.Name);
                    }
                foreach (var wood in TownLayout.WoodSegments)
                    AssertClear(new Ray(new Vector3(wood.X / 1000f, 50, wood.Z / 1000f), Vector3.down), 60,
                        "木带 " + wood.BandId + "/" + wood.SegmentId);
                AssertClear(new Ray(new Vector3(-8, 50, -12), Vector3.down), 60, "BOSS 水中区域");

                void AssertClear(Ray ray, float length, string purpose)
                {
                    foreach (var collider in colliders)
                        Assert.That(collider.Raycast(ray, out _, length), Is.False, purpose + " 被 " + collider.name + " 遮挡");
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void FormalSceneUsesAuthoredArtAndHidesBlockoutRenderers()
        {
            var scene = EditorSceneManager.OpenScene("Assets/HookIsland/Scenes/DrownedBellTown.unity", OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var authored = roots.Single(g => g.name == "Drowned Bell Town authored art");
                Assert.That(authored.activeSelf, Is.True);
                Assert.That(authored.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(authored.GetComponentsInChildren<Renderer>().All(r => r.enabled), Is.True);
                foreach (var root in roots.Where(g => g.name == "Authoritative stone streets and arena" ||
                    g.name == "Town buildings and authoritative cover"))
                    Assert.That(root.GetComponentsInChildren<Renderer>(true).All(r => !r.enabled), Is.True);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
