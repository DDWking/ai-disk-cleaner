using System.IO;
using AiDiskCleaner.Models;
using AiDiskCleaner.Native;

namespace AiDiskCleaner.Services;

public static class RecycleService
{
    private static readonly string[] ProtectedNames =
    {
        "windows", "system32", "syswow64", "system volume information",
        "$mft", "$logfile", "$volume", "$attrdef", "$bitmap", "$boot",
        "$badclus", "$secure", "$upcase", "$extend", "pagefile.sys",
        "hiberfil.sys", "swapfile.sys", "bootmgr",
    };

    public static bool IsProtected(FileEntry e)
    {
        if (e.IsFilesGroup) return true;
        if (e.Parent == null) return true; // 盘符根
        string name = e.Name.TrimEnd('\\');
        if (ProtectedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        string path = (e.FullPath ?? "").Replace('/', '\\');
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[1].Equals("Windows", StringComparison.OrdinalIgnoreCase)
            && parts.Length <= 3)
            return true;
        if (name.StartsWith('$') && e.Parent?.Parent == null) return true;
        return false;
    }

    public static void SendToRecycle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("empty path");
        var op = new ShellNative.SHFILEOPSTRUCT
        {
            wFunc = ShellNative.FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = (ushort)(ShellNative.FOF_ALLOWUNDO | ShellNative.FOF_NOCONFIRMATION | ShellNative.FOF_SILENT | ShellNative.FOF_NOERRORUI),
        };
        int rc = ShellNative.SHFileOperation(ref op);
        if (rc != 0 || op.fAnyOperationsAborted)
            throw new IOException("SHFileOperation " + rc);
    }
}
