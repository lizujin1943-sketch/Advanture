using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Advanture.HookIsland.Editor
{
    /// <summary>显式将可选挑战装进已有钓鱼地图。重复执行复用资源 GUID，不重建场景或其他玩法。</summary>
    public static class BossChallengeInstaller
    {
        public const string SettingsPath = "Assets/HookIsland/Data/BossSettings.asset";
        [MenuItem("Tools/Hook Island/Install Abyssal Watcher Challenge")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先停止游戏再安装 BOSS。");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != HookPlatformSceneBuilder.ScenePath)
                throw new InvalidOperationException("请先打开 HookPlatform 场景。");
            var platform = AssetDatabase.LoadAssetAtPath<PlatformSettings>(HookPlatformSceneBuilder.SettingsPath);
            if (platform == null || platform.fishing == null) throw new InvalidOperationException("请先安装现有钓鱼系统。");
            AbyssalWatcherArtInstaller.Install();
            var settings = AssetDatabase.LoadAssetAtPath<BossSettings>(SettingsPath);
            if (settings == null)
            { settings = ScriptableObject.CreateInstance<BossSettings>(); AssetDatabase.CreateAsset(settings, SettingsPath); }
            settings.modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AbyssalWatcherArtInstaller.PrefabPath);
            settings.clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/HookIsland/Art/AbyssalWatcher/Animations" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AnimationClip>).OrderBy(c => c.name).ToArray();
            if (settings.modelPrefab == null || settings.clips.Length != 24) throw new InvalidOperationException("BOSS 模型或 24 个动作不完整。");
            settings.enableChallenge = true;
            platform.fishing.boss = settings;
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(platform.fishing);
            var map = platform.CreateMap();
            // 用真正的地图编码器导出，保证服务端和客户端读取同一套数值与地形。
            File.WriteAllBytes("Assets/HookIsland/Data/PlatformMap.bytes", map.Encode());
            AssetDatabase.ImportAsset("Assets/HookIsland/Data/PlatformMap.bytes", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("ABYSSAL_CHALLENGE_READY: 现有地图北侧环形栈道，靠近南入口按 E 唤醒；地图初始地板 " + PlatformRules.InitialTiles(map).Length);
        }
    }
}
