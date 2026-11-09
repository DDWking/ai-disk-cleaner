using System.Globalization;
using System.Text.Json;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.Agent;

/// <summary>
/// query_entries：按大小/年龄/类型/关键词查文件与目录。
/// 只做子串匹配，不做正则；过滤键必须白名单，防止模型自造参数。
/// </summary>
public sealed class QueryEntriesTool : IAgentTool
{
    private const int MaxLimit = 20;
    private const int MaxScanned = 200_000;
    private const int MaxPathContains = 40;

    public string Name => "query_entries";

    public string Description => "按大小、年龄、扩展名、路径子串查条目";

    public ToolResult Execute(ToolContext context, JsonElement args)
    {
        ToolArgs.RejectUnknownKeys(args, "scanId", "filter", "sort", "limit", "cursor");
        context.RequireScanId(args);

        var filterElement = ToolArgs.OptionalObject(args, "filter");
        var filter = filterElement is null ? new EntryFilter() : ParseFilter(filterElement.Value);

        string sort = ToolArgs.OptionalString(args, "sort") ?? "size";
        if (sort is not ("size" or "age" or "name"))
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, "sort 只能是 size / age / name");
        }

        int limit = ToolArgs.OptionalInt(args, "limit", 1, MaxLimit) ?? MaxLimit;
        int offset = ParseCursor(ToolArgs.OptionalString(args, "cursor"));

        var root = context.Session.Root;
        var matches = new List<FileEntry>();
        bool truncated = false;
        int scanned = 0;

        var pending = new Stack<FileEntry>();
        for (int i = root.ChildList.Count - 1; i >= 0; i--) pending.Push(root.ChildList[i]);

        while (pending.Count > 0)
        {
            if (scanned++ >= MaxScanned)
            {
                truncated = true;
                break;
            }

            var node = pending.Pop();
            if (Matches(context, node, filter)) matches.Add(node);

            if (filter.TopLevelOnly || !node.IsDirectory || node.IsReparsePoint) continue;
            for (int i = node.ChildList.Count - 1; i >= 0; i--) pending.Push(node.ChildList[i]);
        }

        int total = matches.Count;
        var ordered = Sort(matches, sort)
            .Skip(offset)
            .Take(limit)
            .Select(node => Project(context, node))
            .ToList();

        int nextOffset = offset + ordered.Count;
        string? nextCursor = nextOffset < total ? nextOffset.ToString(CultureInfo.InvariantCulture) : null;

        return ToolResult.Success(
            Name,
            context.Session.ScanId,
            new QueryEntriesDataDto(ordered),
            truncated: truncated || nextCursor is not null,
            nextCursor: nextCursor);
    }

    private static EntryFilter ParseFilter(JsonElement element)
    {
        ToolArgs.RejectUnknownKeys(
            element,
            "kind", "minSizeBytes", "maxSizeBytes", "minAgeDays", "maxAgeDays",
            "extensions", "pathContains", "topLevelOnly");

        string kind = ToolArgs.OptionalString(element, "kind") ?? "any";
        if (kind is not ("file" or "folder" or "any"))
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, "filter.kind 只能是 file / folder / any");
        }

        var filter = new EntryFilter
        {
            Kind = kind,
            MinSizeBytes = ToolArgs.OptionalLong(element, "minSizeBytes", 0, long.MaxValue) ?? 0,
            MaxSizeBytes = ToolArgs.OptionalLong(element, "maxSizeBytes", 0, long.MaxValue),
            MinAgeDays = ToolArgs.OptionalInt(element, "minAgeDays", 0, 36500),
            MaxAgeDays = ToolArgs.OptionalInt(element, "maxAgeDays", 0, 36500),
            TopLevelOnly = ToolArgs.OptionalBool(element, "topLevelOnly") ?? false,
        };

        if (filter.MaxSizeBytes is { } max && max < filter.MinSizeBytes)
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, "maxSizeBytes 不能小于 minSizeBytes");
        }

        if (filter.MaxAgeDays is { } maxAge && filter.MinAgeDays is { } minAge && maxAge < minAge)
        {
            throw new AgentToolException(ToolErrorCodes.InvalidArgument, "maxAgeDays 不能小于 minAgeDays");
        }

        foreach (var raw in ToolArgs.OptionalStringArray(element, "extensions", 8))
        {
            string ext = raw.Trim().TrimStart('.').ToLowerInvariant();
            if (ext.Length == 0 || ext.Contains('\\') || ext.Contains('/') || ext.Contains(' '))
            {
                throw new AgentToolException(ToolErrorCodes.InvalidArgument, "extensions 只能是扩展名（不带点）");
            }

            filter.Extensions.Add(ext);
        }

        string? pathContains = ToolArgs.OptionalString(element, "pathContains");
        if (pathContains is not null)
        {
            if (pathContains.Length > MaxPathContains)
            {
                throw new AgentToolException(ToolErrorCodes.InvalidArgument, $"pathContains 最多 {MaxPathContains} 个字符");
            }

            filter.PathContains = pathContains;
        }

        return filter;
    }

    private static bool Matches(ToolContext context, FileEntry node, EntryFilter filter)
    {
        if (node.IsFilesGroup || node.IsReparsePoint) return false;

        bool isDirectory = node.IsDirectory;
        if (filter.Kind == "file" && isDirectory) return false;
        if (filter.Kind == "folder" && !isDirectory) return false;

        if (node.Size < filter.MinSizeBytes) return false;
        if (filter.MaxSizeBytes is { } maxSize && node.Size > maxSize) return false;

        int ageDays = EvidenceCardBuilder.AgeDays(context.Session, node.Modified);
        if (filter.MinAgeDays is { } minAge && ageDays < minAge) return false;
        if (filter.MaxAgeDays is { } maxAge && ageDays > maxAge) return false;

        if (filter.Extensions.Count > 0)
        {
            if (isDirectory) return false;
            string ext = ExtensionOf(node.Name);
            if (!filter.Extensions.Contains(ext, StringComparer.Ordinal)) return false;
        }

        if (filter.PathContains is not null)
        {
            string rel = context.Index.GetRelPath(node);
            if (rel.IndexOf(filter.PathContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
        }

        return true;
    }

    private static IEnumerable<FileEntry> Sort(List<FileEntry> items, string sort)
    {
        IOrderedEnumerable<FileEntry> ordered = sort switch
        {
            "age" => items.OrderBy(f => f.Modified),
            "name" => items.OrderBy(f => f.Name, StringComparer.Ordinal),
            _ => items.OrderByDescending(f => f.Size),
        };

        return ordered
            .ThenByDescending(f => f.Size)
            .ThenBy(f => f.FullPath, StringComparer.Ordinal);
    }

    private static EntryItemDto Project(ToolContext context, FileEntry node)
        => new(
            Kind: node.IsDirectory ? "folder" : "file",
            FolderId: node.IsDirectory ? context.Index.GetFolderId(node) : null,
            ParentFolderId: node.Parent is null ? null : context.Index.GetFolderId(node.Parent),
            Name: node.Name,
            RelPath: EvidenceCardBuilder.Redact(context.Index.GetRelPath(node)),
            SizeBytes: node.Size,
            AllocatedBytes: node.Allocated,
            Modified: EvidenceCardBuilder.FormatModified(node.Modified),
            AgeDays: EvidenceCardBuilder.AgeDays(context.Session, node.Modified),
            Ext: node.IsDirectory ? "" : ExtensionOf(node.Name),
            Category: node.Category);

    private static string ExtensionOf(string name)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return "noext";
        return name[(dot + 1)..].ToLowerInvariant();
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

    private sealed class EntryFilter
    {
        public string Kind { get; init; } = "any";
        public long MinSizeBytes { get; init; }
        public long? MaxSizeBytes { get; init; }
        public int? MinAgeDays { get; init; }
        public int? MaxAgeDays { get; init; }
        public List<string> Extensions { get; } = new();
        public string? PathContains { get; set; }
        public bool TopLevelOnly { get; init; }
    }
}
