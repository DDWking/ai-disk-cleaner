using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 软件内虚拟分类结果的本地持久化缓存（按盘符独立存储）：
/// 仅记录「哪个路径属于哪个颜色分组、AI 给的理由、哪些分组处于折叠态」，绝不触碰任何真实文件。
/// 下次打开软件或切盘时自动恢复上次分好的彩色视图，零额外 AI 费用；点「AI 分析 / 重分析」可随时刷新。
/// </summary>
public static class VirtualClassifyCache
{
    public sealed class CachedClassifyEntry
    {
        [JsonPropertyName("c")] public string CategoryId { get; set; } = ClassifyCategoryRule.KeepId;
        [JsonPropertyName("r")] public string AiReason { get; set; } = "";
        [JsonPropertyName("a")] public bool FromAi { get; set; }
    }

    public sealed class VirtualClassifySnapshot
    {
        public int Version { get; set; } = 1;
        public string Drive { get; set; } = "";
        public DateTime SavedAtUtc { get; set; }
        public bool HasAiResults { get; set; }
        public List<ClassifyCategoryRule> Categories { get; set; } = new();
        public List<string> CollapsedGroups { get; set; } = new();
        public Dictionary<string, CachedClassifyEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string CachePath(string drive, string? directoryOverride = null)
    {
        string letter = new string((drive ?? "C").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (letter.Length == 0) letter = "DISK";
        string dir = string.IsNullOrWhiteSpace(directoryOverride) ? AppPaths.ConfigDirectory : directoryOverride;
        return Path.Combine(dir, $"virtual-classify-{letter}.json");
    }

    public static VirtualClassifySnapshot? TryLoad(string drive, string? directoryOverride = null)
    {
        try
        {
            string path = CachePath(drive, directoryOverride);
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<VirtualClassifySnapshot>(json);
            if (dto == null || dto.Entries == null || dto.Entries.Count == 0)
                return null;
            // 反序列化后重建忽略大小写键字典
            var ci = new Dictionary<string, CachedClassifyEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in dto.Entries)
            {
                string norm = CleanListSnapshot.NormPath(kv.Key);
                if (norm.Length > 0 && kv.Value != null)
                    ci[norm] = kv.Value;
            }
            dto.Entries = ci;
            return dto;
        }
        catch (Exception ex)
        {
            AppLog.Record("Classify", ex, "load virtual classify cache");
            return null;
        }
    }

    public static void Save(
        string drive,
        IReadOnlyList<ClassifyItem> items,
        IReadOnlyList<ClassifyCategoryRule> categories,
        IEnumerable<string> collapsedGroups,
        bool hasAiResults,
        string? directoryOverride = null)
    {
        if (string.IsNullOrWhiteSpace(drive) || items == null || items.Count == 0) return;

        try
        {
            var snap = new VirtualClassifySnapshot
            {
                Version = 1,
                Drive = drive.Trim().TrimEnd('\\') + "\\",
                SavedAtUtc = DateTime.UtcNow,
                HasAiResults = hasAiResults,
                Categories = categories.Select(c => c.Clone()).ToList(),
                CollapsedGroups = collapsedGroups.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            };

            foreach (var item in items)
            {
                string norm = CleanListSnapshot.NormPath(item.SourcePath);
                if (norm.Length == 0) continue;
                snap.Entries[norm] = new CachedClassifyEntry
                {
                    CategoryId = item.CategoryId,
                    AiReason = item.AiReason ?? "",
                    FromAi = item.FromAi,
                };
            }

            if (snap.Entries.Count == 0) return;

            string path = CachePath(drive, directoryOverride);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snap, JsonOpts));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Record("Classify", ex, "save virtual classify cache");
        }
    }

    public static void UpdateCollapsedGroups(
        string drive,
        IEnumerable<string> collapsedGroups,
        string? directoryOverride = null)
    {
        try
        {
            var existing = TryLoad(drive, directoryOverride);
            if (existing == null) return;
            existing.CollapsedGroups = collapsedGroups.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string path = CachePath(drive, directoryOverride);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(existing, JsonOpts));
            File.Move(tmp, path, overwrite: true);
        }
        catch { }
    }
}
