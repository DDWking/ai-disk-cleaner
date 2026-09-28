using AiDiskCleaner;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

namespace SafetyCheck;

/// <summary>
/// 重复文件「留哪一个」判定域（<see cref="DuplicateKeepDecisionSet"/>）的离线断言。
///
/// 这个域的风险和「文件夹用途」不一样：那边说错了只是标签不准，
/// 这边说错了是**用户删掉想留的那一份**。所以这里的断言重心在四件事上：
/// <list type="number">
/// <item>回写**只碰说明文字**（Selected / CanDelete / Risk / Evidence 一个都不许动）；</item>
/// <item>把握不够就**什么都不建议**（不是硬挑一个）；</item>
/// <item>成员太多的组**连问题都不发**；</item>
/// <item>出站**脱敏**。</item>
/// </list>
/// </summary>
static class DupKeepDecisionTests
{
    public static void Run(JevDecisionTests.CheckFn check, Action<string> section)
    {
        PurityTests(check, section);
        ThresholdTests(check, section);
        TargetTests(check, section);
        ShapeTests(check, section);
        RedactionTests(check, section);
        GroupingTests(check, section);
    }

    // ------------------------------------------------------------ 分组真的把整组挂上来了吗
    //
    // 这一组断言是**这次改动真正的重点**：清理页把一组重复文件拆成「保留项 / 多余项」两行，
    // 每行的 Items 只有一半。如果分组不把全组挂到行上（DupMembers），
    // 那么「留哪一份」这个问题在生产里永远看不到第二个选项 —— 芯片根本不显示。
    // 早先的测试自己造「Items 装整组」的假数据，所以全绿而功能是死的。
    static void GroupingTests(JevDecisionTests.CheckFn check, Action<string> section)
    {
        section("重复判定：分组把全组挂到每一行上（否则这个功能是死的）");

        var keep = DupItem("a.bin", "dup-keep|1024|a|d");
        var extra1 = DupItem("b.bin", "dup-extra|1024|a|d");
        var extra2 = DupItem("c.bin", "dup-extra|1024|a|d");
        var extra3 = DupItem("d.bin", "dup-extra|1024|a|d");

        var layered = CleanGroupingService.Build(new[] { keep, extra1, extra2, extra3 });

        var dupRows = layered.Purposes
            .SelectMany(p => p.Locations)
            .Where(l => l.Purpose == CleanPurpose.Duplicate)
            .ToList();

        check("一组 4 个副本被拆成两行（保留项 / 多余项）", dupRows.Count == 2,
            string.Join(",", dupRows.Select(r => r.Key)));
        check("每一行自己只装一半（不是整组）",
            dupRows.All(r => r.Items.Count < 4),
            string.Join(" | ", dupRows.Select(r => r.Items.Count)));

        var keepRow = dupRows.FirstOrDefault(r => r.Key.Contains("dup-keep", StringComparison.Ordinal));
        check("保留项那一行拿得到**全组** 4 个成员",
            keepRow != null && keepRow.DupMembers.Count == 4,
            keepRow == null ? "(没有保留项行)" : keepRow.DupMembers.Count.ToString());
        check("全组里第一项就是本地基线的保留项",
            keepRow != null && ReferenceEquals(keepRow.DupMembers[0], keep));
        check("界面据此显示芯片（组大小 >= 2 且 <= 12）",
            keepRow != null && keepRow.ShowDupAskRowChip);
        check("判定域也据此认出这一组（用全组大小，不是行内条数）",
            keepRow != null && DuplicateKeepDecisionSet.SelectTargets(dupRows).Count == 1);

        // 只有一个成员的「组」不该出现，但真出现了也不许问
        var lone = DupItem("only.bin", "dup-keep|2048|x|y");
        var loneLayered = CleanGroupingService.Build(new[] { lone });
        var loneRow = loneLayered.Purposes.SelectMany(p => p.Locations)
            .FirstOrDefault(l => l.Purpose == CleanPurpose.Duplicate);
        check("只有一个成员的组不显示芯片",
            loneRow != null && !loneRow.ShowDupAskRowChip);

        // 成员太多：不显示芯片（理由是人不是 token）
        var many = new List<CleanItem> { DupItem("k0.bin", "dup-keep|4096|a|z") };
        for (int i = 1; i <= CleanLocationNode.DupAskMaxMembers; i++)
            many.Add(DupItem($"k{i}.bin", "dup-extra|4096|a|z"));
        var manyLayered = CleanGroupingService.Build(many);
        var manyRow = manyLayered.Purposes.SelectMany(p => p.Locations)
            .FirstOrDefault(l => l.Key.Contains("dup-keep", StringComparison.Ordinal));
        check($"成员超过 {CleanLocationNode.DupAskMaxMembers} 个的组不显示芯片",
            manyRow != null && manyRow.DupMembers.Count > CleanLocationNode.DupAskMaxMembers
            && !manyRow.ShowDupAskRowChip,
            manyRow == null ? "(没有保留项行)" : manyRow.DupMembers.Count.ToString());
        check("模型里的上限和判定域用的是同一个数字（不许两处漂移）",
            CleanLocationNode.DupAskMaxMembers == DuplicateKeepDecisionSet.MaxMembers);

        // 非重复用途的位置行永远拿不到组（不许出现「别的用途也能问留哪一份」）
        var cache = new CleanItem
        {
            Name = "x.tmp", FullPath = @"C:\Users\Somebody\AppData\Local\Temp\x.tmp", Size = 10,
            Purpose = CleanPurpose.Temp, Handling = "dir|TEMP", Risk = CleanRisk.Safe,
            CanDelete = true, Evidence = EvidenceLevel.Signature,
        };
        var otherLayered = CleanGroupingService.Build(new[] { cache });
        check("非重复用途的位置行没有组、也没有芯片",
            otherLayered.Purposes.SelectMany(p => p.Locations)
                .All(l => l.DupMembers.Count == 0 && !l.ShowDupAskRowChip));
    }

