using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Advanture.HookIsland.Editor
{
    public static class HookIslandGameplayBuilder
    {
        [MenuItem("Tools/Hook Island/Set Up Playable Fishing")]
        public static void Setup()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Stop Play first.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=HookIslandSceneBuilder.ScenePath)throw new InvalidOperationException("Open the HookIsland scene first.");
            var go=GameObject.Find("Island Fishing");
            if(go==null){go=new GameObject("Island Fishing");Undo.RegisterCreatedObjectUndo(go,"Set up island fishing");}
            var game=go.GetComponent<IslandFishingController>();
            if(game==null)game=Undo.AddComponent<IslandFishingController>(go);
            if(go.GetComponent<FishingOverlay>()==null)Undo.AddComponent<FishingOverlay>(go);
            if(go.GetComponent<FishingSmokeDriver>()==null)Undo.AddComponent<FishingSmokeDriver>(go);
            var data=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/IslandMap.bytes");
            var camera=GameObject.Find("Island Camera").GetComponent<Camera>();
            game.Configure(data,camera,Shader.Find("Hook Island/Fishing Line"));
            CreatePierSurface(go.transform);
            camera.orthographicSize=15.5f;
            EditorUtility.SetDirty(game);EditorUtility.SetDirty(camera);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject=go;
        }
        private static void CreatePierSurface(Transform parent)
        {
            // A continuous top surface bridges the deliberate gaps between rendered planks.
            const string path="Assets/HookIsland/Data/PierWalkSurface.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool create=mesh==null;
            if(create)mesh=new Mesh{name="Pier continuous walk surface"};else mesh.Clear();
            var vertices=new Vector3[6];var direction=new Vector3(-.62f,0,-.7846018f);
            var side=new Vector3(.7846018f,0,-.62f);var anchor=new Vector3(-7.2666f,0,-7.58693f);
            var distances=new[]{-1.4f,-.6f,3.2f};var heights=new[]{.751f,.8f,.8f};
            for(int i=0;i<3;i++)for(int j=0;j<2;j++)
                vertices[i*2+j]=anchor+direction*distances[i]+side*(j==0?-1.475f:1.475f)+Vector3.up*heights[i];
            mesh.vertices=vertices;mesh.triangles=new[]{0,1,2,1,3,2,2,3,4,3,5,4};mesh.RecalculateNormals();mesh.RecalculateBounds();
            if(create)AssetDatabase.CreateAsset(mesh,path);else EditorUtility.SetDirty(mesh);
            var existing=parent.Find("Pier Walk Surface");var surface=existing!=null?existing.gameObject:new GameObject("Pier Walk Surface");
            surface.transform.SetParent(parent,false);
            var collider=surface.GetComponent<MeshCollider>();
            if(collider==null)collider=surface.AddComponent<MeshCollider>();
            collider.sharedMesh=mesh;
            AssetDatabase.SaveAssets();
        }
    }
}
