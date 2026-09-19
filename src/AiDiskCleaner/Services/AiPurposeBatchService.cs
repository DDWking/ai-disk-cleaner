using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>
/// 一次批量归类的结果，用来给用户一句实话（而不是「已完成」）。
///
/// <paramref name="Unsure"/> 和 <paramref name="Unknown"/> **必须分开**：
/// 前者是「模型给了答案但没把握」（放宽阈值就能救），后者是「模型说不出」（得改提示词）。
/// 两者在界面上都显示成「未识别」，混成一个数就没法判断问题出在哪。
/// </summary>
public sealed record AiPurposeBatchOutcome(
    int Asked,
    int Applied,
    int Unsure,
    int Unknown,
    int Calls,
    int InputTokens,
    double Cost);

/// <summary>
/// 把「认不出用途」的文件夹一次性交给结构化判定通道（TypeSafe Jev 这类）归类。
///
/// <b>服务对象是「按文件夹删除」页的 <see cref="OrganizeNode"/>，不是清理中心的候选。</b>
/// 清理中心的候选项由 8 条规则产出，而**每条规则都声明了具体用途**，不会留下「认不出来」的堆积；
/// 真正堆积未知的是文件夹树（页头那行「本地认出 / AI 有结论 / 未知 / 失败」就是它在数）。
///
/// 三条实测得来的硬约束，别随手改：
///
/// <list type="number">
/// <item><b>路径必须写进问题里，不能让它「数第几行」。</b>
/// 48 项、每条路径重复三次的自检里，行号引用只有 8/16 一致 ——
/// 而且失败模式是系统性的：前 16 行答得又准又对，后面整片塌成同一个答案。
/// 把路径直接写进 instructions 之后，48 项 15/16、176 项 15/16 一致，token 只多 5%。</item>
///
/// <item><b>一批 80 条。</b>条数上限跟着**每项 token 数**走，而那个数由选项表规模决定
/// （10 类 304 / 放宽 keep 后 362 / 拆开「别删」成 13 类后 488）。
/// 取 80 让单批输入稳定在 64K 的六成左右，见 <see cref="MaxPerRequest"/> 的对照表。</item>
///
/// <item><b>出站路径一律走 <see cref="PathRedactor.Outbound"/>。</b>
/// 默认脱敏（<c>&lt;UserProfile&gt;</c> / <c>&lt;User&gt;</c> / <c>&lt;PC&gt;</c>），
/// 只有用户在设置里明确允许才发完整路径。实测脱敏不影响归类（16/16 与非脱敏一致）。</item>
/// </list>
///
/// <b>安全：写入只落在 <see cref="OrganizeNode.BatchPurpose"/> 这一个展示字段上。</b>
/// 整理树的对象根本没有 Risk / CanDelete / Selected 这些清理能力，
/// 所以「AI 不会替用户打勾」在这里是**结构上成立**的，不靠提示词约束。
/// </summary>
public static class AiPurposeBatchService
{
    /// <summary>
    /// 一次请求最多问多少条。
    ///
    /// 这个数字跟着**每项 token 数**走，而每项 token 数由选项表规模决定：
    ///
    /// <code>
    /// 选项表                每项 token   单批   单批输入   占 64K
    /// 10 类（原来）            304       160    48,640      76%
    /// 10 类 + 放宽 keep        362       160    57,920      90%  ← 真机撞 max tokens
    /// 10 类 + 放宽 keep        362       100    36,200      57%
    /// 13 类（拆开「别删」）     488        80    39,040      61%  ← 现在
    /// </code>
    ///
    /// 稳定在六成左右：路径长短有个体差异，留够余量才不会被某一批特别长的顶爆。
    /// 花费不变（同样的条数、同样的单价），只是多几次小请求；
    /// 而且上面那层「太大就拆两半」还会兜底。
    /// </summary>
    public const int MaxPerRequest = 80;

    /// <summary>
    /// 自适应步长的**起点**。故意小：别人可能填 32k 上下文的端点，
    /// 一上来发 80 条会整批 400；从 20 条起步只多花几次往返。
    /// 成功一次就翻倍，很快长到 <see cref="MaxPerRequest"/>。
    /// </summary>
    public const int AdaptiveStart = 20;

