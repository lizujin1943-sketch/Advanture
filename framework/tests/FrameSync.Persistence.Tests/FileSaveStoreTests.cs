using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FrameSync.Storage.FileSystem;

namespace FrameSync.Persistence.Tests
{
    /// <summary>真实文件系统上的存档行为，另以内部原子提交边界稳定触发失败。</summary>
    internal static class FileSaveStoreTests
    {
        /// <summary>测试覆盖成功创建、替换、预算、旧档保护、临时文件和并发写入。</summary>
        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("file.creates_and_atomically_replaces_a_complete_save", () => {
                using var directory = new ScratchDirectory();
                var service = new SaveService(new FileSaveStore(directory.SavePath));
                service.Save(Fixture.SaveId, Fixture.Checkpoint());
                Assert.Equal(Fixture.SaveId, service.Load().SaveId);
                var replacementId = Guid.Parse("88888888-1234-5678-9012-123456789abc");
                service.Save(replacementId, Fixture.Checkpoint());
                Assert.Equal(replacementId, service.Load().SaveId);
                Assert.Equal(102UL, service.Load().Checkpoint.PersistentTick);
                directory.AssertNoTemporaryFiles();
            });
            yield return ("file.rejects_oversized_read_before_allocating_the_file", () => {
                using var directory = new ScratchDirectory();
                using (var file = new FileStream(directory.SavePath, FileMode.CreateNew)) file.SetLength(4 * 1024 * 1024);
                var store = new FileSaveStore(directory.SavePath);
                long before = GC.GetAllocatedBytesForCurrentThread();
                Assert.Throws<InvalidDataException>(() => store.Read(1024));
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(allocated < 64 * 1024, "读取超限文件不应先分配整个文件的缓冲区。");
            });
            yield return ("file.rejects_oversized_write_without_replacing_old_data", () => {
                using var directory = new ScratchDirectory();
                byte[] old = { 1, 2, 3 }; File.WriteAllBytes(directory.SavePath, old);
                var store = new FileSaveStore(directory.SavePath);
                Assert.Throws<InvalidDataException>(() => store.WriteAtomic(new byte[65], 64));
                Assert.Bytes(old, store.Read(64));
                directory.AssertNoTemporaryFiles();
            });
            yield return ("file.failed_commit_preserves_old_save_and_cleans_candidate", () => {
                using var directory = new ScratchDirectory();
                var original = new SaveService(new FileSaveStore(directory.SavePath));
                original.Save(Fixture.SaveId, Fixture.Checkpoint());
                byte[] old = File.ReadAllBytes(directory.SavePath);
                var commit = new FailingCommit();
                var store = new FileSaveStore(directory.SavePath, commit);
                byte[] replacement = { 9, 8, 7, 6 };
                Assert.Throws<IOException>(() => store.WriteAtomic(replacement, 1024));
                Assert.Bytes(replacement, commit.TemporaryBytes!);
                Assert.Bytes(old, File.ReadAllBytes(directory.SavePath));
                Assert.Equal(Fixture.SaveId, original.Load().SaveId);
                directory.AssertNoTemporaryFiles();
            });
            yield return ("file.failed_initial_commit_leaves_no_partial_save", () => {
                using var directory = new ScratchDirectory();
                var store = new FileSaveStore(directory.SavePath, new FailingCommit());
                Assert.Throws<IOException>(() => store.WriteAtomic(new byte[] { 1, 2 }, 64));
                Assert.True(!File.Exists(directory.SavePath), "首次提交失败不得出现部分正式存档。");
                directory.AssertNoTemporaryFiles();
            });
            yield return ("file.rejects_same_path_writer_and_keeps_reads_on_old_snapshot", () => {
                using var directory = new ScratchDirectory();
                byte[] old = { 1, 2 }, first = { 3, 4 }, rejected = { 5, 6 };
                File.WriteAllBytes(directory.SavePath, old);
                using var commit = new BlockingCommit();
                var firstStore = new FileSaveStore(directory.SavePath, commit);
                Task firstWriter = Task.Run(() => firstStore.WriteAtomic(first, 64));
                try
                {
                    Assert.True(commit.Entered.Wait(TimeSpan.FromSeconds(10)), "第一个写入者未进入提交阶段。");
                    var samePath = new FileSaveStore(Path.Combine(directory.Root, "unused", "..", "state.fss"));
                    Assert.Throws<IOException>(() => samePath.WriteAtomic(rejected, 64));
                    Assert.Bytes(old, samePath.Read(64));
                    // 互不相关的保存位置不能被全局锁阻塞。
                    var other = new FileSaveStore(Path.Combine(directory.Root, "other.fss"));
                    other.WriteAtomic(rejected, 64); Assert.Bytes(rejected, other.Read(64));
                }
                finally { commit.Release.Set(); firstWriter.GetAwaiter().GetResult(); }
                Assert.Bytes(first, new FileSaveStore(directory.SavePath).Read(64));
                directory.AssertNoTemporaryFiles();
            });
            yield return ("file.rejects_cross_process_writer_holding_the_same_lock", () => {
                using var directory = new ScratchDirectory();
                byte[] old = { 1, 2, 3 }; File.WriteAllBytes(directory.SavePath, old);
                using Process child = StartLockHolder(directory.SavePath + ".lock");
                try
                {
                    Task<string?> ready = child.StandardOutput.ReadLineAsync();
                    Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "锁持有进程没有及时就绪。");
                    Assert.Equal("READY", ready.Result);
                    var store = new FileSaveStore(directory.SavePath);
                    Assert.Throws<IOException>(() => store.WriteAtomic(new byte[] { 4, 5 }, 64));
                    Assert.Bytes(old, store.Read(64));
                }
                finally
                {
                    if (!child.HasExited) child.StandardInput.WriteLine("release");
                    if (!child.WaitForExit(10000)) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
                }
                Assert.Equal(0, child.ExitCode);
                // 稳定锁文件保留，但进程释放句柄后应可以正常再次保存。
                var after = new FileSaveStore(directory.SavePath);
                after.WriteAtomic(new byte[] { 6, 7 }, 64);
                Assert.Bytes(new byte[] { 6, 7 }, after.Read(64));
                directory.AssertNoTemporaryFiles();
            });
            yield return ("file.rejects_invalid_budget_and_missing_content", () => {
                using var directory = new ScratchDirectory(); var store = new FileSaveStore(directory.SavePath);
                Assert.Throws<ArgumentOutOfRangeException>(() => store.Read(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => store.WriteAtomic(new byte[] { 1 }, -1));
                Assert.Throws<FileNotFoundException>(() => store.Read(64));
                Assert.Throws<ArgumentException>(() => new FileSaveStore(""));
                Assert.Throws<ArgumentNullException>(() => new FileSaveStore(null!));
            });
        }

