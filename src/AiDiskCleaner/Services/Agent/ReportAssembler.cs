namespace AiDiskCleaner.Services.Agent;

/// <summary>
/// 把已通过校验的条目组装成 UI 可直接渲染的报告卡：
/// 去重 → 阈值降级 → 总量校正 → 分组 → 排序。
///
/// 这里再强调一次：组装过程只调整展示与统计，不会新增任何 folderId 或证据。
/// </summary>
public static class ReportAssembler
{
    /// <summary>低于这个置信度的 recommend 自动降级为 consider（03 §6.3 第 6 步）。</summary>
    public const double RecommendConfidenceFloor = 0.55;

    public static AgentReport Build(
        ScanSession session,
        IReadOnlyList<ReportItem> items,
        string summary,
        IReadOnlyList<ReportQuestion> questions,
        string model,
        string promptVersion,
        DateTimeOffset generatedAt,
        int dropped,
        IReadOnlyList<string> warnings,
        string? degraded)
    {
        var report = new AgentReport
        {
            ScanId = session.ScanId,
            Drive = session.Drive,
            GeneratedAt = generatedAt,
            Model = model,
            PromptVersion = promptVersion,
            DroppedItems = dropped,
            Degraded = degraded,
        };
        report.Warnings.AddRange(warnings);
        report.Questions.AddRange(questions);

        var deduped = Deduplicate(items);
        foreach (var item in deduped)
        {
            if (item.Verdict == ReportVerdict.Recommend && item.Confidence < RecommendConfidenceFloor)
                item.Verdict = ReportVerdict.Consider;
        }

        long total = deduped.Sum(i => i.ReclaimableBytes);
        if (session.FreeBytes > 0 && total > session.FreeBytes && total > 0)
        {
            double scale = (double)session.FreeBytes / total;
            foreach (var item in deduped)
                item.ReclaimableBytes = (long)(item.ReclaimableBytes * scale);
            total = deduped.Sum(i => i.ReclaimableBytes);
            report.Degraded ??= "total_clamped";
            report.Warnings.Add("可回收总量超过磁盘剩余空间，已按比例下调");
        }

        report.TotalReclaimableBytes = total;
        report.Summary = string.IsNullOrWhiteSpace(summary)
            ? LocalSummary(deduped.Count, total)
            : summary;

        AddGroup(report, "推荐清理", new[] { ReportVerdict.Recommend }, deduped);
        AddGroup(report, "再看看", new[] { ReportVerdict.Consider, ReportVerdict.Unknown }, deduped);
        AddGroup(report, "建议保留", new[] { ReportVerdict.Keep }, deduped);

        report.ReportId = "r_" + session.ScanId + "_" + deduped.Count + "_" + total;
        return report;
    }

    static List<ReportItem> Deduplicate(IReadOnlyList<ReportItem> items)
    {
        var byFolder = new Dictionary<string, ReportItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!byFolder.TryGetValue(item.FolderId, out var existing))
            {
                byFolder[item.FolderId] = item;
                continue;
            }

