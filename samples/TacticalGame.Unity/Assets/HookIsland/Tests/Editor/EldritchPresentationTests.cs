using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class EldritchPresentationTests
    {
        [Test] public void BlenderCatalogueHasTenSkinnedModelsAndEightyDeformingClips()
        {
            var catalog=EldritchVisualCatalog.LoadRequired();
            Assert.That(catalog.Complete,Is.True);
            foreach(var entry in catalog.species)
            {
                var instance=Object.Instantiate(entry.model);
                var baked=new Mesh();
                try
                {
                    var animator=instance.GetComponentInChildren<Animator>();
                    var skin=instance.GetComponentInChildren<SkinnedMeshRenderer>();
                    Assert.That(animator,Is.Not.Null);Assert.That(skin,Is.Not.Null);
                    Assert.That(instance.GetComponentsInChildren<Collider>(),Is.Empty);
                    Assert.That(skin.bones,Is.Not.Empty);Assert.That(skin.bones.All(b=>b!=null),Is.True);
                    Assert.That(skin.sharedMesh.bindposes.Length,Is.EqualTo(skin.bones.Length));
                    Assert.That(skin.quality,Is.EqualTo(SkinQuality.Bone2));
                    var imported=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HookIsland/Art/Eldritch/Models/"+entry.kind+".fbx")
                        .GetComponentInChildren<SkinnedMeshRenderer>();
                    Assert.That(skin.sharedMaterials.Select(m=>m.name),Is.EqualTo(imported.sharedMaterials.Select(m=>m.name)),
                        "FBX material order must survive Unity's submesh reordering.");
                    var facing=instance.GetComponentsInChildren<Transform>().Single(t=>t.name=="Facing");
                    var origin=instance.GetComponentsInChildren<Transform>().Single(t=>t.name=="Root");
                    Assert.That(Vector3.Dot((facing.position-origin.position).normalized,Vector3.forward),Is.GreaterThan(.99f));
                    foreach(var clip in entry.clips)
                    {
                        clip.SampleAnimation(animator.gameObject,0);skin.BakeMesh(baked);var first=baked.vertices;
                        clip.SampleAnimation(animator.gameObject,clip.length*.43f);skin.BakeMesh(baked);var next=baked.vertices;
                        Assert.That(next.Length,Is.EqualTo(first.Length));
                        Assert.That(next.Any(v=>float.IsNaN(v.sqrMagnitude)||float.IsInfinity(v.sqrMagnitude)),Is.False);
                        float motion=0;for(int i=0;i<first.Length;i++)motion=Mathf.Max(motion,(first[i]-next[i]).sqrMagnitude);
                        Assert.That(motion,Is.GreaterThan(.000001f),entry.kind+" / "+clip.name+" is static after import");
                    }
                }
                finally{Object.DestroyImmediate(baked);Object.DestroyImmediate(instance);}
            }
        }
        [Test] public void RuntimeGraphChangesThePoseWithoutMovingTheEncounterRoot()
        {
            var parent=new GameObject("confirmed pose test");
            var baked=new Mesh();
            try
            {
                using(var model=new EldritchModel(parent.transform,EldritchKind.DeepOne))
                {
                    var creature=new EldritchCreature{Kind=EldritchKind.DeepOne,Health=65,Generation=1,
                        Phase=EldritchPhase.Windup,Timer=24};
                    var skin=model.Root.GetComponentInChildren<SkinnedMeshRenderer>();
                    model.Animate(creature,1);skin.BakeMesh(baked);var first=baked.vertices;
                    creature.Timer=1;model.Animate(creature,24);skin.BakeMesh(baked);var next=baked.vertices;
                    float motion=0;for(int i=0;i<first.Length;i++)motion=Mathf.Max(motion,(first[i]-next[i]).sqrMagnitude);
                    Assert.That(motion,Is.GreaterThan(.001f));
                    Assert.That(model.Root.localPosition,Is.EqualTo(Vector3.zero));
                    Assert.That(parent.transform.position,Is.EqualTo(Vector3.zero));
                }
            }
            finally{Object.DestroyImmediate(baked);Object.DestroyImmediate(parent);}
        }
        [Test] public void AuthoredTenSpeciesMatchPackagedMap()
        {
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset");
            var config=settings.CreateMap().Fishing.Eldritch;
            Assert.That(config.Enabled,Is.True);Assert.That(config.SpeciesMask,Is.EqualTo(1023));
            Assert.That(config.MaxAlive,Is.EqualTo(4));
            Assert.That(settings.CreateMap().Encode(),Is.EqualTo(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/PlatformMap.bytes").bytes));
        }
        [Test] public void TownDeathHidesTheModelAndWarningsEvenWhenTheEncounterClockIsFrozen()
        {
            var root=new GameObject("Town death test");
            var config=new EldritchConfig();
            var creature=new EldritchCreature{Id=0,Kind=TownEncounters.Kind(0),Phase=EldritchPhase.Windup,
                Generation=1,Health=20,Timer=1,Ally=-1,DirectionZ=1000000};
            HookState Snapshot()=>new HookState(30,new[]{new Fisher()},Array.Empty<Fish>(),0,
                eldritch:new EldritchState(new[]{creature}),town:new TownState(true));
            try
            {
                using(var view=new EldritchPresentation(root.transform,Shader.Find("Hook Island/Fishing Line"),config,true))
                {
                    view.Synchronize(Snapshot());Assert.That(root.GetComponentsInChildren<Renderer>(),Is.Not.Empty);
                    creature.Phase=EldritchPhase.Dead;creature.Health=0;creature.Timer=config.RespawnTicks;
                    var dead=Snapshot();view.Synchronize(dead,0);
                    Assert.That(root.GetComponentsInChildren<SkinnedMeshRenderer>(),Is.Not.Empty,"Observed kills play their death pose.");
                    Assert.That(root.GetComponentsInChildren<LineRenderer>().Any(l=>l.enabled),Is.False,"Dead enemies must not retain warnings or health bars.");
                    view.Synchronize(dead,1.3f);Assert.That(root.GetComponentsInChildren<Renderer>(),Is.Empty);
                    view.Clear();view.Synchronize(dead);Assert.That(root.GetComponentsInChildren<Renderer>(),Is.Empty);
                    creature.Phase=EldritchPhase.Hunting;creature.Health=20;view.Synchronize(Snapshot());
                    Assert.That(root.GetComponentsInChildren<Renderer>(),Is.Not.Empty);
                }
            }
            finally{Object.DestroyImmediate(root);}
        }
        [Test] public void ConfirmedTelegraphsAreReadOnlyAndClearOnRestore()
        {
            var root=new GameObject("eldritch-presentation-test");
            var creatures=EldritchCatalog.All.Select(d=>new EldritchCreature{Id=(int)d.Kind,Kind=d.Kind,Phase=EldritchPhase.Windup,
                Generation=1,AttackSerial=1,Health=d.Health,Timer=d.WindupTicks,Target=0,Ally=d.Kind==EldritchKind.ChoirPriest?0:-1,
                X=(int)d.Kind*4000,DirectionZ=1000000,EffectX=(int)d.Kind*4000,EffectZ=d.Range}).ToArray();
            var state=new HookState(20,new[]{new Fisher{Slot=0}},Array.Empty<Fish>(),0,PlatformRules.InitialTiles(),eldritch:new EldritchState(creatures));
            var bytes=new HookGame().Encode(state);
            try
            {
                using(var view=new EldritchPresentation(root.transform,Shader.Find("Hook Island/Fishing Line"),new EldritchConfig()))
                {
                    view.Synchronize(state);
                    Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty,"Models must not participate in authoritative physics.");
                    var warnings=root.GetComponentsInChildren<LineRenderer>().Where(l=>l.name=="Attack warning" && l.enabled).ToArray();
                    Assert.That(warnings.Length,Is.EqualTo(10));
                    Assert.That(root.GetComponentsInChildren<LineRenderer>().Count(l=>l.name=="Safe inner ring" && l.enabled),Is.EqualTo(1));
                    Assert.That(warnings.All(l=>l.positionCount>0),Is.True);
                    foreach(var line in warnings)for(int i=0;i<line.positionCount;i++)
                        Assert.That(float.IsNaN(line.GetPosition(i).sqrMagnitude) || float.IsInfinity(line.GetPosition(i).sqrMagnitude),Is.False);
                    Assert.That(new HookGame().Encode(state),Is.EqualTo(bytes));
                    view.Clear();Assert.That(root.GetComponentsInChildren<LineRenderer>(),Is.Empty);
                    view.Synchronize(state);Assert.That(root.GetComponentsInChildren<LineRenderer>().Count(l=>l.name=="Attack warning" && l.enabled),Is.EqualTo(10));
                }
                Assert.That(root.transform.childCount,Is.Zero,"Disposal leaked encounter views.");
            }
            finally{Object.DestroyImmediate(root);}
        }
    }
}
