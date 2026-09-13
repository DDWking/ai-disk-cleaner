using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models;

/// <summary>
/// 「文件夹整理」页里的一个文件夹对象。
///
/// 它**只描述用途与容量**，没有 Risk / CanDelete / Selected 这类字段 ——
/// 用途识别绝不参与清理资格，也不改任何选择状态。对象与真实目录一一对应
/// （<see cref="FullPath"/> 就是磁盘上的那个目录），这里不建虚拟分类树，
/// 也不移动任何文件。
/// </summary>
public sealed class OrganizeNode : INotifyPropertyChanged
{
    public OrganizeNode(FileEntry dir, FolderId id, int depth, string relativePath)
    {
        Dir = dir;
        Id = id;
        Depth = depth;
        RelativePath = relativePath;
        Ai = new ItemAiView { ScopeKey = dir.FullPath };
    }

    /// <summary>
    /// 这一项的**单项 AI 分析**状态（用户点了行里的 AI 按钮才有内容）。
    ///
    /// 语义与清理页逐项分析完全一致：这是什么 / 删除可能影响什么 / 依据 / 缺什么。
    /// 它是**展示态**，不参与 Risk / CanDelete / Selected，也绝不自动勾选或删除。
    /// 一个对象 = 一次有限的摘要请求；重复点击走缓存或去重，不会重复发。
    /// </summary>
    public ItemAiView Ai { get; }

    /// <summary>对应的真实目录条目（容量、子节点都从这里来）。</summary>
    public FileEntry Dir { get; }
    /// <summary>稳定标识 = 完整路径 + 扫描代次。结果一律按它关联。</summary>
    public FolderId Id { get; }
    /// <summary>0 = 顶层对象。</summary>
    public int Depth { get; }
    /// <summary>相对扫描根的路径（脱敏展示用；完整路径在悬停与详情里）。</summary>
    public string RelativePath { get; }

    // ---------------- 认识层级（起点：盘符直接子目录 / 系统入口） ----------------

    /// <summary>
    /// 第几级对象：盘符直接子目录与系统入口 = 1，它们的直接子目录 = 2，三级及更深 = 3+。
    /// 只限制**展示与 AI 调用**，不影响容量统计，也不影响任何清理字段。
    /// </summary>
    public int Level { get; private set; } = OrganizeLevelPolicy.AutoLevel;

    /// <summary>是不是「扫描后自动批量识别」的范围（一、二级）。</summary>
    public bool IsAutoLevel => Level >= OrganizeLevelPolicy.AutoLevel
                               && Level <= OrganizeLevelPolicy.AutoChildLevel;

    /// <summary>三级及更深：**不自动调用 AI**，只由用户点「识别当前文件夹」。</summary>
    public bool IsDeepLevel => Level > OrganizeLevelPolicy.AutoChildLevel;

    /// <summary>写一次层级（由整理工作区在创建子对象时调用，之后不再变）。</summary>
    public void SetLevel(int level)
    {
        if (level <= 0 || level == Level) return;
        Level = level;
        Raise(nameof(Level));
        Raise(nameof(IsAutoLevel));
        Raise(nameof(IsDeepLevel));
        Raise(nameof(LevelText));
    }

    /// <summary>层级的可读标注（只给悬停/调试用，不抢主视觉）。</summary>
    public string LevelText => Level <= OrganizeLevelPolicy.AutoChildLevel
        ? Loc.OrganizeLevelAuto(Level)
        : Loc.OrganizeLevelDeep(Level);

    public string Name => Dir.Name;
    public string FullPath => Dir.FullPath;
    public long Size => Dir.Size;
    public string SizeText => FileEntry.FormatSize(Dir.Size);
    public int FileCount => Dir.FileCount;
    public int FolderCount => Dir.FolderCount;

    /// <summary>这个对象在树里的缩进（像素）。不引用 WPF 类型，便于离线测试。</summary>
    public double IndentWidth => Depth * 18.0;

    // ---------------- 识别结论（**来源如实**） ----------------

    private string _purposeName = "";
    private string _purposeCategory = "";
    private string _basis = "";
    private PurposeSource _source = PurposeSource.None;
    private bool _needsConfirm;
    private FolderKind _kind = FolderKind.Unknown;
    private PurposeState? _override;

    public string PurposeName => _purposeName;
    public string PurposeCategory => _purposeCategory;
    public string Basis => _basis;
    public PurposeSource Source => _source;
    public bool NeedsConfirm => _needsConfirm;
    public FolderKind Kind => _kind;

    /// <summary>有结论才算「已识别」—— 没有结论时状态不允许是成功。</summary>
    public bool HasConclusion => _purposeName.Length > 0;

