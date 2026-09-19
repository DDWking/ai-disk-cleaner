using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

namespace SafetyCheck;

/// <summary>
/// 批量用途归类（结构化判定通道）的针对性回归。全部离线、纯模型层 + 源码级不变量。
///
/// 服务对象是**「按文件夹删除」页的文件夹**（清理中心的候选项由 8 条规则产出，
/// 每条规则都声明了用途，不会有「认不出来」的堆积）。
///
/// 这一批守的是三条不变量：
/// <list type="number">
/// <item><b>选项表是闭合的。</b>只认这 10 个键；认不出的键必须落成 Unknown，
///   绝不能因为模型吐了个新词就当成某个具体用途。</item>
/// <item><b>阈值按判错代价分档。</b>说「这是缓存」说错了会误导用户去删，
///   所以清理类阈值必须高于 keep；低于阈值一律不采纳。</item>
/// <item><b>写入是纯展示。</b>只落在 <see cref="OrganizeNode.BatchPurpose"/> 上，
///   不碰逐项 AI 的槽位、不碰任何清理能力 —— 而整理树的对象**结构上就没有**
///   Selected / CanDelete / Risk，所以「AI 不会替用户打勾」在这里是结构成立的。</item>
/// </list>
/// </summary>
public static class AiPurposeTests
{
    public delegate void CheckFn(string name, bool ok, string? detail = null);

    public static void Run(CheckFn check, Action<string> section)
    {
        section("AI 用途归类：选项表 / 阈值 / 写入纯度");
        OptionTableTests(check);
        ThresholdTests(check);
        EligibilityTests(check);
        LocalCleanableTests(check);
        OrganizeWritePurityTests(check);
        BucketTests(check);
        SourceInvariantTests(check);
    }

    // ------------------------------------------------------------ 选项表

    static void OptionTableTests(CheckFn check)
    {
        // 10 个键，一个不多一个不少
        var expected = new[] { "temp", "browsercache", "appcache", "devcache", "applog", "dump", "installer", "model", "keep", "unknown" };
        var actual = AiPurposeCriteria.Keys;
        check("选项表正好 10 类", actual.Length == 10, string.Join(",", actual));
        check("选项表的键与约定一致",
            expected.All(k => actual.Contains(k)) && actual.All(k => expected.Contains(k)),
            string.Join(",", actual));
        // 除 unknown 自己以外，每个键都必须解析成一个具体大类（不许漏成 Unknown）
        check("除 unknown 外每个键都解析成具体大类",
            actual.Where(k => k != "unknown").All(k => AiPurposeCriteria.Parse(k) != AiPurposeKind.Unknown),
            string.Join(",", actual.Where(k => k != "unknown" && AiPurposeCriteria.Parse(k) == AiPurposeKind.Unknown)));

        // 键是契约，文案只是展示：两边不许漂移
        string locSrc = ReadSource("src/AiDiskCleaner/Services/Loc.cs");
        check("Loc 的选项表包含全部规范键",
            actual.All(k => locSrc.Contains($"(\"{k}\"", StringComparison.Ordinal)),
            string.Join(",", actual.Where(k => !locSrc.Contains($"(\"{k}\"", StringComparison.Ordinal))));

        // 每个能被采纳的类别都要有一句能看的短名（整理页的用途是自由文本，不是枚举）。
        // 这里按源码断言，而不是调 Loc —— SafetyCheck 用的是 Loc 的桩，
        // 调桩只会测到桩自己，测不到「文案到底加没加」。
        check("Loc 里每个类别都写了短名",
            actual.Where(k => k != "unknown").All(k =>
                locSrc.Contains($"AiPurposeKind.{AiPurposeCriteria.Parse(k)} =>", StringComparison.Ordinal)),
            string.Join(",", actual.Where(k => k != "unknown"
                && !locSrc.Contains($"AiPurposeKind.{AiPurposeCriteria.Parse(k)} =>", StringComparison.Ordinal))));

        // 认得出的键
        check("temp 解析正确", AiPurposeCriteria.Parse("temp") == AiPurposeKind.Temporary);
        check("devcache 解析正确", AiPurposeCriteria.Parse("devcache") == AiPurposeKind.DevCache);
        check("installer 解析正确", AiPurposeCriteria.Parse("installer") == AiPurposeKind.Installer);
        check("model 解析正确", AiPurposeCriteria.Parse("model") == AiPurposeKind.Model);
        check("keep 解析正确", AiPurposeCriteria.Parse("keep") == AiPurposeKind.Keep);
        check("大小写与空格不影响解析", AiPurposeCriteria.Parse("  DevCache ") == AiPurposeKind.DevCache);

        // 闭合性：模型吐任何没约定的词，一律 Unknown，绝不落到某个具体用途
        // 注意 "model " 不算垃圾：Parse 会 trim，带空格是同一个键（上面已断言）
        foreach (var junk in new[] { "cache", "temporary", "browser", "", "   ", "delete", "safe", "keeep", "TRASH", "dev cache" })
        {
            var k = AiPurposeCriteria.Parse(junk);
            if (k != AiPurposeKind.Unknown)
            {
                check($"未约定的键必须落 Unknown：\"{junk}\"", false, k.ToString());
                return;
            }
        }
        check("未约定的键一律落 Unknown（闭合）", true);

        // 回写映射：只有 7 个缓存类能落到 CleanPurpose（整理页不走它，但清理树的映射仍在）
        var mappable = new[] { AiPurposeKind.Temporary, AiPurposeKind.BrowserCache, AiPurposeKind.AppCache,
                               AiPurposeKind.DevCache, AiPurposeKind.AppLog, AiPurposeKind.Dump, AiPurposeKind.Installer };
        check("7 个可清理类都能落到 CleanPurpose",
            mappable.All(k => AiPurposeCriteria.TryToPurpose(k, out _)));
        check("keep / model / unknown 落不到用途上",
            new[] { AiPurposeKind.Keep, AiPurposeKind.Model, AiPurposeKind.Unknown }
                .All(k => !AiPurposeCriteria.TryToPurpose(k, out _)));
    }