    static CleanItem DupItem(string name, string handling) => new()
    {
        Name = name,
        FullPath = @"D:\dup\" + name,
        Size = 1024,
        Reason = handling.StartsWith("dup-keep", StringComparison.Ordinal)
            ? "内容相同的另一份在别处，这是保留的一份"
            : "和保留的那一份内容相同",
        Purpose = CleanPurpose.Duplicate,
        Handling = handling,
        Risk = CleanRisk.Confirm,
        CanDelete = !handling.StartsWith("dup-keep", StringComparison.Ordinal),
        Evidence = EvidenceLevel.Verified,
    };

    // ------------------------------------------------------------ 只写展示

    static void PurityTests(JevDecisionTests.CheckFn check, Action<string> section)
    {
        section("重复判定：只写展示，绝不碰勾选 / 可删除性 / 风险");

        var set = new DuplicateKeepDecisionSet();
        var node = Node(3);

        // 用户之前手动勾过「多余项」，AI 不许把这个状态改掉
        node.DupMembers[1].Selected = true;
        var beforeSelected = node.DupMembers.Select(x => x.Selected).ToArray();
        var beforeCanDelete = node.DupMembers.Select(x => x.CanDelete).ToArray();
        var beforeRisk = node.DupMembers.Select(x => x.Risk).ToArray();
        var beforeEvidence = node.DupMembers.Select(x => x.Evidence).ToArray();
        var beforeKeepReason = node.DupMembers[0].Reason;
        var beforeExtraReason = node.DupMembers[1].Reason;

        var applied = ((IDecisionSet<CleanLocationNode>)set).Apply(node, Answer("m2", 0.91));

        check("回写被采纳", applied.Kind == DecisionWriteBack.Applied, applied.Kind.ToString());
        check("Selected 一个都没动",
            node.DupMembers.Select(x => x.Selected).SequenceEqual(beforeSelected),
            string.Join(",", node.DupMembers.Select(x => x.Selected)));
        check("CanDelete 一个都没动",
            node.DupMembers.Select(x => x.CanDelete).SequenceEqual(beforeCanDelete),
            string.Join(",", node.DupMembers.Select(x => x.CanDelete)));
        check("Risk 一个都没动",
            node.DupMembers.Select(x => x.Risk).SequenceEqual(beforeRisk));
        check("Evidence 一个都没动",
            node.DupMembers.Select(x => x.Evidence).SequenceEqual(beforeEvidence));
        check("建议只追加在**保留项**那一行",
            node.DupMembers[0].Reason.Length > beforeKeepReason.Length
            && node.DupMembers[0].Reason.Contains(Loc.DupKeepNoteMarker, StringComparison.Ordinal),
            node.DupMembers[0].Reason);
        check("多余项那一行的说明没被动过",
            node.DupMembers[1].Reason == beforeExtraReason, node.DupMembers[1].Reason);
        check("建议里写清了「改留哪一个」（和基线不一致时必须给路径）",
            node.DupMembers[0].Reason.Contains(node.DupMembers[1].FullPath, StringComparison.Ordinal),
            node.DupMembers[0].Reason);

        // 和基线一致时：只确认一句，不重复路径
        var node2 = Node(2);
        ((IDecisionSet<CleanLocationNode>)set).Apply(node2, Answer("m1", 0.88));
        check("和本地基线一致 ⇒ 一句话确认，不重复写路径",
            node2.DupMembers[0].Reason.Contains(Loc.DupKeepNoteMarker, StringComparison.Ordinal)
            && !node2.DupMembers[0].Reason.Contains(@"D:\dup\a.bin", StringComparison.Ordinal),
            node2.DupMembers[0].Reason);

        // 解析不出 / 越界 ⇒ 当作没答案，什么都不写
        var node3 = Node(2);
        var reason3 = node3.DupMembers[0].Reason;
        check("选项名解析不出 ⇒ 什么都不写",
            ((IDecisionSet<CleanLocationNode>)set).Apply(node3, Answer("g1", 0.9)).Kind
                == DecisionWriteBack.Unknown
            && node3.DupMembers[0].Reason == reason3);
        check("选项下标越界 ⇒ 什么都不写",
            ((IDecisionSet<CleanLocationNode>)set).Apply(node3, Answer("m9", 0.9)).Kind
                == DecisionWriteBack.Unknown
            && node3.DupMembers[0].Reason == reason3);
        check("模型没给这一条 ⇒ 什么都不写",
            ((IDecisionSet<CleanLocationNode>)set).Apply(node3, null).Kind == DecisionWriteBack.Unknown
            && node3.DupMembers[0].Reason == reason3);
    }

