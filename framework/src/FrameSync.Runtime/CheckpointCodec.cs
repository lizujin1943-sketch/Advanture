using System;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    /// <summary>完整检查点的稳定二进制格式，包含版本号、框架信息、游戏状态和校验摘要。</summary>
    /// <remarks>
    /// 多字节整数统一小端，Guid 使用 .NET 的固定字节排列；字段和集合顺序都明确规定。
    /// 此类只处理框架结构，游戏状态仍是原样保存的字节，恢复时再由游戏编码器解释。
    /// 当前格式为版本 2，改变字段布局时必须显式处理格式版本兼容。
    /// </remarks>
    public static class CheckpointCodec
    {
        // 按小端写入后的四个字节为 ASCII 标识 FSCP，区别于输入录制的 FSRP。
        private const uint Magic = 0x50435346; // FSCP
        /// <summary>按固定顺序编码完整检查点，超过自身声明的字节上限时失败。</summary>
        /// <param name="checkpoint">运行器捕获或本编码器解码得到的检查点。</param>
        /// <returns>包含格式头和末尾 SHA-256 摘要的独立字节数组。</returns>
        public static byte[] Encode(Checkpoint checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            // 总大小限制包含末尾 32 字节摘要，因此正文写入器先扣除这部分预算。
            using var w = new CanonicalWriter(checkpoint.Limits.MaxCheckpointBytes - DataEnvelope.DigestLength);
            // 字段分组：格式头 → 游戏兼容信息 → 会话和帧号 → 容量 → 成员 → 历史 → 游戏数据。
            // Decode 必须按完全相同的顺序读取，不能依赖对象内存布局或平台默认序列化。
            w.U32(Magic); w.U16(2);
            var m = checkpoint.Manifest;
            w.Text(m.ApplicationId); w.Text(m.RulesVersion); w.Text(m.InputFormatVersion);
            w.Text(m.StateFormatVersion); w.Text(m.ContentHash); w.Text(m.DeterminismHash);
            var s = checkpoint.Session;
            w.Guid(s.SessionId); w.U64(s.Epoch); w.U32(s.TickRate); w.U64(s.BaseTick); w.U64(checkpoint.Tick);
            WriteLimits(w, checkpoint.Limits);
            w.U16((ushort)s.Participants.Count);
            foreach (var p in s.Participants) { w.U16(p.Slot); w.Guid(p.ParticipantId); w.U64(p.Generation); w.U8(p.IsActive ? (byte)1 : (byte)0); }
            WriteMembership(w, checkpoint.Membership);
            WriteMembership(w, checkpoint.HistoryStartMembership);
            // 缺失次数与成员表同序，输入历史也按帧号递增，恢复后框架上下文才会一致。
            foreach (ulong count in checkpoint.MissingInputCounts) w.U64(count);
            w.U32((uint)checkpoint.InputHistory.Count);
            foreach (var frame in checkpoint.InputHistory) InputFormat.Write(w, frame);
            // 初始化数据和当前状态用途不同：先用前者创建环境，再用后者恢复当前进度。
            w.Blob(checkpoint.InitializationData.Span);
            w.Blob(checkpoint.ApplicationState.Span);
            return DataEnvelope.Seal(w.ToArray());
        }

        /// <summary>验证完整检查点的长度、摘要和框架字段，生成独立的检查点对象。</summary>
        /// <param name="bytes">包含头部和摘要的完整检查点字节。</param>
        /// <param name="limits">本次加载允许的上限；为空时使用默认值，不会覆盖文件中的运行配置。</param>
        /// <returns>结构有效的检查点；是否兼容某个游戏模块和运行器，由 Restore 进一步检查。</returns>
        /// <exception cref="InvalidDataException">格式、摘要、容量、成员、帧历史或计数不合法。</exception>
        public static Checkpoint Decode(ReadOnlySpan<byte> bytes, RuntimeLimits? limits = null)
        {
            var cap = limits ?? new RuntimeLimits();
            // 1. 用调用方的上限检查实际总长度和摘要，再进入包含不可信长度字段的正文。
            var r = new CanonicalReader(DataEnvelope.Open(bytes, cap.MaxCheckpointBytes));
            try
            {
                if (r.U32() != Magic || r.U16() != 2) throw new InvalidDataException("Unsupported checkpoint format.");
                var manifest = new CompatibilityManifest(r.Text(), r.Text(), r.Text(), r.Text(), r.Text(), r.Text());
                var sessionId = r.Guid(); ulong epoch = r.U64(); uint tickRate = r.U32();
                ulong baseTick = r.U64(); ulong tick = r.U64();
                _ = checked(baseTick + tick);
                // 2. 文件里的每项容量都不能超过调用方上限，实际大小也不能超过文件自己的声明。
                // 两层检查缺一不可：外层允许 8 MiB 不代表声明只允许 1 MiB 的文件可以更大。
                var stored = ReadLimits(ref r, cap);
                if (bytes.Length > stored.MaxCheckpointBytes)
                    throw new InvalidDataException("Checkpoint exceeds its own declared byte budget.");
                int memberCount = r.U16();
                if (memberCount < 1 || memberCount > stored.MaxParticipants)
                    throw new InvalidDataException("Invalid participant count.");
                // 3. 人数验证后再分配；要求原始字节中的成员位置已严格递增。
                // SessionDescriptor 会再次检查身份唯一性，但不能用它的排序掩盖错误的文件顺序。
                var participants = new Participant[memberCount];
                for (int i = 0; i < memberCount; i++)
                {
                    participants[i] = new Participant(r.U16(), r.Guid(), r.U64(), ReadActive(ref r));
                    if (i > 0 && participants[i].Slot <= participants[i - 1].Slot)
                        throw new InvalidDataException("Participants are not in canonical order.");
                }
                var session = new SessionDescriptor(sessionId, epoch, tickRate, participants, baseTick);
                var membership = ReadMembership(ref r, session);
                var historyStart = ReadMembership(ref r, session);
                if (tick <= (ulong)stored.HistoryFrames && !historyStart.SameAs(new MembershipState(session.Participants)))
                    throw new InvalidDataException("Initial membership does not match the history base.");
                var missing = new ulong[memberCount];
                for (int i = 0; i < memberCount; i++)
                {
                    missing[i] = r.U64();
                    if (missing[i] > tick) throw new InvalidDataException("Impossible missing-input count.");
                }
                // 4. 历史必须是最近的完整窗口，长度恰好等于 min(已执行帧数, 历史容量)。
                // 例如帧 200、窗口 128，应保存帧 73～200，不能缺帧或夹带更早输入。
                int historyCount = r.Count(stored.HistoryFrames);
                if ((ulong)historyCount != Math.Min(tick, (ulong)stored.HistoryFrames))
                    throw new InvalidDataException("Incomplete input history.");
                var history = new InputFrame[historyCount];
                var historicalMembership = historyStart;
                for (int i = 0; i < historyCount; i++)
                {
                    history[i] = InputFormat.Read(ref r, session, stored);
                    historicalMembership = historicalMembership.Apply(history[i].MemberEvents);
                    historicalMembership.ValidateInputs(history[i].Inputs, stored.MaxInputBytes);
                    if (history[i].Tick != tick - (ulong)historyCount + 1 + (ulong)i)
                        throw new InvalidDataException("Nonconsecutive history.");
                }
                // 5. 缺失计数必须与保存的输入来源相符；随后读取游戏数据并拒绝尾随字节。
                if (!historicalMembership.SameAs(membership)) throw new InvalidDataException("Membership disagrees with history.");
                ValidateMissing(missing, history, membership);
                var initialization = new ByteString(r.Blob(stored.MaxCheckpointBytes));
                byte[] state = r.Blob(stored.MaxCheckpointBytes).ToArray();
                r.Finish();
                return new Checkpoint(manifest, session, stored, tick, history, missing, state, initialization, membership, historyStart);
            }
            // 将构造器参数检查和算术溢出统一转换成坏检查点数据，保留原始异常便于诊断。
            catch (ArgumentException e) { throw new InvalidDataException("Invalid checkpoint fields.", e); }
            catch (OverflowException e) { throw new InvalidDataException("Checkpoint integer overflow.", e); }
        }

        /// <summary>获取完整检查点的稳定摘要，用于恢复和跨进程重放的一致性比较。</summary>
        /// <param name="checkpoint">待比较的完整检查点。</param>
        /// <returns>SHA-256 摘要的小写十六进制文本，共 64 个字符。</returns>
        /// <remarks>
        /// 摘要包含会话、版本、输入来源和历史等框架信息，不只是游戏状态。
        /// 因此游戏数值相同但会话身份不同，摘要也可能不同；它不等同于对象 GetHashCode。
        /// </remarks>
        public static string Hash(Checkpoint checkpoint)
        {
            byte[] bytes = Encode(checkpoint);
            return DataEnvelope.Hex(bytes.AsSpan(bytes.Length - DataEnvelope.DigestLength));
        }

        /// <summary>利用最近输入的连续补齐后缀，检查各参与者的缺失计数是否可能成立。</summary>
        /// <param name="missing">截至当前帧的连续缺失次数。</param>
        /// <param name="history">已验证连续性的近期输入，旧帧在前。</param>
        private static void ValidateMissing(ulong[] missing, InputFrame[] history, MembershipState membership)
        {
            for (int index = 0; index < missing.Length; index++)
            {
                var member = membership.Participants[index];
                ulong suffix = 0; bool resetSeen = !member.IsActive;
                if (member.IsActive)
                {
                    for (int i = history.Length - 1; i >= 0; i--)
                    {
                        var input = history[i].Inputs.FirstOrDefault(x => x.Slot == member.Slot);
                        if (input == null || input.Origin == InputOrigin.Submitted) { resetSeen = true; break; }
                        suffix++;
                        if (history[i].MemberEvents.Any(e => e.Slot == member.Slot)) { resetSeen = true; break; }
                    }
                }
                if (resetSeen ? missing[index] != suffix : missing[index] < suffix)
                    throw new InvalidDataException("Missing-input counter disagrees with membership/history.");
            }
        }

        /// <summary>成员动态状态只保存版本与启用位；稳定身份仍来自固定会话名单。</summary>
        private static void WriteMembership(CanonicalWriter w, MembershipState state)
        {
            w.U64(state.LastSequence);
            foreach (var p in state.Participants) { w.U64(p.Generation); w.U8(p.IsActive ? (byte)1 : (byte)0); }
        }
        private static MembershipState ReadMembership(ref CanonicalReader r, SessionDescriptor session)
        {
            ulong sequence = r.U64(); var members = new Participant[session.Participants.Count];
            for (int i = 0; i < members.Length; i++)
            {
                var original = session.Participants[i]; ulong generation = r.U64(); bool active = ReadActive(ref r);
                if (generation < original.Generation || generation - original.Generation > sequence)
                    throw new InvalidDataException("Invalid membership generation.");
                members[i] = new Participant(original.Slot, original.ParticipantId, generation, active);
            }
            return new MembershipState(members, sequence);
        }
        private static bool ReadActive(ref CanonicalReader r)
        {
            byte value = r.U8();
            if (value > 1) throw new InvalidDataException("Invalid membership flag.");
            return value == 1;
        }

        /// <summary>按固定顺序写入六项运行限制，与 ReadLimits 一一对应。</summary>
        /// <param name="w">正文写入器。</param>
        /// <param name="limits">检查点所属运行的容量配置。</param>
        internal static void WriteLimits(CanonicalWriter w, RuntimeLimits limits)
        {
            w.U32((uint)limits.MaxParticipants); w.U32((uint)limits.MaxInputBytes);
            w.U32((uint)limits.MaxCheckpointBytes); w.U32((uint)limits.HistoryFrames);
            w.U32((uint)limits.MaxEventsPerFrame); w.U32((uint)limits.MaxEventBytes);
        }
        /// <summary>逐项读取文件声明的限制，每项都先受调用方上限约束，再检查配置合法性。</summary>
        /// <param name="r">共享的正文读取位置。</param>
        /// <param name="cap">加载方允许的最大容量配置。</param>
        /// <returns>文件中原有的运行限制，不将其放宽到加载方的上限。</returns>
        private static RuntimeLimits ReadLimits(ref CanonicalReader r, RuntimeLimits cap) =>
            new RuntimeLimits(r.Count(cap.MaxParticipants), r.Count(cap.MaxInputBytes),
                r.Count(cap.MaxCheckpointBytes), r.Count(cap.HistoryFrames),
                r.Count(cap.MaxEventsPerFrame), r.Count(cap.MaxEventBytes));

        /// <summary>恢复到现有运行器时，要求容量配置逐项相同，保持历史窗口等行为一致。</summary>
        /// <param name="a">第一份运行限制。</param>
        /// <param name="b">第二份运行限制。</param>
        /// <returns>六项配置全部相等时为 true。</returns>
        internal static bool SameLimits(RuntimeLimits a, RuntimeLimits b) =>
            a.MaxParticipants == b.MaxParticipants && a.MaxInputBytes == b.MaxInputBytes &&
            a.MaxCheckpointBytes == b.MaxCheckpointBytes && a.HistoryFrames == b.HistoryFrames &&
            a.MaxEventsPerFrame == b.MaxEventsPerFrame && a.MaxEventBytes == b.MaxEventBytes;

        /// <summary>比较完整会话配置，不能只依赖会话身份相同。</summary>
        /// <param name="a">第一份会话配置。</param>
        /// <param name="b">第二份会话配置。</param>
        /// <returns>身份、运行编号、时间基准和每位参与者的控制信息全部一致时为 true。</returns>
        internal static bool SameSession(SessionDescriptor a, SessionDescriptor b) =>
            a.SessionId == b.SessionId && a.Epoch == b.Epoch && a.TickRate == b.TickRate &&
            a.BaseTick == b.BaseTick && a.Participants.Count == b.Participants.Count &&
            a.Participants.Zip(b.Participants, (x, y) =>
                x.Slot == y.Slot && x.ParticipantId == y.ParticipantId && x.Generation == y.Generation && x.IsActive == y.IsActive).All(x => x);
    }
}
