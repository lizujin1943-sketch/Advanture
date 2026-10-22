using System.Linq;
using System.Reflection;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class RevivalSceneTests
    {
        [Test] public void RescueDisplayKeepsHeldTargetWhenCloserTeammateBecomesDowned()
        {
            var map=IslandMap.CreatePlatform();
            var players=new[]{
                new Fisher {Slot=0,X=0,Z=-2000,PulledBy=-1,HookedPlayer=-1,Vitals=new CharacterVitals {Health=100}},
                new Fisher {Slot=1,X=900,Z=-2000,PulledBy=-1,HookedPlayer=-1},
                new Fisher {Slot=2,X=500,Z=-2000,PulledBy=-1,HookedPlayer=-1}};
            HookState State()=>new HookState(0,players,System.Array.Empty<Fish>(),0,PlatformRules.InitialTiles());
            Assert.That(RevivalController.SelectTarget(map,State(),players[0]),Is.EqualTo(2));
            Assert.That(RevivalController.SelectTarget(map,State(),players[0],1),Is.EqualTo(1),"Hint must describe the target receiving held R input.");
            players[1].Revival.Dead=true;
            Assert.That(RevivalController.SelectTarget(map,State(),players[0],1),Is.EqualTo(2));
            Assert.That(RevivalController.SelectTarget(map,State(),players[0],99),Is.EqualTo(2));
        }
        [Test] public void AuthoredRevivalRulesMatchPackagedPlatformMap()
        {
            var platform=AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset");
            Assert.That(platform.fishing.revival,Is.Not.Null);
            var c=platform.fishing.revival.CreateConfig();
            Assert.That(c.DownedTicks,Is.EqualTo(1800));Assert.That(c.RescueTicks,Is.EqualTo(90));
            Assert.That(c.RescueRange,Is.EqualTo(1500));Assert.That(c.Cost(9000,200),Is.EqualTo(35));
            var map=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/PlatformMap.bytes");
            Assert.That(platform.CreateMap().Encode().SequenceEqual(map.bytes),Is.True);
        }
        [Test] public void RevivalConfigurationChangesRoomCompatibility()
        {
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();var settings=ScriptableObject.CreateInstance<RevivalSettings>();
            try
            {
                fishing.revival=settings;var before=fishing.CreateConfig().Encode();settings.rescueSeconds=4;
                Assert.That(fishing.CreateConfig().Revival.RescueTicks,Is.EqualTo(120));
                Assert.That(before.SequenceEqual(fishing.CreateConfig().Encode()),Is.False);
            }
            finally{Object.DestroyImmediate(fishing);Object.DestroyImmediate(settings);}
        }
        [Test] public void RescueInputPreservesMovementAndCancelAndDoesNotMixTransactions()
        {
            var go=new GameObject("revival-input-contract");
            try
            {
                go.SetActive(false);var controller=go.AddComponent<RevivalController>();
                typeof(RevivalController).GetField("heldTarget",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(controller,2);
                var input=controller.Compose(new HookInput(500,-250,3000,-4000,false,true));
                var decoded=HookInput.Decode(input.Encode());Assert.That(decoded.Revival.RescueSlot,Is.EqualTo(2));
                Assert.That(decoded.MoveX,Is.EqualTo(500));Assert.That(decoded.MoveZ,Is.EqualTo(-250));Assert.That(decoded.Cancel,Is.True);
                var building=new HookInput(0,0,build:TileKind.Wood,buildX:2,buildZ:0,buildId:1);
                Assert.That(controller.Compose(building).Revival.IsEmpty,Is.True);
                controller.ResetSession();Assert.That(controller.Compose(default).Revival.IsEmpty,Is.True);
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test] public void PaidIntentSurvivesControllerInputComposition()
        {
            var go=new GameObject("revival-room-input-contract");
            try
            {
                go.SetActive(false);var controller=go.AddComponent<IslandFishingController>();
                controller.InputProvider=_=>new HookInput(0,0,revival:new RevivalCommand(respawnRequest:5,deathId:2));
                var input=(HookInput)typeof(IslandFishingController).GetMethod("Sample",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,new object[]{1UL});
                Assert.That(input.Revival.RespawnRequest,Is.EqualTo(5));Assert.That(input.Revival.DeathId,Is.EqualTo(2));
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
