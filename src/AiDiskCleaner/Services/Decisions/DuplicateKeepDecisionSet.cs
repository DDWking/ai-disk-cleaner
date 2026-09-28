using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>
/// 「这一组重复文件该留哪一份」这个判定域。
///
/// <b>为什么这是本地规则做不到的事。</b>重复检测只能证明「这些文件内容一样」，
/// 证明不了「哪一份对用户更有用」。现有的基线是**路径最短者为保留项**
/// （<see cref="DuplicateDetector.FullGroupOrder"/>）—— 对
/// `C:\Users\me\Documents\报告.docx` 和 `D:\备份\2024\报告.docx` 这种，
/// 谁也说不出哪个更该留，但人一眼就知道。这就是该问模型的地方。
///
/// <b>与「文件夹用途」域的三处不同</b>：
/// <list type="number">
/// <item>选项表**每题都不一样**（组内成员），所以键是 <c>m1..mN</c> 而不是固定的类别表。</item>
/// <item>回写落在**这一组的保留项那一行的说明文字**上，不是分类字段。</item>
/// <item>把握不够就**什么都不建议** —— 见下面第 3 条。</item>
/// </list>
///
/// 四条硬约束（都有离线断言守着）：
///
/// <list type="number">
/// <item><b>只写展示。</b>建议只落在保留项的 <see cref="CleanItem.Reason"/> 上。
/// <c>Selected</c> / <c>CanDelete</c> / <c>Risk</c> / <c>Evidence</c> 一个都不许碰 ——
/// 重复项本来就是「永久人工选择」，这里也不改 keep/extra 的归属，
/// 只是多一句人话。基线永远是本地规则算出来的那个保留项。</item>
///
/// <item><b>成员太多的组不出题。</b>选项数上限 255 是端点的硬限制，
/// 但真正的原因是**人**：十几个长得差不多的路径摆在一起，模型也只能瞎猜。
/// 超过 <see cref="MaxMembers"/> 的组直接 <see cref="DecisionWriteBack.Skipped"/>，
/// 连问题都不发（这也让 255 那个上限不可能被撞到）。</item>
///
/// <item><b>把握不够就不建议。</b>阈值 <see cref="DecisionThresholds.DuplicateKeep"/>。
/// 这里和「文件夹用途」域**刻意不同**：那边低置信也写（标成「拿不准」），
/// 因为「这个文件夹大概是缓存」哪怕不准也无害；而这里一句
/// 「建议留 D 盘那份」会直接影响用户删哪个文件 —— 宁可不说。</item>
///
/// <item><b>脱敏。</b>出站路径一律走 <see cref="PathRedactor.Outbound"/>。</item>
/// </list>
///
/// <b>什么时候问：只有用户点按钮。</b>它不在扫描流程里 —— 扫描 / 切页 / 展开 / 筛选
/// 全程 0 次请求是软件对外承诺，而且那条承诺是用真实出站计数断言的。
/// 跟着扫描顺手问一轮，等于在用户不知情的时候花钱。
/// </summary>
public sealed class DuplicateKeepDecisionSet : IDecisionSet<CleanLocationNode>
{
    /// <summary>
    /// 一次请求最多问几组。
    ///
    /// 这个域的「每项 token」比文件夹用途大得多：一组 2~8 个成员，
    /// 每题带一份成员选项表 + 一份 state。取 24 组 ≈ 单批输入稳定在 32K 以内，
    /// 剩下的交给 <see cref="DecisionRunner"/> 的自适应步长与「撑爆拆半」兜底。
    /// </summary>
    public const int MaxPerRequest = 24;

    /// <summary>
    /// 自适应步长的起点。比文件夹用途域更小：**这里每一组都是一道不同的题**，
    /// 一批 24 组就是一屏完全不同的选择，先小批试一次更划算。
    /// </summary>
    public const int AdaptiveStart = 4;

    /// <summary>
    /// 组内成员超过这个数就不出题（连问都不问）。
    /// 理由不是 token，是**人**：十几个长得差不多的路径摆一起，模型也只能瞎猜，
    /// 而猜错一句「建议留这份」的代价是用户删错文件。
    /// </summary>
    public const int MaxMembers = 12;

    /// <summary>
    /// 一组里至少要有两个成员才谈得上「留哪一个」。
    /// </summary>
    public const int MinMembers = 2;

    string IDecisionSet<CleanLocationNode>.Name => "dup-keep";
    int IDecisionSet<CleanLocationNode>.MaxPerRequest => MaxPerRequest;
    int IDecisionSet<CleanLocationNode>.AdaptiveStart => AdaptiveStart;

    string IDecisionSet<CleanLocationNode>.ProgressText(int done, int total)
        => Loc.DupKeepProgress(done, total);

    /// <summary>
    /// 重复组没有「问过了」这个落盘标记 —— 一次扫描里每组只会在这一轮被问一次，
    /// 而下一轮扫描的组键会随成员变化。所以这里是**空实现**，不是漏了：
    /// 重复问同一组的唯一途径是用户再点一次按钮，那正是他想要的。
    /// </summary>
    void IDecisionSet<CleanLocationNode>.MarkAsked(CleanLocationNode item) { }

    IReadOnlyList<string> IDecisionSet<CleanLocationNode>.KeysFor(IReadOnlyList<CleanLocationNode> chunk)
    {
        var keys = new string[chunk.Count];
        for (int i = 0; i < chunk.Count; i++) keys[i] = "g" + (i + 1);
        return keys;
    }

