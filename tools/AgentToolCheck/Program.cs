using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.Agent;

namespace AiDiskCleaner.AgentToolCheck;

internal static class Program
{
    private const long KB = 1024;
    private const long MB = 1024 * 1024;
    private const long GB = 1024L * 1024 * 1024;

    private static readonly DateTimeOffset ScannedAt = new(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(8));

    private static int _pass;
    private static int _fail;
    private static readonly List<string> Failures = new();

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        SessionTests();
        FolderIndexTests();
        EvidenceCardTests();
        CandidateTests();
        EnvelopeTests();
        ToolTests();
        SafetyTests();

        Console.WriteLine();
        Console.WriteLine($"PASS {_pass}   FAIL {_fail}");
        if (_fail > 0)
        {
            Console.WriteLine("失败项：");
            foreach (string failure in Failures) Console.WriteLine("  - " + failure);
        }

        return _fail == 0 ? 0 : 1;
    }

    // ───────────────────────── 会话 ─────────────────────────

    private static void SessionTests()
    {
        Section("ScanSession");

        var fixture = Fixture.Create(ScannedAt);
        var session = fixture.Session;

        Check("scanId 格式", Regex.IsMatch(session.ScanId, @"^s_\d{8}_\d{6}_[0-9a-f]{6}$"));
        Check("scanId 由扫描时间生成", session.ScanId.StartsWith("s_20261101_120000_", StringComparison.Ordinal));
        Check("Source 映射 mft", session.Source == "mft");
        Check("RootFolderId = f_root", session.RootFolderId == FolderIndex.RootId);
        Check("根目录大小", session.Root.Size == 8 * GB);
        Check("FreshFor = 30 分钟", ScanSession.FreshFor == TimeSpan.FromMinutes(30));
        Check("ExpireAfter = 7 天", ScanSession.ExpireAfter == TimeSpan.FromDays(7));

        var noQuality = ScanSession.Create("C:", fixture.Root, quality: null, scannedAt: ScannedAt, scanId: "s_x");
        Check("Quality 为空时 Source=unknown", noQuality.Source == "unknown");

        var recursive = ScanSession.Create(
            "C:", fixture.Root,
            new ScanQuality { Source = ScanSource.Recursive },
            scannedAt: ScannedAt, scanId: "s_y");
        Check("Source 映射 recursive", recursive.Source == "recursive");

        var stale = ScanSession.Create("C:", fixture.Root, scannedAt: DateTimeOffset.Now.AddMinutes(-31));
        var fresh = ScanSession.Create("C:", fixture.Root, scannedAt: DateTimeOffset.Now.AddMinutes(-10));
        Check("31 分钟前的会话过期", stale.IsStale);
        Check("10 分钟前的会话新鲜", !fresh.IsStale);
        Check("7 天边界", ScanSession.Create("C:", fixture.Root, scannedAt: DateTimeOffset.Now.AddDays(-8)).IsExpired);

        var totals = session.CategoryTotals;
        Check("CategoryTotals 聚合缓存字节", totals.TryGetValue("缓存", out long cacheBytes) && cacheBytes == 3 * MB);
        Check("CategoryTotals 不为空", totals.Count > 0);

        // 不同扫描时间会得到不同 scanId（但索引 id 不变）
        var later = ScanSession.Create("C:", fixture.Root, scannedAt: ScannedAt.AddSeconds(1));
        Check("scanId 含随机后缀", later.ScanId != session.ScanId);
        Check("索引 id 与扫描时间无关", new FolderIndex(fixture.Root).GetFolderId(fixture.Data) == fixture.Index.GetFolderId(fixture.Data));
    }

    // ───────────────────────── 索引 ─────────────────────────

    private static void FolderIndexTests()
    {
        Section("FolderIndex");

        var f = Fixture.Create(ScannedAt);
        var index = f.Index;

        Check("根 id 固定", index.GetFolderId(f.Root) == "f_root");
        Check("folderId 形如 f_+12 hex", f.DataId.Length == 14 && f.DataId.StartsWith("f_", StringComparison.Ordinal)
            && f.DataId[2..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'));

        Check("folderId 对路径规范化稳定",
            FolderIndex.MakeFolderId("Data\\Item0") == FolderIndex.MakeFolderId("/data/item0/"));

        Check("folderId 大小写稳定",
            FolderIndex.MakeFolderId("Program Files\\OldApp") == FolderIndex.MakeFolderId("program files\\oldapp"));

        Check("两次建索引 id 相同", new FolderIndex(f.Root).GetFolderId(f.Alice) == index.GetFolderId(f.Alice));
        Check("根路径为空", index.GetRelPath(f.Root) == "");
        Check("相对路径", index.GetRelPath(f.Alice) == "Users\\Alice");
        Check("深度 root=0", index.GetDepth(f.Root) == 0);
        Check("深度 Data=1", index.GetDepth(f.Data) == 1);
        Check("深度 Item0=2", index.GetDepth(f.Item0) == 2);
        Check("FolderCount 覆盖全部真实目录", index.FolderCount >= 20);

        Check("TryGet 已知目录", index.TryGet(f.DataId, out var found) && ReferenceEquals(found, f.Data));
        Check("TryGet 未知 id 返回 false", !index.TryGet("f_000000000000", out _));
        Check("LooksLikeFolderId 合法", FolderIndex.LooksLikeFolderId("f_0123456789ab"));
        Check("LooksLikeFolderId 根", FolderIndex.LooksLikeFolderId("f_root"));
        Check("LooksLikeFolderId 非法", !FolderIndex.LooksLikeFolderId("f_XYZ") && !FolderIndex.LooksLikeFolderId(null));
        Check("GetFolderId 未知条目为 null", index.GetFolderId(new FileEntry { Kind = EntryKind.Directory, Name = "x" }) is null);
    }

    // ───────────────────────── 证据卡 ─────────────────────────

    private static void EvidenceCardTests()
    {
        Section("EvidenceCard");

        var f = Fixture.Create(ScannedAt);
        var aliceId = f.Index.GetFolderId(f.Alice)!;

        var card1 = EvidenceCardBuilder.Build(f.Session, f.Index, f.Alice);
        var card2 = EvidenceCardBuilder.Build(f.Session, f.Index, f.Alice);
        string json1 = ToolJson.Serialize(card1);
        string json2 = ToolJson.Serialize(card2);

        Check("卡片确定性（字节级相同）", json1 == json2);
        Check("卡片 folderId", card1.FolderId == aliceId);
        Check("卡片版本", card1.CardVersion == "card-v1");
        Check("默认不出完整路径", card1.FullPath is null);
        Check("默认相对路径已脱敏", card1.RelPath == "Users\\%USER%" && !json1.Contains("Alice", StringComparison.Ordinal));
        Check("samples ≤ 6", card1.Samples.Count <= 6 && card1.Samples.Count == 6);
        Check("sampleDirs ≤ 5", card1.SampleDirs.Count <= 5);
        Check("typeMix ≤ 8", card1.TypeMix.Count <= 8);
        Check("文件>6 触发 truncated", card1.Truncated);
        Check("ageDays 为非负整数", card1.AgeDays >= 0);

        var multi = EvidenceCardBuilder.Build(f.Session, f.Index, f.Mixed);
        Check("12 种扩展名截断到 8", multi.TypeMix.Count == 8);
        Check("扩展名种类超限 truncated", multi.Truncated);

        var dataCard = EvidenceCardBuilder.Build(f.Session, f.Index, f.Data);
        Check("子目录>5 时 sampleDirs 截断", dataCard.SampleDirs.Count == 5);

        var full = EvidenceCardBuilder.Build(f.Session, f.Index, f.Alice, new EvidenceCardOptions { Privacy = CardPrivacyMode.Full });
        Check("Full 模式带完整路径", full.FullPath == f.Alice.FullPath && full.RelPath == "Users\\Alice");

        var off = EvidenceCardBuilder.Build(f.Session, f.Index, f.Alice, new EvidenceCardOptions { Privacy = CardPrivacyMode.Off });
        Check("Off 模式无路径", off.RelPath is null && off.FullPath is null);

        Check("脱敏用户目录", EvidenceCardBuilder.Redact(@"C:\Users\Alice\AppData") == @"C:\Users\%USER%\AppData");
        Check("脱敏不误伤普通路径", EvidenceCardBuilder.Redact(@"D:\Games\Steam") == @"D:\Games\Steam");
        Check("脱敏 ~ 前缀路径", EvidenceCardBuilder.Redact(@"C:\Users\Bob") == @"C:\Users\%USER%");

        // ageDays 必须用 scannedAt，不读 Now：用 2020 年的会话来锁死
        var oldScan = new DateTimeOffset(2020, 1, 11, 12, 0, 0, TimeSpan.FromHours(8));
        var oldRoot = new FileEntry
        {
            Name = "C:\\", FullPath = "C:\\", Kind = EntryKind.Directory,
            Size = 1 * GB, Modified = new DateTime(2020, 1, 1, 12, 0, 0),
        };
        var oldSession = ScanSession.Create("C:", oldRoot, scannedAt: oldScan, scanId: "s_old");
        var oldIndex = new FolderIndex(oldRoot);
        var oldCard = EvidenceCardBuilder.Build(oldSession, oldIndex, oldRoot);
        Check("ageDays 基于 ScannedAt（2020 会话=10 天）", oldCard.AgeDays == 10 && !oldCard.Truncated);
        Check("卡片 relPath 根为空", oldCard.RelPath == "");
    }

    // ───────────────────────── 候选集 ─────────────────────────

    private static void CandidateTests()
    {
        Section("CandidateSelector");

        var f = Fixture.Create(ScannedAt);
        var set = CandidateSelector.Select(f.Session, f.Index);
        var paths = set.Items.Select(i => i.RelPath).ToList();

        Check("候选非空", set.Items.Count > 0);
        Check("大目录进入候选", paths.Contains("Data") && paths.Contains("Games") && paths.Contains("Users\\%USER%"));
        Check("小目录不进入候选", !paths.Contains("Tiny"));
        Check("保护目录整棵剪掉", paths.All(p => !p.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            && paths.All(p => !p.Contains("System32", StringComparison.OrdinalIgnoreCase)));
        Check("容器根不作为候选", paths.All(p => p is not ("Users" or "Program Files" or "Windows" or "ProgramData")));
        Check("同父最多 8 个", paths.Count(p => p.StartsWith("Data\\", StringComparison.Ordinal)) <= 8);
        Check("候选总数上限", set.Items.Count <= 200);
        Check("候选分数降序", set.Items.Zip(set.Items.Skip(1)).All(pair => pair.First.Score >= pair.Second.Score));
        Check("候选 folderId 都能解析", set.Items.All(i => f.Index.TryGet(i.FolderId, out _)));

        var again = CandidateSelector.Select(f.Session, f.Index);
        Check("候选确定性", set.Items.Select(i => i.FolderId).SequenceEqual(again.Items.Select(i => i.FolderId)));

        var capped = CandidateSelector.Select(f.Session, f.Index, new CandidateOptions { MaxCandidates = 2 });
        Check("MaxCandidates 生效", capped.Items.Count == 2 && capped.Truncated);

        var narrower = CandidateSelector.Select(f.Session, f.Index, new CandidateOptions { MaxDepth = 1 });
        Check("MaxDepth 生效", narrower.Items.All(i => i.Depth <= 1));

        var hugeThreshold = CandidateSelector.Select(f.Session, f.Index, new CandidateOptions { MinSizeBytes = 4 * GB });
        Check("MinSizeBytes 生效", hugeThreshold.Items.Count == 0);

        Check("系统目录不被评分抬高", CandidateSelector.Score(f.Session, f.System32)
            < CandidateSelector.Score(f.Session, f.Data));
        Check("候选 relPath 已脱敏", set.Items.All(i => !i.RelPath.Contains("Alice", StringComparison.Ordinal)));
    }

    // ───────────────────────── 信封 ─────────────────────────

    private static void EnvelopeTests()
    {
        Section("ToolEnvelope");

        var f = Fixture.Create(ScannedAt);
        var host = new ToolHost(f.Session, f.Index);

        string ok = host.Invoke("get_overview", Args(("scanId", f.Session.ScanId)));
        var okRoot = Parse(ok);
        Check("成功信封 ok=true", okRoot.GetProperty("ok").GetBoolean());
        Check("成功信封带 tool/scanId", okRoot.GetProperty("tool").GetString() == "get_overview"
            && okRoot.GetProperty("scanId").GetString() == f.Session.ScanId);
        Check("成功信封 approxTokens>0", okRoot.GetProperty("approxTokens").GetInt32() > 0);
        Check("成功信封无 error 字段", !okRoot.TryGetProperty("error", out _));
        Check("成功信封 data 是对象", okRoot.GetProperty("data").ValueKind == JsonValueKind.Object);

        string unknownTool = host.Invoke("execute", "{}");
        Check("未知工具 INVALID_ARGUMENT", ErrorCode(unknownTool) == ToolErrorCodes.InvalidArgument
            && !Parse(unknownTool).GetProperty("ok").GetBoolean());

        string dangerous = host.Invoke("delete_files", "{}");
        Check("delete_files 不存在", ErrorCode(dangerous) == ToolErrorCodes.InvalidArgument);

        string badScan = host.Invoke("get_overview", Args(("scanId", "s_nope")));
        Check("scanId 不匹配 SCAN_NOT_FOUND", ErrorCode(badScan) == ToolErrorCodes.ScanNotFound);

        string badJson = host.Invoke("get_overview", "{ not json");
        Check("非法 JSON INVALID_ARGUMENT", ErrorCode(badJson) == ToolErrorCodes.InvalidArgument);

        string unknownKey = host.Invoke("get_overview", Args(("scanId", f.Session.ScanId), ("path", @"C:\Windows")));
        Check("未知参数拒绝", ErrorCode(unknownKey) == ToolErrorCodes.InvalidArgument);

        string badFolder = host.Invoke("get_folder_card", Args(("scanId", f.Session.ScanId), ("folderId", "nope")));
        Check("folderId 格式错 INVALID_ARGUMENT", ErrorCode(badFolder) == ToolErrorCodes.InvalidArgument);

        string missingFolder = host.Invoke("get_folder_card", Args(("scanId", f.Session.ScanId), ("folderId", "f_000000000000")));
        Check("folderId 不存在 FOLDER_NOT_FOUND", ErrorCode(missingFolder) == ToolErrorCodes.FolderNotFound);

        string protectedCard = host.Invoke("get_folder_card", Args(("scanId", f.Session.ScanId), ("folderId", f.System32Id)));
        Check("保护目录证据卡 OUT_OF_SCOPE", ErrorCode(protectedCard) == ToolErrorCodes.OutOfScope);

        string missingScanId = host.Invoke("get_overview", "{}");
        Check("缺 scanId INVALID_ARGUMENT", ErrorCode(missingScanId) == ToolErrorCodes.InvalidArgument);

        var budget = new ToolBudget { MaxCalls = 2 };
        var limited = new ToolHost(f.Session, f.Index, budget);
        limited.Invoke("get_overview", Args(("scanId", f.Session.ScanId)));
        limited.Invoke("get_overview", Args(("scanId", f.Session.ScanId)));
        string exhausted = limited.Invoke("get_overview", Args(("scanId", f.Session.ScanId)));
        Check("第 3 次调用 BUDGET_EXCEEDED", ErrorCode(exhausted) == ToolErrorCodes.BudgetExceeded);
        Check("预算记录调用数", budget.CallsUsed == 2 && budget.IsExhausted);

        var tokenBudget = new ToolBudget { MaxInputTokens = 10 };
        var tokenHost = new ToolHost(f.Session, f.Index, tokenBudget);
        tokenHost.Invoke("get_overview", Args(("scanId", f.Session.ScanId)));
        if (tokenBudget.TokensUsed >= tokenBudget.MaxInputTokens)
        {
            string next = tokenHost.Invoke("get_overview", Args(("scanId", f.Session.ScanId)));
            Check("token 预算耗尽 BUDGET_EXCEEDED", ErrorCode(next) == ToolErrorCodes.BudgetExceeded);
        }
        else
        {
            Check("token 预算已记账", tokenBudget.TokensUsed > 0);
        }

        Check("ApproxTokens 估算不为 0", ToolJson.ApproxTokens("{\"a\":\"中文\"}") > 0);
        Check("信封用 camelCase", ok.Contains("\"approxTokens\"", StringComparison.Ordinal)
            && ok.Contains("\"scanId\"", StringComparison.Ordinal));
    }

    // ───────────────────────── 工具 ─────────────────────────

    private static void ToolTests()
    {
        Section("Tools");

        var f = Fixture.Create(ScannedAt);
        var host = new ToolHost(f.Session, f.Index);
        string scanId = f.Session.ScanId;

        string overview = host.Invoke("get_overview", Args(("scanId", scanId)));
        var overviewData = Parse(overview).GetProperty("data");
        var topFolders = overviewData.GetProperty("topFolders");
        Check("overview topFolders ≤ 15", topFolders.GetArrayLength() <= 15 && topFolders.GetArrayLength() > 0);
        long previous = long.MaxValue;
        bool sorted = true;
        foreach (var item in topFolders.EnumerateArray())
        {
            long size = item.GetProperty("sizeBytes").GetInt64();
            if (size > previous) sorted = false;
            previous = size;
        }

        Check("overview 按大小降序", sorted);
        Check("overview 带总量与质量", overviewData.GetProperty("totalBytes").GetInt64() == 8 * GB
            && overviewData.GetProperty("quality").GetProperty("complete").GetBoolean());

        string page1 = host.Invoke("list_folders", Args(("scanId", scanId), ("limit", 2)));
        var page1Root = Parse(page1);
        if (!page1Root.GetProperty("ok").GetBoolean()) Console.WriteLine("DEBUG list_folders: " + page1);
        var page1Data = page1Root.GetProperty("data").GetProperty("items");
        Check("分页 limit 生效", page1Data.GetArrayLength() == 2);
        Check("分页 nextCursor 存在", page1Root.GetProperty("truncated").GetBoolean()
            && page1Root.TryGetProperty("nextCursor", out var cursor) && cursor.GetString() == "2");

        string page2 = host.Invoke("list_folders", Args(("scanId", scanId), ("limit", 2), ("cursor", "2")));
        var page2Items = Parse(page2).GetProperty("data").GetProperty("items");
        Check("第二页不同", page2Items[0].GetProperty("relPath").GetString()
            != page1Data[0].GetProperty("relPath").GetString());

        string byName = host.Invoke("list_folders", Args(("scanId", scanId), ("sort", "name"), ("limit", 20)));
        var names = Parse(byName).GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("relPath").GetString()!).ToList();
        Check("name 排序", names.SequenceEqual(names.OrderBy(n => n, StringComparer.Ordinal)));

        string byAge = host.Invoke("list_folders", Args(("scanId", scanId), ("sort", "age"), ("limit", 20)));
        Check("age 排序可用", Parse(byAge).GetProperty("ok").GetBoolean());

        string filtered = host.Invoke("list_folders",
            Args(("scanId", scanId), ("minSizeBytes", 3 * GB), ("limit", 20)));
        var filteredItems = Parse(filtered).GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Check("minSizeBytes 过滤", filteredItems.Count > 0
            && filteredItems.All(i => i.GetProperty("sizeBytes").GetInt64() >= 3 * GB));

        string card = host.Invoke("get_folder_card", Args(("scanId", scanId), ("folderId", f.DataId)));
        var cardData = Parse(card).GetProperty("data");
        Check("卡片按 folderId 返回", cardData.GetProperty("folderId").GetString() == f.DataId
            && cardData.GetProperty("relPath").GetString() == "Data");
        Check("卡片 JSON 无完整路径", !card.Contains("C:\\Users\\Alice", StringComparison.Ordinal));

        string bigQuery = host.Invoke("query_entries",
            Args(("scanId", scanId), ("filter", new RawJson("{\"kind\":\"folder\",\"minSizeBytes\":104857600}")), ("limit", 20)));
        if (!Parse(bigQuery).GetProperty("ok").GetBoolean()) Console.WriteLine("DEBUG query: " + bigQuery);
        var bigItems = Parse(bigQuery).GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Check("query 大小过滤", bigItems.Count > 0
            && bigItems.All(i => i.GetProperty("sizeBytes").GetInt64() >= 100 * MB)
            && bigItems.All(i => i.GetProperty("kind").GetString() == "folder"));

        string extQuery = host.Invoke("query_entries",
            Args(("scanId", scanId), ("filter", new RawJson("{\"extensions\":[\"TMP\"]}")), ("limit", 20)));
        var extItems = Parse(extQuery).GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Check("query 扩展名过滤", extItems.Count > 0
            && extItems.All(i => i.GetProperty("ext").GetString() == "tmp"));

        string pathQuery = host.Invoke("query_entries",
            Args(("scanId", scanId), ("filter", new RawJson("{\"kind\":\"folder\",\"pathContains\":\"Item\"}")), ("limit", 5)));
        var pathItems = Parse(pathQuery).GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Check("query pathContains + limit", pathItems.Count == 5
            && pathItems.All(i => i.GetProperty("relPath").GetString()!.Contains("Item", StringComparison.Ordinal)));

        string pageQuery = host.Invoke("query_entries",
            Args(("scanId", scanId), ("filter", new RawJson("{\"extensions\":[\"tmp\"]}")), ("limit", 2)));
        Check("query 分页", Parse(pageQuery).GetProperty("data").GetProperty("items").GetArrayLength() == 2
            && Parse(pageQuery).GetProperty("truncated").GetBoolean());

        string badSort = host.Invoke("list_folders", Args(("scanId", scanId), ("sort", "haha")));
        Check("非法 sort INVALID_ARGUMENT", ErrorCode(badSort) == ToolErrorCodes.InvalidArgument);

        string badExt = host.Invoke("query_entries", Args(("scanId", scanId), ("filter", new RawJson("{\"extensions\":[\"a/b\"]}"))));
        Check("非法扩展名 INVALID_ARGUMENT", ErrorCode(badExt) == ToolErrorCodes.InvalidArgument);

        Check("工具清单 = 4", host.ToolNames.Count == 4 && host.HasTool("get_overview")
            && host.HasTool("list_folders") && host.HasTool("get_folder_card") && host.HasTool("query_entries"));
    }

    // ───────────────────────── 安全 ─────────────────────────

    private static void SafetyTests()
    {
        Section("Safety");

        var f = Fixture.Create(ScannedAt);
        var host = new ToolHost(f.Session, f.Index);

        var toolTypes = typeof(ToolHost).Assembly.GetTypes()
            .Where(t => typeof(IAgentTool).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();
        Check("默认工具全部实现 IAgentTool", toolTypes.Count == 4);

        string[] forbidden = { "delete", "remove", "shell", "execute", "write", "uninstall", "recycle", "run", "kill" };
        bool clean = true;
        foreach (var type in toolTypes)
        {
            var instance = (IAgentTool)Activator.CreateInstance(type)!;
            if (!Regex.IsMatch(instance.Name, "^[a-z][a-z0-9_]*$")) clean = false;
            if (forbidden.Any(word => instance.Name.Contains(word, StringComparison.OrdinalIgnoreCase))) clean = false;
        }

        Check("工具名 snake_case 且无危险动词", clean);

        var methodNames = typeof(ToolHost)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();
        Check("ToolHost 只暴露 Invoke/HasTool 等只读方法",
            methodNames.Contains("Invoke") && !methodNames.Any(n =>
                n.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Execute", StringComparison.OrdinalIgnoreCase)));

        Check("ToolHost 无文件系统字段", typeof(ToolHost).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .All(field => !field.FieldType.Name.Contains("FileSystem", StringComparison.OrdinalIgnoreCase)
                && !field.FieldType.Name.Contains("Deletion", StringComparison.OrdinalIgnoreCase)
                && !field.FieldType.Name.Contains("Stream", StringComparison.OrdinalIgnoreCase)));

        // 所有工具调用都带 scanId 校验
        foreach (string tool in host.ToolNames)
        {
            string result = host.Invoke(tool, "{}");
            Check($"{tool} 缺 scanId 被拒", ErrorCode(result) == ToolErrorCodes.InvalidArgument);
        }

        var candidates = CandidateSelector.Select(f.Session, f.Index);
        Check("候选不含保护路径", candidates.Items.All(i =>
            f.Index.TryGet(i.FolderId, out var node) && !ProtectedPaths.IsCleanupBlocked(node)));

        Check("候选不含盘根", candidates.Items.All(i => i.RelPath != ""));

        string cardJson = host.Invoke("get_folder_card", Args(("scanId", f.Session.ScanId), ("folderId", f.AliceId)));
        Check("证据卡不泄露用户名", !cardJson.Contains("Alice", StringComparison.Ordinal));
        Check("证据卡不含 ACL/SID", !cardJson.Contains("S-1-5-", StringComparison.OrdinalIgnoreCase));

        string overviewJson = host.Invoke("get_overview", Args(("scanId", f.Session.ScanId)));
        Check("概览不泄露完整路径", !overviewJson.Contains("C:\\\\Users", StringComparison.Ordinal)
            && !overviewJson.Contains("Alice", StringComparison.Ordinal));
    }

    // ───────────────────────── 脚手架 ─────────────────────────

    private sealed class Fixture
    {
        public FileEntry Root = null!;
        public FileEntry Alice = null!;
        public FileEntry System32 = null!;
        public FileEntry Data = null!;
        public FileEntry Item0 = null!;
        public FileEntry Mixed = null!;
        public FileEntry Tiny = null!;
        public ScanSession Session = null!;
        public FolderIndex Index = null!;

        public string DataId => Index.GetFolderId(Data)!;
        public string AliceId => Index.GetFolderId(Alice)!;
        public string System32Id => Index.GetFolderId(System32)!;

        public static Fixture Create(DateTimeOffset scannedAt)
        {
            var fixture = new Fixture();
            fixture.Root = NewDir("C:\\", "C:\\", 8 * GB, scannedAt.AddDays(-1).DateTime);

            var users = NewDir("Users", @"C:\Users", 2 * GB, scannedAt.AddDays(-30).DateTime);
            fixture.Alice = NewDir("Alice", @"C:\Users\Alice", 1536 * MB, scannedAt.AddDays(-200).DateTime);
            Add(fixture.Alice, NewFile("Notes.log", @"C:\Users\Alice\Notes.log", 1 * MB, scannedAt.AddDays(-200).DateTime));
            Add(fixture.Alice, NewFile("big.log", @"C:\Users\Alice\big.log", 20 * MB, scannedAt.AddDays(-100).DateTime));
            for (int i = 0; i < 6; i++)
            {
                Add(fixture.Alice, NewFile($"t{i}.tmp", $@"C:\Users\Alice\t{i}.tmp", 1 * MB, scannedAt.AddDays(-10).DateTime));
            }

            Add(users, fixture.Alice);
            Add(fixture.Root, users);

            var windows = NewDir("Windows", @"C:\Windows", 3 * GB, scannedAt.AddDays(-365).DateTime);
            fixture.System32 = NewDir("System32", @"C:\Windows\System32", 2560 * MB, scannedAt.AddDays(-365).DateTime);
            fixture.System32.IsSystem = true;
            Add(fixture.System32, NewDir("drivers", @"C:\Windows\System32\drivers", 1 * GB, scannedAt.AddDays(-365).DateTime));
            Add(windows, fixture.System32);
            Add(fixture.Root, windows);

            var programFiles = NewDir("Program Files", @"C:\Program Files", 300 * MB, scannedAt.AddDays(-300).DateTime);
            Add(programFiles, NewDir("OldApp", @"C:\Program Files\OldApp", 150 * MB, scannedAt.AddDays(-300).DateTime));
            Add(fixture.Root, programFiles);

            fixture.Data = NewDir("Data", @"C:\Data", 2 * GB, scannedAt.AddDays(-120).DateTime);
            for (int i = 0; i < 10; i++)
            {
                var item = NewDir($"Item{i}", $@"C:\Data\Item{i}", 100 * MB, scannedAt.AddDays(-120).DateTime);
                Add(item, NewFile($"blob{i}.bin", $@"C:\Data\Item{i}\blob{i}.bin", 100 * MB, scannedAt.AddDays(-120).DateTime));
                Add(fixture.Data, item);
                if (i == 0) fixture.Item0 = item;
            }

            Add(fixture.Root, fixture.Data);

            var temp = NewDir("Temp", @"C:\Temp", 60 * MB, scannedAt.AddDays(-5).DateTime);
            for (int i = 0; i < 3; i++)
            {
                var file = NewFile($"tmp{i}.tmp", $@"C:\Temp\tmp{i}.tmp", 1 * MB, scannedAt.AddDays(-5).DateTime);
                file.Category = "缓存";
                Add(temp, file);
            }

            Add(fixture.Root, temp);

            var games = NewDir("Games", @"C:\Games", 800 * MB, scannedAt.AddDays(-60).DateTime);
            Add(games, NewFile("game.bin", @"C:\Games\game.bin", 700 * MB, scannedAt.AddDays(-60).DateTime));
            Add(fixture.Root, games);

            fixture.Mixed = NewDir("Mixed", @"C:\Mixed", 30 * MB, scannedAt.AddDays(-20).DateTime);
            for (int i = 0; i < 12; i++)
            {
                Add(fixture.Mixed, NewFile($"f{i}.e{i}", $@"C:\Mixed\f{i}.e{i}", 1 * MB, scannedAt.AddDays(-20).DateTime));
            }

            Add(fixture.Root, fixture.Mixed);

            fixture.Tiny = NewDir("Tiny", @"C:\Tiny", 5 * MB, scannedAt.AddDays(-2).DateTime);
            Add(fixture.Tiny, NewFile("small.tmp", @"C:\Tiny\small.tmp", 5 * MB, scannedAt.AddDays(-2).DateTime));
            Add(fixture.Root, fixture.Tiny);

            Recompute(fixture.Root);

            var quality = new ScanQuality
            {
                Source = ScanSource.Mft,
                Complete = true,
                FilesRead = 1234,
                DirsRead = 99,
            };
            fixture.Session = ScanSession.Create("C:", fixture.Root, quality, scannedAt, "s_20261101_120000_abcdef", 16 * GB);
            fixture.Index = new FolderIndex(fixture.Root);
            return fixture;
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
                if (child.IsDirectory)
                {
                    dirs += childDirs + 1;
                }
            }

            node.FileCount = files;
            node.FolderCount = dirs;
            return (files, dirs);
        }
    }

    private static string Args(params (string Key, object Value)[] pairs)
    {
        var builder = new StringBuilder("{");
        for (int i = 0; i < pairs.Length; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(JsonSerializer.Serialize(pairs[i].Key)).Append(':');
            builder.Append(pairs[i].Value switch
            {
                RawJson raw => raw.Json,
                string text => JsonSerializer.Serialize(text),
                int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                bool flag => flag ? "true" : "false",
                _ => JsonSerializer.Serialize(pairs[i].Value),
            });
        }

        return builder.Append('}').ToString();
    }

    private sealed record RawJson(string Json);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string ErrorCode(string json)
    {
        var root = Parse(json);
        return root.TryGetProperty("error", out var error)
            ? error.GetProperty("code").GetString() ?? ""
            : "";
    }

    private static void Section(string name) => Console.WriteLine("== " + name);

    private static void Check(string name, bool ok)
    {
        if (ok)
        {
            _pass++;
        }
        else
        {
            _fail++;
            Failures.Add(name);
            Console.WriteLine("  FAIL " + name);
        }
    }
}
