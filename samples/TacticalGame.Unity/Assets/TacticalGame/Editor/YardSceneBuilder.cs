using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Advanture.TacticalGame.Editor
{
    /// <summary>
    /// 将仓库中的 Blender 导出资源导入为可直接试玩的 Unity 场景。
    /// 可视模型与碰撞模型使用同一 FBX 导入坐标转换，避免手动换轴造成错位。
    /// 仅属于游戏示例，不参与帧同步框架。
    /// </summary>
    public static class YardSceneBuilder
    {
        public const string ScenePath = "Assets/TacticalGame/Scenes/Yard07.unity";
        private const string ArtPath = "Assets/TacticalGame/Art/Yard07/";
        private const string MaterialPath = "Assets/TacticalGame/Art/Materials/";
        private const string PrefabPath = "Assets/TacticalGame/Prefabs/FirstPersonPlayer.prefab";
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string ArtifactPath => Path.GetFullPath(Path.Combine(ProjectRoot,"../../artifacts/tactical-unity"));

        [MenuItem("Tactical Game/Create or Rebuild Yard 07")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (var folder in new[] { ArtPath,MaterialPath,"Assets/TacticalGame/Scenes","Assets/TacticalGame/Prefabs" })
                Directory.CreateDirectory(folder);
            Directory.CreateDirectory(ArtifactPath);
            string source = Path.GetFullPath(Path.Combine(ProjectRoot,"../TacticalMap/Exports"));
            foreach (string file in new[] { "yard_07.fbx", "yard_07_collision.fbx" })
                File.Copy(Path.Combine(source,file),ArtPath+file,true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporter(ArtPath+"yard_07.fbx");
            ConfigureImporter(ArtPath+"yard_07_collision.fbx");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPath+"yard_07.fbx");
            if (model == null) throw new InvalidOperationException("地图 FBX 导入失败。");
            var map = (GameObject)PrefabUtility.InstantiatePrefab(model);
            map.name = "Yard07_Visual";
            var palette = CreatePalette();
            foreach (var renderer in map.GetComponentsInChildren<MeshRenderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i=0; i<materials.Length; i++)
                {
                    string name = materials[i] != null ? materials[i].name : "Concrete";
                    if (!palette.TryGetValue(name,out var replacement))
                        throw new InvalidOperationException("缺少地图材质映射："+name);
                    materials[i]=replacement;
                }
                renderer.sharedMaterials=materials;
                renderer.shadowCastingMode=ShadowCastingMode.On;
                renderer.receiveShadows=true;
            }

            var collisionModel=AssetDatabase.LoadAssetAtPath<GameObject>(ArtPath+"yard_07_collision.fbx");
            var collisionRoot=(GameObject)PrefabUtility.InstantiatePrefab(collisionModel);
            collisionRoot.name="Yard07_Collision";
            // 解开碰撞体副本的预制体关联后删除显示组件，只留下可编辑 BoxCollider。
            PrefabUtility.UnpackPrefabInstance(collisionRoot,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach (var filter in collisionRoot.GetComponentsInChildren<MeshFilter>())
            {
                var box=filter.gameObject.AddComponent<BoxCollider>();
                box.center=filter.sharedMesh.bounds.center;
                box.size=filter.sharedMesh.bounds.size;
                UnityEngine.Object.DestroyImmediate(filter.GetComponent<MeshRenderer>());
                UnityEngine.Object.DestroyImmediate(filter);
            }

            var spawnA=map.GetComponentsInChildren<Transform>().Single(t=>t.name=="Spawn_Team_A");
            var spawnB=map.GetComponentsInChildren<Transform>().Single(t=>t.name=="Spawn_Team_B");
            Vector3 across=Vector3.ProjectOnPlane(spawnB.position-spawnA.position,Vector3.up).normalized;
            Vector3 north=Vector3.Cross(across,Vector3.up);
            var northContainer=map.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Container_North_Body");
            if (Vector3.Dot(north,northContainer.bounds.center)<0) north=-north;

            var player=new GameObject("FirstPersonPlayer");
            player.transform.position=spawnA.position+Vector3.up*.05f;
            // 出生时略朝向遮挡墙一端的出口，避免初始画面完全被近处墙面填满。
            player.transform.rotation=Quaternion.LookRotation(across+north*1.4f);
            var controller=player.AddComponent<CharacterController>();
            controller.height=1.8f;
            controller.radius=.35f;
            controller.center=new Vector3(0,.9f,0);
            controller.skinWidth=.035f;
            controller.stepOffset=.25f;
            controller.slopeLimit=45;
            controller.minMoveDistance=0;
            var cameraObject=new GameObject("FirstPersonCamera");
            cameraObject.tag="MainCamera";
            cameraObject.transform.SetParent(player.transform,false);
            cameraObject.transform.localPosition=new Vector3(0,1.65f,0);
            var camera=cameraObject.AddComponent<Camera>();
            camera.fieldOfView=75;
            camera.nearClipPlane=.05f;
            camera.farClipPlane=180;
            camera.allowHDR=true;
            cameraObject.AddComponent<AudioListener>();
            player.AddComponent<FirstPersonMotor>().Configure(camera.transform);
            player.AddComponent<FirstPersonInput>();
            player.AddComponent<ExplorationOverlay>();
            PrefabUtility.SaveAsPrefabAssetAndConnect(player,PrefabPath,InteractionMode.AutomatedAction);
            ConfigureLighting();
            PlayerSettings.companyName="Advanture";
            PlayerSettings.productName="Yard 07";
            PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth=1280;
            PlayerSettings.defaultScreenHeight=800;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.resizableWindow=true;
            Physics.SyncTransforms();
            EditorSceneManager.SaveScene(scene,ScenePath);
            // 场景保存后才写构建列表，确保 Unity 已生成场景的资源标识。
            EditorBuildSettings.scenes=new[] { new EditorBuildSettingsScene(ScenePath,true) };
            AssetDatabase.SaveAssets();
            CapturePreview(camera);
            Debug.Log("YARD07_BUILD_OK: scene="+ScenePath+"; boxColliders="+collisionRoot.GetComponentsInChildren<BoxCollider>().Length);
        }

        private static void ConfigureImporter(string path)
        {
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.globalScale=1;
            importer.useFileScale=true;
            importer.bakeAxisConversion=true;
            importer.importAnimation=false;
            importer.importCameras=false;
            importer.importLights=false;
            importer.addCollider=false;
            importer.SaveAndReimport();
        }

        private static Dictionary<string,Material> CreatePalette()
        {
            var colors=new Dictionary<string,string>
            {
                {"Ground","738183"},{"Concrete","C4BCAB"},{"Sand","C8BA9A"},{"Roof","526674"},
                {"Teal","378E8C"},{"Rust","C5684B"},{"Wood","BF9457"},{"WoodLight","DEC494"},
                {"Dark","293E49"},{"Ivory","EEE7D6"},{"LightMetal","90A9AB"},{"Ink","61513C"},
                {"Yellow","E3B856"},{"WarmLight","FFF0C8"},{"SpawnBlue","467F92"},{"SpawnRed","B8654F"}
            };
            var shader=Shader.Find("Standard");
            if(shader==null) throw new InvalidOperationException("需要 Unity 内置渲染管线的 Standard 着色器。");
            var result=new Dictionary<string,Material>();
            foreach(var pair in colors)
            {
                string path=MaterialPath+pair.Key+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(material==null)
                {
                    material=new Material(shader) { name=pair.Key };
                    AssetDatabase.CreateAsset(material,path);
                }
                ColorUtility.TryParseHtmlString("#"+pair.Value,out Color color);
                material.color=color;
                material.SetFloat("_Glossiness",.18f);
                material.SetFloat("_Metallic",0);
                EditorUtility.SetDirty(material);
                result.Add(pair.Key,material);
            }
            return result;
        }

        private static void ConfigureLighting()
        {
            var sun=new GameObject("Sun").AddComponent<Light>();
            sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(48,-35,0);
            sun.color=new Color(1,.94f,.82f);
            sun.intensity=1.1f;
            sun.shadows=LightShadows.Soft;
            sun.shadowBias=.03f;
            sun.shadowNormalBias=.25f;
            RenderSettings.sun=sun;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.62f,.72f,.80f);
            RenderSettings.ambientEquatorColor=new Color(.48f,.53f,.54f);
            RenderSettings.ambientGroundColor=new Color(.28f,.30f,.30f);
            RenderSettings.reflectionIntensity=.3f;
            var sky=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath+"Sky.mat");
            if(sky==null)
            {
                sky=new Material(Shader.Find("Skybox/Procedural")) { name="Sky" };
                AssetDatabase.CreateAsset(sky,MaterialPath+"Sky.mat");
            }
            sky.SetColor("_SkyTint",new Color(.54f,.62f,.68f));
            sky.SetColor("_GroundColor",new Color(.42f,.45f,.43f));
            sky.SetFloat("_AtmosphereThickness",.8f);
            sky.SetFloat("_Exposure",1.15f);
            RenderSettings.skybox=sky;
            QualitySettings.antiAliasing=4;
            QualitySettings.shadows=ShadowQuality.All;
            QualitySettings.shadowResolution=ShadowResolution.High;
            QualitySettings.shadowDistance=70;
            QualitySettings.shadowCascades=2;
        }

        private static void CapturePreview(Camera camera)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) return;
            var target=new RenderTexture(1280,800,24);
            var old=RenderTexture.active;
            var original=camera.targetTexture;
            var pixels=new Texture2D(1280,800,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;
                camera.Render();
                RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1280,800),0,0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(ArtifactPath,"unity-first-person.png"),pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=original;
                RenderTexture.active=old;
                UnityEngine.Object.DestroyImmediate(pixels);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>命令行打开工程后进入试玩；不重建地图，保留已保存的场景编辑。</summary>
        public static void OpenForPlay()
        {
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.delayCall += () =>
            {
                var gameView=Type.GetType("UnityEditor.GameView,UnityEditor");
                if(gameView!=null) EditorWindow.GetWindow(gameView).Focus();
                EditorApplication.isPlaying=true;
            };
        }
    }
}
