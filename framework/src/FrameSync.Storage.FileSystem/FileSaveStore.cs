using System;
using System.IO;
using FrameSync.Persistence;

namespace FrameSync.Storage.FileSystem
{
    /// <summary>使用同目录候选文件、落盘刷新、回读校验和操作系统原子替换的文件存储适配器。</summary>
    /// <remarks>
    /// 保存目录由宿主预先创建。每个规范绝对路径使用稳定的“目标路径.lock”文件拒绝并发写入；
    /// 锁覆盖本进程与其他遵守同一约定的进程，冲突立即抛 IOException，不等待、不覆盖对方操作。
    /// 锁文件在句柄释放后保留，避免删除再重建造成两个不同文件各自被锁住的竞争条件。
    /// 读取不占写锁：原子提交之前可看到完整旧文件，提交之后可看到完整新文件。
    /// 宿主须为每个位置统一路径，不能通过符号链接、硬链接、大小写别名或其他程序绕过协作约定。
    /// Flush(true) 刷新候选内容；本适配器不承诺所有文件系统在断电后持久保留目录替换元数据。
    /// </remarks>
    public sealed class FileSaveStore : ISaveStore
    {
        private readonly string _path;
        private readonly string _directory;
        private readonly string _lockPath;
        private readonly IAtomicFileCommit _commit;

        /// <summary>为指定保存文件建立适配器，构造时不写入磁盘。</summary>
        /// <param name="path">保存文件路径；会规范化为绝对路径，所在目录必须在保存前已存在。</param>
        public FileSaveStore(string path) : this(path, new AtomicFileCommit()) { }

        /// <summary>注入内部操作系统提交边界，公开使用者始终使用默认原子替换实现。</summary>
        /// <param name="path">需要保存到的文件路径。</param>
        /// <param name="commit">已校验候选文件的原子提交实现。</param>
        internal FileSaveStore(string path, IAtomicFileCommit commit)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A save file path is required.", nameof(path));
            _path = Path.GetFullPath(path);
            if (string.IsNullOrEmpty(Path.GetFileName(_path)))
                throw new ArgumentException("The save path must name a file.", nameof(path));
            _directory = Path.GetDirectoryName(_path) ?? throw new ArgumentException("A parent directory is required.", nameof(path));
            _lockPath = _path + ".lock";
            _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        }

        /// <summary>打开同一文件句柄后先检查长度，再分配和读取，拒绝读取期间的截断或增长。</summary>
        /// <param name="maxBytes">允许读取的最大字节数，必须为正数。</param>
        /// <returns>完整独立字节数组；文件不存在、不可访问或超限时抛异常。</returns>
        public byte[] Read(int maxBytes)
        {
            RequirePositiveLimit(maxBytes);
            return ReadBoundedFile(_path, maxBytes);
        }

        /// <summary>在独占写锁下准备完整候选文件，只有刷新和回读成功后才原子建立或替换正式文件。</summary>
        /// <param name="bytes">完整待保存字节，调用结束前不得并发修改其底层数组。</param>
        /// <param name="maxBytes">允许保存的最大字节数，必须为正数。</param>
        /// <exception cref="InvalidDataException">输入超过预算或候选文件回读结果不匹配。</exception>
        /// <exception cref="IOException">并发写入冲突或操作系统文件操作失败。</exception>
        /// <remarks>
        /// 不先删除旧文件；提交前任何失败都保留原正式文件。临时文件清理尽力而为，
        /// 清理失败不掩盖原始异常；进程被终止时可能留下临时文件，由宿主按自己的维护策略清理。
        /// 不采用不原子的删除加移动回退；文件系统不支持原子替换时明确失败。
        /// </remarks>
        public void WriteAtomic(ReadOnlySpan<byte> bytes, int maxBytes)
        {
            RequirePositiveLimit(maxBytes);
            if (bytes.Length > maxBytes) throw new InvalidDataException("Save exceeds its byte budget.");
            // 稳定锁文件不能删除；文件句柄释放即解锁，进程退出也由操作系统释放。
            using var writerLock = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            string? temporaryPath = Path.Combine(_directory, ".framesync-save-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool ownsCandidate = false;
            try
            {
                // CreateNew 防止偶然重名覆盖其他候选；同目录保证与目标处于同一文件系统。
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsCandidate = true;
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                // 关闭写句柄再重新打开文件，比较磁盘读回的全部字节，验证后才允许提交。
                byte[] readBack = ReadBoundedFile(temporaryPath, maxBytes);
                if (!bytes.SequenceEqual(readBack)) throw new InvalidDataException("Temporary save did not read back exactly.");
                _commit.Commit(temporaryPath, _path);
                temporaryPath = null; // 提交已成功，不能再以清理异常把成功报告为失败。
            }
            finally
            {
                // CreateNew 若因重名等原因失败，不能删除尚未由本次操作成功创建的文件。
                if (ownsCandidate && temporaryPath != null) TryDeleteCandidate(temporaryPath);
            }
        }

        /// <summary>先依据已打开文件的长度限制分配，然后读取恰好完整内容。</summary>
        /// <param name="path">正式文件或本次独占候选文件的路径。</param>
        /// <param name="maxBytes">已验证为正数的读取上限。</param>
        /// <returns>与文件实际长度相同的独立字节数组。</returns>
        private static byte[] ReadBoundedFile(string path, int maxBytes)
        {
            // 允许目标被原子替换，让已打开的读者继续持有旧文件快照；禁止合作写入者就地改写。
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            long length = stream.Length;
            if (length > maxBytes) throw new InvalidDataException("Save exceeds its read byte budget.");
            var bytes = new byte[(int)length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new InvalidDataException("Save was truncated while reading.");
                offset += count;
            }
            if (stream.ReadByte() != -1) throw new InvalidDataException("Save grew while reading.");
            return bytes;
        }

        /// <summary>在任何文件操作前拒绝无效预算。</summary>
        /// <param name="maxBytes">调用方传入的读取或写入字节上限。</param>
        private static void RequirePositiveLimit(int maxBytes)
        {
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        /// <summary>仅清理本次生成的候选文件，不删除正式存档，也不掩盖原始失败原因。</summary>
        /// <param name="path">本次写入唯一生成的候选路径。</param>
        private static void TryDeleteCandidate(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>将已验证候选文件交给操作系统进行原子建立或替换。</summary>
    internal sealed class AtomicFileCommit : IAtomicFileCommit
    {
        /// <summary>已有文件使用 Replace，首次保存使用不覆盖目标的 Move；失败不删除旧文件。</summary>
        /// <param name="temporaryPath">与目标同目录的完整候选文件。</param>
        /// <param name="targetPath">正式存档路径。</param>
        public void Commit(string temporaryPath, string targetPath)
        {
            if (File.Exists(targetPath)) File.Replace(temporaryPath, targetPath, destinationBackupFileName: null);
            else File.Move(temporaryPath, targetPath);
        }
    }
}
