using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>
/// 「这个文件夹是什么用途」这个判定域。
///
/// <b>服务对象是「按文件夹删除」页的 <see cref="OrganizeNode"/>，不是清理中心的候选。</b>
/// 清理中心的候选项由 8 条规则产出，而**每条规则都声明了具体用途**，不会留下
/// 「认不出来」的堆积；真正堆积未知的是文件夹树。
///
/// 四条实测得来的硬约束，别随手改：
///
/// <list type="number">
/// <item><b>一个请求只问一个目录。</b>以前是「一批 80 条路径 + 每题问一条」，
/// 结果是准确率塌方：模型看不到任何目录内容（state 只有一串路径），
/// 而且 80 个互不相关的目录挤在同一份 state 里会互相串味 ——
/// 问的是某一个路径，模型却要在一坨无关路径里定位它。
/// 现在一次请求 = 一个目录，state 是它自己的结构化摘要
/// （<see cref="FolderPurposeRules.BuildDecisionState"/>：路径、大小、文件数、
/// 类型分布、最大文件、子目录名、已知产品、最后修改）。</item>
///
/// <item><b>模型面文案一律英文常量，不跟界面语言走。</b>
/// 题面在 <see cref="AiPurposeCriteria.QuestionInstructions"/>，选项描述在
/// <see cref="AiPurposeCriteria.Descriptions"/>。旧实现从 <c>Loc</c> 取题面与选项，
/// 界面是中文时就发中文 —— 而 Jev 在英文上最准。选项**只描述「这是什么数据」**，
/// 不再夹「别删」「只有在…才选这个」这类建议与元指令（那会把分类题答成建议题）。</item>
///
/// <item><b>自适应步长起点也是 1。</b>既然一批只有一条，起点自然等于上限；
/// 步长那套逻辑照常跑（只是不再翻倍），撑爆时也不会再拆（单条无从拆起）。</item>
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
public sealed class FolderPurposeDecisionSet : IDecisionSet<OrganizeNode>
{
    /// <summary>
    /// 一次请求最多问几条。**1**：一个目录一次请求。
    ///
    /// 这不是省 token 的取舍，是准确率的取舍。旧值 80 的前提是「每项 token 很小、
    /// 靠条数摊薄 state」—— 那个前提本身就错了：模型看不到目录内容，
    /// 而 80 条无关路径共享一份 state 会让定位失效。现在每条 state 是一份
    /// 约几百字符的结构化摘要，单条就远低于 <see cref="JevLimits.MaxRequestChars"/>。
    /// </summary>
    public const int MaxPerRequest = 1;

    /// <summary>
    /// 自适应步长的**起点**。跟上限一样是 1：一批一条，不存在「先小批试再放大」。
    /// 步长逻辑本身保留（见 <see cref="DecisionRunner"/>），只是不再翻倍。
    /// </summary>
    public const int AdaptiveStart = 1;

    string IDecisionSet<OrganizeNode>.Name => "folder-purpose";
    int IDecisionSet<OrganizeNode>.MaxPerRequest => MaxPerRequest;
    int IDecisionSet<OrganizeNode>.AdaptiveStart => AdaptiveStart;

    /// <summary>
    /// 并发度：每条 state 是一份独立摘要、每题一份选项表，请求之间没有共享状态，
    /// 所以可以并行发。Jev 端点是 ~1200 req/min、延迟通常 &lt;0.5s，
    /// 串行问几百个目录会把界面卡很久。
    /// </summary>
    int IDecisionSet<OrganizeNode>.MaxConcurrency => 8;

    string IDecisionSet<OrganizeNode>.ProgressText(int done, int total)
        => Loc.AiPurposeBatchProgress(done, total);

    void IDecisionSet<OrganizeNode>.MarkAsked(OrganizeNode item) => item.MarkBatchAsked();

    IReadOnlyList<string> IDecisionSet<OrganizeNode>.KeysFor(IReadOnlyList<OrganizeNode> chunk)
    {
        var keys = new string[chunk.Count];
        for (int i = 0; i < chunk.Count; i++) keys[i] = KeyOf(i);
        return keys;
    }

    AiDecisionQuestion IDecisionSet<OrganizeNode>.QuestionFor(int index, OrganizeNode item)
        => new()
        {
            Type = "choice",
            // 模型面题面是**英文常量**：不跟界面语言走（Jev 在英文上最准）。
            // 路径不写进题面 —— 一个请求只问一个目录，路径在 state 的 path: 行里。
            Instructions = AiPurposeCriteria.QuestionInstructions,
            Criteria = Criteria(),
        };

    string IDecisionSet<OrganizeNode>.StateFor(IReadOnlyList<OrganizeNode> chunk)
    {
        // 一次请求只问一个目录（MaxPerRequest = 1），但这里仍按 chunk 遍历：
        // 万一调用方绕过批大小直接塞多条，也不会只发第一条而让答案错位。
        var parts = new string[chunk.Count];
        for (int i = 0; i < chunk.Count; i++)
        {
            var node = chunk[i];
            var sum = FolderPurposeRules.Summarize(node.Dir, node.Id, node.Depth, node.RelativePath);
            parts[i] = FolderPurposeRules.BuildDecisionState(sum, PathRedactor.Outbound(node.FullPath));
        }
        return string.Join(Environment.NewLine, parts);
    }

    DecisionApplied IDecisionSet<OrganizeNode>.Apply(OrganizeNode item, AiDecisionAnswer? answer)
    {
        if (answer == null) return DecisionApplied.NoAnswer;

        var kind = AiPurposeCriteria.Parse(answer.Choice);
        string label = kind.ToString();
        double confidence = answer.Confidence;

        // 模型说不出：什么都不写（只是「未识别」）
        if (kind == AiPurposeKind.Unknown) return new DecisionApplied(DecisionWriteBack.Unknown, label, confidence);

        string name = Loc.AiPurposeDisplayName(kind);
        if (name.Length == 0) return new DecisionApplied(DecisionWriteBack.Unknown, label, confidence);

        if (AiPurposeCriteria.IsAccepted(kind, confidence))
        {
            item.SetBatchPurpose(name);
            return new DecisionApplied(DecisionWriteBack.Applied, label, confidence);
        }

        // 说了但没把握：**照样写进去**，只是标成「拿不准」——
        // 官方对这种情况的说法是 escalate to a human when confidence is low，
        // 丢掉只剩「未识别」，用户读到的信息量是零。
        item.SetBatchPurpose(Loc.AiPurposeUnsureName(name), unsure: true);
        return new DecisionApplied(DecisionWriteBack.Unsure, label, confidence);
    }

    static string KeyOf(int index) => "p" + (index + 1);

    /// <summary>
    /// 选项表。每题一份（API 强制：criteria 挂在问题上，放进 state 会被校验拒掉）。
    /// 文案是**英文常量**（<see cref="AiPurposeCriteria.Descriptions"/>），不跟界面语言走。
    /// </summary>
    static Dictionary<string, string> Criteria()
    {
        var criteria = new Dictionary<string, string>(AiPurposeCriteria.Keys.Length, StringComparer.Ordinal);
        foreach (var key in AiPurposeCriteria.Keys)
            criteria[key] = AiPurposeCriteria.Descriptions.TryGetValue(key, out var text) ? text : "";
        return criteria;
    }
}