    /// <summary>步长的下限。撞上限就一直减半，但不许减到 0（那会死循环）。</summary>
    public const int AdaptiveMin = 5;

    /// <param name="stillCurrent">
    /// 每写回一批之前问一次：扫描有没有换代、这一页还在不在。返回 false 就停止写入
    /// —— 旧结果绝不许贴到新扫描上。为 null 表示调用方自己保证。
    /// </param>
    public static async Task<AiPurposeBatchOutcome> RunAsync(
        IReadOnlyList<OrganizeNode> nodes,
        Func<bool>? stillCurrent,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        // 入选判据在节点自己身上（OrganizeNode.NeedsPurposeClassification），离线可测
        var targets = nodes.Where(n => n.NeedsPurposeClassification).ToList();
        if (targets.Count == 0)
            return new AiPurposeBatchOutcome(0, 0, 0, 0, 0, 0, 0);

        var provider = App.Settings.DecisionProvider();
        string model = App.Settings.DecisionModelId();
        if (provider == null || string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiPurposeBatchNotConfigured);

        int applied = 0, unsure = 0, unknown = 0, calls = 0, inTokens = 0;
        double cost = 0;
        int done = 0;
        // 类别分布 + 置信度分布：优化提示词和阈值时**看数据，不靠猜**
        var dist = new Dictionary<string, int>(StringComparer.Ordinal);
        var conf = new List<double>();

        // 一批问不完就拆两半再问。
        //
        // 存在的理由是真机上撞过的：**criteria 是「每题一份」**（API 强制，放进 state 会被
        // 校验拒掉），所以一批的输入 = 条数 × (路径 + 类别文案)。实测每项 487 token
        // （13 类），80 项一批就是 38,960 —— 64K 还剩 40% 余量，但**别人可能填一个 32k 的
        // 端点**，那就直接爆。拆开比整批失败强得多，而且总花费一样。
        //
        // 返回：这一批是不是**第一次就成功**了 —— 外层据此调步长（见下面的自适应循环）。
        async Task<bool> AskAsync(List<OrganizeNode> chunk)
        {
            if (chunk.Count == 0) return true;
            ct.ThrowIfCancellationRequested();
            if (stillCurrent != null && !stillCurrent()) return true;

            AiDecisionReply reply;
            try
            {
                reply = await AiGateway.DecideAsync(BuildRequest(provider, model, chunk), ct);
            }
            catch (Exception ex) when (chunk.Count > 1 && IsTooLargeForModel(ex))
            {
                AppLog.Warn("AiClassify",
                    $"batch too large ({chunk.Count} items), splitting in half | {ex.Message}");
                int half = chunk.Count / 2;
                await AskAsync(chunk.GetRange(0, half));
                await AskAsync(chunk.GetRange(half, chunk.Count - half));
                return false;
            }
            calls++;
            inTokens += reply.InputTokens;
            cost += reply.Cost;

            // 结果回来之后再确认一次：等待期间可能刚换了扫描。
            // 返回值只用来调步长；真正"该不该继续"由外层循环再判一次，所以这里给 true 不影响正确性。
            if (stillCurrent != null && !stillCurrent()) return true;

            for (int i = 0; i < chunk.Count; i++)
            {
                // **先记「问过了」**，不管下面拿没拿到结论。
                // 不记的话，没结论的条目会被反复追问 —— 它看起来仍然「没结论」。
                chunk[i].MarkBatchAsked();
                if (!reply.Answers.TryGetValue(KeyOf(i), out var answer)) { unknown++; continue; }
                var kind = AiPurposeCriteria.Parse(answer.Choice);
                dist[kind.ToString()] = dist.TryGetValue(kind.ToString(), out var c) ? c + 1 : 1;
                conf.Add(answer.Confidence);
                // 模型说不出：什么都不写（只是「未识别」）。
                // 模型说了但没把握：**照样写进去**，只是标成「拿不准」——
                // 官方对这种情况的说法是 escalate to a human when confidence is low，
                // 丢掉只剩「未识别」，用户读到的信息量是零。
                if (kind == AiPurposeKind.Unknown) { unknown++; continue; }
                string name = Loc.AiPurposeDisplayName(kind);
                if (name.Length == 0) { unknown++; continue; }
                bool accepted = AiPurposeCriteria.IsAccepted(kind, answer.Confidence);
                if (accepted)
                {
                    chunk[i].SetBatchPurpose(name);
                    applied++;
                }
                else
                {
                    chunk[i].SetBatchPurpose(Loc.AiPurposeUnsureName(name), unsure: true);
                    unsure++;
                }
            }

            done += chunk.Count;
            progress?.Report(Loc.AiPurposeBatchProgress(done, targets.Count));
            return true;
        }

        // **自适应步长**：从 AdaptiveStart 起步，成功一次翻倍、撞上限减半。
        //
        // 为什么不直接固定 80：别人填的端点可能是 32k 上下文（甚至是自建的小服务），
        // 一上来发 80 条就整批 400。从 20 条起步只多花几次往返，代价远小于"第一批就失败"；
        // 而一旦确认放得下，它会自己长到 MaxPerRequest，不会一直小步爬。
        int size = AdaptiveStart;
        for (int start = 0; start < targets.Count; )
        {
            ct.ThrowIfCancellationRequested();
            if (stillCurrent != null && !stillCurrent()) break;
            progress?.Report(Loc.AiPurposeBatchProgress(done, targets.Count));

            var chunk = targets.GetRange(start, Math.Min(size, targets.Count - start));
            bool firstTry = await AskAsync(chunk);
            // 无论拆没拆，这一批都已经处理完了（拆出来的两半也处理完了），所以步长照常往前推
            start += chunk.Count;
            size = firstTry
                ? Math.Min(MaxPerRequest, size * 2)
                : Math.Max(AdaptiveMin, size / 2);
        }

        if (conf.Count > 0) conf.Sort();
        AppLog.Info("AiClassify",
            $"asked={targets.Count} applied={applied} unsure={unsure} unknown={unknown} calls={calls} "
            + $"in={inTokens} cost=${cost:0.000000} "
            + $"medianConf={(conf.Count > 0 ? conf[conf.Count / 2] : 0):0.00} "
            + $"dist={string.Join(",", dist.OrderByDescending(x => x.Value).Select(x => x.Key + ":" + x.Value))}");

        return new AiPurposeBatchOutcome(targets.Count, applied, unsure, unknown, calls, inTokens, cost);
    }

