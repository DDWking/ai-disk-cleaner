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
        OrganizeWritePurityTests(check);
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
        check("keep 阈值不低于 0.5", keep >= 0.5, keep.ToString());

        check("清理类低于阈值不采纳", !AiPurposeCriteria.IsAccepted(AiPurposeKind.AppCache, cache - 0.01));
        check("清理类到阈值即采纳", AiPurposeCriteria.IsAccepted(AiPurposeKind.AppCache, cache));
        check("keep 低于阈值不采纳", !AiPurposeCriteria.IsAccepted(AiPurposeKind.Keep, keep - 0.01));

        // 实测里低置信的那批（0.22~0.46）必须全部被挡掉
        foreach (var c in new[] { 0.22, 0.24, 0.28, 0.36, 0.40, 0.42, 0.43, 0.46 })
        {
            if (AiPurposeCriteria.IsAccepted(AiPurposeKind.AppCache, c)
                || AiPurposeCriteria.IsAccepted(AiPurposeKind.Keep, c))
            {
                check($"实测低置信样本 {c} 必须被挡掉", false);
                return;
            }
        }
        check("实测低置信样本（0.22~0.46）全部被挡掉", true);

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

        // 逐项 AI 的槽位一个字都不能碰：碰了界面就会假装逐项分析跑过
        check("写入后：没有假装逐项 AI 跑过（Status 仍 Idle、Result 仍空）",
            node.Ai.Status == ItemAiStatus.Idle && node.Ai.Result == null,
            $"{node.Ai.Status}/{node.Ai.Result}");

        // 本地结论优先：批量不许顶掉已经认出来的
        node.Apply(Local(@"D:\some\unknown\folder", "我自己的项目"));
        check("本地结论优先于批量归类", node.PurposeText == "我自己的项目", node.PurposeText);
        check("本地结论下来源是本地，不是 AI", node.Source == PurposeSource.Local, node.Source.ToString());

        // 逐项 AI 结论优先于批量（它读过这一项的摘要，更具体）
        var both = Node(@"D:\some\both");
        both.SetBatchPurpose("软件缓存");
        both.Ai.Source = ItemAiSource.Organize;
        both.Ai.Result = new ItemAiResult(ItemAiSuggestion.Unknown, "Steam 游戏库",
            "", "", "", "", false, 0, 0, 0, "", 0);
        both.Ai.Status = ItemAiStatus.Done;
        check("逐项 AI 结论优先于批量", both.PurposeText == "Steam 游戏库", both.PurposeText);

        // 清空可以回到未识别
        var clear = Node(@"D:\some\clear");
        clear.SetBatchPurpose("软件缓存");
        clear.SetBatchPurpose("");
        check("清空批量用途后回到未识别",
            !clear.HasConclusion && clear.State == PurposeState.Unrecognized);
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
