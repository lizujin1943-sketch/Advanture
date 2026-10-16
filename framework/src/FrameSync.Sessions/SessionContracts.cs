using System;

namespace FrameSync.Sessions
{
    /// <summary>会话生命周期。Ready 表示客户端恢复了初态，但仍在等服务器开局。</summary>
    public enum SessionStatus
    {
        /// <summary>正在分发或接收初始检查点。</summary>
        Preparing,
        /// <summary>客户端已恢复初态，等待服务器开局。</summary>
        Ready,
        /// <summary>执行服务器确认的帧；客户端仅在 IsClosing 为 false 时还可提交输入。</summary>
        Running,
        /// <summary>已停止计算；服务器仍需 Poll 排空关闭消息。</summary>
        Closed,
        /// <summary>发生无法继续的错误；通过 Failure 读取原因。</summary>
        Failed,
        /// <summary>服务器已冻结确认状态等待保存成功；保存失败后可重试，不继续计算。</summary>
        Saving
    }

    /// <summary>宿主预先验证的连接与控制位置绑定；传输连接编号与游戏玩家身份分开。</summary>
    public sealed class PeerBinding
    {
        /// <summary>绑定一个非零连接编号与名单中的位置。</summary>
        public PeerBinding(ulong connectionId, ushort slot)
        { if (connectionId == 0) throw new ArgumentOutOfRangeException(nameof(connectionId)); ConnectionId = connectionId; Slot = slot; }
        /// <summary>传输连接编号。</summary>
        public ulong ConnectionId { get; }
        /// <summary>该连接有权提交输入的位置；玩家稳定身份和控制代数从名单读取。</summary>
        public ushort Slot { get; }
    }
}
