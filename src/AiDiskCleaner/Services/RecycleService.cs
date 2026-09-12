using System.IO;
using AiDiskCleaner.Models;
using AiDiskCleaner.Native;

namespace AiDiskCleaner.Services;

/// <summary>
/// 回收站操作。清单侧的「受保护」判定已挪到 <see cref="ProtectedPaths"/>（老口径原样保留），
/// 删除时的完整预检在 <see cref="DeletionPreflight"/> / <see cref="DeletionExecutor"/>。
/// </summary>
public static class RecycleService
{
    /// <summary>
    /// 清理列表口径的保护判定。行为与历史版本一致 —— CleanAnalyzer 的 CanDelete
    /// 与默认勾选策略都依赖它，不要在这里加严，加严会让老用户看到的面板变小。
    /// </summary>
    public static bool IsProtected(FileEntry? e) => ProtectedPaths.IsProtectedEntry(e);

    public static void SendToRecycle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("empty path");
        SendMany(new[] { path });
    }

    public static void SendMany(IEnumerable<string> paths)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return;
        const int chunk = 40;
        for (int i = 0; i < list.Count; i += chunk)
        {
            var slice = list.Skip(i).Take(chunk).ToList();
            string joined = string.Join("\0", slice) + "\0\0";
            var op = new ShellNative.SHFILEOPSTRUCT
            {
                wFunc = ShellNative.FO_DELETE,
                pFrom = joined,
                fFlags = (ushort)(ShellNative.FOF_ALLOWUNDO | ShellNative.FOF_NOCONFIRMATION | ShellNative.FOF_SILENT | ShellNative.FOF_NOERRORUI),
            };
            int rc = ShellNative.SHFileOperation(ref op);
            if (rc != 0 || op.fAnyOperationsAborted)
                throw new IOException("SHFileOperation " + rc) { HResult = rc };
        }
    }
}
