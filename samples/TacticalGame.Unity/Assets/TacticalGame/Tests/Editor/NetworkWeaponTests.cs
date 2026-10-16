using System.Linq;
using Advanture.Tactical.Simulation;
using Advanture.TacticalGame.NetworkView;
using NUnit.Framework;
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
        [Test] public void FireAndReloadSurviveUnityInputQuantization()
        {
            var input=InputQuantizer.Create(Vector2.up,90,0,fire:true,reload:true);
            var decoded=TacticalInput.Decode(input.Encode());Assert.That(decoded.Fire,Is.True);Assert.That(decoded.Reload,Is.True);
            Assert.That(decoded.MoveX,Is.EqualTo(1000));
        }
    }
}