    // -------------------------------------------------------------- 阈值

    static void ThresholdTests(CheckFn check)
    {
        double cache = AiPurposeCriteria.ThresholdFor(AiPurposeKind.AppCache);
        double keep = AiPurposeCriteria.ThresholdFor(AiPurposeKind.Keep);
        check("清理类阈值高于 keep（判错代价不同）", cache > keep, $"cache={cache} keep={keep}");
        check("清理类阈值不低于 0.7", cache >= 0.7, cache.ToString());
        // （旧断言「keep 阈值不低于 0.5」已被下面的「keep 不设门槛」取代 —— 那是旧设计，
        //   当时的理由是「方向保守」，但实测证明它会把模型往猜缓存上逼，方向正好反了。）

        check("清理类低于阈值不采纳", !AiPurposeCriteria.IsAccepted(AiPurposeKind.AppCache, cache - 0.01));
        check("清理类到阈值即采纳", AiPurposeCriteria.IsAccepted(AiPurposeKind.AppCache, cache));

        // **两侧刻意不对称**：拒绝猜「缓存」是保护，拒绝猜「别删」是危险。
        // 一条拿不准的 keep 被挡掉，界面就只剩「未识别」—— 比一句「像是你的数据」危险得多。
        // 实测：AppData\Local\Programs 猜 keep 0.39 被 0.55 挡掉 → 显示未识别 → 那是装软件的地方。
        check("keep 不设门槛（拿不准也照示）",
            AiPurposeCriteria.ThresholdFor(AiPurposeKind.Keep) == 0
            && AiPurposeCriteria.IsAccepted(AiPurposeKind.Keep, 0));
        check("keep 的阈值确实低于清理类", keep < cache);

        // 实测里低置信的那批（0.22~0.46）对**清理类**必须全部被挡掉
        foreach (var c in new[] { 0.22, 0.24, 0.28, 0.36, 0.40, 0.42, 0.43, 0.46 })
        {
            if (AiPurposeCriteria.IsAccepted(AiPurposeKind.AppCache, c)
                || AiPurposeCriteria.IsAccepted(AiPurposeKind.Installer, c))
            {
                check($"实测低置信样本 {c} 对清理类必须被挡掉", false);
                return;
            }
        }
        check("实测低置信样本（0.22~0.46）对清理类全部被挡掉", true);

        check("Unknown 永远不采纳", !AiPurposeCriteria.IsAccepted(AiPurposeKind.Unknown, 1.0));
    }

