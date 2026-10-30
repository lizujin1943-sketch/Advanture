using System;
using System.Collections.Generic;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;

namespace Advanture.HookIsland.Tests
{
    public sealed class FishingFeedbackTests
    {
        [Test] public void AudioCallbacksAndComboBounceOnlyReceiveEachConfirmedIdentityOnce()
        {
            var f=new FishingFeedback();var owner=new object();var id=Guid.NewGuid();var played=new List<HookGameplayEventKind>();
            var state=AcquisitionFeedbackTests.State(9,new Fisher{Slot=0,Combo=new ComboState{Count=3}});
            var batch=new[]{new ConfirmedGameplayEvent(id,1,8,0,new HookGameplayEvent(HookGameplayEventKind.HookCast,0)),
                new ConfirmedGameplayEvent(id,1,9,0,new HookGameplayEvent(HookGameplayEventKind.FishCaught,0)),
                new ConfirmedGameplayEvent(id,1,9,1,new HookGameplayEvent(HookGameplayEventKind.ComboIncreased,0,value:3))};
            f.Update(owner,1,9,state,0,batch,1,true,e=>played.Add(e.Kind));
            Assert.That(f.Combo.Sample(1.08f).Scale,Is.GreaterThan(1));
            f.Update(owner,1,9,state,0,batch,2,true,e=>played.Add(e.Kind));
            Assert.That(played.Count,Is.EqualTo(3));Assert.That(f.Combo.Sample(2).Flash,Is.Zero);
            f.Update(new object(),1,9,state,0,Array.Empty<ConfirmedGameplayEvent>(),3,true,e=>played.Add(e.Kind));
            Assert.That(f.Combo.Sample(3).Count,Is.EqualTo(3));Assert.That(f.Combo.Sample(3).Flash,Is.Zero);Assert.That(played.Count,Is.EqualTo(3));
        }
        [Test] public void AComboBrokenLaterInTheBatchIsNotRevivedByAnEarlierSuccess()
        {
            var f=new FishingFeedback();var state=AcquisitionFeedbackTests.State(10,new Fisher{Slot=0});
            f.Update(new object(),1,10,state,0,new[]{new ConfirmedGameplayEvent(Guid.NewGuid(),1,9,0,new HookGameplayEvent(HookGameplayEventKind.ComboIncreased,0,value:3))},1,true);
            Assert.That(f.Combo.Sample(1.08f).Alpha,Is.Zero);
        }
    }
}
