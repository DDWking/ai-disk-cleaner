using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 删除协调：预检 → （用户确认）→ 逐项执行 → 汇总文案。
/// 主界面只负责弹确认框和刷新界面，删除决策全在这里，可以独立测试。
///
/// 依赖通过构造函数注入，模块内部不 new 全局对象。
/// </summary>
public sealed class DeletionCoordinator
{
    private readonly DeletionPreflight _preflight;
    private readonly DeletionExecutor _executor;

    public DeletionCoordinator(DeletionPreflight? preflight = null, DeletionExecutor? executor = null)
    {
        _preflight = preflight ?? new DeletionPreflight();
        _executor = executor ?? new DeletionExecutor();
    }

    /// <summary>最近一次执行的批量结果。</summary>
    public DeletionBatchResult? LastBatch { get; private set; }

    /// <summary>
    /// 第一步：出计划。这一步**不删任何东西**，只是把能不能删、为什么不能删算清楚，
    /// 好让确认框把「这一批里有几项其实删不了」说在用户点确定之前。
    /// </summary>
    public DeletionPlan Plan(IEnumerable<DeletionTarget> targets, CancellationToken ct = default)
        => _preflight.CreatePlan(targets.ToList(), sensitiveConfirmed: false, ct);

    /// <summary>确认框里要显示的补充说明。</summary>
    public static string PlanNote(DeletionPlan plan)
    {
        var p = plan.Preflight;
        return Loc.DeletePreflightNote(p.ReadyCount, p.BlockedCount, p.MissingCount,
            p.ChangedCount, p.InUseCount, p.SensitiveCount);
    }

    /// <summary>
    /// 第二步：用户点过「继续」之后才执行。
    /// <paramref name="allowSensitive"/> 决定敏感位置（Program Files / 用户目录一级）要不要一起删。
    /// </summary>
    public DeletionBatchResult Execute(
        DeletionPlan plan,
        bool allowSensitive,
        IProgress<DeletionItemResult>? progress = null,
        CancellationToken ct = default)
    {
        plan.UserConfirmed = true;
        plan.AllowSensitive = allowSensitive;
        var batch = _executor.Execute(plan, progress, ct);
        LastBatch = batch;
        LogBatch(batch);
        return batch;
    }

    /// <summary>结果汇报：逐项结局一条都不丢；成功项只报总数。</summary>
    public static List<string> Summarize(DeletionBatchResult batch, DeletionPlan? plan = null)
    {
        var lines = new List<string>
        {
            Loc.DeleteResultHead(batch.Recycled, FileEntry.FormatSize(batch.FreedBytes), batch.Total),
        };

        foreach (var g in batch.Results
                     .Where(r => r.Outcome != DeletionOutcome.Recycled)
                     .GroupBy(r => r.Outcome)
                     .OrderBy(g => (int)g.Key))
        {
            var names = g.Take(8).Select(r => r.Label).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            string suffix = string.IsNullOrWhiteSpace(g.First().Message) ? "" : " — " + g.First().Message;
            lines.Add(Loc.DeleteResultGroup(g.Key, g.Count(), string.Join("、", names)) + suffix);
        }

        if (batch.Total == 0) lines.Add(Loc.NothingSelected);
        return lines;
    }

    /// <summary>把真正删掉的路径集合拿出来，交给界面摘树/清列表。</summary>
    public static HashSet<string> RecycledPaths(DeletionBatchResult batch)
        => new(batch.RecycledPaths, StringComparer.OrdinalIgnoreCase);

    private static void LogBatch(DeletionBatchResult batch)
    {
        AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Info, "Delete", "", "batch",
            $"recycled={batch.Recycled} failed={batch.Failed} protected={batch.Protected} inuse={batch.InUse} "
            + $"missing={batch.NotFound} changed={batch.PathChanged}/{batch.Modified} denied={batch.AccessDenied} "
            + $"skipped={batch.Skipped} redundant={batch.Redundant} canceled={batch.Canceled}",
            batch.Elapsed.TotalMilliseconds, batch.Total));

        foreach (var r in batch.Results.Where(x => x.Outcome != DeletionOutcome.Recycled))
        {
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Delete", "", r.OutcomeText,
                PathRedactor.Redact(r.Path) + " | " + r.Message + " | " + r.Detail));
        }
    }
}
