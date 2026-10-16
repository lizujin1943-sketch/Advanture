using System;
using FrameSync.Runtime;

namespace FrameSync.Persistence
{
    /// <summary>将确认检查点与存储接口连接起来，不依赖引擎、文件系统或具体游戏。</summary>
    /// <remarks>
    /// 检查点本身不标记“确认”或“预测”；宿主必须传入权威会话捕获的确认状态。
    /// 此服务不推进或恢复运行中的模拟，不关闭会话，也不读取系统时间或分配存档身份。
    /// 存储失败时异常直接传给宿主，由宿主决定重试；只有 Save 正常返回才表示提交成功。
    /// </remarks>
    public sealed class SaveService
    {
        private readonly ISaveStore _store;
        private readonly RuntimeLimits _limits;
        private readonly int _maxBytes;

        /// <summary>建立使用指定存储适配器和固定容量预算的存档服务。</summary>
        /// <param name="store">提供完整读和原子写的存储实现。</param>
        /// <param name="limits">允许加载或保存的检查点上限，缺省使用默认值。</param>
        public SaveService(ISaveStore store, RuntimeLimits? limits = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _limits = limits ?? new RuntimeLimits();
            _maxBytes = SaveCodec.GetMaximumEncodedBytes(_limits);
        }

        /// <summary>先编码并校验确认检查点，再将整份字节交给存储原子提交。</summary>
        /// <param name="saveId">由宿主指定、覆盖同一存档时保持稳定的非空身份。</param>
        /// <param name="confirmedCheckpoint">权威会话已确认帧结束后的完整检查点。</param>
        /// <returns>已成功提交的存档；抛异常时不得据此关闭仍需保存的会话。</returns>
        public SaveDocument Save(Guid saveId, Checkpoint confirmedCheckpoint)
        {
            var document = new SaveDocument(saveId, confirmedCheckpoint);
            byte[] bytes = SaveCodec.Encode(document, _limits);
            // 在任何外部写入之前完整解码一次，保证当前加载规则确实接受这份数据。
            // 此步骤只创建候选对象，不修改调用者的检查点和运行状态。
            _ = SaveCodec.Decode(bytes, _limits);
            _store.WriteAtomic(bytes, _maxBytes);
            return document;
        }

        /// <summary>把读取预算传到适配器，在核心重新检查格式和容量后才返回候选存档。</summary>
        /// <returns>已验证的存档；新会话身份建立和游戏恢复由宿主的重开流程负责。</returns>
        public SaveDocument Load() => SaveCodec.Decode(_store.Read(_maxBytes), _limits);
    }
}
