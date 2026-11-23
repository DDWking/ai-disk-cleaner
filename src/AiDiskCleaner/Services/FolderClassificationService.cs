using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiDiskCleaner.Models;
using AiDiskCleaner.Native;
using AiDiskCleaner.Services.Agent;

namespace AiDiskCleaner.Services;

public sealed record ClassifyProgress(string Phase, int Completed, int Total, string Message);

public sealed record ClassifyAiResult(
    bool Ok,
    int ClassifiedCount,
    int Calls,
    string Error = "",
    ChatErrorKind ErrorKind = ChatErrorKind.Unknown);

/// <summary>
/// 「整理归类」分析服务：只读内存 MFT 树 + 本地证据红线 + 用户自定义分类规则 + AI 内容归纳。
///
/// 安全铁律：
/// 1) 这里只做「分类诊断与计划生成」，不移动任何文件、不改任何注册表或快捷方式；
/// 2) 路径出站默认经 %USER% 脱敏；
/// 3) 保守拦截由本地红线（系统/KnownFolder/卸载清单/启动器/venv）硬决定，AI 不能解锁被拦项目，
///    也不能把含可执行文件的绿软自动标为 Safe 默认勾选。
/// </summary>
public sealed class FolderClassificationService
{
    public const int MaxCandidates = 120;
    public const int CardsPerBatch = 8;
    private const int MaxWalkEntriesPerDir = 5000;
    private const int MaxWalkDepth = 6;

    private static readonly Regex UserSegment = new(
        @"(^|\\)Users\\([^\\]+)(?=\\|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> RootSystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "program files", "program files (x86)", "programdata", "users",
        "recovery", "perflogs", "system volume information", "$recycle.bin", "$winreagent",
        "$extend", "boot", "efi", "msocache", "windows.old", "esupport", "drivers",
    };

    /// <summary>安全防护、反作弊驱动与系统核心组件目录关键词：即使使用 NTFS 软链也永久硬拦，绝不迁移。</summary>
    private static readonly string[] HardNoJunctionTokens =
    {
        "riot vanguard", "easyanticheat", "battleye", "faceit", "nprotect", "gameguard",
        "ace-guard", "anticheatexpert", "huorong", "360safe", "360totalsecurity",
        "kaspersky", "bitdefender", "norton", "mcafee", "eset", "avast", "avg",
        "crowdstrike", "windows defender", "windowsapps", "driverstore",
    };

    public static bool IsHardNoJunctionPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        string norm = CleanListSnapshot.NormPath(path);
        if (ProtectedPaths.IsDriveRoot(norm)) return true;
        if (ProtectedPaths.Classify(norm).Guard == PathGuard.Blocked) return true;

        var segs = ProtectedPaths.Segments(norm);
        if (segs.Length <= 2 && segs.Length >= 2 && RootSystemNames.Contains(segs[1]))
            return true;

        foreach (var tok in HardNoJunctionTokens)
        {
            if (norm.Contains(tok, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".rmvb",
        ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".ncm", ".ape",
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".heic", ".raw", ".cr2", ".nef",
        ".ass", ".srt", ".ssa", ".vtt", ".psd", ".prproj", ".aep",
    };

    private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".xz", ".iso", ".img", ".wim",
        ".exe", ".msi", ".msix", ".appx", ".cab", ".torrent", ".bc!", ".crdownload",
    };

    private static readonly HashSet<string> DocsExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md",
        ".csv", ".epub", ".mobi", ".azw3", ".xmind", ".one",
    };