    /// <summary>
    /// 界面状态。排队/处理中只由流程写入（<see cref="SetState"/>），
    /// 所以**排队时不可能显示 AI 成功结论**。
    /// </summary>
    public PurposeState State => _override
        ?? (_source switch
        {
            PurposeSource.None => PurposeState.Unrecognized,
            _ => _needsConfirm ? PurposeState.NeedsConfirm : PurposeState.Recognized,
        });

    /// <summary>本地识别 / AI 推测 / 你确认的。没有结论时为空。</summary>
    public string SourceText => _source switch
    {
        PurposeSource.Local => Loc.PurposeFromLocal,
        PurposeSource.Ai => Loc.PurposeFromAi,
        PurposeSource.User => Loc.PurposeFromUser,
        _ => "",
    };

    /// <summary>主行：用途名，或状态词（未识别 / 待确认 / 排队中…）。</summary>
    public string PurposeText
    {
        get
        {
            if (State is PurposeState.Queued) return Loc.PurposeQueued;
            if (State is PurposeState.Running) return Loc.PurposeRunning;
            if (State is PurposeState.Failed) return Loc.PurposeFailed;
            if (_purposeName.Length > 0) return _purposeName;
            if (_needsConfirm) return Loc.PurposeUnclear;
            return Loc.PurposeUnrecognized;
        }
    }

    /// <summary>次行：来源 · 依据，或状态说明。**不把本地判断说成 AI。**</summary>
    public string PurposeDetail
    {
        get
        {
            if (State is PurposeState.Queued or PurposeState.Running)
                return Loc.OrganizeWaitNoResult;
            if (State is PurposeState.Failed) return Loc.OrganizeRetryHint;
            if (_purposeName.Length == 0)
                return _needsConfirm ? Loc.PurposeNeedsConfirm : Loc.PurposeIdentify;
            string s = _source == PurposeSource.None ? "" : SourceText;
            if (_basis.Length > 0) s = s.Length > 0 ? s + " · " + _basis : _basis;
            if (_needsConfirm) s = s.Length > 0 ? s + " · " + Loc.PurposeNeedsConfirm : Loc.PurposeNeedsConfirm;
            return s;
        }
    }

    /// <summary>只有「有结论」的行才用正常文字色；没结论一律弱化。</summary>
    public bool IsResolved => HasConclusion && !_needsConfirm;

    /// <summary>排到「待确认」筛选里的行。</summary>
    public bool IsPending => !HasConclusion || _needsConfirm || State == PurposeState.Failed;

    /// <summary>
    /// 行内「展开 / 收起」文案。
    ///
    /// 行里**不再有**逐项「识别」按钮 —— 识别是一、二级自动完成的，
    /// 三级及更深的识别入口是「当前文件夹」工作区那一个主操作。
    /// 这里只留导航（展开子文件夹），避免用户以为必须手动逐个识别。
    /// </summary>
    public string ToggleText => !CanExpand ? "" : (_isExpanded ? Loc.OrganizeCollapse : Loc.OrganizeExpand);

    public bool HasToggleText => ToggleText.Length > 0;

    /// <summary>系统解析出来的识别入口（不是清理结论）。</summary>
    public bool IsSystemEntry { get; init; }

    /// <summary>入口的短标签（入口本身只是导航/容器，不是清理结论）。</summary>
    public string SystemEntryTag => IsSystemEntry ? Loc.OrganizeEntryPointTag : "";

    /// <summary>顶层对象（首屏只显示这些）。</summary>
    public bool IsRoot => Depth == 0;

    // ---------------- 真实摘要证据（行内详情只显示这些，不编造） ----------------

    private int _directFolderCount;
    private IReadOnlyList<string> _sampleNames = Array.Empty<string>();

    /// <summary>
    /// 写入这次判定用到的**真实摘要证据**（直接子目录数、文件数、代表文件名）。
    /// 只用于把「为什么这么判断」说清楚，不参与任何清理字段。
    /// </summary>
    public void SetEvidence(FolderSummary sum)
    {
        _directFolderCount = sum.DirectFolderCount;
        // 代表文件名最多留 3 个，行内详情不该变成一份清单
        _sampleNames = sum.SampleNames.Count > 3 ? sum.SampleNames.Take(3).ToList() : sum.SampleNames;
        Raise(nameof(DetailText));
    }

    // ---------------- 行内详情（点击用途展开；只用真实摘要证据） ----------------

    private bool _detailOpen;

    /// <summary>这一行的说明是否展开。</summary>
    public bool IsDetailOpen => _detailOpen;

    /// <summary>展开 / 收起说明（只改展示，不动结论、不动任何清理字段）。</summary>
    public void SetDetailOpen(bool open)
    {
        if (_detailOpen == open) return;
        _detailOpen = open;
        Raise(nameof(IsDetailOpen));
        Raise(nameof(DetailTip));
    }

