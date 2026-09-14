using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models;

/// <summary>
/// 一次手动勾选在进入选择/去重逻辑时的**最小投影**。
///
/// 用值对象而不是让 <see cref="OrganizeNode"/> 实现新接口，是刻意为之：
/// <c>OrganizeNode.cs</c> 被多个离线检查工程单独链接，不能因此引入新类型依赖；
/// 选择逻辑本身也不需要知道节点的其余部分。
/// </summary>
public readonly record struct FolderPick(bool IsSelected, string FullPath, string Name, long Size);

/// <summary>一次手动删除任务的整体状态（界面按它决定按钮与文案）。</summary>
public enum FolderDeleteState
{
    /// <summary>什么都没发生。</summary>
    Idle,
    /// <summary>没有勾选任何文件夹。</summary>
    NoSelection,
    /// <summary>预览算好了，等用户确认。</summary>
    PreviewReady,
    /// <summary>正在执行（此刻不接受第二次执行）。</summary>
    Busy,
    /// <summary>全部按预期处理完。</summary>
    Completed,
    /// <summary>一部分成功、一部分没做成。</summary>
    PartialFailure,
    /// <summary>一个都没做成。</summary>
    Failed,
    /// <summary>用户取消：已处理项保留真实结果，其余未处理。</summary>
    Canceled,
    /// <summary>选中的项都被保护/回收站不可用，一个都不执行。</summary>
    Blocked,
}

/// <summary>单个文件夹在预览里的处境。</summary>
public enum FolderDeleteItemState
{
    /// <summary>可删（回收站可用）。</summary>
    Ready,
    /// <summary>敏感位置：需要用户明确确认整批。</summary>
    NeedsConfirm,
    /// <summary>硬拦截（系统根 / 链接 / 重解析点下 / 保护段）。</summary>
    Blocked,
    /// <summary>扫描后已经不存在。</summary>
    Missing,
    /// <summary>这个位置换成了别的对象。</summary>
    Changed,
    /// <summary>被占用。</summary>
    InUse,
    /// <summary>没有权限。</summary>
    NoAccess,
    /// <summary>回收站不可用：**拒绝永久删除**，不执行。</summary>
    RecycleUnavailable,
    /// <summary>父目录已经在选中里，这一项不重复删。</summary>
    Nested,
}

/// <summary>单个文件夹的真实结局。刻意与清理页的 <see cref="DeletionOutcome"/> 分开：
/// 文件夹删除**没有永久删除路径**，所以需要独有的「不确定 / 部分完成」结局。</summary>
public enum FolderDeleteOutcome
{
    /// <summary>已按「必须进回收站」的方式移入回收站。</summary>
    Recycled,
    /// <summary>
    /// Shell 报错或被中断，但路径**已经消失**：无法确认是进了回收站还是被永久删除。
    /// 绝不冒认成功，也绝不假装「拒绝了」。
    /// </summary>
    Uncertain,
    /// <summary>操作被中断，路径仍在：可能只删掉了一部分，需要人工检查。</summary>
    PartiallyRemoved,
    /// <summary>路径已经不存在（扫描后已被别处删掉）。</summary>
    NotFound,
    /// <summary>位置换成了别的对象。</summary>
    PathChanged,
    /// <summary>原本的文件夹位置变成了文件之类。</summary>
    Replaced,
    /// <summary>没有权限。</summary>
    AccessDenied,
    /// <summary>受保护位置。</summary>
    Protected,
    /// <summary>正在被占用。</summary>
    InUse,
    /// <summary>回收站不可用：拒绝执行，没有删。</summary>
    RecycleUnavailable,
    /// <summary>用户取消 / 敏感位置未确认：没有处理。</summary>
    SkippedByUser,
    /// <summary>父目录已选中，不重复处理。</summary>
    Nested,
    /// <summary>删除失败，路径仍在。</summary>
    Failed,
}

/// <summary>
/// 用户在确认框上给出的授权。
/// **没有「允许永久删除」这一项**：本功能不存在永久删除路径，
/// 回收站不可用时一律拒绝执行。
/// </summary>
public sealed record FolderDeleteConsent(bool AllowSensitive)
{
    public static readonly FolderDeleteConsent None = new(false);
    public static readonly FolderDeleteConsent SensitiveConfirmed = new(true);
}

