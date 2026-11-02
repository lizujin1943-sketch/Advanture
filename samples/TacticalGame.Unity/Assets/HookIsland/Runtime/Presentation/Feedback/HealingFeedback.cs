using System.Collections.Generic;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Short-lived actual healing amounts, sourced only from committed food uses.</summary>
    public sealed class HealingFeedback
    {
        public const double Lifetime=1.3;
        public readonly struct Notice
        {
            public Notice(ushort slot,int amount,double at){Slot=slot;Amount=amount;StartedAt=at;}
            public readonly ushort Slot;
            public readonly int Amount;
            public readonly double StartedAt;
        }
        private readonly List<Notice> notices=new List<Notice>();
        public IReadOnlyList<Notice> Notices=>notices;
        public void Advance(double now,bool visible)=>notices.RemoveAll(n=>!visible || now-n.StartedAt>=Lifetime);
        public void Apply(HookGameplayEvent value,double now)
        {
            if(value.Kind!=HookGameplayEventKind.FoodConsumed || value.Amount<=0)return;
            if(notices.Count>=16)notices.RemoveAt(0);
            notices.Add(new Notice(value.Slot,value.Amount,now));
        }
        public void Reset()=>notices.Clear();
    }
}
