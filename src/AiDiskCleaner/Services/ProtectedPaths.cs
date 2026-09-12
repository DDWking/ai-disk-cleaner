using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>某个路径的防护结论。</summary>
public enum PathGuard
{
    /// <summary>正常可删。</summary>
    Allowed,
    /// <summary>敏感位置，需要用户额外确认整批。</summary>
    NeedsConfirm,
    /// <summary>硬拦截，任何情况下都不删。</summary>
    Blocked,
}

/// <summary>一条防护判定的结果，带用户可读原因和技术说明。</summary>
public readonly record struct PathGuardResult(PathGuard Guard, string Reason, string Tech)
{
    public static readonly PathGuardResult Allowed = new(PathGuard.Allowed, "", "");
    public static PathGuardResult Blocked(string reason, string tech) => new(PathGuard.Blocked, reason, tech);
    public static PathGuardResult Confirm(string reason, string tech) => new(PathGuard.NeedsConfirm, reason, tech);
}

/// <summary>
/// 受保护路径策略。纯函数、不碰磁盘，方便离线测试。
///
/// 注意：这里只作用于**删除时**的拦截。<see cref="IsProtectedEntry"/> 保持了
/// CleanAnalyzer 一直在用的那套老口径，用来保证清理列表的展示与默认勾选不变。
/// </summary>
public static class ProtectedPaths
{
    /// <summary>旧口径：磁盘根下这些名字永远不删（保持既有行为）。</summary>
    private static readonly string[] LegacyProtectedNames =
    {
        "windows", "system32", "syswow64", "system volume information",
        "$mft", "$logfile", "$volume", "$attrdef", "$bitmap", "$boot",
        "$badclus", "$secure", "$upcase", "$extend", "pagefile.sys",
        "hiberfil.sys", "swapfile.sys", "bootmgr",
    };

    /// <summary>路径里任意一段命中即硬拦截的目录名（不区分大小写）。</summary>
    private static readonly string[] BlockedSegments =
    {
        "system32", "syswow64", "winsxs", "system volume information",
        "$recycle.bin", "recovery", "boot", "efi", "msocache",
        "$mft", "$logfile", "$volume", "$attrdef", "$bitmap", "$boot",
        "$badclus", "$secure", "$upcase", "$extend", "$winreagent",
        "windows.old",
    };

