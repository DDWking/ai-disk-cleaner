using System.Globalization;
using System.Text.RegularExpressions;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.Agent;

/// <summary>扩展名 × 数量 × 字节（证据卡里的 typeMix 项）。</summary>
public sealed record TypeMixEntry(string Ext, int Count, long Bytes);

/// <summary>隐藏/系统/重解析点。重解析点单独列出，避免模型误以为普通目录。</summary>
public sealed record CardAttrs(bool Hidden, bool System, bool Reparse);

/// <summary>本地规则命中（P0 先留结构，P1 接 CleanReport 后填）。</summary>
public sealed record CardRuleRef(string Id, double Confidence);

/// <summary>
/// 证据卡：唯一允许出站的目录事实。
///
/// 字段故意做少：够判断，不够泄露。同一 scanId + folderId + cardVersion
/// 必须字节级相同（用会话里的 ScannedAt 算 ageDays，不读当前时间）。
/// </summary>
public sealed record EvidenceCard(
    string FolderId,
    string? RelPath,
    int Depth,
    long SizeBytes,
    long AllocatedBytes,
    int FileCount,
    int FolderCount,
    int DirectFolderCount,
    string Modified,
    int AgeDays,
    IReadOnlyList<TypeMixEntry> TypeMix,
    IReadOnlyList<string> Samples,
    IReadOnlyList<string> SampleDirs,
    string ProductHint,
    CardAttrs Attrs,
    IReadOnlyList<CardRuleRef> Rules,
    bool Truncated,
    string CardVersion,
    string? FullPath = null)
{
    /// <summary>本地软件签名 key（AppSignatures.Classify 的 Key），用于校验 app: 证据引用。</summary>
    public string? AppKey { get; init; }
}

public enum CardPrivacyMode
{
    /// <summary>默认：只出相对路径，用户名替换为 %USER%。</summary>
    Relative,

    /// <summary>出完整路径（需用户显式开启）。</summary>
    Full,

    /// <summary>不出路径。</summary>
    Off,
}

public sealed class EvidenceCardOptions
{
    public CardPrivacyMode Privacy { get; init; } = CardPrivacyMode.Relative;

    public string CardVersion { get; init; } = "card-v1";

    /// <summary>可选规则来源（P1 注入 CleanReport 的命中；P0 为空）。</summary>
    public Func<FileEntry, IReadOnlyList<CardRuleRef>>? Rules { get; init; }
}

/// <summary>证据卡构建器：纯函数，只读树，不碰文件系统。</summary>
public static class EvidenceCardBuilder
{
    public const int MaxTypeMix = 8;
    public const int MaxSamples = 6;
    public const int MaxSampleDirs = 5;

    private static readonly Regex UserSegment = new(
        @"(^|\\)Users\\([^\\]+)(?=\\|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static EvidenceCard Build(ScanSession session, FolderIndex index, FileEntry dir, EvidenceCardOptions? options = null)
    {
        options ??= new EvidenceCardOptions();

        var files = new List<FileEntry>();
        var dirs = new List<FileEntry>();
        foreach (var child in dir.ChildList)
        {
            if (child.IsDirectory)
            {
                if (!child.IsFilesGroup) dirs.Add(child);
            }
            else
            {
                files.Add(child);
            }
        }

        var typeMix = BuildTypeMix(files);
        var samples = files
            .OrderByDescending(f => f.Size)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .Take(MaxSamples)
            .Select(f => f.Name)
            .ToList();

        var sampleDirs = dirs
            .OrderByDescending(d => d.Size)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .Take(MaxSampleDirs)
            .Select(d => d.Name)
            .ToList();

        string relPath = index.GetRelPath(dir);
        string? outboundRel = options.Privacy switch
        {
            CardPrivacyMode.Off => null,
            CardPrivacyMode.Full => relPath,
            _ => Redact(relPath),
        };

        int ageDays = AgeDays(session, dir.Modified);

        return new EvidenceCard(
            FolderId: index.GetFolderId(dir) ?? FolderIndex.MakeFolderId(relPath),
            RelPath: outboundRel,
            Depth: index.GetDepth(dir),
            SizeBytes: dir.Size,
            AllocatedBytes: dir.Allocated,
            FileCount: dir.FileCount,
            FolderCount: dir.FolderCount,
            DirectFolderCount: dirs.Count,
            Modified: FormatModified(dir.Modified),
            AgeDays: ageDays,
            TypeMix: typeMix,
            Samples: samples,
            SampleDirs: sampleDirs,
            ProductHint: AppSignatures.FriendlyName(dir.FullPath) ?? "",
            Attrs: new CardAttrs(dir.IsHidden, dir.IsSystem, dir.IsReparsePoint),
            Rules: options.Rules?.Invoke(dir) ?? Array.Empty<CardRuleRef>(),
            Truncated: files.Count > MaxSamples || dirs.Count > MaxSampleDirs || CountExtensions(files) > MaxTypeMix,
            CardVersion: options.CardVersion,
            FullPath: options.Privacy == CardPrivacyMode.Full ? dir.FullPath : null)
        {
            AppKey = AppSignatures.Classify(dir.FullPath)?.Key,
        };
    }

    /// <summary>把用户目录名换掉：C:\Users\Alice\x → C:\Users\%USER%\x。</summary>
    public static string Redact(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        string redacted = UserSegment.Replace(path, m => m.Groups[1].Value + @"Users\%USER%");

        string machine = Environment.MachineName;
        if (!string.IsNullOrEmpty(machine))
        {
            redacted = redacted.Replace(@"\" + machine + @"\", @"\%PC%\", StringComparison.OrdinalIgnoreCase);
        }

        return redacted;
    }

    public static int AgeDays(ScanSession session, DateTime modified)
    {
        double days = (session.ScannedAt - new DateTimeOffset(modified)).TotalDays;
        if (days < 0) return 0;
        return days > 36500 ? 36500 : (int)days;
    }

    public static string FormatModified(DateTime modified)
        => new DateTimeOffset(modified).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static List<TypeMixEntry> BuildTypeMix(List<FileEntry> files)
    {
        var stats = new Dictionary<string, (int Count, long Bytes)>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            string ext = ExtensionOf(file.Name);
            stats.TryGetValue(ext, out var item);
            stats[ext] = (item.Count + 1, item.Bytes + file.Size);
        }

        return stats
            .OrderByDescending(kv => kv.Value.Count)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(MaxTypeMix)
            .Select(kv => new TypeMixEntry(kv.Key, kv.Value.Count, kv.Value.Bytes))
            .ToList();
    }

    private static int CountExtensions(List<FileEntry> files)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files) set.Add(ExtensionOf(file.Name));
        return set.Count;
    }

    private static string ExtensionOf(string name)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return "noext";
        return name[(dot + 1)..].ToLowerInvariant();
    }
}
