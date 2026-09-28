using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace SafetyCheck;

/// <summary>
/// 判定层（<see cref="DecisionRunner"/> / <see cref="JevLimits"/> / <see cref="IDecisionSet{TItem}"/>）
/// 的针对性回归。**全部离线**：出站口是一个假的 DecisionsSender，不联网、不碰真实文件。
///
/// 这一批守的是「第二批判定域不用重写这些规则」这件事真的成立：
/// <list type="number">
/// <item><b>硬限制在发出去之前挡住。</b>选项数、档位数、字符预算违反时抛明确异常，
///   而不是让端点回一个 400、在界面上冒充成「网断了」。</item>
/// <item><b>自适应步长与拆半。</b>先小批试、成功翻倍、撞上下文上限拆两半 —— 总花费不变。</item>
/// <item><b>换代即停写。</b>等待期间换了扫描，reply 回来也不许写。</item>
/// <item><b>没结论也记「问过了」。</b>否则同一批会被反复追问（无限请求）。</item>
/// <item><b>执行器自己不碰任何清理字段。</b>它是泛型的，只该调 MarkAsked / Apply。</item>
/// </list>
/// </summary>
public static class JevDecisionTests
{
    public delegate void CheckFn(string name, bool ok, string? detail = null);

    // ------------------------------------------------------------ 测试替身

    sealed class Item
    {
        public string Key = "";
        public string Path = "";
        public int Asked;
    }

    sealed class Set : IDecisionSet<Item>
    {
        public string Name => "fake";
        public int MaxPerRequest { get; init; } = 8;
        public int AdaptiveStart { get; init; } = 2;

        /// <summary>这一批实际发出去时每条占多少（用来断言分批与拆半）。</summary>
        public readonly List<int> ChunkSizes = new();

        /// <summary>每条的答案怎么造；返回 null 表示这条没有答案。</summary>
        public Func<Item, AiDecisionAnswer?> AnswerFor { get; set; } = _ => Applied();

        /// <summary>回写行为；默认按 AnswerFor 造出来的答案走。</summary>
        public Func<Item, AiDecisionAnswer?, DecisionApplied>? ApplyFn { get; set; }

        /// <summary>KeysFor 是否故意少给一个名字（用于断言对齐检查）。</summary>
        public bool DropLastKey { get; set; }

        /// <summary>KeysFor 是否给重复名字（用于断言重名检查）。</summary>
        public bool DuplicateKeys { get; set; }

        public string ProgressText(int done, int total) => $"{done}/{total}";

        public void MarkAsked(Item item) => item.Asked++;

        public IReadOnlyList<string> KeysFor(IReadOnlyList<Item> chunk)
        {
            ChunkSizes.Add(chunk.Count);
            var keys = new List<string>();
            foreach (var item in chunk) keys.Add(DuplicateKeys ? "same" : item.Key);
            if (DropLastKey && keys.Count > 0) keys.RemoveAt(keys.Count - 1);
            return keys;
        }

        public AiDecisionQuestion QuestionFor(int index, Item item) => new()
        {
            Type = "choice",
            Instructions = "这是啥：" + item.Path,
            Criteria = new Dictionary<string, string> { ["a"] = "甲", ["b"] = "乙" },
        };

        public string StateFor(IReadOnlyList<Item> chunk)
            => string.Join("\n", chunk.Select(x => x.Path));

        public DecisionApplied Apply(Item item, AiDecisionAnswer? answer)
            => ApplyFn != null ? ApplyFn(item, answer) : DefaultApply(item, answer);

        DecisionApplied DefaultApply(Item item, AiDecisionAnswer? answer)
        {
            if (answer == null) return DecisionApplied.NoAnswer;
            return new DecisionApplied(DecisionWriteBack.Applied, answer.Choice ?? "", answer.Confidence);
        }
    }

    static AiDecisionAnswer Applied(string choice = "a", double confidence = 0.9)
        => new() { Type = "choice", Choice = choice, Confidence = confidence };