    AiDecisionQuestion IDecisionSet<CleanLocationNode>.QuestionFor(int index, CleanLocationNode item)
        => new()
        {
            Type = "choice",
            // 把「这是同一份内容的多个副本，选最该留下的那一份」写进问题本身 ——
            // 不让模型去猜这道题在问什么。
            Instructions = Loc.DupKeepQuestion(index + 1, item.FileCount),
            Criteria = Criteria(item),
        };

    string IDecisionSet<CleanLocationNode>.StateFor(IReadOnlyList<CleanLocationNode> chunk)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < chunk.Count; i++)
        {
            var items = chunk[i].Items;
            sb.Append("g").Append(i + 1).Append(" · ")
              .Append(Loc.DupKeepGroupSummary(items.Count, chunk[i].Bytes))
              .AppendLine();
            // 成员按**现有保留顺序**列出：第一项就是本地基线算出来的保留项。
            // 出站一律脱敏（默认 <UserProfile> / <User> / <PC>）。
            int shown = Math.Min(items.Count, MaxMembers);
            for (int m = 0; m < shown; m++)
            {
                sb.Append("  m").Append(m + 1).Append("  ")
                  .Append(PathRedactor.Outbound(items[m].FullPath)).Append("  ")
                  .Append(FileEntry.FormatSize(items[m].Size)).AppendLine();
            }
        }
        return sb.ToString().TrimEnd();
    }

    DecisionApplied IDecisionSet<CleanLocationNode>.Apply(CleanLocationNode item, AiDecisionAnswer? answer)
    {
        if (answer == null) return DecisionApplied.NoAnswer;

        int index = MemberIndex(answer.Choice);
        if (index < 0 || index >= item.Items.Count) return DecisionApplied.NoAnswer;

        var keep = item.Items[index];
        double confidence = answer.Confidence;
        string label = "m" + (index + 1);

        // 阈值以下**什么都不写**（见类型注释第 3 条）：宁可不说，也不给一句
        // 可能让用户删错文件的建议。
        if (confidence < DecisionThresholds.DuplicateKeep)
            return new DecisionApplied(DecisionWriteBack.Unsure, label, confidence);

        var baseline = BaselineKeep(item);
        string note = ReferenceEquals(keep, baseline)
            ? Loc.DupKeepSameAsBaseline(confidence)
            : Loc.DupKeepDifferent(PathRedactor.Outbound(keep.FullPath), confidence);

        // 只碰说明文字：Risk / CanDelete / Selected / Evidence 一个都不动。
        baseline.Reason = baseline.Reason + Loc.DupKeepNoteMarker + note;
        return new DecisionApplied(DecisionWriteBack.Applied, label, confidence);
    }

    /// <summary>
    /// 把答案里的选项名（<c>m3</c>）解析成组内下标。解析不出返回 -1。
    /// 用序号而不是路径：路径里什么字符都可能有，当键不可靠。
    /// </summary>
    internal static int MemberIndex(string? choice)
    {
        if (string.IsNullOrWhiteSpace(choice)) return -1;
        string s = choice.Trim();
        if (s.Length < 2 || (s[0] != 'm' && s[0] != 'M')) return -1;
        return int.TryParse(s.AsSpan(1), out int n) && n >= 1 ? n - 1 : -1;
    }

    /// <summary>
    /// 本地基线算出来的保留项 = 组内第一项（<see cref="DuplicateDetector.FullGroupOrder"/>
    /// 把「路径最短 / 最浅」排在最前，重复检测也是按它产出 keep 行的）。
    /// </summary>
    internal static CleanItem BaselineKeep(CleanLocationNode node) => node.Items[0];

    /// <summary>选项表：组内每个成员一项，键 <c>m1..mN</c>，值是**脱敏后的路径**。</summary>
    static Dictionary<string, string> Criteria(CleanLocationNode node)
    {
        int count = Math.Min(node.Items.Count, MaxMembers);
        var criteria = new Dictionary<string, string>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
            criteria["m" + (i + 1)] = PathRedactor.Outbound(node.Items[i].FullPath);
        return criteria;
    }

    /// <summary>
    /// 从这一屏的候选里挑出「该问的重复组」。
    ///
    /// 过滤规则（每一条都有对应断言）：
    /// <list type="bullet">
    /// <item>只有 <see cref="CleanPurpose.Duplicate"/> 的位置行；</item>
    /// <item>只有**保留项**那一行（<c>dup-keep|</c>）—— 多余项那一行是同一组的另一半，
    ///   问两遍等于付两次钱问同一个问题；</item>
    /// <item>成员数在 <see cref="MinMembers"/>..<see cref="MaxMembers"/> 之间；</item>
    /// <item>已经拿到过建议的组跳过（同一轮里再点一次按钮不该重复付钱）。</item>
    /// </list>
    /// </summary>
    public static List<CleanLocationNode> SelectTargets(IEnumerable<CleanLocationNode> locations)
    {
        var targets = new List<CleanLocationNode>();
        foreach (var node in locations)
        {
            if (node.Purpose != CleanPurpose.Duplicate) continue;
            if (!node.Key.Contains("dup-keep", StringComparison.Ordinal)) continue;
            if (node.Items.Count < MinMembers || node.Items.Count > MaxMembers) continue;
            if (AlreadySuggested(node)) continue;
            targets.Add(node);
        }
        return targets;
    }

    /// <summary>这一组的保留项说明里是不是已经有建议了（判断在模型那一侧，见 <see cref="CleanLocationNode.AlreadySuggested"/>）。</summary>
    internal static bool AlreadySuggested(CleanLocationNode node) => node.AlreadySuggested;
}
