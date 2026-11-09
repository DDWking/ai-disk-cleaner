using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.Agent;

/// <summary>一个候选目录（本地生成，模型看不到未入候选的节点）。</summary>
public sealed record CandidateItem(
    string FolderId,
    string RelPath,
    long SizeBytes,
    double Score,
    int Depth,
    string? AppHint);

public sealed record CandidateSet(IReadOnlyList<CandidateItem> Items, bool Truncated, int Considered);

public sealed class CandidateOptions
{
    /// <summary>太小的目录不值得占用一次模型注意力。</summary>
    public long MinSizeBytes { get; init; } = 10L * 1024 * 1024;

    /// <summary>只看到这个深度，避免整棵子树刷屏。</summary>
    public int MaxDepth { get; init; } = 4;

    public int MaxCandidates { get; init; } = 200;

    /// <summary>同一父目录最多几个候选。</summary>
    public int MaxPerParent { get; init; } = 8;

    public int OldAgeDays { get; init; } = 180;

    public long OldMinSizeBytes { get; init; } = 500L * 1024 * 1024;
}

/// <summary>
/// 候选集：纯本地、确定性、不依赖 AI。
///
/// 入口是「树的规模 + 年龄 + 本地签名 + 保护路径」，P1 再接 CleanReport 的规则命中。
/// 保护路径（<see cref="ProtectedPaths"/> 判为 Blocked）**整棵子树都不进候选**，也不继续下钻。
/// </summary>
public static class CandidateSelector
{
    /// <summary>
    /// 这些是「容器根」而不是可清理对象：整目录删掉等于毁系统/毁账号。
    /// ProtectedPaths 对它们本身返回 Allowed/Confirm（真正硬拦的是里面的关键子目录），
    /// 所以候选层要单独把它们排除，避免模型看到「Program Files 有 40GB」就建议整个删。
    /// </summary>
    private static readonly HashSet<string> ContainerRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "Users",
        "PerfLogs",
        "Windows",
        "Recovery",
    };

    public static CandidateSet Select(ScanSession session, FolderIndex index, CandidateOptions? options = null)
    {
        options ??= new CandidateOptions();

        var considered = new List<CandidateItem>();
        var stack = new Stack<(FileEntry Node, int Depth)>();

        foreach (var child in session.Root.ChildList)
        {
            if (!FolderIndex.IsRealFolder(child)) continue;
            if (ProtectedPaths.IsCleanupBlocked(child)) continue;
            stack.Push((child, 1));
        }

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();

            // 命中保护路径的子树直接剪掉：既不进候选，也不继续下钻。
            if (ProtectedPaths.IsCleanupBlocked(node)) continue;

            if (node.Size >= options.MinSizeBytes
                && !ProtectedPaths.IsDriveRoot(node.FullPath)
                && !IsContainerRoot(node))
            {
                considered.Add(new CandidateItem(
                    FolderId: index.GetFolderId(node) ?? FolderIndex.MakeFolderId(index.GetRelPath(node)),
                    RelPath: EvidenceCardBuilder.Redact(index.GetRelPath(node)),
                    SizeBytes: node.Size,
                    Score: Score(session, node),
                    Depth: depth,
                    AppHint: AppSignatures.Classify(node.FullPath)?.Name));
            }

            if (depth >= options.MaxDepth) continue;
            foreach (var child in node.ChildList)
            {
                if (FolderIndex.IsRealFolder(child)) stack.Push((child, depth + 1));
            }
        }

        // 稳定排序：分数降序，相对路径升序兜底。
        var ranked = considered
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.RelPath, StringComparer.Ordinal)
            .ToList();

        var kept = new List<CandidateItem>(Math.Min(ranked.Count, options.MaxCandidates));
        var perParent = new Dictionary<FileEntry, int>(ReferenceEqualityComparer.Instance);
        bool truncated = false;

        foreach (var item in ranked)
        {
            if (kept.Count >= options.MaxCandidates)
            {
                truncated = true;
                break;
            }

            var node = index.TryGet(item.FolderId, out var entry) ? entry : null;
            var parent = node?.Parent;
            if (parent is not null)
            {
                perParent.TryGetValue(parent, out int count);
                if (count >= options.MaxPerParent)
                {
                    truncated = true;
                    continue;
                }

                perParent[parent] = count + 1;
            }

            kept.Add(item);
        }

        return new CandidateSet(kept, truncated, considered.Count);
    }

    /// <summary>盘符根下的一级「容器目录」，整目录不可作为清理对象。</summary>
    private static bool IsContainerRoot(FileEntry node)
    {
        if (node.Parent is null) return false;
        if (node.Parent.Parent is not null) return false; // 只判盘根直接子项
        return ContainerRoots.Contains(node.Name);
    }

    /// <summary>
    /// score = w1·log10(size) + w2·min(age,730)/365 + w3·ruleConfidence + w4·appHint - p2·systemPenalty
    /// P0 没有规则引擎，ruleConfidence 用签名命中做保守替代（Keep 类签名不给分）。
    /// </summary>
    public static double Score(ScanSession session, FileEntry node)
    {
        double age = Math.Min(EvidenceCardBuilder.AgeDays(session, node.Modified), 730) / 365.0;
        double appHint = 0;
        double ruleConfidence = 0;

        var signature = AppSignatures.Classify(node.FullPath);
        if (signature is { } hit)
        {
            appHint = 1.0;
            if (hit.Risk != CleanRisk.Keep) ruleConfidence = 0.5;
        }

        double systemPenalty = node.IsSystem ? 1.0 : 0.0;
        return 1.0 * Math.Log10(Math.Max(node.Size, 1))
               + 0.8 * age
               + 1.2 * ruleConfidence
               + 0.6 * appHint
               - 3.0 * systemPenalty;
    }
}
