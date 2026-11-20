using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.Tactical.Simulation;
using Advanture.TacticalGame.NetworkView;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Advanture.TacticalGame.Editor
{
    public static class FortifiedCompoundBuilder
    {
        public const string ScenePath = "Assets/TacticalGame/Scenes/FortifiedCompound.unity";
        public const string Art = "Assets/TacticalGame/Art/FortifiedCompound/";
        private static string Repository => Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));

        [Serializable] private class Palette { public Surface[] materials; }
        [Serializable] private class Surface
        {
#pragma warning disable 649
            public string id, title, tile;
            public float[] dark, light, paint;
            public float band, meters, low, high, rough, metal;
            public bool plaza;
#pragma warning restore 649
        }

        [MenuItem("Tactical Game/Rebuild Fortified Compound")]
        public static void BuildScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Art+"FortifiedCompound.fbx");
            importer.bakeAxisConversion = true;
            importer.useFileScale = true;
            importer.globalScale = 1;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.importAnimation = importer.importCameras = importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();

            var materials = ImportSurfaces();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Art+"FortifiedCompound.fbx");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            // Establish the same metre grid as the deterministic simulation using exported Blender datums.
            var nodes = model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
            var origin = nodes["AxisOrigin"];
            var north = nodes["AxisNorth"];
            var east = nodes["AxisEast"];
            var up = nodes["AxisUp"];
            var root = new GameObject("Fortified Compound");
            model.transform.SetParent(root.transform,true);
            root.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(north.position-origin.position,up.position-origin.position));
            root.transform.localScale = Vector3.one * (10f / Vector3.Distance(north.position,origin.position));
            root.transform.position -= origin.position;
            if (Vector3.Distance(east.position,Vector3.right*10)>.02f || Vector3.Distance(up.position,Vector3.up*10)>.02f)
                throw new InvalidDataException("FBX axes do not match the authored map: east="+east.position+" up="+up.position);

            foreach (var mesh in model.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = mesh.GetComponent<MeshRenderer>();
                if (mesh.name.StartsWith("COL_",StringComparison.Ordinal))
                {
                    renderer.enabled=false;
                    mesh.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh.sharedMesh;
                }
                else
                {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(mat=>materials[mat.name]).ToArray();
                    renderer.shadowCastingMode=ShadowCastingMode.On;
                    renderer.receiveShadows=true;
                }
            }

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Art+"FortifiedCompound.bytes");
            var map = TacticalMap.Decode(asset.bytes);
            var camera = new GameObject("Player Camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();
            camera.tag="MainCamera";
            camera.fieldOfView=74; camera.nearClipPlane=.045f; camera.farClipPlane=230;
            // Before room entry the lobby overlooks the same plaza used by gameplay.
            camera.transform.position=new Vector3(-15,20,-19);
            camera.transform.LookAt(new Vector3(-4,3,19));
            var room = new GameObject("Fortified Compound Room",typeof(NetworkInputSource),typeof(NetworkYardController),typeof(NetworkRoomOverlay),typeof(YardLaunchArguments));
            room.GetComponent<NetworkYardController>().Configure(asset,camera,"要塞基地",true);

            var sun = new GameObject("Afternoon sun").AddComponent<Light>();
            sun.type=LightType.Directional; sun.color=new Color(1f,.945f,.845f);
            sun.intensity=1.20f; sun.shadows=LightShadows.Soft; sun.shadowStrength=.70f;
            sun.shadowBias=.035f; sun.shadowNormalBias=.24f;
            sun.transform.rotation=Quaternion.Euler(44,-37,0);
            RenderSettings.sun=sun;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.72f,.81f,.9f);
            RenderSettings.ambientEquatorColor=new Color(.60f,.63f,.64f);
            RenderSettings.ambientGroundColor=new Color(.43f,.40f,.34f);
            RenderSettings.skybox=AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.fog=false;
            Lightmapping.giWorkflowMode=Lightmapping.GIWorkflowMode.OnDemand;
            Physics.SyncTransforms();
            ValidateSpawns(map);
            if (!EditorSceneManager.SaveScene(scene,ScenePath)) throw new IOException("Could not save "+ScenePath);
            RegisterScene(ScenePath,true);
            AssetDatabase.SaveAssets();
            Debug.Log("COMPOUND_SCENE_OK meshes="+model.GetComponentsInChildren<MeshFilter>().Length+" slots="+map.Spawns.Count);
        }

        private static Dictionary<string,Material> ImportSurfaces()
        {
            var palette=JsonUtility.FromJson<Palette>(File.ReadAllText(Art+"Surfaces.json"));
            var shader=Shader.Find("Advanture/Fortified Compound");
            if (shader==null || ShaderUtil.ShaderHasError(shader)) throw new InvalidDataException("Compound surface shader failed to import.");
            if (!AssetDatabase.IsValidFolder(Art+"Materials")) AssetDatabase.CreateFolder(Art.TrimEnd('/'),"Materials");
            foreach (var tile in palette.materials.Select(s=>s.tile).Where(t=>!string.IsNullOrEmpty(t)).Distinct())
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(Art+tile+".png");
                importer.sRGBTexture=false; importer.wrapMode=TextureWrapMode.Repeat; importer.anisoLevel=8;
                importer.mipmapEnabled=true; importer.filterMode=FilterMode.Trilinear; importer.SaveAndReimport();
            }
            var materials=new Dictionary<string,Material>();
            foreach (var value in palette.materials)
            {
                string path=Art+"Materials/"+value.id+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat==null) { mat=new Material(shader); AssetDatabase.CreateAsset(mat,path); }
                mat.shader=shader; mat.name=value.title;
                mat.SetColor("_Dark",ColorOf(value.dark)); mat.SetColor("_Light",ColorOf(value.light));
                mat.SetColor("_Paint",ColorOf(value.paint)); mat.SetFloat("_Band",value.band);
                mat.SetFloat("_Meters",value.meters); mat.SetVector("_Range",new Vector4(value.low,value.high,0,0));
                mat.SetFloat("_Plaza",value.plaza?1:0); mat.SetFloat("_Roughness",value.rough);
                mat.SetFloat("_Metallic",value.metal); mat.SetFloat("_HasScan",string.IsNullOrEmpty(value.tile)?0:1);
                if (!string.IsNullOrEmpty(value.tile)) mat.SetTexture("_Scan",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+value.tile+".png"));
                EditorUtility.SetDirty(mat); materials.Add(value.id,mat);
            }
            return materials;
        }
        private static Color ColorOf(float[] rgb) => new Color(rgb[0],rgb[1],rgb[2],1);

        private static void ValidateSpawns(TacticalMap map)
        {
            foreach (var spawn in map.Spawns)
            {
                var feet=new Vector3(spawn.X/1000f,map.GroundHeight(spawn.X,spawn.Z)/1000f,spawn.Z/1000f);
                if (!Physics.Raycast(feet+Vector3.up,Vector3.down,out var hit,2f) || !hit.collider.name.Contains("Plaza") ||
                    Mathf.Abs(feet.y-hit.point.y)>.025f || Physics.CheckCapsule(feet+Vector3.up*.37f,feet+Vector3.up*1.53f,.35f))
                    throw new InvalidDataException("广场出生点缺少地面或净空 "+feet+" "+(hit.collider!=null?hit.collider.name:"无碰撞"));
                Debug.Log("COMPOUND_SPAWN feet="+feet.ToString("F3")+" surface="+hit.collider.name+" y="+hit.point.y);
            }
        }
        internal static void RegisterScene(string path,bool enabled)
        {
            var scenes=EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s=>s.path==path)) scenes.Add(new EditorBuildSettingsScene(path,enabled));
            EditorBuildSettings.scenes=scenes.ToArray();
        }

        [MenuItem("Tactical Game/Build Fortified Compound macOS Player")]
        public static void BuildPlayer()
        {
            bool development=Environment.GetCommandLineArgs().Contains("-compound-development");
            string output=Path.Combine(Repository,development?".build/compound-game/dev":"artifacts/fortified-compound-game","FortifiedCompound.app");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string previousName=PlayerSettings.productName;
            BuildReport report;
            try
            {
                PlayerSettings.productName="Fortified Compound";
                report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes=development ? new[]{ScenePath} : new[]{ScenePath,"Assets/HookIsland/Scenes/DrownedBellTown.unity"},
                    locationPathName=output, target=BuildTarget.StandaloneOSX,
                    options=development ? BuildOptions.Development : BuildOptions.None
                });
            }
            finally
            {
                PlayerSettings.productName=previousName;
            }
            if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("要塞基地构建失败 "+report.summary.result);
            Debug.Log("COMPOUND_PLAYER_OK "+output+" bytes="+report.summary.totalSize);
        }
    }
}
