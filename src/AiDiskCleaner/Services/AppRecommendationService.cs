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

    public static List<AppUsage> CalculateUsage(
        IEnumerable<AppUninstallItem> apps,
        IEnumerable<FileEntry> files)
    {
        var indexed = files
            .Select(x => new FileUsage(Normalize(x.FullPath), Math.Max(0, x.Allocated > 0 ? x.Allocated : x.Size)))
            .Where(x => x.Path.Length > 0)
            .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var locations = apps
            .Select(app => new AppLocation(app, Normalize(app.InstallLocation)))
            .ToList();
        var overlappingApps = FindOverlappingApps(locations);
        var processes = GetProcessPaths();
        var usage = new List<AppUsage>();
        foreach (var item in locations)
        {
            var app = item.App;
            long actual = 0;
            bool canMeasureUsage = IsUsableInstallLocation(item.Location)
                && !overlappingApps.Contains(app);
            if (canMeasureUsage)
            {
                int start = LowerBound(indexed, item.Location);
                for (int i = start; i < indexed.Count; i++)
                {
                    string path = indexed[i].Path;
                    if (!path.StartsWith(item.Location, StringComparison.OrdinalIgnoreCase)) break;
                    if (IsPathWithin(path, item.Location))
                    {
                        actual += indexed[i].Bytes;
                        continue;
                    }
                    break;
                }
            }
            var runningState = GetRunningState(
                item.Location,
                IsUsableInstallLocation(item.Location),
                processes);
            usage.Add(new AppUsage(
                app,
                actual > 0 ? actual : app.SizeBytes,
                actual > 0,
                runningState));
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
        }
    }

    public static AppRecommendation LocalRule(AppUninstallItem app)
    {
        if (app.IsProtected || app.SystemComponent || app.GroupKey == 2)
            return Keep(1, Loc.AppKeepSystem);
        if (!app.CanUninstall)
            return Keep(1, Loc.AppKeepNoUninstaller);
        if (IsSafetyCritical(app))
            return Keep(0.9, Loc.AppKeepCritical);
        if (app.RunningState == AppRunningState.Running)
            return Consider(0.95, Loc.AppConsiderRunning, Loc.AppRunningWarning);
        if (app.RunningState == AppRunningState.Unknown)
            return Consider(0.9, Loc.AppConsiderRunningUnknown, Loc.AppRunningUnknownWarning);
        if (IsKnownBloat(app))
            return Recommend(0.86, Loc.AppRecommendBloat);
        if (app.ActualSizeBytes >= 5L * 1024 * 1024 * 1024)
            return Consider(0.72, Loc.AppConsiderLarge);
        if (app.HasStartup)
            return Consider(0.66, Loc.AppConsiderStartup);
        return Consider(0.55, Loc.AppConsiderUnknown);
    }

    static AppRecommendation EnforceSafety(AppUninstallItem app, AppRecommendation result)
    {
        var local = LocalRule(app);
        if (app.IsProtected || app.SystemComponent || app.GroupKey == 2 || !app.CanUninstall || IsSafetyCritical(app))
            return local;
        if (app.RunningState != AppRunningState.NotRunning
            && result.Decision == AppRecommendationDecision.Recommend)
        {
            result.Decision = AppRecommendationDecision.Consider;
            string warning = app.RunningState == AppRunningState.Running
                ? Loc.AppRunningWarning
                : Loc.AppRunningUnknownWarning;
            result.DataWarning = AppendWarning(result.DataWarning, warning);
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

    static bool IsSafetyCritical(AppUninstallItem app)
    {
        string text = string.Join(" ", app.Name, app.Publisher, app.InstallLocation);
        return SafetyCriticalNames.Any(x => text.Contains(x, StringComparison.CurrentCultureIgnoreCase));
    }

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

    static string Normalize(string? path)
        => (path ?? "").Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');

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

public readonly record struct AppUsage(
    AppUninstallItem App,
    long ActualSizeBytes,
    bool HasMeasuredSize,
    AppRunningState RunningState);
