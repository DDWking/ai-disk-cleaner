using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AiDiskCleaner.Models;
using UninstallTools;

namespace AiDiskCleaner.Services;

/// <summary>
/// 软件卸载建议的安全边界。规则负责硬性拦截和基础判断，远程 AI 只能补充解释与排序。
/// </summary>
public static class AppRecommendationService
{
    private static readonly string[] BloatNames =
    {
        "2345", "驱动精灵", "驱动人生", "快压", "好压", "鲁大师",
        "优化大师", "浏览器助手", "网址大全",
    };
    private static readonly string[] SafetyCriticalNames =
    {
        "microsoft visual c++", "visual studio", ".net runtime", ".net framework",
        "windows sdk", "windows driver kit", "directx", "webview2",
        "nvidia", "amd software", "intel", "realtek", "driver", "驱动",
        "defender", "antivirus", "security", "firewall", "火绒", "卡巴斯基",
        "eset", "bitdefender", "安全卫士", "杀毒", "防火墙",
        "vmware", "virtualbox", "docker", "wsl", "hyper-v",
    };

    public static void ApplyLocalRules(IList<AppUninstallItem> apps)
        => ApplyLocalRules(apps, clearSelection: true);

    public static void ApplyLocalRules(IList<AppUninstallItem> apps, bool clearSelection)
    {
        foreach (var app in apps)
        {
            var result = LocalRule(app);
            Apply(app, result, fromAi: false, clearSelection);
        }
    }

