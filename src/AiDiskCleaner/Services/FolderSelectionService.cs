using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>手动勾选集合经过父子去重后的结果。</summary>
public sealed class FolderSelection
{
    /// <summary>用户实际勾选的项数（含被父目录覆盖的子项）。</summary>
    public int SelectedCount { get; init; }
    /// <summary>真正会送去处理的项数。</summary>
    public int KeptCount => Targets.Count;
    /// <summary>因为父目录已选中 / 完全重复而没有单独处理的项数。</summary>
    public int NestedCount { get; init; }
    /// <summary>处理项加起来的扫描容量（父目录已含子项，去重后不会重复计）。</summary>
    public long TotalBytes { get; init; }
    public IReadOnlyList<DeletionTarget> Targets { get; init; } = Array.Empty<DeletionTarget>();

    public bool IsEmpty => Targets.Count == 0;
}

/// <summary>
/// 手动选择 → 删除目标。**纯函数**：只看投影里的 <see cref="FolderPick.IsSelected"/>，
/// 不读 AI 结论、不碰磁盘、不删任何东西。
///
/// 去重口径与 <see cref="DeletionPreflight"/> 一致（规范化路径 + 父目录覆盖子目录），
/// 先在这里做一遍，是为了让**预览**就能如实说出「有几个子项被父目录合并了」；
/// 预检那边还有一遍，作为第二道保险。
/// </summary>
public static class FolderSelectionService
{
    /// <summary>勾选中的项（保持传入顺序，便于界面稳定显示）。</summary>
    public static List<FolderPick> Selected(IEnumerable<FolderPick>? picks)
        => picks?.Where(x => x.IsSelected).ToList() ?? new List<FolderPick>();

    /// <summary>构建删除目标：去重、带上扫描容量与稳定路径。</summary>
    public static FolderSelection Build(IEnumerable<FolderPick>? picks, CancellationToken ct = default)
    {
        var selected = Selected(picks);
        int selectedCount = selected.Count;

        // 短的在前：父目录一定比子项短，先收父就能把子项判成被覆盖。
        var ordered = selected
            .Select(x => (Item: x, Norm: DeletionPreflight.NormPath(x.FullPath)))
            .Where(x => x.Norm.Length > 0)
            .OrderBy(x => x.Norm.Length)
            .ToList();

        var keptExact = new HashSet<string>(StringComparer.Ordinal);
        var keptDirs = new List<string>();
        var targets = new List<DeletionTarget>(ordered.Count);
        int nested = 0;
        long bytes = 0;

        foreach (var (item, norm) in ordered)
        {
            ct.ThrowIfCancellationRequested();

            if (!keptExact.Add(norm)) { nested++; continue; }

            bool covered = false;
            foreach (var dir in keptDirs)
            {
                if (norm.Length > dir.Length && norm.StartsWith(dir, StringComparison.Ordinal)
                    && norm[dir.Length] == '\\')
                {
                    covered = true;
                    break;
                }
            }
            if (covered) { nested++; continue; }

            keptDirs.Add(norm);
            targets.Add(new DeletionTarget
            {
                Path = item.FullPath,
                Label = item.Name,
                IsDirectory = true,
                // 目录的 Size 是子树聚合值，和磁盘目录项对不上：只用于「预计空间」，不参与比对。
                ExpectedSize = Math.Max(0, item.Size),
                ExpectedModified = default,
                SnapshotIsExact = false,
                Source = null,
            });
            bytes += Math.Max(0, item.Size);
        }

        return new FolderSelection
        {
            SelectedCount = selectedCount,
            NestedCount = nested,
            TotalBytes = bytes,
            Targets = targets,
        };
    }
}
