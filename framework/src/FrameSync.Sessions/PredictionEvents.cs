using FrameSync.Abstractions;

namespace FrameSync.Sessions
{
    /// <summary>表现层对一个稳定事件身份的三种处理通知。</summary>
    public enum PredictionEventKind
    {
        /// <summary>暂时呈现；例如可取消的动画。不要据此发放成就或落盘。</summary>
        Predicted,
        /// <summary>原预测消失或内容改变；撤销之前呈现的效果。</summary>
        Invalidated,
        /// <summary>服务器输入与完整状态摘要已经匹配；同一事件只确认一次。</summary>
        Confirmed
    }
    /// <summary>一次事件变化。身份取 SimulationEvent 的会话、运行、帧和序号；内容可能在重算后变化。</summary>
    public sealed class PredictionEvent
    {
        internal PredictionEvent(PredictionEventKind kind, SimulationEvent value) { Kind = kind; Event = value; }
        /// <summary>调用方应执行的呈现/撤销/确认操作。</summary>
        public PredictionEventKind Kind { get; }
        /// <summary>对应的不可变模拟事件。</summary>
        public SimulationEvent Event { get; }
    }
}