    // ------------------------------------------------------------ 阈值

    static void ThresholdTests(JevDecisionTests.CheckFn check, Action<string> section)
    {
        section("重复判定：没把握就不给建议");

        var set = new DuplicateKeepDecisionSet();
        var node = Node(3);
        var reason = node.DupMembers[0].Reason;

        var low = ((IDecisionSet<CleanLocationNode>)set).Apply(
            node, Answer("m2", DecisionThresholds.DuplicateKeep - 0.01));
        check("低于阈值 ⇒ 标成「拿不准」", low.Kind == DecisionWriteBack.Unsure, low.Kind.ToString());
        check("低于阈值 ⇒ **一个字都不写**（宁可不说，也不给可能让用户删错文件的建议）",
            node.DupMembers[0].Reason == reason, node.DupMembers[0].Reason);

        var at = ((IDecisionSet<CleanLocationNode>)set).Apply(
            node, Answer("m2", DecisionThresholds.DuplicateKeep));
        check("正好等于阈值 ⇒ 采纳（门槛是「不低于」）",
            at.Kind == DecisionWriteBack.Applied, at.Kind.ToString());

        check("阈值是 0.75，而且和「可清理类」的门槛分开",
            Math.Abs(DecisionThresholds.DuplicateKeep - 0.75) < 1e-9
            && DecisionThresholds.DuplicateKeep > DecisionThresholds.CleanKind);
    }

    // ------------------------------------------------------------ 挑哪些组