        /// <summary>子进程直接占用同一操作系统文件锁，直到父进程从标准输入通知退出。</summary>
        internal static int HoldWriterLock(string path)
        {
            using var handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Console.WriteLine("READY"); Console.Out.Flush(); Console.ReadLine();
            return 0;
        }

        /// <summary>重用当前测试宿主启动独立进程，兼容应用宿主和 dotnet 加 DLL 两种启动方式。</summary>
        private static Process StartLockHolder(string lockPath)
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位测试宿主。");
            var start = new ProcessStartInfo(executable) { RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--hold-writer-lock"); start.ArgumentList.Add(lockPath);
            return Process.Start(start) ?? throw new InvalidOperationException("无法启动锁验证进程。");
        }

        /// <summary>在真正提交前抛 I/O 异常，同时记录已完整写好的候选数据。</summary>
        private sealed class FailingCommit : IAtomicFileCommit
        {
            /// <summary>提交边界实际看到的磁盘字节。</summary>
            public byte[]? TemporaryBytes { get; private set; }
            /// <summary>用真实文件读取验证候选已存在，然后模拟原子提交失败。</summary>
            public void Commit(string temporaryPath, string targetPath)
            {
                TemporaryBytes = File.ReadAllBytes(temporaryPath);
                throw new IOException("模拟操作系统原子提交失败。");
            }
        }

        /// <summary>将第一个提交停在可观察位置，避免用时间延迟猜测并发发生时刻。</summary>
        private sealed class BlockingCommit : IAtomicFileCommit, IDisposable
        {
            /// <summary>首位写入者已完成候选文件准备并进入提交边界。</summary>
            public ManualResetEventSlim Entered { get; } = new ManualResetEventSlim();
            /// <summary>第二位写入者完成冲突检查后释放首位写入者。</summary>
            public ManualResetEventSlim Release { get; } = new ManualResetEventSlim();
            /// <summary>同步点释放后仍执行真实原子替换，最后文件内容必须是第一位写入者的数据。</summary>
            public void Commit(string temporaryPath, string targetPath)
            {
                Entered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new IOException("等待并发测试释放超时。");
                new AtomicFileCommit().Commit(temporaryPath, targetPath);
            }
            /// <summary>由测试清理同步资源，不向生产类型添加测试专属清理接口。</summary>
            public void Dispose() { Entered.Dispose(); Release.Dispose(); }
        }
    }
}
