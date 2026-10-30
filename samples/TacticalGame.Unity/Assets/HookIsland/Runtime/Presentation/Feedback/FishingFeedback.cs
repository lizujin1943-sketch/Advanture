using System;
using System.Collections.Generic;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>One confirmed-event consumer per local view. Draw calls cannot create or replay feedback.</summary>
    public sealed class FishingFeedback
    {
        private object identity;
        private Guid eventSession;
        private ulong epoch,observedTick,eventTick;
        private int eventIndex=-1;
        private bool initialized,hasEvent;
        public AcquisitionFeedback Acquisitions {get;}=new AcquisitionFeedback();
        public HealthDamageFeedback Damage {get;}=new HealthDamageFeedback();
        public ComboHudAnimation Combo {get;}=new ComboHudAnimation();
        public int Generation {get;private set;}
        public void Update(object session,ulong round,ulong confirmedTick,HookState state,ushort local,
            IReadOnlyList<ConfirmedGameplayEvent> events,double now,bool showDamage,Action<HookGameplayEvent> present=null)
        {
            if(session==null || state==null){Reset();return;}
            if(!initialized || !ReferenceEquals(identity,session) || epoch!=round || confirmedTick<observedTick)Reset();
            initialized=true;identity=session;epoch=round;observedTick=confirmedTick;
            Acquisitions.Advance(now);Damage.Advance(now,showDamage && !state.Won);
            int combo=local<state.Players.Count?state.Players[local].Combo.Count:0;
            Combo.Observe(session,round,confirmedTick,combo,state.Tackle?.Config.Combo,(float)now);
            foreach(var receipt in events)
            {
                if(receipt.Epoch!=round || receipt.Tick>confirmedTick || receipt.Index<0)continue;
                if(hasEvent && (receipt.SessionId!=eventSession || receipt.Tick<eventTick || receipt.Tick==eventTick && receipt.Index<=eventIndex))continue;
                eventSession=receipt.SessionId;eventTick=receipt.Tick;eventIndex=receipt.Index;hasEvent=true;
                var value=receipt.Event;
                if(value.Slot>=state.Players.Count || state.Players[value.Slot].WaitingToJoin)continue;
                Acquisitions.Apply(value,now);
                if(showDamage && !state.Won)Damage.Apply(value,now);
                if(value.Kind==HookGameplayEventKind.ComboIncreased && value.Slot==local)Combo.ConfirmIncrease(value.Value,(float)now);
                present?.Invoke(value);
            }
        }
        public void Reset()
        {
            identity=null;eventSession=Guid.Empty;epoch=observedTick=eventTick=0;eventIndex=-1;initialized=hasEvent=false;
            Acquisitions.Reset();Damage.Reset();Combo.Reset();Generation++;
        }
    }
}
