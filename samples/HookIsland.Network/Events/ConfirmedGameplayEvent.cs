using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Network;

/// <summary>A confirmed result with the framework's stable identity; never a prediction notification.</summary>
public readonly struct ConfirmedGameplayEvent
{
    public ConfirmedGameplayEvent(Guid sessionId,ulong epoch,ulong tick,int index,HookGameplayEvent value)
    { SessionId=sessionId;Epoch=epoch;Tick=tick;Index=index;Event=value; }
    public Guid SessionId { get; }
    public ulong Epoch { get; }
    public ulong Tick { get; }
    public int Index { get; }
    public HookGameplayEvent Event { get; }
}

/// <summary>One Poll's bounded batch. Confirmed frames arrive in increasing tick order;
/// a watermark for the full session/epoch identity avoids an unbounded historical ID set.
/// Checkpoint bootstrap produces no frames, so restored history is never enumerated.
/// </summary>
internal sealed class ConfirmedGameplayEventBatch
{
    private readonly List<ConfirmedGameplayEvent> values=new List<ConfirmedGameplayEvent>();
    private Guid sessionId;
    private ulong epoch,lastTick;
    private bool hasFrame;
    internal ConfirmedGameplayEventBatch(){Values=values.AsReadOnly();}
    internal IReadOnlyList<ConfirmedGameplayEvent> Values { get; }
    internal void BeginPoll()=>values.Clear();
    internal void Reset(Guid sessionId,ulong epoch)
    { this.sessionId=sessionId;this.epoch=epoch;lastTick=0;hasFrame=false;values.Clear(); }
    internal void Add(IReadOnlyList<FrameResult> frames)
    {
        foreach(var frame in frames)
        {
            if(hasFrame && frame.Tick<=lastTick)continue;
            foreach(var value in frame.Events)
            {
                if(value.SessionId!=sessionId || value.Epoch!=epoch || value.Tick!=frame.Tick)
                    throw new InvalidOperationException("Confirmed event identity does not match the current frame.");
                values.Add(new ConfirmedGameplayEvent(value.SessionId,value.Epoch,value.Tick,value.Index,
                    HookGameplayEventCodec.Decode(value.Payload.Span)));
            }
            lastTick=frame.Tick;hasFrame=true;
        }
    }
}