    // ---------------------------------------------------------- 入选判据

    static FileEntry DirNode(string path)
        => new()
        {
            Name = System.IO.Path.GetFileName(path),
            FullPath = path,
            Kind = EntryKind.Directory,
            Size = 4096,
        };

    static OrganizeNode Node(string path)
    {
        var dir = DirNode(path);
        var n = new OrganizeNode(dir, new FolderId(path, 1), 0, dir.Name);
        n.SetLevel(1);
        return n;
    }

    /// <summary>一条**本地**结论（不是 AI 推测），用来验证「批量不许顶掉已经认出来的」。</summary>
    static FolderPurposeResult Local(string path, string name)
        => new(new FolderId(path, 1), name, "开发", "有源码",
            PurposeSource.Local, NeedsConfirm: false, FolderKind.Concrete);

    static void EligibilityTests(CheckFn check)
    {
        var fresh = Node(@"D:\some\unknown\folder");
        check("没结论 + 有路径 = 等着归类", fresh.NeedsPurposeClassification);

        var done = Node(@"D:\some\other");
        done.SetBatchPurpose("软件缓存");
        check("已经有批量结论的不再入选", !done.NeedsPurposeClassification);

        var local = Node(@"D:\some\local");
        local.Apply(Local(@"D:\some\local", "我自己的项目"));
        check("本地已经有结论的不再入选", !local.NeedsPurposeClassification);

        var failed = Node(@"D:\some\failed");
        failed.SetState(PurposeState.Failed);
        check("失败态不入选（页头把它算「失败」，不混进「未知」）", !failed.NeedsPurposeClassification);

        var blank = Node("");
        check("路径为空的不入选", !blank.NeedsPurposeClassification);
    }

    // ------------------------------------------------------ 写入纯度（重点）

    static void OrganizeWritePurityTests(CheckFn check)
    {
        // 结构上就没有清理能力：不是「代码没写」，而是**根本没有这些成员**。
        // 这比任何运行时断言都硬 —— 想越权都没有字段可写。
        var t = typeof(OrganizeNode);
        foreach (var forbidden in new[] { "Selected", "CanDelete", "Risk", "CanSelect", "Purpose", "Evidence" })
        {
            if (t.GetProperty(forbidden) != null)
            {
                check($"整理对象结构上没有 {forbidden}", false);
                return;
            }
        }
        check("整理对象结构上没有 Selected / CanDelete / Risk / CanSelect / Purpose / Evidence", true);

        var node = Node(@"D:\some\unknown\folder");
        check("写入前：没有结论、状态是未识别",
            !node.HasConclusion && node.State == PurposeState.Unrecognized);

        node.SetBatchPurpose("开发工具缓存");

        check("写入后：有结论", node.HasConclusion);
        check("写入后：来源显示为 AI 推测", node.Source == PurposeSource.Ai, node.Source.ToString());
        check("写入后：用途列就是这个用途", node.PurposeText == "开发工具缓存", node.PurposeText);
        check("写入后：不再是未识别", node.State != PurposeState.Unrecognized, node.State.ToString());

        // **逐项 AI 的槽位已经不存在了** —— 整套能力删掉之后，整理对象上只剩
        // 批量归类这一个 AI 挂点。「不会假装逐项分析跑过」这道断言因此升级成结构断言：
        // 没有那个槽位，就没有「假装」的可能。
        check("整理对象上已经没有逐项 AI 的槽位",
            typeof(OrganizeNode).GetProperty("Ai") == null);

        // 本地结论优先：批量不许顶掉已经认出来的
        node.Apply(Local(@"D:\some\unknown\folder", "我自己的项目"));
        check("本地结论优先于批量归类", node.PurposeText == "我自己的项目", node.PurposeText);
        check("本地结论下来源是本地，不是 AI", node.Source == PurposeSource.Local, node.Source.ToString());

        // 问过之后不再入选：没有它自动识别会无限追问同一条
        var asked = Node(@"D:\some\asked");
        check("没问过时是入选的", asked.NeedsPurposeClassification);
        asked.MarkBatchAsked();
        check("问过之后（哪怕没拿到结论）不再入选", !asked.NeedsPurposeClassification);

        // 清空可以回到未识别
        var clear = Node(@"D:\some\clear");
        clear.SetBatchPurpose("软件缓存");
        clear.SetBatchPurpose("");
        check("清空批量用途后回到未识别",
            !clear.HasConclusion && clear.State == PurposeState.Unrecognized);
    }

