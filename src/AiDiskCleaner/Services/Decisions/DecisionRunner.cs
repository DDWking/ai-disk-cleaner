namespace AiDiskCleaner.Services;

/// <summary>
/// 批量判定的执行器：一个域只写「出题 / 拼 state / 回写」，其余规则全在这里，
/// 所以**只需要写一遍**：
///
/// <list type="number">
/// <item><b>自适应步长。</b>从 <see cref="IDecisionSet{TItem}.AdaptiveStart"/> 起步，
/// 成功一次翻倍、撞上限减半。为什么不直接发最大批：别人可能填一个 32k 上下文的端点，
/// 一上来发 80 条就整批 400；从 20 条起步只多花几次往返，而一旦确认放得下它会自己长大。</item>
/// <item><b>撑爆就拆两半。</b>命中上下文类报错时递归拆半，而不是整批失败 —— 总花费一样。</item>
/// <item><b>换代即停写。</b><paramref name="stillCurrent"/> 在<b>发请求前与收到回复后各查一次</b>：
/// 等待期间用户可能刚换了扫描，旧结果绝不许贴到新扫描上。</item>
/// <item><b>没结论也要记「问过了」。</b>否则那一批看起来仍然「没结论」，会被反复追问。</item>
/// <item><b>低置信不丢。</b>标成「拿不准」交给用户，而不是静默扔掉。</item>
/// </list>
///
/// 出站计数、限流、重试、超时都在 <see cref="AiGateway.DecideAsync"/> 那一层，
/// 这里不重复实现 —— 它仍然是全进程唯一的出站闸门。
/// </summary>
public static class DecisionRunner
{
    /// <summary>
    /// 步长的下限。撞上限就一直减半，但不许减到 0（那会死循环）。
    /// </summary>
    public const int AdaptiveMin = 5;

