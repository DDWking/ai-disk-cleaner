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

        return byFolder.Values.ToList();
    }

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
