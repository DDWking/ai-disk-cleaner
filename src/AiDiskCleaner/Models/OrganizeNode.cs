using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models;

/// <summary>
/// 一个文件夹的内容形态。**这是为了回答「为什么这一行没有展开箭头」** ——
/// 以前没有箭头就是没有箭头，用户看不出是「真的空」「只有文件」「是个链接没扫」
/// 还是「只是没扫到」，而 0 KB 又把它们全都指成了「空」。
/// </summary>
public enum FolderContentKind
{
    /// <summary>还没判断出来。</summary>
    Unknown,
    /// <summary>有直接子文件夹 —— 可以展开（箭头是有效的）。</summary>
    ChildDirs,
    /// <summary>没有子文件夹，但有文件 —— **不伪造箭头**，给「查看文件」入口。</summary>
    FilesOnly,
    /// <summary>扫描结果里没有任何内容。说「空」必须限定在「这次扫描看到的」范围内。</summary>
    Empty,
    /// <summary>重解析点 / 链接：本次扫描**没有进去**，所以内容与占用都未知。</summary>
    NotScanned,
}

/// <summary>「只有文件」的那一类目录在行内列出来的一个文件（**只读展示，不带任何删除能力**）。</summary>
public sealed record OrganizeFileRow(string Name, string SizeText, string ModifiedText, string FullPath)
{
    public string Hint => FullPath.Length > 0 ? FullPath : Name;
}

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

    /// <summary>
    /// 容量那一格。
    /// **未扫描的（链接 / 重解析点）绝不显示 0 KB** ——「没进去看」和「里面是空的」
    /// 是两回事，0 KB 会把后者说成前者。空目录也换成明确的「扫描无内容」，不用 0 暗示。
    /// </summary>
    public string SizeText => ContentKind switch
    {
        FolderContentKind.NotScanned => Loc.OrganizeSizeNotScanned,
        FolderContentKind.Empty => Loc.OrganizeSizeEmpty,
        _ => FileEntry.FormatSize(Dir.Size),
    };

    public int FileCount => Dir.FileCount;
    public int FolderCount => Dir.FolderCount;

    // ==================== 内容形态：解释「为什么没有展开箭头」 ====================

    private bool _directFileCountKnown;
    private int _directFileCount;

    /// <summary>直接子**文件**数（不是递归的 FileCount）。</summary>
    public int DirectFileCount
    {
        get
        {
            if (!_directFileCountKnown)
            {
                int n = 0;
                foreach (var c in Dir.ChildList) if (!c.IsDirectory) n++;
                _directFileCount = n;
                _directFileCountKnown = true;
            }
            return _directFileCount;
        }
    }

    /// <summary>
    /// 内容形态。判定只看**扫描树里真实存在的东西**，不猜：
    /// 链接一律 NotScanned；有直接子目录 ⇒ 可展开；没有子目录但有文件 ⇒ FilesOnly；
    /// 两样都没有 ⇒ Empty（文案限定为「这次扫描里没有内容」）。
    /// </summary>
    public FolderContentKind ContentKind
    {
        get
        {
            if (!FolderPurposeRules.CanDescend(Dir)) return FolderContentKind.NotScanned;
            if (ChildDirCount > 0) return FolderContentKind.ChildDirs;
            if (DirectFileCount > 0) return FolderContentKind.FilesOnly;
            return FolderContentKind.Empty;
        }
    }

    /// <summary>「只有文件」的行要有一个**有效**的查看入口，而不是伪造一个展开箭头。</summary>
    public bool CanViewFiles => ContentKind == FolderContentKind.FilesOnly;

    /// <summary>
    /// 容量列下面那行短状态。有箭头或常态时不占位置（返回空串）。
    /// </summary>
    public string ContentStateText => ContentKind switch
    {
        FolderContentKind.NotScanned => Loc.OrganizeStateNotScanned,
        FolderContentKind.FilesOnly => Loc.OrganizeStateFilesOnly(DirectFileCount),
        FolderContentKind.Empty => Loc.OrganizeStateEmpty,
        _ => "",
    };

    public bool HasContentState => ContentStateText.Length > 0;

    /// <summary>悬停解释这一行为什么没有箭头 / 为什么写未知。</summary>
    public string ContentStateTip => ContentKind switch
    {
        FolderContentKind.NotScanned => Loc.OrganizeTipNotScanned,
        FolderContentKind.FilesOnly => Loc.OrganizeTipFilesOnly,
        FolderContentKind.Empty => Loc.OrganizeTipEmpty,
        _ => "",
    };

    // ==================== 「只有文件」的行内查看（只读、有界） ====================

    /// <summary>行内最多列几个文件（刻意有界，不做嵌套滚动）。</summary>
    public const int InlineFileLimit = 12;

    private bool _filesOpen;
    private bool _filesLoaded;
    private IReadOnlyList<OrganizeFileRow> _files = Array.Empty<OrganizeFileRow>();

    public bool IsFilesOpen
    {
        get => _filesOpen;
        private set
        {
            if (_filesOpen == value) return;
            _filesOpen = value;
            Raise(nameof(IsFilesOpen));
            Raise(nameof(FilesToggleText));
        }
    }

    /// <summary>
    /// 这个文件夹自己的文件。**展开之前恒为空**（惰性）：
    /// 绑定不会为了「可能被展开」就把整页目录的文件都排一遍。
    /// </summary>
    public IReadOnlyList<OrganizeFileRow> VisibleFiles => _files;

    public int HiddenFileCount => Math.Max(0, DirectFileCount - _files.Count);

    public string FilesNote
    {
        get
        {
            if (!_filesLoaded) return "";
            string head = Loc.OrganizeFilesCount(_files.Count, DirectFileCount);
            return HiddenFileCount > 0 ? head + " · " + Loc.OrganizeFilesMore(HiddenFileCount) : head;
        }
    }

    public string FilesToggleText => _filesOpen ? Loc.CollapseFilesAction : Loc.ViewFilesAction;

    public void ToggleFiles() => SetFilesOpen(!_filesOpen);

    public void SetFilesOpen(bool open)
    {
        if (open && !_filesLoaded) EnsureFiles();
        IsFilesOpen = open;
        Raise(nameof(FilesNote));
        Raise(nameof(VisibleFiles));
        Raise(nameof(HiddenFileCount));
    }

    /// <summary>
    /// 惰性建行：只取直接子文件里最大的前几个。
    /// **只读展示**：这里没有任何勾选、没有清理资格、也没有删除入口 ——
    /// 整理页绝不因为「看了一个文件夹」就获得清理能力。
    /// </summary>
    private void EnsureFiles()
    {
        _filesLoaded = true;
        var rows = new List<(long Size, OrganizeFileRow Row)>();
        foreach (var c in Dir.ChildList)
        {
            if (c.IsDirectory) continue;
            rows.Add((Math.Max(0, c.Allocated > 0 ? c.Allocated : c.Size), new OrganizeFileRow(
                c.Name ?? "",
                FileEntry.FormatSize(c.Size),
                c.Modified == DateTime.MinValue ? "" : c.Modified.ToString("yyyy-MM-dd HH:mm"),
                c.FullPath ?? "")));
        }
        // 按占用降序：用户最想先看到大的那几个
        _files = rows
            .OrderByDescending(x => x.Size)
            .ThenBy(x => x.Row.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(InlineFileLimit)
            .Select(x => x.Row)
            .ToList();
    }

    public string FilesScopeText => Loc.OrganizeFilesScope;

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
    private string _batchPurpose = "";

    public string PurposeName => _purposeName;
    public string PurposeCategory => _purposeCategory;
    public string Basis => _basis;

    /// <summary>
    /// 批量归类（结构化判定通道）给出的用途名。
    ///
    /// **刻意和逐项 AI 结果分开存**：逐项 AI 的结果还带「删除影响 / 依据 / 缺什么 / 建议档位」，
    /// 而批量只有一个用途。混进 <see cref="Ai"/> 那个槽位会让界面**假装逐项分析跑过**
    /// （行内「查看结果」按钮、状态行、建议档位都会被点亮）。
    ///
    /// 和逐项 AI 一样：**只影响展示** —— 不 Store、不改 Risk / CanDelete / Selected。
    /// </summary>
    public string BatchPurpose => _batchPurpose;

    /// <summary>
    /// 展示用来源。本地/用户结论优先；用户点过单项 AI（或跑过批量归类）且给出了用途时，
    /// 按 AI 推测展示 —— **不写进用途缓存、不改任何清理字段**。
    /// </summary>
    public PurposeSource Source => _purposeName.Length > 0
        ? _source
        : HasBatchPurpose ? PurposeSource.Ai : _source;
    public bool NeedsConfirm => _needsConfirm && !HasBatchPurpose;
    public FolderKind Kind => _kind;

    /// <summary>批量归类给过用途。只影响展示。</summary>
    bool HasBatchPurpose => _batchPurpose.Length > 0;

    private bool _batchAsked;

    /// <summary>
    /// 这一项**已经问过模型了**（不管最后有没有拿到结论）。
    ///
    /// 没有它自动识别会**无限追问**：拿不到结论的条目按 <see cref="HasConclusion"/> 看仍然「没结论」，
    /// 于是每次可见集合一变就把它重新问一遍，花的还是真金白银。
    /// 换扫描时节点是重建的，所以这个标记天然跟着换代复位。
    /// </summary>
    public bool BatchAsked => _batchAsked;

    /// <summary>标记「问过了」。只有批量服务在真的发出请求之后调用。</summary>
    public void MarkBatchAsked() => _batchAsked = true;

    /// <summary>
    /// 这一项还等着被归类：没有任何结论、**没问过**、不是失败态、路径非空。
    ///
    /// 这是批量归类的**唯一入选判据**，刻意放在节点自己身上 ——
    /// 这样离线回归只要连 <see cref="OrganizeNode"/> 就能断言它，
    /// 不必把整个批量服务（连带 AI 网关 / 设置 / 网络）拖进测试工程。
    /// </summary>
    public bool NeedsPurposeClassification =>
        !HasConclusion && !_batchAsked
        && State != PurposeState.Failed && !string.IsNullOrWhiteSpace(FullPath);

    /// <summary>有结论才算「已识别」—— 没有结论时状态不允许是成功。</summary>
    public bool HasConclusion => _purposeName.Length > 0 || HasBatchPurpose;

    private bool _batchUnsure;

    /// <summary>
    /// 这条是模型**说了但没把握**的（低于采纳阈值），不是它的定论。
    ///
    /// 官方对这种情况的说法是 *escalate to a human when confidence is low* ——
    /// **升级给人看，而不是丢掉**。丢掉的话界面只剩「未识别」，用户读到的信息量是零；
    /// 标一句「拿不准：可能是软件缓存」，他至少知道该往哪边怀疑。
    /// 这也是「看不出来太多」这个观感的一部分来源。
    /// </summary>
    public bool BatchPurposeUnsure => _batchUnsure && HasBatchPurpose;

    /// <summary>
    /// 批量归类写入用途。**唯一的写入口**，只改 <c>_batchPurpose</c> / <c>_batchUnsure</c>
    /// 并刷新展示属性；不碰 <c>_source</c> / <c>_needsConfirm</c> / <c>_kind</c>，
    /// 也不碰任何清理字段。
    /// </summary>
    /// <param name="unsure">
    /// 模型给了答案但没把握（低于阈值）。仍然写进去，只是标出来 —— 见 <see cref="BatchPurposeUnsure"/>。
    /// </param>
    public void SetBatchPurpose(string? purpose, bool unsure = false)
    {
        string next = purpose ?? "";
        if (_batchPurpose == next && _batchUnsure == unsure) return;
        _batchPurpose = next;
        _batchUnsure = unsure && next.Length > 0;
        // 沿用 Apply 的口径：有结论就落回结论状态；没有结论才是「未识别」。
        // 不清 _override 的话，之前 Apply 写下的 Unrecognized 会一直盖住新结论。
        _override = null;
        if (!HasConclusion) _override = PurposeState.Unrecognized;
        RaiseConclusionDisplay();
    }

    /// <summary>
    /// 界面状态。排队/处理中只由流程写入（<see cref="SetState"/>），
    /// 所以**排队时不可能显示 AI 成功结论**。
    /// </summary>
    public PurposeState State => _override
        ?? (Source switch
        {
            PurposeSource.None => PurposeState.Unrecognized,
            _ => NeedsConfirm ? PurposeState.NeedsConfirm : PurposeState.Recognized,
        });

    /// <summary>本地识别 / AI 推测 / 你确认的。没有结论时为空。</summary>
    public string SourceText => Source switch
    {
        PurposeSource.Local => Loc.PurposeFromLocal,
        PurposeSource.Ai => Loc.PurposeFromAi,
        PurposeSource.User => Loc.PurposeFromUser,
        _ => "",
    };

    /// <summary>主行：用途名，或状态词（未识别 / 待确认 / 排队中…）。识别过后替换未识别。</summary>
    public string PurposeText
    {
        get
        {
            if (State is PurposeState.Queued) return Loc.PurposeQueued;
            if (State is PurposeState.Running) return Loc.PurposeRunning;
            if (State is PurposeState.Failed) return Loc.PurposeFailed;
            if (_purposeName.Length > 0) return _purposeName;
            if (HasBatchPurpose) return _batchPurpose;
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
            {
                // 批量归类只给一个用途名，没有依据可写 —— 如实说「来源是 AI 推测」就够。
                // 拿不准的那批必须说清是「模型也没把握」，不能和它有把握的混成一句。
                if (BatchPurposeUnsure) return Loc.PurposeDetailUnsure;
                if (HasBatchPurpose) return Loc.PurposeFromAi;
                return _needsConfirm ? Loc.PurposeNeedsConfirm : Loc.PurposeIdentify;
            }
            string s = Source == PurposeSource.None ? "" : SourceText;
            if (_basis.Length > 0) s = s.Length > 0 ? s + " · " + _basis : _basis;
            if (NeedsConfirm) s = s.Length > 0 ? s + " · " + Loc.PurposeNeedsConfirm : Loc.PurposeNeedsConfirm;
            return s;
        }
    }

    /// <summary>只有「有结论」的行才用正常文字色；没结论一律弱化。</summary>
    public bool IsResolved => HasConclusion && !NeedsConfirm;

    /// <summary>排到「未识别」筛选里的行。用户点过 AI 且已给出用途的，不再算未识别。</summary>
    public bool IsPending => !HasConclusion || NeedsConfirm || State == PurposeState.Failed;

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

    private bool _coveredByAncestor;

    /// <summary>
    /// 列表里**上面那一项已经把它整个包住了**（父子同时上榜）。
    ///
    /// 真机上用户就是这样困惑的：`Users` 和 `Users\32098` 都显示 179 G，看着像重复 ——
    /// 它们确实是父子，而这个盘上 `32098` 是 `Users` 里唯一的东西，所以容量一样。
    /// **勾了两个不会算两遍**（<c>FolderSelectionService</c> 按父目录覆盖子目录去重），
    /// 但界面得说清楚，否则用户会以为自己选了两份空间。
    ///
    /// 由 <c>MarkCoveredRows</c> 在每次建行之后算一次（O(行数×深度)，不做两两比较）。
    /// </summary>
    public bool IsCoveredByAncestor
    {
        get => _coveredByAncestor;
        set
        {
            if (_coveredByAncestor == value) return;
            _coveredByAncestor = value;
            Raise();
            Raise(nameof(CoveredTag));
        }
    }

    /// <summary>被祖先包含时给的那句短标签。没被包含就是空串（标签自己会收起来）。</summary>
    public string CoveredTag => IsCoveredByAncestor ? Loc.OrganizeCoveredTag : "";

    /// <summary>顶层对象（首屏只显示这些）。</summary>
    public bool IsRoot => Depth == 0;

    // ==================== 手动选择（与 AI 结论完全分离） ====================

    private bool _isChecked;

    /// <summary>
    /// 用户**手动勾选**这一项，准备删除。它只由用户操作写入 ——
    /// 本地识别、AI 结论、纠正用途都**不碰**它（<see cref="Ai"/> 的 CanSelect 在整理页恒为 false）。
    /// 勾选本身也不删任何东西；真正删除要经过预览与明确确认。
    ///
    /// 命名说明：刻意叫 <c>IsChecked</c>（复选框语义）而不是 <c>IsSelected</c> ——
    /// 既有的清理侧安全检查把「选择」当成清理能力，用 <c>Selected</c> 命名会被误判；
    /// 这里的勾选与清理列表的选择毫无关系，也不能由 AI 写入。
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            Raise(nameof(IsChecked));
        }
    }

    public void ToggleSelect() => IsChecked = !IsChecked;

    private PathGuardResult? _selectionGuard;

    /// <summary>
    /// 选择期的**纯路径**保护结论（不碰磁盘，可放心给每行绑定）。
    ///
    /// 这里刻意不引用删除服务：<c>OrganizeNode.cs</c> 被多个离线检查工程单独链接，
    /// 不能拖进新类型。真正的删除判定以 <c>FolderDeleteGuard</c> 为准（那边还会读磁盘，
    /// 拒绝链接 / 重解析祖先）；这里只是给复选框一个即时提示。
    /// </summary>
    private PathGuardResult SelectionGuard => _selectionGuard ??= ComputeSelectionGuard();

    private PathGuardResult ComputeSelectionGuard()
    {
        var byPath = ProtectedPaths.Classify(FullPath);
        if (byPath.Guard == PathGuard.Blocked) return byPath;

        var parts = ProtectedPaths.Segments(FullPath);
        // 与 FolderDeleteGuard 的根级容器表保持一致（这里只做展示提示）。
        if (parts.Length == 2 && IsSelectionRootContainer(parts[1]))
            return PathGuardResult.Blocked("系统/用户根级容器目录，不能整删", "root container: " + parts[1]);
        if (parts.Length == 3 && parts[1].Equals("Users", StringComparison.OrdinalIgnoreCase))
            return PathGuardResult.Confirm("整个用户配置根目录，删了该用户的文件就没了", "user profile root");
        return byPath;
    }

    private static bool IsSelectionRootContainer(string name)
    {
        foreach (var c in SelectionRootContainers)
            if (string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static readonly string[] SelectionRootContainers =
    {
        "windows", "program files", "program files (x86)", "programdata", "perflogs",
        "users", "recovery", "$recycle.bin", "system volume information",
        "boot", "efi", "msocache", "$winreagent", "windows.old",
    };

    /// <summary>
    /// 复选框是否禁用。只对硬拦（Windows / Program Files / Users / ProgramData 等根容器）。
    /// 需确认项（如用户配置根）仍可勾，额外确认留在删除预览/对话框。
    /// </summary>
    public bool IsSelectionProtected => SelectionGuard.Guard == PathGuard.Blocked;

    /// <summary>
    /// 硬拦时解释为何灰掉；需确认时说明删除还会再问一次。允许勾选的普通目录为空。
    /// </summary>
    public string SelectionNote => SelectionGuard.Guard == PathGuard.Allowed ? "" : SelectionGuard.Reason;

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

    /// <summary>「结论相关的展示属性」这一组。批量归类写回之后刷这一组。</summary>
    void RaiseConclusionDisplay()
    {
        Raise(nameof(HasConclusion));
        Raise(nameof(PurposeText));
        Raise(nameof(PurposeDetail));
        Raise(nameof(Source));
        Raise(nameof(SourceText));
        Raise(nameof(NeedsConfirm));
        Raise(nameof(IsPending));
        Raise(nameof(IsResolved));
        Raise(nameof(DetailText));
        Raise(nameof(State));
    }

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
