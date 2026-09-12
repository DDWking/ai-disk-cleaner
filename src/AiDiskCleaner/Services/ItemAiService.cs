using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 逐项 AI 分析的编排：缓存 → 排队（有限并发）→ 请求 → 解析 → 回填缓存。
///
/// 设计取舍：
/// <list type="bullet">
/// <item>**按需**：只有用户点了某一项的 AI 按钮才发请求，扫描完成后不自动发。</item>
/// <item>**范围有限**：一个文件就只分析那个文件；一个位置就只分析那个位置，
///       不会静默扩大到整个用途。</item>
/// <item>**有限并发 + 超时**：密集点击不会打出一堆并发请求。</item>
/// <item>**缓存**：键含项目身份、元数据版本、提示词版本与脱敏配置，任一项变了就过期。</item>
/// <item>**可取消**：取消会一路传到 HTTP / sidecar。</item>
/// </list>
/// </summary>
public sealed class ItemAiService
{
    /// <summary>同时最多几个请求（密集点击时多余的在队列里等）。</summary>
    public const int MaxConcurrent = 2;

    /// <summary>单项超时。逐项分析不该让人等两分钟。</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);
    private readonly Dictionary<ItemAiCacheKey, ItemAiResult> _cache = new();
    private readonly object _lock = new();

    public int CacheCount { get { lock (_lock) return _cache.Count; } }

    /// <summary>只看缓存，不发请求。</summary>
    public ItemAiResult? TryGetCached(ItemAiCacheKey key)
    {
        lock (_lock)
            return _cache.TryGetValue(key, out var hit) ? hit with { FromCache = true } : null;
    }

    public void Store(ItemAiCacheKey key, ItemAiResult result)
    {
        lock (_lock) _cache[key] = result;
    }

    /// <summary>换了扫描根/设置后清掉缓存，避免拿旧盘的结果。</summary>
    public void ClearCache()
    {
        lock (_lock) _cache.Clear();
    }

    public void DropExpired(ItemAiRequest r, string configSignature)
    {
        long v = ItemAiPrompt.VersionOf(r, configSignature);
        lock (_lock)
        {
            foreach (var k in _cache.Keys.Where(k => k.ScopeKey == r.ScopeKey && k.Version != v).ToList())
                _cache.Remove(k);
        }
    }

    /// <summary>
    /// 分析一项。返回 null 结果表示被取消。
    /// 调用方负责把结果贴回界面；这里不碰任何 UI 状态。
    /// </summary>
    /// <param name="groups">
    /// 本地算出来的候选项分组。有值时只把**分组汇总数字**发给模型，
    /// 让它针对每个子组给一句建议，而不是对整个目录下笼统结论。
    /// </param>
    public async Task<ItemAiResult?> AnalyzeAsync(
        ItemAiRequest request,
        AiProviderCfg? provider,
        string? model,
        bool sendFullPaths,
        string configSignature,
        CancellationToken ct)
    {
        var key = new ItemAiCacheKey(request.ScopeKey, ItemAiPrompt.VersionOf(request, configSignature));

        if (TryGetCached(key) is { } cached)
        {
            AppLog.Info("Ai", $"op=item-ai stage=cache scope={request.ScopeKey}");
            return cached;
        }

        double queueMs;
        var swQueue = System.Diagnostics.Stopwatch.StartNew();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        swQueue.Stop();
        queueMs = swQueue.Elapsed.TotalMilliseconds;

        try
        {
            ct.ThrowIfCancellationRequested();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);

            string user = ItemAiPrompt.BuildUser(request, sendFullPaths);
            var turns = new List<AiMsg> { new() { Role = "user", Text = user } };

            var swSend = System.Diagnostics.Stopwatch.StartNew();
            // 单项分析同样只走一轮：这是纯文本判定，不需要多轮工具调查。
            var reply = await AiClient.StreamAsync(
                provider, model, Loc.AiItemSystem, turns, _ => { }, timeoutCts.Token, maxTurns: 1)
                .ConfigureAwait(false);
            swSend.Stop();

            var swParse = System.Diagnostics.Stopwatch.StartNew();
            var result = ItemAiPrompt.Parse(reply.Text, key.Version, queueMs,
                swSend.Elapsed.TotalMilliseconds, 0, AiGateway.LastStatus.Channel);
            swParse.Stop();
            result = result with { ParseMs = swParse.Elapsed.TotalMilliseconds };

            Store(key, result);

            AppLog.Info("Ai", $"op=item-ai stage=done scope={request.ScopeKey} folder={request.IsFolder} "
                + $"suggest={result.Suggestion} "
                + $"queueMs={queueMs:0} sendMs={result.SendMs:0} "
                + $"parseMs={result.ParseMs:0} raw={result.Raw.Length} channel={result.Channel}");
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }
}
