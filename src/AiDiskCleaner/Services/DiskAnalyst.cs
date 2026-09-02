using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

public static class DiskAnalyst
{
    public static string SystemPrompt()
    {
        string extra = (App.Settings.AiExtraPrompt ?? "").Trim();
        return string.IsNullOrEmpty(extra) ? Loc.AiAnalystSystem : Loc.AiAnalystSystem + "\n\n" + extra;
    }

    public static string ScanSummary(FileEntry root, CleanReport report, long used, long total)
    {
        var lines = new List<string>
        {
            Loc.AiScanHeader,
            $"volume: {root.FullPath}  used {FileEntry.FormatSize(used)} / {FileEntry.FormatSize(total)}",
            $"tree: {root.FileCount:N0} files, {root.FolderCount:N0} folders, {FileEntry.FormatSize(root.Size)}",
            $"cleanable: {report.Cleanable.Count:N0} items, {FileEntry.FormatSize(report.CleanableBytes)}",
        };
        foreach (var g in report.Cleanable.GroupBy(x => string.IsNullOrEmpty(x.Group) ? "-" : x.Group)
                     .OrderByDescending(x => x.Sum(i => i.Size)))
            lines.Add($"  {g.Key}: {g.Count():N0}, {FileEntry.FormatSize(g.Sum(i => i.Size))}");
        lines.Add($"large: {report.LargeFiles.Count:N0}, {Sum(report.LargeFiles)}");
        lines.Add($"old: {report.OldFiles.Count:N0}, {Sum(report.OldFiles)}");
        lines.Add($"duplicates: {report.Duplicates.Count:N0} in {report.DupGroupCount:N0} groups, {Sum(report.Duplicates)}");
        lines.Add($"empty folders: {report.EmptyFolders.Count:N0}");
        lines.Add($"broken shortcuts: {report.BrokenShortcuts.Count:N0}");
        lines.Add($"long paths: {report.LongPaths.Count:N0}");
        if (!string.IsNullOrWhiteSpace(report.CompareNote))
            lines.Add("compare: " + report.CompareNote);
        return string.Join(Environment.NewLine, lines);
    }

    static string Sum(List<CleanItem> items)
        => FileEntry.FormatSize(items.Sum(x => x.Size));
}
