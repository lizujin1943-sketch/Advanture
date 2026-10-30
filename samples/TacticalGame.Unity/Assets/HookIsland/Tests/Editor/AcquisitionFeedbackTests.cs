using System;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;

namespace Advanture.HookIsland.Tests
{
    public sealed class AcquisitionFeedbackTests
    {
        internal static HookState State(ulong tick,params Fisher[] players)=>new HookState(tick,players,Array.Empty<Fish>(),0,
            tackle:new TackleState(new FishingConfig(),new InventorySystem(players.Length).Capture(),Array.Empty<RodStock>(),1,18000));
        private readonly object session=new object();
        private readonly Guid roomId=Guid.NewGuid();
        private ConfirmedGameplayEvent Receipt(ulong tick,int index,HookGameplayEvent value,ulong epoch=1)=>new ConfirmedGameplayEvent(roomId,epoch,tick,index,value);
        [Test] public void IncomeSurvivesSameFrameSpendingAndRepeatedPresentationDoesNotReplay()
        {
            var f=new FishingFeedback();var p=new Fisher{Slot=0,Wood=0,Coins=0};
            // Net balances are zero: both rewards were spent before this snapshot was rendered.
            var state=State(8,p);
            var events=new[]{Receipt(8,0,new HookGameplayEvent(HookGameplayEventKind.CoinsReceived,0,36)),
                Receipt(8,1,new HookGameplayEvent(HookGameplayEventKind.MaterialReceived,0,10,itemKind:ItemKind.Wood)),
                Receipt(8,2,new HookGameplayEvent(HookGameplayEventKind.MaterialReceived,0,8,itemKind:ItemKind.Fishbone))};
            f.Update(session,1,8,state,0,events,.2,true);f.Update(session,1,8,state,0,events,.4,true);
            Assert.That(f.Acquisitions.Notices.Select(n=>n.Amount),Is.EqualTo(new[]{36,10,8}));
            Assert.That(f.Acquisitions.Notices.Select(n=>n.Kind),Is.EqualTo(new ItemKind?[]{null,ItemKind.Wood,ItemKind.Fishbone}));
            f.Update(session,1,8,state,0,events,5,true);Assert.That(f.Acquisitions.Notices,Is.Empty);
        }
        [Test] public void SnapshotChangesAloneNeverSynthesizeAReward()
        {
            var f=new FishingFeedback();var p=new Fisher{Slot=0,CoinsEarned=400,Wood=90};
            f.Update(session,1,100,State(100,p),0,Array.Empty<ConfirmedGameplayEvent>(),0,true);
            p.CoinsEarned+=30;p.Wood+=4;
            f.Update(session,1,101,State(101,p),0,Array.Empty<ConfirmedGameplayEvent>(),.1,true);
            Assert.That(f.Acquisitions.Notices,Is.Empty,"Bootstrap, inventory rearrangement or prediction cannot create an acquisition.");
        }
        [Test] public void CooperativeRecipientsAndPartialPickupsUseActualCreditedAmounts()
        {
            var f=new FishingFeedback();var state=State(3,new Fisher{Slot=0},new Fisher{Slot=1});
            var events=new[]{Receipt(2,0,new HookGameplayEvent(HookGameplayEventKind.MaterialReceived,0,3,itemKind:ItemKind.Fishbone)),
                Receipt(2,1,new HookGameplayEvent(HookGameplayEventKind.MaterialReceived,1,2,itemKind:ItemKind.Fishbone)),
                Receipt(3,0,new HookGameplayEvent(HookGameplayEventKind.MaterialReceived,1,1,itemKind:ItemKind.Wood)),
                Receipt(3,1,new HookGameplayEvent(HookGameplayEventKind.EquipmentReceived,1,1,itemKind:ItemKind.SwimRing,itemType:2))};
            f.Update(session,1,3,state,0,events,.2,true);
            Assert.That(f.Acquisitions.Notices.Where(n=>n.Slot==0).Single().Amount,Is.EqualTo(3));
            Assert.That(f.Acquisitions.Notices.Where(n=>n.Slot==1).Select(n=>n.Amount),Is.EqualTo(new[]{2,1,1}));
            Assert.That(f.Acquisitions.Notices.Last().Type,Is.EqualTo(2));
        }
        [Test] public void ReconnectRestartAndWaitingSlotsClearTransientFeedback()
        {
            var f=new FishingFeedback();var state=State(100,new Fisher{Slot=0});
            var receipt=Receipt(100,0,new HookGameplayEvent(HookGameplayEventKind.CoinsReceived,0,10));
            f.Update(session,1,100,state,0,new[]{receipt},0,true);Assert.That(f.Acquisitions.Notices.Count,Is.EqualTo(1));
            f.Update(new object(),1,100,state,0,Array.Empty<ConfirmedGameplayEvent>(),.1,true);Assert.That(f.Acquisitions.Notices,Is.Empty);
            f.Update(session,2,1,State(1,new Fisher{Slot=0}),0,new[]{receipt},.2,true);Assert.That(f.Acquisitions.Notices,Is.Empty,"Old epoch cannot produce a reward.");
            var waiting=State(2,new Fisher{Slot=0,WaitingToJoin=true});
            f.Update(session,2,2,waiting,0,new[]{Receipt(2,0,receipt.Event,2)},.3,true);Assert.That(f.Acquisitions.Notices,Is.Empty);
        }
        [Test] public void FeedIsBoundedDuringALargeConfirmedCatchup()
        {
            var f=new AcquisitionFeedback();for(int i=0;i<1000;i++)f.Apply(new HookGameplayEvent(HookGameplayEventKind.CoinsReceived,0,1),0);
            Assert.That(f.Notices.Count,Is.EqualTo(64));f.Advance(5);Assert.That(f.Notices,Is.Empty);
        }
    }
}
