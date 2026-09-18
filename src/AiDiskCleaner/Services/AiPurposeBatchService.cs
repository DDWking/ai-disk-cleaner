using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>一次批量归类的结果，用来给用户一句实话（而不是「已完成」）。</summary>
public sealed record AiPurposeBatchOutcome(
    int Asked,
    int Applied,
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
/// <item><b>一批 160 条。</b>实测 176 条时输入 53542 token（占 64K 的 82%）、
/// 304 token/项、3.6 秒、约 $0.0022。取 160 留 15% 余量。</item>
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
    /// <summary>一次请求最多问多少条（实测依据见类型注释）。</summary>
    public const int MaxPerRequest = 160;

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
            return new AiPurposeBatchOutcome(0, 0, 0, 0, 0, 0);

        var provider = App.Settings.DecisionProvider();
        string model = App.Settings.DecisionModelId();
        if (provider == null || string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiPurposeBatchNotConfigured);

        int applied = 0, unknown = 0, calls = 0, inTokens = 0;
        double cost = 0;
        int done = 0;

        foreach (var chunk in Chunk(targets, MaxPerRequest))
        {
            ct.ThrowIfCancellationRequested();
            if (stillCurrent != null && !stillCurrent()) break;
            progress?.Report(Loc.AiPurposeBatchProgress(done, targets.Count));

            var reply = await AiGateway.DecideAsync(BuildRequest(provider, model, chunk), ct);
            calls++;
            inTokens += reply.InputTokens;
            cost += reply.Cost;

            // 结果回来之后再确认一次：等待期间可能刚换了扫描
            if (stillCurrent != null && !stillCurrent()) break;

            for (int i = 0; i < chunk.Count; i++)
            {
                if (!reply.Answers.TryGetValue(KeyOf(i), out var answer)) { unknown++; continue; }
                var kind = AiPurposeCriteria.Parse(answer.Choice);
                if (!AiPurposeCriteria.IsAccepted(kind, answer.Confidence)) { unknown++; continue; }
                string name = Loc.AiPurposeDisplayName(kind);
                if (name.Length == 0) { unknown++; continue; }
                chunk[i].SetBatchPurpose(name);
                applied++;
            }

            done += chunk.Count;
            progress?.Report(Loc.AiPurposeBatchProgress(done, targets.Count));
        }

        return new AiPurposeBatchOutcome(targets.Count, applied, unknown, calls, inTokens, cost);
    }

    static string KeyOf(int index) => "p" + (index + 1);

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
