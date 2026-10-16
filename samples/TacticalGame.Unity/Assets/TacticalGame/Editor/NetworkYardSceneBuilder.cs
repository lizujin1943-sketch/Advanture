using System;
using System.IO;
using System.Linq;
using Advanture.Tactical.Simulation;
using Advanture.TacticalGame.NetworkView;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Advanture.TacticalGame.Editor
{
    /// <summary>从本地试玩导出纯整数碰撞地图并生成独立联机场景，保留原来的离线场景。</summary>
    public static class NetworkYardSceneBuilder
    {
        public const string ScenePath = "Assets/TacticalGame/Scenes/Yard07Network.unity";
        public const string MapPath = "Assets/TacticalGame/Network/Yard07Map.bytes";
        public static TacticalMap ExportMap()
        {
            Physics.SyncTransforms();
            var boxes = UnityEngine.Object.FindObjectsOfType<BoxCollider>().OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
            var ground = boxes.Single(c => c.name == "COL_Playable_Ground");
            Func<Bounds, MapRect> convert = b => new MapRect(Mathf.RoundToInt(b.min.x*1000), Mathf.RoundToInt(b.min.z*1000), Mathf.RoundToInt(b.max.x*1000), Mathf.RoundToInt(b.max.z*1000));
            return new TacticalMap(convert(ground.bounds), new[] { new PlayerSpawn(-4000,-8000,9000), new PlayerSpawn(4000,-8000,27000) },
                boxes.Where(c => c != ground).Select(c => convert(c.bounds)).ToArray());
        }
        [MenuItem("Tactical Game/Create or Rebuild Network Yard 07")]
        public static void Build()
        {
            // 菜单重建会切换场景，先给用户保存当前修改的机会；取消时不导出、不覆盖。
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(YardSceneBuilder.ScenePath);
            var data = ExportMap(); File.WriteAllBytes(MapPath, data.Encode()); AssetDatabase.ImportAsset(MapPath, ImportAssetOptions.ForceSynchronousImport);
            var old = UnityEngine.Object.FindObjectOfType<FirstPersonMotor>(); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("NetworkRoom"); root.AddComponent<NetworkInputSource>();
            var camera = new GameObject("NetworkCamera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(-4,1.65f,-8); camera.transform.rotation = Quaternion.Euler(0,90,0);
            camera.fieldOfView = 75; camera.nearClipPlane = .05f; camera.farClipPlane = 180; camera.gameObject.AddComponent<AudioListener>();
            root.AddComponent<NetworkYardController>().Configure(AssetDatabase.LoadAssetAtPath<TextAsset>(MapPath), camera);
            root.AddComponent<NetworkRoomOverlay>(); root.AddComponent<YardLaunchArguments>(); root.AddComponent<NetworkSmokeDriver>();
            PlayerSettings.runInBackground = true;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true), new EditorBuildSettingsScene(YardSceneBuilder.ScenePath, false) };
            AssetDatabase.SaveAssets(); Debug.Log("YARD_NETWORK_SCENE_OK obstacles=" + data.Obstacles.Count);
        }
    }
}
