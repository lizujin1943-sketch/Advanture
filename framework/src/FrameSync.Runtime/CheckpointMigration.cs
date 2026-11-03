using System;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    /// <summary>Explicit host-owned application-state migration; the framework does not infer game rules.</summary>
    public static class CheckpointMigration
    {
        /// <summary>Replace validated application bytes and their manifest, preserving session progress and history.</summary>
        /// <remarks>
        /// The host must recognize the source version and validate the migrated game state before restoring it.
        /// Input format and application identity must remain unchanged because existing input history is retained.
        /// No simulation steps run, files are written, or live sessions modified by this operation.
        /// </remarks>
        public static Checkpoint WithApplicationState(Checkpoint source,CompatibilityManifest target,ByteString state)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(state==null)throw new ArgumentNullException(nameof(state));
            if(source.Manifest.ApplicationId!=target.ApplicationId || source.Manifest.InputFormatVersion!=target.InputFormatVersion)
                throw new ArgumentException("Application-state migration cannot reinterpret existing input history.",nameof(target));
            var migrated=new Checkpoint(target,source.Session,source.Limits,source.Tick,source.InputHistory,
                source.MissingInputCounts,state.ToArray(),source.InitializationData,source.Membership,source.HistoryStartMembership);
            return CheckpointCodec.Decode(CheckpointCodec.Encode(migrated),source.Limits);
        }
    }
}