    public void ToggleDetail() => SetDetailOpen(!_detailOpen);

    public string DetailTip => _detailOpen ? Loc.OrganizeDetailHide : Loc.OrganizeDetailTip;

    /// <summary>
    /// 行内详情：「这是什么 / 为什么这么判断 / 判断来源」。
    /// **只用真实摘要证据**（直接子目录数、文件数、类型分布、代表文件名、判定依据），
    /// 证据不足就说未知 —— 不编造产品名，也不写「命中签名」这类开发术语。
    /// </summary>
    public string DetailText
    {
        get
        {
            if (!HasConclusion)
                return Loc.OrganizeDetailWhat + Loc.PurposeUnrecognized
                    + "\n" + Loc.OrganizeDetailWhy + Loc.OrganizeDetailNoEvidence;

            var sb = new System.Text.StringBuilder();
            sb.Append(Loc.OrganizeDetailWhat).Append(PurposeText);
            sb.Append('\n').Append(Loc.OrganizeDetailWhy);
            var bits = new List<string>();
            if (_directFolderCount > 0 || FileCount > 0)
                bits.Add(Loc.OrganizeDetailKind(_directFolderCount, FileCount, ""));
            if (_sampleNames.Count > 0)
                bits.Add(Loc.OrganizeDetailSamples(string.Join(", ", _sampleNames)));
            if (_basis.Length > 0) bits.Add(_basis);
            sb.Append(bits.Count > 0 ? string.Join(" · ", bits) : Loc.OrganizeDetailNoEvidence);
            string src = SourceText;
            if (src.Length > 0) sb.Append('\n').Append(Loc.OrganizeDetailSource(src));
            return sb.ToString();
        }
    }

    /// <summary>平台游戏库这类「里面还有独立对象」的容器 —— 保留往里找游戏的入口。</summary>
    public bool IsPlatformContainer { get; init; }

    /// <summary>平台容器的短标签（有才显示）。</summary>
    public string PlatformTag => IsPlatformContainer ? Loc.OrganizePlatformTag : "";

    // ---------------- 展开 / 子对象 ----------------

    private readonly List<OrganizeNode> _children = new();
    private bool _childrenLoaded;
    private bool _isExpanded;
    private bool _childSetTruncated;
    private int _childTotal;

    public IReadOnlyList<OrganizeNode> Children => _children;
    public bool ChildrenLoaded => _childrenLoaded;

    /// <summary>还能不能再往下看（重解析点/文件组一律不看）。</summary>
    public bool CanExpand => FolderPurposeRules.CanDescend(Dir) && ChildDirCount > 0;