/// <summary>预览里的一行。</summary>
public sealed class FolderDeletePreviewItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public FolderDeleteItemState State { get; set; } = FolderDeleteItemState.Ready;
    /// <summary>用户可读原因。</summary>
    public string Reason { get; set; } = "";
    /// <summary>技术说明（日志/悬停）。</summary>
    public string Tech { get; set; } = "";
    /// <summary>预检给出的原始项（执行阶段据此重查，不重新发明一套判定）。</summary>
    public DeletionPreflightItem? Preflight { get; set; }

    public bool CanDelete => State is FolderDeleteItemState.Ready or FolderDeleteItemState.NeedsConfirm;
    public string StateText => FolderDeleteText.ItemState(State);
    public string SizeText => FileEntry.FormatSize(Math.Max(0, Size));
}

/// <summary>
/// 一次手动删除的只读预览。**算它的过程不删任何东西**。
/// </summary>
public sealed class FolderDeletePreview
{
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>用户勾选的总数（可能含被父目录覆盖的子项）。</summary>
    public int SelectedCount { get; set; }
    /// <summary>因为父目录已选中而没有单独处理的项数。</summary>
    public int NestedCount { get; set; }
    public List<FolderDeletePreviewItem> Items { get; } = new();

    /// <summary>真正可执行的计划（内部使用；界面只读预览行）。</summary>
    public DeletionPlan? Plan { get; set; }

    public int DeletableCount { get; private set; }
    public int SensitiveCount { get; private set; }
    public int BlockedCount { get; private set; }
    public int RecycleUnavailableCount { get; private set; }
    public int MissingCount { get; private set; }
    public long DeletableBytes { get; private set; }

    public bool HasSelection => SelectedCount > 0;
    public bool HasAnything => DeletableCount > 0;
    public bool HasSensitive => SensitiveCount > 0;

    /// <summary>预览汇总（把每一项重新数一遍，任何一项失败都能从 <see cref="Items"/> 查到原因）。</summary>
    public void Recompute()
    {
        DeletableCount = Items.Count(x => x.CanDelete);
        SensitiveCount = Items.Count(x => x.State == FolderDeleteItemState.NeedsConfirm);
        BlockedCount = Items.Count(x => x.State == FolderDeleteItemState.Blocked);
        RecycleUnavailableCount = Items.Count(x => x.State == FolderDeleteItemState.RecycleUnavailable);
        MissingCount = Items.Count(x => x.State == FolderDeleteItemState.Missing);
        DeletableBytes = Items.Where(x => x.CanDelete).Sum(x => Math.Max(0, x.Size));
    }
}

/// <summary>单项执行结果。</summary>
public sealed class FolderDeleteItemResult
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public FolderDeleteOutcome Outcome { get; set; } = FolderDeleteOutcome.Failed;
    public string Message { get; set; } = "";
    public string Detail { get; set; } = "";
    public long FreedBytes { get; set; }
    public TimeSpan Elapsed { get; set; }

    public bool Recycled => Outcome == FolderDeleteOutcome.Recycled;
    public string OutcomeText => FolderDeleteText.Outcome(Outcome);
}

/// <summary>一批手动删除的汇总。</summary>
public sealed class FolderDeleteResult
{
    public FolderDeleteState State { get; set; } = FolderDeleteState.Idle;
    public bool Canceled { get; set; }
    public List<FolderDeleteItemResult> Items { get; } = new();
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public TimeSpan Elapsed { get; set; }

    public int Count(FolderDeleteOutcome outcome) => Items.Count(x => x.Outcome == outcome);
    public int Recycled => Count(FolderDeleteOutcome.Recycled);
    public int Uncertain => Count(FolderDeleteOutcome.Uncertain);
    public int PartiallyRemoved => Count(FolderDeleteOutcome.PartiallyRemoved);
    public int Failed => Count(FolderDeleteOutcome.Failed);
    public int AccessDenied => Count(FolderDeleteOutcome.AccessDenied);
    public int Protected => Count(FolderDeleteOutcome.Protected);
    public int InUse => Count(FolderDeleteOutcome.InUse);
    public int NotFound => Count(FolderDeleteOutcome.NotFound);
    public int PathChanged => Count(FolderDeleteOutcome.PathChanged) + Count(FolderDeleteOutcome.Replaced);
    public int RecycleUnavailable => Count(FolderDeleteOutcome.RecycleUnavailable);
    public int Skipped => Count(FolderDeleteOutcome.SkippedByUser);
    public int Nested => Count(FolderDeleteOutcome.Nested);
    public int Total => Items.Count;
    public long FreedBytes => Items.Sum(x => Math.Max(0, x.FreedBytes));

