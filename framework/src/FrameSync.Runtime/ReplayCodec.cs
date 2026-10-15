using System;
using System.IO;

namespace FrameSync.Runtime
{
    /// <summary>输入录制的二进制编码：文件头、完整起点、连续输入和 SHA-256 校验摘要。</summary>
    /// <remarks>
    /// 只转换字节，不读写文件；文件读取前的大小检查由调用层完成。
    /// 录制容量 ReplayLimits 不写入文件，加载时采用调用方给出的限制；
    /// 起点中的 RuntimeLimits 则属于检查点内容，会被保留并验证。
    /// </remarks>
    public static class ReplayCodec
    {
        // 按小端写入后的四个字节是 ASCII 标识 FSRP，用于区分输入录制与检查点。
        private const uint Magic = 0x50525346; // FSRP

        /// <summary>将录制写成版本 1 的稳定字节格式，并在末尾附加校验摘要。</summary>
        /// <param name="recording">由录制器或解码器生成的完整录制。</param>
        /// <returns>可以交给文件层保存的独立字节数组。</returns>
        public static byte[] Encode(ReplayRecording recording)
        {
            if (recording == null) throw new ArgumentNullException(nameof(recording));
            // 写入正文之前为末尾摘要预留空间，让限制覆盖整个文件。
            using var w = new CanonicalWriter(recording.Limits.MaxBytes - DataEnvelope.DigestLength);
            w.U32(Magic); w.U16(1);
            w.Blob(CheckpointCodec.Encode(recording.Initial));
            w.U32((uint)recording.Frames.Count);
            foreach (var frame in recording.Frames) InputFormat.Write(w, frame);
            return DataEnvelope.Seal(w.ToArray());
        }

        /// <summary>验证录制结构、容量和连续输入，生成可重放对象。</summary>
        /// <param name="bytes">包含文件头和摘要的完整录制字节。</param>
        /// <param name="limits">本次加载允许的录制容量；为空时使用默认值。</param>
        /// <param name="checkpointLimits">本次加载允许的起点检查点容量；为空时使用默认值。</param>
        /// <returns>列表独立且结构合法的录制；游戏输入内容仍需由重放时的模块验证。</returns>
        /// <exception cref="InvalidDataException">格式、摘要、长度、帧顺序或容量不合法。</exception>
        public static ReplayRecording Decode(ReadOnlySpan<byte> bytes, ReplayLimits? limits = null,
            RuntimeLimits? checkpointLimits = null)
        {
            var cap = limits ?? new ReplayLimits(); var stateCap = checkpointLimits ?? new RuntimeLimits();
            // 先检查总长度和摘要，再相信正文中的长度字段并分配数据。
            var r = new CanonicalReader(DataEnvelope.Open(bytes, cap.MaxBytes));
            try
            {
                if (r.U32() != Magic || r.U16() != 1) throw new InvalidDataException("Unsupported recording format.");
                var initial = CheckpointCodec.Decode(r.Blob(stateCap.MaxCheckpointBytes), stateCap);
                int count = r.Count(cap.MaxFrames);
                // 即使输入内容为空，每帧仍有 10 字节帧头，每人仍有 15 字节记录头。
                // 用剩余字节数检查声明帧数，尽早拒绝声称包含大量帧的短文件。
                long minimumFrameBytes = 10L + initial.Session.Participants.Count * 15L;
                if (count > r.Remaining / minimumFrameBytes)
                    throw new InvalidDataException("Frame count cannot fit in the remaining data.");
                // 复用正常录制时的检查，保证加载也遵守相同的帧连续性和容量规则。
                var recorder = new ReplayRecorder(initial, cap);
                for (int i = 0; i < count; i++) recorder.Append(InputFormat.Read(ref r, initial.Session, initial.Limits));
                r.Finish();
                return recorder.Build();
            }
            // 对外统一报告为坏录制数据，并保留内部异常便于诊断。
            catch (ArgumentException e) { throw new InvalidDataException("Invalid recording fields.", e); }
            catch (OverflowException e) { throw new InvalidDataException("Recording integer overflow.", e); }
            catch (InvalidOperationException e) { throw new InvalidDataException("Recording exceeds its limits.", e); }
        }
    }
}
