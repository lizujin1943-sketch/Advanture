using System;
using System.Text;

namespace FrameSync.Abstractions
{
    /// <summary>描述一份状态或录制所对应的游戏及其规则版本。</summary>
    /// <remarks>
    /// 恢复前要求所有字段完全一致。各版本和摘要由游戏接入方维护，
    /// 框架不会扫描游戏代码或资源来自动计算它们，也不会自动迁移旧状态。
    /// </remarks>
    public sealed class CompatibilityManifest : IEquatable<CompatibilityManifest>
    {
        /// <summary>建立兼容清单；每个字段都必须非空白且不超过 128 个 UTF-8 字节。</summary>
        /// <param name="applicationId">游戏或应用的稳定标识，避免不同游戏混用状态。</param>
        /// <param name="rulesVersion">游戏计算规则的版本。</param>
        /// <param name="inputFormatVersion">输入字节格式的版本。</param>
        /// <param name="stateFormatVersion">游戏状态字节格式的版本。</param>
        /// <param name="contentHash">影响计算的配置和资源摘要；无此类内容时可用默认标记。</param>
        /// <param name="determinismHash">影响确定性的算法版本标记或摘要，例如随机数算法版本。</param>
        public CompatibilityManifest(string applicationId, string rulesVersion = "1",
            string inputFormatVersion = "1", string stateFormatVersion = "1",
            string contentHash = "no-content", string determinismHash = "integer-v1")
        {
            ApplicationId = Check(applicationId);
            RulesVersion = Check(rulesVersion);
            InputFormatVersion = Check(inputFormatVersion);
            StateFormatVersion = Check(stateFormatVersion);
            ContentHash = Check(contentHash);
            DeterminismHash = Check(determinismHash);
        }
        /// <summary>游戏或应用的稳定标识。</summary>
        public string ApplicationId { get; }
        /// <summary>游戏计算规则的版本。</summary>
        public string RulesVersion { get; }
        /// <summary>游戏输入的编码格式版本。</summary>
        public string InputFormatVersion { get; }
        /// <summary>游戏状态的编码格式版本。</summary>
        public string StateFormatVersion { get; }
        /// <summary>影响游戏计算的配置和资源摘要，由接入方提供。</summary>
        public string ContentHash { get; }
        /// <summary>确定性算法的版本标记或摘要，由接入方提供。</summary>
        public string DeterminismHash { get; }
        /// <summary>按实际编码字节数检查字段，避免字符数较少但编码后超出格式上限。</summary>
        private static string Check(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || new UTF8Encoding(false, true).GetByteCount(value) > 128)
                throw new ArgumentException("Manifest fields must contain 1..128 UTF-8 bytes.");
            return value;
        }
        /// <summary>按大小写敏感的字符串内容比较全部兼容字段。</summary>
        /// <param name="other">另一个清单；为空时不相等。</param>
        /// <returns>所有字段完全一致时为 true。</returns>
        public bool Equals(CompatibilityManifest? other) => other != null &&
            ApplicationId == other.ApplicationId && RulesVersion == other.RulesVersion &&
            InputFormatVersion == other.InputFormatVersion && StateFormatVersion == other.StateFormatVersion &&
            ContentHash == other.ContentHash && DeterminismHash == other.DeterminismHash;
        /// <summary>将对象作为兼容清单进行完整比较。</summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>对象类型正确且所有字段相等时为 true。</returns>
        public override bool Equals(object? obj) => Equals(obj as CompatibilityManifest);
        /// <summary>为当前进程的字典等集合提供哈希码。</summary>
        /// <remarks>
        /// 这里只使用应用标识，因此不同版本可以产生相同哈希码，最终仍由 Equals 区分。
        /// 字符串哈希不保证跨进程稳定，不能用于网络一致性检查或持久化摘要。
        /// </remarks>
        /// <returns>应用标识的进程内哈希码。</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ApplicationId);
    }
}
