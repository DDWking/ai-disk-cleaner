namespace AiDiskCleaner.Services;

/// <summary>
/// 请求超出了判定通道的硬限制。
///
/// <b>在发请求之前抛。</b>这些限制是端点的契约（选项数、档位数）或上下文预算，
/// 违反时端点只会回一个 400，而 400 到了 UI 就只剩「失败了」三个字 ——
/// 那让「选项表写错了」和「网断了」看起来一模一样。提前抛能直接指出是哪道题、
/// 超了多少。
/// </summary>
public sealed class DecisionLimitException : Exception
{
    public DecisionLimitException(string message) : base(message) { }
}

/// <summary>
/// Jev 的硬限制与请求预算。
///
/// 三种原语的结构限制是**端点契约**（<see cref="MaxChoiceOptions"/>、
/// <see cref="MinScoreBands"/>/<see cref="MaxScoreBands"/>），超了直接失败；
/// 字符预算是**保守估算**（见 <see cref="CharsPerToken"/>），
/// 目的是让「一批太大」在本地就被拆开，而不是撞上端点再折半重试。
/// </summary>
public static class JevLimits
{
    /// <summary><c>choice</c> 最多几个选项。超了要两段式：先选窗口，再在窗口内排序。</summary>
    public const int MaxChoiceOptions = 255;

    /// <summary><c>score</c> 的档位下限。</summary>
    public const int MinScoreBands = 2;

    /// <summary><c>score</c> 的档位上限。</summary>
    public const int MaxScoreBands = 10;

    /// <summary>
    /// 估算字符/token 比。官方口径是「state + 所有问题合计 64K tokens」，
    /// 而路径与中文选项的实测大致落在 2~3 字符/token；取 3 是**保守**的一侧
    /// （宁可提前拆批，也不要撞 413）。
    /// </summary>
    public const int CharsPerToken = 3;

    /// <summary>
    /// state + 所有题面文字的字符预算，默认对应 64K token 上限的约六成
    /// （64K × 3 × 0.66 ≈ 128K 字符）—— 和当初「单批输入稳定在六成」的实测口径一致。
    /// 测试可以调小它来验证拆批路径。
    /// </summary>
    public static int MaxRequestChars { get; set; } = 128_000;

    /// <summary>把 <paramref name="request"/> 量一遍：state + 每题的文字。</summary>
    public static int MeasureChars(AiDecisionRequest request)
    {
        int total = request.State?.Length ?? 0;
        foreach (var question in request.Questions.Values)
        {
            total += question.Instructions?.Length ?? 0;
            if (question.Criteria != null)
            {
                foreach (var (key, text) in question.Criteria)
                    total += key.Length + (text?.Length ?? 0);
            }
            if (question.Scale != null)
            {
                foreach (var band in question.Scale) total += band?.Length ?? 0;
            }
        }
        return total;
    }

    /// <summary>
    /// 校验一次请求。违反就抛 <see cref="DecisionLimitException"/>，**绝不发出去**。
    /// 消息里带题目名与实际值，否则「哪道题写错了」要靠猜。
    /// </summary>
    public static void Validate(AiDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Questions.Count == 0)
            throw new DecisionLimitException("判定请求没有任何问题");

        foreach (var (name, question) in request.Questions)
        {
            switch (question.Type)
            {
                case "choice":
                    if (question.Criteria == null || question.Criteria.Count == 0)
                        throw new DecisionLimitException($"题目 {name}：choice 必须给 criteria");
                    if (question.Criteria.Count > MaxChoiceOptions)
                        throw new DecisionLimitException(
                            $"题目 {name}：choice 有 {question.Criteria.Count} 个选项，上限 {MaxChoiceOptions}");
                    break;

                case "score":
                    if (question.Scale == null
                        || question.Scale.Count < MinScoreBands
                        || question.Scale.Count > MaxScoreBands)
                    {
                        throw new DecisionLimitException(
                            $"题目 {name}：score 档位必须 {MinScoreBands}~{MaxScoreBands}，"
                            + $"实际 {(question.Scale?.Count ?? 0)}");
                    }
                    break;

                case "noul":
                    break;

                default:
                    throw new DecisionLimitException(
                        $"题目 {name}：未知类型 '{question.Type}'（只有 choice / noul / score）");
            }

            if (string.IsNullOrWhiteSpace(question.Instructions))
                throw new DecisionLimitException($"题目 {name}：没有 instructions");
        }

        int chars = MeasureChars(request);
        if (chars > MaxRequestChars)
        {
            throw new DecisionLimitException(
                $"判定请求约 {chars} 字符，超过预算 {MaxRequestChars}"
                + $"（≈{MaxRequestChars / CharsPerToken} tokens，上限 64K）—— 该拆批了");
        }
    }
}
