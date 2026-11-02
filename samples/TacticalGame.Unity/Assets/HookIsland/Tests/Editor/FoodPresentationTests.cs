using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class FoodPresentationTests
    {
        [Test] public void LegacyRoomRejectsQuickSlotsWithoutLeavingAnUnacknowledgeableRequest()
        {
            var b=IslandMap.CreatePlatform();var map=new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots);
            using(var session=new IslandRoomSession(map,()=>Enumerable.Repeat((byte)97,32).ToArray()))
            {
                session.Create(0,1);var clock=System.Diagnostics.Stopwatch.StartNew();
                while(!session.Playing && clock.Elapsed.TotalSeconds<5){session.Poll(clock.Elapsed,_=>default);System.Threading.Thread.Sleep(1);}
                Assert.That(session.Playing,Is.True,session.Failure);
                var go=new GameObject("Legacy inventory input fixture");
                try
                {
                    var room=go.AddComponent<IslandFishingController>();var equipment=go.AddComponent<FishingEquipmentController>();
                    typeof(IslandFishingController).GetField("roomSession",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(room,session);
                    typeof(IslandFishingController).GetProperty("State").SetValue(room,session.Client.Session.InspectState());
                    typeof(FishingEquipmentController).GetField("room",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(equipment,room);
                    equipment.SelectQuickSlot(2);
                    Assert.That(equipment.Pending,Is.False,"A map without inventory can never acknowledge this request.");
                    Assert.That(equipment.Compose(new HookInput(0,0,10000,0,true)).Fire,Is.True);
                }
                finally{Object.DestroyImmediate(go);}
            }
        }
        [Test] public void EatingFeedbackDeduplicatesActualHealingAndRestoreIsSilent()
        {
            var feedback=new FishingFeedback();var session=new object();var id=Guid.NewGuid();int meals=0;
            var state=AcquisitionFeedbackTests.State(12,new Fisher{Slot=0});
            var events=new[]{new ConfirmedGameplayEvent(id,1,12,0,new HookGameplayEvent(HookGameplayEventKind.FoodConsumed,0,amount:7,itemKind:ItemKind.Food,itemType:2))};
            feedback.Update(session,1,12,state,0,events,1,true,_=>meals++);
            feedback.Update(session,1,12,state,0,events,1.1,true,_=>meals++);
            Assert.That(meals,Is.EqualTo(1));Assert.That(feedback.Healing.Notices.Single().Amount,Is.EqualTo(7));
            feedback.Update(new object(),1,12,state,0,Array.Empty<ConfirmedGameplayEvent>(),2,true,_=>meals++);
            Assert.That(feedback.Healing.Notices.Count,Is.Zero);Assert.That(meals,Is.EqualTo(1));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void EatingLiftsFoodToFaceWithoutMovingFeetAndInterruptionHidesIt(int type)
        {
            var definition=Resources.Load<FisherCharacterDefinition>(FisherCharacterDefinition.ResourcePath);
            var model=Object.Instantiate(definition.model);
            using(var art=new FoodArt())using(var animation=new FisherAnimationView(model.GetComponent<Animator>(),definition,0))
            try
            {
                Transform Bone(string name)=>model.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
                var rod=model.transform.Find("Equipped fishing rod");Assert.That(rod,Is.Not.Null);
                var eating=new FoodEatingView(model.transform,rod,art);var foot=Bone("Foot_R").position;
                var hand=Bone("Hand_R");var rest=hand.position;eating.Confirm(type);
                // Sample the middle of this cosmetic gesture without waiting on wall-clock time.
                typeof(FoodEatingView).GetField("started",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(eating,Time.unscaledTime-.55f);
                eating.Present(false);
                Assert.That(hand.position.y,Is.GreaterThan(rest.y+.04f));
                Assert.That(Vector3.Distance(hand.position,Bone("Head").position),Is.LessThan(.3f));
                Assert.That(Vector3.Distance(Bone("Foot_R").position,foot),Is.LessThan(.001f));
                Assert.That(rod.gameObject.activeSelf,Is.False);
                Assert.That(Bone("Food prop "+type).gameObject.activeSelf,Is.True);
                eating.Present(true);Assert.That(Bone("Food prop "+type).gameObject.activeSelf,Is.False);
                animation.Present(0,0,false,false,false);
                Assert.That(Vector3.Distance(hand.position,rest),Is.LessThan(.005f),"Authored idle must restore the hand after eating.");
            }
            finally{Object.DestroyImmediate(model);}
        }
    }
}
