using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>
/// 清理工作流的应用层编排。
///
/// 这里不碰 WPF 控件，也不决定用户是否删除：只把扫描、降级和本地分析
/// 组合成可独立调用的工作流，方便窗口层只负责状态展示与事件转发。
/// </summary>
public sealed class CleanupWorkflowService
{
    private readonly ScanCoordinator _scan;

    public CleanupWorkflowService(IScanService primary, IScanService fallback)
        : this(new ScanCoordinator(primary, fallback))
    {
    }

    public CleanupWorkflowService(ScanCoordinator scan)
    {
        _scan = scan ?? throw new ArgumentNullException(nameof(scan));
    }

    public Task<ScanOutcome> ScanAsync(
        string drive,
        IProgress<ScanProgress>? progress,
        CancellationToken ct,
        Action<string>? onFallback = null)
        => _scan.RunAsync(drive, progress, ct, onFallback);

    /// <summary>
    /// 读取当前扫描树，加载上一份快照，并运行本地清理规则。
    /// 不调用 AI；重复检测仍由后续独立阶段负责。
    /// </summary>
    public Task<CleanupAnalysisOutcome> AnalyzeAsync(
        FileEntry root,
        CancellationToken ct,
        IProgress<ScanProgress>? progress = null,
        bool includeDuplicates = false)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var previous = ScanSnapshot.Load(root.FullPath);
            var files = CollectFiles(root, ct);
            var report = CleanAnalyzer.Analyze(root, previous, ct, progress, includeDuplicates);
            return new CleanupAnalysisOutcome(files, previous, report);
        }, ct);
    }

    public Task<DuplicateScanService.Outcome> FindDuplicatesAsync(
        IReadOnlyList<FileEntry> files,
        CancellationToken ct,
        IProgress<DuplicateProgress>? progress = null,
        long budgetBytes = DuplicateDetector.DefaultBudgetBytes)
    {
        ArgumentNullException.ThrowIfNull(files);
        return Task.Run(() => new DuplicateScanService().Run(files, ct, progress, budgetBytes), ct);
    }

    /// <summary>把界面候选转换成带扫描时身份快照的删除目标。</summary>
    public DeletionPlan PlanDeletion(
        IEnumerable<CleanItem> items,
        bool snapshotIsExact,
        DeletionCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(coordinator);
        return coordinator.Plan(ToDeletionTargets(items, snapshotIsExact));
    }

    private static List<DeletionTarget> ToDeletionTargets(
        IEnumerable<CleanItem> items,
        bool snapshotIsExact)
    {
        return items.Select(x => new DeletionTarget
        {
            Path = x.FullPath,
            Label = x.Name,
            IsDirectory = x.IsDirectory,
            ExpectedSize = x.IsDirectory ? -1 : x.Size,
            ExpectedModified = x.Entry?.Modified ?? default,
            SnapshotIsExact = snapshotIsExact,
            Source = x,
        }).ToList();
    }

    private static List<FileEntry> CollectFiles(FileEntry node, CancellationToken ct)
    {
        var result = new List<FileEntry>(Math.Max(1024, node.FileCount));
        var visited = new HashSet<FileEntry>();
        var stack = new Stack<FileEntry>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            foreach (var child in current.ChildList)
            {
                if (child.IsDirectory)
                {
                    if (visited.Add(child)) stack.Push(child);
                }
                else
                {
                    result.Add(child);
                }
            }
        }

        return result;
    }
}

public sealed record CleanupAnalysisOutcome(
    List<FileEntry> Files,
    ScanSnapshot? Previous,
    CleanReport Report);
