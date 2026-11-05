using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Explicit authoring/inspection tool. Never attached to a gameplay scene or included in builds.</summary>
    public sealed class EldritchBestiaryWindow : EditorWindow
    {
        [NonSerialized] private Texture2D[] portraits;
        private Vector2 scroll;
        [MenuItem("Tools/Hook Island/Eldritch Bestiary")]
        public static void Open()=>GetWindow<EldritchBestiaryWindow>("海岛异教图鉴");
        private void OnGUI()
        {
            // Play-mode/domain reload can restore an empty array after preview textures
            // were released. Treat that cache as absent before indexing the roster.
            if(portraits!=null && (portraits.Length!=EldritchCatalog.SpeciesCount || portraits.Any(t=>t==null)))ClearPortraits();
            GUILayout.Label("海岛异教 · 十种敌人",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("正式遭遇由 FishingSettings → EldritchSettings 控制。以下图鉴只预览模型与规则，不修改正在进行的战斗。",MessageType.Info);
            if(portraits==null && GUILayout.Button("生成图鉴预览"))portraits=EldritchCatalog.All.Select(d=>RenderPortrait(d.Kind,360)).ToArray();
            scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var d in EldritchCatalog.All)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                if(portraits!=null)GUILayout.Label(portraits[(int)d.Kind],GUILayout.Width(150),GUILayout.Height(150));
                EditorGUILayout.BeginVertical();GUILayout.Label($"{(int)d.Kind+1:00}  {d.Name}",EditorStyles.boldLabel);
                GUILayout.Label($"生命 {d.Health}  ·  移速 {d.Speed/1000f:0.0} m/s  ·  击杀 {d.Coins} 金币");
                GUILayout.Label($"前摇 {d.WindupTicks/30f:0.0} 秒  ·  恢复 {d.RecoveryTicks/30f:0.0} 秒");
                GUILayout.Label(d.Counter,EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndVertical();EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
        private void OnDisable()=>ClearPortraits();
        private void ClearPortraits(){if(portraits!=null)foreach(var t in portraits)if(t!=null)DestroyImmediate(t);portraits=null;}
        public static Texture2D RenderPortrait(EldritchKind kind,int size)
        {
            var preview=new PreviewRenderUtility();var root=new GameObject("Bestiary portrait");EldritchModel model=null;
            try
            {
                SceneManager.MoveGameObjectToScene(root,preview.camera.scene);
                model=new EldritchModel(root.transform,kind);
                model.Animate(new EldritchCreature{Id=(int)kind,Kind=kind,Phase=EldritchPhase.Hunting},60);
                preview.camera.transform.position=new Vector3(2.7f,1.9f,4);preview.camera.transform.LookAt(new Vector3(0,.9f,0));
                preview.camera.orthographic=true;preview.camera.orthographicSize=1.28f;preview.camera.nearClipPlane=.05f;preview.camera.farClipPlane=30;
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.035f,.075f,.1f);
                preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-25,0);preview.lights[1].intensity=.8f;
                preview.ambientColor=new Color(.45f,.5f,.55f);
                preview.BeginStaticPreview(new Rect(0,0,size,size));preview.Render(true);return preview.EndStaticPreview();
            }
            finally{model?.Dispose();DestroyImmediate(root);preview.Cleanup();}
        }
        [MenuItem("Tools/Hook Island/Validate Eldritch Bestiary")]
        public static void ValidateAndExport()
        {
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset");var map=settings.CreateMap();
            if(!map.Fishing.Eldritch.Enabled || map.Fishing.Eldritch.SpeciesMask!=1023)throw new InvalidOperationException("Formal ten-species roster is not enabled.");
            if(!map.Encode().SequenceEqual(IslandMap.Decode(map.Encode()).Encode()))throw new InvalidOperationException("Formal map round trip failed.");
            File.WriteAllBytes("Assets/HookIsland/Data/PlatformMap.bytes",map.Encode());
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../artifacts/eldritch"));Directory.CreateDirectory(folder);
            var sheet=new Texture2D(2000,800,TextureFormat.RGB24,false);
            foreach(var d in EldritchCatalog.All)
            {
                var image=RenderPortrait(d.Kind,400);File.WriteAllBytes(Path.Combine(folder,$"{(int)d.Kind+1:00}-{d.Kind}.png"),image.EncodeToPNG());
                sheet.SetPixels((int)d.Kind%5*400,(1-(int)d.Kind/5)*400,400,400,image.GetPixels());DestroyImmediate(image);
            }
            sheet.Apply();File.WriteAllBytes(Path.Combine(folder,"bestiary-contact-sheet.png"),sheet.EncodeToPNG());DestroyImmediate(sheet);
            AssetDatabase.ImportAsset("Assets/HookIsland/Data/PlatformMap.bytes",ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("ELDRITCH_UNITY_OK species=10 portraits=10 formal_map=true map_round_trip=true");
        }
    }
}