    /// <summary>盘符根下这些文件/目录永远不删。</summary>
    private static readonly string[] BlockedRootNames =
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "bootnxt",
        "ntldr", "ntdetect.com", "dumpstack.log.tmp", "bootsect.bak",
    };

    /// <summary>
    /// 这些容器下**直接一级**的目标算敏感（整个软件安装目录 / 整个用户目录），
    /// 更深的位置（AppData 缓存等）按正常处理，避免把普通清理全打成敏感。
    /// </summary>
    private static readonly string[] SensitiveContainers =
    {
        "program files", "program files (x86)", "programdata", "users", "perflogs",
    };

    /// <summary>硬拦截的目录段（Windows 一棵树只挡浅层，与老口径一致）。</summary>
    private static readonly string[] BlockedWindowsTree = { "windows" };

    /// <summary>
    /// `C:\Windows\` 下**任何深度**都硬拦截的子目录。
    ///
    /// 这几个以前只写在 <see cref="KnownPaths"/> 的显示名里（「Windows 安装缓存（别乱删）」），
    /// 并没有真的拦住 —— 结果界面上的名字说别删，实际却能勾、能删。
    /// 现在按真实状态拦住，名字和资格就一致了。
    /// </summary>
    private static readonly string[] BlockedWindowsSubdirs =
    {
        "installer", "winsxs", "servicing", "system32", "syswow64", "assembly",
    };

    /// <summary>任意深度都硬拦截的独特目录名（不放在 BlockedSegments 里的是为了避免误伤同名普通目录）。</summary>
    private static readonly string[] BlockedAnywhereSegments = { "windowsapps" };

    /// <summary>
    /// **清理资格的唯一判据**：能不能进「可操作的清理候选」。
    ///
    /// 以前清理列表用 <see cref="IsProtectedEntry"/>（老口径），删除时用 <see cref="Classify"/>，
    /// 两套不一致，于是出现两种坏情况：
    /// <list type="bullet">
    /// <item>界面能勾选，点确认时被拦（「系统还原点」）；</item>
    /// <item>名字写着别删，实际既没拦住也没提示（「Windows 安装缓存」）。</item>
    /// </list>
    /// 这里把两边合成一处：**清理列表只收真正能删的东西**。
    /// 方向永远是收窄，不会让原本不能删的变得能删。
    /// </summary>
    public static bool IsCleanupBlocked(FileEntry? e)
    {
        if (e == null) return true;
        if (IsProtectedEntry(e)) return true;
        return Classify(e.FullPath).Guard == PathGuard.Blocked;
    }

    /// <summary>路径是否属于「只读、不进清理候选」的位置，并给出原因（文件浏览器里用锁图标展示）。</summary>
    public static string CleanupBlockReason(FileEntry? e)
    {
        if (e == null) return "路径无效";
        var byPath = Classify(e.FullPath);
        if (byPath.Guard == PathGuard.Blocked) return byPath.Reason;
        if (IsProtectedEntry(e)) return "受保护的位置";
        return "";
    }

    /// <summary>
    /// 老口径的条目级保护。CleanAnalyzer 依赖它，行为必须保持原样。
    /// </summary>
    public static bool IsProtectedEntry(FileEntry? e)
    {
        if (e == null) return true;
        if (e.IsFilesGroup) return true;
        if (e.Parent == null) return true; // 盘符根
        string name = (e.Name ?? "").TrimEnd('\\');
        if (LegacyProtectedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        string path = (e.FullPath ?? "").Replace('/', '\\');
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[1].Equals("Windows", StringComparison.OrdinalIgnoreCase)
            && parts.Length <= 3)
            return true;
        if (name.StartsWith('$') && e.Parent?.Parent == null) return true;
        return false;
    }

    /// <summary>把路径切成段（去掉盘符）。</summary>
    public static string[] Segments(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Array.Empty<string>();
        string p = path.Replace('/', '\\').Trim();
        return p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>是不是盘符根（C:\ 或 C:）。</summary>
    public static bool IsDriveRoot(string path)
    {
        var parts = Segments(path);
        return parts.Length == 1 && parts[0].EndsWith(':');
    }

    /// <summary>
    /// 按路径字符串判定删除时的防护等级。纯字符串判断，不访问磁盘。
    /// </summary>
    public static PathGuardResult Classify(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PathGuardResult.Blocked("路径为空", "empty path");

        if (IsDriveRoot(path))
            return PathGuardResult.Blocked("这是整个磁盘根目录", "drive root");

        var parts = Segments(path);
        if (parts.Length == 0)
            return PathGuardResult.Blocked("无法识别的路径", "no path segments");

        // 盘符根下的系统文件（pagefile.sys 等）
        string leaf = parts[^1].TrimEnd('\\');
        if (parts.Length == 2 && BlockedRootNames.Contains(leaf, StringComparer.OrdinalIgnoreCase))
            return PathGuardResult.Blocked("系统正在使用的文件", "root system file: " + leaf);

        if (parts.Length == 2 && leaf.StartsWith('$'))
            return PathGuardResult.Blocked("NTFS 元数据文件", "ntfs metadata: " + leaf);

        // 任意一段命中硬拦截目录
        bool underWindows = parts.Length >= 2 && parts[1].Equals("Windows", StringComparison.OrdinalIgnoreCase);
        for (int i = 1; i < parts.Length; i++)
        {
            var seg = parts[i];
            if (BlockedSegments.Contains(seg, StringComparer.OrdinalIgnoreCase))
                return PathGuardResult.Blocked("系统关键目录，动不了", "blocked segment: " + seg);
            if (BlockedAnywhereSegments.Contains(seg, StringComparer.OrdinalIgnoreCase))
                return PathGuardResult.Blocked("应用商店应用的安装位置，不能删", "blocked segment: " + seg);
            // C:\Windows\Installer 这类：任何深度都要拦。Windows Installer 缓存被删会导致
            // 程序无法修复/卸载，微软明确不建议清理。
            if (underWindows && i == 2 && BlockedWindowsSubdirs.Contains(seg, StringComparer.OrdinalIgnoreCase))
                return PathGuardResult.Blocked("Windows 自己的组件/安装缓存，删了程序会修不好",
                    "blocked windows subdir: " + seg);
        }

        // Windows 树浅层（C:\Windows、C:\Windows\X）
        if (parts.Length >= 2 && parts[1].Equals("Windows", StringComparison.OrdinalIgnoreCase)
            && parts.Length <= 3)
            return PathGuardResult.Blocked("Windows 系统目录", "windows tree depth " + parts.Length);

        // 敏感容器的一级子项
        if (parts.Length >= 3)
        {
            string container = parts[1];
            if (SensitiveContainers.Contains(container, StringComparer.OrdinalIgnoreCase) && parts.Length == 3)
                return PathGuardResult.Confirm(
                    "整个「" + container + "」下的软件目录，删了软件就没了",
                    "sensitive container: " + container);
        }

        return PathGuardResult.Allowed;
    }

    /// <summary>
    /// 结合磁盘属性再判一次。
    /// 链接目录（junction / symlink）不硬拦，但降成「要确认」：删掉的只是链接本身，
    /// 可用户不一定知道，所以要让他在确认框里看到。
    /// 文件的重解析点通常是云盘占位文件（OneDrive），删本地占位是正常清理，放行。
    /// </summary>
    public static PathGuardResult Classify(string path, bool isReparsePoint, bool isSystem, bool isDirectory)
    {
        var byPath = Classify(path);
        if (byPath.Guard == PathGuard.Blocked) return byPath;

        if (isReparsePoint && isDirectory && byPath.Guard != PathGuard.NeedsConfirm)
            return PathGuardResult.Confirm("这是个链接目录，删掉的只是链接", "directory reparse point");

        if (isSystem && isDirectory && byPath.Guard != PathGuard.NeedsConfirm)
            return PathGuardResult.Confirm("系统标记的文件夹", "FILE_ATTRIBUTE_SYSTEM directory");

        return byPath;
    }

    /// <summary>是否位于系统盘的关键位置（用于日志与诊断）。</summary>
    public static bool IsUnderWindowsTree(string path)
    {
        var parts = Segments(path);
        return parts.Length >= 2 && parts[1].Equals("Windows", StringComparison.OrdinalIgnoreCase);
    }
}
