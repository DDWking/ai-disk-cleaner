using System.Diagnostics;
using System.IO;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>
/// 扫完后按规则出可清理项。不走大模型。
///
/// 这里只做三件事：走一遍目录树拿到文件/目录清单 → 把规则一条条跑完 → 汇总成报告。
/// 判定逻辑都在 <see cref="ICleanRule"/> 实现里，每条规则可以单独测。
/// </summary>
public static class CleanAnalyzer
{
    /// <summary>
    /// 规则表。顺序有含义：先命中的规则决定分类和风险（sink 按路径去重），
    /// 所以不要随手调整顺序 —— 那会改变默认勾选。
    ///
    /// 重复检测**不在**这里：它要读文件内容，是最慢的一段，应该作为可独立取消的后续阶段跑
    /// （见 <see cref="Analyze"/> 的 includeDuplicates 参数）。
    /// </summary>
    private static readonly ICleanRule[] Rules =
    {
        new TempCacheRule(),
        new RecycleBinRule(),
        new LargeFileRule(),
        new OldFileRule(),
        new EmptyFolderRule(),
        new LongPathRule(),
        new BrokenShortcutRule(),
        new ScanCompareRule(),
    };

    /// <summary>上一次分析里每条规则的耗时与产出，用于诊断「哪条规则慢/挂了」。</summary>
    public static IReadOnlyList<CleanRuleTiming> LastRuleTimings { get; private set; } = Array.Empty<CleanRuleTiming>();

    /// <summary>
    /// 扫完后按规则出可清理项。
    /// </summary>
    /// <param name="includeDuplicates">
    /// 是否把重复检测也一起跑完。默认 true（离线检查 / 一次性调用方用）。
    /// 界面走 **false**：先出普通清理结果，重复项由 <c>DuplicateScanService</c> 单独跑完再并进来，
    /// 这样用户不用等读盘哈希就能看到列表，而且重复检测可以单独取消。
    /// </param>
    public static CleanReport Analyze(
        FileEntry root,
        ScanSnapshot? previous,
        CancellationToken ct,
        IProgress<ScanProgress>? progress = null,
        bool includeDuplicates = true)
    {
        var report = new CleanReport();
        var files = new List<FileEntry>(Math.Max(1024, root.FileCount));
        var dirs = new List<FileEntry>();
        progress?.Report(new ScanProgress(0, Loc.CleanWalk, 5));
        Walk(root, files, dirs, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new ScanProgress(files.Count, Loc.CleanRules, 20));

        var ctx = new CleanRuleContext
        {
            Root = root,
            Files = files,
            Dirs = dirs,
            Previous = previous,
            Ct = ct,
            Progress = progress,
        };
        var sink = new CleanRuleSink();
        var timings = new List<CleanRuleTiming>(Rules.Length + 1);

        foreach (var rule in Rules)
        {
            ct.ThrowIfCancellationRequested();
            RunRule(rule, ctx, sink, timings);
        }

        if (includeDuplicates)
        {
            ct.ThrowIfCancellationRequested();
            RunRule(new DuplicateFileRule(), ctx, sink, timings);
        }

        LastRuleTimings = timings;
        ReportTimings(timings);
        sink.ApplyTo(report);

        // 对比说明：老快照没有就提示「第一次扫描」，有就报总大小变化。
        report.CompareNote = previous == null
            ? Loc.CompareFirst
            : Loc.CompareSince(previous.ScannedAt, FileEntry.FormatSize(root.Size - previous.RootSize));

        report.CleanableBytes = report.Cleanable.Sum(x => x.Size);
        progress?.Report(new ScanProgress(files.Count, Loc.Analyzing, 100));
        return report;
    }

