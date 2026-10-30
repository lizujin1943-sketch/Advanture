using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Advanture.HookIsland.Editor
{
    public static class HookPlatformSceneBuilder
    {
        public const string ScenePath="Assets/HookIsland/Scenes/HookPlatform.unity";
        public const string SettingsPath="Assets/HookIsland/Data/PlatformSettings.asset";
        private const string Root="Assets/HookIsland";
        [MenuItem("Tools/Hook Island/Create Platform Scene")]
        public static void Create()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Stop Play before creating a scene.");
            if(File.Exists(ScenePath))
            {EditorSceneManager.OpenScene(ScenePath);return;}
            if(Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save the current scene before creating a platform scene.");
            var surface=Shader.Find("Hook Island/Baked Color Surface");
            var oceanShader=Shader.Find("Hook Island/Stylized Ocean");
            if(surface==null || oceanShader==null)throw new InvalidOperationException("Missing island shaders.");
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>(SettingsPath);
            if(settings==null){settings=ScriptableObject.CreateInstance<PlatformSettings>();AssetDatabase.CreateAsset(settings,SettingsPath);}
            var wood=MakeMaterial("Platform Wood",surface,new Color(.64f,.34f,.14f));
            var woodTrim=MakeMaterial("Platform Wood Trim",surface,new Color(.37f,.18f,.07f));
            var bone=MakeMaterial("Platform Bone",surface,new Color(.95f,.89f,.71f));
            var boneTrim=MakeMaterial("Platform Bone Trim",surface,new Color(.74f,.64f,.43f));
            var woodPrefab=MakePrefab(TileKind.Wood,wood,woodTrim,"TileWood");
            var bonePrefab=MakePrefab(TileKind.Fishbone,bone,boneTrim,"TileFishbone");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var sun=new GameObject("Warm Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1;
            sun.color=new Color(1,.95f,.86f);sun.transform.rotation=Quaternion.Euler(52,-35,0);sun.shadows=LightShadows.Soft;
            RenderSettings.sun=sun;RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.25f,.28f,.3f);RenderSettings.fog=false;
            var camera=new GameObject("Platform Camera").AddComponent<Camera>();camera.tag="MainCamera";
            camera.transform.position=new Vector3(0,24,-20);camera.transform.LookAt(new Vector3(0,.45f,0));
            camera.orthographic=true;camera.orthographicSize=9;camera.nearClipPlane=.1f;camera.farClipPlane=180;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.57f,.82f,.82f);
            camera.allowHDR=false;camera.allowMSAA=true;camera.gameObject.AddComponent<AudioListener>();
            camera.gameObject.AddComponent<IslandCameraShadows>();
            IslandAtmosphereInstaller.Configure(camera);
            var ocean=new GameObject("Platform Ocean");ocean.transform.localScale=new Vector3(5,1,5);ocean.AddComponent<MeshFilter>().sharedMesh=
                AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Art/Meshes/OceanGrid.asset");
            var water=ocean.AddComponent<MeshRenderer>();water.sharedMaterial=MakeMaterial("Platform Ocean",oceanShader,Color.white);
            water.sharedMaterial.SetTexture("_ShoreDistance",Texture2D.whiteTexture);
            water.shadowCastingMode=ShadowCastingMode.Off;
            var waves=ocean.AddComponent<IslandOcean>();
            waves.rippleMap=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Art/Textures/OceanRipples.png");
            ocean.AddComponent<IslandOceanReflection>();
            var shoreline=ocean.AddComponent<PlatformShoreline>();
            var preview=new GameObject("Initial platform preview").transform;
            foreach(var tile in PlatformRules.InitialTiles())
            {
                var go=(GameObject)PrefabUtility.InstantiatePrefab(woodPrefab,preview);
                go.transform.localPosition=new Vector3(tile.X*2,0,tile.Z*2);
            }
            var entry=new GameObject("Platform Fishing");var game=entry.AddComponent<IslandFishingController>();
            entry.AddComponent<FishingOverlay>();var construction=entry.AddComponent<PlatformConstruction>();
            construction.Configure(woodPrefab,bonePrefab,preview,shoreline);
            game.ConfigurePlatform(settings,camera,Shader.Find("Hook Island/Fishing Line"));
            File.WriteAllBytes(Root+"/Data/PlatformMap.bytes",settings.CreateMap().Encode());
            EditorUtility.SetDirty(water.sharedMaterial);AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene,ScenePath);InstallFishingEquipment();Selection.activeGameObject=entry;
            if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.LookAt(Vector3.zero,camera.transform.rotation,14);
            Debug.Log("HOOK_PLATFORM_READY: "+ScenePath);
        }
        [MenuItem("Tools/Hook Island/Install Fishing Equipment")]
        public static void InstallFishingEquipment()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Stop Play before installing equipment.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath)throw new InvalidOperationException("Open the platform scene first.");
            var game=UnityEngine.Object.FindObjectOfType<IslandFishingController>();
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>(SettingsPath);
            const string path=Root+"/Data/FishingSettings.asset";
            var fishing=AssetDatabase.LoadAssetAtPath<FishingSettings>(path);
            if(fishing==null){fishing=ScriptableObject.CreateInstance<FishingSettings>();AssetDatabase.CreateAsset(fishing,path);}
            if(fishing.combo==null)
            {
                const string comboPath=Root+"/Data/ComboSettings.asset";
                var combo=AssetDatabase.LoadAssetAtPath<ComboSettings>(comboPath);
                if(combo==null){combo=ScriptableObject.CreateInstance<ComboSettings>();AssetDatabase.CreateAsset(combo,comboPath);}
                fishing.combo=combo;EditorUtility.SetDirty(fishing);
            }
            if(fishing.character==null)
            {
                const string characterPath=Root+"/Data/CharacterSettings.asset";
                var character=AssetDatabase.LoadAssetAtPath<CharacterSettings>(characterPath);
                if(character==null){character=ScriptableObject.CreateInstance<CharacterSettings>();AssetDatabase.CreateAsset(character,characterPath);}
                fishing.character=character;EditorUtility.SetDirty(fishing);
            }
            if(fishing.swimming==null)
            {
                const string swimmingPath=Root+"/Data/SwimmingSettings.asset";
                var swimming=AssetDatabase.LoadAssetAtPath<SwimmingSettings>(swimmingPath);
                if(swimming==null){swimming=ScriptableObject.CreateInstance<SwimmingSettings>();AssetDatabase.CreateAsset(swimming,swimmingPath);}
                fishing.swimming=swimming;EditorUtility.SetDirty(fishing);
            }
            if(fishing.enemies==null)
            {
                const string enemyPath=Root+"/Data/EnemySettings.asset";
                var enemies=AssetDatabase.LoadAssetAtPath<EnemySettings>(enemyPath);
                if(enemies==null){enemies=ScriptableObject.CreateInstance<EnemySettings>();AssetDatabase.CreateAsset(enemies,enemyPath);}
                fishing.enemies=enemies;EditorUtility.SetDirty(fishing);
            }
            if(fishing.revival==null)
            {
                const string revivalPath=Root+"/Data/RevivalSettings.asset";
                var revival=AssetDatabase.LoadAssetAtPath<RevivalSettings>(revivalPath);
                if(revival==null){revival=ScriptableObject.CreateInstance<RevivalSettings>();AssetDatabase.CreateAsset(revival,revivalPath);}
                fishing.revival=revival;EditorUtility.SetDirty(fishing);
            }
            if(fishing.treasure==null)
            {
                const string treasurePath=Root+"/Data/TreasureSettings.asset";
                var treasure=AssetDatabase.LoadAssetAtPath<TreasureSettings>(treasurePath);
                if(treasure==null){treasure=ScriptableObject.CreateInstance<TreasureSettings>();AssetDatabase.CreateAsset(treasure,treasurePath);}
                fishing.treasure=treasure;EditorUtility.SetDirty(fishing);
            }
            if(fishing.commissions==null)
            {
                const string commissionPath=Root+"/Data/CommissionSettings.asset";
                var commissions=AssetDatabase.LoadAssetAtPath<CommissionSettings>(commissionPath);
                if(commissions==null){commissions=ScriptableObject.CreateInstance<CommissionSettings>();AssetDatabase.CreateAsset(commissions,commissionPath);}
                fishing.commissions=commissions;EditorUtility.SetDirty(fishing);
            }
            if(fishing.ocean==null)
            {
                const string oceanPath=Root+"/Data/OceanSettings.asset";
                var ocean=AssetDatabase.LoadAssetAtPath<OceanSettings>(oceanPath);
                if(ocean==null){ocean=ScriptableObject.CreateInstance<OceanSettings>();AssetDatabase.CreateAsset(ocean,oceanPath);}
                fishing.ocean=ocean;EditorUtility.SetDirty(fishing);
            }
            if(game.GetComponent<RevivalController>()==null)game.gameObject.AddComponent<RevivalController>();
            if(fishing.finale==null)
            {
                const string finalePath=Root+"/Data/FinaleSettings.asset";
                var finale=AssetDatabase.LoadAssetAtPath<FinaleSettings>(finalePath);
                if(finale==null){finale=ScriptableObject.CreateInstance<FinaleSettings>();AssetDatabase.CreateAsset(finale,finalePath);}
                fishing.finale=finale;EditorUtility.SetDirty(fishing);
            }
            if(fishing.merchants==null)
            {
                const string merchantPath=Root+"/Data/MerchantSettings.asset";
                var merchants=AssetDatabase.LoadAssetAtPath<MerchantSettings>(merchantPath);
                if(merchants==null){merchants=ScriptableObject.CreateInstance<MerchantSettings>();AssetDatabase.CreateAsset(merchants,merchantPath);}
                fishing.merchants=merchants;EditorUtility.SetDirty(fishing);
            }
            if(game.GetComponent<RevivalOverlay>()==null)game.gameObject.AddComponent<RevivalOverlay>();
            settings.fishing=fishing;EditorUtility.SetDirty(settings);
            if(game.GetComponent<FishingEquipmentController>()==null)game.gameObject.AddComponent<FishingEquipmentController>();
            if(game.GetComponent<FishingEquipmentOverlay>()==null)game.gameObject.AddComponent<FishingEquipmentOverlay>();
            if(GameObject.Find("Platform Merchant")==null)
            {
                var root=new GameObject("Platform Merchant").transform;root.position=new Vector3(0,PlatformGeometry.DeckHeight,1.5f);
                var surface=Shader.Find("Hook Island/Baked Color Surface");
                var wood=MakeMaterial("Merchant Wood",surface,new Color(.52f,.26f,.1f));
                var canvas=MakeMaterial("Merchant Canvas",surface,new Color(.94f,.48f,.12f));
                var trim=MakeMaterial("Merchant Trim",surface,new Color(.95f,.81f,.46f));
                Prop(root,"Counter",new Vector3(0,.28f,0),new Vector3(.9f,.55f,.55f),wood);
                for(int i=-1;i<=1;i+=2)Prop(root,"Canopy post",new Vector3(i*.47f,.65f,.17f),new Vector3(.06f,1.3f,.06f),wood);
                Prop(root,"Awning",new Vector3(0,1.3f,0),new Vector3(1.15f,.1f,.85f),canvas);
                Prop(root,"Counter trim",new Vector3(0,.57f,0),new Vector3(.98f,.07f,.63f),trim);
                for(int i=0;i<3;i++)Prop(root,"Displayed rod",new Vector3(-.25f+i*.23f,.65f,0),new Vector3(.035f,.035f,.5f),trim);
            }
            File.WriteAllBytes(Root+"/Data/PlatformMap.bytes",settings.CreateMap().Encode());
            AssetDatabase.ImportAsset(Root+"/Data/PlatformMap.bytes",ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        private static void Prop(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.transform.SetParent(parent,false);
            part.transform.localPosition=position;part.transform.localScale=scale;part.GetComponent<MeshRenderer>().sharedMaterial=material;
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        }
        private static Material MakeMaterial(string name,Shader shader,Color color)
        {
            string path=Root+"/Art/Materials/"+name.Replace(" ","")+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(shader){name=name};AssetDatabase.CreateAsset(m,path);}
            m.color=color;if(m.HasProperty("_Ambient"))m.SetFloat("_Ambient",.4f);
            if(m.HasProperty("_SunStrength"))m.SetFloat("_SunStrength",.85f);EditorUtility.SetDirty(m);return m;
        }
        private static GameObject MakePrefab(TileKind kind,Material surface,Material trim,string name)
        {
            var temp=PlatformGeometry.CreateTile(null,kind,surface,trim);
            var prefab=PrefabUtility.SaveAsPrefabAsset(temp,Root+"/Prefabs/"+name+".prefab");UnityEngine.Object.DestroyImmediate(temp);return prefab;
        }
    }
}
