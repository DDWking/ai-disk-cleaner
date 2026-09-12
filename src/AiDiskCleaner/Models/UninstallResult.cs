namespace AiDiskCleaner.Models;

/// <summary>一个软件的卸载结局。</summary>
public enum UninstallOutcome
{
    /// <summary>卸载程序正常结束。</summary>
    Uninstalled,
    /// <summary>卸载程序报错。</summary>
    Failed,
    /// <summary>用户在确认框里没选它 / 卸载程序自己跳过。</summary>
    SkippedByUser,
    /// <summary>受保护（系统组件、驱动、运行库、安全软件），本地硬拦截。</summary>
    Protected,
    /// <summary>到卸载这一步时发现已经不可卸载了（比如只剩 Windows 功能）。</summary>
    Kept,
    /// <summary>用户点了停止。</summary>
    Canceled,
}

/// <summary>逐项卸载结果。和删除一样：一项失败不能吞掉其他项的细节。</summary>
public sealed class UninstallItemResult
{
    public string AppId { get; set; } = "";
    public string Name { get; set; } = "";
    public UninstallOutcome Outcome { get; set; } = UninstallOutcome.Kept;
    /// <summary>用户可读原因（为什么跳过 / 为什么保留）。</summary>
    public string Message { get; set; } = "";
    /// <summary>技术细节：BCU 的原生状态、异常类型。</summary>
    public string Detail { get; set; } = "";
    public long FreedBytes { get; set; }
    public string OutcomeText => UninstallText.Outcome(Outcome);
}

/// <summary>一批卸载的汇总。</summary>
public sealed class UninstallBatchResult
{
    public List<UninstallItemResult> Results { get; } = new();
    public DateTime FinishedAt { get; set; } = DateTime.Now;

    public int Count(UninstallOutcome o) => Results.Count(x => x.Outcome == o);
    public int Uninstalled => Count(UninstallOutcome.Uninstalled);
    public int Failed => Count(UninstallOutcome.Failed);
    public int Skipped => Count(UninstallOutcome.SkippedByUser);
    public int Protected => Count(UninstallOutcome.Protected);
    public int Kept => Count(UninstallOutcome.Kept);
    public int Total => Results.Count;
    public long FreedBytes => Results.Sum(x => Math.Max(0, x.FreedBytes));

    /// <summary>需要告诉用户原因的项（失败 / 跳过 / 保留）。</summary>
    public List<UninstallItemResult> Attention => Results
        .Where(x => x.Outcome != UninstallOutcome.Uninstalled)
        .ToList();
}

/// <summary>结局 → 用户可读文案。放在模型层方便离线测试覆盖全部取值。</summary>
public static class UninstallText
{
    public static string Outcome(UninstallOutcome o) => o switch
    {
        UninstallOutcome.Uninstalled => "已卸载",
        UninstallOutcome.Failed => "卸载失败",
        UninstallOutcome.SkippedByUser => "用户跳过",
        UninstallOutcome.Protected => "本地硬拦截",
        UninstallOutcome.Kept => "保留",
        UninstallOutcome.Canceled => "已停止",
        _ => "未知",
    };
}
