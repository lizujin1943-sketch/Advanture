using System;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;

namespace Advanture.HookIsland.Tests
{
    public sealed class HealthDamageFeedbackTests
    {
        [Test] public void DamageRemainsVisibleWhenHealingRestoresHealthInTheSameFrame()
        {
            var f=new FishingFeedback();var session=new object();
            var state=AcquisitionFeedbackTests.State(10,new Fisher{Slot=0,Vitals=new CharacterVitals{Health=100}});
            var damage=new[]{new ConfirmedGameplayEvent(Guid.NewGuid(),1,10,0,new HookGameplayEvent(HookGameplayEventKind.Damaged,0,17))};
            f.Update(session,1,10,state,0,damage,.2,true);f.Update(session,1,10,state,0,damage,.4,true);
            Assert.That(f.Damage.Notices.Count,Is.EqualTo(1));Assert.That(f.Damage.Notices[0].Amount,Is.EqualTo(17));
            f.Update(session,1,10,state,0,damage,1.3,true);Assert.That(f.Damage.Notices,Is.Empty);
        }
        [Test] public void HealthChangesAndEquipmentClampsWithoutDamageEventsRemainSilent()
        {
            var f=new FishingFeedback();var session=new object();
            f.Update(session,1,1,AcquisitionFeedbackTests.State(1,new Fisher{Slot=0,Vitals=new CharacterVitals{Health=200}}),0,Array.Empty<ConfirmedGameplayEvent>(),0,true);
            f.Update(session,1,2,AcquisitionFeedbackTests.State(2,new Fisher{Slot=0,Vitals=new CharacterVitals{Health=100}}),0,Array.Empty<ConfirmedGameplayEvent>(),.1,true);
            Assert.That(f.Damage.Notices,Is.Empty);
        }
        [Test] public void CloseHitsMergePerPlayerAndEachPlayerHasBoundedLanes()
        {
            var f=new HealthDamageFeedback();
            f.Apply(new HookGameplayEvent(HookGameplayEventKind.Damaged,0,10),.2);
            f.Apply(new HookGameplayEvent(HookGameplayEventKind.Damaged,1,20),.2);
            f.Apply(new HookGameplayEvent(HookGameplayEventKind.Damaged,0,5),.25);
            Assert.That(f.Notices.Count,Is.EqualTo(2));Assert.That(f.Notices[0].Amount,Is.EqualTo(15));Assert.That(f.Notices[1].Amount,Is.EqualTo(20));
            for(int i=0;i<10;i++)f.Apply(new HookGameplayEvent(HookGameplayEventKind.Damaged,0,1),.5+i*.2);
            Assert.That(f.Notices.Count,Is.EqualTo(5));
        }
        [Test] public void HiddenDamageIsConsumedWithoutReplayWhenMenuCloses()
        {
            var f=new FishingFeedback();var session=new object();var state=AcquisitionFeedbackTests.State(1,new Fisher{Slot=0});
            var events=new[]{new ConfirmedGameplayEvent(Guid.NewGuid(),1,1,0,new HookGameplayEvent(HookGameplayEventKind.Damaged,0,20))};
            f.Update(session,1,1,state,0,events,0,false);f.Update(session,1,1,state,0,events,.1,true);
            Assert.That(f.Damage.Notices,Is.Empty);
        }
    }
}
