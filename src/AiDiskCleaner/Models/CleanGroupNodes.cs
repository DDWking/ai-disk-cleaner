using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>
/// 勾选状态的三态：全选 / 未选 / 半选。分组勾选必须能表达「组内只有一部分被选中」。
/// </summary>
public enum TriState { Unchecked, Checked, Indeterminate }

/// <summary>
/// 一层分组的公共部分：计数、空间、勾选。
///
/// 关键点：<see cref="Items"/> 始终是**该组的完整候选**，
/// 不是当前显示页 —— 勾选、统计、确认都必须基于它。分页只影响显示。
/// </summary>
public abstract class CleanGroupNodeBase : INotifyPropertyChanged
{
    private bool? _isChecked = false;
    private bool _isExpanded;

    /// <summary>该组的完整候选（不是当前页）。</summary>
    public IReadOnlyList<CleanItem> Items { get; internal set; } = Array.Empty<CleanItem>();

    /// <summary>候选文件数（含不可删的保留项）。</summary>
    public int FileCount => Items.Count;
    /// <summary>规则允许删除的候选数 —— 勾选只能落在这部分上。</summary>
    public int SelectableCount { get; internal set; }
    /// <summary>预计可清理空间（只算规则允许删的、且未被父目录覆盖的）。</summary>
    public long Bytes { get; internal set; }
    /// <summary>空间是否为未知/估算（不编造精确值）。</summary>
    public bool SizeIsEstimate { get; internal set; }
    /// <summary>被同组内目录候选覆盖、未重复计入空间的条目数。</summary>
    public int OverlapCount { get; internal set; }

    public string SizeText => Bytes > 0 ? FileEntry.FormatSize(Bytes) : Loc.EstUnknown;

    public int SelectedCount => Items.Count(x => x.Selected && x.CanDelete);

    /// <summary>三态勾选。写入会**只作用于 <c>CanDelete</c> 的候选**。</summary>
    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            ApplySelection(value == true);
            _isChecked = value;
            OnPropertyChanged();
        }
    }

    /// <summary>界面点勾选框时调用：按三态循环（未选/半选 → 全选，全选 → 取消）。</summary>
    public void ToggleFrom(TriState current)
    {
        bool target = current != TriState.Checked;
        _isChecked = target;
        ApplySelection(target);
        OnPropertyChanged(nameof(IsChecked));
        RaiseSelectionChanged();
    }

    /// <summary>
    /// 只勾选规则本来就允许删的候选。**这里不会创建任何新的删除目标**，
    /// 也不会把分组路径变成递归删除对象 —— 单纯的展示层勾选。
    /// </summary>
    private void ApplySelection(bool on)
    {
        foreach (var item in Items)
        {
            if (!item.CanDelete) continue; // 保留项 / 受保护 / 长路径等永远不动
            item.Selected = on;
        }
        RaiseSelectionChanged();
    }

    /// <summary>外部（明细页）改了勾选后，回来同步三态。</summary>
    public void SyncFromItems()
    {
        bool? state = ComputeTriState() switch
        {
            TriState.Checked => true,
            TriState.Indeterminate => null,
            _ => false,
        };
        if (_isChecked != state)
        {
            _isChecked = state;
            OnPropertyChanged(nameof(IsChecked));
        }
        RaiseSelectionChanged();
    }

    protected TriState ComputeTriState()
    {
        int selectable = 0, selected = 0;
        foreach (var item in Items)
        {
            if (!item.CanDelete) continue;
            selectable++;
            if (item.Selected) selected++;
        }
        if (selectable == 0 || selected == 0) return TriState.Unchecked;
        return selected == selectable ? TriState.Checked : TriState.Indeterminate;
    }

    /// <summary>给界面用的三态查询（界面点勾选框时要知道当前是第几态）。</summary>
    public TriState ComputeTriStatePublic() => ComputeTriState();

    protected void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(SelectionRatioText));
        OnPropertyChanged(nameof(SelectionBadge));
        OnPropertyChanged(nameof(HasSelectionBadge));
    }

    /// <summary>「已选 N / 共 M」——勾选提示必须说清范围。</summary>
    public string SelectionText => SelectableCount == 0
        ? Loc.LocationFiles(FileCount)
        : $"{SelectedCount:N0} / {SelectableCount:N0}";

    /// <summary>
    /// 带单位的比例文案：「已选 0 / 21 项」。
    /// 裸写「0 / 21」会让人猜那个 21 是文件还是位置，所以量词必须写出来。
    /// </summary>
    public string SelectionRatioText => SelectableCount == 0
        ? Loc.NothingSelectable
        : Loc.SelectedRatio(SelectedCount, SelectableCount);

    /// <summary>
    /// 有选择时才显示「已选 N / M 项」。没选择时是空串 ——
    /// 每行都写「已选 0 / 385 项」纯属噪音。
    /// </summary>
    public string SelectionBadge => SelectedCount > 0 ? SelectionRatioText : "";
    public bool HasSelectionBadge => SelectedCount > 0;

    /// <summary>组勾选的范围提示：明确是整组而不是当前页。</summary>
    public string GroupScopeHint => Loc.SelectWholeGroup(SelectableCount);

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 第二层：一个清理位置（某个软件 / 某个实际文件夹 / 一个重复组）。
/// </summary>
public sealed class CleanLocationNode : CleanGroupNodeBase
{
    /// <summary>稳定键：由用途 + 风险档 + 归一化锚点路径（或重复组键）构成，**不用显示名**。</summary>
    public required string Key { get; init; }
    /// <summary>显示名：软件名，或认不出时的「文件夹」标记。</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>实际路径。重复组显示成员数量而不是单个路径。</summary>
    public string Path { get; set; } = "";
    /// <summary>认不出软件时为 true，界面照实说「没认出是哪个软件」。</summary>
    public bool IsUnidentified { get; set; }
    public CleanPurpose Purpose { get; init; }
    public int RiskTier { get; init; }
    public CleanRisk Risk { get; init; }
    /// <summary>处理方式（duplicates 的 keep / extra 会分成不同位置）。</summary>
    public string Handling { get; init; } = "";

