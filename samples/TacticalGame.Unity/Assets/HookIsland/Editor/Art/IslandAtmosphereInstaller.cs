using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Installs saved sky and static scenery assets without rebuilding the gameplay map.</summary>
    public static class IslandAtmosphereInstaller
    {
        public const string SkyPath = "Assets/HookIsland/Art/Materials/IslandSky.mat";
        private const string SceneryPath = "Assets/HookIsland/Art/Materials/DistantScenery.mat";
        private const string SceneryName = "Distant islands (scenery)";
        private static readonly Vector3[] SceneryPositions =
        {
            new Vector3(-36,0,109), new Vector3(118,0,39),
            new Vector3(22,0,-125), new Vector3(-131,0,-40),
            new Vector3(-105,0,165), new Vector3(176,0,-95), new Vector3(-96,0,-188)
        };
        private static readonly Vector3[] SceneryScales =
        {
            new Vector3(1.35f,2,1.1f), new Vector3(1.6f,2.8f,1.1f),
            new Vector3(.95f,1.7f,.8f), new Vector3(1.5f,2.5f,1.1f),
            new Vector3(1.1f,2.2f,1), new Vector3(1.2f,2.1f,1), new Vector3(1.1f,1.8f,1)
        };

        [MenuItem("Tools/Hook Island/Install Sky and Distant Scenery")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play before installing the atmosphere.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != HookPlatformSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the platform scene first.");
            Configure(Camera.main);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Tools/Hook Island/Rebuild Distant Island Meshes")]
        public static void RebuildMeshes()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play before rebuilding scenery assets.");
            for (int n = 0; n < SceneryPositions.Length; n++)
            {
                string path = MeshPath(n);
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                var generated = DistantIslandMesh.Create(n);
                if (mesh == null) AssetDatabase.CreateAsset(generated, path);
                else
                {
                    EditorUtility.CopySerialized(generated, mesh);
                    UnityEngine.Object.DestroyImmediate(generated);
                    EditorUtility.SetDirty(mesh);
                }
            }
            AssetDatabase.SaveAssets();
        }

        // Also used when the platform scene is created for the first time.
        public static void Configure(Camera camera)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Atmosphere installation is editor-only.");
            RenderSettings.skybox = MaterialAt(SkyPath, "Hook Island/Cartoon Sky");
            if (camera != null)
            {
                Undo.RecordObject(camera, "Set island skybox");
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.backgroundColor = RenderSettings.skybox.GetColor("_SkyHorizonColor");
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 360);
            }
            var scene = SceneManager.GetActiveScene();
            if (scene.GetRootGameObjects().Any(go => go.name == SceneryName)) return;
            var material = MaterialAt(SceneryPath, "Hook Island/Distant Scenery");
            var root = new GameObject(SceneryName);
            Undo.RegisterCreatedObjectUndo(root, "Add distant scenery");
            // These islands have no collider, navigation surface, script, or hook target.
            // Ignore Raycast also keeps future generic input raycasts off background scenery.
            for (int n = 0; n < SceneryPositions.Length; n++)
            {
                string path = MeshPath(n);
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null)
                {
                    mesh = DistantIslandMesh.Create(n);
                    AssetDatabase.CreateAsset(mesh, path);
                }
                var island = new GameObject("Distant island " + (n + 1), typeof(MeshFilter), typeof(MeshRenderer));
                island.layer = 2;
                island.isStatic = true;
                island.transform.SetParent(root.transform, false);
                island.transform.localPosition = SceneryPositions[n];
                island.transform.localRotation = Quaternion.Euler(0, n * 63 + 15, 0);
                island.transform.localScale = SceneryScales[n];
                island.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = island.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        private static string MeshPath(int index) { return "Assets/HookIsland/Art/Meshes/DistantIsland" + index + ".asset"; }

        private static Material MaterialAt(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Missing shader: " + shaderName);
            material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
