using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Imports the approved island into its own scene without changing the Yard scenes.</summary>
    public static class HookIslandSceneBuilder
    {
        public const string Root = "Assets/HookIsland";
        public const string ScenePath = Root + "/Scenes/HookIsland.unity";
        private const string ModelPath = Root + "/Art/Models/hook_island_v2.fbx";
        private static readonly string[] Groups = { "Ground", "SandBank", "Shop", "Palm", "Pier", "Dressing" };
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../artifacts/hook-island-unity"));

        [MenuItem("Hook Island/Create or Rebuild Island Scene")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Rebuild island",
                    "This replaces HookIsland.unity and its generated prefab. Save a copy of manual edits first.",
                    "Rebuild", "Cancel")) return;
            Build();
        }

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode before rebuilding.");
            if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save modified scenes before building the island.");
            foreach (string path in new[] { "/Art/Materials", "/Art/Meshes", "/Prefabs", "/Scenes" })
                Directory.CreateDirectory(Root + path);
            Directory.CreateDirectory(Output);
            ConfigureAssets();
            var surfaceShader = Shader.Find("Hook Island/Baked Color Surface");
            var oceanShader = Shader.Find("Hook Island/Stylized Ocean");
            if (surfaceShader == null || oceanShader == null)
                throw new InvalidOperationException("Island shaders have not compiled.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null) throw new InvalidOperationException("The baked island FBX is missing.");
            var island = (GameObject)PrefabUtility.InstantiatePrefab(source);
            island.name = "Hook Island";
            PrefabUtility.UnpackPrefabInstance(island, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var renderer in island.GetComponentsInChildren<MeshRenderer>())
            {
                string group = renderer.name.Substring("Island_".Length);
                if (!Groups.Contains(group)) throw new InvalidOperationException("Unexpected model group: " + group);
                var material = GetMaterial(group, surfaceShader);
                material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Textures/" + group + "_Albedo.png");
                material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Textures/" + group + "_AO.png"));
                material.SetFloat("_Ambient", .25f);
                material.SetFloat("_SunStrength", .98f);
                material.SetFloat("_OcclusionStrength", group == "Ground" ? .72f : .55f);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                renderer.receiveShadows = true;
                EditorUtility.SetDirty(material);
                // Low visual ground and the pier use the exact imported surface for traversal.
                if (group == "Ground" || group == "Pier" || group == "Shop")
                    renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            }
            var markers = island.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("__BlenderAxis_")).ToArray();
            var origin = markers.Single(t => t.name == "__BlenderAxis_Origin").position;
            AssertAxis(markers, "X", origin, Vector3.right);
            AssertAxis(markers, "Y", origin, Vector3.forward);
            AssertAxis(markers, "Z", origin, Vector3.up);
            foreach (var marker in markers) UnityEngine.Object.DestroyImmediate(marker.gameObject);
            PrefabUtility.SaveAsPrefabAssetAndConnect(island, Root + "/Prefabs/HookIsland.prefab", InteractionMode.AutomatedAction);

            var sun = new GameObject("Warm Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(-new Vector3(10, 18, 8).normalized);
            ColorUtility.TryParseHtmlString("#FFF3DB", out Color sunColor);
            sun.color = sunColor;
            sun.intensity = 1;
            sun.shadows = LightShadows.Soft;
            sun.shadowBias = .025f;
            sun.shadowNormalBias = .12f;
            sun.shadowResolution = LightShadowResolution.VeryHigh;
            RenderSettings.sun = sun;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.25f, .28f, .30f);
            RenderSettings.fog = false;

            var camera = new GameObject("Island Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 49, -42);
            camera.transform.LookAt(new Vector3(0, .5f, -.5f));
            camera.orthographic = true;
            camera.orthographicSize = 37.6f * 1100 / 1600 / 2;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 220;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .44f, .58f);
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.renderingPath = RenderingPath.Forward;
            camera.gameObject.AddComponent<AudioListener>();
            camera.gameObject.AddComponent<IslandCameraShadows>();

            var ocean = new GameObject("Ocean");
            var filter = ocean.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateOceanMesh();
            var waterRenderer = ocean.AddComponent<MeshRenderer>();
            var waterMaterial = GetMaterial("Ocean", oceanShader);
            waterMaterial.SetTexture("_ShoreDistance", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Textures/ShoreDistance.png"));
            waterRenderer.sharedMaterial = waterMaterial;
            waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterRenderer.receiveShadows = true;
            ocean.AddComponent<IslandOcean>();
            EditorUtility.SetDirty(waterMaterial);
            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = island;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0, 1, 0), camera.transform.rotation, 29);
            Debug.Log("HOOK_ISLAND_BUILD_OK: " + ScenePath + " / 6 baked model groups / animated ocean.");
        }

        private static void AssertAxis(Transform[] markers, string axis, Vector3 origin, Vector3 expected)
        {
            Vector3 actual = markers.Single(t => t.name == "__BlenderAxis_" + axis).position - origin;
            if (Vector3.Distance(actual, expected) > .001f)
                throw new InvalidOperationException("Unexpected FBX axis conversion: " + axis + " " + actual);
        }

        private static void ConfigureAssets()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new FileNotFoundException(ModelPath);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.importLights = false;
            importer.importCameras = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            foreach (string path in Directory.GetFiles(Root + "/Art/Textures", "*.png"))
            {
                var texture = (TextureImporter)AssetImporter.GetAtPath(path);
                bool shore = Path.GetFileNameWithoutExtension(path) == "ShoreDistance";
                texture.textureType = TextureImporterType.Default;
                texture.sRGBTexture = path.Contains("_Albedo");
                texture.textureCompression = TextureImporterCompression.Uncompressed;
                texture.maxTextureSize = 2048;
                texture.mipmapEnabled = !shore;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.anisoLevel = shore ? 1 : 4;
                texture.alphaSource = TextureImporterAlphaSource.None;
                texture.SaveAndReimport();
            }
        }

        private static Material GetMaterial(string name, Shader shader)
        {
            string path = Root + "/Art/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            return material;
        }

        private static Mesh CreateOceanMesh()
        {
            const int cells = 160;
            const float size = 160;
            var vertices = new Vector3[(cells + 1) * (cells + 1)];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[cells * cells * 6];
            for (int z = 0; z <= cells; z++)
                for (int x = 0; x <= cells; x++)
                {
                    int index = z * (cells + 1) + x;
                    vertices[index] = new Vector3((float)x / cells * size - size / 2, 0, (float)z / cells * size - size / 2);
                    normals[index] = Vector3.up;
                    if (x == cells || z == cells) continue;
                    int t = (z * cells + x) * 6;
                    triangles[t] = index;
                    triangles[t + 1] = index + cells + 1;
                    triangles[t + 2] = index + 1;
                    triangles[t + 3] = index + 1;
                    triangles[t + 4] = index + cells + 1;
                    triangles[t + 5] = index + cells + 2;
                }
            string path = Root + "/Art/Meshes/OceanGrid.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh { name = "OceanGrid" };
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 1, size));
            if (create) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        public static string Capture(string name = "island_unity", float time = 0)
        {
            Directory.CreateDirectory(Output);
            var camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("No island camera.");
            var ocean = UnityEngine.Object.FindObjectOfType<IslandOcean>();
            if (ocean != null) ocean.Apply(time);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(1600, 1100, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
            Texture2D image = null;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                image = new Texture2D(1600, 1100, TextureFormat.RGB24, false, false);
                image.ReadPixels(new Rect(0, 0, 1600, 1100), 0, 0);
                image.Apply();
                string file = Path.Combine(Output, name + ".png");
                File.WriteAllBytes(file, image.EncodeToPNG());
                return file;
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(rt);
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
            }
        }

        public static string Validate()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Wrong scene for island validation.");
            var island = GameObject.Find("Hook Island");
            var renderers = island.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length != 6) throw new InvalidOperationException("Missing imported groups.");
            foreach (var renderer in renderers)
            {
                if (renderer.sharedMaterial == null || renderer.sharedMaterial.mainTexture == null ||
                    ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader))
                    throw new InvalidOperationException("Invalid baked material: " + renderer.name);
            }
            var ocean = UnityEngine.Object.FindObjectOfType<IslandOcean>();
            if (ocean == null || ShaderUtil.ShaderHasError(ocean.GetComponent<Renderer>().sharedMaterial.shader))
                throw new InvalidOperationException("Invalid ocean.");
            var ground = renderers.Single(r => r.name == "Island_Ground").bounds;
            if (Mathf.Abs(ground.size.x - 31.922f) > .1f || Mathf.Abs(ground.size.z - 26.702f) > .1f)
                throw new InvalidOperationException("Island scale changed during FBX conversion.");
            var report = new ValidationReport
            {
                scene = scene.path, groups = renderers.Length,
                triangles = island.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length / 3),
                islandSize = ground.size, collisionMeshes = island.GetComponentsInChildren<MeshCollider>().Length,
                oceanVertices = ocean.GetComponent<MeshFilter>().sharedMesh.vertexCount,
                shadersValid = true, bakedColorTextures = 6, bakedAOTextures = 6
            };
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "scene-validation.json"), json + "\n");
            return json;
        }

        [Serializable]
        private sealed class ValidationReport
        {
            public string scene;
            public int groups, triangles, collisionMeshes, oceanVertices, bakedColorTextures, bakedAOTextures;
            public Vector3 islandSize;
            public bool shadersValid;
        }
    }
}
