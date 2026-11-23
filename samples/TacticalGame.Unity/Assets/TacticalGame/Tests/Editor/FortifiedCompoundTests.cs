using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine.TestTools;
using Advanture.Tactical.Simulation;
using Advanture.TacticalGame.NetworkView;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Advanture.TacticalGame.Tests
{
    public sealed class FortifiedCompoundTests
    {
        private const string Scene = "Assets/TacticalGame/Scenes/FortifiedCompound.unity";
        private TacticalMap map;

        [SetUp] public void OpenMap()
        {
            EditorSceneManager.OpenScene(Scene);
            Physics.SyncTransforms();
            map=TacticalMap.Decode(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/TacticalGame/Art/FortifiedCompound/FortifiedCompound.bytes").bytes);
        }
        [Test] public void PlazaSpawnsHaveHeadroomAndRoomUsesTheNetworkController()
        {
            Assert.That(EditorBuildSettings.scenes.Any(s=>s.enabled && s.path==Scene),Is.True);
            Assert.That(Object.FindObjectOfType<NetworkYardController>().AllowSolo,Is.True);
            Assert.That(Object.FindObjectsOfType<NetworkSmokeDriver>(),Is.Empty);
            Assert.That(Object.FindObjectsOfType<FirstPersonMotor>(),Is.Empty);
            foreach(var spawn in map.Spawns)
            {
                var feet=new Vector3(spawn.X*.001f,2.05f,spawn.Z*.001f);
                Assert.That(Physics.Raycast(feet+Vector3.up,Vector3.down,out var hit,2),Is.True);
                Assert.That(hit.collider.name,Does.Contain("Plaza"));
                Assert.That(hit.point.y,Is.EqualTo(map.GroundHeight(spawn.X,spawn.Z)*.001f).Within(.015f));
                Assert.That(Physics.CheckCapsule(feet+Vector3.up*.36f,feet+Vector3.up*1.55f,.35f),Is.False);
            }
        }
        [Test] public void AllBreachEntrancesHavePhysicalSupportAndHeadroom()
        {
            Assert.That(map.MonsterEntrances.Count,Is.EqualTo(3));
            foreach(var port in map.MonsterEntrances)
            {
                var feet=new Vector3(port.X*.001f,map.GroundHeight(port.X,port.Z)*.001f,port.Z*.001f);
                Assert.That(Physics.SphereCast(feet+Vector3.up, .24f, Vector3.down,out var hit,1.6f),Is.True,feet.ToString());
                Assert.That(hit.point.y,Is.EqualTo(feet.y).Within(.22f),feet.ToString());
                Assert.That(Physics.CheckCapsule(feet+Vector3.up*.50f,feet+Vector3.up*1.55f,.28f),Is.False,feet.ToString());
            }
        }
        [Test] public void BothAmmoDepotsHaveAReachableFloorBesideTheirCases()
        {
            Assert.That(map.AmmoDepots.Count,Is.EqualTo(2));
            foreach (var point in map.AmmoDepots)
            {
                var feet = new Vector3(point.X*.001f,map.GroundHeight(point.X,point.Z)*.001f,point.Z*.001f);
                Assert.That(Physics.Raycast(feet+Vector3.up,Vector3.down,out var hit,1.5f),Is.True);
                Assert.That(hit.point.y,Is.EqualTo(feet.y).Within(.035f));
                Assert.That(Physics.CheckCapsule(feet+Vector3.up*.36f,feet+Vector3.up*1.55f,.35f),Is.False);
            }
        }

        [TestCase(-8.125f,-16.125f)]
        [TestCase(-8.125f,-13.125f)]
        [TestCase(-8.125f,-11.125f)]
        [TestCase(8.125f,42.875f)]
        [TestCase(30.375f,10.375f)]
        [TestCase(2.125f,-42.875f)]
        public void YardAndStairLandingsMatchTheSupportMesh(float x,float z)
        {
            Assert.That(map.CanStand(Mathf.RoundToInt(1000*x),Mathf.RoundToInt(1000*z)),Is.True);
            float height=map.GroundHeight(Mathf.RoundToInt(1000*x),Mathf.RoundToInt(1000*z))*.001f;
            Assert.That(Physics.Raycast(new Vector3(x,height+1,z),Vector3.down,out var hit,2),Is.True);
            Assert.That(hit.point.y,Is.EqualTo(height).Within(.025f));
        }
    }

    public sealed class FortifiedCompoundRoomTests
    {
        [UnityTest] public IEnumerator SoloRoomShowsConfirmedTownModelsAtEveryBreach()
        {
            EditorSceneManager.OpenScene("Assets/TacticalGame/Scenes/FortifiedCompound.unity");
            yield return new EnterPlayMode();
            var room=Object.FindObjectOfType<NetworkYardController>();
            var seen=new HashSet<int>();
            int peak=0;
            double deadline=Time.realtimeSinceStartupAsDouble+75;
            try
            {
                room.PlaySolo();
                while((seen.Count<3 || peak<12) && Time.realtimeSinceStartupAsDouble<deadline)
                {
                    yield return null;
                    Assert.That(room.Failure,Is.Null);
                    if(!room.Playing || !room.LocalPlayer.HasValue)continue;
                    var state=room.Client.Session.InspectState();
                    peak=Mathf.Max(peak,state.Monsters.Creatures.Count(c=>c.Damageable && !c.IsMinion));
                    foreach(var c in state.Monsters.Creatures)
                    {
                        if(c.Phase!=EldritchPhase.Emerging)continue;
                        int index=room.Map.MonsterEntrances.ToList().FindIndex(p=>p.X==c.X && p.Z==c.Z);
                        Assert.That(index,Is.GreaterThanOrEqualTo(0));
                        var view=room.transform.Find($"Eldritch {c.Id:00} — {EldritchCatalog.Get(c.Kind).Name} (confirmed)");
                        Assert.That(view.gameObject.activeInHierarchy,Is.True);
                        Assert.That(view.GetComponentInChildren<SkinnedMeshRenderer>(),Is.Not.Null);
                        float floor=room.Map.GroundHeight(c.X,c.Z)*.001f;
                        float hover=EldritchCatalog.Get(c.Kind).Habitat==EldritchHabitat.Hover ? .35f : 0;
                        var expected=new Vector3(c.X*.001f,floor+hover-.8f*c.Timer/EldritchCatalog.EmergenceTicks,c.Z*.001f);
                        Assert.That(Vector3.Distance(view.position,expected),Is.LessThan(.01f));
                        seen.Add(index);
                    }
                }
                Assert.That(seen.Count,Is.EqualTo(3),"Confirmed and rendered breach entrances");
                Assert.That(peak,Is.GreaterThanOrEqualTo(12));
                Assert.That(room.LocalPlayer.Value.Combat.Reserve,Is.EqualTo(360));
            }
            finally {room.LeaveRoom();}
            yield return null;
            Assert.That(room.GetComponentsInChildren<SkinnedMeshRenderer>(),Is.Empty);
            yield return new ExitPlayMode();
        }
    }
}
