using System;
using System.Linq;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    /// <summary>把一份确认存档转换为新会话的 S[0]；不读取文件，也不创建网络连接。</summary>
    public static class CheckpointRestart
    {
        /// <summary>保留游戏状态和累计时间，使用新会话身份重新开局。</summary>
        /// <param name="saved">已经完整校验的确认检查点。</param>
        /// <param name="newSessionId">宿主新生成的非空身份，必须不同于原会话。</param>
        /// <param name="epoch">新会话的运行编号，通常从 1 开始。</param>
        /// <remarks>固定位置和稳定身份保留；控制权加一、全部暂未启用，开局首帧再激活。
        /// 清空本轮输入历史和缺失计数，BaseTick 继承累计进度。游戏的永久计时应使用 PersistentTick。</remarks>
        public static Checkpoint Create(Checkpoint saved, Guid newSessionId, ulong epoch = 1)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            if (newSessionId == Guid.Empty || newSessionId == saved.Session.SessionId)
                throw new ArgumentException("A restarted world requires a fresh session identity.", nameof(newSessionId));
            var participants = saved.Membership.Participants.Select(p =>
                new Participant(p.Slot, p.ParticipantId, checked(p.Generation + 1), false)).ToArray();
            var session = new SessionDescriptor(newSessionId, epoch, saved.Session.TickRate, participants, saved.PersistentTick);
            var membership = new MembershipState(participants);
            var initial = new Checkpoint(saved.Manifest, session, saved.Limits, 0, Array.Empty<InputFrame>(),
                new ulong[participants.Length], saved.ApplicationState.ToArray(), saved.InitializationData, membership, membership);
            _ = CheckpointCodec.Encode(initial);
            return initial;
        }
    }
}
