using System;
using System.IO;
using Advanture.Tactical.Network;
using FrameSync.Diagnostics;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>试玩的诊断接入层。房主分别记录服务器和本地客户端，另一端只记录自己的客户端。</summary>
    /// <remarks>不解码游戏状态；通用记录和分歧证据由 FrameSync.Diagnostics 负责。</remarks>
    public sealed class YardDiagnostics : IDisposable
    {
        private readonly SessionDiagnostics clientLog;
        private readonly SessionDiagnostics serverLog;
        public YardDiagnostics(string root, bool host, string environment)
        {
            clientLog = new SessionDiagnostics(Path.Combine(root, host ? "client-A" : "client-B"), host ? "client-A" : "client-B", environment);
            if (host) serverLog = new SessionDiagnostics(Path.Combine(root, "server"), "server", environment);
        }
        public string ClientDirectory => clientLog.DirectoryPath;
        public string ServerDirectory => serverLog?.DirectoryPath;
        public string Error => clientLog.Error ?? serverLog?.Error;

        /// <summary>紧接网络 Poll 调用，包括失败和等待初态期间；先记录核心终态，再补充房间层失败。</summary>
        public void Observe(TacticalClient client, TacticalHost host, TimeSpan elapsed)
        {
            clientLog.ObserveClient(client?.Session, elapsed);
            serverLog?.ObserveServer(host?.Session, elapsed);
            // 核心还没建立会话时，地图不匹配、连接超时等错误也不能漏掉。
            // End 幂等：不会把先记录的 Desync/SessionClosed 覆盖成泛化的连接错误。
            if (client?.Failure != null) clientLog.End(client.Session == null ? "ConnectionFailed" : "TransportFailure", client.Failure);
            if (host?.Failure != null) serverLog?.End(host.Session == null ? "ConnectionFailed" : "TransportFailure", host.Failure);
        }
        /// <summary>清理房间前调用；保留各自已经记录的首个失败原因。</summary>
        public void End(string state, string reason = null) { clientLog.End(state, reason); serverLog?.End(state, reason); }
        public void Dispose() { clientLog.Dispose(); serverLog?.Dispose(); }
    }
}
