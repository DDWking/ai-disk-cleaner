namespace AiDiskCleaner.Services;

/// <summary>
/// 决策层的日志。一个域一行，字段固定 —— 优化提示词和阈值时看这些数字，不靠感觉。
///
/// 刻意**不**由各域自己拼日志：那样字段名会漂，历史数据就没法比了。
/// </summary>
public static class JevLog
{
    /// <summary>一次批量判定的收尾统计。</summary>
    public static void Summary(
        string domain,
        int asked,
        int applied,
        int unsure,
        int unknown,
        int skipped,
        int calls,
        int inputTokens,
        double cost,
        List<double> sortedConfidences,
        Dictionary<string, int> distribution)
    {
        double median = sortedConfidences.Count > 0
            ? sortedConfidences[sortedConfidences.Count / 2]
            : 0;
        string dist = string.Join(",", distribution
            .OrderByDescending(x => x.Value)
            .Select(x => x.Key + ":" + x.Value));

        AppLog.Info("Jev",
            $"domain={domain} asked={asked} applied={applied} unsure={unsure} unknown={unknown} "
            + $"skipped={skipped} calls={calls} in={inputTokens} cost=${cost:0.000000} "
            + $"medianConf={median:0.00} dist={dist}");
    }

    /// <summary>这一批把端点的上下文撑爆了，正在拆两半重问。</summary>
    public static void BatchTooLarge(string domain, int count, Exception ex)
        => AppLog.Warn("Jev", $"domain={domain} batch too large ({count} items), splitting in half | {ex.Message}");

    /// <summary>请求在本地就被硬限制挡下来了（**没有发出去**）。</summary>
    public static void LimitRejected(string domain, DecisionLimitException ex)
        => AppLog.Warn("Jev", $"domain={domain} request rejected before sending | {ex.Message}");
}
