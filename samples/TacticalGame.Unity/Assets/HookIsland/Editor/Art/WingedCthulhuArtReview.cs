using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Editor
{
    /// <summary>显式编辑器验收入口。仅临时采样表现，不改场景文件或权威游戏状态。</summary>
    public static class WingedCthulhuArtReview
    {
        public static void InstallAndCapture() { WingedCthulhuArtInstaller.Install(); CaptureTown(); }

        [MenuItem("Tools/Hook Island/Capture Winged Cthulhu In Town")]
        public static void CaptureTown()
        {
            if (Application.isPlaying) throw new InvalidOperationException("先停止运行。");
            // Interactive use must preserve user edits; batch has no user-owned scene state.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(DrownedBellTownSceneBuilder.ScenePath);
            var settings = AssetDatabase.LoadAssetAtPath<BossSettings>(BossChallengeInstaller.SettingsPath);
            var town = AssetDatabase.LoadAssetAtPath<PlatformSettings>(DrownedBellTownSceneBuilder.SettingsPath);
            if (town.fishing.boss != settings || settings.modelPrefab.name != "WingedCthulhu")
                throw new InvalidDataException("沉钟镇没有引用新 Boss 资源。");
            var root = new GameObject("Temporary Boss Art Review");
            root.transform.position = new Vector3(TownLayout.CenterX, 0, TownLayout.CenterZ) / 1000;
            var camObject = new GameObject("Temporary Boss Review Camera");
            var camera = camObject.AddComponent<Camera>(); camera.fieldOfView = 40;
            camera.nearClipPlane = .1f; camera.farClipPlane = 300; camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.Skybox;
            var view = new BossAnimationView(root.transform, settings);
            var render = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                var offsets = new[] { new Vector3(21, 16, -29), new Vector3(0, 8, -29), new Vector3(0, 10, 29), new Vector3(13, 13, -33) };
                var names = new[] { "hero", "front", "back", "slam" };
                for (int i = 0; i < offsets.Length; i++)
                {
                    var limbs = Enumerable.Range(0, 8).Select(id => new BossLimbState(id, 300, 300)).ToArray();
                    if (i == 3) limbs[1] = new BossLimbState(1, 300, 300, BossAttack.Slam, 36);
                    view.Synchronize(new BossState(BossPhase.Combat, 2400, 2400, 1, phaseTicks: 67, limbs: limbs, townArena: true), (ulong)(100 + i));
                    camera.transform.position = root.transform.position + offsets[i];
                    camera.transform.LookAt(root.transform.position + new Vector3(0, 5, i == 3 ? -4 : 0));
                    camera.targetTexture = render; camera.Render(); RenderTexture.active = render;
                    var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                    try
                    {
                        image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
                        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../source-assets/creatures/winged-cthulhu/review/unity-town-" + names[i] + ".png"));
                        File.WriteAllBytes(path, image.EncodeToPNG());
                    }
                    finally { Object.DestroyImmediate(image); }
                }
                Debug.Log("WINGED_CTHULHU_TOWN_REVIEW_CAPTURED");
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; render.Release(); Object.DestroyImmediate(render);
                view.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(camObject);
            }
        }
    }
}
