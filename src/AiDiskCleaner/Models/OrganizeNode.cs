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
    }

    /// <summary>对应的真实目录条目（容量、子节点都从这里来）。</summary>
    public FileEntry Dir { get; }
    /// <summary>稳定标识 = 完整路径 + 扫描代次。结果一律按它关联。</summary>
    public FolderId Id { get; }
    /// <summary>0 = 顶层对象。</summary>
    public int Depth { get; }
    /// <summary>相对扫描根的路径（脱敏展示用；完整路径在悬停与详情里）。</summary>
    public string RelativePath { get; }

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

    /// <summary>这一行唯一那个动作的文案：没结论 = 识别；有结论的收纳目录 = 深入识别。</summary>
    public string ActionText => !HasConclusion
        ? Loc.OrganizeIdentifyOne
        : (CanExpand ? Loc.OrganizeDeepen : "");

    public bool HasAction => ActionText.Length > 0;

    /// <summary>系统解析出来的识别入口（不是清理结论）。</summary>
    public bool IsSystemEntry { get; init; }

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
        private set { if (_isExpanded != value) { _isExpanded = value; Raise(nameof(IsExpanded)); Raise(nameof(ExpandGlyphKey)); Raise(nameof(ExpandTip)); } }
    }

    /// <summary>子目录比预算多 → 明确说出来，不假装列全了。</summary>
    public bool ChildSetTruncated => _childSetTruncated;
    public int HiddenChildCount => Math.Max(0, _childTotal - _children.Count);

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
        Raise(nameof(ActionText));
        Raise(nameof(HasAction));
    }

    /// <summary>写入一次展开的结果。</summary>
    public void SetChildren(IReadOnlyList<OrganizeNode> children, bool truncated, int total)
    {
        _children.Clear();
        _children.AddRange(children);
        _childrenLoaded = true;
        _childSetTruncated = truncated;
        _childTotal = total;
        IsExpanded = true;
        Raise(nameof(Children));
        Raise(nameof(ChildBudgetNote));
        Raise(nameof(HasChildBudgetNote));
        Raise(nameof(HiddenChildCount));
        Raise(nameof(ActionText));
        Raise(nameof(HasAction));
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
        if (r.Kind != FolderKind.Unknown) _kind = r.Kind;
        // 有结论就落回结论状态；没结论就是「待确认」，绝不显示成已识别。
        _override = null;
        if (!HasConclusion) _override = PurposeState.Unrecognized;
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
        Raise(nameof(ActionText));
        Raise(nameof(HasAction));
        Raise(nameof(Kind));
    }

    void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
