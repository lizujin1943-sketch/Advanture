using System;
using System.Runtime.Serialization;

namespace FrameSync.Diagnostics
{
    // 使用 .NET 标准库 JSON 编码器处理中文、换行和引号；不依赖 Unity.JsonUtility 或游戏类型。
    // 字段名就是磁盘格式，schemaVersion 变化时由读取工具决定如何兼容。
    [DataContract]
    internal sealed class DiagnosticEntry
    {
        [DataMember] public int schemaVersion = 1;
        [DataMember] public string runId = "";
        [DataMember] public string role = "";
        [DataMember] public string environment = "";
        [DataMember] public string recordKind = "started";
        [DataMember] public string utc = "";
        [DataMember] public long elapsedMilliseconds;
        [DataMember] public string sessionStatus = "Connecting";
        [DataMember] public string? sessionId;
        [DataMember] public ulong epoch;
        [DataMember] public uint tickRate;
        [DataMember] public int localSlot = -1;
        [DataMember] public string? applicationId;
        [DataMember] public string? rulesVersion;
        [DataMember] public ulong confirmedTick;
        [DataMember] public string? confirmedHash;
        [DataMember] public ulong predictedTick;
        [DataMember] public ulong rollbackCount;
        [DataMember] public ulong lastRestoreTick;
        [DataMember] public ulong rejectedMessages;
        [DataMember] public bool finished;
        [DataMember] public string? endState;
        [DataMember] public string? reason;
        [DataMember] public bool desyncDetected;
        [DataMember] public ulong? problemTick;
    }
    [DataContract]
    internal sealed class DiagnosticDesync
    {
        [DataMember] public int schemaVersion = 1;
        [DataMember] public string sessionId = "";
        [DataMember] public ulong epoch;
        [DataMember] public ulong problemTick;
        [DataMember] public ulong lastConfirmedTick;
        [DataMember] public string expectedHash = "";
        [DataMember] public string actualHash = "";
    }
    [DataContract]
    internal sealed class DiagnosticFrame
    {
        [DataMember] public string sessionId = "";
        [DataMember] public ulong epoch;
        [DataMember] public ulong tick;
        [DataMember] public DiagnosticInput[] inputs = Array.Empty<DiagnosticInput>();
        [DataMember] public DiagnosticMemberEvent[] memberEvents = Array.Empty<DiagnosticMemberEvent>();
    }
    [DataContract]
    internal sealed class DiagnosticInput
    {
        [DataMember] public ushort slot;
        [DataMember] public ulong generation;
        [DataMember] public string origin = "";
        [DataMember] public string payloadBase64 = "";
    }
    [DataContract]
    internal sealed class DiagnosticMemberEvent
    {
        [DataMember] public ulong sequence;
        [DataMember] public string kind = "";
        [DataMember] public ushort slot;
        [DataMember] public ulong generation;
    }
}
