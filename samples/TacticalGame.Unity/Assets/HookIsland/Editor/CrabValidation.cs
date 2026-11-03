using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Explicit editor verification only; never included in a player build or attached to the game scene.</summary>
    public static class CrabValidation
    {
        public static void Run()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset");
            var map=settings.CreateMap();if(!map.Fishing.Crabs.Enabled)throw new Exception("Formal crab config is disabled.");
            if(!map.Encode().SequenceEqual(IslandMap.Decode(map.Encode()).Encode()))throw new Exception("Map round trip failed.");
            File.WriteAllBytes("Assets/HookIsland/Data/PlatformMap.bytes",map.Encode());
            var prefab=Resources.Load<GameObject>("Enemies/ShoreCrab");if(prefab==null)throw new Exception("Crab FBX missing.");
            var transforms=prefab.GetComponentsInChildren<Transform>(true);
            if(transforms.Count(t=>t.name.StartsWith("Leg_"))!=8 || transforms.Count(t=>t.name.StartsWith("Claw_"))!=2 || transforms.Count(t=>t.name.StartsWith("Pincer_"))!=4)
                throw new Exception("Crab articulated joints missing.");
            if(prefab.GetComponentsInChildren<Collider>(true).Length!=0)throw new Exception("Crab model contains gameplay colliders.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var parent=new GameObject("Crab visual validation");
            var camera=new GameObject("Preview camera").AddComponent<Camera>();camera.transform.position=new Vector3(2.2f,2.2f,-3);
            camera.transform.LookAt(new Vector3(0,.75f,0));camera.orthographic=true;camera.orthographicSize=1.15f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.16f,.19f);
            var view=new CrabPresentation(parent.transform,Shader.Find("Sprites/Default"),map.Fishing.Crabs);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../artifacts/crabs"));Directory.CreateDirectory(output);
            foreach(var phase in new[]{CrabPhase.Climbing,CrabPhase.Chasing,CrabPhase.Windup,CrabPhase.Pinching,CrabPhase.Recovering,CrabPhase.Dead})
            {
                var crab=new CrabState{Generation=1,Phase=phase,Health=phase==CrabPhase.Dead?0:75,DirectionZ=1000000,ShoreZ=-1000,
                    Timer=phase==CrabPhase.Dead?map.Fishing.Crabs.RespawnTicks-9:phase==CrabPhase.Climbing?22:phase==CrabPhase.Pinching?2:1};
                view.Clear();view.Synchronize(new HookState(100,Array.Empty<Fisher>(),Array.Empty<Fish>(),0,crab:crab));
                var render=new RenderTexture(800,700,24);camera.targetTexture=render;camera.Render();
                var previous=RenderTexture.active;RenderTexture.active=render;var image=new Texture2D(800,700,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,800,700),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,"unity-crab-"+phase+".png"),image.EncodeToPNG());
                RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(render);
            }
            view.Dispose();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("CRAB_UNITY_OK formal_config=true articulated_joints=14 phases=6 map_round_trip=true");
        }
    }
}
