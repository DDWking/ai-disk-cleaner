using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

namespace SafetyCheck;

/// <summary>
/// 批量用途归类（结构化判定通道）的针对性回归。全部离线、纯模型层 + 源码级不变量。
///
/// 这一批守的是三条不变量 —— 它们是「松开一次只能一个」之后**唯一还绑着的绳子**：
/// <list type="number">
/// <item><b>选项表是闭合的。</b>只认这 10 个键；认不出的键必须落成 Unknown，
///   绝不能因为模型吐了个新词就当成某个可清理用途。</item>
/// <item><b>阈值按判错代价分档。</b>说「这是缓存」说错了会误导用户去删，
///   所以清理类阈值（0.75）必须高于 keep（0.55）；低于阈值一律不采纳。</item>
/// <item><b>AI 不许制造批选资格。</b><see cref="CleanRuleEligibility.GainsBatchEligibility"/>
///   是 AI 与「界面替你打勾」之间唯一的一道闸：本来有签名级证据、只是用途没认出来的候选，
///   写回用途会让它从不可批选变成可批选 —— 那等于 AI 绕道拿到了勾选权，必须撤销。</item>
/// </list>
/// </summary>
public static class AiPurposeTests
{
    public delegate void CheckFn(string name, bool ok, string? detail = null);

    public static void Run(CheckFn check, Action<string> section)
    {
        section("AI 用途归类：选项表 / 阈值 / 批选资格闸");
        OptionTableTests(check);
        ThresholdTests(check);
        BatchEligibilityGuardTests(check);
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

        // 认得出的键
        check("temp 解析正确", AiPurposeCriteria.Parse("temp") == AiPurposeKind.Temporary);
        check("devcache 解析正确", AiPurposeCriteria.Parse("devcache") == AiPurposeKind.DevCache);
        check("installer 解析正确", AiPurposeCriteria.Parse("installer") == AiPurposeKind.Installer);
        check("model 解析正确", AiPurposeCriteria.Parse("model") == AiPurposeKind.Model);
        check("keep 解析正确", AiPurposeCriteria.Parse("keep") == AiPurposeKind.Keep);
        check("大小写与空格不影响解析", AiPurposeCriteria.Parse("  DevCache ") == AiPurposeKind.DevCache);

        // 闭合性：模型吐任何没约定的词，一律 Unknown，绝不落到某个可清理用途
        // 注意 "model " 不算垃圾：Parse 会 trim，带空格是同一个键（下面单独断言）
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

        // 回写映射：只有 7 个缓存类能落到 CleanPurpose
        var mappable = new[] { AiPurposeKind.Temporary, AiPurposeKind.BrowserCache, AiPurposeKind.AppCache,
                               AiPurposeKind.DevCache, AiPurposeKind.AppLog, AiPurposeKind.Dump, AiPurposeKind.Installer };
        foreach (var k in mappable)
        {
            if (!AiPurposeCriteria.TryToPurpose(k, out _))
            {
                check($"可清理类应当能落到用途：{k}", false);
                return;
            }
        }
        check("7 个可清理类都能落到 CleanPurpose", true);

        foreach (var k in new[] { AiPurposeKind.Keep, AiPurposeKind.Model, AiPurposeKind.Unknown })
        {
            if (AiPurposeCriteria.TryToPurpose(k, out var mapped))
            {
                check($"{k} 不该落到 CleanPurpose", false, mapped.ToString());
                return;
            }
        }
        check("keep / model / unknown 落不到用途上（只做提示）", true);
    }

    // -------------------------------------------------------------- 阈值

    static void ThresholdTests(CheckFn check)
    {
        double cache = AiPurposeCriteria.ThresholdFor(AiPurposeKind.AppCache);
        double keep = AiPurposeCriteria.ThresholdFor(AiPurposeKind.Keep);
        check("清理类阈值高于 keep（判错代价不同）", cache > keep, $"cache={cache} keep={keep}");
        check("清理类阈值不低于 0.7", cache >= 0.7, cache.ToString());
        check("keep 阈值不低于 0.5", keep >= 0.5, keep.ToString());

        // 低于阈值一律不采纳
        check("清理类低于阈值不采纳",
            !AiPurposeCriteria.IsAccepted(AiPurposeKind.AppCache, cache - 0.01));
        check("清理类到阈值即采纳",
            AiPurposeCriteria.IsAccepted(AiPurposeKind.AppCache, cache));
        check("keep 低于阈值不采纳",
            !AiPurposeCriteria.IsAccepted(AiPurposeKind.Keep, keep - 0.01));
        // 实测里低置信的那批（0.22~0.46）必须全部被挡掉
        foreach (var c in new[] { 0.22, 0.24, 0.28, 0.36, 0.40, 0.42, 0.43, 0.46 })
        {
            if (AiPurposeCriteria.IsAccepted(AiPurposeKind.AppCache, c)
                || AiPurposeCriteria.IsAccepted(AiPurposeKind.Keep, c))
            {
                check($"实测低置信样本 0.{c} 必须被挡掉", false);
                return;
            }
        }
        check("实测低置信样本（0.22~0.46）全部被挡掉", true);

        // Unknown 永远不采纳（它本来就没结论）
        check("Unknown 永远不采纳", !AiPurposeCriteria.IsAccepted(AiPurposeKind.Unknown, 1.0));
    }