    /// <param name="set">判定域。</param>
    /// <param name="items">要判定的条目（调用方已经筛过「该问的」）。</param>
    /// <param name="provider">判定通道的供应商。</param>
    /// <param name="model">判定模型 id。</param>
    /// <param name="stillCurrent">
    /// 每写回一批之前问一次：扫描有没有换代、这一页还在不在。返回 false 就停止写入。
    /// 为 null 表示调用方自己保证。
    /// </param>
    public static async Task<DecisionOutcome> RunAsync<TItem>(
        IDecisionSet<TItem> set,
        IReadOnlyList<TItem> items,
        AiProviderCfg provider,
        string model,
        Func<bool>? stillCurrent,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(provider);

        if (items.Count == 0) return new DecisionOutcome(0, 0, 0, 0, 0, 0, 0, 0);

        int applied = 0, unsure = 0, unknown = 0, skipped = 0, calls = 0, inTokens = 0;
        double cost = 0;
        int done = 0;
        var dist = new Dictionary<string, int>(StringComparer.Ordinal);
        var conf = new List<double>();

        // 返回：这一批是不是**第一次就成功**了 —— 外层据此调步长。
        async Task<bool> AskAsync(List<TItem> chunk)
        {
            if (chunk.Count == 0) return true;
            ct.ThrowIfCancellationRequested();
            if (stillCurrent != null && !stillCurrent()) return true;

            // 问题名只算一次，出题与回写共用同一份 —— 两次调用之间不可能漂
            var keys = set.KeysFor(chunk);
            if (keys.Count != chunk.Count)
            {
                throw new InvalidOperationException(
                    $"{set.Name}: KeysFor 返回 {keys.Count} 个名字，但这一批有 {chunk.Count} 条");
            }

            AiDecisionRequest request;
            try
            {
                request = BuildRequest(set, provider, model, chunk, keys);
            }
            catch (DecisionLimitException ex)
            {
                // 本地就挡住了，**没有发出去** —— 让它冒到界面上，别冒充成网络失败
                JevLog.LimitRejected(set.Name, ex);
                throw;
            }

            AiDecisionReply reply;
            try
            {
                reply = await AiGateway.DecideAsync(request, ct);
            }
            catch (Exception ex) when (ex is not DecisionLimitException
                                       && chunk.Count > 1
                                       && IsTooLargeForModel(ex))
            {
                JevLog.BatchTooLarge(set.Name, chunk.Count, ex);
                int half = chunk.Count / 2;
                await AskAsync(chunk.GetRange(0, half));
                await AskAsync(chunk.GetRange(half, chunk.Count - half));
                return false;
            }

            calls++;
            inTokens += reply.InputTokens;
            cost += reply.Cost;

            // 结果回来之后再确认一次：等待期间可能刚换了扫描。
            if (stillCurrent != null && !stillCurrent()) return true;

            for (int i = 0; i < chunk.Count; i++)
            {
                // **先记「问过了」**，不管下面拿没拿到结论
                set.MarkAsked(chunk[i]);

                reply.Answers.TryGetValue(keys[i], out var answer);
                var result = set.Apply(chunk[i], answer);

                switch (result.Kind)
                {
                    case DecisionWriteBack.Applied: applied++; break;
                    case DecisionWriteBack.Unsure: unsure++; break;
                    case DecisionWriteBack.Skipped: skipped++; break;
                    default: unknown++; break;
                }

                if (result.Label.Length > 0)
                    dist[result.Label] = dist.TryGetValue(result.Label, out var n) ? n + 1 : 1;
                if (result.Confidence > 0) conf.Add(result.Confidence);
            }

            done += chunk.Count;
            progress?.Report(set.ProgressText(done, items.Count));
            return true;
        }

        // **自适应步长**：从 AdaptiveStart 起步，成功一次翻倍、撞上限减半。
        //
        // 地板取 min(AdaptiveMin, AdaptiveStart)：域可以把起点放得比 5 更低（例如 1 条一条地问），
        // 那就该以它自己的起点为地板 —— 否则「起点 1」会被抬到 5，缩步长时反而长大。
        int floor = Math.Min(AdaptiveMin, set.AdaptiveStart);
        int size = Math.Max(set.AdaptiveStart, 1);
        for (int start = 0; start < items.Count;)
        {
            ct.ThrowIfCancellationRequested();
            if (stillCurrent != null && !stillCurrent()) break;
            progress?.Report(set.ProgressText(done, items.Count));

            int take = Math.Min(size, items.Count - start);
            var chunk = new List<TItem>(take);
            for (int i = 0; i < take; i++) chunk.Add(items[start + i]);

            bool firstTry = await AskAsync(chunk);
            // 无论拆没拆，这一批都已经处理完了（拆出来的两半也处理完了），所以步长照常往前推
            start += chunk.Count;
            size = firstTry
                ? Math.Min(set.MaxPerRequest, size * 2)
                : Math.Max(floor, size / 2);
        }

        if (conf.Count > 0) conf.Sort();
        JevLog.Summary(set.Name, items.Count, applied, unsure, unknown, skipped, calls, inTokens, cost, conf, dist);

        return new DecisionOutcome(items.Count, applied, unsure, unknown, skipped, calls, inTokens, cost);
    }

    /// <summary>
    /// 这一批是不是把模型的上下文撑爆了。判据放宽一点：各家端点的措辞不一样
    /// （<c>max tokens exceeded</c> / <c>context length</c> / <c>too large</c>），
    /// 漏判的代价是整批失败，误判的代价只是多拆一次。
    /// </summary>
    public static bool IsTooLargeForModel(Exception ex)
    {
        string s = ex.ToString();
        return s.Contains("max tokens", StringComparison.OrdinalIgnoreCase)
               || s.Contains("context length", StringComparison.OrdinalIgnoreCase)
               || s.Contains("too many tokens", StringComparison.OrdinalIgnoreCase)
               || s.Contains("maximum context", StringComparison.OrdinalIgnoreCase);
    }

    static AiDecisionRequest BuildRequest<TItem>(
        IDecisionSet<TItem> set,
        AiProviderCfg provider,
        string model,
        List<TItem> chunk,
        IReadOnlyList<string> keys)
    {
        var questions = new Dictionary<string, AiDecisionQuestion>(chunk.Count, StringComparer.Ordinal);

        for (int i = 0; i < chunk.Count; i++)
        {
            // 问题名必须唯一：答案是按名字回填的，重名会静默覆盖掉一条
            if (!questions.TryAdd(keys[i], set.QuestionFor(i, chunk[i])))
            {
                throw new InvalidOperationException($"{set.Name}: 问题名重复 '{keys[i]}'");
            }
        }

        var request = new AiDecisionRequest
        {
            Provider = provider,
            Model = model,
            State = set.StateFor(chunk),
            Questions = questions,
        };

        // 硬限制在**发出去之前**校验（选项数、档位数、字符预算）
        JevLimits.Validate(request);
        return request;
    }
}
