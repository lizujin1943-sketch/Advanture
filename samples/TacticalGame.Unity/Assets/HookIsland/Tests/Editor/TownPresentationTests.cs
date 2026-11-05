using System;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class TownPresentationTests
    {
        [Test] public void FormalSceneBindsTownSettingsAndKeepsLegacyMode()
        {
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/TownPlatformSettings.asset");
            Assert.That(settings.CreateMap().IsTown,Is.True);
            Assert.That(settings.CreateMap().Encode(),Is.EqualTo(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/TownMap.bytes").bytes));
            Assert.That(settings.fishing.boss.modelPrefab,Is.Not.Null);Assert.That(settings.fishing.eldritch,Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset").CreateMap().IsTown,Is.False);
            Assert.That(EditorBuildSettings.scenes.First(s=>s.enabled).path,Is.EqualTo("Assets/HookIsland/Scenes/DrownedBellTown.unity"));
            var scene=EditorSceneManager.OpenScene("Assets/HookIsland/Scenes/DrownedBellTown.unity",OpenSceneMode.Additive);
            try
            {
                var components=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true)).ToArray();
                Assert.That(components.OfType<IslandFishingController>().Count(),Is.EqualTo(1));
                Assert.That(components.OfType<Camera>().Count(),Is.EqualTo(1));Assert.That(components.OfType<Light>().Any(l=>l.type==LightType.Directional),Is.True);
                Assert.That(components.OfType<PlatformConstruction>(),Is.Empty);
                Assert.That(components.Where(c=>c!=null && c.GetType().Name.Contains("Pilot")),Is.Empty);
            }
            finally{EditorSceneManager.CloseScene(scene,true);}
        }
        [Test] public void StoneMeshLeavesCenterAndAllSixteenRotatedCutsOpen()
        {
            var mesh=Object.Instantiate(AssetDatabase.LoadAssetAtPath<Mesh>("Assets/HookIsland/Art/Meshes/TownGround.asset"));
            try
            {
                var empty=Array.Empty<PlatformTile>();
                foreach(var v in mesh.vertices)Assert.That(TownLayout.OnGround(empty,Mathf.RoundToInt(v.x*1000),Mathf.RoundToInt(v.z*1000)),Is.True);
                var vertices=mesh.vertices;var indices=mesh.triangles;
                bool Covered(float x,float z)
                {for(int i=0;i<indices.Length;i+=6){var a=vertices[indices[i]];var b=vertices[indices[i+2]];if(x>a.x && x<b.x && z>a.z && z<b.z)return true;}return false;}
                Assert.That(Covered(-8,-12),Is.False);
                foreach(var s in TownLayout.WoodSegments)Assert.That(Covered(s.X/1000f,s.Z/1000f),Is.False,"wood slot must not be masked by stone");
                Assert.That(Covered(-8.1f,9.1f),Is.True,"permanent outer ring");
                Assert.That(Covered(-8.1f,-22.1f),Is.True,"southern firing stone");
            }
            finally{Object.DestroyImmediate(mesh);}
        }
        [Test] public void DynamicTerrainRemovesRestoresAndAddsConfirmedBridge()
        {
            var root=new GameObject("town terrain test");
            try
            {
                using(var view=new TownPresentation(root.transform))
                {
                    var s=TownLayout.WoodSegments[0];var wood=root.transform.GetChild(0).GetChild(0);
                    Assert.That(Vector3.Distance(wood.position,TownPresentation.SegmentPosition(s)),Is.LessThan(.001f));
                    Assert.That(Vector3.Distance(wood.forward,new Vector3(s.Direction.X,0,s.Direction.Z).normalized),Is.LessThan(.00001f));
                    var initial=TownLayout.InitialTiles();var removed=initial.Where(t=>t.X!=s.CellX || t.Z!=s.CellZ).ToArray();
                    view.Synchronize(State(removed));Assert.That(wood.gameObject.activeSelf,Is.False);
                    view.Synchronize(State(initial.Concat(TownLayout.ReturnBridgeTiles()).ToArray()));Assert.That(wood.gameObject.activeSelf,Is.True);
                    Assert.That(root.transform.GetChild(0).childCount,Is.GreaterThan(16));view.Reset();
                    Assert.That(root.transform.GetChild(0).Cast<Transform>().Count(t=>t.gameObject.activeSelf),Is.EqualTo(16));
                }
                Assert.That(root.transform.childCount,Is.Zero);
            }
            finally{Object.DestroyImmediate(root);}
        }
        [Test] public void NineteenSlotsUseAuthoredKindsAndPausedUnitsDisappear()
        {
            var root=new GameObject("town creatures test");
            var creatures=Enumerable.Range(0,19).Select(i=>new EldritchCreature{Id=i,Kind=TownEncounters.Kind(i),TownAdult=i<15,Phase=EldritchPhase.Hunting,Health=100,DirectionZ=1000000,X=-55700,Z=-47700,Paused=i==18}).ToArray();
            try
            {
                using(var view=new EldritchPresentation(root.transform,Shader.Find("Hook Island/Fishing Line"),new EldritchConfig(),true))
                {
                    view.Synchronize(new HookState(1,Array.Empty<Fisher>(),Array.Empty<Fish>(),0,TownLayout.InitialTiles(),eldritch:new EldritchState(creatures),town:new TownState(true)));
                    Assert.That(root.transform.childCount,Is.EqualTo(19));
                    for(int i=0;i<19;i++)
                    {Assert.That(root.transform.GetChild(i).name,Does.Contain(EldritchCatalog.Get(TownEncounters.Kind(i)).Name));Assert.That(root.transform.GetChild(i).gameObject.activeSelf,Is.EqualTo(i!=18));}
                    Assert.That(root.transform.GetChild(0).position.y,Is.EqualTo(.45f).Within(.001f));
                }
            }
            finally{Object.DestroyImmediate(root);}
        }
        [Test] public void FailedServiceReceiptStopsRetryUntilExplicitNewPress()
        {
            var buffer=new TownRequestBuffer();Assert.That(buffer.TryQueue(5,TownAction.BuyAmmo),Is.True);
            for(int i=0;i<3;i++){var input=buffer.Compose(new HookInput(100,0,0,1000,fire:true,reload:true));Assert.That(input.Town.RequestId,Is.EqualTo(6));Assert.That(input.Fire || input.Reload || input.Shopping,Is.False);}
            Assert.That(buffer.Observe(new TownPersonalState{LastRequest=6,LastResult=TownResult.TooFar},out var result),Is.True);Assert.That(result,Is.EqualTo(TownResult.TooFar));
            Assert.That(buffer.Compose(default).Town.IsEmpty,Is.True);Assert.That(buffer.TryQueue(6,TownAction.BuyAmmo),Is.True);Assert.That(buffer.Command.RequestId,Is.EqualTo(7));
        }
        [Test] public void TownLockedAttackBonesAndInkClockMatchAuthority()
        {
            var root=new GameObject("town boss test");root.transform.position=new Vector3(-8,0,-12);
            var settings=AssetDatabase.LoadAssetAtPath<BossSettings>("Assets/HookIsland/Data/BossSettings.asset");
            try
            {
                using(var view=new BossAnimationView(root.transform,settings))
                {
                    var joints=root.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
                    for(int scenario=0;scenario<3;scenario++)
                    {
                        var limbs=Enumerable.Range(0,8).Select(i=>new BossLimbState(i,300,300)).ToArray();
                        limbs[0]=new BossLimbState(0,300,300,BossAttack.Slam,45,attackX:342,attackZ:-940);
                        limbs[2]=new BossLimbState(2,300,300,BossAttack.Sweep,47,attackX:-866,attackZ:-500);
                        var boss=new BossState(BossPhase.Combat,2000,2400,1,phaseTicks:67,limbs:limbs,townArena:true,inkWindupTicks:scenario==0?30:scenario==1?60:100);
                        view.Synchronize(boss,(ulong)(100+scenario));var proxies=BossGeometry.Proxies(boss);
                        foreach(int id in new[]{0,2})
                        {
                            var points=proxies.Where(p=>p.LimbId==id).ToArray();
                            for(int j=0;j<points.Length-1;j++)
                            {var p=points[j];Assert.That(Vector3.Distance(joints["DEF_A0"+(id+1)+"_"+j.ToString("00")].position,new Vector3(p.X,p.Y,p.Z)/1000f),Is.LessThan(.035f),"locked bone "+id+"/"+j);}
                        }
                        var eye=proxies.Single(p=>p.Eye);var skin=joints["AW_Eye_Center"].GetComponent<SkinnedMeshRenderer>();var baked=new Mesh();
                        try{skin.BakeMesh(baked);var bounds=new Bounds(skin.transform.TransformPoint(baked.vertices[0]),Vector3.zero);foreach(var v in baked.vertices)bounds.Encapsulate(skin.transform.TransformPoint(v));Assert.That(Vector3.Distance(bounds.center,new Vector3(eye.X,eye.Y,eye.Z)/1000f),Is.LessThan(.008f));}finally{Object.DestroyImmediate(baked);}
                    }
                }
            }
            finally{Object.DestroyImmediate(root);}
        }
        private static HookState State(PlatformTile[] tiles)=>new HookState(1,Array.Empty<Fisher>(),Array.Empty<Fish>(),0,tiles,town:new TownState(true));
    }
}
