using System.Globalization;
using System.Text.Json;

namespace AiDiskCleaner.Services.Agent;

/// <summary>get_overview：全盘概览。一次调用回答「这台盘都装了什么、哪里最大」。</summary>
public sealed class GetOverviewTool : IAgentTool
{
    public string Name => "get_overview";

    public string Description => "全盘概览：总量、Top 目录、分类、扫描质量";

    public ToolResult Execute(ToolContext context, JsonElement args)
    {
        ToolArgs.RejectUnknownKeys(args, "scanId");
        context.RequireScanId(args);

        var root = context.Session.Root;
        int folderChildren = 0;
        var folders = new List<AiDiskCleaner.Models.FileEntry>();
        foreach (var child in root.ChildList)
        {
            if (!child.IsDirectory || child.IsFilesGroup) continue;
            folderChildren++;
            folders.Add(child);
        }

        var topFolders = folders
            .OrderByDescending(f => f.Size)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .Take(15)
            .Select(f => new TopFolderDto(
                FolderId: context.Index.GetFolderId(f) ?? "",
                RelPath: EvidenceCardBuilder.Redact(context.Index.GetRelPath(f)),
                SizeBytes: f.Size,
                FileCount: f.FileCount))
            .ToList();

        var categories = context.Session.CategoryTotals
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(10)
            .Select(kv => new CategoryDto(kv.Key, kv.Value))
            .ToList();

        var quality = context.Session.Quality is null
            ? new OverviewQualityDto(true, 0, 0)
            : new OverviewQualityDto(
                context.Session.Quality.Complete,
                context.Session.Quality.UnparsedRecords,
                context.Session.Quality.OrphanRecords);

        var dto = new OverviewDto(
            Drive: context.Session.Drive,
            TotalBytes: root.Size,
            FreeBytes: context.Session.FreeBytes,
            FileCount: root.FileCount,
            FolderCount: root.FolderCount,
            ScannedAt: context.Session.ScannedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            Quality: quality,
            TopFolders: topFolders,
            Categories: categories);

        return ToolResult.Success(Name, context.Session.ScanId, dto, truncated: folderChildren > 15);
    }
}
