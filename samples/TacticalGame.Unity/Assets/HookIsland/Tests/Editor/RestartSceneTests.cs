using System.Reflection;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class RestartSceneTests
    {
        private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
        private static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Private).GetValue(target);
        [Test] public void NewRoundClearsAllPendingActionsAndWindowsBeforeSampling()
        {
            var go=new GameObject("restart-input-contract");go.SetActive(false);
            try
            {
                var room=go.AddComponent<IslandFishingController>();
                var overlay=go.AddComponent<FishingOverlay>();
                var equipment=go.AddComponent<FishingEquipmentController>();
                var equipmentUi=go.AddComponent<FishingEquipmentOverlay>();
                var construction=go.AddComponent<PlatformConstruction>();
                var revival=go.AddComponent<RevivalController>();
                Set(room,"overlay",overlay);Set(room,"<Equipment>k__BackingField",equipment);Set(room,"<Construction>k__BackingField",construction);Set(room,"<Revival>k__BackingField",revival);
                Set(Get<FishingPlayerInput>(room,"playerInput"),"moveX",(short)1000);Set(Get<FishingPlayerInput>(room,"playerInput"),"moveZ",(short)-1000);Set(Get<FishingPlayerInput>(room,"playerInput"),"pendingFire",true);Set(Get<FishingPlayerInput>(room,"playerInput"),"pendingCancel",true);
                Set(room,"pendingPurchaseId",5u);Set(room,"pendingUpgrade",HookUpgrade.Range);Set(Get<FishingPlayerInput>(room,"playerInput"),"aim",new Vector3(12,0,12));Set(room,"<ShopOpen>k__BackingField",true);
                Set(room,"<State>k__BackingField",new HookState(99,System.Array.Empty<Fisher>(),System.Array.Empty<Fish>(),0));
                overlay.MenuOpen=true;equipmentUi.SelectCharacterPage(false);
                Set(equipment,"<InventoryOpen>k__BackingField",true);Set(construction,"<Open>k__BackingField",true);
                Get<RodRequestBuffer>(equipment,"requests").TryQueue(4,RodAction.Equip,1,0);
                Get<InventoryRequestBuffer>(equipment,"inventoryRequests").TryQueue(4,new InventoryCommand(InventoryAction.Pickup,target:1));
                Get<PlatformBuildRequestBuffer>(construction,"requests").TryQueue(4,TileKind.Wood,2,0);
                Get<RevivalRequestBuffer>(revival,"requests").TryQueue(new RevivalState{Dead=true,Deaths=2,DownedId=2,DeathTick=1,LastRequest=4});
                Get<RoomRulesRequestBuffer>(room,"roomRulesRequests").TryQueue(new RoomRulesState(true,4),false);
                Set(revival,"heldTarget",1);
                Assert.That(equipment.Pending && construction.Pending && revival.Pending,Is.True);
                var reset=typeof(IslandFishingController).GetMethod("ResetLocalRoundState",Private);
                Assert.That(reset,Is.Not.Null,"A restart must clear local queues without leaving the room.");reset.Invoke(room,null);
                Assert.That(room.State,Is.Null);Assert.That(room.PurchasePending || room.RoomRulesPending || equipment.Pending || construction.Pending || revival.Pending,Is.False);
                Assert.That(overlay.MenuOpen || room.ShopOpen || equipment.InventoryOpen || construction.Open,Is.False);
                Assert.That(equipmentUi.CharacterPage,Is.True);
                var input=(HookInput)typeof(IslandFishingController).GetMethod("Sample",Private).Invoke(room,new object[]{1UL});
                Assert.That(input.Encode(),Is.EqualTo(default(HookInput).Encode()),"Old movement, casting, trade, construction or revival intent crossed the round boundary.");
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test] public void NoRoomCannotShowOrExecuteHostRestart()
        {
            var go=new GameObject("restart-authority-contract");go.SetActive(false);
            try
            {
                var room=go.AddComponent<IslandFishingController>();
                var method=typeof(IslandFishingController).GetMethod("RestartRound");
                Assert.That(method,Is.Not.Null);Assert.That(method.Invoke(room,null),Is.False);
                Assert.That(typeof(IslandFishingController).GetProperty("CanRestart").GetValue(room),Is.False);
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
