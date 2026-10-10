using System;
using System.Text;

namespace FrameSync.Abstractions
{
    public sealed class CompatibilityManifest : IEquatable<CompatibilityManifest>
    {
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
        public string ApplicationId { get; }
        public string RulesVersion { get; }
        public string InputFormatVersion { get; }
        public string StateFormatVersion { get; }
        public string ContentHash { get; }
        public string DeterminismHash { get; }
        private static string Check(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || new UTF8Encoding(false, true).GetByteCount(value) > 128)
                throw new ArgumentException("Manifest fields must contain 1..128 UTF-8 bytes.");
            return value;
        }
        public bool Equals(CompatibilityManifest? other) => other != null &&
            ApplicationId == other.ApplicationId && RulesVersion == other.RulesVersion &&
            InputFormatVersion == other.InputFormatVersion && StateFormatVersion == other.StateFormatVersion &&
            ContentHash == other.ContentHash && DeterminismHash == other.DeterminismHash;
        public override bool Equals(object? obj) => Equals(obj as CompatibilityManifest);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ApplicationId);
    }
}
