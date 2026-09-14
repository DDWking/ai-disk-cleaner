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
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnExpandedChanged();
        }
    }

    /// <summary>
    /// 展开状态变化后的派生通知。默认什么都不做；
    /// 分类行用它把「就地展开的位置集合」通知给绑定（见 <see cref="CleanPurposeNode.VisibleLocations"/>）。
    /// </summary>
    protected virtual void OnExpandedChanged() { }

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
    /// 不再挂「用途待确认」。点过 AI 且给出了用途时，改显示该用途。
    /// </summary>
    public string RowSubText
    {
        get
        {
            var bits = new List<string>();
            string parent = ParentHint;
            if (parent.Length > 0) bits.Add(parent);
            bits.Add(Loc.LocationFiles(FileCount));
            string aiPurpose = ItemAiPurposeText();
            if (aiPurpose.Length > 0) bits.Add(aiPurpose);
            return string.Join(" · ", bits);
        }
    }

    /// <summary>用户点过这一项的 AI 且给出了用途时的短文本；没点过或过期则为空。</summary>
    string ItemAiPurposeText()
    {
        var v = _ai;
        if (v == null || v.IsStale || v.Status != ItemAiStatus.Done) return "";
        return v.Result?.Purpose?.Trim() ?? "";
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
                _ai.PropertyChanged += OnItemAiChanged;
                OnPropertyChanged(nameof(Ai));
            }
            return _ai;
        }
    }

    private ItemAiView? _ai;

    /// <summary>已经建出来的 AI 视图；没建过就是 null（不会顺手创建）。</summary>
    public ItemAiView? ExistingAi => _ai;

    void OnItemAiChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ItemAiView.Result)
            or nameof(ItemAiView.Status)
            or nameof(ItemAiView.IsStale)
            or nameof(ItemAiView.HasResult)
            or null))
            return;
        OnPropertyChanged(nameof(RowSubText));
    }

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

    // ==================== 行内展开：直接看这一处的候选文件 ====================
    //
    // 需求背景（实拍反馈）：以前要看具体文件，必须先进右侧明细面板、或者先跑一次
    // 单项 AI 再从 AI 结果里点「查看文件」—— 等于每看一个文件夹都要绕一圈。
    // 现在点位置行**就地展开**这一处的候选文件（文件名 / 大小 / 修改时间 / 勾选），
    // 不需要 AI，也不离开当前界面。
    //
    // 三条硬约束：
    //   1) 惰性：**只在这一处被展开时**才建列表，而且只取前 <see cref="InlineFileLimit"/> 条，
    //      按占用降序 —— 不会为了一次展开把全盘文件都加载进来；
    //   2) 复用同一份模型：列表里就是本位置真正的 <see cref="CleanItem"/> 实例，
    //      勾选直接写回同一份状态，不存在「行内一套、明细又一套」；
    //   3) 不加任何删除能力：这里只是看与勾，执行仍然只从底部「清理已选项目」走。

    /// <summary>
    /// 行内一次最多列出多少条候选。
    /// **刻意不设成几百条**：行内的这一块不做嵌套滚动（那会和外层虚拟化列表抢滚轮），
    /// 所以它只负责「一眼看清这一处主要是些什么」；多出来的只报「另有 N 项」，
    /// 要看真实目录用行上的文件夹图标。
    /// </summary>
    public const int InlineFileLimit = 30;

    private bool _filesOpen;
    private bool _filesLoaded;
    private IReadOnlyList<CleanItem> _visibleFiles = Array.Empty<CleanItem>();

    /// <summary>这一处的候选文件是否已就地展开。</summary>
    public bool IsFilesOpen
    {
        get => _filesOpen;
        private set
        {
            if (_filesOpen == value) return;
            _filesOpen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilesGlyphKey));
            OnPropertyChanged(nameof(FilesToggleText));
        }
    }

    /// <summary>有没有东西可看（有候选项就允许展开）。</summary>
    public bool CanShowFiles => FileCount > 0;

    /// <summary>
    /// 行内的候选文件。
    /// **展开之前恒为空**：绑定不会顺手把每一行的候选都排序建一遍
    /// （那等于为了「可能被展开」把整页的候选都加载进来）。只有真的展开过才返回内容。
    /// </summary>
    public IReadOnlyList<CleanItem> VisibleFiles => _visibleFiles;

    /// <summary>因上限没有列出的条数。</summary>
    public int HiddenFileCount => Math.Max(0, FileCount - _visibleFiles.Count);

    public bool HasHiddenFiles => _filesLoaded && HiddenFileCount > 0;

    /// <summary>列表说明：共几项、列了几项；超出上限时追加「另有 N 项」。</summary>
    public string FilesNote
    {
        get
        {
            if (!_filesLoaded) return "";
            string head = Loc.InlineFilesCount(_visibleFiles.Count, FileCount);
            return HasHiddenFiles ? head + " · " + Loc.InlineFilesHidden(HiddenFileCount) : head;
        }
    }

    public string FilesGlyphKey => _filesOpen ? "IconChevronDown" : "IconChevronRight";
    public string FilesToggleText => _filesOpen ? Loc.CollapseFilesAction : Loc.ViewFilesAction;
    public string NameHeaderText => Loc.FileNameHeader;
    public string ModifiedHeaderText => Loc.FileModifiedHeader;
    public string SizeHeaderText => Loc.FileSizeHeader;
    public string FilesScopeText => Loc.InlineFilesScope;
    public string FullListButtonText => Loc.OpenFullListAction;
    public string FullListTipText => Loc.OpenFullListTip;

    /// <summary>展开 / 收起行内文件列表（展开是惰性的，只在第一次真的建列表）。</summary>
    public void ToggleFiles() => SetFilesOpen(!_filesOpen);

    public void SetFilesOpen(bool open)
    {
        if (open && !_filesLoaded) EnsureFiles();
        IsFilesOpen = open;
        OnPropertyChanged(nameof(FilesNote));
        OnPropertyChanged(nameof(HasHiddenFiles));
        OnPropertyChanged(nameof(VisibleFiles));
        RaiseSelectionChanged();
    }

    /// <summary>
    /// 惰性建行。按占用降序取前 <see cref="InlineFileLimit"/> 条 ——
    /// 用户最想先看到的是大的那些；剩下的如实计数，不做「加载更多」的假动作。
    /// </summary>
    private void EnsureFiles()
    {
        _filesLoaded = true;
        if (Items.Count == 0)
        {
            _visibleFiles = Array.Empty<CleanItem>();
            return;
        }
        _visibleFiles = Items.Count <= InlineFileLimit
            ? Items.OrderByDescending(x => x.Size).ToList()
            : Items.OrderByDescending(x => x.Size).Take(InlineFileLimit).ToList();
    }
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

    /// <summary>
    /// 分区标题的可访问名称 / 悬停补充：标题 + 右侧统计一起读出来。
    /// 折叠状态下统计是唯一说明「里面有多少」的信息，不能只让看得见的用户拿到。
    /// </summary>
    public string HeaderAccessibleName => HeaderStats.Length > 0
        ? HeaderTitle + " · " + HeaderStats
        : HeaderTitle;

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

    // ==================== 就地展开：分类 → 位置（不换视图） ====================
    //
    // 需求背景（2.11 实拍反馈）：以前点分类会**换掉整个主内容**进入「位置页」，
    // 位置页再点一次才看文件 —— 滚动位置和上下文都丢了。现在分类行就地展开，
    // 位置行继续就地展开文件，整条链路都在同一屏里完成。
    //
    // 硬约束：
    //   1) 收起时 <see cref="VisibleLocations"/> 返回空 —— 折叠的分类不会提前把
    //      几百个位置行实例化出来（首屏只建真正可见的东西）；
    //   2) 渲染的还是同一批 <see cref="CleanLocationNode"/> 实例，
    //      勾选状态挂在实例上，反复展开/收起来回切换一项都不会丢。

    /// <summary>展开时这一类的清理位置；**收起时为空**（惰性渲染的关键）。</summary>
    public IReadOnlyList<CleanLocationNode> VisibleLocations =>
        IsExpanded ? Locations : Array.Empty<CleanLocationNode>();

    /// <summary>展开箭头的悬停 / 可访问文案：说清点下去是展开还是收起。</summary>
    public string ExpandTip => IsExpanded ? Loc.CollapseLocations : Loc.ExpandLocations;

    protected override void OnExpandedChanged()
    {
        base.OnExpandedChanged();
        OnPropertyChanged(nameof(VisibleLocations));
        OnPropertyChanged(nameof(ExpandTip));
    }
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
