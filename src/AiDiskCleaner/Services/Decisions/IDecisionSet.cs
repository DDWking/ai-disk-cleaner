namespace AiDiskCleaner.Services;

/// <summary>一条判定的归属：采纳 / 有答案但没把握 / 模型说不出 / 这次跳过。</summary>
public enum DecisionWriteBack
{
    /// <summary>够格被采纳（过了阈值），已写进展示字段。</summary>
    Applied,
    /// <summary>模型给了答案但没把握。**照样写**，只是标出来等人看（官方口径：escalate to a human）。</summary>
    Unsure,
    /// <summary>模型说不出（没给答案 / 给了 Unknown）。什么都不写。</summary>
    Unknown,
    /// <summary>这一条这次不该问（例如组太大），连问题都没发。</summary>
    Skipped,
}

/// <summary>
/// 一条判定的回写结果。<see cref="Label"/> 和 <see cref="Confidence"/> 只用于遥测
/// （类别分布、置信度中位数）—— 优化提示词和阈值时**看数据，不靠猜**。
/// </summary>
public readonly record struct DecisionApplied(DecisionWriteBack Kind, string Label, double Confidence)
{
    public static DecisionApplied Skipped => new(DecisionWriteBack.Skipped, "", 0);
    public static DecisionApplied NoAnswer => new(DecisionWriteBack.Unknown, "", 0);
}

/// <summary>
/// 一个「判定域」：把某一类对象翻译成问题，并把答案写回展示字段。
///
/// 一个域只做三件事：<b>出题</b>（<see cref="QuestionFor"/>）、<b>拼 state</b>
/// （<see cref="StateFor"/>）、<b>回写</b>（<see cref="Apply"/>）。
/// 分批、重试、折半、取消、代次守卫、计数与遥测全在 <see cref="DecisionRunner"/> 里，
/// 所以「一批撑爆上下文要拆」「换代即停写」这些规则**只需要写一遍**。
///
/// <b>回写只能碰展示字段。</b>AI 永远不写 Risk / CanDelete / Selected / Evidence ——
/// 这不是靠注释约束的，是靠调用方（界面模型）根本没有这些槽位。
/// </summary>
public interface IDecisionSet<TItem>
{
    /// <summary>域名字，进日志（<c>domain=...</c>）。</summary>
    string Name { get; }

    /// <summary>一次请求最多问多少条 —— 跟着「每项 token 数」走，由选项表规模决定。</summary>
    int MaxPerRequest { get; }

    /// <summary>自适应步长的起点。故意小：别人可能填 32k 上下文的端点。</summary>
    int AdaptiveStart { get; }

    /// <summary>
    /// 最多同时发几个请求。默认 **1**（串行）—— 保持既有行为不变。
    ///
    /// 只有「每批各自独立、state 与选项表互不共享」的域才该调大它。
    /// 执行器在 &gt;1 时用 <see cref="System.Threading.SemaphoreSlim"/> 限流并发，
    /// 并发下计数/分布/进度全部加锁；=1 时走原来的串行路径，行为逐字节不变。
    /// </summary>
    int MaxConcurrency => 1;

    /// <summary>进度文案（域自己写，因为「在认什么」只有它知道）。</summary>
    string ProgressText(int done, int total);

    /// <summary>
    /// 记下「这条问过了」，<b>不管下面拿没拿到结论</b>。
    /// 不记的话，没结论的条目看起来仍然「没结论」，会被反复追问 —— 那是无限请求。
    /// </summary>
    void MarkAsked(TItem item);

    /// <summary>这一批的问题名，顺序即下标（答案按名字回填，不靠位置）。</summary>
    IReadOnlyList<string> KeysFor(IReadOnlyList<TItem> chunk);

    /// <summary>第 <paramref name="index"/> 条的问题。</summary>
    AiDecisionQuestion QuestionFor(int index, TItem item);

    /// <summary>这一批的 state。**出站路径一律走脱敏**。</summary>
    string StateFor(IReadOnlyList<TItem> chunk);

    /// <summary>把一条答案写回去。<paramref name="answer"/> 为 null 表示模型没给这一条。</summary>
    DecisionApplied Apply(TItem item, AiDecisionAnswer? answer);
}

/// <summary>一次批量判定的结果，用来给用户一句实话（而不是「已完成」）。</summary>
public sealed record DecisionOutcome(
    int Asked,
    int Applied,
    int Unsure,
    int Unknown,
    int Skipped,
    int Calls,
    int InputTokens,
    double Cost);
