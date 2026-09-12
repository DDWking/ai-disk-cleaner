namespace AiDiskCleaner.Services;

/// <summary>当前在干什么。界面用这个决定停止按钮和进度条，不再靠零散的 bool 拼。</summary>
public enum WorkPhase
{
    Idle,
    /// <summary>扫盘（MFT / 递归）。</summary>
    Scanning,
    /// <summary>清理规则分析。</summary>
    Analyzing,
    /// <summary>重复文件分阶段哈希。</summary>
    Duplicates,
    /// <summary>收尾（快照、软件占用）。</summary>
    Finishing,
    /// <summary>有结果，但后续步骤被取消了。</summary>
    Canceled,
    Done,
}

/// <summary>
/// 可取消阶段的登记簿。
///
/// 存在的理由很具体：以前 `RunScan` 的 finally 会无条件把停止按钮藏起来，
/// 而 `FinishScan` 是 fire-and-forget 地启动分析 —— 于是「扫描结束、分析还在跑」的那段时间里
/// 用户根本没有停止入口。现在只要还有阶段登记在册，停止按钮就可用。
///
/// 不碰 WPF，可以离线测。
/// </summary>
public sealed class WorkState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WorkPhase> _stages = new(StringComparer.Ordinal);

    /// <summary>阶段集合变化时触发（UI 线程上重新算按钮状态）。</summary>
    public event EventHandler? Changed;

    public bool Busy
    {
        get { lock (_gate) return _stages.Count > 0; }
    }

    /// <summary>用户点过停止，但阶段可能还没退干净。</summary>
    public bool CancelRequested { get; private set; }

    /// <summary>还有阶段在跑，且用户还没点停止 → 停止按钮要亮着。</summary>
    public bool CanStop
    {
        get { lock (_gate) return _stages.Count > 0 && !CancelRequested; }
    }

    /// <summary>登记在册的阶段名（日志/诊断用）。</summary>
    public IReadOnlyList<string> Stages
    {
        get { lock (_gate) return _stages.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList(); }
    }

    /// <summary>
    /// 最「靠前」的阶段决定界面显示什么。按流水线顺序取第一个在跑的。
    /// </summary>
    public WorkPhase Phase
    {
        get
        {
            lock (_gate)
            {
                if (_stages.Count == 0) return CancelRequested ? WorkPhase.Canceled : WorkPhase.Idle;
                if (_stages.ContainsKey(StageNames.Scan)) return WorkPhase.Scanning;
                if (_stages.ContainsKey(StageNames.Analyze)) return WorkPhase.Analyzing;
                if (_stages.ContainsKey(StageNames.Duplicates)) return WorkPhase.Duplicates;
                return WorkPhase.Finishing;
            }
        }
    }

    /// <summary>被取消过、而且还有结果可看 → 界面要标「未完成部分」。</summary>
    public bool ShowPartialResult => CancelRequested;

    /// <summary>登记一个可取消阶段。用 using 包起来，退出即注销。</summary>
    public IDisposable Begin(string stage, WorkPhase phase)
    {
        lock (_gate) { _stages[stage] = phase; }
        Raise();
        return new Handle(this, stage);
    }

    /// <summary>用户点了停止：标记，并让界面把按钮切成「正在取消」。</summary>
    public void RequestCancel()
    {
        bool changed;
        lock (_gate)
        {
            changed = !CancelRequested;
            CancelRequested = true;
        }
        if (changed) Raise();
    }

    /// <summary>新一轮工作开始，清掉上一次的取消标记。</summary>
    public void ResetCancel()
    {
        bool changed;
        lock (_gate)
        {
            changed = CancelRequested;
            CancelRequested = false;
        }
        if (changed) Raise();
    }

    /// <summary>界面文案：正在取消 / 忙什么 / 空闲。</summary>
    public string Describe()
    {
        lock (_gate)
        {
            if (_stages.Count == 0) return CancelRequested ? "canceled" : "idle";
            string what = string.Join("+", _stages.Keys.OrderBy(x => x, StringComparer.Ordinal));
            return CancelRequested ? "canceling:" + what : what;
        }
    }

    private void End(string stage)
    {
        bool changed;
        lock (_gate) { changed = _stages.Remove(stage); }
        if (changed) Raise();
    }

    private void Raise()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch { /* 界面回调失败不能影响任务收尾 */ }
    }

    private sealed class Handle : IDisposable
    {
        private WorkState? _owner;
        private readonly string _stage;

        public Handle(WorkState owner, string stage)
        {
            _owner = owner;
            _stage = stage;
        }

        public void Dispose()
        {
            var o = _owner;
            _owner = null;
            o?.End(_stage);
        }
    }
}

/// <summary>阶段名。集中放一处，避免字符串写错导致「停止按钮莫名其妙消失」。</summary>
public static class StageNames
{
    public const string Scan = "scan";
    public const string Analyze = "analyze";
    public const string Duplicates = "duplicates";
    public const string Snapshot = "snapshot";
    public const string Apps = "apps";
}
