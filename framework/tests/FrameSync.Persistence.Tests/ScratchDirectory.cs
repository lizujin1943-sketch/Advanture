using System;
using System.IO;

namespace FrameSync.Persistence.Tests
{
    /// <summary>每项文件测试使用独立目录，所有产物保留在正式仓库 artifacts 下。</summary>
    internal sealed class ScratchDirectory : IDisposable
    {
        /// <summary>从测试程序集定位正式仓库，避免宿主工作目录改变时写到仓库外。</summary>
        public ScratchDirectory()
        {
            DirectoryInfo? root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("无法定位正式项目目录。");
            Root = Path.Combine(root.FullName, "artifacts", "initial-core", "persistence-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }
        /// <summary>本测试独占的临时目录绝对路径。</summary>
        public string Root { get; }
        /// <summary>测试默认的正式存档位置。</summary>
        public string SavePath => Path.Combine(Root, "state.fss");
        /// <summary>不允许遗留候选文件；正常锁文件会长期保留，不能按临时文件处理。</summary>
        public void AssertNoTemporaryFiles() => Assert.Equal(0, Directory.GetFiles(Root, "*.tmp").Length);
        /// <summary>测试退出后删除自有目录，绝不清理别的测试或用户文件。</summary>
        public void Dispose() { Directory.Delete(Root, recursive: true); }
    }
}
