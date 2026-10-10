using System;
using System.Collections.Generic;

namespace FrameSync.Tests
{
    internal static class Assert
    {
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"Expected {expected}; actual {actual}.");
        }
        public static void True(bool value, string message = "Expected true.")
        { if (!value) throw new Exception(message); }
        public static void Bytes(byte[] expected, byte[] actual)
        { if (!expected.AsSpan().SequenceEqual(actual)) throw new Exception("Byte sequences differ."); }
        public static T Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T error) { return error; }
            throw new Exception($"Expected {typeof(T).Name}.");
        }
    }
}