    // -------------------------------------------------- 批选资格闸（重点）

    static CleanItem Item(CleanPurpose purpose, EvidenceLevel evidence, CleanRisk risk = CleanRisk.Safe, bool canDelete = true)
        => new()
        {
            Name = "x",
            FullPath = @"C:\x\y",
            Size = 1024,
            Purpose = purpose,
            Evidence = evidence,
            Risk = risk,
            CanDelete = canDelete,
        };

    static void BatchEligibilityGuardTests(CheckFn check)
    {
        // ① 危险组合：签名级证据 + 用途没认出来。
        //    这条本来**不可**批选（用途是 Other），把用途改成缓存类就**会**变成可批选。
        var dangerous = Item(CleanPurpose.Other, EvidenceLevel.Signature);
        check("危险组合：改用途前确实不可批选",
            !CleanRuleEligibility.IsRuleClear(dangerous));
        check("危险组合：闸必须拦住（改完会凭空多出批选资格）",
            CleanRuleEligibility.GainsBatchEligibility(dangerous, CleanPurpose.AppCache));

        // ② 正常情况：启发式证据 + 用途没认出来。改了也够不到签名级，放行。
        var normal = Item(CleanPurpose.Other, EvidenceLevel.Heuristic);
        check("启发式证据：闸放行（改了也不会变可批选）",
            !CleanRuleEligibility.GainsBatchEligibility(normal, CleanPurpose.AppCache));

        // ③ 本来就够格的：不算「凭空造出」，闸不该报
        var already = Item(CleanPurpose.Temp, EvidenceLevel.Signature);
        check("本来就够格的：闸不报（不是凭空造出）",
            !CleanRuleEligibility.GainsBatchEligibility(already, CleanPurpose.AppCache));

        // ④ 改成「不可批选的用途」永远不会造出资格
        check("改成不可批选用途：闸不报",
            !CleanRuleEligibility.GainsBatchEligibility(normal, CleanPurpose.Large));

        // ⑤ 风险不是 Safe、或不可删的候选，怎么改都够不到
        check("风险为 Confirm：闸不报",
            !CleanRuleEligibility.GainsBatchEligibility(Item(CleanPurpose.Other, EvidenceLevel.Signature, CleanRisk.Confirm), CleanPurpose.AppCache));
        check("不可删：闸不报",
            !CleanRuleEligibility.GainsBatchEligibility(Item(CleanPurpose.Other, EvidenceLevel.Signature, canDelete: false), CleanPurpose.AppCache));

        // ⑥ 闸必须是纯函数：问一次不能改变候选
        var probe = Item(CleanPurpose.Other, EvidenceLevel.Signature);
        CleanRuleEligibility.GainsBatchEligibility(probe, CleanPurpose.AppCache);
        check("闸是纯函数：问过之后用途没被改动",
            probe.Purpose == CleanPurpose.Other && !CleanRuleEligibility.IsRuleClear(probe));
    }

    // ---------------------------------------------------- 源码级不变量

    static void SourceInvariantTests(CheckFn check)
    {
        string src = ReadSource("src/AiDiskCleaner/Services/AiPurposeBatchService.cs");
        check("读到批量归类服务源码", src.Length > 0);
        if (src.Length == 0) return;

        // 只读规则产出的字段，不许自己重新判文件、更不许碰任何判定
        check("批量归类不写 Risk", !src.Contains(".Risk ="));
        check("批量归类不写 CanDelete", !src.Contains(".CanDelete ="));
        check("批量归类不写 Selected", !src.Contains(".Selected ="));
        check("批量归类不写 Evidence", !src.Contains(".Evidence ="));
        check("批量归类走共用闸 GainsBatchEligibility",
            src.Contains("GainsBatchEligibility", StringComparison.Ordinal));

        // 出站路径必须走唯一入口：默认脱敏，只有用户明确允许才发完整路径。
        // 实测脱敏后归类不受影响（16/16 与非脱敏一致）—— <UserProfile>\Temp\ 照样认出是临时文件。
        check("出站路径走 PathRedactor.Outbound（默认脱敏）",
            src.Contains("PathRedactor.Outbound", StringComparison.Ordinal));
        check("批量归类不直接发 FullPath 给模型",
            !src.Contains("Loc.AiPurposeQuestion(chunk[i].FullPath", StringComparison.Ordinal)
            && !src.Contains("chunk.Select(x => x.FullPath)", StringComparison.Ordinal));

        // 路径必须写进问题里：行号引用在 48 项时就 8/16 自相矛盾（实测）
        string loc = ReadSource("src/AiDiskCleaner/Services/Loc.cs");
        check("问题里带路径（不让模型数行号）",
            loc.Contains("AiPurposeQuestion"));
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