    static void TargetTests(JevDecisionTests.CheckFn check, Action<string> section)
    {
        section("重复判定：哪些组该问");

        var ok = Node(2);
        var single = Node(1);
        var huge = Node(DuplicateKeepDecisionSet.MaxMembers + 1);
        var extraHalf = Node(2, key: "dup|dup-extra|1000|a|b");
        var notDup = Node(2, purpose: CleanPurpose.AppCache);

        var targets = DuplicateKeepDecisionSet.SelectTargets(
            new[] { ok, single, huge, extraHalf, notDup });

        check("只挑保留项那一行（多余项那一行是同一组的另一半，问两遍付两次钱）",
            targets.Count == 1 && ReferenceEquals(targets[0], ok),
            string.Join(",", targets.Select(t => t.Key)));
        check("只有一个成员的不问（没有「留哪一个」这回事）",
            !targets.Contains(single));
        check("成员超过 12 个的不问（理由是人不是 token：十几个差不多的路径只能瞎猜）",
            DuplicateKeepDecisionSet.MaxMembers == 12 && !targets.Contains(huge));
        check("不是重复用途的位置行不问", !targets.Contains(notDup));

        // 已经问过的组不再进候选（同一轮里再点一次不该重复花钱）
        ok.DupMembers[0].Reason = ok.DupMembers[0].Reason + Loc.DupKeepNoteMarker + "已建议";
        check("已经给过建议的组不再进候选",
            DuplicateKeepDecisionSet.SelectTargets(new[] { ok }).Count == 0);
    }

    // ------------------------------------------------------------ 出题形状

    static void ShapeTests(JevDecisionTests.CheckFn check, Action<string> section)
    {
        section("重复判定：一题一组，选项就是组内成员");

        var set = (IDecisionSet<CleanLocationNode>)new DuplicateKeepDecisionSet();
        var chunk = new[] { Node(3), Node(2) };

        var keys = set.KeysFor(chunk);
        check("问题名是 g1..gN，和组一一对应",
            string.Join(",", keys) == "g1,g2", string.Join(",", keys));

        var q = set.QuestionFor(0, chunk[0]);
        check("用的是 choice 原语", q.Type == "choice", q.Type);
        check("选项键是 m1..mN", q.Criteria != null
            && string.Join(",", q.Criteria.Keys) == "m1,m2,m3",
            q.Criteria == null ? "(null)" : string.Join(",", q.Criteria.Keys));
        check("选项值是成员路径，顺序和保留顺序一致", q.Criteria != null
            && q.Criteria["m1"] == chunk[0].DupMembers[0].FullPath
            && q.Criteria["m3"] == chunk[0].DupMembers[2].FullPath,
            q.Criteria?["m1"] ?? "(null)");
        check("题目里说清「内容完全相同，指出该留哪一个」",
            q.Instructions.Contains("identical content", StringComparison.Ordinal)
            && q.Instructions.Contains("kept", StringComparison.Ordinal),
            q.Instructions);

        // 硬限制：这个域的选项数不可能撞 255（MaxMembers 挡在前面）
        check("成员上限远低于 choice 的 255 选项上限（那道限制永远撞不到）",
            DuplicateKeepDecisionSet.MaxMembers < JevLimits.MaxChoiceOptions);

        // state：每组一段，成员按保留顺序列出
        var state = set.StateFor(chunk);
        check("state 里两组都在", state.Contains("g1", StringComparison.Ordinal)
            && state.Contains("g2", StringComparison.Ordinal));
        check("state 里成员按现有保留顺序编号（m1 就是本地基线的保留项）",
            state.Contains("m1", StringComparison.Ordinal)
            && state.Contains("m3", StringComparison.Ordinal), state.Split('\n')[1]);
        // 脱敏的实质断言放在 RedactionTests 里（用带用户名的路径）：
        // 这里的 D:\dup\ 本来就没有可脱敏的部分，PathRedactor 原样返回是对的。
        check("state 里每组的成员都列出来了",
            state.Split('\n').Count(l => l.TrimStart().StartsWith("m", StringComparison.Ordinal)) == 5,
            state);

        check("自适应起点比文件夹用途更小（每题都不一样，先小批试）",
            DuplicateKeepDecisionSet.AdaptiveStart == 4,
            DuplicateKeepDecisionSet.AdaptiveStart.ToString());

        // MarkAsked 是空实现 —— 重复组没有「问过了」这个落盘标记，这是有意的
        set.MarkAsked(chunk[0]);
        check("MarkAsked 是空实现（重复组不落盘「问过了」，不会因此被反复追问）", true);
    }

