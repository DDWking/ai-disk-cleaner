using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiDiskCleaner.Models;
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
/// 「软件内虚拟分类视图」分析服务：只读内存 MFT 树 + 本地特征规则 + AI 内容归纳。
///
/// 核心铁律：
/// 1) 100% 纯软件内可视化分组展示，绝不移动、改名、软链或改动磁盘上任何真实文件与注册表；
/// 2) 路径出站默认经 %USER% 脱敏；
/// 3) Windows 系统核心与根级容器归入底部「系统核心」分组，其余目录（含游戏库、应用、开发工具、影音文档）均可按真实用途清晰归色展示。
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

    private static readonly string[] SystemCoreTokens =
    {
        "windowsapps", "driverstore", "windows defender", "system32", "syswow64", "winsxs",
    };

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
        ".vmdk", ".vhdx", ".vdi",
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
        "downloads", "download", "迅雷下载", "百度网盘", "baidunetdiskdownload", "quarkdownloads", "下载",
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

    public static bool IsSystemCorePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        string norm = CleanListSnapshot.NormPath(path);
        if (ProtectedPaths.IsDriveRoot(norm)) return true;
        if (ProtectedPaths.Classify(norm).Guard == PathGuard.Blocked) return true;

        var segs = ProtectedPaths.Segments(norm);
        if (segs.Length == 2 && RootSystemNames.Contains(segs[1]))
            return true;

        foreach (var tok in SystemCoreTokens)
        {
            if (norm.Contains(tok, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static Dictionary<string, string> ResolveCategoryTargetNames(
        FileEntry? driveRootEntry,
        IReadOnlyList<ClassifyCategoryRule> categories)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in categories)
        {
            if (string.IsNullOrWhiteSpace(rule.Id)) continue;
            resolved[rule.Id] = (rule.FolderName ?? rule.Id).Trim();
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

        var candidateDirs = new List<FileEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddCandidate(FileEntry dir, bool expandDownloadsContainer)
        {
            if (!dir.IsDirectory || dir.IsFilesGroup) return;
            string norm = CleanListSnapshot.NormPath(dir.FullPath);
            if (norm.Length == 0 || !seenPaths.Add(norm)) return;
            if (context.IgnoredPaths.Contains(norm)) return;

            if (expandDownloadsContainer && !dir.IsReparsePoint)
            {
                foreach (var sub in dir.ChildList)
                {
                    if (!sub.IsDirectory || sub.IsFilesGroup || sub.IsReparsePoint || sub.Size < 512 * 1024) continue;
                    string subNorm = CleanListSnapshot.NormPath(sub.FullPath);
                    if (subNorm.Length == 0 || context.IgnoredPaths.Contains(subNorm))
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
                        if (subNorm.Length == 0 || context.IgnoredPaths.Contains(subNorm))
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
            var (tier, safetyNote, installedHint) = EvaluateSafetyGuard(dir, summary, context);
            string localCatId = SuggestCategoryLocally(dir, summary, tier, installedHint, categories);

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
            };

            string chosenCat = tier == ClassifySafetyTier.BlockedConservative
                ? ClassifyCategoryRule.KeepId
                : localCatId;
            ApplyChoice(item, chosenCat, choices);
            item.FallbackCategoryId = chosenCat;
            items.Add(item);
        }

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
        choices.Add(new ClassifyCategoryChoice(ClassifyCategoryRule.KeepId, "系统核心", ""));
        return choices;
    }

    public static void ApplyChoice(ClassifyItem item, string categoryId, IReadOnlyList<ClassifyCategoryChoice> choices)
    {
        var match = choices.FirstOrDefault(c => string.Equals(c.Id, categoryId, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            item.ApplyCategorySelection(ClassifyCategoryRule.KeepId, "系统核心");
        else
            item.ApplyCategorySelection(match.Id, match.DisplayName);
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

    public static (ClassifySafetyTier Tier, string Reason, string InstalledHint) EvaluateSafetyGuard(
        FileEntry dir, ContentSummary summary, ContextSnapshot context)
    {
        string path = CleanListSnapshot.NormPath(dir.FullPath);
        string name = (dir.Name ?? "").Trim();

        if (ProtectedPaths.IsDriveRoot(path))
            return (ClassifySafetyTier.BlockedConservative, "磁盘根目录", "");

        if (dir.IsReparsePoint)
            return (ClassifySafetyTier.BlockedConservative, "链接 / 重解析点目录", "");

        if (ProtectedPaths.IsProtectedEntry(dir) || ProtectedPaths.Classify(path).Guard == PathGuard.Blocked)
            return (ClassifySafetyTier.BlockedConservative, "系统核心或受保护目录", "");

        if (RootSystemNames.Contains(name) && ProtectedPaths.Segments(path).Length <= 2)
            return (ClassifySafetyTier.BlockedConservative, "系统与用户根目录容器", "");

        if (IsSystemCorePath(path))
            return (ClassifySafetyTier.BlockedConservative, "系统核心组件目录", "");

        // 仅软件内虚拟分组展示，不挪动真实文件：游戏平台库、已安装软件、开发工具链等全部正常参与分类归色
        if (FolderPurposeRules.IsObjectContainer(path))
            return (ClassifySafetyTier.ReviewGreenApp, "游戏平台库目录", name);

        if (summary.HasVenvMarker)
            return (ClassifySafetyTier.ReviewGreenApp, "内含 Python 虚拟环境", "");

        foreach (var kv in context.InstalledLocations)
        {
            if (FolderOrganize.IsUnderOrEqual(path, kv.Key) || FolderOrganize.IsStrictlyUnder(kv.Key, path))
                return (ClassifySafetyTier.ReviewGreenApp, $"已安装程序：{kv.Value}", kv.Value);
        }

        var sigHit = AppSignatures.Match(path);
        if (sigHit is { } sh && sh.Sig.Risk == SigRisk.Keep && (sh.Sig.Subs is not { Length: > 0 } || sh.SubHit))
            return (ClassifySafetyTier.ReviewGreenApp, $"程序或工具目录：{sh.Sig.Name}", sh.Sig.Name);

        if (summary.HasExecutable || summary.HasGameMarker)
        {
            string note = summary.HasGameMarker ? "游戏或图形引擎目录" : "应用程序目录";
            return (ClassifySafetyTier.ReviewGreenApp, note, "");
        }

        return (ClassifySafetyTier.Safe, "内容或数据目录", "");
    }

    public static string SuggestCategoryLocally(
        FileEntry dir,
        ContentSummary summary,
        ClassifySafetyTier tier,
        string installedHint,
        IReadOnlyList<ClassifyCategoryRule> categories)
    {
        if (tier == ClassifySafetyTier.BlockedConservative)
            return ClassifyCategoryRule.KeepId;

        bool HasEnabled(string id) => categories.Any(c => c.Enabled && c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        string lowerName = (dir.Name ?? "").Trim().ToLowerInvariant();
        string hint = (lowerName + " " + (installedHint ?? "") + " " + (summary.DigestText ?? "")).ToLowerInvariant();

        // 1) 优先按目录名精确匹配规则自身名称或别名（如 D:\文档 → docs，D:\视频 / D:\图片 → media，D:\Games → games）
        foreach (var rule in categories)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Id)) continue;
            if (!string.IsNullOrWhiteSpace(rule.FolderName) && lowerName.Equals(rule.FolderName.Trim(), StringComparison.OrdinalIgnoreCase))
                return rule.Id;
            if (rule.Aliases.Any(a => !string.IsNullOrWhiteSpace(a) && lowerName.Equals(a.Trim(), StringComparison.OrdinalIgnoreCase)))
                return rule.Id;
        }

        // 2) 知名开发引擎 / IDE / 虚拟机目录名（避免 Unity 内置 UnityPlayer.dll 误判为游戏）
        if (HasEnabled("dev") && (lowerName == "unity" || lowerName.StartsWith("unity ") || lowerName.StartsWith("unity_")
            || lowerName.Contains("unreal") || lowerName.Contains("godot") || lowerName.Contains("blender")
            || lowerName.Contains("visual studio") || lowerName.Contains("jetbrains") || lowerName.Contains("android studio")
            || lowerName.Contains("vscode") || lowerName.Contains("vmware") || lowerName.Contains("virtualbox")
            || lowerName.Contains("pintos") || lowerName.Contains("docker") || lowerName == "msys64" || lowerName == "mingw"
            || summary.HasVenvMarker || summary.Markers.Contains(".git")))
            return "dev";

        // 3) 知名游戏平台库与启动器目录
        if (HasEnabled("games") && (hint.Contains("steam") || hint.Contains("epic") || hint.Contains("wegame")
            || hint.Contains("game") || hint.Contains("gog") || hint.Contains("ubisoft")
            || hint.Contains("riot") || hint.Contains("battle.net") || hint.Contains("minecraft")
            || lowerName == "mc" || lowerName == "pcl" || lowerName == "hmcl" || summary.HasGameMarker))
            return "games";

        long totalBytes = Math.Max(1, summary.TopExtensions.Sum(e => e.Bytes));
        long SumBytes(HashSet<string> set) => summary.TopExtensions.Where(e => set.Contains(e.Ext)).Sum(e => e.Bytes);

        if ((double)SumBytes(MediaExtensions) / totalBytes >= 0.45 && HasEnabled("media"))
            return "media";

        if ((double)SumBytes(DevExtensions) / totalBytes >= 0.45 && HasEnabled("dev"))
            return "dev";

        if ((double)SumBytes(DocsExtensions) / totalBytes >= 0.45 && HasEnabled("docs"))
            return "docs";

        if ((LooseDownloadDirNames.Contains(dir.Name ?? "") || (double)SumBytes(InstallerExtensions) / totalBytes >= 0.45)
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

        // 系统核心容器不送给 AI；其余目录全部交由 AI 在软件内做精准可视化分类
        var askable = items
            .Where(i => !i.IsBlocked)
            .Take(MaxCandidates)
            .ToList();
        if (askable.Count == 0)
            return new ClassifyAiResult(false, 0, 0, "当前盘没有可归类的普通目录");

        var targetNames = ResolveCategoryTargetNames(null, categories);
        var choices = BuildChoices(categories, targetNames);

        int batches = (askable.Count + CardsPerBatch - 1) / CardsPerBatch;
        var slices = new List<List<ClassifyItem>>(batches);
        for (int b = 0; b < batches; b++)
            slices.Add(askable.Skip(b * CardsPerBatch).Take(CardsPerBatch).ToList());

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
            progress?.Report(new ClassifyProgress("calling", b, batches, $"AI 正在逐批归纳目录（第 {b + 1}/{batches} 批）…"));

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
                    ? "模型只输出了思考过程、未返回最终分类结果（建议换对话模型如 deepseek-chat 或重试）"
                    : "模型返回为空";
                return new ClassifyAiResult(false, totalClassified, calls, msg, ChatErrorKind.Protocol);
            }

            var validIds = new HashSet<string>(slice.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
            var verdicts = ParseAiVerdicts(resp.Content, validIds, enabledCats);
            if (verdicts.Count == 0 && b == 0 && batches == 1)
                return new ClassifyAiResult(false, totalClassified, calls, "AI 返回格式未能识别为分类结果，请重试或检查模型", ChatErrorKind.Protocol);

            int updated = ApplyAiVerdictsToItems(slice, verdicts, choices);
            totalClassified += updated;

            nextRequest = (b + 1 < batches) ? SendBatchAsync(b + 1) : null;
            if (onBatchReady != null)
                await onBatchReady(slice);
        }

        if (totalClassified == 0)
            return new ClassifyAiResult(false, 0, calls, "AI 没有返回任何匹配的目录结果，请重试");

        progress?.Report(new ClassifyProgress("done", batches, batches, $"AI 已在软件内为 {totalClassified} 个目录完成分类"));
        return new ClassifyAiResult(true, totalClassified, calls);
    }

    private sealed record AiVerdict(string FolderId, string CategoryId, string Reason);

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
                list.Add(new AiVerdict(id, catId, reason));
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
            if (item.IsBlocked) continue;

            string catId = v.CategoryId;
            if (string.Equals(catId, ClassifyCategoryRule.KeepId, StringComparison.OrdinalIgnoreCase)
                && item.FallbackCategoryId != ClassifyCategoryRule.KeepId)
            {
                catId = item.FallbackCategoryId;
            }

            ApplyChoice(item, catId, choices);
            if (v.Reason.Length > 0) item.AiReason = v.Reason;
            item.FromAi = true;
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
        sb.AppendLine("以下是软件内虚拟分类的目标分组列表（仅作界面可视化展示，绝不移动任何真实磁盘文件；请为每个目录选出最贴切的 categoryId，只有真正属于系统底层的目录才填 \"__keep__\"）：");
        sb.AppendLine("[");
        foreach (var c in categories)
        {
            sb.AppendLine($"  {{\"categoryId\":{JsonStr(c.Id)},\"name\":{JsonStr(c.Name)},\"hint\":{JsonStr(c.Description)}}},");
        }
        sb.AppendLine("]");
        sb.AppendLine();
        sb.AppendLine("以下是待分类目录的只读画像列表（只包含元数据与样本文件名）：");
        sb.AppendLine("[");
        foreach (var item in candidates)
        {
            var summary = InspectTreeContent(item.SourceEntry);
            string displayPath = sendFullPaths ? item.SourcePath : RedactUser(item.SourcePath);
            string exts = string.Join(", ", summary.TopExtensions.Take(4).Select(e => $"{e.Ext}({FileEntry.FormatSize(e.Bytes)},{e.Count})"));
            string samples = string.Join(", ", summary.SampleFiles.Take(6));
            string subdirs = string.Join(", ", summary.SampleDirs.Take(5));
            string markers = string.Join(", ", summary.Markers.Take(5));

            sb.AppendLine("  {");
            sb.AppendLine($"    \"folderId\":{JsonStr(item.Id)},");
            sb.AppendLine($"    \"name\":{JsonStr(item.SourceName)},");
            sb.AppendLine($"    \"path\":{JsonStr(displayPath)},");
            sb.AppendLine($"    \"size\":{JsonStr(item.SizeText)},");
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
        "你是 Windows 磁盘目录视觉分类助手。注意：本软件仅在界面内将文件夹按用途做彩色分组展示，绝不移动或修改磁盘上任何真实文件，因此完全不存在破坏软件或注册表的风险。\n" +
        "你的任务：根据每个目录的扩展名占比、样本文件名、子目录名，判断里面装的是什么，并归入最贴切的 categoryId。\n" +
        "规则：\n" +
        "1. 只有 Windows 系统底层目录、回收站($RECYCLE.BIN)或完全无法判断用途的系统杂项才填 \"__keep__\"。\n" +
        "2. 所有游戏本体、SteamLibrary/WeGameApps/Epic/Minecraft 等归入 games；所有影音/番剧/图片/录屏归入 media；所有软件/IDE/Unity/VMware/虚拟机/代码仓库/SDK 归入 dev；所有下载目录/压缩包/驱动/安装包归入 installers；所有文档/书籍/课件/备份资料归入 docs。\n" +
        "3. 只输出严格合法的 JSON，格式为：{\"items\":[{\"folderId\":\"c0\",\"categoryId\":\"media\",\"reason\":\"用20字左右说明目录里装的是什么\"}]}，不要附带任何 markdown 说明文字。";

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