    static string KeyOf(int index) => "p" + (index + 1);

    /// <summary>
    /// 这批是不是把模型的上下文撑爆了。判据放宽一点：各家端点的措辞不一样
    /// （<c>max tokens exceeded</c> / <c>context length</c> / <c>too large</c>），
    /// 漏判的代价是整批失败，误判的代价只是多拆一次。
    /// </summary>
    static bool IsTooLargeForModel(Exception ex)
    {
        string s = ex.ToString();
        return s.Contains("max tokens", StringComparison.OrdinalIgnoreCase)
               || s.Contains("context length", StringComparison.OrdinalIgnoreCase)
               || s.Contains("too many tokens", StringComparison.OrdinalIgnoreCase)
               || s.Contains("maximum context", StringComparison.OrdinalIgnoreCase);
    }

    static AiDecisionRequest BuildRequest(AiProviderCfg provider, string model, List<OrganizeNode> chunk)
    {
        var criteria = new Dictionary<string, string>();
        foreach (var (key, text) in Loc.AiPurposeOptions) criteria[key] = text;

        var questions = new Dictionary<string, AiDecisionQuestion>();
        for (int i = 0; i < chunk.Count; i++)
        {
            // 路径写进问题本身 —— 不让模型去数行号（见类型注释第 1 条）
            questions[KeyOf(i)] = new AiDecisionQuestion
            {
                Type = "choice",
                Instructions = Loc.AiPurposeQuestion(PathRedactor.Outbound(chunk[i].FullPath)),
                Criteria = criteria,
            };
        }

        return new AiDecisionRequest
        {
            Provider = provider,
            Model = model,
            State = string.Join(Environment.NewLine, chunk.Select(x => PathRedactor.Outbound(x.FullPath))),
            Questions = questions,
        };
    }

    static IEnumerable<List<OrganizeNode>> Chunk(List<OrganizeNode> all, int size)
    {
        for (int i = 0; i < all.Count; i += size)
            yield return all.GetRange(i, Math.Min(size, all.Count - i));
    }
}