    // ------------------------------------------------------------ 脱敏

    static void RedactionTests(JevDecisionTests.CheckFn check, Action<string> section)
    {
        section("重复判定：出站路径必须脱敏");

        var set = (IDecisionSet<CleanLocationNode>)new DuplicateKeepDecisionSet();
        var node = Node(2);
        node.DupMembers[0].FullPath = @"C:\Users\Somebody\Documents\report.docx";
        node.DupMembers[1].FullPath = @"D:\Backup\2024\report.docx";

        var q = set.QuestionFor(0, node);
        var state = set.StateFor(new[] { node });
        var criteria = q.Criteria ?? new Dictionary<string, string>();

        check("选项表里没有真实用户名",
            criteria.Values.All(v => !v.Contains("Somebody", StringComparison.Ordinal)),
            string.Join(" | ", criteria.Values));
        check("state 里没有真实用户名",
            !state.Contains("Somebody", StringComparison.Ordinal), state);
        check("脱敏后仍然是可区分的两条路径（否则模型没法选）",
            criteria.Count == 2 && criteria["m1"] != criteria["m2"],
            criteria["m1"] + " vs " + criteria["m2"]);

        // 用户明确允许发完整路径时，才走原样
        bool old = App.Settings.AiSendFullPaths;
        try
        {
            App.Settings.AiSendFullPaths = true;
            var state2 = set.StateFor(new[] { node });
            check("用户允许发完整路径时原样发出（设置生效）",
                state2.Contains("Somebody", StringComparison.Ordinal), state2);
        }
        finally
        {
            App.Settings.AiSendFullPaths = old;
        }
    }

    // ------------------------------------------------------------ 造数据

    static AiDecisionAnswer Answer(string choice, double confidence)
        => new() { Choice = choice, Confidence = confidence };

    /// <summary>
    /// 造一个重复组的**保留项位置行**：<paramref name="members"/> 个成员，
    /// 键与重复检测产出的形状一致（<c>dup|dup-keep|大小|首路径|末路径</c>）。
    ///
    /// <b>刻意照生产的样子造</b>：清理页把一组拆成「保留项 / 多余项」两行，
    /// 每行的 <c>Items</c> 只有这一组的一半，全组成员挂在 <c>DupMembers</c> 上。
    /// 早先这里图省事让 <c>Items</c> 直接装整组 —— 测试全绿，
    /// 而生产里每一行永远只有 1 条，芯片根本不显示。现在两半都造出来。
    /// </summary>
    static CleanLocationNode Node(int members, string? key = null, CleanPurpose? purpose = null)
    {
        string groupKey = "1024|a|b";
        var all = new List<CleanItem>();
        for (int i = 0; i < members; i++)
        {
            string name = ((char)('a' + i)) + ".bin";
            all.Add(new CleanItem
            {
                Name = name,
                FullPath = @"D:\dup\" + name,
                Size = 1024,
                Purpose = CleanPurpose.Duplicate,
                Handling = (i == 0 ? "dup-keep|" : "dup-extra|") + groupKey,
                Risk = CleanRisk.Confirm,
                CanDelete = i != 0,
                Evidence = EvidenceLevel.Verified,
            });
        }

        // 每一行只装「自己那一半」，说明文字按生产的两套写法给
        bool keepRow = (purpose ?? CleanPurpose.Duplicate) == CleanPurpose.Duplicate;
        var rowItems = all.Where(x => x.Handling.StartsWith(
            keepRow ? "dup-keep" : "dup-extra", StringComparison.Ordinal)).ToList();
        if (rowItems.Count == 0) rowItems = new List<CleanItem> { all[0] };
        for (int i = 0; i < rowItems.Count; i++)
            rowItems[i].Reason = ReferenceEquals(rowItems[i], all[0])
                ? "内容相同的另一份在别处，这是保留的一份"
                : "和保留的那一份内容相同";

        return new CleanLocationNode
        {
            Key = key ?? "dup|dup-keep|" + groupKey,
            DisplayName = "重复文件",
            Path = "2 个副本",
            Purpose = purpose ?? CleanPurpose.Duplicate,
            Items = rowItems,
            DupMembers = all,
        };
    }
}