    /// <summary>真正没做成 / 结果不确定、必须让用户看到的项。</summary>
    public List<FolderDeleteItemResult> Problems => Items
        .Where(x => x.Outcome is FolderDeleteOutcome.Uncertain or FolderDeleteOutcome.PartiallyRemoved
            or FolderDeleteOutcome.Failed or FolderDeleteOutcome.AccessDenied
            or FolderDeleteOutcome.InUse or FolderDeleteOutcome.Protected
            or FolderDeleteOutcome.PathChanged or FolderDeleteOutcome.Replaced
            or FolderDeleteOutcome.RecycleUnavailable)
        .ToList();

    public string Headline => FolderDeleteText.ResultHeadline(this);
    public string SummaryText => FolderDeleteText.ResultSummary(this);
}

/// <summary>
/// 底部操作栏的视图模型。**只描述选择与状态**，不持有任何删除能力 ——
/// 真正的执行入口在 MainWindow 的 partial 与 <see cref="FolderDeleteService"/>。
/// </summary>
public sealed class FolderDeleteBar : INotifyPropertyChanged
{
    private int _selected, _kept, _nested;
    private long _bytes;
    private bool _busy, _cancelable, _hasNote, _hasResult;
    private string _stateText = "";
    private string _noteText = "";
    private string _progressText = "";

    public bool HasSelection => _kept > 0;
    public bool CanDelete => !_busy && _kept > 0;
    public bool IsBusy => _busy;
    public bool CanCancel => _busy && _cancelable;
    public bool HasNote => _hasNote;
    public string StateText => _stateText;
    public string NoteText => _noteText;
    public string ProgressText => _progressText;
    public string DeleteText => FolderDeleteText.DeleteAction;
    public string CancelText => FolderDeleteText.CancelAction;
    public string SelectAllText => FolderDeleteText.SelectAllAction;
    public string ClearText => FolderDeleteText.ClearAction;

    public string SelectedText => FolderDeleteText.SelectionSummary(_selected, _kept, _nested, _bytes);

    /// <summary>重新读一遍选择（重建/展开/筛选之后由界面调用）。</summary>
    public void Refresh(int selected, int kept, int nested, long bytes)
    {
        _selected = Math.Max(0, selected);
        _kept = Math.Max(0, kept);
        _nested = Math.Max(0, nested);
        _bytes = Math.Max(0, bytes);
        if (!_busy)
        {
            _progressText = "";
            // 有新选择就清掉上一次的结果状态；没有选择且刚跑完，则保留结果标题（别把结果盖成「先勾选」）。
            if (_kept > 0) { _hasResult = false; _stateText = ""; }
            else if (!_hasResult) _stateText = FolderDeleteText.NothingSelected;
        }
        RaiseAll();
    }

    public void SetNote(string text)
    {
        _noteText = text ?? "";
        _hasNote = _noteText.Length > 0;
        Raise(nameof(NoteText));
        Raise(nameof(HasNote));
    }

    /// <summary>开始执行：按钮禁用，取消可用。</summary>
    public void Begin(int total)
    {
        _busy = true;
        _cancelable = true;
        _progressText = total > 0 ? $"0/{total}" : "";
        _stateText = FolderDeleteText.Running;
        SetNote("");
        RaiseAll();
    }

    public void Report(int done, int total)
    {
        _progressText = total > 0 ? $"{done}/{total}" : "";
        Raise(nameof(ProgressText));
    }

    public void RequestCancel()
    {
        if (!_busy) return;
        _cancelable = false;
        _stateText = FolderDeleteText.Stopping;
        RaiseAll();
    }

