namespace AiDiskCleaner.Services;

/// <summary>
/// 批量判定的执行器：一个域只写「出题 / 拼 state / 回写」，其余规则全在这里，
/// 所以**只需要写一遍**：
///
/// <list type="number">
/// <item><b>自适应步长。</b>从 <see cref="IDecisionSet{TItem}.AdaptiveStart"/> 起步，
/// 成功一次翻倍、撞上限减半。为什么不直接发最大批：别人可能填一个 32k 上下文的端点，
/// 一上来发最大批就整批 400；从小批起步只多花几次往返，而一旦确认放得下它会自己长大。
/// （一次只问一条的域起点就是 1，步长逻辑照常跑、只是不再翻倍。）</item>
/// <item><b>撑爆就拆两半。</b>命中上下文类报错时递归拆半，而不是整批失败 —— 总花费一样。</item>
/// <item><b>换代即停写。</b><paramref name="stillCurrent"/> 在<b>发请求前与收到回复后各查一次</b>：
/// 等待期间用户可能刚换了扫描，旧结果绝不许贴到新扫描上。</item>
/// <item><b>没结论也要记「问过了」。</b>否则那一批看起来仍然「没结论」，会被反复追问。</item>
/// <item><b>低置信不丢。</b>标成「拿不准」交给用户，而不是静默扔掉。</item>
/// <item><b>并发是域自己声明的。</b><see cref="IDecisionSet{TItem}.MaxConcurrency"/> 默认 1
/// （串行，行为不变）；&gt;1 时走 <see cref="RunConcurrentAsync"/> 用
/// <see cref="System.Threading.SemaphoreSlim"/> 限流。只有「每批各自独立」的域才该调大它
/// —— 并发下计数/分布/进度全部加锁，且**不用 <c>ConfigureAwait(false)</c>**：
/// <c>Apply</c> 会碰 WPF 绑定的对象，必须回到调用方的上下文。</item>
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

        // 并发时这些计数会被多个工作线程同时改。**不能指望 SynchronizationContext**：
        // 离线回归在普通控制台进程里跑，await 之后的续体落在线程池上。
        var statsGate = new object();

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

            lock (statsGate)
            {
                calls++;
                inTokens += reply.InputTokens;
                cost += reply.Cost;
            }

            // 结果回来之后再确认一次：等待期间可能刚换了扫描。
            if (stillCurrent != null && !stillCurrent()) return true;

            // 这一批的记账先在本地累积，最后一次性并进共享计数 ——
            // 锁只覆盖「加法」，不覆盖 MarkAsked / Apply（它们碰的是各自的条目，
            // 并发域下每批的条目互不相交）。
            int cApplied = 0, cUnsure = 0, cUnknown = 0, cSkipped = 0;
            var labels = new List<string>();
            var confidences = new List<double>();

            for (int i = 0; i < chunk.Count; i++)
            {
                // **先记「问过了」**，不管下面拿没拿到结论
                set.MarkAsked(chunk[i]);

                reply.Answers.TryGetValue(keys[i], out var answer);
                var result = set.Apply(chunk[i], answer);

                switch (result.Kind)
                {
                    case DecisionWriteBack.Applied: cApplied++; break;
                    case DecisionWriteBack.Unsure: cUnsure++; break;
                    case DecisionWriteBack.Skipped: cSkipped++; break;
                    default: cUnknown++; break;
                }

                if (result.Label.Length > 0) labels.Add(result.Label);
                if (result.Confidence > 0) confidences.Add(result.Confidence);
            }

            int doneNow;
            lock (statsGate)
            {
                applied += cApplied;
                unsure += cUnsure;
                unknown += cUnknown;
                skipped += cSkipped;
                foreach (var label in labels)
                    dist[label] = dist.TryGetValue(label, out var n) ? n + 1 : 1;
                conf.AddRange(confidences);
                done += chunk.Count;
                doneNow = done;
            }

            progress?.Report(set.ProgressText(doneNow, items.Count));
            return true;
        }

        if (set.MaxConcurrency > 1)
        {
            await RunConcurrentAsync(set, items, stillCurrent, progress, ct, AskAsync);
        }
        else
        {
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
        }

        if (conf.Count > 0) conf.Sort();
        JevLog.Summary(set.Name, items.Count, applied, unsure, unknown, skipped, calls, inTokens, cost, conf, dist);

        return new DecisionOutcome(items.Count, applied, unsure, unknown, skipped, calls, inTokens, cost);
    }

    /// <summary>
    /// 并发跑批：<paramref name="set"/> 声明了 <see cref="IDecisionSet{TItem}.MaxConcurrency"/> &gt; 1 时走这里。
    ///
    /// <b>什么时候可以并发。</b>只有「每批各自独立」的域 —— state 与选项表不跨批共享，
    /// 请求之间也没有顺序依赖（文件夹用途域就是：一批一个目录，一份独立摘要）。
    /// 串行问几百个目录会把界面卡很久，而 Jev 端点是 ~1200 req/min、单次延迟通常 &lt;0.5s。
    ///
    /// <b>并发下仍然逐条成立的四件事</b>（都在 <c>AskAsync</c> 里，和串行版共用同一份实现）：
    /// <list type="bullet">
    /// <item>发请求前与收到回复后**各查一次** <c>stillCurrent</c>；</item>
    /// <item><c>MarkAsked</c> 先于 <c>Apply</c>；</item>
    /// <item>撑爆上下文仍拆两半（拆出来的两半在这个工作线程里顺序跑）；</item>
    /// <item><see cref="DecisionLimitException"/> 原样冒到调用方 —— 并**立刻停止再取新块**，
    ///   本地校验都不过的请求没必要继续发。</item>
    /// </list>
    ///
    /// 步长逻辑保留（成功翻倍、撞上限减半），只是改由取块时那把锁串行决定。
    /// 这里**刻意不用 <c>ConfigureAwait(false)</c>**：<c>Apply</c> 会碰 WPF 绑定的对象，
    /// 必须回到调用方的上下文。
    /// </summary>
    static async Task RunConcurrentAsync<TItem>(
        IDecisionSet<TItem> set,
        IReadOnlyList<TItem> items,
        Func<bool>? stillCurrent,
        IProgress<string>? progress,
        CancellationToken ct,
        Func<List<TItem>, Task<bool>> ask)
    {
        var gate = new object();
        using var slots = new SemaphoreSlim(set.MaxConcurrency);
        int cursor = 0;
        int size = Math.Max(set.AdaptiveStart, 1);
        int floor = Math.Min(AdaptiveMin, set.AdaptiveStart);
        bool stopped = false;

        async Task WorkerAsync()
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (stillCurrent != null && !stillCurrent())
                {
                    lock (gate) stopped = true;
                    return;
                }

                List<TItem> chunk;
                lock (gate)
                {
                    if (stopped || cursor >= items.Count) return;
                    int take = Math.Min(size, items.Count - cursor);
                    chunk = new List<TItem>(take);
                    for (int i = 0; i < take; i++) chunk.Add(items[cursor + i]);
                    cursor += take;
                }

                await slots.WaitAsync(ct);
                bool firstTry;
                try
                {
                    firstTry = await ask(chunk);
                }
                catch
                {
                    // 任何失败都停止再取新块：本地校验不过（DecisionLimitException）没必要继续发；
                    // 网络/鉴权失败时界面会把整批标成失败，其余工作线程再发只是白花钱。
                    // 异常原样冒到调用方（DecisionLimitException 不冒充网络失败）。
                    lock (gate) stopped = true;
                    throw;
                }
                finally
                {
                    slots.Release();
                }

                lock (gate)
                {
                    size = firstTry
                        ? Math.Min(set.MaxPerRequest, size * 2)
                        : Math.Max(floor, size / 2);
                }
            }
        }

        int workers = Math.Min(set.MaxConcurrency, items.Count);
        var tasks = new Task[workers];
        for (int i = 0; i < workers; i++) tasks[i] = WorkerAsync();
        await Task.WhenAll(tasks);
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
