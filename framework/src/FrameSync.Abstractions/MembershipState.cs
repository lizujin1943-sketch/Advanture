using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FrameSync.Abstractions
{
    /// <summary>成员变化的种类。只改变既有位置，不在初版运行中增加新位置。</summary>
    public enum MemberEventKind : byte
    {
        /// <summary>启用已分配位置的同一控制者，版本保持不变；更换控制者必须使用 Rebind。</summary>
        Activate = 1,
        /// <summary>从本帧开始停用位置；不再要求或执行该位置的输入。</summary>
        Deactivate = 2,
        /// <summary>重新启用位置，并将控制权版本严格加一，隔离旧连接的输入。</summary>
        Rebind = 3
    }

    /// <summary>由服务器写进最终输入帧的成员操作；所有端在同一帧应用。</summary>
    public sealed class MemberEvent
    {
        /// <summary>建立不可变操作。sequence 是本会话内连续递增的成员操作编号。</summary>
        public MemberEvent(ulong sequence, MemberEventKind kind, ushort slot, ulong generation)
        {
            if (sequence == 0 || generation == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (kind < MemberEventKind.Activate || kind > MemberEventKind.Rebind)
                throw new ArgumentOutOfRangeException(nameof(kind));
            Sequence = sequence; Kind = kind; Slot = slot; Generation = generation;
        }
        /// <summary>本次运行内的全局成员事件序号，从 1 开始，不可跳号或重复。</summary>
        public ulong Sequence { get; }
        /// <summary>本次成员状态变化。</summary>
        public MemberEventKind Kind { get; }
        /// <summary>预先分配的参与者位置。</summary>
        public ushort Slot { get; }
        /// <summary>操作完成后的控制权版本。</summary>
        public ulong Generation { get; }
    }

    /// <summary>某一帧结束时的成员状态；实例不可变，可安全放入检查点。</summary>
    public sealed class MembershipState
    {
        /// <summary>复制并按位置排序成员状态。调用层负责验证其与会话固定名单一致。</summary>
        public MembershipState(IReadOnlyList<Participant> participants, ulong lastSequence = 0)
        {
            if (participants == null) throw new ArgumentNullException(nameof(participants));
            // 复用名单检查：位置、稳定身份唯一；不把网络连接写入模拟状态。
            var validated = new SessionDescriptor(Guid.Parse("11111111-1111-1111-1111-111111111111"), 1, 1, participants);
            Participants = validated.Participants; LastSequence = lastSequence;
        }
        /// <summary>包含启用和停用位置的完整名单，按位置排序。</summary>
        public IReadOnlyList<Participant> Participants { get; }
        /// <summary>已经应用的最后一个成员操作编号。</summary>
        public ulong LastSequence { get; }
        /// <summary>根据固定位置查询当前身份、控制权和启用状态。</summary>
        public Participant Find(ushort slot) => Participants.FirstOrDefault(p => p.Slot == slot)
            ?? throw new ArgumentException("Unknown member slot.", nameof(slot));

        /// <summary>验证并应用一帧的成员事件，返回新状态；失败时原实例不变。</summary>
        public MembershipState Apply(IReadOnlyList<MemberEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (events.Count == 0) return this;
            if (events.Count > Participants.Count) throw new ArgumentException("Too many member events.");
            var copy = Participants.ToArray(); var touched = new HashSet<ushort>(); ulong sequence = LastSequence;
            foreach (var e in events)
            {
                if (e == null || e.Sequence != checked(sequence + 1) || !touched.Add(e.Slot))
                    throw new ArgumentException("Member events must be consecutive and affect each slot at most once per frame.");
                int index = Array.FindIndex(copy, p => p.Slot == e.Slot);
                if (index < 0) throw new ArgumentException("Unknown member slot.");
                var old = copy[index];
                bool active = e.Kind != MemberEventKind.Deactivate;
                if (e.Kind == MemberEventKind.Rebind)
                {
                    if (old.IsActive || e.Generation != checked(old.Generation + 1))
                        throw new ArgumentException("Rebind requires an inactive slot and the next generation.");
                }
                else if (old.IsActive == active || e.Generation != old.Generation)
                    throw new ArgumentException("Invalid activation state or control generation.");
                copy[index] = new Participant(old.Slot, old.ParticipantId, e.Generation, active);
                sequence = e.Sequence;
            }
            return new MembershipState(copy, sequence);
        }

        /// <summary>核对帧输入恰好覆盖启用成员，控制权匹配，且每份输入不超过上限。</summary>
        public void ValidateInputs(IReadOnlyList<RecordedInput> inputs, int maxInputBytes)
        {
            int index = 0;
            foreach (var p in Participants)
            {
                if (!p.IsActive) continue;
                if (index >= inputs.Count || inputs[index].Slot != p.Slot || inputs[index].Generation != p.Generation ||
                    inputs[index].Payload.Length > maxInputBytes)
                    throw new ArgumentException("Frame inputs do not match active membership or input byte limit.");
                index++;
            }
            if (index != inputs.Count) throw new ArgumentException("Inactive or unknown member input.");
        }
        /// <summary>逐字段比较，避免对象引用比较掩盖成员或控制权差异。</summary>
        public bool SameAs(MembershipState other) => other != null && LastSequence == other.LastSequence &&
            Participants.Count == other.Participants.Count && Participants.Zip(other.Participants, (a, b) =>
                a.Slot == b.Slot && a.ParticipantId == b.ParticipantId &&
                a.Generation == b.Generation && a.IsActive == b.IsActive).All(x => x);
    }
}
