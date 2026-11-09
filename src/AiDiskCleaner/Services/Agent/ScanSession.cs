using System.Globalization;
using System.Security.Cryptography;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.Agent;

/// <summary>
/// 一次扫描会话：把「这次扫的是哪台盘、什么时候扫的、扫得全不全」固定下来。
///
/// 工具只认 <see cref="ScanId"/>，不认路径。会话本身不复制整棵树，
/// 只持有根节点；索引在第一次用到时才建（见 <see cref="Index"/>）。
/// </summary>
public sealed class ScanSession
{
    /// <summary>30 分钟内算新鲜；更旧的会话仍可用，但会被标记 stale。</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(30);

    /// <summary>超过 7 天视为过期（返回 SCAN_NOT_FOUND）。</summary>
    public static readonly TimeSpan ExpireAfter = TimeSpan.FromDays(7);

    public string ScanId { get; }

    /// <summary>盘符，形如 "C:"。</summary>
    public string Drive { get; }

    public DateTimeOffset ScannedAt { get; }

    public FileEntry Root { get; }

    /// <summary>扫描质量报告；假树/测试里可以为 null。</summary>
    public ScanQuality? Quality { get; }

    /// <summary>扫描当刻的可用空间（字节）。取不到就是 0，报告里只作概览。</summary>
    public long FreeBytes { get; }

    /// <summary>"mft" / "recursive" / "unknown"。</summary>
    public string Source => Quality?.Source switch
    {
        ScanSource.Mft => "mft",
        ScanSource.Recursive => "recursive",
        _ => "unknown",
    };

    public bool IsStale => DateTimeOffset.UtcNow - ScannedAt > FreshFor;

    public bool IsExpired => DateTimeOffset.UtcNow - ScannedAt > ExpireAfter;

    public string RootFolderId => FolderIndex.RootId;

    private FolderIndex? _index;

    /// <summary>folderId ↔ FileEntry 索引；第一次访问时构建。</summary>
    public FolderIndex Index => _index ??= new FolderIndex(Root);

    private IReadOnlyDictionary<string, long>? _categoryTotals;

    /// <summary>按 <see cref="FileEntry.Category"/> 聚合的字节数（首次访问时 DFS 一次）。</summary>
    public IReadOnlyDictionary<string, long> CategoryTotals => _categoryTotals ??= ComputeCategoryTotals();

    private ScanSession(string scanId, string drive, DateTimeOffset scannedAt, FileEntry root, ScanQuality? quality, long freeBytes)
    {
        ScanId = scanId;
        Drive = drive;
        ScannedAt = scannedAt;
        Root = root;
        Quality = quality;
        FreeBytes = freeBytes;
    }

    /// <summary>
    /// 建一个会话。扫描流程里由 UI 调用；离线检查里可以传假树和固定时间。
    /// </summary>
    public static ScanSession Create(
        string drive,
        FileEntry root,
        ScanQuality? quality = null,
        DateTimeOffset? scannedAt = null,
        string? scanId = null,
        long freeBytes = 0)
    {
        if (root is null) throw new ArgumentNullException(nameof(root));

        var at = scannedAt ?? DateTimeOffset.Now;
        var id = string.IsNullOrWhiteSpace(scanId) ? NewScanId(at) : scanId;
        return new ScanSession(id, drive, at, root, quality, freeBytes);
    }

    /// <summary>形如 s_20261109_1a2b3c（时间 + 3 字节随机，便于日志里对上）。</summary>
    public static string NewScanId(DateTimeOffset at)
    {
        string stamp = at.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string random = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        return $"s_{stamp}_{random}";
    }

    private Dictionary<string, long> ComputeCategoryTotals()
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        var stack = new Stack<FileEntry>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.IsDirectory)
            {
                foreach (var child in node.ChildList) stack.Push(child);
                continue;
            }

            string key = string.IsNullOrWhiteSpace(node.Category) ? "其他" : node.Category;
            totals.TryGetValue(key, out long bytes);
            totals[key] = bytes + node.Size;
        }

        return totals;
    }
}
