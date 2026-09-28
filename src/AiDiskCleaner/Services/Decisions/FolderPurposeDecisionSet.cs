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
/// 取 80 让单批输入稳定在 64K 的六成左右。</item>
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
    /// 而且 <see cref="DecisionRunner"/> 里那层「太大就拆两半」还会兜底。
    /// </summary>
    public const int MaxPerRequest = 80;

    /// <summary>
    /// 自适应步长的**起点**。故意小：别人可能填 32k 上下文的端点，
    /// 一上来发 80 条会整批 400；从 20 条起步只多花几次往返。
    /// 成功一次就翻倍，很快长到 <see cref="MaxPerRequest"/>。
    /// </summary>
    public const int AdaptiveStart = 20;

    string IDecisionSet<OrganizeNode>.Name => "folder-purpose";
    int IDecisionSet<OrganizeNode>.MaxPerRequest => MaxPerRequest;
    int IDecisionSet<OrganizeNode>.AdaptiveStart => AdaptiveStart;

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
            // 路径写进问题本身 —— 不让模型去数行号（见类型注释第 1 条）
            Instructions = Loc.AiPurposeQuestion(PathRedactor.Outbound(item.FullPath)),
            Criteria = Criteria(),
        };

    string IDecisionSet<OrganizeNode>.StateFor(IReadOnlyList<OrganizeNode> chunk)
        => string.Join(Environment.NewLine, chunk.Select(x => PathRedactor.Outbound(x.FullPath)));

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

    /// <summary>选项表。每题一份（API 强制：criteria 挂在问题上，放进 state 会被校验拒掉）。</summary>
    static Dictionary<string, string> Criteria()
    {
        var options = Loc.AiPurposeOptions;
        var criteria = new Dictionary<string, string>(options.Length, StringComparer.Ordinal);
        foreach (var (key, text) in options) criteria[key] = text;
        return criteria;
    }
}