    private string _reason = "";
    /// <summary>清理原因：取组内代表项的规则原因。</summary>
    public string Reason
    {
        get => _reason;
        set { if (_reason == value) return; _reason = value; OnPropertyChanged(); }
    }

    /// <summary>清理影响。</summary>
    public string Impact { get; set; } = "";

    bool _isPathVisible;
    /// <summary>
    /// 「查看路径」是否已展开。默认不显示完整路径 —— 路径不该占据整列，
    /// 需要核对时再点开（并可复制 / 在资源管理器中打开）。
    /// </summary>
    public bool IsPathVisible
    {
        get => _isPathVisible;
        set
        {
            if (_isPathVisible == value) return;
            _isPathVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PathToggleText));
        }
    }

    public string PathToggleText => IsPathVisible ? Loc.HidePathAction : Loc.ShowPathAction;

    /// <summary>
    /// 第二行的「精简父路径」：用来说明同名文件夹分别是哪一个。
    /// 最多 3 段，前面用 … 省略，长路径不会把行撑破。
    /// </summary>
    public string ParentHint => Services.CleanGroupingService.ParentHint(Path);

    /// <summary>
    /// 第二行统计：`C:\Windows\Logs · 385 个文件`。
    /// 「用途待确认」只在认不出用途时出现，不再每行都挂一句「看不出用途」。
    /// </summary>
    public string RowSubText
    {
        get
        {
            var bits = new List<string>();
            string parent = ParentHint;
            if (parent.Length > 0) bits.Add(parent);
            bits.Add(Loc.LocationFiles(FileCount));
            if (IsUnidentified) bits.Add(Loc.PurposeUnclear);
            return string.Join(" · ", bits);
        }
    }

    /// <summary>技术细节（命中了哪条签名、风险词等）。术语留在悬停里，不进标题。</summary>
    public string Tech { get; set; } = "";

    /// <summary>
    /// 这一项的 AI 状态。**挂在项目自己身上**（位置用稳定键、文件用完整路径），
    /// 所以滚动回收、切页、重扫都不会让结果串到别的行上。
    /// 惰性创建以保证绑定永远拿得到非 null 的对象。
    /// </summary>
    public ItemAiView Ai
    {
        get
        {
            if (_ai == null)
            {
                _ai = new ItemAiView
                {
                    ScopeKey = Key,
                    // 本地规则给出的是**本地判断**，必须标注，不能冒充 AI 结论
                    LocalNote = Services.Loc.ItemAiLocalOnly + Reason,
                };
                OnPropertyChanged(nameof(Ai));
            }
            return _ai;
        }
    }

    private ItemAiView? _ai;

    /// <summary>已经建出来的 AI 视图；没建过就是 null（不会顺手创建）。</summary>
    public ItemAiView? ExistingAi => _ai;

    /// <summary>悬停：软件名 + 实际路径 + 技术细节。</summary>
    public string HintText
    {
        get
        {
            var bits = new List<string>();
            if (IsUnidentified) bits.Add(Loc.NoSoftwareName);
            if (!string.IsNullOrEmpty(Path)) bits.Add(Path);
            if (!string.IsNullOrEmpty(Tech)) bits.Add(Tech);
            return string.Join("\n", bits);
        }
    }

    public string FileCountText => Loc.LocationFiles(FileCount);
}

