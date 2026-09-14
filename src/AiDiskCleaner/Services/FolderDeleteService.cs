using System.Diagnostics;
using System.IO;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 手动删除文件夹的**唯一执行入口**。职责：
/// <list type="bullet">
/// <item><see cref="Preview"/>：只读预检 + 文件夹专项防护 + 回收站可用性 → 给用户看的清单；</item>
/// <item><see cref="Execute"/>：明确确认之后才逐项执行；确认前取消 => 零后端调用；</item>
/// <item>每一项都**重新探测、重新过防护、重新比对身份**，再调用「必须进回收站」的 shell 操作；</item>
/// <item>执行中取消：停掉后续项，已处理项保留真实结果；</item>
/// <item>失败但路径已消失：如实报告「结果不确定」，绝不冒认回收成功，也绝不假装拒绝。</item>
/// </list>
///
/// 刻意复用 <see cref="DeletionPreflight"/> 做去重与身份快照（它是纯判定、不删东西），
/// 但**不复用** <see cref="DeletionExecutor"/> 的「路径消失就算 Recycled」口径 ——
/// 那个口径在回收站不可用时会误报，正是本模块要修掉的问题。
/// </summary>
public sealed class FolderDeleteService
{
    private readonly IFileSystemProbe _probe;
    private readonly IFolderRecycleOperation _recycle;
    private readonly DeletionPreflight _preflight;
    private bool _busy;

    public FolderDeleteService(
        IFileSystemProbe? probe = null,
        IFolderRecycleOperation? recycle = null,
        DeletionPreflight? preflight = null)
    {
        _probe = probe ?? Win32FileSystemProbe.Instance;
        _recycle = recycle ?? new ShellRecycleOperation();
        _preflight = preflight ?? new DeletionPreflight(_probe);
    }

    /// <summary>同一时刻只允许一个删除任务在跑。</summary>
    public bool IsBusy => _busy;

    // ==================== 预览（只读） ====================

