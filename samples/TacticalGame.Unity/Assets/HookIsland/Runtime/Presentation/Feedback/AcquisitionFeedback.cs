using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Transient acquisition notices from explicit, confirmed settlement events.</summary>
    public sealed class AcquisitionFeedback
    {
        public const double Lifetime = 4.5, WorldLifetime = 1.8;
        public readonly struct Notice
        {
            public readonly long Id;
            public readonly ushort Slot;
            // Null denotes coins; all other icons and names come from the inventory catalog.
            public readonly ItemKind? Kind;
            public readonly int Type, Amount;
            public readonly double StartedAt;
            public Notice(long id, ushort slot, ItemKind? kind, int type, int amount, double startedAt)
            { Id = id; Slot = slot; Kind = kind; Type = type; Amount = amount; StartedAt = startedAt; }
        }
        private readonly List<Notice> notices = new List<Notice>();
        private long sequence;
        public IReadOnlyList<Notice> Notices => notices;
        public void Advance(double now)
        {
            for(int i=notices.Count-1;i>=0;i--)
                if(now-notices[i].StartedAt>=Lifetime)notices.RemoveAt(i);
        }
        public void Apply(HookGameplayEvent value,double now)
        {
            if(value.Kind==HookGameplayEventKind.CoinsReceived)Add(value.Slot,null,0,value.Amount,now);
            else if(value.Kind==HookGameplayEventKind.MaterialReceived || value.Kind==HookGameplayEventKind.EquipmentReceived)
                Add(value.Slot,value.ItemKind,value.ItemType,value.Amount,now);
        }
        private void Add(ushort slot, ItemKind? kind, int type, int amount, double now)
        {
            if (amount <= 0) return;
            // Bounded even after a large confirmed-frame catch-up. No authority or inventory writes.
            if (notices.Count == 64) notices.RemoveAt(0);
            notices.Add(new Notice(++sequence, slot, kind, type, amount, now));
        }
        public void Reset()
        { notices.Clear(); }
    }
}
