using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 清理前检查页要显示的数字与结论。
///
/// 抽成纯函数是为了**可测**：这些数字决定用户看到「将处理多少」，
/// 一旦算错（例如把非可删项也算进去、把位置数当分类数），
/// 用户会在一个错误的范围认知下点「确认清理」。
/// </summary>
public sealed record CleanPreflightFacts(
    int Locations,
    int Items,
    long Bytes,
    int NeedsConfirm)
{
    public string Size => FileEntry.FormatSize(Bytes);
    public bool HasNothing => Items == 0;
}

public static class CleanPreflight
{
    /// <summary>
    /// 按**将要真正处理的集合**算：只统计 CanDelete 且有路径的项。
    /// 位置数按稳定键去重（风险拆组不重复计同一实际位置）。
    /// 「需要你确认」= 风险不是 Safe 的那些，必须如实报出来。
    /// </summary>
    public static CleanPreflightFacts Build(
        IEnumerable<CleanItem> picked,
        CleanLayeredResult? layered)
    {
        var items = picked
            .Where(x => x.CanDelete && !string.IsNullOrEmpty(x.FullPath))
            .ToList();

        long bytes = 0;
        foreach (var x in items) bytes += Math.Max(0, x.Size);

        int needsConfirm = items.Count(x => x.Risk != CleanRisk.Safe);

        // 位置数：从分层结果里找每个已选项落在哪个位置，按稳定键去重
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (layered != null)
        {
            var set = new HashSet<CleanItem>(items);
            foreach (var p in layered.Purposes)
                foreach (var loc in p.Locations)
                    if (loc.Items.Any(set.Contains)) keys.Add(loc.Key);
        }

        return new CleanPreflightFacts(keys.Count, items.Count, bytes, needsConfirm);
    }
}
