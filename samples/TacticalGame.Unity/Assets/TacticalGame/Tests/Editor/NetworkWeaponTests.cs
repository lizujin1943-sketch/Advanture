using System.Linq;
using Advanture.Tactical.Simulation;
using Advanture.TacticalGame.NetworkView;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine;
namespace Advanture.TacticalGame.Tests
{
    public sealed class NetworkWeaponTests
    {
        [Test] public void RifleHasRecognizablePartsAndDoesNotAddPhysics()
        {
            var parent=new GameObject("WeaponTest");
            try
            {
                var gun=Ak47View.Create(parent.transform,true);
                Assert.That(gun.transform.Find("AK47/CurvedMagazine"),Is.Not.Null);
                Assert.That(gun.transform.Find("AK47/WoodStock"),Is.Not.Null);
                Assert.That(gun.transform.Find("AK47/Muzzle"),Is.Not.Null);
                Assert.That(parent.GetComponentsInChildren<Collider>(true).Length,Is.Zero);
            }
            finally{Object.DestroyImmediate(parent);}
        }
        [Test] public void ConfirmedShotPresentationDoesNotReplayAfterRollback()
        {
            var parent=new GameObject("WeaponTest");
            try
            {
                var gun=Ak47View.Create(parent.transform,true);
                var shot=new CombatState(ammo:29,shotSequence:1,lastShotTick:1,shotY:1650,endY:1650,endZ:5000);
                gun.Present(shot,1,0);gun.Present(shot,1,0);gun.Present(new CombatState(),0,0);gun.Present(shot,1,0);
                Assert.That(gun.PresentedShots,Is.EqualTo(1));
                gun.Present(new CombatState(ammo:28,shotSequence:2,lastShotTick:4),4,0);
                Assert.That(gun.PresentedShots,Is.EqualTo(2));
            }
            finally{Object.DestroyImmediate(parent);}
        }
        [Test] public void AuthoredViewsShareValidSurfaceAndSocketsInGameSpace()
        {
            var observer = new GameObject("WeaponTestObserver", typeof(Camera));
            var ally = new GameObject("WeaponTestAlly");
            try
            {
                var camera = observer.GetComponent<Camera>();
                var near = Ak47View.Create(observer.transform, true);
                var far = Ak47View.Create(ally.transform, false, camera);
                var surface = Resources.Load<Material>("Weapons/AK47/AK47_Surface");
                Assert.That(surface.shader.name, Is.EqualTo("Standard"));
                foreach (var slot in new[] {"_MainTex", "_MetallicGlossMap", "_BumpMap"})
                {
                    var texture = (Texture2D)surface.GetTexture(slot);
                    Assert.That(texture, Is.Not.Null, slot);
                    Assert.That(texture.width, Is.EqualTo(2048));
                    Assert.That(texture.mipmapCount, Is.GreaterThan(1));
                    var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
                    Assert.That(importer.sRGBTexture, Is.EqualTo(slot == "_MainTex"));
                    if (slot == "_BumpMap") Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap));
                }
                int[] triangles = new int[2];
                var views = new[] {near, far};
                for (int i = 0; i < views.Length; i++)
                {
                    var rig = views[i].transform.Find("AK47");
                    var nodes = rig.GetComponentsInChildren<Transform>();
                    Vector3 Socket(string name) => views[i].transform.InverseTransformPoint(nodes.Single(n => n.name == name).position);
                    var muzzle = Socket("Muzzle");
                    var stock = Socket("WoodStock");
                    var support = Socket("SupportHand");
                    var grip = Socket("FiringHand");
                    Debug.Log($"AK47 sockets view={i} muzzle={muzzle:F3} stock={stock:F3} left={support:F3} right={grip:F3}");
                    Assert.That(muzzle.z, Is.InRange(.64f, .72f));
                    Assert.That(Vector3.Dot(rig.Find("Muzzle").forward, views[i].transform.forward), Is.GreaterThan(.99f));
                    Assert.That(Mathf.Abs(muzzle.x), Is.LessThan(.01f));
                    Assert.That(stock.z, Is.LessThan(-.10f));
                    Assert.That(support.x, Is.LessThan(-.03f));
                    Assert.That(grip.x, Is.GreaterThan(.03f));
                    var surfaces = rig.GetComponentsInChildren<MeshFilter>().Where(f => f.GetComponent<MeshRenderer>()?.sharedMaterial == surface).ToArray();
                    Assert.That(surfaces.Length, Is.InRange(4, 9));
                    foreach (var filter in surfaces)
                    {
                        var mesh = filter.sharedMesh;
                        Assert.That(filter.gameObject.layer, Is.EqualTo(i == 0 ? 31 : 0));
                        Assert.That(mesh.HasVertexAttribute(VertexAttribute.TexCoord0) && mesh.HasVertexAttribute(VertexAttribute.Tangent), Is.True, filter.name);
                        triangles[i] += (int)mesh.GetIndexCount(0) / 3;
                    }
                    Assert.That(rig.GetComponentsInChildren<Collider>(true), Is.Empty);
                }
                Assert.That(triangles[1], Is.LessThan(triangles[0] * .65f));
                Assert.That(camera.cullingMask & (1 << 31), Is.Zero);
                Object.DestroyImmediate(near.gameObject);
                Assert.That(camera.cullingMask & (1 << 31), Is.Not.Zero);
            }
            finally { Object.DestroyImmediate(observer); Object.DestroyImmediate(ally); }
        }

        [Test] public void ReloadMovesHandAndMagazineThenReturnsBothToTheirBindPose()
        {
            var parent = new GameObject("WeaponTest");
            try
            {
                var gun = Ak47View.Create(parent.transform, true);
                var mag = gun.transform.Find("AK47/CurvedMagazine");
                var hand = gun.transform.Find("AK47/SupportHand");
                var closedHand = gun.transform.Find("AK47/SupportCarry");
                var socket = gun.transform.Find("AK47/CarrySocket");
                Vector3 wristOnProp = mag.InverseTransformPoint(socket.position);
                Vector3 magBind = mag.localPosition, handBind = hand.localPosition;
                var reload = new CombatState(ammo:24, reloadUntilTick:102);
                gun.Present(reload, 30, .5f);
                gun.Present(reload, 65, .1f);
                var mid = mag.position;
                Assert.That(closedHand.gameObject.activeSelf, Is.True);
                Assert.That(hand.gameObject.activeSelf, Is.False);
                Assert.That(Vector3.Distance(closedHand.position, mag.TransformPoint(wristOnProp)), Is.LessThan(.002f));
                Assert.That(Vector3.Distance(magBind, mag.localPosition), Is.GreaterThan(.13f));
                Assert.That(Vector3.Distance(handBind, hand.localPosition), Is.GreaterThan(.17f));
                gun.Present(reload, 44, 0);
                Assert.That(Vector3.Distance(mid, mag.position), Is.LessThan(.0001f));
                gun.Present(new CombatState(), 102, .05f);
                Assert.That(closedHand.gameObject.activeSelf, Is.False);
                Assert.That(hand.gameObject.activeSelf, Is.True);
                Assert.That(Vector3.Distance(magBind, mag.localPosition), Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(handBind, hand.localPosition), Is.LessThan(.0001f));
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [Test] public void OnlyRecentConfirmedEventsAreHeardAfterDeathOrLateJoin()
        {
            var parent = new GameObject("WeaponTest");
            try
            {
                var gun = Ak47View.Create(parent.transform, true);
                gun.Present(new CombatState(ammo:21, shotSequence:9, lastShotTick:140), 170, .05f);
                Assert.That(gun.PresentedShots, Is.Zero);
                var dead = new CombatState(health:0, respawnAtTick:271, shotSequence:9, lastShotTick:140);
                gun.Present(dead, 181, .04f);
                Assert.That(gun.IsVisible, Is.False);
                gun.Present(new CombatState(shotSequence:9, lastShotTick:140), 271, .04f);
                Assert.That(gun.IsVisible, Is.True);
                Assert.That(gun.PresentedShots, Is.Zero);
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [Test] public void MuzzleFlashExpiresAndOnlyConfirmedContactsProduceAnImpact()
        {
            var parent = new GameObject("WeaponTest");
            try
            {
                var gun = Ak47View.Create(parent.transform, false);
                var flash = gun.transform.Find("AK47/Muzzle/MuzzleFlash");
                var effects = gun.transform.Find("ShotEffects");
                var dust = effects.Find("ImpactDust").GetComponent<ParticleSystem>();
                var fabric = effects.Find("CharacterHit").GetComponent<ParticleSystem>();
                var misses = new CombatState(shotSequence:1, lastShotTick:2, endZ:CombatState.ShotRangeMillimeters);
                gun.Present(misses, 2, .3f);
                Assert.That(flash.gameObject.activeSelf, Is.True);
                gun.Present(misses, 6, .14f);
                Assert.That(flash.gameObject.activeSelf, Is.False);
                Assert.That(dust.particleCount, Is.Zero);

                var contact = new CombatState(shotSequence:2, lastShotTick:8, endZ:6000);
                gun.Present(contact, 8, 0);
                gun.Present(contact, 12, .13f);
                Assert.That(dust.particleCount, Is.GreaterThan(0));
                var simultaneous = new CombatState(health:0, respawnAtTick:120, shotSequence:3, lastShotTick:30, hits:1, lastHitSlot:1, endZ:8000);
                gun.Present(simultaneous, 30, .65f);
                Assert.That(flash.gameObject.activeInHierarchy, Is.False);
                gun.Present(simultaneous, 33, .08f);
                Assert.That(fabric.particleCount, Is.GreaterThan(0));
                Assert.That(dust.particleCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [Test] public void ShippingBankContainsSeparateCloseDistantAndHandlingTakes()
        {
            var clips = Resources.LoadAll<AudioClip>("Weapons/AK47/Audio");
            Assert.That(clips.Count(c => c.name.StartsWith("ShotClose") && c.channels == 2), Is.EqualTo(5));
            Assert.That(clips.Count(c => c.name.StartsWith("ShotFar") && c.channels == 1), Is.EqualTo(5));
            Assert.That(clips.Count(c => c.name.StartsWith("Reload")), Is.EqualTo(3));
            Assert.That(clips.All(c => c.frequency == 48000 && c.length > .10f), Is.True);
        }
        [Test] public void FireAndReloadSurviveUnityInputQuantization()
        {
            var input=InputQuantizer.Create(Vector2.up,90,0,fire:true,reload:true);
            var decoded=TacticalInput.Decode(input.Encode());Assert.That(decoded.Fire,Is.True);Assert.That(decoded.Reload,Is.True);
            Assert.That(decoded.MoveX,Is.EqualTo(1000));
        }
    }
}
