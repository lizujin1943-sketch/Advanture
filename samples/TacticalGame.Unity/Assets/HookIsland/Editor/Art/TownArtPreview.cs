using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Editor
{
    /// <summary>
    /// 显式调用的编辑器预览工具。使用正式场景的水面、材质和方向光，
    /// 临时摄像机与初始木带仅在拍摄期间存在，不保存到场景或发布构建。
    /// </summary>
    public static class TownArtPreview
    {
        public static void InstallAndCapture()
        {
            TownArtInstaller.Install();
            Capture();
        }

        [MenuItem("Tools/Hook Island/Town Art/Capture Scene Previews")]
        public static void Capture()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != DrownedBellTownSceneBuilder.ScenePath)
                scene = EditorSceneManager.OpenScene(DrownedBellTownSceneBuilder.ScenePath);
            bool wasDirty = scene.isDirty;
            var cameraObject = new GameObject("Temporary town art preview camera");
            var terrainRoot = new GameObject("Temporary confirmed initial wood preview");
            float oldShadowDistance = QualitySettings.shadowDistance;
            var target = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            var pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            string output = Path.Combine(TownArtLayoutExporter.RepositoryRoot, "artifacts/drowned-bell-town-art");
            Directory.CreateDirectory(output);
            try
            {
                using var terrain = new TownPresentation(terrainRoot.transform);
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.farClipPlane = 600;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.allowHDR = false;
                camera.allowMSAA = true;
                camera.targetTexture = target;
                QualitySettings.shadowDistance = 350;
                CaptureOne("unity-overview", new Vector3(128, 156, -162), new Vector3(0, 1, 5), 63);
                CaptureOne("unity-chapel", new Vector3(31, 32, -12), new Vector3(6, 7, 18), 15);
                CaptureOne("unity-harbor", new Vector3(-73, 25, -64), new Vector3(-52, 2, -30), 15);
                CaptureOne("unity-engine", new Vector3(86, 27, -42), new Vector3(57, 3, -13), 13);

                void CaptureOne(string name, Vector3 at, Vector3 lookAt, float size)
                {
                    camera.transform.position = at;
                    camera.transform.LookAt(lookAt);
                    camera.orthographicSize = size;
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = previous;
                QualitySettings.shadowDistance = oldShadowDistance;
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(terrainRoot);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
                // 原本未修改的场景重新从磁盘打开，不把临时对象造成的脏标记留给用户。
                // 原本有未保存编辑时不重载，保留用户的内存改动。
                if (!wasDirty) EditorSceneManager.OpenScene(scene.path);
            }
            Debug.Log("TOWN_ART_UNITY_PREVIEWS " + output);
        }
    }
}
