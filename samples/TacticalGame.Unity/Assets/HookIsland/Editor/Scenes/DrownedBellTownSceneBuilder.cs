using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Reproducible formal scene. Every playable surface and obstruction is derived from TownLayout.</summary>
    public static class DrownedBellTownSceneBuilder
    {
        public const string ScenePath="Assets/HookIsland/Scenes/DrownedBellTown.unity";
        public const string SettingsPath="Assets/HookIsland/Data/TownPlatformSettings.asset";
        private const string Root="Assets/HookIsland";
        [MenuItem("Tools/Hook Island/Create Drowned Bell Town")]
        public static void Create()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Stop Play before rebuilding town.");
            var fishing=Asset<FishingSettings>("TownFishingSettings");
            EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<FishingSettings>(Root+"/Data/FishingSettings.asset"),fishing);
            fishing.name="TownFishingSettings";fishing.startingCoins=300;EditorUtility.SetDirty(fishing);
            var settings=Asset<PlatformSettings>("TownPlatformSettings");settings.drownedBellTown=true;settings.fishing=fishing;EditorUtility.SetDirty(settings);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var sun=new GameObject("Town warm sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.05f;sun.color=new Color(1,.91f,.76f);sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(52,-35,0);
            RenderSettings.sun=sun;RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.29f,.36f,.37f);RenderSettings.fog=false;
            var camera=new GameObject("Town Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.farClipPlane=300;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.23f,.48f,.52f);camera.allowHDR=false;
            new FishingOverheadCamera(camera,new FishingOverheadSettings());camera.transform.position=new Vector3(-55,24,-67);camera.gameObject.AddComponent<AudioListener>();camera.gameObject.AddComponent<IslandCameraShadows>();IslandAtmosphereInstaller.Configure(camera);
            var sky=Material("TownSky",Color.white,"Hook Island/Cartoon Sky");
            sky.SetColor("_SkyHorizonColor",new Color(.38f,.63f,.65f));sky.SetColor("_SkyMiddleColor",new Color(.42f,.68f,.75f));sky.SetColor("_SkyZenithColor",new Color(.18f,.42f,.58f));sky.SetColor("_HorizonGlowColor",new Color(.62f,.79f,.76f));sky.SetColor("_CloudShadowColor",new Color(.48f,.63f,.65f));sky.SetFloat("_CloudCoverage",.45f);RenderSettings.skybox=sky;EditorUtility.SetDirty(sky);
            var ocean=new GameObject("Continuous town water");ocean.transform.localScale=new Vector3(8,1,8);ocean.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Art/Meshes/OceanGrid.asset");
            var water=Material("TownWater",new Color(.13f,.47f,.48f),"Hook Island/Stylized Ocean");water.SetTexture("_ShoreDistance",Texture2D.whiteTexture);water.SetFloat("_HorizonStart",130);water.SetFloat("_HorizonEnd",280);ocean.AddComponent<MeshRenderer>().sharedMaterial=water;
            var waves=ocean.AddComponent<IslandOcean>();waves.rippleMap=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Art/Textures/OceanRipples.png");ocean.AddComponent<IslandOceanReflection>();
            var stone=Material("TownStone",new Color(.46f,.54f,.51f));var wall=Material("TownWalls",new Color(.64f,.69f,.62f));var dark=Material("TownTimber",new Color(.26f,.19f,.14f));var gold=Material("TownBrass",new Color(.84f,.62f,.23f));
            var ground=new GameObject("Authoritative stone streets and arena");var mesh=BuildGroundMesh();string meshPath=Root+"/Art/Meshes/TownGround.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(existing!=null){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,meshPath);
            ground.AddComponent<MeshFilter>().sharedMesh=mesh;ground.AddComponent<MeshRenderer>().sharedMaterial=stone;
            var geometry=new GameObject("Town buildings and authoritative cover").transform;
            foreach(var o in TownLayout.Obstacles)
                Part(geometry,"Authority obstacle",new Vector3(o.X/1000f,.45f+o.Height/2000f,o.Z/1000f),new Vector3(o.Width/1000f,o.Height/1000f,o.Depth/1000f),o.Height>=5000?wall:dark);
            string[] names={"01 HARBOR","02 ARMORY","03 CLINIC","04 SWIM GEAR","05 WORKSHOP","06 CONTRACTS","07 PAWNSHOP","08 CHARTS","09 TIDE GATES","10 INN","11 ARCHIVE","12 CHAPEL"};
            foreach(var b in TownLayout.Buildings)
            {
                var house=new GameObject(names[b.Id-1]).transform;house.SetParent(geometry,false);
                var color=Color.HSVToRGB((b.Id*.087f)%1,.4f,.65f);var roof=Material("TownRoof"+b.Id,color);
                if(!b.Enterable && b.Kind!=TownBuildingKind.Dock)
                {
                    Part(house,"Roof ridge",new Vector3(b.X/1000f,5.75f,b.Z/1000f),new Vector3(b.Width/1000f+.6f,.65f,b.Depth/1000f+.6f),roof);
                    Part(house,"Roof cap",new Vector3(b.X/1000f,6.2f,b.Z/1000f),new Vector3(b.Width/1000f+.2f,.3f,b.Depth/1600f),roof);
                }
                // Open interiors deliberately have no roof; authoritative south door stays 2.8m wide.
                var entry=new Vector3(b.Entrance.X/1000f,.48f,b.Entrance.Z/1000f);
                Part(house,"Door service marker",entry+Vector3.up*.04f,new Vector3(2,.08f,.7f),gold);
                Part(house,"Service sign",entry+new Vector3(0,2.9f,.65f),new Vector3(3.2f,.65f,.18f),dark);
                Label(house,names[b.Id-1],entry+new Vector3(0,2.9f,.54f),.12f,true);
                if(b.Enterable)
                {
                    Part(house,"Interior wall carving",new Vector3(b.X/1000f,3,b.Z/1000f+b.Depth/2000f-.3f),new Vector3(b.Width/1000f-1,1.2f,.1f),gold);
                    Label(house,b.Kind==TownBuildingKind.Chapel?"THE BELL WAITS":b.Kind==TownBuildingKind.Archive?"THE THREE SEALS":"SALT LANTERN",new Vector3(b.X/1000f,.5f,b.Z/1000f),.19f);
                }
                // Visible building-specific roof silhouettes, purely decorative above solid footprints.
                if(b.Kind==TownBuildingKind.Workshop || b.Kind==TownBuildingKind.Engine)Part(house,"Chimney",new Vector3(b.X/1000f+1,7,b.Z/1000f),new Vector3(1,2.5f,1),dark);
                if(b.Kind==TownBuildingKind.Chapel){Part(house,"Bell arch",new Vector3(b.X/1000f,6,b.Z/1000f+b.Depth/2000f),new Vector3(2.5f,2,.65f),gold);}
                if(b.Kind==TownBuildingKind.Clinic){Part(house,"Clinic cross vertical",new Vector3(b.X/1000f,3,b.Z/1000f+b.Depth/2000f+.3f),new Vector3(.35f,1.6f,.2f),gold);Part(house,"Clinic cross horizontal",new Vector3(b.X/1000f,3,b.Z/1000f+b.Depth/2000f+.31f),new Vector3(1.5f,.35f,.2f),gold);}
            }
            foreach(var a in TownLayout.EncounterAreas)Label(geometry,"DISTRICT "+a.Name,new Vector3(a.X/1000f,.5f,a.Z/1000f+a.Depth/2000f-1),.35f);
            Label(geometry,"G  DROWNED BELL",new Vector3(-8,.5f,10),.3f);
            var power=TownLayout.EncounterAreas[2];Label(geometry,"C  POWER / E",new Vector3(power.X/1000f,4.2f,power.Z/1000f),.2f);
            foreach(var port in TownLayout.SpawnPorts)Part(geometry,"Hidden emergence port",new Vector3(port.X/1000f,.47f,port.Z/1000f),new Vector3(1.4f,.08f,1.4f),dark);
            foreach(var segment in TownLayout.WoodSegments)if(segment.SegmentId==3)
            {var d=segment.Direction;var at=new Vector3(TownLayout.CenterX/1000f+d.X*.021f,.6f,TownLayout.CenterZ/1000f+d.Z*.021f);Part(geometry,"Repair winch "+(segment.BandId+1),at,new Vector3(.6f,.3f,.6f),gold);}
            var entryObject=new GameObject("Drowned Bell Town Session");var game=entryObject.AddComponent<IslandFishingController>();
            entryObject.AddComponent<FishingOverlay>();entryObject.AddComponent<FishingEquipmentController>();entryObject.AddComponent<FishingEquipmentOverlay>();entryObject.AddComponent<RevivalController>();entryObject.AddComponent<RevivalOverlay>();
            game.ConfigurePlatform(settings,camera,Shader.Find("Hook Island/Fishing Line"));
            File.WriteAllBytes(Root+"/Data/TownMap.bytes",settings.CreateMap().Encode());AssetDatabase.ImportAsset(Root+"/Data/TownMap.bytes");
            EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)}.Concat(EditorBuildSettings.scenes.Where(s=>s.path!=ScenePath)).ToArray();
            Selection.activeGameObject=entryObject;Debug.Log("DROWNED_BELL_TOWN_READY: "+ScenePath);
        }
        public static Mesh BuildGroundMesh()
        {
            const int step=500,nx=320,nz=256;var empty=Array.Empty<PlatformTile>();var ground=new bool[nx+1,nz+1];
            for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)ground[x,z]=TownLayout.OnGround(empty,-80000+x*step,-64000+z*step);
            var v=new List<Vector3>();var t=new List<int>();
            // Merge runs, retain a conservative half-metre contour; never cover the water hole or wooden cuts.
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
            {
                bool Cell(int a)=>ground[a,z] && ground[a+1,z] && ground[a,z+1] && ground[a+1,z+1];
                if(!Cell(x))continue;int first=x;while(x+1<nx && Cell(x+1))x++;
                float x0=-80+first*.5f,x1=-80+(x+1)*.5f,z0=-64+z*.5f,z1=z0+.5f;int i=v.Count;
                v.Add(new Vector3(x0,.45f,z0));v.Add(new Vector3(x0,.45f,z1));v.Add(new Vector3(x1,.45f,z1));v.Add(new Vector3(x1,.45f,z0));t.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
            }
            var mesh=new Mesh{name="Town authoritative stone surface",indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static T Asset<T>(string name) where T:ScriptableObject
        {string path=Root+"/Data/"+name+".asset";var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset==null){asset=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(asset,path);}return asset;}
        private static Material Material(string name,Color color,string shader="Hook Island/Baked Color Surface")
        {string path=Root+"/Art/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}m.color=color;if(m.HasProperty("_Ambient"))m.SetFloat("_Ambient",.5f);EditorUtility.SetDirty(m);return m;}
        private static void Part(Transform parent,string name,Vector3 position,Vector3 size,Material material)=>PlatformGeometry.Part(parent,name,position,size,material);
        private static void Label(Transform parent,string text,Vector3 at,float size,bool upright=false)
        {var go=new GameObject(text);go.transform.SetParent(parent,false);go.transform.position=at;go.transform.rotation=Quaternion.Euler(upright?0:70,0,0);var label=go.AddComponent<TextMesh>();label.text=text;label.characterSize=size;label.fontSize=48;label.anchor=TextAnchor.MiddleCenter;label.color=new Color(1,.91f,.57f);}
    }
}
