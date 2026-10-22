using System.Reflection;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class RoomRulesSceneTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
        private static T Get<T>(object target,string field)=>(T)target.GetType().GetField(field,Private).GetValue(target);
        [Test] public void RoomRequestSurvivesTradeAndRespawnComposition()
        {
            var go=new GameObject("room-rules-input-contract");go.SetActive(false);
            try
            {
                var room=go.AddComponent<IslandFishingController>();var equipment=go.AddComponent<FishingEquipmentController>();var revival=go.AddComponent<RevivalController>();
                Set(room,"<Equipment>k__BackingField",equipment);Set(room,"<Revival>k__BackingField",revival);
                Get<RoomRulesRequestBuffer>(room,"roomRulesRequests").TryQueue(new RoomRulesState(true),false);
                Get<InventoryRequestBuffer>(equipment,"inventoryRequests").TryQueue(0,new InventoryCommand(InventoryAction.Pickup,1));
                var sample=typeof(IslandFishingController).GetMethod("Sample",Private);
                var input=(HookInput)sample.Invoke(room,new object[]{1UL});
                Assert.That(HookInput.Decode(input.Encode()).RoomRules.RequestId,Is.EqualTo(1));Assert.That(input.Inventory.Action,Is.EqualTo(InventoryAction.Pickup));
                Get<RevivalRequestBuffer>(revival,"requests").TryQueue(new RevivalState{Dead=true,Deaths=1});
                input=(HookInput)sample.Invoke(room,new object[]{2UL});
                Assert.That(HookInput.Decode(input.Encode()).RoomRules.RequestId,Is.EqualTo(1));Assert.That(input.Revival.RespawnRequest,Is.EqualTo(1));
                Assert.That(room.RoomRulesPending,Is.True,"Sampling is not an authoritative acknowledgement.");
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test] public void NoHostCannotQueueRoomPolicy()
        {
            var go=new GameObject("room-rules-authority");go.SetActive(false);
            try{var room=go.AddComponent<IslandFishingController>();Assert.That(room.RequestFriendlyPranks(false),Is.False);Assert.That(room.RoomRulesPending,Is.False);}
            finally{Object.DestroyImmediate(go);}
        }
    }
}