    public static int ApplyAiResults(IList<AppUninstallItem> apps, IEnumerable<AppRecommendation> results)
    {
        var known = apps
            .GroupBy(x => x.AppId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        int applied = 0;
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.AppId)) continue;
            if (!known.TryGetValue(result.AppId, out var app)) continue;
            var safe = EnforceSafety(app, result);
            Apply(app, safe, fromAi: true, clearSelection: false);
            applied++;
        }
        return applied;
    }

    public static async Task<List<AppRecommendation>> AnalyzeRemoteAsync(
        IList<AppUninstallItem> apps,
        CancellationToken ct)
    {
        if (!AiConfigured()) return new();

        var candidates = apps
            .Where(x => x.CanUninstall)
            .OrderByDescending(x => x.ActualSizeBytes)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(300)
            .ToList();
        if (candidates.Count == 0) return new();

        var remoteCandidates = candidates
            .Select((app, index) => new RemoteCandidate(app, "app-" + (index + 1).ToString("D3")))
            .ToList();
        string payload = JsonSerializer.Serialize(remoteCandidates.Select(x => new
        {
            appId = x.RemoteId,
            name = x.App.Name,
            publisher = x.App.Publisher,
            version = x.App.Version,
            size = x.App.ActualSizeText,
            installDate = x.App.InstallDateText,
            runningState = x.App.RunningState switch
            {
                AppRunningState.Running => "running",
                AppRunningState.NotRunning => "not-running",
                _ => "unknown",
            },
            startup = x.App.HasStartup,
        }));

        var turns = new List<AiMsg>
        {
            new() { Role = "user", Text = Loc.AiAppsPrompt(payload) },
        };
        string text = await AiClient.ChatAsync(Loc.AiAppsSystem, turns, ct);
        var results = Parse(text);
        var localIds = remoteCandidates.ToDictionary(
            x => x.RemoteId,
            x => x.App.AppId,
            StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (localIds.TryGetValue(result.AppId, out var appId))
                result.AppId = appId;
            else
                result.AppId = "";
        }
        return results;
    }

    private static readonly object UsageCacheGate = new();
    private static string _usageCacheKey = "";
    private static List<AppUsage>? _usageCache;

    /// <summary>
    /// 算每个软件的真实占用。同一份 (软件清单, 扫描结果) 只算一次 ——
    /// 卸载后重扫会重新算，但切页签、刷新界面这类操作不会再跑一遍。
    /// </summary>
    public static List<AppUsage> CalculateUsage(
        IEnumerable<AppUninstallItem> apps,
        IEnumerable<FileEntry> files,
        CancellationToken ct = default)
    {
        var appList = apps as IList<AppUninstallItem> ?? apps.ToList();
        var fileList = files as IReadOnlyList<FileEntry> ?? files.ToList();

        string key = BuildUsageCacheKey(appList, fileList);
        lock (UsageCacheGate)
        {
            if (_usageCache != null && key == _usageCacheKey)
                return _usageCache;
        }

        var computed = CalculateUsageCore(appList, fileList, ct);

        lock (UsageCacheGate)
        {
            _usageCacheKey = key;
            _usageCache = computed;
        }
        return computed;
    }

    /// <summary>缓存键：软件数量 + 安装路径 + 扫描到的文件数 + 总分配字节。</summary>
    private static string BuildUsageCacheKey(IList<AppUninstallItem> apps, IReadOnlyList<FileEntry> files)
    {
        var sb = new System.Text.StringBuilder(64 + apps.Count * 24);
        sb.Append(apps.Count).Append('|');
        foreach (var a in apps)
        {
            sb.Append(a.InstallLocation).Append('\u0001');
            sb.Append(a.CanUninstall ? '1' : '0');
            sb.Append('\u0002');
        }
        long total = 0;
        for (int i = 0; i < files.Count; i++) total += files[i].Allocated > 0 ? files[i].Allocated : files[i].Size;
        sb.Append('|').Append(files.Count).Append('|').Append(total);
        return sb.ToString();
    }

    /// <summary>丢弃占用缓存（卸载完成、重新扫描后调用）。</summary>
    public static void InvalidateUsageCache()
    {
        lock (UsageCacheGate)
        {
            _usageCache = null;
            _usageCacheKey = "";
        }
    }

    private static List<AppUsage> CalculateUsageCore(
        IList<AppUninstallItem> appList,
        IReadOnlyList<FileEntry> fileList,
        CancellationToken ct)
    {
        var indexed = fileList
            .Select(x => new FileUsage(NormalizePath(x.FullPath), Math.Max(0, x.Allocated > 0 ? x.Allocated : x.Size)))
            .Where(x => x.Path.Length > 0)
            .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // 本次扫描覆盖了哪些盘的根：安装目录不在这上面时，**根本没有数据**，
        // 不能把「没测到」说成软件的体积。
        var scannedRoots = indexed
            .Select(x => Path.GetPathRoot(x.Path) ?? "")
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var locations = appList
            .Select(app => new AppLocation(app, NormalizePath(app.InstallLocation)))
            .ToList();
        var overlappingApps = FindOverlappingApps(locations);
        var processes = GetProcessPaths();
        var usage = new List<AppUsage>();
        foreach (var item in locations)
        {
            ct.ThrowIfCancellationRequested();
            var app = item.App;
            long actual = 0;
            long installBytes = 0, userBytes = 0, cacheBytes = 0;
            string skipNote = "";
            bool attributable = IsUsableInstallLocation(item.Location);
            if (!attributable)
            {
                skipNote = item.Location.Length == 0
                    ? Loc.AppFootprintWhyMissing
                    : Loc.AppFootprintWhyGenericRoot;
            }
            else if (!CanAttributeLocation(item.Location, out string rootWhy))
            {
                attributable = false;
                skipNote = rootWhy;
            }
            else if (overlappingApps.Contains(app))
            {
                // 安装目录和别的软件重叠（含「父目录被多个软件共用」）：
                // 整棵树的总量**不属于任何单个软件**，谁都不给。
                attributable = false;
                skipNote = Loc.AppFootprintWhyShared;
            }
            else
            {
                string locationRoot = Path.GetPathRoot(item.Location) ?? "";
                if (locationRoot.Length > 0 && scannedRoots.Count > 0 && !scannedRoots.Contains(locationRoot))
                {
                    // 跨盘：安装目录不在本次扫描的盘里，没有数据可算。
                    attributable = false;
                    skipNote = Loc.AppFootprintWhyNotScanned;
                }
            }

            if (attributable)
            {
                int start = LowerBound(indexed, item.Location);
                for (int i = start; i < indexed.Count; i++)
                {
                    string path = indexed[i].Path;
                    if (!path.StartsWith(item.Location, StringComparison.OrdinalIgnoreCase)) break;
                    if (IsPathWithin(path, item.Location))
                    {
                        long bytes = indexed[i].Bytes;
                        actual += bytes;
                        switch (ClassifyFootprint(path, item.Location))
                        {
                            case FootprintKind.Cache: cacheBytes += bytes; break;
                            case FootprintKind.UserData: userBytes += bytes; break;
                            default: installBytes += bytes; break;
                        }
                        continue;
                    }
                    break;
                }
            }
            var runningState = GetRunningState(
                item.Location,
                IsUsableInstallLocation(item.Location),
                processes);
            // 只有真测到内容才算「实测」。测到 0 不等于「这个软件是 0 字节」，
            // 所以这里如实落回安装记录 / 未知（见 AppUninstallItem.FootprintSource）。
            bool measured = attributable && actual > 0;
            if (!measured && skipNote.Length == 0 && !Directory.Exists(item.Location))
                skipNote = Loc.AppFootprintWhyMissing;
            usage.Add(new AppUsage(
                app,
                measured ? actual : app.SizeBytes,
                measured,
                runningState,
                measured ? installBytes : 0,
                measured ? userBytes : 0,
                measured ? cacheBytes : 0,
                measured ? "" : skipNote));
        }
        return usage;
    }

    public static void ApplyUsage(IEnumerable<AppUsage> usage)
    {
        foreach (var item in usage)
        {
            item.App.ActualSizeBytes = item.ActualSizeBytes;
            item.App.HasMeasuredSize = item.HasMeasuredSize;
            item.App.RunningState = item.RunningState;
            item.App.FootprintNote = item.FootprintNote;
            // 占用拆分：安装目录 / 用户数据 / 缓存（可释放）
            item.App.InstallDirBytes = item.InstallDirBytes;
            item.App.UserDataBytes = item.UserDataBytes;
            item.App.CacheBytes = item.CacheBytes;
        }
    }

    public static AppRecommendation LocalRule(AppUninstallItem app)
    {
        if (app.IsProtected || app.SystemComponent || app.GroupKey == 2)
            return Keep(1, Loc.AppKeepSystem);
        if (!app.CanUninstall)
            return Keep(1, Loc.AppKeepNoUninstaller);
        // Windows 自带组件 / 厂商驱动软件：由**卸载程序路径或安装路径**结构判定，
        // 不是靠名字里出现「windows」这种子串（那会把真正的软件也误伤）。
        if (app.InboxComponent || IsInboxComponent(app))
            return Keep(0.9, Loc.AppKeepInboxComponent);
        if (IsSafetyCritical(app))
            return Keep(0.9, Loc.AppKeepCritical);
        // 「正在运行」是**一件具体的、可核实的事**，值得用户看一眼；
        // 但它同时也意味着现在卸载会失败，所以只到「需要看一下」，永不 Recommend。
        if (app.RunningState == AppRunningState.Running)
            return Consider(0.95, Loc.AppConsiderRunning, Loc.AppRunningWarning);
        // 命中已知捆绑软件特征：这是**有依据的结论**，理由栏写这条依据。
        // 但「没法确认它没在跑」时不得升到「建议卸载」—— 只降一档，理由仍然是真信号。
        if (IsKnownBloat(app))
        {
            return app.RunningState == AppRunningState.NotRunning
                ? Recommend(0.86, Loc.AppRecommendBloat)
                : Consider(0.72, Loc.AppRecommendBloat);
        }
        // 其余一律**中性**。以前这里会依次用「无法确认是否正在运行」「占用空间较大」
        // 「会随系统启动」「没有足够依据」把它们全塞进「可以考虑」，207 行全是同一句套话，
        // 没有任何决策价值。没有依据就是没有依据。
        return Neutral();
    }

    /// <summary>
    /// 中性档：不说可以卸，也不说别卸，理由栏留空（不写套话）。
    /// </summary>
    static AppRecommendation Neutral()
        => new()
        {
            Decision = AppRecommendationDecision.Neutral,
            Confidence = 0,
            Reason = Loc.AppNeutralReason,
            DataWarning = "",
        };

    static AppRecommendation EnforceSafety(AppUninstallItem app, AppRecommendation result)
    {
        var local = LocalRule(app);
        if (app.IsProtected || app.SystemComponent || app.GroupKey == 2 || !app.CanUninstall
            || app.InboxComponent || IsInboxComponent(app) || IsSafetyCritical(app))
            return local;
        if (app.RunningState != AppRunningState.NotRunning
            && result.Decision == AppRecommendationDecision.Recommend)
        {
            // 运行状态没确认（或确认在跑）时，模型不许把它抬到「建议卸载」；
            // 同时明确告诉用户先退出软件 —— 这句话只在真的有这回事时出现，
            // 不再像以前那样挂在两百多行上。
            result.Decision = AppRecommendationDecision.Consider;
            result.DataWarning = AppendWarning(result.DataWarning, Loc.AppRunningWarning);
        }
        result.Confidence = Math.Clamp(result.Confidence, 0, 1);
        if (string.IsNullOrWhiteSpace(result.Reason)) result.Reason = local.Reason;
        return result;
    }

    static bool IsKnownBloat(AppUninstallItem app)
    {
        string text = string.Join(" ", app.Name, app.Publisher, app.InstallLocation);
        return BloatNames.Any(x => text.Contains(x, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// 运行库 / 驱动 / 安全 / 虚拟化组件：按**名字签名表**判定。
    /// 注意这里**不含**「Windows 自带组件」那条结构化判定 ——
    /// 那个由 <see cref="IsInboxComponent(AppUninstallItem)"/> 单独判定，
    /// 因为两者的理由文案不同，用户看到的原因必须是准确的那一条。
    /// </summary>
    static bool IsSafetyCritical(AppUninstallItem app)
    {
        string text = string.Join(" ", app.Name, app.Publisher, app.InstallLocation);
        return SafetyCriticalNames.Any(x => text.Contains(x, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Windows 目录（含 System32 / SysWOW64 / WinSxS），由系统 API 解析，不写死盘符。</summary>
    static string WindowsDirectory
        => Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');

    /// <summary>
    /// 这个条目是不是 Windows 自带组件 / 设备软件。
    ///
    /// 依据是**结构**，不是名字：卸载命令行里的可执行文件在 Windows 目录内，
    /// 或者记录下来的安装位置在 Windows 目录内。
    /// 实测依据：本机 `Microsoft® Windows® Operating System` 的卸装命令是
    /// `"C:\Windows\System32\mstsc.exe" /uninstall`、安装位置 `C:\Windows\System32`，
    /// 但 BCU 的 SystemComponent / IsProtected 都是 false —— 只用 BCU 的标记会漏掉它。
    /// </summary>
    public static bool IsInboxComponent(AppUninstallItem app)
        => app.InboxComponent || IsInboxComponent(app.Entry?.UninstallString, app.InstallLocation);

    /// <summary>同上，但直接吃原始字段（BCU 清点阶段就要把标记落下来）。</summary>
    public static bool IsInboxComponent(string? uninstallString, string? installLocation)
    {
        string win = WindowsDirectory;
        if (win.Length == 0) return false;
        string exe = UninstallExecutable(uninstallString);
        if (exe.Length > 0 && IsPathWithin(exe, win)) return true;
        string location = NormalizePath(installLocation);
        return location.Length > 0 && IsPathWithin(location, win);
    }

    /// <summary>从卸载命令行里取出可执行文件路径（带引号或不带引号都认）。</summary>
    internal static string UninstallExecutable(string? command)
    {
        string s = (command ?? "").Trim();
        if (s.Length == 0) return "";
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end].Trim() : "";
        }
        int sp = s.IndexOf(' ');
        return (sp > 0 ? s[..sp] : s).Trim();
    }

    // ---- 通用父目录 / 系统目录：不能把整棵树算成某一个软件的占用 ----

    /// <summary>系统盘上的通用父目录、用户根目录、临时目录：它们不是「某个软件的安装目录」。</summary>
    private static readonly Lazy<string[]> GenericRoots = new(() =>
    {
        var roots = new List<string>();
        void Add(Environment.SpecialFolder f)
        {
            try
            {
                string p = Environment.GetFolderPath(f);
                if (!string.IsNullOrWhiteSpace(p)) roots.Add(p);
            }
            catch { /* 解析不出来就不加，不猜 */ }
        }
        Add(Environment.SpecialFolder.Windows);
        Add(Environment.SpecialFolder.System);
        Add(Environment.SpecialFolder.SystemX86);
        Add(Environment.SpecialFolder.ProgramFiles);
        Add(Environment.SpecialFolder.ProgramFilesX86);
        Add(Environment.SpecialFolder.CommonApplicationData);
        Add(Environment.SpecialFolder.UserProfile);
        Add(Environment.SpecialFolder.LocalApplicationData);
        Add(Environment.SpecialFolder.ApplicationData);
        try
        {
            string temp = Path.GetTempPath();
            if (!string.IsNullOrWhiteSpace(temp)) roots.Add(temp);
        }
        catch { }
        return roots.Select(Normalize).Where(x => x.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    });

    /// <summary>
    /// 这个安装位置能不能把整棵树的占用算给这一个软件。
    /// 不能的情况分三类，界面必须分别说清楚（见 <see cref="FootprintSkip"/>）。
    /// </summary>
    internal static bool CanAttributeLocation(string location, out string why)
    {
        why = "";
        if (location.Length == 0) { why = Loc.AppFootprintWhyMissing; return false; }
        if (IsVolumeRoot(location)) { why = Loc.AppFootprintWhyGenericRoot; return false; }
        // Windows 目录之内：那是操作系统的树，不是软件自己的目录。
        string win = WindowsDirectory;
        if (win.Length > 0 && IsPathWithin(location, win))
        {
            why = Loc.AppFootprintWhySystemDir;
            return false;
        }
        // 正好等于一个通用父目录（Program Files / ProgramData / 用户目录 / Temp …）
        if (GenericRoots.Value.Any(root => location.Equals(root, StringComparison.OrdinalIgnoreCase)))
        {
            why = Loc.AppFootprintWhyGenericRoot;
            return false;
        }
        return true;
    }

    /// <summary>路径规范化：去引号、统一分隔符、去尾反斜杠。空串表示不可用。</summary>
    internal static string NormalizePath(string? path)
        => (path ?? "").Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');

    static bool AiConfigured()
    {
        var p = App.Settings.CurrentProvider();
        return p != null && !string.IsNullOrWhiteSpace(p.BaseUrl)
            && !string.IsNullOrWhiteSpace(App.Settings.AiModel);
    }

    static ProcessSnapshot GetProcessPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool complete = true;
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    string? filename = process.MainModule?.FileName;
                    string path = Normalize(filename);
                    if (path.Length > 0) paths.Add(path);
                    else complete = false;
                }
                catch { complete = false; }
                finally { process.Dispose(); }
            }
        }
        catch { complete = false; }
        return new ProcessSnapshot(paths, complete);
    }

    static int LowerBound(List<FileUsage> values, string target)
    {
        int low = 0, high = values.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (string.Compare(values[mid].Path, target, StringComparison.OrdinalIgnoreCase) < 0)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }

    static string Normalize(string? path) => NormalizePath(path);

    /// <summary>占用拆分的三类。用户最关心的是「不卸软件也能删的是哪块」。</summary>
    internal enum FootprintKind { InstallDir, UserData, Cache }

    /// <summary>缓存目录特征：删了会自动重建，且不影响已装软件能不能用。</summary>
    private static readonly string[] CacheBits =
    {
        @"\cache\", @"\caches\", @"\temp\", @"\tmp\", @"\logs\", @"\log\",
        @"\crashdumps\", @"\shadercache\", @"\gpucache\", @"\code cache\",
        @"\softwaredistribution\download\", @"\inetcache\",
    };

    /// <summary>用户数据目录特征：卸载时通常要问用户留不留。</summary>
    private static readonly string[] UserDataBits =
    {
        @"\appdata\", @"\documents\", @"\saved games\", @"\saves\", @"\profiles\",
        @"\userdata\", @"\user data\", @"\my games\",
    };

    /// <summary>
    /// 把安装目录里的一个文件归到「缓存 / 保存的数据 / 程序本体」三档。
    /// 判定顺序：先看缓存（cache/temp/logs），再看用户数据特征目录，剩下的算程序本体。
    ///
    /// 注意范围：只看**安装目录这棵树**。装在 AppData 里的用户数据不在这个统计内
    /// （那需要按软件名去用户目录里找，容易算错，宁可不算）。
    /// </summary>
    internal static FootprintKind ClassifyFootprint(string path, string lowerInstallLocation = "")
    {
        string p = (path ?? "").ToLowerInvariant().Replace('/', '\\');
        if (CacheBits.Any(p.Contains)) return FootprintKind.Cache;
        if (UserDataBits.Any(p.Contains)) return FootprintKind.UserData;
        return FootprintKind.InstallDir;
    }

    static HashSet<AppUninstallItem> FindOverlappingApps(IEnumerable<AppLocation> locations)
    {
        var usable = locations
            .Where(x => IsUsableInstallLocation(x.Location))
            .OrderBy(x => x.Location, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var overlap = new HashSet<AppUninstallItem>();
        for (int i = 0; i < usable.Count; i++)
        {
            for (int j = i + 1; j < usable.Count; j++)
            {
                if (!LocationsOverlap(usable[i].Location, usable[j].Location)) continue;
                overlap.Add(usable[i].App);
                overlap.Add(usable[j].App);
            }
        }
        return overlap;
    }

    static bool IsUsableInstallLocation(string location)
        => location.Length > 0
            && Path.IsPathFullyQualified(location)
            && !IsVolumeRoot(location);

    static bool IsVolumeRoot(string location)
    {
        string? root = Path.GetPathRoot(location);
        return !string.IsNullOrWhiteSpace(root)
            && location.Equals(Normalize(root), StringComparison.OrdinalIgnoreCase);
    }

    static bool LocationsOverlap(string left, string right)
        => IsPathWithin(left, right) || IsPathWithin(right, left);

    static bool IsPathWithin(string path, string location)
        => path.Equals(location, StringComparison.OrdinalIgnoreCase)
            || (path.Length > location.Length
                && path.StartsWith(location, StringComparison.OrdinalIgnoreCase)
                && path[location.Length] == '\\');

    static AppRunningState GetRunningState(
        string location,
        bool canInspectLocation,
        ProcessSnapshot processes)
    {
        if (!canInspectLocation || !processes.IsComplete)
            return AppRunningState.Unknown;
        return processes.Paths.Any(path => IsPathWithin(path, location))
            ? AppRunningState.Running
            : AppRunningState.NotRunning;
    }

    static string AppendWarning(string existing, string warning)
    {
        if (string.IsNullOrWhiteSpace(existing)) return warning;
        return existing.Contains(warning, StringComparison.CurrentCultureIgnoreCase)
            ? existing
            : existing + "；" + warning;
    }

    static void Apply(AppUninstallItem app, AppRecommendation result, bool fromAi, bool clearSelection)
    {
        app.Recommendation = result.Decision;
        app.RecommendationConfidence = result.Confidence;
        app.RecommendationReason = result.Reason;
        app.RecommendationWarning = result.DataWarning;
        app.AiSuggested = fromAi;
        if (clearSelection) app.Selected = false;
    }

    static AppRecommendation Recommend(double confidence, string reason, string warning = "")
        => new() { Decision = AppRecommendationDecision.Recommend, Confidence = confidence, Reason = reason, DataWarning = warning };

    static AppRecommendation Consider(double confidence, string reason, string warning = "")
        => new() { Decision = AppRecommendationDecision.Consider, Confidence = confidence, Reason = reason, DataWarning = warning };

    static AppRecommendation Keep(double confidence, string reason, string warning = "")
        => new() { Decision = AppRecommendationDecision.Keep, Confidence = confidence, Reason = reason, DataWarning = warning };

    static List<AppRecommendation> Parse(string text)
    {
        string json = ExtractJson(text);
        if (json.Length == 0) return new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement items = doc.RootElement;
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("items", out var listed))
                items = listed;
            if (items.ValueKind != JsonValueKind.Array) return new();

            var result = new List<AppRecommendation>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string decision = GetString(item, "decision").ToLowerInvariant();
                string appId = GetString(item, "appId");
                if (string.IsNullOrWhiteSpace(appId) || !seen.Add(appId)) continue;
                var parsed = decision switch
                {
                    "recommend" or "uninstall" or "remove" => AppRecommendationDecision.Recommend,
                    "keep" or "retain" => AppRecommendationDecision.Keep,
                    _ => AppRecommendationDecision.Consider,
                };
                result.Add(new AppRecommendation
                {
                    AppId = appId,
                    Decision = parsed,
                    Confidence = GetDouble(item, "confidence"),
                    Reason = LimitText(GetString(item, "reason")),
                    DataWarning = LimitText(GetString(item, "dataWarning")),
                });
            }
            return result;
        }
        catch
        {
            return new();
        }
    }

    static string ExtractJson(string text)
    {
        string value = (text ?? "").Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            int first = value.IndexOf('\n');
            int last = value.LastIndexOf("```", StringComparison.Ordinal);
            if (first >= 0 && last > first) value = value[(first + 1)..last].Trim();
        }
        int start = value.IndexOfAny(['{', '[']);
        if (start < 0) return "";
        char close = value[start] == '{' ? '}' : ']';
        int end = value.LastIndexOf(close);
        return start >= 0 && end > start ? value[start..(end + 1)] : "";
    }

    static string GetString(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    static double GetDouble(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : 0;

    static string LimitText(string value)
    {
        string normalized = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 180 ? normalized : normalized[..177] + "...";
    }

    private readonly record struct FileUsage(string Path, long Bytes);
    private readonly record struct AppLocation(AppUninstallItem App, string Location);
    private readonly record struct ProcessSnapshot(HashSet<string> Paths, bool IsComplete);
    private readonly record struct RemoteCandidate(AppUninstallItem App, string RemoteId);
}

/// <summary>
/// 一个软件占用的拆分。<see cref="InstallDirBytes"/> 是安装目录，
/// <see cref="UserDataBytes"/> 是用户配置/数据，<see cref="CacheBytes"/> 是缓存（删了会自动重建，
/// 也就是「不卸载软件也能先拿回来」的那部分）。
/// </summary>
public readonly record struct AppUsage(
    AppUninstallItem App,
    long ActualSizeBytes,
    bool HasMeasuredSize,
    AppRunningState RunningState,
    long InstallDirBytes = 0,
    long UserDataBytes = 0,
    long CacheBytes = 0,
    string FootprintNote = "")
{
    /// <summary>估计可释放：缓存部分（不动软件、不动用户数据）。</summary>
    public long ReclaimableBytes => CacheBytes;
}