            if (item.Confidence > existing.Confidence)
                byFolder[item.FolderId] = item;
        }

        return CollapseOverlappingPaths(byFolder.Values.ToList());
    }

    private static readonly HashSet<string> ContainerSegmentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "steamlibrary",
        "steamapps",
        "common",
        "appdata",
        "local",
        "roaming",
        "locallow",
        "program files",
        "program files (x86)",
        "programdata",
        "users",
        "%user%",
    };

    /// <summary>
    /// 折叠重复的父子层级（例如 eSupport -> eDriver -> Software 或 SteamLibrary -> steamapps -> The Forest），
    /// 避免同一份磁盘占用或泛化容器目录在建议列表里反复刷屏。
    /// </summary>
    static List<ReportItem> CollapseOverlappingPaths(List<ReportItem> items)
    {
        if (items.Count <= 1) return items;

        // 按路径层级从浅到深排序（稳定次序保证确定性）。
        var ordered = items
            .OrderBy(i => PathDepth(i.RelPath))
            .ThenBy(i => NormalizeRelPath(i.RelPath), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pass 1：收敛单线包装目录（非多软件容器，且同结论的子目录占据父目录 80%+ 空间）-> 保留最外层代表目录，去掉下层套娃项。
        foreach (var ancestor in ordered)
        {
            if (dropped.Contains(ancestor.FolderId)) continue;
            string ancestorNorm = NormalizeRelPath(ancestor.RelPath);
            if (ancestorNorm.Length == 0 || IsLibraryContainer(ancestor)) continue;

            var descendants = ordered
                .Where(d => !dropped.Contains(d.FolderId) && IsStrictDescendant(ancestorNorm, NormalizeRelPath(d.RelPath)))
                .ToList();
            if (descendants.Count == 0) continue;

            long ancestorSize = EffectiveSize(ancestor);
            long maxDescendantSize = descendants.Max(EffectiveSize);
            bool sameVerdict = descendants.All(d => d.Verdict == ancestor.Verdict);
            if (sameVerdict && ancestorSize > 0 && maxDescendantSize >= (long)(ancestorSize * 0.8))
            {
                foreach (var desc in descendants)
                    dropped.Add(desc.FolderId);
            }
        }

        // Pass 2：若外层容器/根目录之下已有具体的可处理子目录，去掉外层重复概括卡，保留具体子项。
        var remaining = ordered.Where(i => !dropped.Contains(i.FolderId)).ToList();
        foreach (var ancestor in remaining)
        {
            string ancestorNorm = NormalizeRelPath(ancestor.RelPath);
            if (ancestorNorm.Length == 0) continue;

            bool hasSpecificActionableChild = remaining.Any(d =>
                d.FolderId != ancestor.FolderId
                && d.Verdict != ReportVerdict.Keep
                && IsStrictDescendant(ancestorNorm, NormalizeRelPath(d.RelPath)));
            if (hasSpecificActionableChild)
                dropped.Add(ancestor.FolderId);
        }

        return ordered.Where(i => !dropped.Contains(i.FolderId)).ToList();
    }

    static bool IsLibraryContainer(ReportItem item)
    {
        if (item.Verdict == ReportVerdict.Keep) return true;
        string norm = NormalizeRelPath(item.RelPath);
        int slash = norm.LastIndexOf('\\');
        string last = slash >= 0 ? norm[(slash + 1)..] : norm;
        return ContainerSegmentNames.Contains(last);
    }

    static bool IsStrictDescendant(string ancestorNorm, string candidateNorm)
        => candidateNorm.Length > ancestorNorm.Length + 1
           && candidateNorm.StartsWith(ancestorNorm + "\\", StringComparison.OrdinalIgnoreCase);

    static string NormalizeRelPath(string? relPath)
        => (relPath ?? "").Replace('/', '\\').Trim().Trim('\\');

    static int PathDepth(string? relPath)
    {
        string norm = NormalizeRelPath(relPath);
        if (norm.Length == 0) return 0;
        int depth = 1;
        foreach (char c in norm)
            if (c == '\\') depth++;
        return depth;
    }

    static long EffectiveSize(ReportItem item)
        => item.SizeBytes > 0 ? item.SizeBytes : item.ReclaimableBytes;

    static void AddGroup(AgentReport report, string name, ReportVerdict[] verdicts, List<ReportItem> items)
    {
        var selected = items
            .Where(i => verdicts.Contains(i.Verdict))
            .OrderByDescending(i => i.ReclaimableBytes)
            .ThenByDescending(i => i.Confidence)
            .ThenBy(i => i.RelPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selected.Count == 0) return;

        report.Groups.Add(new ReportGroup
        {
            Group = name,
            TotalBytes = selected.Sum(i => i.ReclaimableBytes),
            Items = selected,
        });
    }

    static string LocalSummary(int count, long total)
        => count == 0
            ? "这次扫描没有发现需要清理的目录。"
            : $"发现 {count} 个值得关注的目录，预计可回收 {ReportParser.FormatSize(total)}。";
}