    // ------------------------------------------ 本地清理资格（从已删的 AI 结论测试搬来）

    /// <summary>
    /// 「受保护项永远不可能被判为可直接清理」。
    ///
    /// 这条断言原来长在 <c>AiVerdictTests</c> 里，但它守的**根本不是 AI** ——
    /// <c>IsCleanable</c> 是纯本地判据（<c>CanDelete &amp;&amp; Risk != Keep/Confirm</c>），
    /// 只是当初恰好和 AI 结论放在一个类里。逐项 AI 那套删掉时把它搬到了
    /// <see cref="CleanRuleEligibility"/>；断言跟着改指向，**不删**。
    /// </summary>
    static void LocalCleanableTests(CheckFn check)
    {
        static CleanItem Item(CleanRisk risk, bool canDelete)
            => new() { Name = "x", FullPath = @"C:\P\x.tmp", Size = 1, Risk = risk, CanDelete = canDelete };

        check("不可删的项不被判为可清理", !CleanRuleEligibility.IsCleanable(Item(CleanRisk.Safe, canDelete: false)));
        check("保留档不被判为可清理", !CleanRuleEligibility.IsCleanable(Item(CleanRisk.Keep, canDelete: true)));
        check("需确认档不被判为可清理", !CleanRuleEligibility.IsCleanable(Item(CleanRisk.Confirm, canDelete: true)));
        check("安全且可删才判为可清理", CleanRuleEligibility.IsCleanable(Item(CleanRisk.Safe, canDelete: true)));
    }

    // ------------------------------------------------ 「合并」面板的一行

