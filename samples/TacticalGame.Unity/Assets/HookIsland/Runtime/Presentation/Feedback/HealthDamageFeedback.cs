using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Transient damage notices from confirmed damage applications, independently of subsequent healing.</summary>
    public sealed class HealthDamageFeedback
    {
        public const double Lifetime = 1.05;
        public readonly struct Notice
        {
            public readonly ushort Slot;
            public readonly int Amount;
            public readonly int Lane;
            public readonly double StartedAt;
            public Notice(ushort slot, int amount, double startedAt, int lane)
            { Slot = slot; Amount = amount; StartedAt = startedAt; Lane = lane; }
        }
        private readonly List<Notice> notices = new List<Notice>();
        private readonly Dictionary<ushort,int> nextLanes=new Dictionary<ushort,int>();
        public IReadOnlyList<Notice> Notices => notices;
        public void Advance(double now,bool visible)
        {
            for(int i=notices.Count-1;i>=0;i--)
                if(!visible || now-notices[i].StartedAt>=Lifetime)notices.RemoveAt(i);
        }
        public void Apply(HookGameplayEvent value,double now)
        {
            if(value.Kind!=HookGameplayEventKind.Damaged || value.Amount<=0)return;
            ushort slot=value.Slot;int lost=value.Amount;
            nextLanes.TryGetValue(slot,out int lane);
            int count = 0, oldest = -1;
            for (int i = notices.Count - 1; i >= 0; i--)
            {
                var notice = notices[i];
                if (notice.Slot != slot) continue;
                if (now - notice.StartedAt < .12)
                { notices[i] = new Notice(slot, (int)Math.Min(int.MaxValue, (long)notice.Amount + lost), notice.StartedAt, notice.Lane); return; }
                count++; oldest = i;
            }
            if (count >= 4) notices.RemoveAt(oldest);
            notices.Add(new Notice(slot, lost, now, lane));
            nextLanes[slot]=(lane+1)%3;
        }
        public void Reset() { notices.Clear(); nextLanes.Clear(); }
    }
}