    private static readonly HashSet<string> DevExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".py", ".js", ".ts", ".tsx", ".jsx", ".go", ".rs",
        ".c", ".cpp", ".h", ".hpp", ".java", ".kt", ".json", ".yaml", ".yml", ".toml", ".sql",
    };

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".bat", ".cmd", ".ps1", ".dll",
    };

    private static readonly HashSet<string> GameMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "unityplayer.dll", "gameassembly.dll", "steam_api64.dll", "steam_api.dll",
        "ue4prereqsetup_x64.exe", "crashreportclient.exe", "dxgi.dll", "xinput1_3.dll",
    };

    private static readonly HashSet<string> GameExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pak", ".vpk", ".ucas", ".utoc", ".upk", ".assets", ".unity3d", ".rpf",
    };

    private static readonly HashSet<string> LooseDownloadDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "downloads", "download", "迅雷下载", "百度网盘", "baidunetdiskdownload", "quarkdownloads",
    };

    public sealed record ContentSummary(
        int SampledFiles,
        int SampledDirs,
        bool HasExecutable,
        bool HasGameMarker,
        bool HasVenvMarker,
        bool HasDevMarker,
        List<(string Ext, long Bytes, int Count)> TopExtensions,
        List<string> SampleFiles,
        List<string> SampleDirs,
        List<string> Markers,
        string DigestText);

    public sealed record ContextSnapshot(
        HashSet<string> KnownFolderPaths,
        Dictionary<string, string> InstalledLocations,
        HashSet<string> IgnoredPaths);

    /// <summary>采集某盘符下可作为分类目标的现存顶级目录映射（例如 D:\Game → 归入 games）。</summary>
    public static Dictionary<string, string> ResolveCategoryTargetNames(
        FileEntry? driveRootEntry,
        IReadOnlyList<ClassifyCategoryRule> categories)
    {
        var topDirNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (driveRootEntry != null)
        {
            foreach (var c in driveRootEntry.ChildList)
            {
                if (!c.IsDirectory || c.IsReparsePoint || c.IsFilesGroup) continue;
                topDirNames[c.Name] = c.Name;
            }
        }

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in categories)
        {
            if (string.IsNullOrWhiteSpace(rule.Id)) continue;
            string? target = null;
            if (!string.IsNullOrWhiteSpace(rule.FolderName)
                && topDirNames.TryGetValue(rule.FolderName.Trim(), out var exact))
            {
                target = exact;
            }
            if (target == null)
            {
                foreach (var alias in rule.Aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    if (topDirNames.TryGetValue(alias.Trim(), out var hit))
                    {
                        target = hit;
                        break;
                    }
                }
            }
            resolved[rule.Id] = target ?? rule.FolderName.Trim();
        }
        return resolved;
    }

    public static ContextSnapshot BuildContext(
        ILocalPurposeEvidence? evidence,
        IReadOnlyList<AppUninstallItem>? installedApps,
        IEnumerable<string>? ignoredPaths)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (evidence != null)
        {
            foreach (var r in evidence.SystemRoles())
            {
                string norm = CleanListSnapshot.NormPath(r.Path);
                if (norm.Length > 0) known.Add(norm);
            }
        }

        if (installedApps != null)
        {
            foreach (var app in installedApps)
            {
                string loc = CleanListSnapshot.NormPath(app.InstallLocation);
                if (loc.Length == 0 || ProtectedPaths.IsDriveRoot(loc)) continue;
                apps[loc] = string.IsNullOrWhiteSpace(app.Name) ? Path.GetFileName(loc) : app.Name;
            }
        }

        if (ignoredPaths != null)
        {
            foreach (var p in ignoredPaths)
            {
                string norm = CleanListSnapshot.NormPath(p);
                if (norm.Length > 0) ignored.Add(norm);
            }
        }

        return new ContextSnapshot(known, apps, ignored);
    }

    /// <summary>
    /// 轻量只读注册表安装路径快照（约 10ms，不解码图标、不启动外部 Helper 进程），
    /// 供尚未打开「卸载」页时的保守拦截使用。
    /// </summary>
    public static Dictionary<string, string> ReadInstalledLocationsFast()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return map;

        void ScanHive(Microsoft.Win32.RegistryHive hive, Microsoft.Win32.RegistryView view)
        {
            try
            {
                using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var uninst = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninst == null) return;
                foreach (var subName in uninst.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = uninst.OpenSubKey(subName);
                        if (sub == null) continue;
                        string? loc = sub.GetValue("InstallLocation") as string;
                        if (string.IsNullOrWhiteSpace(loc)) continue;
                        string norm = CleanListSnapshot.NormPath(loc.Trim().Trim('"'));
                        if (norm.Length == 0 || ProtectedPaths.IsDriveRoot(norm)) continue;
                        string name = (sub.GetValue("DisplayName") as string)?.Trim() ?? "";
                        if (!map.ContainsKey(norm))
                            map[norm] = name.Length > 0 ? name : Path.GetFileName(norm);
                    }
                    catch { }
                }
            }
            catch { }
        }

        ScanHive(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        ScanHive(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32);
        ScanHive(Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Default);
        return map;
    }

    public static List<ClassifyItem> BuildCandidates(
        FileEntry? driveRootEntry,
        IReadOnlyList<ClassifyCategoryRule> categories,
        ContextSnapshot context,
        bool includeDownloadsChildren,
        IEnumerable<FileEntry>? extraPinnedDirs = null)
    {
        var items = new List<ClassifyItem>();
        if (driveRootEntry == null || string.IsNullOrWhiteSpace(driveRootEntry.FullPath))
            return items;

        string driveRoot = EnsureDirContainerPath(driveRootEntry.FullPath);
        string physicalVolumeRoot = Path.GetPathRoot(driveRoot) ?? driveRoot;
        var resolvedTargets = ResolveCategoryTargetNames(driveRootEntry, categories);
        var choices = BuildChoices(categories, resolvedTargets);
        var targetBucketPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in resolvedTargets)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            targetBucketPaths.Add(CleanListSnapshot.NormPath(Path.Combine(driveRoot, kv.Value)));
        }

        var candidateDirs = new List<FileEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddCandidate(FileEntry dir, bool expandDownloadsContainer)
        {
            if (!dir.IsDirectory || dir.IsFilesGroup) return;
            string norm = CleanListSnapshot.NormPath(dir.FullPath);
            if (norm.Length == 0 || !seenPaths.Add(norm)) return;
            if (context.IgnoredPaths.Contains(norm)) return;
            if (targetBucketPaths.Contains(norm)) return; // 已经是目标大类桶，不能自己搬自己

            if (expandDownloadsContainer && !dir.IsReparsePoint)
            {
                foreach (var sub in dir.ChildList)
                {
                    if (!sub.IsDirectory || sub.IsFilesGroup || sub.IsReparsePoint || sub.Size < 512 * 1024) continue;
                    string subNorm = CleanListSnapshot.NormPath(sub.FullPath);
                    if (subNorm.Length == 0 || targetBucketPaths.Contains(subNorm) || context.IgnoredPaths.Contains(subNorm))
                        continue;
                    if (seenPaths.Add(subNorm))
                        candidateDirs.Add(sub);
                }
            }

            candidateDirs.Add(dir);
        }

        if (extraPinnedDirs != null)
        {
            foreach (var extra in extraPinnedDirs)
            {
                if (extra != null && IsSameDrive(physicalVolumeRoot, extra.FullPath))
                    TryAddCandidate(extra, expandDownloadsContainer: false);
            }
        }

        var topDirs = driveRootEntry.ChildList
            .Where(c => c.IsDirectory && !c.IsFilesGroup)
            .OrderByDescending(c => c.Size)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var d in topDirs)
        {
            bool isLooseDownload = includeDownloadsChildren && LooseDownloadDirNames.Contains(d.Name);
            TryAddCandidate(d, expandDownloadsContainer: isLooseDownload);
        }

        // 若开启「包含下载内子目录」，也检查真实 KnownFolder Downloads 是否正好位于同一盘符
        if (includeDownloadsChildren)
        {
            foreach (var kp in context.KnownFolderPaths)
            {
                if (!kp.EndsWith("\\Downloads", StringComparison.OrdinalIgnoreCase)
                    && !kp.EndsWith("\\下载", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsSameDrive(physicalVolumeRoot, kp)) continue;
                var dlEntry = FolderOrganize.Find(driveRootEntry, kp);
                if (dlEntry != null && !dlEntry.IsReparsePoint)
                {
                    foreach (var sub in dlEntry.ChildList.Where(c => c.IsDirectory && !c.IsFilesGroup && !c.IsReparsePoint && c.Size >= 512 * 1024))
                    {
                        string subNorm = CleanListSnapshot.NormPath(sub.FullPath);
                        if (subNorm.Length == 0 || targetBucketPaths.Contains(subNorm) || context.IgnoredPaths.Contains(subNorm))
                            continue;
                        if (seenPaths.Add(subNorm))
                            candidateDirs.Add(sub);
                    }
                }
            }
        }

        candidateDirs.Sort(static (a, b) =>
        {
            int bySize = b.Size.CompareTo(a.Size);
            if (bySize != 0) return bySize;
            return string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
        });

        int seq = 0;
        foreach (var dir in candidateDirs)
        {
            if (items.Count >= MaxCandidates) break;
            if (dir.Size == 0 && !dir.HasChildren && !dir.IsReparsePoint) continue;

            var summary = InspectTreeContent(dir);
            var (tier, safetyNote, installedHint, supportsJunction) = EvaluateSafetyGuard(dir, summary, context);
            string localCatId = SuggestCategoryLocally(dir, summary, tier, categories);

            var item = new ClassifyItem
            {
                Id = "c" + seq++,
                SourceEntry = dir,
                DriveRoot = driveRoot,
                SourcePath = CleanListSnapshot.NormPath(dir.FullPath),
                SourceName = dir.Name,
                SizeBytes = dir.Size,
                ContentDigest = summary.DigestText,
                IsGreenExecutable = tier == ClassifySafetyTier.ReviewGreenApp,
                InstalledProductHint = installedHint,
                Choices = choices,
                Tier = tier,
                SafetyNote = safetyNote,
                SupportsJunctionMigration = supportsJunction,
            };

            string chosenCat = tier == ClassifySafetyTier.BlockedConservative
                ? ClassifyCategoryRule.KeepId
                : localCatId;
            ApplyChoice(item, chosenCat, choices);
            item.FallbackCategoryId = chosenCat;
            // 保守规则：纯内容归类命中非 KeepId 默认预勾选；绿软/已安装/KeepInPlace 绝不自动勾
            item.IsChecked = tier == ClassifySafetyTier.Safe && !item.IsKeepInPlace;
            items.Add(item);
        }

        // 把真正可能整理到一起的 Safe/Review 排前面，保守保留放后面
        items.Sort(static (a, b) =>
        {
            int byTier = ((int)a.Tier).CompareTo((int)b.Tier);
            if (byTier != 0) return byTier;
            int bySize = b.SizeBytes.CompareTo(a.SizeBytes);
            if (bySize != 0) return bySize;
            return string.Compare(a.SourcePath, b.SourcePath, StringComparison.OrdinalIgnoreCase);
        });

        return items;
    }

    public static List<ClassifyCategoryChoice> BuildChoices(
        IReadOnlyList<ClassifyCategoryRule> categories,
        IReadOnlyDictionary<string, string> resolvedTargetNames)
    {
        var choices = new List<ClassifyCategoryChoice>(categories.Count + 1);
        foreach (var c in categories)
        {
            if (!c.Enabled || string.IsNullOrWhiteSpace(c.Id)) continue;
            string folder = resolvedTargetNames.TryGetValue(c.Id, out var fn) && !string.IsNullOrWhiteSpace(fn)
                ? fn
                : c.FolderName.Trim();
            choices.Add(new ClassifyCategoryChoice(c.Id, c.Name, folder));
        }
        choices.Add(new ClassifyCategoryChoice(ClassifyCategoryRule.KeepId, "保留在原位", ""));
        return choices;
    }

    public static void ApplyChoice(ClassifyItem item, string categoryId, IReadOnlyList<ClassifyCategoryChoice> choices)
    {
        var match = choices.FirstOrDefault(c => string.Equals(c.Id, categoryId, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            item.ApplyCategorySelection(ClassifyCategoryRule.KeepId, "保留在原位", "");
        else
            item.ApplyCategorySelection(match.Id, match.DisplayName, match.TargetFolderName);
    }

    public static ContentSummary InspectTreeContent(FileEntry rootDir)
    {
        var extStats = new Dictionary<string, (long Bytes, int Count)>(StringComparer.OrdinalIgnoreCase);
        var topFiles = new List<(string Name, long Size)>();
        var topDirs = new List<(string Name, long Size)>();
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasExe = false;
        bool hasGameMarker = false;
        bool hasVenvMarker = false;
        bool hasDevMarker = false;
        int walkedFiles = 0;
        int walkedDirs = 0;

        foreach (var c in rootDir.ChildList)
        {
            if (c.IsDirectory)
            {
                if (!c.IsFilesGroup)
                    topDirs.Add((c.Name, c.Size));
            }
            else
            {
                topFiles.Add((c.Name, c.Size));
            }
        }

        var stack = new Stack<(FileEntry Dir, int Depth)>();
        stack.Push((rootDir, 0));
        int budget = MaxWalkEntriesPerDir;

        while (stack.Count > 0 && budget > 0)
        {
            var (cur, depth) = stack.Pop();
            foreach (var child in cur.ChildList)
            {
                if (--budget <= 0) break;
                if (child.IsFilesGroup) continue;

                string name = child.Name ?? "";
                if (child.IsDirectory)
                {
                    walkedDirs++;
                    if (name.Equals(".venv", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("conda-meta", StringComparison.OrdinalIgnoreCase))
                    {
                        hasVenvMarker = true;
                        markers.Add(name);
                    }
                    else if (name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDevMarker = true;
                        markers.Add(".git");
                    }
                    else if (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        markers.Add(name);
                    }

                    if (depth < MaxWalkDepth && !child.IsReparsePoint)
                        stack.Push((child, depth + 1));
                }
                else
                {
                    walkedFiles++;
                    string ext = Path.GetExtension(name).ToLowerInvariant();
                    if (ext.Length == 0) ext = "(无扩展名)";

                    extStats.TryGetValue(ext, out var prev);
                    extStats[ext] = (prev.Bytes + Math.Max(0, child.Size), prev.Count + 1);

                    if (name.Equals("pyvenv.cfg", StringComparison.OrdinalIgnoreCase))
                    {
                        hasVenvMarker = true;
                        markers.Add("pyvenv.cfg");
                    }
                    if (GameMarkers.Contains(name))
                    {
                        hasGameMarker = true;
                        markers.Add(name);
                    }
                    if (GameExtensions.Contains(ext))
                    {
                        hasGameMarker = true;
                        markers.Add("*" + ext);
                    }
                    if (ExecutableExtensions.Contains(ext) && !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                        hasExe = true;
                    if (DevExtensions.Contains(ext))
                        hasDevMarker = true;
                }
            }
        }

        var topExt = extStats
            .OrderByDescending(kv => kv.Value.Bytes)
            .ThenByDescending(kv => kv.Value.Count)
            .Take(6)
            .Select(kv => (kv.Key, kv.Value.Bytes, kv.Value.Count))
            .ToList();

        var sampleFiles = topFiles
            .OrderByDescending(f => f.Size)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(f => f.Name)
            .ToList();

        var sampleDirs = topDirs
            .OrderByDescending(d => d.Size)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(d => d.Name)
            .ToList();

        long totalBytes = Math.Max(1, rootDir.Size);
        var extSummaryParts = new List<string>(3);
        foreach (var (ext, bytes, count) in topExt.Take(3))
        {
            double pct = Math.Clamp((double)bytes / totalBytes * 100.0, 0, 100);
            extSummaryParts.Add(pct >= 1 ? $"{ext} {pct:0}%" : $"{ext}×{count}");
        }

        string extLine = extSummaryParts.Count > 0 ? string.Join(" · ", extSummaryParts) : "空或仅目录";
        string markerLine = markers.Count > 0 ? " [" + string.Join(", ", markers.Take(3)) + "]" : "";
        string countHint = rootDir.FileCount > 0 ? $"{rootDir.FileCount:N0} 文件" : $"{walkedFiles:N0} 文件";
        string digest = $"{countHint} · {extLine}{markerLine}";

        return new ContentSummary(
            walkedFiles, walkedDirs, hasExe, hasGameMarker, hasVenvMarker, hasDevMarker,
            topExt, sampleFiles, sampleDirs, markers.ToList(), digest);
    }

    public static (ClassifySafetyTier Tier, string Reason, string InstalledHint, bool SupportsJunction) EvaluateSafetyGuard(
        FileEntry dir, ContentSummary summary, ContextSnapshot context)
    {
        string path = CleanListSnapshot.NormPath(dir.FullPath);
        string name = (dir.Name ?? "").Trim();

        if (ProtectedPaths.IsDriveRoot(path))
            return (ClassifySafetyTier.BlockedConservative, "磁盘根目录不可移动", "", false);

        if (dir.IsReparsePoint)
        {
            string jNote = JunctionNative.IsJunction(path, out var jt) && !string.IsNullOrWhiteSpace(jt)
                ? $"已通过隐藏软链映射到 {jt}"
                : "目录为链接 / 重解析点，避免破坏原映射";
            return (ClassifySafetyTier.BlockedConservative, jNote, "", false);
        }

        if (ProtectedPaths.IsProtectedEntry(dir) || ProtectedPaths.Classify(path).Guard == PathGuard.Blocked)
            return (ClassifySafetyTier.BlockedConservative, "系统核心或受保护目录，永久禁止移动", "", false);

        if (RootSystemNames.Contains(name) && ProtectedPaths.Segments(path).Length <= 2)
            return (ClassifySafetyTier.BlockedConservative, "系统与用户根目录容器，永久保留原地", "", false);

        if (context.KnownFolderPaths.Contains(path))
            return (ClassifySafetyTier.BlockedConservative, "Windows 活跃默认位置（系统下载/桌面/文档），禁止移动", "", false);

        if (IsHardNoJunctionPath(path))
            return (ClassifySafetyTier.BlockedConservative, "涉及安全防护或反作弊驱动目录，永久禁止迁移", "", false);

        if (FolderPurposeRules.IsObjectContainer(path))
        {
            return (ClassifySafetyTier.BlockedConservative,
                "游戏平台库目录（如 Steam/Epic）；默认锁死，可勾选「启用软链搬家」以 NTFS 隐藏软链无损迁移（搬前请先退出客户端）",
                name,
                true);
        }

        if (summary.HasVenvMarker)
        {
            return (ClassifySafetyTier.BlockedConservative,
                "内含 Python 虚拟环境（硬编码绝对路径）；普通移动会失效，可「启用软链搬家」保持原路径透明映射",
                "",
                true);
        }

        // 检查注册表安装清单：当前目录本身、其父级，或其内部包含已安装软件（支持用户主动启用 NTFS 隐藏软链无损搬家）
        foreach (var kv in context.InstalledLocations)
        {
            if (FolderOrganize.IsUnderOrEqual(path, kv.Key) || FolderOrganize.IsStrictlyUnder(kv.Key, path))
            {
                return (ClassifySafetyTier.BlockedConservative,
                    $"命中已安装程序「{kv.Value}」；直接移动会破坏注册表，可点击「启用软链搬家」以隐藏 NTFS Junction 无损迁移",
                    kv.Value,
                    true);
            }
        }

        // 本地特征库里标记 Keep（重要/运行数据）也按保守拦截，非红线软件目录允许用户手动启用软链迁移
        var sigHit = AppSignatures.Match(path);
        if (sigHit is { } sh && sh.Sig.Risk == SigRisk.Keep && (sh.Sig.Subs is not { Length: > 0 } || sh.SubHit))
        {
            return (ClassifySafetyTier.BlockedConservative,
                $"识别为程序或工具链「{sh.Sig.Name}」；默认原地保护，可点击「启用软链搬家」以隐藏软链无损迁移",
                sh.Sig.Name,
                true);
        }

        // 可执行程序 / 绿色单机游戏：允许整理建议，但绝不默认全自动勾选
        if (summary.HasExecutable || summary.HasGameMarker)
        {
            string note = summary.HasGameMarker
                ? "未在安装清单中登记，疑似免安装单机游戏；请核对是否直接运行后再勾选"
                : "内含可执行文件（.exe / .msi 等），未命中系统卸载清单；请确认无需固定路径后再勾选";
            return (ClassifySafetyTier.ReviewGreenApp, note, "", true);
        }

        return (ClassifySafetyTier.Safe, "纯内容或数据目录，无安装注册表依赖，可放心归类", "", true);
    }

    public static string SuggestJunctionCategoryId(ClassifyItem item, IReadOnlyList<ClassifyCategoryRule> categories)
    {
        bool HasEnabled(string id) => categories.Any(c => c.Enabled && c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        string hint = ((item.SourceName ?? "") + " " + (item.SourcePath ?? "") + " " + (item.InstalledProductHint ?? "") + " " + (item.ContentDigest ?? "")).ToLowerInvariant();

        if ((hint.Contains("steam") || hint.Contains("epic") || hint.Contains("wegame")
             || hint.Contains("game") || hint.Contains("gog") || hint.Contains("ubisoft")
             || hint.Contains("riot") || hint.Contains("battle.net") || hint.Contains("minecraft")
             || hint.Contains("pcl") || hint.Contains("hmcl") || hint.Contains("tgp"))
            && HasEnabled("games"))
            return "games";

        if ((hint.Contains("msys") || hint.Contains("mingw") || hint.Contains("python") || hint.Contains("venv")
             || hint.Contains("conda") || hint.Contains("node") || hint.Contains("jdk") || hint.Contains("sdk")
             || hint.Contains("git") || hint.Contains("code") || hint.Contains("studio") || hint.Contains("dev")
             || hint.Contains("unity") || hint.Contains("unreal") || hint.Contains("vmware") || hint.Contains("virtualbox")
             || hint.Contains("pintos") || hint.Contains("docker") || hint.Contains("jetbrains") || hint.Contains("idea")
             || hint.Contains("pycharm") || hint.Contains("clion") || hint.Contains("goland") || hint.Contains("rust")
             || hint.Contains("llvm") || hint.Contains("cmake"))
            && HasEnabled("dev"))
            return "dev";

        if ((hint.Contains("video") || hint.Contains("movie") || hint.Contains("media") || hint.Contains("music")
             || hint.Contains("photo") || hint.Contains("anime") || hint.Contains("bilibili")
             || hint.Contains("视频") || hint.Contains("影视") || hint.Contains("音乐") || hint.Contains("相册"))
            && HasEnabled("media"))
            return "media";

        if ((hint.Contains("download") || hint.Contains("driver") || hint.Contains("install") || hint.Contains("setup")
             || hint.Contains("package") || hint.Contains("驱动") || hint.Contains("下载") || hint.Contains("安装包"))
            && HasEnabled("installers"))
            return "installers";

        if ((hint.Contains("doc") || hint.Contains("book") || hint.Contains("pdf") || hint.Contains("office")
             || hint.Contains("study") || hint.Contains("文档") || hint.Contains("资料") || hint.Contains("课件"))
            && HasEnabled("docs"))
            return "docs";

        if (HasEnabled("dev")) return "dev";
        return categories.FirstOrDefault(c => c.Enabled)?.Id ?? ClassifyCategoryRule.KeepId;
    }

    public static string SuggestCategoryLocally(
        FileEntry dir,
        ContentSummary summary,
        ClassifySafetyTier tier,
        IReadOnlyList<ClassifyCategoryRule> categories)
    {
        if (tier == ClassifySafetyTier.BlockedConservative)
            return ClassifyCategoryRule.KeepId;

        bool HasEnabled(string id) => categories.Any(c => c.Enabled && c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        long totalBytes = Math.Max(1, summary.TopExtensions.Sum(e => e.Bytes));
        long SumBytes(HashSet<string> set) => summary.TopExtensions.Where(e => set.Contains(e.Ext)).Sum(e => e.Bytes);

        if (summary.HasGameMarker && HasEnabled("games")) return "games";

        if ((double)SumBytes(MediaExtensions) / totalBytes >= 0.45 && HasEnabled("media"))
            return "media";

        if ((summary.Markers.Contains(".git") || (double)SumBytes(DevExtensions) / totalBytes >= 0.45) && HasEnabled("dev"))
            return "dev";

        if ((double)SumBytes(DocsExtensions) / totalBytes >= 0.45 && HasEnabled("docs"))
            return "docs";

        if ((LooseDownloadDirNames.Contains(dir.Name) || (double)SumBytes(InstallerExtensions) / totalBytes >= 0.45)
            && HasEnabled("installers"))
            return "installers";

        if (summary.HasExecutable && summary.HasGameMarker && HasEnabled("games"))
            return "games";

        return ClassifyCategoryRule.KeepId;
    }

    public async Task<ClassifyAiResult> ClassifyWithAiAsync(
        IChatModel model,
        IReadOnlyList<ClassifyItem> items,
        IReadOnlyList<ClassifyCategoryRule> categories,
        bool sendFullPaths,
        IProgress<ClassifyProgress>? progress = null,
        CancellationToken ct = default,
        Func<IReadOnlyList<ClassifyItem>, Task>? onBatchReady = null)
    {
        var enabledCats = categories.Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.Id)).ToList();
        if (enabledCats.Count == 0)
            return new ClassifyAiResult(false, 0, 0, "没有启用的目标分类");

        // 永久硬锁死项不送给 AI；已开启「NTFS 隐藏软链」兜底的非红线软件/游戏库正常交由 AI 精细分入对应用途大类
        var askable = items
            .Where(i => !i.IsBlocked && !i.Executed)
            .Take(MaxCandidates)
            .ToList();
        if (askable.Count == 0)
            return new ClassifyAiResult(false, 0, 0, "当前盘没有可整理的零散目录（已全部按系统/软件规则保守保护）");

        var targetNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (items.Count > 0)
        {
            var rootEntry = items[0].SourceEntry;
            while (rootEntry.Parent != null) rootEntry = rootEntry.Parent;
            targetNames = ResolveCategoryTargetNames(rootEntry, categories);
        }
        var choices = BuildChoices(categories, targetNames);

        int batches = (askable.Count + CardsPerBatch - 1) / CardsPerBatch;
        var slices = new List<List<ClassifyItem>>(batches);
        for (int b = 0; b < batches; b++)
            slices.Add(askable.Skip(b * CardsPerBatch).Take(CardsPerBatch).ToList());

        // 一次性在后台构建所有批次的本地指纹提示词（~20ms），避免批间卡顿
        var prompts = await Task.Run(() =>
            slices.Select(s => BuildAiPrompt(s, enabledCats, sendFullPaths)).ToList(), ct);

        Task<ChatResponse> SendBatchAsync(int idx)
        {
            var req = new ChatRequest
            {
                Model = model.ModelId,
                Messages = new[]
                {
                    new ChatMessage(ChatRole.System, SystemPrompt),
                    new ChatMessage(ChatRole.User, prompts[idx]),
                },
                Temperature = 0.15,
                MaxTokens = 8192,
                JsonMode = true,
            };
            return model.CompleteAsync(req, ct);
        }

        int totalClassified = 0;
        int calls = 0;
        Task<ChatResponse>? nextRequest = batches > 0 ? SendBatchAsync(0) : null;

        for (int b = 0; b < batches; b++)
        {
            ct.ThrowIfCancellationRequested();
            var slice = slices[b];
            progress?.Report(new ClassifyProgress("calling", b, batches, $"AI 正在逐批研判目录（第 {b + 1}/{batches} 批）…"));

            ChatResponse resp;
            try
            {
                calls++;
                resp = await nextRequest!;
            }
            catch (ChatModelException ex)
            {
                return new ClassifyAiResult(false, totalClassified, calls, ex.Message, ex.Kind);
            }

            if (string.IsNullOrWhiteSpace(resp.Content))
            {
                string msg = !string.IsNullOrWhiteSpace(resp.Reasoning)
                    ? "模型只输出了思考过程、未返回最终方案正文（建议换非推理对话模型如 deepseek-chat 或重试）"
                    : "模型返回为空";
                return new ClassifyAiResult(false, totalClassified, calls, msg, ChatErrorKind.Protocol);
            }

            var validIds = new HashSet<string>(slice.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
            var verdicts = ParseAiVerdicts(resp.Content, validIds, enabledCats);
            if (verdicts.Count == 0 && b == 0 && batches == 1)
                return new ClassifyAiResult(false, totalClassified, calls, "AI 返回格式未能识别为分类方案，请重试或检查模型", ChatErrorKind.Protocol);

            int updated = ApplyAiVerdictsToItems(slice, verdicts, choices);
            totalClassified += updated;

            // 先触发下一批网络请求，再播放当前批的逐行排序动画：网络请求与 UI 动画完全并发
            nextRequest = (b + 1 < batches) ? SendBatchAsync(b + 1) : null;
            if (onBatchReady != null)
                await onBatchReady(slice);
        }

        if (totalClassified == 0)
            return new ClassifyAiResult(false, 0, calls, "AI 没有返回任何匹配的目录结果，请重试");

        progress?.Report(new ClassifyProgress("done", batches, batches, $"AI 已为 {totalClassified} 个文件夹给出整理方案"));
        return new ClassifyAiResult(true, totalClassified, calls);
    }

    private sealed record AiVerdict(string FolderId, string CategoryId, string Safety, string Reason);

    private static List<AiVerdict> ParseAiVerdicts(
        string raw,
        HashSet<string> validFolderIds,
        IReadOnlyList<ClassifyCategoryRule> enabledCats)
    {
        var list = new List<AiVerdict>();
        string jsonText = ExtractAnyJson(raw);
        if (jsonText.Length == 0) return list;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(jsonText); }
        catch { return list; }

        var validCatIds = new HashSet<string>(enabledCats.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

        using (doc)
        {
            JsonElement arr;
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                arr = doc.RootElement;
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (!doc.RootElement.TryGetProperty("items", out arr)
                    && !doc.RootElement.TryGetProperty("folders", out arr)
                    && !doc.RootElement.TryGetProperty("results", out arr))
                {
                    return list;
                }
                if (arr.ValueKind != JsonValueKind.Array) return list;
            }
            else
            {
                return list;
            }

            foreach (var elem in arr.EnumerateArray())
            {
                if (elem.ValueKind != JsonValueKind.Object) continue;
                string id = FirstStr(elem, "folderId", "folder_id", "id");
                if (!validFolderIds.Contains(id)) continue;

                string catId = FirstStr(elem, "categoryId", "category_id", "category");
                if (!validCatIds.Contains(catId))
                    catId = ClassifyCategoryRule.KeepId;

                string reason = Truncate(FirstStr(elem, "reason", "why", "summary"), 68);
                string safety = FirstStr(elem, "safety", "risk", "tier").ToLowerInvariant();
                list.Add(new AiVerdict(id, catId, safety, reason));
            }
        }

        return list;
    }

    private static int ApplyAiVerdictsToItems(
        IReadOnlyList<ClassifyItem> slice,
        IReadOnlyList<AiVerdict> verdicts,
        IReadOnlyList<ClassifyCategoryChoice> choices)
    {
        var byId = slice.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        int applied = 0;

        foreach (var v in verdicts)
        {
            if (!byId.TryGetValue(v.FolderId, out var item)) continue;

            // 本地红线硬守：未启用软链的保守红线项任何时候不能被 AI 修改归类或解锁勾选
            if (item.IsBlocked) continue;

            string catId = v.CategoryId;
            bool keepChosen = string.Equals(catId, ClassifyCategoryRule.KeepId, StringComparison.OrdinalIgnoreCase);

            if (v.Safety is "keep" or "blocked")
            {
                if (!keepChosen && item.SupportsJunctionMigration)
                {
                    // AI 给出了具体大类（如 dev/games），结合 NTFS 隐藏软链即可安全按色归拢
                    if (item.Tier == ClassifySafetyTier.Safe)
                        item.Tier = ClassifySafetyTier.ReviewGreenApp;
                    item.UseJunction = true;
                }
                else if (item.Tier == ClassifySafetyTier.BlockedConservative && item.UseJunction
                         && item.FallbackCategoryId != ClassifyCategoryRule.KeepId)
                {
                    catId = item.FallbackCategoryId;
                }
                else
                {
                    catId = ClassifyCategoryRule.KeepId;
                }
            }
            else
            {
                if (v.Safety is "review" or "caution" && item.Tier == ClassifySafetyTier.Safe)
                    item.Tier = ClassifySafetyTier.ReviewGreenApp;

                if (keepChosen && item.Tier == ClassifySafetyTier.BlockedConservative && item.UseJunction
                    && item.FallbackCategoryId != ClassifyCategoryRule.KeepId)
                {
                    catId = item.FallbackCategoryId;
                }
            }

            // 若本地已标记含可执行文件，即使 AI 写了 safe 也不许自动升为 Safe
            if (item.IsGreenExecutable && item.Tier == ClassifySafetyTier.Safe)
                item.Tier = ClassifySafetyTier.ReviewGreenApp;

            ApplyChoice(item, catId, choices);
            if (v.Reason.Length > 0) item.AiReason = v.Reason;
            item.FromAi = true;

            // 保守策略：仅当 AI 确认安全且非绿软、未走软链迁移时才自动勾选；绿软与软链迁移项给好分类但让用户亲自打勾确认
            item.IsChecked = item.Tier == ClassifySafetyTier.Safe && !item.UseJunction && !item.IsKeepInPlace;
            applied++;
        }

        return applied;
    }

    private static string ExtractAnyJson(string raw)
    {
        string obj = ReportParser.ExtractJson(raw);
        if (obj.Length > 0) return obj;
        if (string.IsNullOrWhiteSpace(raw)) return "";
        int start = raw.IndexOf('[');
        int end = raw.LastIndexOf(']');
        if (start >= 0 && end > start)
            return raw.Substring(start, end - start + 1);
        return "";
    }

    private static string FirstStr(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
        {
            string v = Str(el, k);
            if (v.Length > 0) return v;
        }
        return "";
    }

    private static string BuildAiPrompt(
        IReadOnlyList<ClassifyItem> candidates,
        IReadOnlyList<ClassifyCategoryRule> categories,
        bool sendFullPaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("以下是用户自定义的目标分类规则（你只能从这些 categoryId 中选择，若不适合整理或有运行路径依赖风险请填 \"__keep__\"）：");
        sb.AppendLine("[");
        foreach (var c in categories)
        {
            sb.AppendLine($"  {{\"categoryId\":{JsonStr(c.Id)},\"name\":{JsonStr(c.Name)},\"folder\":{JsonStr(c.FolderName)},\"hint\":{JsonStr(c.Description)}}},");
        }
        sb.AppendLine("]");
        sb.AppendLine();
        sb.AppendLine("以下是待分析的同一盘符下的真实目录画像列表（只包含元数据与样本文件名）：");
        sb.AppendLine("[");
        foreach (var item in candidates)
        {
            var summary = InspectTreeContent(item.SourceEntry);
            string displayPath = sendFullPaths ? item.SourcePath : RedactUser(item.SourcePath);
            string exts = string.Join(", ", summary.TopExtensions.Take(4).Select(e => $"{e.Ext}({FileEntry.FormatSize(e.Bytes)},{e.Count})"));
            string samples = string.Join(", ", summary.SampleFiles.Take(6));
            string subdirs = string.Join(", ", summary.SampleDirs.Take(5));
            string markers = string.Join(", ", summary.Markers.Take(5));
            string safetyHint = item.Tier == ClassifySafetyTier.ReviewGreenApp ? "contains_executable_portable" : "data_or_mixed";

            sb.AppendLine("  {");
            sb.AppendLine($"    \"folderId\":{JsonStr(item.Id)},");
            sb.AppendLine($"    \"name\":{JsonStr(item.SourceName)},");
            sb.AppendLine($"    \"path\":{JsonStr(displayPath)},");
            sb.AppendLine($"    \"size\":{JsonStr(item.SizeText)},");
            sb.AppendLine($"    \"safetyHint\":{JsonStr(safetyHint)},");
            sb.AppendLine($"    \"extensions\":{JsonStr(exts)},");
            sb.AppendLine($"    \"sampleFiles\":{JsonStr(samples)},");
            sb.AppendLine($"    \"sampleSubdirs\":{JsonStr(subdirs)},");
            sb.AppendLine($"    \"markers\":{JsonStr(markers)}");
            sb.AppendLine("  },");
        }
        sb.AppendLine("]");
        return sb.ToString();
    }

    private const string SystemPrompt =
        "你是 Windows 磁盘文件夹整理顾问。本软件现已支持 NTFS 隐藏软链（Junction）无损迁移：移动软件、游戏库或开发工具后会在原路径自动建立隐藏软链，注册表与启动路径 100% 透明兼容。\n" +
        "你的任务：根据每个目录的真实扩展名占比、样本文件名、子目录名，判断文件夹里装的是什么，并归入用户提供的最匹配目标分类（categoryId）；只有在确认是系统底层核心时才填 \"__keep__\"。\n" +
        "严格约束：\n" +
        "1. 只有 Windows 系统核心组件、系统用户根容器、回收站($RECYCLE.BIN)或杀毒/反作弊驱动底座，才填 \"__keep__\" 或 safety:\"keep\"。\n" +
        "2. 已安装软件、游戏平台库（SteamLibrary/WeGameApps/Epic/Minecraft）、独立软件目录（如 Unity/Visual Studio/VMware/虚拟机/驱动包/开发工具链/含 .venv 目录），支持通过隐藏软链无损归拢：请务必按主要用途归入对应 categoryId（如 games / dev / installers），并诚实标 safety:\"review\"。\n" +
        "3. 纯番剧视频/文档/压缩包/下载资源可填 safety:\"safe\" 并选最匹配分类。\n" +
        "4. 只输出严格合法的 JSON，格式为：{\"items\":[{\"folderId\":\"c0\",\"categoryId\":\"media\",\"safety\":\"safe\",\"reason\":\"用20字左右说明实际内容依据\"}]}，不要附带任何 markdown 说明文字。";

    public static string RedactUser(string path)
        => string.IsNullOrEmpty(path) ? "" : UserSegment.Replace(path, "$1Users\\%USER%");

    private static string EnsureDirContainerPath(string? path)
    {
        string n = CleanListSnapshot.NormPath(path);
        return n.Length == 2 && n[1] == ':' ? n + "\\" : n;
    }

    private static bool IsSameDrive(string driveRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string? pRoot = Path.GetPathRoot(path);
        return !string.IsNullOrWhiteSpace(pRoot)
               && string.Equals(pRoot.TrimEnd('\\'), driveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    private static string JsonStr(string? s) => JsonSerializer.Serialize(s ?? "");

    private static string Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Truncate(string s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }
}
