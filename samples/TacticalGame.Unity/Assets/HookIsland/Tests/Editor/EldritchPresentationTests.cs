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
                    var dead=Snapshot();view.Synchronize(dead);Assert.That(root.GetComponentsInChildren<Renderer>(),Is.Empty);
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
