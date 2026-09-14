using System.IO;
using AiDiskCleaner.Native;

namespace AiDiskCleaner.Services;

/// <summary>
/// 真实的「必须进回收站」操作，基于 Shell 的 <c>IFileOperation</c> +
/// <c>FOFX_RECYCLEONDELETE</c>（详见 <see cref="FolderRecycleNative"/>）。
///
/// 与旧的 <see cref="RecycleService"/> **刻意隔离**：旧链路为保持兼容仍用
/// SHFileOperation + FOF_ALLOWUNDO（那并不保证进回收站），本类只服务手动文件夹删除，
/// 不改变旧清理链路的任何行为。
/// </summary>
public sealed class ShellRecycleOperation : IFolderRecycleOperation
{
    /// <summary>FOFX_RECYCLEONDELETE 需要 Windows 8+；更老的系统一律拒绝执行。</summary>
    public static bool RecycleRequiredFlagSupported =>
        OperatingSystem.IsWindows() && OperatingSystem.IsWindowsVersionAtLeast(6, 2);

    public bool CanRecycle(string path, out string tech)
    {
        tech = "";
        if (string.IsNullOrWhiteSpace(path)) { tech = "empty path"; return false; }
        if (!RecycleRequiredFlagSupported)
        {
            tech = "FOFX_RECYCLEONDELETE requires Windows 8 or newer";
            return false;
        }

        string root;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
        }
        catch (Exception ex)
        {
            tech = "bad path: " + ex.GetType().Name;
            return false;
        }
        if (root.Length == 0) { tech = "no volume root"; return false; }

        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady) { tech = "volume not ready"; return false; }
            if (drive.DriveType is DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown)
            {
                tech = "volume type " + drive.DriveType + " has no Recycle Bin";
                return false;
            }
        }
        catch (Exception ex)
        {
            tech = ex.GetType().Name + ": " + ex.Message;
            return false;
        }

        return FolderRecycleNative.TryQueryRecycleBin(root, out tech);
    }

    public RecycleShellResult Recycle(string path)
    {
        if (!CanRecycle(path, out var why))
            return RecycleShellResult.Fail(unchecked((int)0x80070032), "recycle unavailable: " + why);

        try
        {
            FolderRecycleNative.RecycleRequired(path);
            return RecycleShellResult.Ok("IFileOperation + FOFX_RECYCLEONDELETE");
        }
        catch (OperationCanceledException ex)
        {
            return RecycleShellResult.Abort(ex.HResult,
                "shell reported aborted (0x" + (ex.HResult & 0xFFFFFFFF).ToString("X8") + ")");
        }
        catch (Exception ex)
        {
            return RecycleShellResult.Fail(ex.HResult,
                ex.GetType().Name + " 0x" + (ex.HResult & 0xFFFFFFFF).ToString("X8"));
        }
    }
}
