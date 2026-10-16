using System;
using System.Collections.Generic;

namespace FrameSync.Persistence.Tests
{
    /// <summary>不依赖外部测试包的可观察行为断言。</summary>
    internal static class Assert
    {
        /// <summary>比较两个值；错误信息同时保留预期和实际结果。</summary>
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"预期 {expected}；实际 {actual}。");
        }
        /// <summary>要求指定条件成立。</summary>
        public static void True(bool condition, string message)
        { if (!condition) throw new Exception(message); }
        /// <summary>逐字节比较数据。</summary>
        public static void Bytes(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
        { if (!expected.SequenceEqual(actual)) throw new Exception("字节序列不同。"); }
        /// <summary>要求操作以指定异常失败。</summary>
        public static T Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T error) { return error; }
            throw new Exception($"预期抛出 {typeof(T).Name}。");
        }
    }
}