    /// <summary>跑一条规则：异常隔离（记日志继续），但取消原样抛出。</summary>
    private static void RunRule(ICleanRule rule, CleanRuleContext ctx, CleanRuleSink sink, List<CleanRuleTiming> timings)
    {
        int before = sink.TotalHits;
        var sw = Stopwatch.StartNew();
        string? error = null;
        // 先告诉汇总口这条规则的风险归属口径，再让它产出条目
        sink.UseRule(rule);
        try
        {
            rule.Evaluate(ctx, sink);
        }
        catch (OperationCanceledException)
        {
            // 取消是正常路径，不能被当成规则故障吞掉
            throw;
        }
        catch (Exception ex)
        {
            // 一条规则挂了不能阻断其他规则：记下来，继续跑下一条
            error = ex.GetType().Name;
            AppLog.Record("Clean", ex, "rule " + rule.Name);
        }
        finally
        {
            sw.Stop();
            timings.Add(new CleanRuleTiming(rule.Name, rule.Category, sw.Elapsed, sink.TotalHits - before, error));
        }
    }

    private static void ReportTimings(List<CleanRuleTiming> timings)
    {
        foreach (var t in timings)
        {
            AppLog.Write(new LogEntry(DateTime.UtcNow,
                t.Error == null ? LogLevel.Info : LogLevel.Warn, "Clean", "", t.Rule,
                (t.Error == null ? "ok" : "rule failed: " + t.Error), t.ElapsedMs, t.Hits));
        }
    }

    private static void Walk(FileEntry node, List<FileEntry> files, List<FileEntry> dirs, CancellationToken ct)
    {
        var stack = new Stack<FileEntry>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var n = stack.Pop();
            foreach (var c in n.ChildList)
            {
                if (c.IsFilesGroup)
                {
                    stack.Push(c);
                    continue;
                }
                if (c.IsDirectory)
                {
                    dirs.Add(c);
                    stack.Push(c);
                }
                else files.Add(c);
            }
        }
    }

    /// <summary>
    /// 规则结果的汇总口。负责按 (目标列表, 路径) 去重 ——
    /// 不同规则可能命中同一个文件，谁先命中谁说了算。
    /// </summary>
    private sealed class CleanRuleSink : ICleanRuleSink
    {
        private readonly Dictionary<CleanRuleTarget, HashSet<string>> _seen = new();
        private readonly Dictionary<CleanRuleTarget, List<CleanItem>> _items = new();
        private int _dupGroups;
        /// <summary>当前正在跑的规则。用来决定「风险归谁说了算」。</summary>
        private bool _riskAuthoritative;

        public int TotalHits { get; private set; }

        public CleanRuleSink()
        {
            foreach (CleanRuleTarget t in Enum.GetValues<CleanRuleTarget>())
            {
                _seen[t] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _items[t] = new List<CleanItem>();
            }
        }

        /// <summary>跑某条规则前调用：把该规则的风险归属口径带进条目工厂。</summary>
        public void UseRule(ICleanRule rule) => _riskAuthoritative = rule.RiskIsAuthoritative;

        public int Count(CleanRuleTarget target) => _items[target].Count;

        public void CountDuplicateGroup() => _dupGroups++;

        public bool Add(CleanRuleHit hit)
        {
            string key = hit.Entry.FullPath ?? "";
            if (key.Length == 0) return false;
            if (!_seen[hit.Target].Add(key)) return false; // 重复命中：保留第一条

            _items[hit.Target].Add(CleanItemFactory.Create(hit, _riskAuthoritative));
            TotalHits++;
            return true;
        }

        public void ApplyTo(CleanReport report)
        {
            report.Cleanable.AddRange(_items[CleanRuleTarget.Cleanable]);
            report.LargeFiles.AddRange(_items[CleanRuleTarget.Large]);
            report.OldFiles.AddRange(_items[CleanRuleTarget.Old]);
            report.EmptyFolders.AddRange(_items[CleanRuleTarget.EmptyFolders]);
            report.BrokenShortcuts.AddRange(_items[CleanRuleTarget.BrokenShortcuts]);
            report.LongPaths.AddRange(_items[CleanRuleTarget.LongPaths]);
            report.Duplicates.AddRange(_items[CleanRuleTarget.Duplicates]);
            report.Compare.AddRange(_items[CleanRuleTarget.Compare]);

            report.Cleanable.Sort((a, b) => b.Size.CompareTo(a.Size));
            report.Compare.Sort((a, b) => b.Size.CompareTo(a.Size));
            report.DupGroupCount += _dupGroups;
        }
    }
}