    /// <summary>执行结束：如实写入汇总，失败/不确定项留在提示里。</summary>
    public void Finish(FolderDeleteResult result)
    {
        _busy = false;
        _cancelable = false;
        _progressText = "";
        _hasResult = true;
        _stateText = result.Headline;
        string detail = result.Problems.Count > 0 ? FolderDeleteText.ResultProblems(result) : "";
        SetNote(detail);
        RaiseAll();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAll()
    {
        Raise(nameof(HasSelection));
        Raise(nameof(CanDelete));
        Raise(nameof(IsBusy));
        Raise(nameof(CanCancel));
        Raise(nameof(SelectedText));
        Raise(nameof(StateText));
        Raise(nameof(ProgressText));
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 手动删除文件夹的全部界面文案。
/// 刻意**不新增 Loc 键**：删除这一块由本模块自己拥有，避免和界面文案表互相踩。
/// </summary>
public static class FolderDeleteText
{
    private static bool En => Loc.IsEn;

    public static string DeleteAction => En ? "Delete folders" : "删除文件夹";
    public static string CancelAction => En ? "Stop" : "停止";
    public static string SelectAllAction => En ? "Select visible" : "全选当前列表";
    public static string ClearAction => En ? "Clear" : "清空选择";
    public static string NothingSelected => En ? "Tick folders first" : "先勾选要删除的文件夹";
    public static string Running => En ? "Deleting..." : "正在删除…";
    public static string Stopping => En ? "Stopping..." : "正在停止…";
    public static string CancelRequested => En
        ? "Stop requested; finished items keep their result"
        : "已请求停止；已经处理完的项会保留结果";
    public static string CanceledNotProcessed => En ? "canceled, not processed" : "已取消，未处理";
    public static string Busy => En ? "already running" : "正在删除，请稍候";
    public static string SensitiveNotConfirmed => En
        ? "sensitive location not confirmed; not deleted"
        : "敏感位置未确认，没有删";
    public static string RecycleUnavailableReason => En
        ? "no usable Recycle Bin on this volume; will not delete permanently"
        : "这个磁盘没有可用回收站；不会永久删除，已跳过";
    public static string NothingDeletable => En
        ? "none of the ticked folders can be deleted; see the reasons"
        : "勾选的文件夹都删不了，原因见预览";
    public static string NestedReason => En ? "parent folder already selected" : "父目录已选中";
    public static string ConfirmTitle => En ? "Delete folders" : "删除文件夹";
    public static string ResultTitle => En ? "Folder delete result" : "文件夹删除结果";

    public static string SelectionSummary(int selected, int kept, int nested, long bytes)
    {
        if (kept <= 0)
            return selected > 0
                ? (En ? $"{selected} ticked, nothing to run" : $"已勾选 {selected} 个，没有可处理的项")
                : NothingSelected;
        string size = bytes > 0 ? " · " + (En ? "about " : "约 ") + FileEntry.FormatSize(bytes) : "";
        string nestedText = nested > 0
            ? (En ? $" ({nested} nested items merged)" : $"（{nested} 个在已选文件夹里，不重复删）")
            : "";
        return En
            ? $"{selected} ticked · {kept} folders to handle{size}{nestedText}"
            : $"已勾选 {selected} 个 · 实际处理 {kept} 个{size}{nestedText}";
    }

    public static string ItemState(FolderDeleteItemState s) => s switch
    {
        FolderDeleteItemState.Ready => En ? "can delete" : "可删除",
        FolderDeleteItemState.NeedsConfirm => En ? "needs your confirmation" : "需要你确认",
        FolderDeleteItemState.Blocked => En ? "protected, will not delete" : "受保护，不会删",
        FolderDeleteItemState.Missing => En ? "already gone" : "已经不在了",
        FolderDeleteItemState.Changed => En ? "location changed" : "位置已变化",
        FolderDeleteItemState.InUse => En ? "in use" : "正在使用",
        FolderDeleteItemState.NoAccess => En ? "no permission" : "没有权限",
        FolderDeleteItemState.RecycleUnavailable => En ? "no Recycle Bin" : "回收站不可用",
        FolderDeleteItemState.Nested => En ? "covered by parent" : "父目录已选中",
        _ => "",
    };

    public static string Outcome(FolderDeleteOutcome o) => o switch
    {
        FolderDeleteOutcome.Recycled => En ? "moved to Recycle Bin" : "已移入回收站",
        FolderDeleteOutcome.Uncertain => En ? "result uncertain" : "结果不确定",
        FolderDeleteOutcome.PartiallyRemoved => En ? "partially removed" : "可能只删了一部分",
        FolderDeleteOutcome.NotFound => En ? "already gone" : "已经不在了",
        FolderDeleteOutcome.PathChanged => En ? "location changed" : "位置已变化",
        FolderDeleteOutcome.Replaced => En ? "replaced by something else" : "位置已被别的对象占用",
        FolderDeleteOutcome.AccessDenied => En ? "no permission" : "没有权限",
        FolderDeleteOutcome.Protected => En ? "protected" : "受保护",
        FolderDeleteOutcome.InUse => En ? "in use" : "正在使用",
        FolderDeleteOutcome.RecycleUnavailable => En ? "no Recycle Bin, not deleted" : "回收站不可用，没有删",
        FolderDeleteOutcome.SkippedByUser => En ? "skipped" : "未处理",
        FolderDeleteOutcome.Nested => En ? "covered by parent" : "父目录已选中",
        FolderDeleteOutcome.Failed => En ? "failed" : "删除失败",
        _ => "",
    };

    /// <summary>确认框正文：把「会删什么、哪些需要确认、哪些删不了、空间多少」说在点确定之前。</summary>
    public static string ConfirmBody(FolderDeletePreview p)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(En
            ? $"{p.DeletableCount} folder(s) will be sent to the Recycle Bin"
            : $"将把 {p.DeletableCount} 个文件夹移入回收站");
        if (p.DeletableBytes > 0)
            sb.Append(En ? $", about {FileEntry.FormatSize(p.DeletableBytes)}" : $"，约 {FileEntry.FormatSize(p.DeletableBytes)}");
        sb.Append('。');
        if (p.SensitiveCount > 0)
            sb.Append(En
                ? $" {p.SensitiveCount} are sensitive locations (program folders / user profile roots, etc.)."
                : $"其中 {p.SensitiveCount} 个是敏感位置（软件目录 / 用户目录根等）。");
        if (p.RecycleUnavailableCount > 0)
            sb.Append(En
                ? $" {p.RecycleUnavailableCount} have no usable Recycle Bin and will be skipped."
                : $"另有 {p.RecycleUnavailableCount} 个所在磁盘没有可用回收站，会跳过。");
        if (p.BlockedCount > 0)
            sb.Append(En
                ? $" {p.BlockedCount} are protected and will not be touched."
                : $"另有 {p.BlockedCount} 个受保护，不会碰。");

        const int maxLines = 12;
        foreach (var item in p.Items.Where(x => x.CanDelete).Take(maxLines))
            sb.Append('\n').Append("• ").Append(item.Name).Append(" — ").Append(item.SizeText)
              .Append(" — ").Append(item.Reason.Length > 0 ? item.Reason : item.StateText);
        int hidden = p.DeletableCount - Math.Min(maxLines, p.DeletableCount);
        if (hidden > 0) sb.Append('\n').Append(En ? $"… and {hidden} more" : $"… 还有 {hidden} 个");

        sb.Append('\n').Append(En
            ? "Everything goes to the Recycle Bin and can be restored. This app never permanently deletes: if the Recycle Bin is unavailable the item is refused."
            : "都会进回收站，可以还原。本程序不会永久删除：回收站不可用时会直接拒绝这一项。");
        return sb.ToString();
    }

    public static string ResultHeadline(FolderDeleteResult r)
    {
        if (r.State == FolderDeleteState.Canceled)
            return (En ? "Stopped: " : "已停止：")
                + (En ? $"{r.Recycled} recycled, {r.Skipped} not processed"
                      : $"{r.Recycled} 个已进回收站，{r.Skipped} 个未处理");
        if (r.Uncertain > 0)
            return (En ? "Finished with uncertain items: " : "已完成，但有不确定项：")
                + (En ? $"{r.Recycled} recycled, {r.Uncertain} uncertain"
                      : $"{r.Recycled} 个已进回收站，{r.Uncertain} 个结果不确定");
        return En
            ? $"{r.Recycled} recycled, {r.Total - r.Recycled} not recycled"
            : $"{r.Recycled} 个已进回收站，{r.Total - r.Recycled} 个未完成";
    }

    public static string ResultSummary(FolderDeleteResult r)
    {
        var sb = new System.Text.StringBuilder(ResultHeadline(r));
        sb.Append('\n').Append(En ? $"Freed about {FileEntry.FormatSize(r.FreedBytes)}." : $"约释放 {FileEntry.FormatSize(r.FreedBytes)}。");
        if (r.Uncertain > 0)
            sb.Append('\n').Append(En
                ? "Some items reported an error but their path is already gone — check the Recycle Bin before assuming anything."
                : "有项报错但路径已消失，无法确认是否进了回收站，请到回收站核对。");
        if (r.Canceled)
            sb.Append('\n').Append(En ? "Remaining items were not processed." : "其余项没有处理。");
        return sb.ToString();
    }

    public static string ResultProblems(FolderDeleteResult r)
    {
        var problems = r.Problems;
        if (problems.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var p in problems.Take(10))
            sb.Append('\n').Append("• ").Append(p.Name).Append(" — ").Append(p.OutcomeText)
              .Append(p.Message.Length > 0 ? "：" + p.Message : "");
        if (problems.Count > 10)
            sb.Append('\n').Append(En ? $"… and {problems.Count - 10} more" : $"… 还有 {problems.Count - 10} 项");
        return sb.ToString().TrimStart('\n');
    }
}