    static List<Item> Items(int count, string prefix = "p")
    {
        var list = new List<Item>();
        for (int i = 0; i < count; i++) list.Add(new Item { Key = prefix + (i + 1), Path = @"D:\x\" + prefix + (i + 1) });
        return list;
    }

    static AiProviderCfg Provider() => new() { Id = "t", Name = "t", BaseUrl = "http://localhost/decisions" };

    /// <summary>装一个假的出站口，跑完自动还原 —— 绝不联网。</summary>
    static async Task<T> WithSender<T>(
        Func<AiDecisionRequest, AiDecisionReply> respond,
        Func<List<AiDecisionRequest>, Task<T>> body)
    {
        var saved = AiGateway.DecisionsSender;
        var seen = new List<AiDecisionRequest>();
        AiGateway.DecisionsSender = (req, _) =>
        {
            seen.Add(req);
            return Task.FromResult(respond(req));
        };
        try
        {
            return await body(seen);
        }
        finally
        {
            AiGateway.DecisionsSender = saved;
        }
    }

    static AiDecisionReply AnswerAll(AiDecisionRequest req, Func<string, AiDecisionAnswer?> make)
    {
        var reply = new AiDecisionReply { Model = "fake-jev", InputTokens = 12, Cost = 0.0001 };
        foreach (var key in req.Questions.Keys)
        {
            var answer = make(key);
            if (answer != null) reply.Answers[key] = answer;
        }
        return reply;
    }

    // ------------------------------------------------------------ 入口

    public static void Run(CheckFn check, Action<string> section)
    {
        LimitTests(check, section);
        BatchingTests(check, section);
        GuardTests(check, section);
        AccountingTests(check, section);
        PurityTests(check, section);
    }

    // ------------------------------------------------------------ 硬限制

    static void LimitTests(CheckFn check, Action<string> section)
    {
        section("判定层：硬限制在发出去之前挡住");

        // choice 选项数：上限 255，超了要两段式，不能硬发
        var many = new Dictionary<string, string>();
        for (int i = 0; i < JevLimits.MaxChoiceOptions + 1; i++) many["k" + i] = "选项" + i;
        check("choice 超过 255 个选项会被挡住",
            Throws(() => JevLimits.Validate(Choice(many))) is DecisionLimitException);

        many.Remove("k" + JevLimits.MaxChoiceOptions);   // 正好 255 个
        check("正好 255 个选项放行", Throws(() => JevLimits.Validate(Choice(many))) == null);

        // score 档位：2~10
        check("score 只有 1 档会被挡住",
            Throws(() => JevLimits.Validate(new AiDecisionRequest
            {
                Questions = { ["q"] = new AiDecisionQuestion { Type = "score", Instructions = "打分", Scale = new List<string> { "只有一档" } } },
            })) is DecisionLimitException);

        var eleven = new List<string>();
        for (int i = 0; i < 11; i++) eleven.Add("第" + i + "档");
        check("score 超过 10 档会被挡住",
            Throws(() => JevLimits.Validate(new AiDecisionRequest
            {
                Questions = { ["q"] = new AiDecisionQuestion { Type = "score", Instructions = "打分", Scale = eleven } },
            })) is DecisionLimitException);

        check("score 两档放行",
            Throws(() => JevLimits.Validate(new AiDecisionRequest
            {
                Questions = { ["q"] = new AiDecisionQuestion { Type = "score", Instructions = "打分", Scale = new List<string> { "低", "高" } } },
            })) == null);

        // 三种原语之外没有第四种
        check("未知题型会被挡住",
            Throws(() => JevLimits.Validate(new AiDecisionRequest
            {
                Questions = { ["q"] = new AiDecisionQuestion { Type = "ranking", Instructions = "排序" } },
            })) is DecisionLimitException);

        check("没有 instructions 会被挡住",
            Throws(() => JevLimits.Validate(new AiDecisionRequest
            {
                Questions = { ["q"] = new AiDecisionQuestion { Type = "noul", Instructions = "  " } },
            })) is DecisionLimitException);

        check("noul 不需要 criteria 就能过",
            Throws(() => JevLimits.Validate(new AiDecisionRequest
            {
                Questions = { ["q"] = new AiDecisionQuestion { Type = "noul", Instructions = "是垃圾吗" } },
            })) == null);

        check("一个题都没有会被挡住",
            Throws(() => JevLimits.Validate(new AiDecisionRequest())) is DecisionLimitException);

        // 字符预算：临时调小，验证它真的会挡住，再还原
        int saved = JevLimits.MaxRequestChars;
        try
        {
            JevLimits.MaxRequestChars = 40;
            var tooBig = Throws(() => JevLimits.Validate(Choice(new Dictionary<string, string> { ["a"] = "甲", ["b"] = "乙" }, state: new string('x', 200))));
            check("state 超过字符预算会被挡住（≈64K token 的保守余量）", tooBig is DecisionLimitException);
            check("超预算的消息里带了实际用量，而不是只说「失败」",
                tooBig is DecisionLimitException limit && limit.Message.Contains("字符", StringComparison.Ordinal));
        }
        finally { JevLimits.MaxRequestChars = saved; }

        check("限制没被触发时，校验不改动请求",
            Throws(() => JevLimits.Validate(Choice(new Dictionary<string, string> { ["a"] = "甲" }))) == null);
    }

    static AiDecisionRequest Choice(Dictionary<string, string> criteria, string state = "s")
        => new()
        {
            State = state,
            Questions = { ["q"] = new AiDecisionQuestion { Type = "choice", Instructions = "这是啥", Criteria = criteria } },
        };

    // ------------------------------------------------------------ 分批

    static void BatchingTests(CheckFn check, Action<string> section)
    {
        section("判定层：自适应分批与拆半");

        // 步长：起点 2、上限 8，5 条 → 2 + 3
        WithSender(
            req => AnswerAll(req, _ => Applied()),
            async seen =>
            {
                var set = new Set { AdaptiveStart = 2, MaxPerRequest = 8 };
                var outcome = await DecisionRunner.RunAsync(set, Items(5), Provider(), "m", null, null, default);
                check("步长从起点开始，成功后翻倍（2 条 → 3 条）",
                    string.Join(",", set.ChunkSizes) == "2,3", string.Join(",", set.ChunkSizes));
                check("两次请求问完 5 条", outcome.Calls == 2, "calls=" + outcome.Calls);
                return true;
            }).GetAwaiter().GetResult();

        // 步长：起点 1、上限 4，10 条 → 1 + 2 + 4 + 3（翻倍但不超过上限）
        WithSender(
            req => AnswerAll(req, _ => Applied()),
            async seen =>
            {
                var set = new Set { AdaptiveStart = 1, MaxPerRequest = 4 };
                await DecisionRunner.RunAsync(set, Items(10), Provider(), "m", null, null, default);
                check("步长翻倍但不超过单批上限（1,2,4,3）",
                    string.Join(",", set.ChunkSizes) == "1,2,4,3", string.Join(",", set.ChunkSizes));
                return true;
            }).GetAwaiter().GetResult();

        // 撑爆上下文：命中上下文类报错 → 拆两半，而不是整批失败
        WithSender(
            req => req.Questions.Count > 2
                ? throw new InvalidOperationException("maximum context length exceeded")
                : AnswerAll(req, _ => Applied()),
            async seen =>
            {
                var set = new Set { AdaptiveStart = 4, MaxPerRequest = 4 };
                var items = Items(4);
                var outcome = await DecisionRunner.RunAsync(set, items, Provider(), "m", null, null, default);

                check("整批撑爆后自动拆两半（4 → 2 + 2）",
                    string.Join(",", set.ChunkSizes) == "4,2,2", string.Join(",", set.ChunkSizes));
                check("第一次失败不计入 calls，但确实发出去了（出站计数含失败那次）",
                    outcome.Calls == 2 && seen.Count == 3,
                    $"calls={outcome.Calls} sent={seen.Count}");
                check("拆半后四条全都拿到了结论（不是整批丢）", outcome.Applied == 4, "applied=" + outcome.Applied);
                return true;
            }).GetAwaiter().GetResult();

        // 只有一条时不能再拆（拆到 0 会死循环）
        WithSender(
            req => throw new InvalidOperationException("context length exceeded"),
            async seen =>
            {
                var set = new Set { AdaptiveStart = 1, MaxPerRequest = 1 };
                try
                {
                    await DecisionRunner.RunAsync(set, Items(1), Provider(), "m", null, null, default);
                    check("单条撑爆时不再拆（拆到 0 会死循环），如实失败", false, "没有抛异常");
                }
                catch (InvalidOperationException)
                {
                    check("单条撑爆时不再拆（拆到 0 会死循环），如实失败", seen.Count == 1, "sent=" + seen.Count);
                }
                return true;
            }).GetAwaiter().GetResult();

        check("上下文类报错的四种措辞都能认出来",
            DecisionRunner.IsTooLargeForModel(new Exception("max tokens exceeded"))
            && DecisionRunner.IsTooLargeForModel(new Exception("The context length is too long"))
            && DecisionRunner.IsTooLargeForModel(new Exception("too many tokens"))
            && DecisionRunner.IsTooLargeForModel(new Exception("maximum context reached")));
        check("普通网络错误不会被误判成「批太大」",
            !DecisionRunner.IsTooLargeForModel(new HttpRequestException("connection refused"))
            && !DecisionRunner.IsTooLargeForModel(new TimeoutException("timed out")));
    }

    // ------------------------------------------------------------ 守卫

    static void GuardTests(CheckFn check, Action<string> section)
    {
        section("判定层：换代即停写 / 取消 / 本地拒绝");

        // 一开始就换代：一次请求都不该发
        AiGateway.ResetSentCountForTest();
        WithSender(
            req => AnswerAll(req, _ => Applied()),
            async seen =>
            {
                var set = new Set();
                var items = Items(4);
                var outcome = await DecisionRunner.RunAsync(set, items, Provider(), "m", () => false, null, default);
                check("换代的页面一次请求都不发", seen.Count == 0 && AiGateway.SentCount == 0,
                    $"sent={seen.Count} counter={AiGateway.SentCount}");
                check("一条都不问，也不标「问过了」（下次翻回来还能问）",
                    outcome.Applied == 0 && items.All(x => x.Asked == 0));
                return true;
            }).GetAwaiter().GetResult();

        // 请求期间换代：reply 回来也不许写，且不再发下一批
        AiGateway.ResetSentCountForTest();
        WithSender(
            req => AnswerAll(req, _ => Applied()),
            async seen =>
            {
                bool current = true;
                int checks = 0;
                var set = new Set { AdaptiveStart = 1, MaxPerRequest = 1 };
                var items = Items(3);
                var outcome = await DecisionRunner.RunAsync(
                    set, items, Provider(), "m",
                    // 发请求前会被问两次（外层循环一次、AskAsync 自己一次 —— 这是故意的双保险），
                    // 回复到手时是第三次：那次必须说「已经换代了」。
                    () => ++checks <= 2,
                    null, default);
                check("请求期间换代：那一批的结果不写进新扫描", outcome.Applied == 0, "applied=" + outcome.Applied);
                check("请求期间换代：不再发下一批", seen.Count == 1, "sent=" + seen.Count);
                return true;
            }).GetAwaiter().GetResult();

        // 取消
        var cts = new CancellationTokenSource();
        cts.Cancel();
        AiGateway.ResetSentCountForTest();
        WithSender(
            req => AnswerAll(req, _ => Applied()),
            async seen =>
            {
                try
                {
                    await DecisionRunner.RunAsync(new Set(), Items(3), Provider(), "m", null, null, cts.Token);
                    check("已取消的调用直接抛出、不发请求", false, "没有抛异常");
                }
                catch (OperationCanceledException)
                {
                    check("已取消的调用直接抛出、不发请求", seen.Count == 0, "sent=" + seen.Count);
                }
                return true;
            }).GetAwaiter().GetResult();

        // 本地就超限：异常冒到调用方，且**不发出请求**
        AiGateway.ResetSentCountForTest();
        int saved = JevLimits.MaxRequestChars;
        try
        {
            JevLimits.MaxRequestChars = 1;
            WithSender(
                req => AnswerAll(req, _ => Applied()),
                async seen =>
                {
                    try
                    {
                        await DecisionRunner.RunAsync(new Set { AdaptiveStart = 8, MaxPerRequest = 8 }, Items(2), Provider(), "m", null, null, default);
                        check("本地超限时抛异常且不发请求", false, "没有抛异常");
                    }
                    catch (DecisionLimitException)
                    {
                        check("本地超限时抛异常且不发请求（不冒充网络失败）",
                            seen.Count == 0 && AiGateway.SentCount == 0,
                            $"sent={seen.Count} counter={AiGateway.SentCount}");
                    }
                    return true;
                }).GetAwaiter().GetResult();
        }
        finally { JevLimits.MaxRequestChars = saved; }

        // 空集合
        WithSender(
            req => AnswerAll(req, _ => Applied()),
            async seen =>
            {
                var outcome = await DecisionRunner.RunAsync(new Set(), new List<Item>(), Provider(), "m", null, null, default);
                check("没有条目时不发请求，也不写任何东西",
                    seen.Count == 0 && outcome.Asked == 0 && outcome.Calls == 0);
                return true;
            }).GetAwaiter().GetResult();
    }

    // ------------------------------------------------------------ 记账

    static void AccountingTests(CheckFn check, Action<string> section)
    {
        section("判定层：回写口径与遥测记账");

        WithSender(
            req => AnswerAll(req, key => key switch
            {
                "p1" => Applied("a", 0.95),                       // 采纳
                "p2" => Applied("b", 0.2),                        // 有答案但没把握
                "p3" => null,                                     // 模型没说这条
                _ => Applied("a", 0.9),
            }),
            async seen =>
            {
                var set = new Set { AdaptiveStart = 8, MaxPerRequest = 8 };
                set.ApplyFn = (item, answer) => answer == null
                    ? DecisionApplied.NoAnswer
                    : answer.Confidence < 0.5
                        ? new DecisionApplied(DecisionWriteBack.Unsure, "low", answer.Confidence)
                        : new DecisionApplied(DecisionWriteBack.Applied, "ok", answer.Confidence);

                var items = Items(4);
                var outcome = await DecisionRunner.RunAsync(set, items, Provider(), "m", null, null, default);

                check("采纳 / 拿不准 / 说不出分别记账",
                    outcome.Applied == 2 && outcome.Unsure == 1 && outcome.Unknown == 1,
                    $"applied={outcome.Applied} unsure={outcome.Unsure} unknown={outcome.Unknown}");
                check("跳过单独记一档（不算进说不出）", outcome.Skipped == 0, "skipped=" + outcome.Skipped);
                check("**每一条都标了「问过了」**，包括没有答案的那条",
                    items.All(x => x.Asked == 1), string.Join(",", items.Select(x => x.Asked)));
                return true;
            }).GetAwaiter().GetResult();

        // 跳过：整批都 Skip
        WithSender(
            req => AnswerAll(req, _ => Applied()),
            async seen =>
            {
                var set = new Set { AdaptiveStart = 8, MaxPerRequest = 8 };
                set.ApplyFn = (_, _) => DecisionApplied.Skipped;
                var outcome = await DecisionRunner.RunAsync(set, Items(3), Provider(), "m", null, null, default);
                check("整批跳过时只记 Skipped，不冒充成功",
                    outcome.Skipped == 3 && outcome.Applied == 0);
                return true;
            }).GetAwaiter().GetResult();

        // 问题名与条目数不对齐 → 明确报错，而不是错位回写
        WithSender(
            req => AnswerAll(req, _ => Applied()),
            async seen =>
            {
                var set = new Set { AdaptiveStart = 8, MaxPerRequest = 8, DropLastKey = true };
                try
                {
                    await DecisionRunner.RunAsync(set, Items(3), Provider(), "m", null, null, default);
                    check("问题名数量与条目数不一致时报错（防错位回写）", false, "没有抛异常");
                }
                catch (InvalidOperationException ex)
                {
                    check("问题名数量与条目数不一致时报错（防错位回写）",
                        ex.Message.Contains("KeysFor", StringComparison.Ordinal), ex.Message);
                }
                return true;
            }).GetAwaiter().GetResult();

        // 重名 → 明确报错（重名会静默覆盖掉一条答案）
        WithSender(
            req => AnswerAll(req, _ => Applied()),
            async seen =>
            {
                var set = new Set { AdaptiveStart = 8, MaxPerRequest = 8, DuplicateKeys = true };
                try
                {
                    await DecisionRunner.RunAsync(set, Items(3), Provider(), "m", null, null, default);
                    check("问题名重复时报错（否则会静默丢掉一条答案）", false, "没有抛异常");
                }
                catch (InvalidOperationException ex)
                {
                    check("问题名重复时报错（否则会静默丢掉一条答案）",
                        ex.Message.Contains("重复", StringComparison.Ordinal), ex.Message);
                }
                return true;
            }).GetAwaiter().GetResult();

        // 请求体：一条 entry 一道题，state 与题目一一对应
        WithSender(
            req => AnswerAll(req, _ => Applied()),
            async seen =>
            {
                var set = new Set { AdaptiveStart = 8, MaxPerRequest = 8 };
                await DecisionRunner.RunAsync(set, Items(3), Provider(), "m", null, null, default);
                var req = seen[0];
                check("一次请求 = N 道题（不是一道题问 N 条）", req.Questions.Count == 3, "questions=" + req.Questions.Count);
                check("问题名按顺序给出（p1..pN）",
                    req.Questions.ContainsKey("p1") && req.Questions.ContainsKey("p2") && req.Questions.ContainsKey("p3"),
                    string.Join(",", req.Questions.Keys));
                check("provider / model 原样带给通道", req.Model == "m" && req.Provider?.Id == "t");
                return true;
            }).GetAwaiter().GetResult();
    }

    // ------------------------------------------------------------ 执行器纯度

    static void PurityTests(CheckFn check, Action<string> section)
    {
        section("判定层：执行器自己不碰清理字段");

        string src = ReadSource("src/AiDiskCleaner/Services/Decisions/DecisionRunner.cs");
        check("读到执行器源码", src.Length > 0);
        if (src.Length == 0) return;

        check("执行器不写 Risk", !src.Contains(".Risk =", StringComparison.Ordinal));
        check("执行器不写 CanDelete", !src.Contains(".CanDelete =", StringComparison.Ordinal));
        check("执行器不写 Selected", !src.Contains(".Selected =", StringComparison.Ordinal));
        check("执行器不写 Evidence", !src.Contains(".Evidence =", StringComparison.Ordinal));
        check("执行器不落盘（接线在界面层）",
            !src.Contains("RecognitionStore", StringComparison.Ordinal)
            && !src.Contains("PutAiPurpose", StringComparison.Ordinal));
        check("执行器只通过判定域回写（MarkAsked / Apply）",
            src.Contains("set.MarkAsked(", StringComparison.Ordinal)
            && src.Contains("set.Apply(", StringComparison.Ordinal));

        string limits = ReadSource("src/AiDiskCleaner/Services/Decisions/JevLimits.cs");
        check("硬限制表里写着 255 与 2~10（端点契约，不许漂）",
            limits.Contains("MaxChoiceOptions = 255", StringComparison.Ordinal)
            && limits.Contains("MinScoreBands = 2", StringComparison.Ordinal)
            && limits.Contains("MaxScoreBands = 10", StringComparison.Ordinal));

        string thresholds = ReadSource("src/AiDiskCleaner/Services/Decisions/DecisionThresholds.cs");
        check("阈值只有一处出处，且清理类 0.60 / 别删类 0",
            thresholds.Contains("CleanKind = 0.60", StringComparison.Ordinal)
            && thresholds.Contains("KeepKind = 0.0", StringComparison.Ordinal));

        string criteria = ReadSource("src/AiDiskCleaner/Services/CleanRules/AiPurposeCriteria.cs");
        check("选项表的阈值改成引用唯一出处，不再散落字面量",
            criteria.Contains("DecisionThresholds.CleanKind", StringComparison.Ordinal)
            && !criteria.Contains("=> 0.60", StringComparison.Ordinal));
    }

    static Exception? Throws(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    static string ReadSource(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.targets"))) dir = dir.Parent;
        if (dir == null) return "";
        string full = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? File.ReadAllText(full) : "";
    }
}