    public FolderDeletePreview Preview(
        IEnumerable<DeletionTarget>? targets,
        int selectedCount = 0,
        int nestedCount = 0,
        CancellationToken ct = default)
    {
        var list = targets?
            .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Path))
            .ToList() ?? new List<DeletionTarget>();

        var preview = new FolderDeletePreview
        {
            CreatedAt = DateTime.Now,
            SelectedCount = selectedCount > 0 ? selectedCount : list.Count,
            NestedCount = Math.Max(0, nestedCount),
        };
        if (list.Count == 0) return preview;

        var plan = _preflight.CreatePlan(list, sensitiveConfirmed: false, ct);
        preview.Plan = plan;

        foreach (var item in plan.Preflight.Items)
        {
            ct.ThrowIfCancellationRequested();
            preview.Items.Add(BuildPreviewItem(item, ct));
        }

        preview.NestedCount += preview.Items.Count(x => x.State == FolderDeleteItemState.Nested);
        preview.Recompute();
        return preview;
    }

    private FolderDeletePreviewItem BuildPreviewItem(DeletionPreflightItem item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var row = new FolderDeletePreviewItem
        {
            Path = item.Target.Path,
            Name = item.Label,
            Size = Math.Max(0, item.Target.ExpectedSize),
            Reason = item.Reason,
            Tech = item.Tech,
            Preflight = item,
            State = StateFromPreflight(item),
        };

        if (row.CanDelete)
        {
            // 文件夹专项防护：容器本身 / 链接（目标或祖先是重解析点）一律硬拦。
            var extra = FolderDeleteGuard.ClassifyForFolderDelete(row.Path, _probe);
            if (extra.Guard == PathGuard.Blocked)
            {
                row.State = FolderDeleteItemState.Blocked;
                row.Reason = extra.Reason;
                row.Tech = extra.Tech;
            }
            else if (extra.Guard == PathGuard.NeedsConfirm)
            {
                row.State = FolderDeleteItemState.NeedsConfirm;
                row.Reason = extra.Reason;
                row.Tech = extra.Tech;
            }
        }

        // 回收站不可用 => 不执行（本模块不存在永久删除路径）。
        if (row.CanDelete)
        {
            string tech;
            if (!_recycle.CanRecycle(row.Path, out tech))
            {
                row.State = FolderDeleteItemState.RecycleUnavailable;
                row.Reason = FolderDeleteText.RecycleUnavailableReason;
                row.Tech = tech;
            }
        }

        if (row.Reason.Length == 0) row.Reason = row.StateText;
        return row;
    }

    private static FolderDeleteItemState StateFromPreflight(DeletionPreflightItem item)
    {
        switch (item.Outcome)
        {
            case DeletionOutcome.RedundantChild: return FolderDeleteItemState.Nested;
            case DeletionOutcome.NotFound: return FolderDeleteItemState.Missing;
            case DeletionOutcome.PathChanged:
            case DeletionOutcome.Modified: return FolderDeleteItemState.Changed;
            case DeletionOutcome.AccessDenied: return FolderDeleteItemState.NoAccess;
            case DeletionOutcome.Protected: return FolderDeleteItemState.Blocked;
            case DeletionOutcome.InUse: return FolderDeleteItemState.InUse;
            case DeletionOutcome.Ready:
            case DeletionOutcome.SkippedByUser:
                return item.Guard == DeletionGuardLevel.NeedsConfirm
                    ? FolderDeleteItemState.NeedsConfirm
                    : FolderDeleteItemState.Ready;
            default: return FolderDeleteItemState.Blocked;
        }
    }

    // ==================== 执行 ====================

    /// <summary>
    /// 逐项执行。**<paramref name="confirm"/> 返回 false 时一个后端删除调用都不会发。**
    /// </summary>
    public FolderDeleteResult Execute(
        FolderDeletePreview? preview,
        FolderDeleteConsent? consent,
        Func<FolderDeletePreview, bool>? confirm,
        IProgress<FolderDeleteItemResult>? progress = null,
        CancellationToken ct = default)
    {
        var result = new FolderDeleteResult { StartedAt = DateTime.Now };
        var total = Stopwatch.StartNew();

        if (_busy)
        {
            result.State = FolderDeleteState.Busy;
            return Finish(result, total);
        }

        if (preview?.Plan == null || !preview.HasSelection)
        {
            result.State = FolderDeleteState.NoSelection;
            return Finish(result, total);
        }

        if (!preview.HasAnything)
        {
            // 一个都执行不了：如实登记每一项被拒的原因，不调用后端。
            result.State = FolderDeleteState.Blocked;
            foreach (var row in preview.Items) result.Items.Add(Refused(row));
            return Finish(result, total);
        }

        // 明确确认门槛：确认前取消 = 预览丢弃，零后端调用。
        consent ??= FolderDeleteConsent.None;
        if (confirm != null && !confirm(preview))
        {
            result.Canceled = true;
            result.State = FolderDeleteState.Canceled;
            foreach (var row in preview.Items)
                result.Items.Add(Skipped(row, FolderDeleteText.CanceledNotProcessed));
            return Finish(result, total);
        }

        _busy = true;
        try
        {
            int done = 0;
            int count = preview.Items.Count;
            bool canceled = false;
            foreach (var row in preview.Items)
            {
                FolderDeleteItemResult r;
                if (canceled || ct.IsCancellationRequested)
                {
                    canceled = true;
                    r = Skipped(row, FolderDeleteText.CanceledNotProcessed);
                }
                else
                {
                    r = ExecuteRow(row, consent);
                    done++;
                }
                result.Items.Add(r);
                progress?.Report(r);
            }

            result.Canceled = canceled;
            result.State = ComputeState(result, canceled);
        }
        finally
        {
            _busy = false;
        }
        return Finish(result, total);
    }

    private static FolderDeleteResult Finish(FolderDeleteResult result, Stopwatch sw)
    {
        sw.Stop();
        result.Elapsed = sw.Elapsed;
        return result;
    }

    private static FolderDeleteState ComputeState(FolderDeleteResult result, bool canceled)
    {
        if (canceled) return FolderDeleteState.Canceled;
        int ok = result.Recycled;
        int problems = result.Problems.Count;
        if (ok > 0 && problems > 0) return FolderDeleteState.PartialFailure;
        if (ok > 0) return FolderDeleteState.Completed;
        if (problems > 0) return FolderDeleteState.Failed;
        return FolderDeleteState.Blocked;
    }

    /// <summary>按预览行的状态执行（需要真正删除时才重新探测）。</summary>
    private FolderDeleteItemResult ExecuteRow(FolderDeletePreviewItem row, FolderDeleteConsent consent)
    {
        switch (row.State)
        {
            case FolderDeleteItemState.Nested:
                return Skipped(row, FolderDeleteText.NestedReason, FolderDeleteOutcome.Nested);
            case FolderDeleteItemState.Blocked:
                return Done(row, FolderDeleteOutcome.Protected, row.Reason, row.Tech);
            case FolderDeleteItemState.Missing:
                return Done(row, FolderDeleteOutcome.NotFound, "文件夹已经不在了", row.Tech);
            case FolderDeleteItemState.Changed:
                return Done(row, FolderDeleteOutcome.PathChanged, row.Reason, row.Tech);
            case FolderDeleteItemState.InUse:
                return Done(row, FolderDeleteOutcome.InUse, "文件夹正在被程序使用，先关掉它", row.Tech);
            case FolderDeleteItemState.NoAccess:
                return Done(row, FolderDeleteOutcome.AccessDenied, "没有权限读取", row.Tech);
            case FolderDeleteItemState.RecycleUnavailable:
                return Done(row, FolderDeleteOutcome.RecycleUnavailable, row.Reason, row.Tech);
            case FolderDeleteItemState.NeedsConfirm when !consent.AllowSensitive:
                return Done(row, FolderDeleteOutcome.SkippedByUser, FolderDeleteText.SensitiveNotConfirmed, "sensitive not confirmed");
            default:
                return ExecuteOne(row);
        }
    }

    /// <summary>
    /// 真正执行一项：**再探一次、再过一遍防护、再比一次身份**，然后调用必须回收的 shell 删除。
    /// </summary>
    private FolderDeleteItemResult ExecuteOne(FolderDeletePreviewItem row)
    {
        var sw = Stopwatch.StartNew();
        var path = row.Path;

        FileProbeInfo probe;
        try { probe = _probe.Probe(path); }
        catch (Exception ex)
        {
            return Done(row, FolderDeleteOutcome.Failed, "读取路径失败", ex.GetType().Name, sw);
        }

        if (!probe.Exists)
            return Done(row, FolderDeleteOutcome.NotFound, "文件夹已经不在了", probe.Error ?? "not found", sw);
        if (probe.AccessDenied)
            return Done(row, FolderDeleteOutcome.AccessDenied, "没有权限读取", "access denied", sw);
        if (!probe.IsDirectory)
            return Done(row, FolderDeleteOutcome.Replaced, "这个位置已经不是文件夹了", "not a directory", sw);

        var guard = FolderDeleteGuard.ClassifyForFolderDelete(path, _probe);
        if (guard.Guard == PathGuard.Blocked)
            return Done(row, FolderDeleteOutcome.Protected, guard.Reason, guard.Tech, sw);

        var expected = row.Preflight?.ObservedIdentity;
        if (expected is { IsKnown: true } && probe.Identity.IsKnown && !expected.Value.Equals(probe.Identity))
            return Done(row, FolderDeleteOutcome.PathChanged,
                "这个位置已经换成别的文件夹了，没有删", "file id changed between preview and execute", sw);

        if (probe.InUse)
            return Done(row, FolderDeleteOutcome.InUse, "文件夹正在被程序使用，先关掉它", "sharing violation", sw);

        string tech;
        if (!_recycle.CanRecycle(path, out tech))
            return Done(row, FolderDeleteOutcome.RecycleUnavailable, FolderDeleteText.RecycleUnavailableReason, tech, sw);

        long estimated = row.Size > 0 ? row.Size : Math.Max(0, probe.AllocatedSize);

        RecycleShellResult shell;
        try
        {
            shell = _recycle.Recycle(path);
        }
        catch (Exception ex)
        {
            shell = RecycleShellResult.Fail(ex.HResult,
                ex.GetType().Name + " 0x" + (ex.HResult & 0xFFFFFFFF).ToString("X8"));
        }

        var after = SafeProbe(path);
        bool gone = !after.Exists;

        switch (shell.Status)
        {
            case RecycleShellStatus.Recycled:
                if (gone)
                    return Done(row, FolderDeleteOutcome.Recycled, "已移入回收站", shell.Message, sw, estimated);
                return Done(row, FolderDeleteOutcome.Failed, "系统报告完成，但这个文件夹还在", "path still exists after shell op", sw);

            case RecycleShellStatus.Aborted:
                if (gone)
                    return Done(row, FolderDeleteOutcome.Uncertain, UncertainMessage,
                        "aborted but path gone; " + shell.Message, sw);
                return Done(row, FolderDeleteOutcome.PartiallyRemoved,
                    "删除被中断，可能只删掉了一部分，请手动检查这个文件夹", shell.Message, sw);

            default:
                if (gone)
                    return Done(row, FolderDeleteOutcome.Uncertain, UncertainMessage,
                        "shell failed but path gone; " + shell.Message, sw);
                var (outcome, message) = ClassifyShellFailure(shell.HResult);
                return Done(row, outcome, message, shell.Message, sw);
        }
    }

    private const string UncertainMessage =
        "删除报错，但路径已经消失：无法确认是进了回收站还是被永久删除，请到回收站核对";

    /// <summary>把 shell 的错误码翻译成一档用户能理解的结局（路径仍在，所以绝不含糊）。</summary>
    public static (FolderDeleteOutcome Outcome, string Message) ClassifyShellFailure(int hresult)
    {
        int code = hresult & 0xFFFF;
        if (code == 5)
            return (FolderDeleteOutcome.AccessDenied, "没有权限删除，试试用管理员身份运行");
        if (code is 32 or 33)
            return (FolderDeleteOutcome.InUse, "文件夹正在被程序使用，先关掉它");
        return (FolderDeleteOutcome.Failed, "删除失败（回收站操作报错），文件没有动");
    }

    private FileProbeInfo SafeProbe(string path)
    {
        try { return _probe.Probe(path); }
        catch { return FileProbeInfo.Missing("probe failed"); }
    }

    private static FolderDeleteItemResult Refused(FolderDeletePreviewItem row)
        => Done(row, OutcomeForState(row.State), row.Reason, row.Tech);

    private static FolderDeleteOutcome OutcomeForState(FolderDeleteItemState s) => s switch
    {
        FolderDeleteItemState.Nested => FolderDeleteOutcome.Nested,
        FolderDeleteItemState.Blocked => FolderDeleteOutcome.Protected,
        FolderDeleteItemState.Missing => FolderDeleteOutcome.NotFound,
        FolderDeleteItemState.Changed => FolderDeleteOutcome.PathChanged,
        FolderDeleteItemState.InUse => FolderDeleteOutcome.InUse,
        FolderDeleteItemState.NoAccess => FolderDeleteOutcome.AccessDenied,
        FolderDeleteItemState.RecycleUnavailable => FolderDeleteOutcome.RecycleUnavailable,
        _ => FolderDeleteOutcome.Failed,
    };

    private static FolderDeleteItemResult Skipped(
        FolderDeletePreviewItem row, string message, FolderDeleteOutcome outcome = FolderDeleteOutcome.SkippedByUser)
        => Done(row, outcome, message, "");

    private static FolderDeleteItemResult Done(
        FolderDeletePreviewItem row, FolderDeleteOutcome outcome, string message, string detail,
        Stopwatch? sw = null, long freed = 0)
    {
        sw?.Stop();
        return new FolderDeleteItemResult
        {
            Path = row.Path,
            Name = row.Name,
            Outcome = outcome,
            Message = message ?? "",
            Detail = detail ?? "",
            FreedBytes = outcome == FolderDeleteOutcome.Recycled ? Math.Max(0, freed) : 0,
            Elapsed = sw?.Elapsed ?? TimeSpan.Zero,
        };
    }
}

/// <summary>同步把进度回调打在调用线程上（UI 线程执行时不会因为 Post 而丢进度）。</summary>
internal sealed class ActionProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;
    public ActionProgress(Action<T> report) => _report = report;
    public void Report(T value) => _report(value);
}
