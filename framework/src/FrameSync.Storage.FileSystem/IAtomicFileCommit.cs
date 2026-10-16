namespace FrameSync.Storage.FileSystem
{
    /// <summary>操作系统原子提交边界；调用时临时文件已刷新并回读校验。</summary>
    internal interface IAtomicFileCommit
    {
        /// <summary>用完整临时文件原子建立或替换目标；异常时不提前删除目标。</summary>
        /// <param name="temporaryPath">与目标在同一目录的完整候选文件。</param>
        /// <param name="targetPath">最终保存位置。</param>
        void Commit(string temporaryPath, string targetPath);
    }
}
