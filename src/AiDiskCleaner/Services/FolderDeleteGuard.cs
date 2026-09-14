using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 「整个文件夹删除」比清理页的逐文件删除更需要收紧的保护层。
/// <see cref="ProtectedPaths"/> 仍然是第一道判定（它负责任意路径段与盘符根），
/// 这里只补上文件夹删除独有的三件事：
///
/// 1) **容器本身**不能删：C:\Windows、C:\Program Files、C:\ProgramData、C:\Users、
///    C:\Recovery、C:\$Recycle.Bin、C:\System Volume Information 等；
/// 2) **链接 / 重解析点一律拒绝**（本轮保守口径）：
///    选中的文件夹本身是链接，或它的任何上层目录是链接，都直接硬拦 ——
///    删除会穿透到链接指向的真实位置，不能让用户「确认后放行」；
/// 3) **用户配置根**（C:\Users\<名字>）需要用户明确确认。
///
/// 纯字符串判定不碰磁盘；只有 <see cref="CheckAncestors"/> 需要探针来读重解析属性。
/// </summary>
public static class FolderDeleteGuard
{
    /// <summary>盘符根下这些**容器目录本身**不能删（它们下面的一级子项另有口径）。</summary>
    private static readonly string[] BlockedContainers =
    {
        "windows", "program files", "program files (x86)", "programdata", "perflogs",
        "users", "recovery", "$recycle.bin", "system volume information",
        "boot", "efi", "msocache", "$winreagent", "windows.old", "config.msi",
        "$windows.~bt", "$windows.~ws",
    };

    /// <summary>
    /// 按路径字符串判定「整个文件夹」的额外防护。
    /// 与 <see cref="ProtectedPaths.Classify(string)"/> 取更严的一档。
    /// </summary>
    public static PathGuardResult ClassifyFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PathGuardResult.Blocked("路径为空", "empty path");

        if (ProtectedPaths.IsDriveRoot(path))
            return PathGuardResult.Blocked("这是整个磁盘根目录", "drive root");

        var parts = ProtectedPaths.Segments(path);
        if (parts.Length == 0)
            return PathGuardResult.Blocked("无法识别的路径", "no segments");

        string leaf = parts[^1].TrimEnd('\\');

        // 盘符根下的 NTFS 元数据（$MFT 等）
        if (parts.Length == 2 && leaf.StartsWith('$'))
            return PathGuardResult.Blocked("NTFS 元数据，不能删", "ntfs metadata: " + leaf);

        // Windows 一棵树：任何深度都硬拦（整目录删除不做「只删应用自己那层」的猜测）
        if (parts.Length >= 2 && parts[1].Equals("Windows", StringComparison.OrdinalIgnoreCase))
            return PathGuardResult.Blocked("Windows 系统目录（含其下任何一层）", "windows tree: " + parts[1]);

        // 盘符根下的系统/用户容器目录本身
        if (parts.Length == 2 && BlockedContainers.Contains(parts[1], StringComparer.OrdinalIgnoreCase))
            return PathGuardResult.Blocked("系统/用户根级容器目录，不能整删", "root container: " + parts[1]);

        // 用户配置根：C:\Users\<名字>（需要明确确认）
        if (parts.Length == 3 && parts[1].Equals("Users", StringComparison.OrdinalIgnoreCase))
            return PathGuardResult.Confirm("整个用户配置根目录，删了该用户的文件就没了", "user profile root");

        return PathGuardResult.Allowed;
    }

    /// <summary>
    /// 目标文件夹本身是重解析点（junction / symlink / 云盘占位目录）⇒ 硬拦。
    /// 说明里明确写「删的是链接」也不放行：本轮不在界面上提供「确认后删链接」的口子。
    /// </summary>
    public static PathGuardResult CheckTargetReparse(string path, IFileSystemProbe probe)
    {
        FileProbeInfo p;
        try { p = probe.Probe(path); }
        catch { return PathGuardResult.Allowed; } // 探针失败交给预检的其他判定，不在这里造结论
        if (!p.Exists) return PathGuardResult.Allowed;
        if (p.IsReparsePoint)
            return PathGuardResult.Blocked("这是个链接 / 重解析点，删除会穿透到它指向的位置；本轮不支持删链接",
                "target reparse point");
        return PathGuardResult.Allowed;
    }

    /// <summary>
    /// 上层目录里只要有一段是重解析点，就硬拦。
    /// 例：C:\Data\link\child 里 link 是 junction ⇒ 删 child 实际动的是链接另一头的真实目录。
    /// </summary>
    public static PathGuardResult CheckAncestors(string path, IFileSystemProbe probe)
    {
        var parts = ProtectedPaths.Segments(path);
        if (parts.Length <= 2) return PathGuardResult.Allowed;

        string cur = parts[0].TrimEnd('\\') + "\\";
        for (int i = 1; i < parts.Length - 1; i++)
        {
            cur = cur.TrimEnd('\\') + "\\" + parts[i];
            FileProbeInfo p;
            try { p = probe.Probe(cur); }
            catch { continue; }
            if (p.Exists && p.IsReparsePoint)
                return PathGuardResult.Blocked(
                    "上层目录「" + parts[i] + "」是链接 / 重解析点，删除会穿透到链接指向的位置",
                    "ancestor reparse point: " + cur);
        }
        return PathGuardResult.Allowed;
    }

    /// <summary>一次把三条判定合起来，取最严的一档。</summary>
    public static PathGuardResult ClassifyForFolderDelete(string path, IFileSystemProbe probe)
        => Strictest(ClassifyFolder(path), CheckTargetReparse(path, probe), CheckAncestors(path, probe));

    /// <summary>取最严结论：Blocked &gt; NeedsConfirm &gt; Allowed。</summary>
    public static PathGuardResult Strictest(params PathGuardResult[] results)
    {
        PathGuardResult best = PathGuardResult.Allowed;
        foreach (var r in results)
            if ((int)r.Guard > (int)best.Guard) best = r;
        return best;
    }
}
