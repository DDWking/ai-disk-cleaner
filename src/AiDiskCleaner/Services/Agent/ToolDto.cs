namespace AiDiskCleaner.Services.Agent;

// 工具的返回 DTO。字段名即协议名（camelCase 由序列化器转），顺序即字节顺序——
// 同一输入必须得到同一串 JSON，评测与缓存都依赖这一点。

public sealed record OverviewQualityDto(bool Complete, int Unparsed, int Orphans);

public sealed record TopFolderDto(string FolderId, string RelPath, long SizeBytes, int FileCount);

public sealed record CategoryDto(string Name, long SizeBytes);

public sealed record OverviewDto(
    string Drive,
    long TotalBytes,
    long FreeBytes,
    int FileCount,
    int FolderCount,
    string ScannedAt,
    OverviewQualityDto Quality,
    IReadOnlyList<TopFolderDto> TopFolders,
    IReadOnlyList<CategoryDto> Categories);

public sealed record FolderItemDto(
    string FolderId,
    string RelPath,
    int Depth,
    long SizeBytes,
    long AllocatedBytes,
    int FileCount,
    int FolderCount,
    int DirectFolderCount,
    string Modified,
    int AgeDays,
    int ChildCount,
    bool HasMoreChildren,
    string? Hint);

public sealed record ListFoldersDataDto(IReadOnlyList<FolderItemDto> Items);

public sealed record EntryItemDto(
    string Kind,
    string? FolderId,
    string? ParentFolderId,
    string Name,
    string RelPath,
    long SizeBytes,
    long AllocatedBytes,
    string Modified,
    int AgeDays,
    string Ext,
    string Category);

public sealed record QueryEntriesDataDto(IReadOnlyList<EntryItemDto> Items);
