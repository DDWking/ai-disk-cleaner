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

/// <summary>
/// 在飞请求登记表：按「来源 + 稳定标识」（<see cref="ItemAiView.IsolationKey"/>）登记取消源。
///
/// 存在的理由很具体：清理页的一个文件与整理页的一个文件夹可能**同路径**。
/// 如果只按路径登记，两次请求会共用一条记录：取消一个会误取消另一个，
/// 而且先结束的那个会顺手把新请求的登记删掉（之后再也取消不了）。
/// 这里用 <see cref="RemoveIfCurrent"/> 保证**只有登记的还是自己那一条时才移除**。
///
/// 线程模型：只在 UI 线程访问（调用方保证）。
/// </summary>
public sealed class ItemAiRunningRegistry
{
    private readonly Dictionary<string, CancellationTokenSource> _map = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _map.Count;

    public void Add(string key, CancellationTokenSource cts) => _map[key] = cts;

    public bool TryGet(string key, out CancellationTokenSource? cts)
        => _map.TryGetValue(key, out cts);

    /// <summary>只有当前登记的仍是 <paramref name="cts"/> 时才移除；返回是否真的移除了。</summary>
    public bool RemoveIfCurrent(string key, CancellationTokenSource cts)
    {
        if (!_map.TryGetValue(key, out var current) || !ReferenceEquals(current, cts)) return false;
        _map.Remove(key);
        return true;
    }

    /// <summary>当前所有在飞请求的快照（用于「全部停止」）。</summary>
    public IReadOnlyList<CancellationTokenSource> Snapshot() => _map.Values.ToList();

    /// <summary>取消全部在飞请求，但不清空登记（各请求结束时会各自按身份移除）。</summary>
    public void CancelAll()
    {
        foreach (var cts in _map.Values.ToList())
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    public void Clear() => _map.Clear();
}
