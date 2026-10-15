using System;
using System.Collections.Generic;

namespace FrameSync.Abstractions
{
    /// <summary>创建独立游戏模拟实例的工厂，供首次运行和检查点恢复使用。</summary>
    /// <typeparam name="TState">由游戏定义的完整状态类型，框架不依赖其具体字段。</typeparam>
    public interface ISimulationFactory<TState>
    {
        /// <summary>每次创建一个尚未初始化的独立模拟实例。</summary>
        /// <returns>新的非空实例，随后由运行器调用 Initialize。</returns>
        /// <remarks>
        /// 不得返回旧实例；实例之间不能共享可变世界或随机数进度。
        /// 恢复时框架会先操作新实例，成功后才替换旧实例，因此隔离是恢复可靠性的前提。
        /// </remarks>
        IDeterministicSimulation<TState> Create();
    }

    /// <summary>游戏提供的计算逻辑：同样的完整起点和输入必须得到同样的状态及事件。</summary>
    /// <typeparam name="TState">包含所有影响后续计算数据的游戏状态类型。</typeparam>
    /// <remarks>
    /// 此接口不依赖引擎和平台。实现方不能让系统时间、设备输入、异步结果或未保存的
    /// 随机状态进入计算；音效、成就、文件写入等外部操作应交给调用层处理。
    /// </remarks>
    public interface IDeterministicSimulation<TState>
    {
        /// <summary>根据会话和游戏参数建立初始状态，不读取外部环境。</summary>
        /// <param name="initialization">参与者名单、逻辑帧率和游戏自定义初始化字节。</param>
        /// <remarks>正常创建和恢复新实例时都会调用；恢复流程随后再调用 Restore。</remarks>
        void Initialize(SimulationInitialization initialization);
        /// <summary>按给定输入推进一帧，并返回本帧产生的事件数据。</summary>
        /// <param name="context">本帧的整数帧号、累计帧号和逻辑帧率。</param>
        /// <param name="inputs">已按参与者位置排序、格式已验证的完整输入列表。</param>
        /// <returns>非 null 的事件列表；没有事件时返回空列表。顺序也必须确定。</returns>
        /// <remarks>
        /// 输入格式正确不代表动作在当前游戏状态下可执行，业务规则仍由此方法判断。
        /// 执行中抛异常可能已修改游戏状态，运行器会停止继续推进，等待显式恢复。
        /// </remarks>
        IReadOnlyList<ByteString> Step(FrameContext context, IReadOnlyList<SimulationInput> inputs);
        /// <summary>捕获足以从当前时刻继续计算的全部状态，不改变当前模拟。</summary>
        /// <returns>独立状态，不得暴露仍在变化的内部对象或集合。</returns>
        /// <remarks>随机数进度、计时器和未来会影响计算的数据都必须包含在内。</remarks>
        TState Capture();
        /// <summary>把完整状态复制进当前实例，使下一帧可以从保存位置继续执行。</summary>
        /// <param name="state">通过游戏编码器解码得到的完整状态。</param>
        /// <remarks>不得修改传入对象或保留它的可变引用；不得通过全局状态影响其他实例。</remarks>
        void Restore(TState state);
    }

    /// <summary>游戏状态与稳定字节格式之间的转换约定。</summary>
    /// <typeparam name="TState">游戏的完整状态类型。</typeparam>
    /// <remarks>
    /// 同一状态必须编码成完全相同的字节：明确字段顺序、整数宽度、字节序和集合顺序。
    /// 框架不使用引擎对象序列化，也不会自动推断哪些游戏字段需要保存。
    /// </remarks>
    public interface IStateCodec<TState>
    {
        /// <summary>将状态编码为独立字节数组，不修改传入状态。</summary>
        /// <param name="state">待保存或比较的游戏状态。</param>
        /// <returns>非 null 的独立字节数组；长度由游戏格式决定，相同状态必须产生相同字节。</returns>
        byte[] Encode(TState state);
        /// <summary>检查游戏状态格式并还原独立对象。</summary>
        /// <param name="bytes">游戏状态字节，不包含框架的会话和检查点头部。</param>
        /// <returns>完整状态对象，不能与正在运行的世界共享可变数据。</returns>
        /// <remarks>应拒绝截断、额外字节和非法值，并自行限制解码需要的内存。</remarks>
        TState Decode(ReadOnlySpan<byte> bytes);
    }

    /// <summary>检查游戏输入是否采用唯一、合法的字节格式。</summary>
    /// <remarks>
    /// 检查不能依赖当前世界，也不能修改状态，例如只检查命令编号、字段长度和数值范围。
    /// 当前状态下能否执行命令由 Step 判断；预测和缺失输入策略在后续阶段加入。
    /// </remarks>
    public interface IInputValidator
    {
        /// <summary>验证一份输入；不合法时抛异常，合法时直接返回。</summary>
        /// <param name="bytes">某位参与者本帧的输入内容。</param>
        void ValidateCanonical(ReadOnlySpan<byte> bytes);
    }

    /// <summary>把一个游戏接入框架所需的实现组合成模块。</summary>
    /// <typeparam name="TState">该游戏的完整状态类型。</typeparam>
    /// <remarks>
    /// 模块持有兼容信息、工厂、编码器和输入检查器，运行器只通过这些约定调用游戏。
    /// 这些服务可以由同一个无状态对象实现；可变游戏状态应留在工厂创建的实例中。
    /// </remarks>
    public sealed class SimulationModule<TState>
    {
        /// <summary>建立游戏模块，拒绝缺少任一接入组件的配置。</summary>
        /// <param name="manifest">游戏身份及版本兼容信息。</param>
        /// <param name="factory">创建独立计算实例的工厂。</param>
        /// <param name="codec">游戏完整状态的编码器。</param>
        /// <param name="inputValidator">无副作用的输入格式检查器。</param>
        public SimulationModule(CompatibilityManifest manifest, ISimulationFactory<TState> factory,
            IStateCodec<TState> codec, IInputValidator inputValidator)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            Codec = codec ?? throw new ArgumentNullException(nameof(codec));
            InputValidator = inputValidator ?? throw new ArgumentNullException(nameof(inputValidator));
        }
        /// <summary>恢复检查点时必须匹配的游戏兼容信息。</summary>
        public CompatibilityManifest Manifest { get; }
        /// <summary>创建彼此隔离的游戏计算实例。</summary>
        public ISimulationFactory<TState> Factory { get; }
        /// <summary>游戏完整状态与字节之间的转换实现。</summary>
        public IStateCodec<TState> Codec { get; }
        /// <summary>推进之前使用的游戏输入格式检查器。</summary>
        public IInputValidator InputValidator { get; }
    }
}