/// <summary>
/// 首页的一个风险分区：建议清理（默认展开）/ 需要你确认（默认折叠）。
///
/// 存在的理由很具体：风险由**分区标题**表达，行里就不再重复「建议清理」四个字，
/// 首页也不再有单独的类型列。
/// </summary>
public sealed class CleanPurposeSection : INotifyPropertyChanged
{
    private bool _isExpanded;

    public required int RiskTier { get; init; }
    public required string Title { get; init; }
    /// <summary>该分区的用途行。</summary>
    public required IReadOnlyList<CleanPurposeNode> Rows { get; init; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Chevron));
            OnPropertyChanged(nameof(HasCollapsedSelection));
        }
    }

    public int LocationCount { get; init; }
    public int FileCount { get; init; }
    public long Bytes { get; init; }
    public string SizeText => Bytes > 0 ? FileEntry.FormatSize(Bytes) : Loc.EstUnknown;

    /// <summary>这一组里有多少个用途行。</summary>
    public int RowCount => Rows.Count;

    /// <summary>分区标题文字（不含统计）。风险只在这里表达一次。</summary>
    public string HeaderTitle => Title;

    /// <summary>
    /// 分区副标题：说清这一组的**性质**，不只靠颜色区分风险。
    /// 「建议清理」＝规则明确可以优先；「需要你确认」＝可能仍有价值，要看过再决定。
    /// </summary>
    public string Subtitle => RiskTier == 0 ? Loc.SectionSubtitleSafe : Loc.SectionSubtitleConfirm;

    /// <summary>分区图标：✓ / !。文字符号同样能读出状态，不依赖颜色。</summary>
    public string Icon => RiskTier == 0 ? Loc.SectionIconSafe : Loc.SectionIconConfirm;

    /// <summary>左侧色带的画刷键（克制：绿松石 / 暗橙）。</summary>
    public string AccentKey => RiskTier == 0 ? "SectionSafeAccent" : "SectionConfirmAccent";

    /// <summary>
    /// 分区容器背景键。两个区域用不同背景层级，让人一眼看出是两块独立区域。
    /// </summary>
    public string SurfaceKey => RiskTier == 0 ? "SectionSafeSurface" : "SectionConfirmSurface";

    /// <summary>展开箭头：折叠/展开一眼可辨（不靠颜色）。</summary>
    public string Chevron => IsExpanded ? "▾" : "▸";

    /// <summary>
    /// 右侧次要统计：「8 类 · 82 个位置 · 约 131 GB」。
    /// 空间是估算，不表示整组都能放心删。
    /// </summary>
    public string HeaderStats => Loc.SectionStats(RowCount, LocationCount, SizeText);

    /// <summary>这一组里已选中的项数（用于折叠时的提示）。</summary>
    public int SelectedCount => Rows.Sum(r => r.SelectedCount);

    /// <summary>已选项分布在多少个位置里。</summary>
    public int SelectedLocationCount => Rows.Sum(r => r.Locations.Count(l => l.SelectedCount > 0));

    /// <summary>折叠时若仍有已选项，必须显示提示，否则用户不知道选了什么。</summary>
    public bool HasCollapsedSelection => !IsExpanded && SelectedCount > 0;

    public string CollapsedSelectionNote => Loc.CollapsedSelected(SelectedCount, SelectedLocationCount);

    /// <summary>勾选后刷新分区标题（不重算，只重新求值）。</summary>
    public void RaiseHeaderChanged()
    {
        OnPropertyChanged(nameof(HeaderStats));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedLocationCount));
        OnPropertyChanged(nameof(HasCollapsedSelection));
        OnPropertyChanged(nameof(CollapsedSelectionNote));
    }

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// 按风险档把用途分成两个分区。建议清理默认展开，需要你确认默认折叠 ——
    /// 这样「需确认」的项目不会被低风险分区的展开状态顺手带进来。
    /// </summary>
    public static List<CleanPurposeSection> Build(IReadOnlyList<CleanPurposeNode> purposes)
    {
        var sections = new List<CleanPurposeSection>(2);
        foreach (var tier in new[] { 0, 1 })
        {
            var rows = purposes.Where(p => p.RiskTier == tier).ToList();
            if (rows.Count == 0) continue;
            sections.Add(new CleanPurposeSection
            {
                RiskTier = tier,
                Title = tier == 0 ? Loc.LayerSafe : Loc.LayerConfirm,
                Rows = rows,
                LocationCount = rows.Sum(x => x.LocationCount + x.LocationsNotShown),
                FileCount = rows.Sum(x => x.FileCount),
                Bytes = rows.Sum(x => x.Bytes),
                IsExpanded = tier == 0,
            });
        }
        return sections;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 第一层：用途分类。首页一行 = 一个 (用途, 风险档)。
/// 风险档必须拆开，否则「建议清理」的勾选会把「需要你确认」的项目一起带走。
/// </summary>
public sealed class CleanPurposeNode : CleanGroupNodeBase
{
    public CleanPurpose Purpose { get; init; }
    public int RiskTier { get; init; }
    public string PurposeName { get; init; } = "";
    public string Impact { get; init; } = "";

    /// <summary>类型列：只描述这是什么，不下安全结论（和列表里的口径一致）。</summary>
    public string RiskText => RiskTier == 0 ? Loc.LayerSafe : Loc.LayerConfirm;

    /// <summary>该用途下的清理位置。</summary>
    public IReadOnlyList<CleanLocationNode> Locations { get; internal set; } = Array.Empty<CleanLocationNode>();

    /// <summary>**位置数量** —— 不是分类数，也不是风险组数。</summary>
    public int LocationCount => Locations.Count;

    /// <summary>
    /// 因显示上限未建行的位置数。候选和空间统计**不受影响**（始终按全量算），
    /// 只是没给每个位置都建一行 —— 界面要如实说明，不能让人以为只有这些位置。
    /// </summary>
    public int LocationsNotShown { get; internal set; }

    public bool HasHiddenLocations => LocationsNotShown > 0;

    public string LocationsNotShownText => Loc.LocationsNotShown(LocationsNotShown);

    /// <summary>首页一行显示：位置数（次要信息）。</summary>
    public string RowLocationText => LocationCountText;

    /// <summary>首页一行显示：用途 · 位置数 · 文件数（次要）· 空间。</summary>
    public string LocationCountText => IsEn
        ? $"{LocationCount:N0} location(s)"
        : $"{LocationCount:N0} 个位置";
    public string FileCountText => Loc.LocationFiles(FileCount);

    private static bool IsEn => Loc.IsEn;

    /// <summary>位置数量未知（还没展开算过）时不要谎报 0。</summary>
    public bool LocationsCounted { get; internal set; }
}

/// <summary>
/// 分层结果：完整候选 → 用途 → 位置。
/// <see cref="AllItems"/> 是**完整候选集**，一行不丢；分页只影响明细显示。
/// </summary>
public sealed class CleanLayeredResult
{
    public required IReadOnlyList<CleanPurposeNode> Purposes { get; init; }
    /// <summary>完整候选（去重后），候选总数以它为准。</summary>
    public required IReadOnlyList<CleanItem> AllItems { get; init; }

    public int TotalLocations { get; init; }
    public int TotalFiles => AllItems.Count;
    public long TotalBytes { get; init; }
    /// <summary>规则允许删除的候选总数。</summary>
    public int SelectableTotal { get; init; }
    /// <summary>空间口径为估算的条目数（不编造精确值）。</summary>
    public int EstimatedCount { get; init; }
    /// <summary>被同组父目录覆盖、未重复计数的条目数。</summary>
    public int OverlapCount { get; init; }
    /// <summary>误报重复/重叠被剔除的条数（同一路径重复命中）。</summary>
    public int DuplicateHitsRemoved { get; init; }

    public static readonly CleanLayeredResult Empty = new()
    {
        Purposes = Array.Empty<CleanPurposeNode>(),
        AllItems = Array.Empty<CleanItem>(),
    };

    /// <summary>当前选中的候选（= 实际执行集合的来源，明细/位置/用途共用一份）。</summary>
    public IEnumerable<CleanItem> SelectedItems => AllItems.Where(x => x.Selected && x.CanDelete);

    public long SelectedBytes => SelectedItems.Sum(x => x.Size);
}
