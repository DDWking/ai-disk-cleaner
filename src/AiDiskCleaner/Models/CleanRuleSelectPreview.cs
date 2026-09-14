using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>
/// 「选择规则明确的清理项」预览里的一类。
///
/// 它只承载**展示与排除**：勾上 <see cref="Excluded"/> 表示这一类不参与本次批量选择。
/// 它不持有任何删除能力，也不改风险或清理资格 —— 那两件事仍然只由规则与用户逐个决定。
/// </summary>
public sealed class CleanRuleSelectGroup : INotifyPropertyChanged
{
    /// <summary>这一类里的完整候选（全部满足规则明确的判据）。</summary>
    public required IReadOnlyList<CleanItem> Items { get; init; }
    public required CleanPurpose Purpose { get; init; }
    public required string Name { get; init; }
    public required string Impact { get; init; }

    public int FileCount => Items.Count;
    public long Bytes { get; init; }
    public string SizeText => Bytes > 0 ? FileEntry.FormatSize(Bytes) : Loc.EstUnknown;
    public string CountText => $"{FileCount:N0}";

    /// <summary>最多三个代表文件名，让「这是什么」不用点开也能看出来。</summary>
    public string SampleText { get; init; } = "";

    private bool _excluded;
    /// <summary>勾上 = 这一类不要参与批量选择（默认全不排除）。</summary>
    public bool Excluded
    {
        get => _excluded;
        set
        {
            if (_excluded == value) return;
            _excluded = value;
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    /// <summary>排除状态变化时通知预览重算汇总。</summary>
    public Action? Changed { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 批量选择的预览。**这是唯一加在「选什么」上的东西**，不新增第三种规则引擎：
/// 成员筛选完全交给 <see cref="CleanRuleEligibility"/>。
/// </summary>
public sealed class CleanRuleSelectPreview
{
    public required IReadOnlyList<CleanRuleSelectGroup> Groups { get; init; }

    /// <summary>没有被排除的类。</summary>
    public IEnumerable<CleanRuleSelectGroup> Active => Groups.Where(g => !g.Excluded);

    public int ActiveFiles => Active.Sum(g => g.FileCount);
    public long ActiveBytes => Active.Sum(g => g.Bytes);
    public int ActiveKinds => Active.Count();

    public int ExcludedFiles => Groups.Where(g => g.Excluded).Sum(g => g.FileCount);
    public int ExcludedKinds => Groups.Count(g => g.Excluded);

    public string SummaryText => ActiveFiles == 0
        ? Loc.RuleSelectNoEligible
        : Loc.RuleSelectSummary(ActiveFiles, FileEntry.FormatSize(ActiveBytes), ActiveKinds);

    public string ExcludedNote => ExcludedKinds == 0
        ? ""
        : Loc.RuleSelectExcludedNote(ExcludedFiles, ExcludedKinds, FileEntry.FormatSize(ExcludedBytes));

    private long ExcludedBytes => Groups.Where(g => g.Excluded).Sum(g => g.Bytes);

    /// <summary>本次会真正被勾上的候选（排除的类不在内）。</summary>
    public IEnumerable<CleanItem> ActiveItems => Active.SelectMany(g => g.Items);

    /// <summary>
    /// 按用途把「规则明确」的候选聚成几类。返回 null 表示这一类都没有。
    /// **只读**：不写任何 Selected。
    /// </summary>
    public static CleanRuleSelectPreview? Build(CleanLayeredResult layered)
    {
        var eligible = layered.AllItems.Where(CleanRuleEligibility.IsRuleClear).ToList();
        if (eligible.Count == 0) return null;

        var groups = eligible
            .GroupBy(x => x.Purpose)
            .OrderBy(g => CleanPurposes.Rank(g.Key))
            .Select(g =>
            {
                var items = g.ToList();
                var group = new CleanRuleSelectGroup
                {
                    Items = items,
                    Purpose = g.Key,
                    Name = CleanPurposes.Name(g.Key),
                    Impact = CleanPurposes.Impact(g.Key),
                    Bytes = items.Sum(x => Math.Max(0, x.Size)),
                    SampleText = string.Join("、", items
                        .OrderByDescending(x => x.Size)
                        .Take(3)
                        .Select(x => x.Name)
                        .Where(x => x.Length > 0)),
                };
                return group;
            })
            .ToList();

        var preview = new CleanRuleSelectPreview { Groups = groups };
        foreach (var group in groups) group.Changed = () => preview.RaiseChanged();
        return preview;
    }

    /// <summary>排除状态变了以后，界面要重算汇总（不重算成员）。</summary>
    public event Action? Changed;
    private void RaiseChanged() => Changed?.Invoke();
}
