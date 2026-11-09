using System.Globalization;
using System.Text.Json;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.Agent;

/// <summary>list_folders：按父目录列子目录，支持排序、阈值与分页。</summary>
public sealed class ListFoldersTool : IAgentTool
{
    private static readonly string[] Sorts = { "size", "age", "name", "fileCount" };

    public string Name => "list_folders";

    public string Description => "列目录（分页、排序、按大小过滤）";

    public ToolResult Execute(ToolContext context, JsonElement args)
    {
        ToolArgs.RejectUnknownKeys(args, "scanId", "parentId", "depth", "sort", "minSizeBytes", "limit", "cursor");
        context.RequireScanId(args);

        string? parentId = ToolArgs.OptionalString(args, "parentId");
        var parent = parentId is null ? context.Session.Root : context.RequireFolder(parentId);

        int depth = ToolArgs.OptionalInt(args, "depth", 1, 2) ?? 1;
        string sort = ToolArgs.OptionalString(args, "sort") ?? "size";
        if (!Sorts.Contains(sort, StringComparer.Ordinal))
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, "sort 只能是 size / age / name / fileCount");
        }

        long minSize = ToolArgs.OptionalLong(args, "minSizeBytes", 0, long.MaxValue) ?? 0;
        int limit = ToolArgs.OptionalInt(args, "limit", 1, 50) ?? 20;
        int offset = ParseCursor(ToolArgs.OptionalString(args, "cursor"));

        var items = new List<FileEntry>();
        foreach (var child in parent.ChildList)
        {
            if (!FolderIndex.IsRealFolder(child)) continue;
            items.Add(child);

            if (depth < 2) continue;
            foreach (var grandchild in child.ChildList)
            {
                if (FolderIndex.IsRealFolder(grandchild)) items.Add(grandchild);
            }
        }

        if (minSize > 0) items.RemoveAll(f => f.Size < minSize);

        int total = items.Count;
        var ordered = Sort(items, sort)
            .Skip(offset)
            .Take(limit)
            .Select(f => Project(context, f))
            .ToList();

        int nextOffset = offset + ordered.Count;
        string? nextCursor = nextOffset < total ? nextOffset.ToString(CultureInfo.InvariantCulture) : null;

        return ToolResult.Success(
            Name,
            context.Session.ScanId,
            new ListFoldersDataDto(ordered),
            truncated: nextCursor is not null,
            nextCursor: nextCursor);
    }

    private static IEnumerable<FileEntry> Sort(List<FileEntry> items, string sort)
    {
        IOrderedEnumerable<FileEntry> ordered = sort switch
        {
            "age" => items.OrderByDescending(f => f.Modified),
            "name" => items.OrderBy(f => f.Name, StringComparer.Ordinal),
            "fileCount" => items.OrderByDescending(f => f.FileCount),
            _ => items.OrderByDescending(f => f.Size),
        };

        return ordered
            .ThenByDescending(f => f.Size)
            .ThenBy(f => f.Name, StringComparer.Ordinal);
    }

    private static FolderItemDto Project(ToolContext context, FileEntry entry)
    {
        int childFolders = 0;
        foreach (var child in entry.ChildList)
        {
            if (FolderIndex.IsRealFolder(child)) childFolders++;
        }

        string? hint = AppSignatures.Classify(entry.FullPath)?.Key;
        return new FolderItemDto(
            FolderId: context.Index.GetFolderId(entry) ?? "",
            RelPath: EvidenceCardBuilder.Redact(context.Index.GetRelPath(entry)),
            Depth: context.Index.GetDepth(entry),
            SizeBytes: entry.Size,
            AllocatedBytes: entry.Allocated,
            FileCount: entry.FileCount,
            FolderCount: entry.FolderCount,
            DirectFolderCount: childFolders,
            Modified: EvidenceCardBuilder.FormatModified(entry.Modified),
            AgeDays: EvidenceCardBuilder.AgeDays(context.Session, entry.Modified),
            ChildCount: childFolders,
            HasMoreChildren: childFolders > 0,
            Hint: hint);
    }

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        if (!int.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset) || offset < 0)
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, "cursor 不合法");
        }

        return offset;
    }
}
