using System.Diagnostics;

namespace AiDiskCleaner.Services;

/// <summary>
/// 分段计时。先量再改 —— 卡顿到底卡在排序、布局、读盘还是内存压力，只有分段数字能回答。
/// 每段一行写进 app.log，用 <see cref="Summary"/> 拼出一整条流水线耗时。
/// </summary>
public sealed class PerfTrace
{
    private readonly List<(string Name, double Ms, long? Count)> _marks = new();
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly string _operation;

    public PerfTrace(string operation) => _operation = operation;

    /// <summary>量一段同步代码。</summary>
    public T Measure<T>(string name, Func<T> body)
    {
        var sw = Stopwatch.StartNew();
        try { return body(); }
        finally { Mark(name, sw.Elapsed.TotalMilliseconds, null); }
    }

    /// <summary>量一段同步代码（无返回值）。</summary>
    public void Measure(string name, Action body)
    {
        var sw = Stopwatch.StartNew();
        try { body(); }
        finally { Mark(name, sw.Elapsed.TotalMilliseconds, null); }
    }

    /// <summary>异步版本。</summary>
    public async Task<T> MeasureAsync<T>(string name, Func<Task<T>> body)
    {
        var sw = Stopwatch.StartNew();
        try { return await body(); }
        finally { Mark(name, sw.Elapsed.TotalMilliseconds, null); }
    }

    public void Mark(string name, double ms, long? count)
    {
        _marks.Add((name, ms, count));
        AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Perf", _operation, name,
            $"elapsedMs={ms:0.0}", ms, count is { } n ? (int)Math.Min(n, int.MaxValue) : null));
    }

    /// <summary>「阶段=毫秒」一行，方便直接对比两次扫描。</summary>
    public string Summary()
    {
        _total.Stop();
        var parts = _marks.Select(m => $"{m.Name}={m.Ms:0.0}");
        return $"{_operation} total={_total.Elapsed.TotalMilliseconds:0.0}ms | " + string.Join(" ", parts);
    }

    /// <summary>把汇总写进日志（信息级，能在诊断包里看到）。</summary>
    public void Flush()
        => AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Info, "Perf", _operation, "summary",
            Summary(), _total.Elapsed.TotalMilliseconds));

    /// <summary>取某一段的毫秒数（测试用）。</summary>
    public double Get(string name)
        => _marks.Where(m => m.Name == name).Select(m => m.Ms).DefaultIfEmpty(0).Sum();
}
