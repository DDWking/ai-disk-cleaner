using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.CleanRules;

/// <summary>
/// 独立跑的重复检测阶段。
///
/// 为什么要跟主分析分开：重复检测要真的读文件内容，是全流程里最慢、最该能单独取消的一段。
/// 普通清理结果不该等它 —— 先出列表，重复项跑完再并进去。
///
/// 「未验证完成的重复候选不能提前成为可删除结果」在这里落地：
/// 只有**完整哈希对上**的组才会产出条目；检测没跑完（撞预算/被截断）时，
/// 产出条目的 <c>Selected</c> 一律为 false，并在说明里明确标出「本轮没跑完」。
/// </summary>
public sealed class DuplicateScanService
{
    private readonly Func<string, FileIdentity?>? _identityProbe;

    /// <param name="identityProbe">硬链接身份探测。null = 用真实的 Win32 查询。</param>
    public DuplicateScanService(Func<string, FileIdentity?>? identityProbe = null)
        => _identityProbe = identityProbe;

    public sealed record Outcome(List<CleanItem> Items, DuplicateScanResult Result, TimeSpan Elapsed);

    /// <summary>
    /// 跑一轮重复检测。取消原样抛 <see cref="OperationCanceledException"/>。
    /// </summary>
    public Outcome Run(
        IReadOnlyList<FileEntry> files,
        CancellationToken ct,
        IProgress<DuplicateProgress>? progress = null,
        long budgetBytes = DuplicateDetector.DefaultBudgetBytes)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var probe = _identityProbe ?? DefaultIdentityProbe;

        var result = DuplicateDetector.Find(files, probe, ct, progress, budgetBytes);
        var items = BuildItems(result, ct);

        sw.Stop();
        return new Outcome(items, result, sw.Elapsed);
    }

    /// <summary>
    /// 把检测结果变成界面条目。**只处理已验证的组。**
    /// 检测不完整时默认不勾选，并在说明里标注。
    /// </summary>
    public static List<CleanItem> BuildItems(DuplicateScanResult result, CancellationToken ct = default)
    {
        var items = new List<CleanItem>();
        bool incomplete = !result.Complete;
        string suffix = incomplete
            ? (result.BudgetExhausted ? Loc.DupIncompleteBudget : Loc.DupIncompletePartial)
            : "";

        foreach (var group in result.Groups)
        {
            ct.ThrowIfCancellationRequested();
            var keep = group.Members[0];
            // 同一重复组里的「保留项」和「多余项」处理方式完全不同，
            // 必须用不同的 Handling，否则会被归到同一个位置行里混淆。
            string groupKey = DuplicateGroupKey(group);
            foreach (var f in group.Members)
            {
                bool extra = !ReferenceEquals(f, keep);
                bool canDelete = extra && CleanRuleHelpers.CanOffer(f);
                string reason = extra ? Loc.ReasonDupExtra(keep.FullPath) : Loc.ReasonDupKeep;
                if (suffix.Length > 0) reason += " · " + suffix;

                items.Add(CleanItemFactory.Create(new CleanRuleHit
                {
                    Entry = f,
                    Target = CleanRuleTarget.Duplicates,
                    Reason = reason,
                    Group = Loc.GroupDup,
                    Purpose = CleanPurpose.Duplicate,
                    // 重复组是「逻辑位置」，不是文件夹：靠 groupKey 把同组文件拢在一起
                    Handling = (extra ? "dup-extra|" : "dup-keep|") + groupKey,
                    Risk = CleanRisk.Confirm,
                    CanDelete = canDelete,
                    // **默认不勾选**：重复项也是用户的选择，程序不替他决定保留哪一份。
                    Selected = false,
                    Evidence = EvidenceLevel.Verified,
                    // 「和哪个文件重复」「本轮检测有没有跑完」都在说明里，不能被签名文案顶掉
                    ReasonIsEssential = true,
                    Tech = $"size={FileEntry.FormatSize(group.Size)}" + (suffix.Length > 0 ? " · " + suffix : ""),
                }));
            }
        }
        return items;
    }

    /// <summary>
    /// 重复组的稳定标识：用「大小 + 内容排序后的首尾路径」拼，不用显示名。
    /// 保留项变了（路径最短的那个被删了）标识才会变，这是符合预期的。
    /// </summary>
    public static string DuplicateGroupKey(DuplicateGroup group)
    {
        var ordered = group.Members
            .Select(m => m.FullPath ?? "")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        string first = ordered.Count > 0 ? ordered[0] : "";
        string last = ordered.Count > 1 ? ordered[^1] : "";
        return group.Size + "|" + first + "|" + last;
    }

    private static FileIdentity? DefaultIdentityProbe(string path)
        => Win32FileSystemProbe.Instance.TryGetIdentity(path);
}