    static void BucketTests(CheckFn check)
    {
        // 容量必须去掉被别的成员包住的那些：父子同时出现在一屏里是允许的，
        // 直接相加会把同一块空间算两遍（页头统计也因此不累加顶层对象）。
        var parent = Node(@"D:\apps");
        parent.Apply(Local(@"D:\apps", "我的软件"));
        var child = Node(@"D:\apps\inner");
        child.Apply(Local(@"D:\apps\inner", "我的软件"));
        var sibling = Node(@"D:\other");
        sibling.Apply(Local(@"D:\other", "我的软件"));
        parent.Dir.Size = 1000;
        child.Dir.Size = 400;
        sibling.Dir.Size = 300;

        var bucket = new OrganizeBucket
        {
            Name = "我的软件",
            Members = new[] { parent, child, sibling },
        };
        check("同类合并成一行", bucket.Count == 3, bucket.Count.ToString());
        check("容量去掉嵌套重复（1000+300，不是 1700）", bucket.Bytes == 1300, bucket.Bytes.ToString());
        check("容量文案非空", bucket.SizeText.Length > 0, bucket.SizeText);

        // 勾选回填：全勾 / 全不勾 / 部分
        parent.IsChecked = true; child.IsChecked = true; sibling.IsChecked = true;
        bucket.SyncFromMembers();
        check("成员全勾 → 这一行也是勾的", bucket.IsChecked);
        check("全勾时不显示「已选 x / y」", bucket.SelectedText.Length == 0, bucket.SelectedText);

        sibling.IsChecked = false;
        bucket.SyncFromMembers();
        check("部分勾选 → 这一行不显示成满勾", !bucket.IsChecked);
        check("部分勾选时如实说选了几个", bucket.SelectedText.Contains("2"), bucket.SelectedText);

        parent.IsChecked = false; child.IsChecked = false;
        bucket.SyncFromMembers();
        check("成员全不勾 → 这一行也不勾", !bucket.IsChecked);

        // 「还没认出来」那一桶没有整片勾选的能力：
        // 把它做成可整片勾选 = 送一个「一键删掉所有看不出来的」按钮，那是最危险的动作。
        var unnamed = new OrganizeBucket { Name = "还没认出来", Members = new[] { Node(@"D:\x") }, CanSelect = false };
        check("「还没认出来」不能整片勾选", !unnamed.CanSelect);
        check("有结论的桶可以整片勾选", bucket.CanSelect);

        // 桶模型自己绝不碰节点的勾选 —— 勾选只由用户在界面上点
        string src = ReadSource("src/AiDiskCleaner/Models/OrganizeBucket.cs");
        check("桶模型不写任何节点的勾选",
            src.Length > 0 && !src.Contains(".IsChecked =", StringComparison.Ordinal));

        // 只有那个点击处理器写节点勾选，而且写的是「桶的状态」
        string org = ReadSource("src/AiDiskCleaner/MainWindow.Organize.cs");
        check("只有点击处理器写节点勾选，且写的就是用户点出来的那个状态",
            org.Contains("m.IsChecked = bucket.IsChecked", StringComparison.Ordinal));
        check("硬拦的节点跳过去，勾不动",
            org.Contains("if (m.IsSelectionProtected) continue;", StringComparison.Ordinal));
    }

    // ---------------------------------------------------- 源码级不变量

    static void SourceInvariantTests(CheckFn check)
    {
        string src = ReadSource("src/AiDiskCleaner/Services/AiPurposeBatchService.cs");
        check("读到批量归类服务源码", src.Length > 0);
        if (src.Length == 0) return;

        check("批量归类只写 BatchPurpose", src.Contains("SetBatchPurpose", StringComparison.Ordinal));
        check("批量归类不写 Risk", !src.Contains(".Risk ="));
        check("批量归类不写 CanDelete", !src.Contains(".CanDelete ="));
        check("批量归类不写 Selected", !src.Contains(".Selected ="));
        check("批量归类不写 Evidence", !src.Contains(".Evidence ="));
        check("批量归类不碰逐项 AI 结果", !src.Contains(".Ai.Result =") && !src.Contains(".Ai.Status ="));

        // 出站路径必须走唯一入口：默认脱敏，只有用户明确允许才发完整路径。
        // 实测脱敏不影响归类（16/16 与非脱敏一致）—— <UserProfile>\Temp\ 照样认出是临时文件。
        check("出站路径走 PathRedactor.Outbound（默认脱敏）",
            src.Contains("PathRedactor.Outbound", StringComparison.Ordinal));
        check("批量归类不直接发 FullPath 给模型",
            !src.Contains("AiPurposeQuestion(chunk[i].FullPath", StringComparison.Ordinal)
            && !src.Contains("x => x.FullPath", StringComparison.Ordinal));

        // 路径必须写进问题里：行号引用在 48 项时就 8/16 自相矛盾（实测）
        string loc = ReadSource("src/AiDiskCleaner/Services/Loc.cs");
        check("问题里带路径（不让模型数行号）", loc.Contains("AiPurposeQuestion"));
        check("题目文案里嵌了路径参数",
            loc.Contains("What is this path: {path}") || loc.Contains("这个路径是什么：{path}"));
    }

    /// <summary>从 bin 往上找到仓库根，读源文件。</summary>
    static string ReadSource(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.targets"))) dir = dir.Parent;
        if (dir == null) return "";
        string full = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? File.ReadAllText(full) : "";
    }
}