    /// <summary>直接子目录数（含因预算没列出来的）。</summary>
    public int ChildDirCount { get; private set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            Raise(nameof(IsExpanded));
            Raise(nameof(ExpandGlyphKey));
            Raise(nameof(ExpandTip));
            Raise(nameof(ToggleText));
            Raise(nameof(HasToggleText));
        }
    }

    /// <summary>子目录比预算多 → 明确说出来，不假装列全了。</summary>
    public bool ChildSetTruncated => _childSetTruncated;
    public int HiddenChildCount => Math.Max(0, _childTotal - _children.Count);

    /// <summary>
    /// 因为行数上限**没有材料化、也没有结论**的直接子目录数。
    /// 这些条目既不在列表里，也**绝不算「已识别」**；页头会如实报出总数。
    /// </summary>
    public int UnlistedChildCount { get; private set; }

    /// <summary>有没列出来的子目录时，这一行要显示「已列出 X / 共 Y / 还有 N 个未列出（未识别）」。</summary>
    public string UnlistedNote => UnlistedChildCount > 0
        ? Loc.OrganizeUnlistedNote(_children.Count, _childTotal, UnlistedChildCount)
        : "";

    public bool HasUnlistedNote => UnlistedChildCount > 0;

    public string ExpandGlyphKey => _isExpanded ? "IconChevronDown" : "IconChevronRight";
    public string ExpandTip => _isExpanded ? Loc.OrganizeCollapse : Loc.OrganizeExpand;

    /// <summary>展开提示里带上没列出来的数量（有才显示）。</summary>
    public string ChildBudgetNote => _childSetTruncated
        ? Loc.OrganizeChildBudgetNote(_children.Count, _childTotal)
        : "";

    public bool HasChildBudgetNote => _childSetTruncated;

    /// <summary>写入直接子目录总数（在材料化之前调用，用于「还能展开」判定）。</summary>
    public void SetChildDirCount(int count)
    {
        ChildDirCount = count;
        Raise(nameof(CanExpand));
        Raise(nameof(ToggleText));
        Raise(nameof(HasToggleText));
    }

    /// <summary>写入一次展开的结果。**默认不展开**：首屏只显示根的一级。</summary>
    public void SetChildren(IReadOnlyList<OrganizeNode> children, bool truncated, int total)
        => SetChildren(children, truncated, total, Math.Max(0, total - children.Count), autoExpand: false);

    /// <summary>
    /// 写入一次材料化的结果，并如实带上「没列出来、也没有结论」的数量。
    ///
    /// <paramref name="autoExpand"/> 默认 **false**：后台可以把子对象材料化好，
    /// 但**首屏可见树只展示根的一级**，展开与否完全由用户决定。
    /// </summary>
    public void SetChildren(
        IReadOnlyList<OrganizeNode> children, bool truncated, int total, int unlisted,
        bool autoExpand = false)
    {
        _children.Clear();
        _children.AddRange(children);
        _childrenLoaded = true;
        _childSetTruncated = truncated;
        _childTotal = total;
        UnlistedChildCount = Math.Max(0, unlisted);
        if (autoExpand) IsExpanded = true;
        Raise(nameof(Children));
        Raise(nameof(ChildBudgetNote));
        Raise(nameof(HasChildBudgetNote));
        Raise(nameof(HiddenChildCount));
        Raise(nameof(UnlistedChildCount));
        Raise(nameof(UnlistedNote));
        Raise(nameof(HasUnlistedNote));
        Raise(nameof(ToggleText));
        Raise(nameof(HasToggleText));
    }

    /// <summary>收起：**保留已材料化的子对象**（结果不丢，再展开不重复请求）。</summary>
    public void Collapse() => IsExpanded = false;

    /// <summary>重新展开（子对象已经材料化过，直接显示，不再请求）。</summary>
    public void Expand() => IsExpanded = true;

    /// <summary>清掉「排队/处理中/失败」这类流程态，落回结论本身的状态。</summary>
    public void ClearState()
    {
        _override = null;
        Raise(nameof(State));
        Raise(nameof(PurposeText));
        Raise(nameof(PurposeDetail));
        Raise(nameof(IsPending));
    }

    // ---------------- 状态写入（都由识别流程调用） ----------------

    /// <summary>排队/处理中/失败：只动展示状态，不写结论。</summary>
    public void SetState(PurposeState state)
    {
        _override = state;
        Raise(nameof(State));
        Raise(nameof(PurposeText));
        Raise(nameof(PurposeDetail));
        Raise(nameof(IsPending));
    }

    /// <summary>写入一次识别结论。没有结论时**不覆盖**，只把状态说清楚。</summary>
    public void Apply(FolderPurposeResult r)
    {
        _purposeName = r.HasConclusion ? r.PurposeName : "";
        _purposeCategory = r.HasConclusion ? r.Category : "";
        _basis = r.HasConclusion ? r.Basis : "";
        _source = r.HasConclusion ? r.Source : PurposeSource.None;
        _needsConfirm = r.HasConclusion ? r.NeedsConfirm : true;
        // 目录性质只由本地结构判定决定：**用户纠正只改用途结论**，
        // 不能因为纠成一个「具体东西」就把这个集合的子项藏起来 / 停止展开。
        if (r.Source != PurposeSource.User && r.Kind != FolderKind.Unknown) _kind = r.Kind;
        // 有结论就落回结论状态；没有结论时：
        //   - 这次**失败/超时** ⇒ Failed（页头会把它算进「失败」，可统一重试）
        //   - 只是判不出来   ⇒ Unrecognized + 待确认
        // 两种情况都绝不显示成「已识别」。
        _override = null;
        if (!HasConclusion) _override = r.Failed ? PurposeState.Failed : PurposeState.Unrecognized;
        RaiseAll();
    }

    /// <summary>本地规则给的目录性质（结论或没有结论都要更新，下钻判定依赖它）。</summary>
    public void SetKind(FolderKind kind)
    {
        if (kind == FolderKind.Unknown) return;
        _kind = kind;
        Raise(nameof(Kind));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void RaiseAll()
    {
        Raise(nameof(PurposeName));
        Raise(nameof(PurposeCategory));
        Raise(nameof(Basis));
        Raise(nameof(Source));
        Raise(nameof(SourceText));
        Raise(nameof(NeedsConfirm));
        Raise(nameof(HasConclusion));
        Raise(nameof(State));
        Raise(nameof(PurposeText));
        Raise(nameof(PurposeDetail));
        Raise(nameof(IsResolved));
        Raise(nameof(IsPending));
        Raise(nameof(DetailText));
        Raise(nameof(Kind));
    }

    void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
