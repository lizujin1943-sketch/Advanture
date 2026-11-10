using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Editor
{
    /// <summary>
    /// 导入 Blender 制作的沉钟镇，并给正式场景安装纯表现层预制件。
    /// 建筑布局来自 TownLayout；模型不带 Collider，不参与帧同步和权威地形判断。
    /// 可毁木带仍由 TownPresentation 按确认状态创建，不能烘进静态 FBX。
    /// </summary>
    public static class TownArtInstaller
    {
        public const string ArtDirectory = "Assets/HookIsland/Art/Town";
        public const string ModelPath = ArtDirectory + "/DrownedBellTown.fbx";
        public const string PrefabPath = "Assets/HookIsland/Prefabs/DrownedBellTownArt.prefab";
        public const string SceneRootName = "Drowned Bell Town authored art";

        [MenuItem("Tools/Hook Island/Town Art/Import and Apply to Formal Scene")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先停止运行再安装地图美术。");
            var active = SceneManager.GetActiveScene();
            if (!Application.isBatchMode && active.isDirty && active.path != DrownedBellTownSceneBuilder.ScenePath)
                throw new InvalidOperationException("请先保存当前场景，再安装沉钟镇美术。");
            BuildPrefab();
            var scene = active.path == DrownedBellTownSceneBuilder.ScenePath ? active :
                EditorSceneManager.OpenScene(DrownedBellTownSceneBuilder.ScenePath, OpenSceneMode.Single);
            if (!TryApply(scene)) throw new InvalidOperationException("沉钟镇美术预制件未生成。");
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("TOWN_ART_INSTALLED " + PrefabPath);
        }

        /// <summary>
        /// 重建预制件，但保留材质与预制件的 GUID，使场景引用保持稳定。
        /// 先核对布局指纹、米制坐标和材质槽，避免过期模型覆盖新玩法布局。
        /// </summary>
        public static void BuildPrefab()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var manifest = ReadManifest();
            ValidateLayout(manifest);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("请先运行 Blender 地图脚本。", ModelPath);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();

            var shader = Shader.Find("Hook Island/Baked Color Surface");
            if (shader == null) throw new InvalidOperationException("地图表面着色器缺失。");
            Directory.CreateDirectory(ArtDirectory + "/Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var palette = new Dictionary<string, Material>();
            foreach (var entry in manifest.materials)
            {
                string path = ArtDirectory + "/Materials/" + entry.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = entry.name };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                // Blender 清单存在线性空间；Unity 的 Color 属性以 sRGB 写入。
                material.color = entry.color.gamma;
                material.SetFloat("_Ambient", entry.name == "Town_Window" ? .95f : .34f);
                material.SetFloat("_SunStrength", entry.name == "Town_Window" ? .20f : .85f);
                EditorUtility.SetDirty(material);
                palette.Add(entry.name, material);
            }
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            try
            {
                model.name = SceneRootName;
                var markers = model.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith("__BlenderAxis_", StringComparison.Ordinal)).ToArray();
                var origin = markers.Single(t => t.name == "__BlenderAxis_Origin").position;
                if (origin.sqrMagnitude > .000001f) throw new InvalidOperationException("地图原点偏移。");
                AssertAxis(markers, "X", origin, Vector3.right);
                AssertAxis(markers, "Y", origin, Vector3.forward);
                AssertAxis(markers, "Z", origin, Vector3.up);
                foreach (var marker in markers) Object.DestroyImmediate(marker.gameObject);
                var meshes = manifest.meshes.ToDictionary(m => m.name);
                var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != meshes.Count) throw new InvalidOperationException("FBX 网格数量与清单不符。");
                foreach (var renderer in renderers)
                {
                    var entry = meshes[renderer.name];
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh.subMeshCount != entry.materials.Length)
                        throw new InvalidOperationException("材质槽顺序不匹配：" + renderer.name);
                    renderer.sharedMaterials = entry.materials.Select(n => palette[n]).ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                    renderer.receiveShadows = true;
                }
                if (model.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("地图美术不允许新增物理碰撞体。");
                if (model.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Preview_", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Blender 预览水面或木桥不能导入正式美术。");
                PrefabUtility.SaveAsPrefabAsset(model, PrefabPath);
            }
            finally { Object.DestroyImmediate(model); }
        }

        /// <summary>
        /// 可重复应用到现有场景或场景生成器。先确认新资源存在，再隐藏旧方块表现；
        /// 原地面网格、控制器、配置资源均由各自系统维护，不在这里重建。
        /// </summary>
        public static bool TryApply(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return false;
            ValidateLayout(ReadManifest());
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == SceneRootName) Object.DestroyImmediate(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = SceneRootName;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Authoritative stone streets and arena" || root.name == "Town buildings and authoritative cover")
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            EditorSceneManager.MarkSceneDirty(scene);
            return true;
        }

        /// <summary>布局变更后须先重新导出、建模，不能把错位美术静默装进场景。</summary>
        public static void ValidateCurrentLayout() => ValidateLayout(ReadManifest());

        private static void ValidateLayout(Manifest manifest)
        {
            using var sha = SHA256.Create();
            string actual = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(TownArtLayoutExporter.CreateJson().Trim())))
                .Replace("-", "").ToLowerInvariant();
            if (manifest.version != 1 || actual != manifest.layoutHash)
                throw new InvalidOperationException("沉钟镇布局已变化，请重新导出布局并运行 Blender 脚本。");
        }

        private static Manifest ReadManifest() => JsonUtility.FromJson<Manifest>(File.ReadAllText(ArtDirectory + "/manifest.json"));

        private static void AssertAxis(Transform[] markers, string axis, Vector3 origin, Vector3 expected)
        {
            var actual = markers.Single(t => t.name == "__BlenderAxis_" + axis).position - origin;
            if (Vector3.Distance(actual, expected) > .001f)
                throw new InvalidOperationException("FBX 坐标或单位错误：" + axis + " = " + actual);
        }

        [Serializable] private sealed class Manifest
        {
            public int version;
            public string layoutHash;
            public PaletteEntry[] materials;
            public MeshEntry[] meshes;
        }
        [Serializable] private sealed class PaletteEntry { public string name; public Color color; }
        [Serializable] private sealed class MeshEntry { public string name; public string[] materials; }
    }
}
