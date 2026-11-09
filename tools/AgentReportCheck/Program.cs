using System.Globalization;
using System.Text.Json;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.Agent;

namespace AgentReportCheck;

internal static class Program
{
    private const long MB = 1024L * 1024;
    private const long GB = 1024L * MB;

    private static int _pass;
    private static int _fail;
    private static readonly List<string> Failures = new();

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PromptTests();
        ParserTests();
        AssemblerTests();
        ServiceTests();

        Console.WriteLine();
        Console.WriteLine($"PASS {_pass}   FAIL {_fail}");
        foreach (var failure in Failures) Console.WriteLine("  FAILED: " + failure);
        return _fail == 0 ? 0 : 1;
    }

    // ───────────────────────── 提示词 ─────────────────────────

    private static void PromptTests()
    {
        Section("Prompt");
        var f = Fixture.Create(ScannedAt);

        var cards = new List<EvidenceCard>
        {
            EvidenceCardBuilder.Build(f.Session, f.Index, f.Temp),
            EvidenceCardBuilder.Build(f.Session, f.Index, f.Games),
            EvidenceCardBuilder.Build(f.Session, f.Index, f.Data),
        };

        string user = PromptBuilder.BuildUserPrompt(f.Session, cards, 1, 2);
        Check("提示词带 scanId", user.Contains(f.Session.ScanId, StringComparison.Ordinal));
        Check("提示词带 folderId", user.Contains("folderId", StringComparison.Ordinal));
        Check("提示词不泄露用户名", !user.Contains("Alice", StringComparison.Ordinal));
        Check("提示词卡片数量正确", CountOccurrences(user, "\"folderId\"") == 3);
        Check("系统提示词要求引用证据", PromptBuilder.SystemPrompt.Contains("evidenceRefs", StringComparison.Ordinal)
            && PromptBuilder.SystemPrompt.Contains("folderId", StringComparison.Ordinal));
        Check("提示词版本固定", PromptBuilder.Version == "cleanup-report-v1");
    }

    // ───────────────────────── 解析与校验 ─────────────────────────

    private static void ParserTests()
    {
        Section("Parser");
        var f = Fixture.Create(ScannedAt);
        string scanId = f.Session.ScanId;

        var cardA = Card("f_aaaaaaaaaaaa", @"Users\%USER%\Temp", 2 * GB, 38, rules: new[] { new CardRuleRef("temp-files", 0.9) });
        var cardB = Card("f_bbbbbbbbbbbb", @"Games", 800 * MB, 60);
        var cards = new[] { cardA, cardB };

        var valid = ReportParser.Parse(Output(scanId,
            Item(cardA.FolderId, reclaimable: 1 * GB, refs: new[] { "card:" + cardA.FolderId, "age:38d", "size:2.0GB" }),
            Item(cardB.FolderId, verdict: "keep", refs: new[] { "card:" + cardB.FolderId })), f.Session, cards);
        Check("合法输出解析成功", valid.FatalError == null && valid.Items.Count == 2 && valid.Dropped == 0);
        Check("证据回填可读标签", valid.Items[0].Evidence.Count == 3
            && valid.Items[0].Evidence[0].Label.StartsWith("证据卡", StringComparison.Ordinal));
        Check("verdict/action 映射", valid.Items[0].Action == "select" && valid.Items[1].Action == "keep");

        var fenced = ReportParser.Parse("```json\n" + Output(scanId, Item(cardA.FolderId, refs: new[] { "card:" + cardA.FolderId })) + "\n```", f.Session, cards);
        Check("剥掉 markdown 围栏", fenced.FatalError == null && fenced.Items.Count == 1);

        var unknownFolder = ReportParser.Parse(Output(scanId, Item("f_cccccccccccc", refs: new[] { "card:f_cccccccccccc" })), f.Session, cards);
        Check("未知 folderId 丢弃", unknownFolder.Items.Count == 0 && unknownFolder.Dropped == 1);

        var noSelf = ReportParser.Parse(Output(scanId, Item(cardA.FolderId, refs: new[] { "age:38d" })), f.Session, cards);
        Check("缺少自己的 card 引用丢弃", noSelf.Items.Count == 0 && noSelf.Dropped == 1);

        var badAge = ReportParser.Parse(Output(scanId, Item(cardA.FolderId, refs: new[] { "card:" + cardA.FolderId, "age:10d" })), f.Session, cards);
        Check("年龄对不上丢弃", badAge.Items.Count == 0 && badAge.Dropped == 1);

        var badSize = ReportParser.Parse(Output(scanId, Item(cardA.FolderId, refs: new[] { "card:" + cardA.FolderId, "size:900MB" })), f.Session, cards);
        Check("大小对不上丢弃", badSize.Items.Count == 0 && badSize.Dropped == 1);

        var goodRule = ReportParser.Parse(Output(scanId, Item(cardA.FolderId, refs: new[] { "card:" + cardA.FolderId, "rule:temp-files" })), f.Session, cards);
        Check("规则引用命中卡片", goodRule.Items.Count == 1 && goodRule.Items[0].Evidence.Any(e => e.Target == "rule"));

        var badRule = ReportParser.Parse(Output(scanId, Item(cardA.FolderId, refs: new[] { "card:" + cardA.FolderId, "rule:not-exist" })), f.Session, cards);
        Check("规则引用不存在丢弃", badRule.Items.Count == 0);

        var appCard = Card("f_dddddddddddd", @"Users\%USER%\AppData\Chrome", 300 * MB, 20, product: "Chrome", appKey: "chrome");
        var goodApp = ReportParser.Parse(Output(scanId, Item(appCard.FolderId, refs: new[] { "card:" + appCard.FolderId, "app:chrome" })), f.Session, new[] { appCard });
        Check("app 引用命中产品提示", goodApp.Items.Count == 1);

        var lockCard = Card("f_eeeeeeeeeeee", @"Logs", 100 * MB, 5);
        var goodLock = ReportParser.Parse(Output(scanId, Item(lockCard.FolderId, refs: new[] { "card:" + lockCard.FolderId, "lock:none" })), f.Session, new[] { lockCard });
        Check("lock:none 可接受", goodLock.Items.Count == 1);
        var badLock = ReportParser.Parse(Output(scanId, Item(lockCard.FolderId, refs: new[] { "card:" + lockCard.FolderId, "lock:locked" })), f.Session, new[] { lockCard });
        Check("lock 非 none 丢弃", badLock.Items.Count == 0);

        var dupRef = ReportParser.Parse(Output(scanId, Item(cardA.FolderId, refs: new[] { "card:" + cardA.FolderId, "dup:g_1" })), f.Session, cards);
        Check("P1 不接受 dup 引用", dupRef.Items.Count == 0);

        var clamp = ReportParser.Parse(Output(scanId, Item(cardA.FolderId, confidence: 1.7, reclaimable: 99 * GB, refs: new[] { "card:" + cardA.FolderId })), f.Session, cards);
        Check("reclaimable 不超过卡片", clamp.Items.Count == 1 && clamp.Items[0].ReclaimableBytes == cardA.SizeBytes);
        Check("confidence 夹到 [0,1]", Math.Abs(clamp.Items[0].Confidence - 1) < 0.0001);

        string longWhy = new('长', 120);
        var truncatedWhy = ReportParser.Parse(Output(scanId, Item(cardA.FolderId, why: longWhy, refs: new[] { "card:" + cardA.FolderId })), f.Session, cards);
        Check("why 截断到 60 字", truncatedWhy.Items[0].Why.Length == 60);

        var wrongScan = ReportParser.Parse(Output("s_other", Item(cardA.FolderId, refs: new[] { "card:" + cardA.FolderId })), f.Session, cards);
        Check("scanId 不符整份拒绝", wrongScan.FatalError != null && wrongScan.Items.Count == 0);

        var wrongVersion = ReportParser.Parse(Output(scanId, Item(cardA.FolderId, refs: new[] { "card:" + cardA.FolderId })).Replace("\"reportVersion\":1", "\"reportVersion\":2", StringComparison.Ordinal), f.Session, cards);
        Check("reportVersion 不符整份拒绝", wrongVersion.FatalError != null);

        var notJson = ReportParser.Parse("我觉得可以清理。", f.Session, cards);
        Check("非 JSON 整份拒绝", notJson.FatalError != null);

        var many = Enumerable.Range(0, 41)
            .Select(_ => Item(cardA.FolderId, refs: new[] { "card:" + cardA.FolderId }))
            .ToArray();
        var capped = ReportParser.Parse(Output(scanId, many), f.Session, cards);
        Check("超过 40 条截断", capped.Items.Count == 40 && capped.Warnings.Any(w => w.Contains("截断", StringComparison.Ordinal)));

        var tooManyRefs = ReportParser.Parse(Output(scanId,
            Item(cardA.FolderId, refs: new[] { "card:" + cardA.FolderId, "card:" + cardA.FolderId, "card:" + cardA.FolderId, "card:" + cardA.FolderId, "card:" + cardA.FolderId, "card:" + cardA.FolderId, "age:38d" })),
            f.Session, cards);
        Check("evidenceRefs 超过 6 个截断", tooManyRefs.Items.Count == 1 && tooManyRefs.Items[0].Evidence.Count == 6);

        Check("大小单位解析 GB", ReportParser.ParseSize("3.0GB") is long g && Math.Abs(g - 3 * GB) < GB / 100);
        Check("大小单位解析 空格 MB", ReportParser.ParseSize("512 MB") == 512 * MB);
        Check("大小单位解析非法返回 null", ReportParser.ParseSize("很大") == null);
    }

    // ───────────────────────── 组装 ─────────────────────────

    private static void AssemblerTests()
    {
        Section("Assembler");
        var f = Fixture.Create(ScannedAt);

        var low = Item("f_111111111111", ReportVerdict.Recommend, 0.4, 100 * MB, @"X\low");
        var high = Item("f_111111111111", ReportVerdict.Recommend, 0.9, 200 * MB, @"X\high");
        var weak = Item("f_aaaaaaaaaaaa", ReportVerdict.Recommend, 0.4, 60 * MB, @"X\weak");
        var consider = Item("f_222222222222", ReportVerdict.Consider, 0.7, 50 * MB, @"Y\consider");
        var keep = Item("f_333333333333", ReportVerdict.Keep, 0.9, 10 * MB, @"Z\keep");

        var report = ReportAssembler.Build(f.Session, new[] { low, high, weak, consider, keep }, "", Array.Empty<ReportQuestion>(),
            "test-model", PromptBuilder.Version, ScannedAt, 0, Array.Empty<string>(), null);

        Check("同 folderId 保留高置信", report.AllItems.Count(i => i.FolderId == low.FolderId) == 1
            && report.AllItems.First(i => i.FolderId == low.FolderId).Confidence == 0.9);
        Check("低置信 recommend 降级为 consider", report.AllItems.First(i => i.FolderId == weak.FolderId).Verdict == ReportVerdict.Consider);
        Check("分组齐全", report.Groups.Any(g => g.Group == "推荐清理")
            && report.Groups.Any(g => g.Group == "再看看")
            && report.Groups.Any(g => g.Group == "建议保留"));
        Check("空 summary 有本地兜底", report.Summary.Contains("可回收", StringComparison.Ordinal));
        Check("action 映射", report.AllItems.First(i => i.Verdict == ReportVerdict.Keep).Action == "keep"
            && report.AllItems.First(i => i.Verdict == ReportVerdict.Consider).Action == "ask");

        var neat = new[]
        {
            Item("f_444444444444", ReportVerdict.Recommend, 0.9, 100 * MB, @"A\small"),
            Item("f_555555555555", ReportVerdict.Recommend, 0.9, 400 * MB, @"B\big"),
        };
        var sorted = ReportAssembler.Build(f.Session, neat, "", Array.Empty<ReportQuestion>(), "m", "v", ScannedAt, 0, Array.Empty<string>(), null);
        Check("组内按可回收大小排序", sorted.AllItems.First().ReclaimableBytes == 400 * MB);

        var tight = new[]
        {
            Item("f_666666666666", ReportVerdict.Recommend, 0.9, 3 * GB, @"C\big"),
            Item("f_777777777777", ReportVerdict.Recommend, 0.9, 3 * GB, @"D\big2"),
        };
        var tightSession = ScanSession.Create("C:", f.Root, f.Session.Quality, ScannedAt, f.Session.ScanId, 1 * MB);
        var clamped = ReportAssembler.Build(tightSession, tight, "", Array.Empty<ReportQuestion>(), "m", "v", ScannedAt, 0, Array.Empty<string>(), null);
        Check("总量不超过剩余空间", clamped.TotalReclaimableBytes <= tightSession.FreeBytes);
        Check("总量下调有标记", clamped.Degraded == "total_clamped"
            && clamped.Warnings.Any(w => w.Contains("按比例", StringComparison.Ordinal)));
        Check("reportId 稳定前缀", clamped.ReportId.StartsWith("r_" + f.Session.ScanId, StringComparison.Ordinal));
    }

    // ───────────────────────── 服务编排 ─────────────────────────

    private static void ServiceTests()
    {
        Section("Service");
        var f = Fixture.Create(ScannedAt);
        var options = new AgentReportOptions
        {
            CardsPerBatch = 5,
            MaxBatches = 4,
            MaxCandidates = 100,
            Now = ScannedAt,
        };

        var model = new ScriptedModel();
        var service = new AgentReportService(model, options);
        var result = service.AnalyzeAsync(f.Session).GetAwaiter().GetResult();
        Check("完整流程返回报告", result.Ok && result.Report != null);
        Check("调用批次数一致", result.Calls == result.Batches && result.Calls > 0);
        Check("报告带模型与版本", result.Report!.Model.Contains("scripted", StringComparison.Ordinal)
            && result.Report.PromptVersion == PromptBuilder.Version);
        Check("报告里有建议", result.Report.AllItems.Any());
        Check("报告统计大于 0", result.Report.TotalReclaimableBytes > 0);
        Check("每次调用都带用户提示词", model.Prompts.Count == result.Calls
            && model.Prompts.All(p => p.Contains("证据卡", StringComparison.Ordinal)));
        Check("服务层不泄露用户名", model.Prompts.All(p => !p.Contains("Alice", StringComparison.Ordinal)));

        var model2 = new ScriptedModel();
        var result2 = new AgentReportService(model2, options).AnalyzeAsync(f.Session).GetAwaiter().GetResult();
        string first = JsonSerializer.Serialize(result.Report);
        string second = JsonSerializer.Serialize(result2.Report);
        Check("同样输入产出同样报告", first == second);

        var authModel = new ScriptedModel { FailAll = true, FailFirstKind = ChatErrorKind.Auth };
        var authResult = new AgentReportService(authModel, options).AnalyzeAsync(f.Session).GetAwaiter().GetResult();
        Check("认证失败返回错误", !authResult.Ok && authResult.ErrorKind == ChatErrorKind.Auth);

        var partialModel = new ScriptedModel { FailFirst = true, FailFirstKind = ChatErrorKind.Network };
        var partial = new AgentReportService(partialModel, options).AnalyzeAsync(f.Session).GetAwaiter().GetResult();
        Check("单批失败仍能出报告", partial.Ok && partial.Warnings.Any(w => w.Contains("调用失败", StringComparison.Ordinal)));

        var budgetModel = new ScriptedModel { Usage = new ChatUsage(1000, 50) };
        var budget = new AgentReportService(budgetModel, new AgentReportOptions
        {
            CardsPerBatch = 3,
            MaxBatches = 4,
            MaxInputTokens = 100,
            Now = ScannedAt,
        }).AnalyzeAsync(f.Session).GetAwaiter().GetResult();
        Check("超预算停止并降级", budget.Ok && budget.Report!.Degraded == "budget");

        var small = Fixture.CreateSmall(ScannedAt);
        var empty = new AgentReportService(new ScriptedModel(), new AgentReportOptions { Now = ScannedAt })
            .AnalyzeAsync(small.Session).GetAwaiter().GetResult();
        Check("没有候选也能出空报告", empty.Ok && empty.Report != null
            && empty.Report.TotalReclaimableBytes == 0 && empty.Report.Groups.Count == 0);
        Check("空报告不调用模型", empty.Calls == 0);
    }

    // ───────────────────────── 脚手架 ─────────────────────────

    private static readonly DateTimeOffset ScannedAt = new(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(8));

    private static EvidenceCard Card(string id, string relPath, long size, int ageDays, string product = "", string? appKey = null, CardRuleRef[]? rules = null)
        => new(id, relPath, 2, size, size, 8, 2, 1, "2026-01-01T00:00:00+08:00", ageDays,
            Array.Empty<TypeMixEntry>(), new[] { "a.tmp" }, Array.Empty<string>(), product,
            new CardAttrs(false, false, false), rules ?? Array.Empty<CardRuleRef>(), false, "card-v1")
        {
            AppKey = appKey,
        };

    private static string Output(string scanId, params string[] items)
        => "{\"reportVersion\":1,\"scanId\":\"" + scanId + "\",\"summary\":\"可回收约 2GB\",\"items\":[" + string.Join(",", items) + "],\"questions\":[]}";

    private static string Item(
        string folderId,
        string verdict = "recommend",
        double confidence = 0.9,
        long reclaimable = 1000,
        string why = "缓存与旧文件堆积",
        string risk = "",
        string[]? refs = null)
        => "{\"folderId\":\"" + folderId + "\",\"verdict\":\"" + verdict + "\",\"confidence\":"
            + confidence.ToString(CultureInfo.InvariantCulture) + ",\"why\":\"" + why + "\",\"risk\":\"" + risk
            + "\",\"reclaimableBytes\":" + reclaimable.ToString(CultureInfo.InvariantCulture)
            + ",\"evidenceRefs\":[" + string.Join(",", (refs ?? Array.Empty<string>()).Select(r => "\"" + r + "\"")) + "]}";

    private static ReportItem Item(string folderId, ReportVerdict verdict, double confidence, long reclaimable, string relPath)
        => new()
        {
            FolderId = folderId,
            Title = relPath,
            RelPath = relPath,
            SizeBytes = reclaimable,
            ReclaimableBytes = reclaimable,
            Verdict = verdict,
            Confidence = confidence,
            Why = "原因",
            Evidence = new List<ReportEvidence> { new() { Ref = "card:" + folderId, Label = "证据卡", Target = "folder" } },
        };

    private static void Section(string name) => Console.WriteLine("== " + name);

    private static void Check(string name, bool ok)
    {
        if (ok)
        {
            _pass++;
            return;
        }

        _fail++;
        Failures.Add(name);
        Console.WriteLine("  FAIL " + name);
    }

    private static int CountOccurrences(string text, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    /// <summary>脚本化模型：从用户提示词里读出卡片，再为每张卡生成一条合法建议。</summary>
    private sealed class ScriptedModel : IChatModel
    {
        public string ModelId => "scripted-1";
        public string Describe() => "scripted endpoint · scripted-1";
        public int Calls { get; private set; }
        public List<string> Prompts { get; } = new();
        public ChatUsage Usage { get; init; } = new(1000, 50);
        public bool FailFirst { get; init; }
        public bool FailAll { get; init; }
        public ChatErrorKind FailFirstKind { get; init; } = ChatErrorKind.Network;

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct)
        {
            Calls++;
            string prompt = request.Messages[^1].Content;
            Prompts.Add(prompt);
            if (FailAll || (FailFirst && Calls == 1))
                throw new ChatModelException("模拟失败（" + FailFirstKind + "）", FailFirstKind, retryable: true);

            int start = prompt.IndexOf('{');
            int tail = prompt.IndexOf("\n请按系统提示", StringComparison.Ordinal);
            string json = tail > start ? prompt[start..tail] : prompt[start..];
            using var doc = JsonDocument.Parse(json);
            string scanId = doc.RootElement.GetProperty("scanId").GetString() ?? "";

            var items = new List<Dictionary<string, object?>>();
            foreach (var card in doc.RootElement.GetProperty("cards").EnumerateArray())
            {
                string id = card.GetProperty("folderId").GetString() ?? "";
                int age = card.GetProperty("ageDays").GetInt32();
                long size = card.GetProperty("sizeBytes").GetInt64();
                items.Add(new Dictionary<string, object?>
                {
                    ["folderId"] = id,
                    ["verdict"] = "recommend",
                    ["confidence"] = 0.9,
                    ["why"] = "缓存与旧文件堆积",
                    ["risk"] = "",
                    ["reclaimableBytes"] = size,
                    ["evidenceRefs"] = new[] { "card:" + id, "age:" + age + "d" },
                });
            }

            var payload = new Dictionary<string, object?>
            {
                ["reportVersion"] = 1,
                ["scanId"] = scanId,
                ["summary"] = "可回收约 2GB",
                ["items"] = items,
                ["questions"] = Array.Empty<object>(),
                ["degraded"] = null,
            };

            return Task.FromResult(new ChatResponse
            {
                Content = JsonSerializer.Serialize(payload),
                Model = "scripted-1",
                Usage = Usage,
            });
        }
    }

    private sealed class Fixture
    {
        public FileEntry Root = null!;
        public FileEntry Alice = null!;
        public FileEntry Data = null!;
        public FileEntry Temp = null!;
        public FileEntry Games = null!;
        public FileEntry Mixed = null!;
        public ScanSession Session = null!;
        public FolderIndex Index = null!;

        public static Fixture Create(DateTimeOffset scannedAt)
        {
            var f = new Fixture();
            f.Root = NewDir("C:\\", "C:\\", 8 * GB, scannedAt.AddDays(-1).DateTime);

            var users = NewDir("Users", @"C:\Users", 2 * GB, scannedAt.AddDays(-30).DateTime);
            f.Alice = NewDir("Alice", @"C:\Users\Alice", 1536 * MB, scannedAt.AddDays(-200).DateTime);
            Add(f.Alice, NewFile("big.log", @"C:\Users\Alice\big.log", 20 * MB, scannedAt.AddDays(-100).DateTime));
            Add(users, f.Alice);
            Add(f.Root, users);

            var windows = NewDir("Windows", @"C:\Windows", 3 * GB, scannedAt.AddDays(-365).DateTime);
            var system32 = NewDir("System32", @"C:\Windows\System32", 2 * GB, scannedAt.AddDays(-365).DateTime);
            system32.IsSystem = true;
            Add(windows, system32);
            Add(f.Root, windows);

            var programFiles = NewDir("Program Files", @"C:\Program Files", 300 * MB, scannedAt.AddDays(-300).DateTime);
            Add(programFiles, NewDir("OldApp", @"C:\Program Files\OldApp", 150 * MB, scannedAt.AddDays(-300).DateTime));
            Add(f.Root, programFiles);

            f.Data = NewDir("Data", @"C:\Data", 2 * GB, scannedAt.AddDays(-120).DateTime);
            for (int i = 0; i < 10; i++)
            {
                var item = NewDir($"Item{i}", $@"C:\Data\Item{i}", 100 * MB, scannedAt.AddDays(-120).DateTime);
                Add(item, NewFile($"blob{i}.bin", $@"C:\Data\Item{i}\blob{i}.bin", 100 * MB, scannedAt.AddDays(-120).DateTime));
                Add(f.Data, item);
            }

            Add(f.Root, f.Data);

            f.Temp = NewDir("Temp", @"C:\Temp", 60 * MB, scannedAt.AddDays(-5).DateTime);
            for (int i = 0; i < 3; i++)
            {
                var file = NewFile($"tmp{i}.tmp", $@"C:\Temp\tmp{i}.tmp", 1 * MB, scannedAt.AddDays(-5).DateTime);
                file.Category = "缓存";
                Add(f.Temp, file);
            }

            Add(f.Root, f.Temp);

            f.Games = NewDir("Games", @"C:\Games", 800 * MB, scannedAt.AddDays(-60).DateTime);
            Add(f.Games, NewFile("game.bin", @"C:\Games\game.bin", 700 * MB, scannedAt.AddDays(-60).DateTime));
            Add(f.Root, f.Games);

            f.Mixed = NewDir("Mixed", @"C:\Mixed", 30 * MB, scannedAt.AddDays(-20).DateTime);
            for (int i = 0; i < 12; i++)
                Add(f.Mixed, NewFile($"f{i}.e{i}", $@"C:\Mixed\f{i}.e{i}", 1 * MB, scannedAt.AddDays(-20).DateTime));
            Add(f.Root, f.Mixed);

            Add(f.Root, NewDir("Tiny", @"C:\Tiny", 5 * MB, scannedAt.AddDays(-2).DateTime));

            Recompute(f.Root);
            f.Session = ScanSession.Create("C:", f.Root, new ScanQuality { Source = ScanSource.Mft, Complete = true }, scannedAt, "s_20261109_120000_abcdef", 16 * GB);
            f.Index = new FolderIndex(f.Root);
            return f;
        }

        public static Fixture CreateSmall(DateTimeOffset scannedAt)
        {
            var f = new Fixture();
            f.Root = NewDir("C:\\", "C:\\", 2 * MB, scannedAt.AddDays(-1).DateTime);
            Add(f.Root, NewDir("Docs", @"C:\Docs", 1 * MB, scannedAt.AddDays(-2).DateTime));
            Add(f.Root, NewDir("Pics", @"C:\Pics", 1 * MB, scannedAt.AddDays(-2).DateTime));
            Recompute(f.Root);
            f.Session = ScanSession.Create("C:", f.Root, null, scannedAt, "s_20261109_130000_ffffff", 8 * GB);
            f.Index = new FolderIndex(f.Root);
            return f;
        }

        private static FileEntry NewDir(string name, string fullPath, long size, DateTime modified)
            => new()
            {
                Name = name,
                FullPath = fullPath,
                Kind = EntryKind.Directory,
                Size = size,
                Allocated = size,
                Modified = modified,
            };

        private static FileEntry NewFile(string name, string fullPath, long size, DateTime modified)
            => new()
            {
                Name = name,
                FullPath = fullPath,
                Kind = EntryKind.File,
                Size = size,
                Allocated = size,
                Modified = modified,
            };

        private static void Add(FileEntry parent, FileEntry child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        private static (int Files, int Dirs) Recompute(FileEntry node)
        {
            if (!node.IsDirectory) return (1, 0);

            int files = 0;
            int dirs = 0;
            foreach (var child in node.ChildList)
            {
                var (childFiles, childDirs) = Recompute(child);
                files += childFiles;
                if (child.IsDirectory) dirs += childDirs + 1;
            }

            node.FileCount = files;
            node.FolderCount = dirs;
            return (files, dirs);
        }
    }
}
