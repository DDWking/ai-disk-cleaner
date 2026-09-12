using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 按计划逐项执行删除。设计要点：
/// - 每个路径单独走一次回收站调用，所以每项都有精确结局；一项失败不影响其他项。
/// - 删除前再探一次盘（预检到执行之间可能过了几十秒），身份/大小/占用重新确认。
/// - 取消是安全的：已经处理完的项保留完整结果，<see cref="DeletionBatchResult.Canceled"/> 标记为真。
/// </summary>
public sealed class DeletionExecutor
{
    private readonly IFileSystemProbe _probe;

    public DeletionExecutor(IFileSystemProbe? probe = null)
        => _probe = probe ?? Win32FileSystemProbe.Instance;

    public DeletionBatchResult Execute(
        DeletionPlan plan,
        IProgress<DeletionItemResult>? progress = null,
        CancellationToken ct = default)
    {
        var batch = new DeletionBatchResult { StartedAt = DateTime.Now };
        var total = Stopwatch.StartNew();

        // 按请求顺序逐项产出结果，**每个请求项恰好一条记录**：
        // 预检就挡下的（不存在/受保护/已变化/占用/父目录已包含）如实登记预检结局，
        // 不能因为「没执行」就从结果里消失 —— 否则界面只说得出「删了几项」，
        // 说不出「另外几项为什么没删」。
        foreach (var item in plan.Preflight.Items)
        {
            if (item.CanExecute)
            {
                // 敏感位置没确认：记成「用户跳过」，不是失败
                if (!plan.AllowSensitive && item.NeedsUserConfirm)
                {
                    var held = Result(item, DeletionOutcome.SkippedByUser,
                        "敏感位置未确认，没有删", "sensitive not confirmed");
                    batch.Results.Add(held);
                    progress?.Report(held);
                    continue;
                }

                if (ct.IsCancellationRequested)
                {
                    batch.Canceled = true;
                    break;
                }

                var executed = ExecuteOne(item);
                batch.Results.Add(executed);
                progress?.Report(executed);
                continue;
            }

            // 预检拒绝：沿用预检给出的结局与原因
            var refused = Result(item, item.Outcome,
                string.IsNullOrEmpty(item.Reason) ? item.Target.Path : item.Reason,
                item.Tech);
            batch.Results.Add(refused);
            progress?.Report(refused);
        }

        total.Stop();
        batch.Elapsed = total.Elapsed;
        return batch;
    }

    private DeletionItemResult ExecuteOne(DeletionPreflightItem item)
    {
        var sw = Stopwatch.StartNew();
        var path = item.Target.Path;

        // ---- 执行前重新探测：预检之后文件可能被改动/占用 ----
        var probe = _probe.Probe(path);
        if (!probe.Exists)
            return Done(item, DeletionOutcome.NotFound, "文件已经不在了", probe.Error ?? "not found", sw);

        if (probe.AccessDenied)
            return Done(item, DeletionOutcome.AccessDenied, "没有权限读取", "access denied: " + (probe.Error ?? ""), sw);

        var guard = ProtectedPaths.Classify(path, probe.IsReparsePoint, probe.IsSystem,
            probe.IsDirectory || item.Target.IsDirectory);
        if (guard.Guard == PathGuard.Blocked)
            return Done(item, DeletionOutcome.Protected, guard.Reason, guard.Tech, sw);

        if (probe.InUse)
            return Done(item, DeletionOutcome.InUse, "文件正在被程序使用，先关掉它", "sharing violation", sw);

        if (item.ObservedIdentity is { } expected && expected.IsKnown
            && probe.Identity.IsKnown && !expected.Equals(probe.Identity))
            return Done(item, DeletionOutcome.PathChanged, "这个位置已经换成别的文件了，没有删", "file id changed", sw);

        // ---- 真的删 ----
        try
        {
            _probe.SendToRecycle(new[] { path });
        }
        catch (Exception ex)
        {
            var (outcome, msg) = ClassifyDeleteFailure(ex);
            return Done(item, outcome, msg, Describe(ex), sw);
        }

        // ---- 复查：路径应该没了 ----
        var after = _probe.Probe(path);
        if (after.Exists)
            return Done(item, DeletionOutcome.Failed, "系统没有删掉它", "path still exists after SHFileOperation", sw);

        long freed = probe.AllocatedSize > 0
            ? probe.AllocatedSize
            : item.Target.ExpectedSize > 0 ? item.Target.ExpectedSize : probe.Size;

        return Done(item, DeletionOutcome.Recycled, "已移入回收站", "", sw, freed);
    }

    /// <summary>把原生/托管异常翻译成用户能理解的一档。</summary>
    public static (DeletionOutcome Outcome, string Message) ClassifyDeleteFailure(Exception ex)
    {
        int code = ex.HResult & 0xFFFF;
        if (ex is UnauthorizedAccessException || code == 5)
            return (DeletionOutcome.AccessDenied, "没有权限删除，试试用管理员身份运行");
        if (ex is IOException io)
        {
            int sub = io.HResult & 0xFFFF;
            if (sub is 32 or 33)
                return (DeletionOutcome.InUse, "文件正在被程序使用，先关掉它");
            if (sub is 5)
                return (DeletionOutcome.AccessDenied, "没有权限删除，试试用管理员身份运行");
            return (DeletionOutcome.Failed, "删除失败：" + Brief(io.Message));
        }
        return (DeletionOutcome.Failed, "删除失败：" + Brief(ex.Message));
    }

    private static string Brief(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "未知原因";
        return s.Length <= 120 ? s : s[..117] + "…";
    }

    private static string Describe(Exception ex)
        => ex.GetType().Name + " hr=0x" + ex.HResult.ToString("X8") + " " + Brief(ex.Message);

    private static DeletionItemResult Done(
        DeletionPreflightItem item, DeletionOutcome outcome, string message, string detail,
        Stopwatch sw, long freed = 0)
    {
        sw.Stop();
        return new DeletionItemResult
        {
            Path = item.Target.Path,
            Label = item.Label,
            Outcome = outcome,
            Message = message,
            Detail = detail,
            FreedBytes = outcome == DeletionOutcome.Recycled ? Math.Max(0, freed) : 0,
            Elapsed = sw.Elapsed,
        };
    }

    private static DeletionItemResult Result(
        DeletionPreflightItem item, DeletionOutcome outcome, string message, string detail)
        => new()
        {
            Path = item.Target.Path,
            Label = item.Label,
            Outcome = outcome,
            Message = message,
            Detail = detail,
        };
}
