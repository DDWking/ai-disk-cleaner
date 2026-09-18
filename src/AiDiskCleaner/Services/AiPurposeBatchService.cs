using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>一次批量归类的结果，用来给用户一句实话（而不是「已完成」）。</summary>
public sealed record AiPurposeBatchOutcome(
    int Asked,
    int Applied,
    int Hinted,
    int Unknown,
    int Calls,
    int InputTokens,
    double Cost);

/// <summary>
/// 把「本地规则认不出用途」的那批一次性交给结构化判定通道（TypeSafe Jev 这类）归类。
///
/// 三条实测得来的硬约束，别随手改：
///
/// <list type="number">
/// <item><b>路径必须写进问题里，不能让它「数第几行」。</b>
/// 48 项、每条路径重复三次的自检里，行号引用只有 8/16 一致 ——
/// 而且失败模式是系统性的：前 16 行答得又准又对，后面整片塌成同一个答案。
/// 把路径直接写进 instructions 之后，48 项 15/16、176 项 15/16 一致，
/// token 只多 5%。报告里那句「别让它算数」就是这个意思。</item>
///
/// <item><b>一批 160 条。</b>实测 176 条时输入 53542 token（占 64K 的 82%）、
/// 304 token/项、3.6 秒、约 $0.0022。取 160 留 15% 余量。</item>
///
/// <item><b>不许让条目从「不可批选」变成「可批选」。</b>
/// 见 <see cref="Apply"/> 里的守卫。</item>
/// </list>
/// </summary>
public static class AiPurposeBatchService
{
    /// <summary>一次请求最多问多少条（实测依据见类型注释）。</summary>
    public const int MaxPerRequest = 160;

    /// <summary>哪些条目值得问：规则没给出用途、而且有路径。</summary>
    public static bool IsEligible(CleanItem item)
        => item.Purpose == CleanPurpose.Other && !string.IsNullOrWhiteSpace(item.FullPath);

    public static async Task<AiPurposeBatchOutcome> RunAsync(
        IReadOnlyList<CleanItem> items,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var targets = items.Where(IsEligible).ToList();
        if (targets.Count == 0)
            return new AiPurposeBatchOutcome(0, 0, 0, 0, 0, 0, 0);

        var provider = App.Settings.DecisionProvider();
        string model = App.Settings.DecisionModelId();
        if (provider == null || string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiPurposeBatchNotConfigured);

        int applied = 0, hinted = 0, unknown = 0, calls = 0, inTokens = 0;
        double cost = 0;
        int done = 0;

        foreach (var chunk in Chunk(targets, MaxPerRequest))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(Loc.AiPurposeBatchProgress(done, targets.Count));

            var request = BuildRequest(provider, model, chunk);
            var reply = await AiGateway.DecideAsync(request, ct);
            calls++;
            inTokens += reply.InputTokens;
            cost += reply.Cost;

            for (int i = 0; i < chunk.Count; i++)
            {
                if (!reply.Answers.TryGetValue(KeyOf(i), out var answer)) { unknown++; continue; }
                var kind = AiPurposeCriteria.Parse(answer.Choice);
                if (!AiPurposeCriteria.IsAccepted(kind, answer.Confidence)) { unknown++; continue; }

                switch (Apply(chunk[i], kind))
                {
                    case Applied.Applied: applied++; break;
                    case Applied.Hinted: hinted++; break;
                    default: unknown++; break;
                }
            }

            done += chunk.Count;
            progress?.Report(Loc.AiPurposeBatchProgress(done, targets.Count));
        }

        return new AiPurposeBatchOutcome(targets.Count, applied, hinted, unknown, calls, inTokens, cost);
    }

    enum Applied { Rejected, Applied, Hinted }

    /// <summary>
    /// 把一条判定落到候选上。
    ///
    /// <b>安全守卫</b>：批量勾选的判据是 <c>Evidence &gt;= Signature &amp;&amp; 用途属于缓存类</c>。
    /// 如果这条候选本来就有签名级证据、只是用途没认出来，那么写回用途会**凭空造出批选资格** ——
    /// 等于 AI 绕道获得了「界面替用户打勾」的能力。所以写回前后各算一次，
    /// 一旦发现它从「不可批选」变成「可批选」，立刻撤销并降级成纯提示。
    /// </summary>
    static Applied Apply(CleanItem item, AiPurposeKind kind)
    {
        // keep / model 落不到 CleanPurpose 上：只给提示，绝不改用途、更不阻挡删除。
        if (!AiPurposeCriteria.TryToPurpose(kind, out var purpose))
        {
            item.AiNote = kind switch
            {
                AiPurposeKind.Keep => Loc.AiPurposeFromAi(Loc.AiPurposeKeepHint),
                AiPurposeKind.Model => Loc.AiPurposeFromAi(Loc.AiPurposeModelHint),
                _ => item.AiNote,
            };
            return Applied.Hinted;
        }

        if (CleanRuleEligibility.GainsBatchEligibility(item, purpose))
        {
            // 撤销：不许 AI 造出批选资格。用途不动，只留一句提示。
            item.AiNote = Loc.AiPurposeNote(CleanPurposes.Name(purpose), CleanPurposes.Impact(purpose));
            return Applied.Hinted;
        }

        item.Purpose = purpose;
        item.AiNote = Loc.AiPurposeNote(CleanPurposes.Name(purpose), CleanPurposes.Impact(purpose));
        return Applied.Applied;
    }

    static string KeyOf(int index) => "p" + (index + 1);

    static AiDecisionRequest BuildRequest(AiProviderCfg provider, string model, List<CleanItem> chunk)
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
                Instructions = Loc.AiPurposeQuestion(chunk[i].FullPath),
                Criteria = criteria,
            };
        }

        return new AiDecisionRequest
        {
            Provider = provider,
            Model = model,
            State = string.Join(Environment.NewLine, chunk.Select(x => x.FullPath)),
            Questions = questions,
        };
    }

    static IEnumerable<List<CleanItem>> Chunk(List<CleanItem> all, int size)
    {
        for (int i = 0; i < all.Count; i += size)
            yield return all.GetRange(i, Math.Min(size, all.Count - i));
    }
}
