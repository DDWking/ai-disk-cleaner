using System.IO;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

public static class KnownPaths
{
    static readonly (string Needle, string Label)[] Rules =
    {
        (@"\steamapps\common", "Steam 游戏库"),
        (@"\steamlibrary", "Steam 游戏库"),
        (@"\steam\steamapps", "Steam 游戏库"),
        (@"\epic games", "Epic 游戏"),
        (@"\gog galaxy", "GOG 游戏"),
        (@"\origin games", "Origin / EA 游戏"),
        (@"\ubisoft game launcher", "育碧游戏"),
        (@"\battle.net", "战网游戏"),
        (@"\xboxgames", "Xbox 游戏"),
        (@"\windowsapps", "微软商店应用（别删）"),
        (@"\program files\windowsapps", "微软商店应用（别删）"),
        (@"\node_modules\", "npm 依赖 node_modules"),
        (@"\.venv\", "Python 虚拟环境"),
        (@"\venv\", "Python 虚拟环境"),
        (@"\.git\", "Git 仓库"),
        (@"\winsxs", "Windows 组件存储 WinSxS（别删）"),
        (@"\windows\installer", "Windows 安装缓存（别乱删）"),
        (@"\windows\softwaredistribution", "Windows 更新下载"),
        (@"\windows\temp", "Windows 临时目录"),
        (@"\system volume information", "系统还原点（别删）"),
        (@"\$recycle.bin", "回收站"),
        (@"\onedrive", "OneDrive 云同步"),
        (@"\dropbox", "Dropbox 同步"),
        (@"\downloads", "下载文件夹"),
        (@"\迅雷下载", "迅雷下载"),
        (@"\documents", "文档"),
        (@"\videos", "视频"),
        (@"\pictures", "图片"),
        (@"\desktop", "桌面"),
        (@"\appdata\local\temp", "用户临时目录"),
        (@"\appdata\local\docker", "Docker 数据"),
        (@"\docker", "Docker 数据"),
        (@"\wsl", "WSL 发行版"),
        (@"\appdata\local\packages", "UWP 应用数据"),
        (@"\appdata\local\nvidia", "NVIDIA 缓存"),
        (@"\appdata\local\pip", "pip 缓存"),
        (@"\appdata\local\npm-cache", "npm 缓存"),
        (@"\appdata\local\yarn", "Yarn 缓存"),
        (@"\appdata\local\nuget", "NuGet 缓存"),
        (@"\appdata\local\pnpm", "pnpm 缓存"),
        (@"\appdata\roaming\npm", "npm 全局包"),
        (@"\android\sdk", "Android SDK"),
        (@"\android-sdk", "Android SDK"),
        (@"\programdata\package cache", "安装包缓存"),
        (@"\program files", "已安装程序（别整夹删）"),
        (@"\program files (x86)", "已安装程序 32 位（别整夹删）"),
        (@"\windows", "Windows 系统（别删）"),
        (@"\users\", "用户目录"),
        (@"\vmware", "VMware 虚拟机"),
        (@"\virtualbox vms", "VirtualBox 虚拟机"),
        (@"\hyper-v", "Hyper-V 虚拟机"),
        (@"\.android\avd", "Android 模拟器镜像"),
        (@"\crashdumps", "崩溃转储"),
        (@"\minidump", "崩溃转储"),
    };

    static readonly HashSet<string> InstallExt = new(StringComparer.OrdinalIgnoreCase)
        { ".msi", ".iso", ".msu", ".exe", ".zip", ".7z", ".rar" };

    public static string? Describe(string? path)
    {
        string p = Norm(path);
        if (p.Length == 0) return null;
        foreach (var (needle, label) in Rules)
        {
            if (p.Contains(needle, StringComparison.Ordinal))
                return label;
        }
        return null;
    }

    public static string Fingerprint(FileEntry e)
    {
        var bits = new List<string> { e.SizeText, e.IsDirectory ? "dir" : "file" };
        if (e.IsDirectory) bits.Add(e.FileCount.ToString("N0") + " files");
        else
        {
            string ext = Path.GetExtension(e.Name);
            if (!string.IsNullOrEmpty(ext)) bits.Add(ext);
            string cat = string.IsNullOrEmpty(e.Category) ? FileClassifier.Classify(e.Name) : e.Category;
            if (!string.IsNullOrEmpty(cat) && cat != "其他") bits.Add(cat);
        }
        if (e.Modified != DateTime.MinValue)
            bits.Add("modified " + e.AgeText);
        string? known = Describe(e.FullPath);
        if (!string.IsNullOrEmpty(known)) bits.Add(known);
        if (e.IsDirectory)
        {
            string top = TopExt(e, 3);
            if (!string.IsNullOrEmpty(top)) bits.Add("ext " + top);
        }
        bits.Add(e.FullPath);
        return string.Join("  |  ", bits);
    }

    public static (string Reason, string Group)? LargeHint(FileEntry f)
    {
        string path = Norm(f.FullPath);
        string ext = Path.GetExtension(f.Name);
        if (InstallExt.Contains(ext) && path.Contains(@"\downloads\") && f.Size >= 20L * 1024 * 1024)
            return (Loc.ReasonInstaller, Loc.GroupInstaller);
        if ((ext.Equals(".iso", StringComparison.OrdinalIgnoreCase) || ext.Equals(".img", StringComparison.OrdinalIgnoreCase))
            && f.Size >= 500L * 1024 * 1024 && f.AgeDays >= 180)
            return (Loc.ReasonOldInstaller, Loc.GroupInstaller);
        if ((ext.Equals(".vhdx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".vhd", StringComparison.OrdinalIgnoreCase)
             || ext.Equals(".vmdk", StringComparison.OrdinalIgnoreCase))
            && (path.Contains(@"\docker") || path.Contains(@"\wsl") || path.Contains(@"\.android")))
            return (Loc.ReasonVmDisk, Loc.GroupLarge);
        return null;
    }

    static string TopExt(FileEntry dir, int n)
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        Walk(dir, map, 0);
        return string.Join(", ",
            map.OrderByDescending(kv => kv.Value).Take(n)
               .Select(kv => kv.Key + " " + FileEntry.FormatSize(kv.Value)));
    }

    static void Walk(FileEntry n, Dictionary<string, long> map, int depth)
    {
        if (depth > 4) return;
        foreach (var c in n.Children)
        {
            if (c.IsFilesGroup || c.IsDirectory) { Walk(c, map, depth + (c.IsDirectory ? 1 : 0)); continue; }
            string ext = Path.GetExtension(c.Name);
            if (string.IsNullOrEmpty(ext)) ext = "(none)";
            map[ext] = map.GetValueOrDefault(ext) + c.Size;
        }
    }

    static string Norm(string? path)
        => (path ?? "").Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
}
