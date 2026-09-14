using System.IO;
using System.Reflection;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

namespace RecognitionPersistenceCheck;

/// <summary>
/// 「识别结果持久化」的离线行为检查（不联网、不碰真实用户配置）。
///
/// 覆盖需求里的每一条硬约束：
/// <list type="number">
/// <item>**真往返**：一个服务实例识别并落盘，**另一个全新实例**读回同一份描述性文本；</item>
/// <item>**恢复不发请求**：读回路径上一发请求都没有（出站计数不变）；</item>
/// <item>**证据变化失效**：内容指纹变了就不再命中旧结论；</item>
/// <item>**损坏容忍**：坏文件 / 坏记录都当没有，绝不抛，下一次写入能覆盖；</item>
/// <item>**有界 + 原子**：条数有上限、写入不留临时文件；</item>
/// <item>**无密钥**：落盘文本统一脱敏，原始模型回复根本不落盘；</item>
/// <item>**来源隔离**：整理树拿不到清理树的落盘结果，恢复结果也没有勾选 / 删除能力；</item>
/// <item>**确定性键**：路径规范化、不含扫描代次、不用进程随机 GetHashCode。</item>
/// </list>
/// 运行：dotnet run --project tools/RecognitionPersistenceCheck
/// </summary>
public static class Program
{
    static int _pass, _fail;
    static readonly List<string> Failures = new();
    static string _tmp = "";

    static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) { Console.WriteLine("  ok   " + name); _pass++; }
        else
        {
            Console.WriteLine("  FAIL " + name + (detail is null ? "" : "  [" + detail + "]"));
            _fail++;
            Failures.Add(name + (detail is null ? "" : "  [" + detail + "]"));
        }
    }

    static void Section(string title) => Console.WriteLine("== " + title + " ==");

    static string StorePath(string name) => Path.Combine(_tmp, name + ".json");

    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        _tmp = Path.Combine(Path.GetTempPath(), "dashao-recog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);

        // 配置目录也指向临时目录：绝不碰真实用户配置。
        string? oldOverride = AppPaths.OverrideDirectory;
        AppPaths.OverrideDirectory = Path.Combine(_tmp, "cfg");
        Directory.CreateDirectory(AppPaths.OverrideDirectory);

        try
        {
            KeyDeterminismTests();
            StoreBoundsAndAtomicityTests();
            ItemAiRoundtripTests();
            PurposeRoundtripTests();
            ChangedEvidenceInvalidationTests();
            CorruptionToleranceTests();
            NoSecretsTests();
            OrganizeIsolationTests();
            WiringHookTests();
        }
        finally
        {
            AppPaths.OverrideDirectory = oldOverride;
            AiClient.Handler = null;
            try { Directory.Delete(_tmp, true); } catch { /* 清理失败不影响结论 */ }
        }

        Console.WriteLine();
        Console.WriteLine($"PASS {_pass}   FAIL {_fail}");
        foreach (var f in Failures) Console.WriteLine("  - " + f);
        return _fail == 0 ? 0 : 1;
    }

    // ---------------- 工具 ----------------

    static ItemAiRequest ItemReq(string path, long size = 100, ItemAiSource src = ItemAiSource.Clean)
        => new(
            ScopeKey: path, IsFolder: true, Path: path, Label: "x", Size: size,
            Modified: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), Kind: "folder",
            LocalReason: "rule", FolderSummary: new[] { "1 KB a" },
            FolderSummaryShown: 1, FolderChildTotal: 3)
        { Source = src };

    static ItemAiResult ItemResult(string purpose)
        => new(ItemAiSuggestion.CanConsider, purpose, "影响文本", "证据文本", "缺少文本",
            "RAW-MODEL-REPLY", false, 3, 4, 5, "http", 7);

    static FileEntry Folder(string path, params string[] files)
    {
        var d = new FileEntry { Name = Path.GetFileName(path), FullPath = path, Kind = EntryKind.Directory };
        foreach (var f in files)
        {
            d.Children.Add(new FileEntry
            {
                Name = f, FullPath = Path.Combine(path, f), Kind = EntryKind.File, Size = 16, Parent = d,
            });
        }
        d.FileCount = files.Length;
        return d;
    }

    static AiProviderCfg Provider() => new() { Id = "t", BaseUrl = "http://localhost", Models = { "m" } };

    /// <summary>装一个能用的假通道，并返回「被调用次数」的读取器。</summary>
    static Func<int> StubAi(string reply)
    {
        int calls = 0;
        AiClient.Handler = (_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new AiReply { Text = reply });
        };
        return () => Volatile.Read(ref calls);
    }

    static string ReadSource(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.targets"))) dir = dir.Parent;
        if (dir == null) return "";
        string full = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? File.ReadAllText(full) : "";
    }

    // ---------------- 1. 键的确定性 ----------------

    static void KeyDeterminismTests()
    {
        Section("确定性键：路径规范化、不含扫描代次、不用 GetHashCode");

        Check("同路径不同写法（斜杠/大小写/末尾反斜杠）键相同",
            RecognitionKey.Item(@"C:\A", 5) == RecognitionKey.Item(@"c:/a\", 5),
            RecognitionKey.Item(@"C:\A", 5) + " vs " + RecognitionKey.Item(@"c:/a\", 5));
        Check("版本变化 ⇒ 键变化（内容/提示词/配置任一变化都会失效）",
            RecognitionKey.Item(@"C:\A", 5) != RecognitionKey.Item(@"C:\A", 6));
        Check("不同路径 ⇒ 键不同", RecognitionKey.Item(@"C:\A", 5) != RecognitionKey.Item(@"C:\B", 5));
        Check("稳定哈希是纯函数（同输入两次一致）",
            RecognitionKey.Stable("a", "b", "c") == RecognitionKey.Stable("a", "b", "c"));
        Check("分段有分隔符，不会把 (“a”,“bc”) 和 (“ab”,“c”) 撞到一起",
            RecognitionKey.Stable("a", "bc") != RecognitionKey.Stable("ab", "c"));

        // 用途键：除扫描代次外完全相同的摘要 ⇒ 键必须相同（证明代次不参与）
        var dir = Folder(@"C:\purpose-key", "a.dat");
        var s1 = FolderPurposeRules.Summarize(dir, new FolderId(dir.FullPath, 1), 0, "purpose-key");
        var s2 = FolderPurposeRules.Summarize(dir, new FolderId(dir.FullPath, 98765), 0, "purpose-key");
        Check("用途键不含扫描代次（不同代次仍是同一个目录）",
            FolderPurposeService.PurposeKey(s1, "cfg") == FolderPurposeService.PurposeKey(s2, "cfg"));
        var s3 = s2 with { Size = s2.Size + 1 };
        Check("内容指纹变化 ⇒ 用途键变化",
            FolderPurposeService.PurposeKey(s1, "cfg") != FolderPurposeService.PurposeKey(s3, "cfg"));
        Check("配置签名变化 ⇒ 用途键变化",
            FolderPurposeService.PurposeKey(s1, "cfg") != FolderPurposeService.PurposeKey(s1, "cfg2"));

        Check("默认落盘路径跟随配置目录（档案隔离）",
            RecognitionStore.DefaultPath.StartsWith(AppPaths.ConfigDirectory, StringComparison.OrdinalIgnoreCase),
            RecognitionStore.DefaultPath);

        // 源码守卫：键的实现里不能出现进程随机哈希
        string storeSrc = ReadSource("src/AiDiskCleaner/Services/RecognitionStore.cs");
        string itemSrc = ReadSource("src/AiDiskCleaner/Services/ItemAiService.cs");
        string purposeSrc = ReadSource("src/AiDiskCleaner/Services/FolderPurposeService.cs");
        Check("键/存储实现不使用 GetHashCode（跨进程稳定）",
            storeSrc.Length > 0 && itemSrc.Length > 0 && purposeSrc.Length > 0
            && !storeSrc.Contains("GetHashCode(", StringComparison.Ordinal)
            && !itemSrc.Contains("GetHashCode(", StringComparison.Ordinal)
            && !purposeSrc.Contains("GetHashCode(", StringComparison.Ordinal));
    }

    // ---------------- 2. 有界与原子 ----------------

    static void StoreBoundsAndAtomicityTests()
    {
        Section("有界 + 原子写：条数封顶、不留临时文件");

        string path = StorePath("bounds");
        var store = new RecognitionStore(path);
        for (int i = 0; i < RecognitionStore.MaxEntries + 30; i++)
        {
            long v = i + 1;
            store.PutItem(RecognitionKey.Item(@"C:\bounded\" + i, v), v, ItemResult("p" + i));
        }

        Check("条数不超过上限", store.Count == RecognitionStore.MaxEntries, store.Count.ToString());
        Check("写入不留临时文件", !File.Exists(path + ".tmp"));
        Check("落盘文件存在且非空", File.Exists(path) && new FileInfo(path).Length > 0);

        var reload = new RecognitionStore(path);
        Check("新实例读回同样多的条数", reload.Count == store.Count, reload.Count.ToString());
        // 最新写入的一条必须还在（淘汰的是最旧的）
        long lastV = RecognitionStore.MaxEntries + 30;
        Check("淘汰的是最旧、保留的是最新",
            reload.TryGetItem(RecognitionKey.Item(@"C:\bounded\" + (lastV - 1), lastV), out var last)
            && last.Purpose == "p" + (lastV - 1),
            last?.Purpose ?? "(missing)");

        store.Clear();
        Check("Clear 后文件被删除", !File.Exists(path));
    }

    // ---------------- 3. 逐项 AI 真往返 ----------------

    static void ItemAiRoundtripTests()
    {
        Section("逐项 AI：服务实例 A 落盘 ⇒ 全新实例 B 读回，且不再发请求");

        string path = StorePath("item");
        var req = ItemReq(@"C:\cache-dir");
        string cfg = "cfg-item";

        var svcA = new ItemAiService(new RecognitionStore(path));
        var calls = StubAi("SUGGEST: 可考虑清理\nPURPOSE: 缓存目录\nIMPACT: 删了会重建\nBASIS: 命中规则\nMISSING: 无");
        var first = svcA.AnalyzeAsync(req, Provider(), "m", false, cfg, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check("首次识别真的发了一次并拿到结论",
            calls() == 1 && first != null && first.Purpose == "缓存目录", first?.Purpose ?? "(null)");

        // 恢复阶段：换成会计数的空回复通道；一旦恢复真的去问模型，计数就会 > 0。
        int afterStore = calls();
        var svcB = new ItemAiService(new RecognitionStore(path));
        var restored = svcB.TryGetRestored(req, cfg);
        Check("全新实例从落盘读回同一份文本",
            restored != null
            && restored.Purpose == "缓存目录" && restored.Impact == "删了会重建"
            && restored.Basis == "命中规则" && restored.Missing == "无",
            restored?.Purpose ?? "(null)");
        Check("恢复结果标为来自缓存", restored?.FromCache == true);
        Check("恢复阶段没有再发任何请求", calls() == afterStore, calls().ToString());
        Check("恢复结果不带建议档位（不是判定）", restored?.Suggestion == ItemAiSuggestion.Unknown);
        Check("恢复结果不带模型原始回复", restored != null && restored.Raw.Length == 0);

        // 通过完整 Analyze 路径也会命中落盘缓存，同样不发请求
        var viaAnalyze = svcB.AnalyzeAsync(req, Provider(), "m", false, cfg, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check("Analyze 命中落盘缓存，仍然一次请求都没发",
            calls() == afterStore && viaAnalyze != null && viaAnalyze.FromCache);
        AiClient.Handler = null;
    }

    // ---------------- 4. 用途识别真往返 ----------------

    static void PurposeRoundtripTests()
    {
        Section("目录用途：AI 结论落盘 ⇒ 新实例直接读回（跨扫描代次）");

        string path = StorePath("purpose");
        var dir = Folder(@"D:\mystery-folder", "a.dat", "b.dat");
        string cfg = "cfg-purpose";
        var id1 = new FolderId(dir.FullPath, 1);

        var svcA = new FolderPurposeService(null, new RecognitionStore(path));
        var calls = StubAi("PURPOSE: 神秘目录\nCATEGORY: 资料\nBASIS: 模型判断");
        var res1 = svcA.RecognizeAsync(dir, id1, 0, "mystery-folder", allowAi: true,
            provider: Provider(), model: "m", sendFullPath: false, configSignature: cfg,
            ct: CancellationToken.None).GetAwaiter().GetResult();
        Check("本地说不清 ⇒ 走一次 AI 并得到结论",
            calls() == 1 && res1.HasConclusion && res1.PurposeName == "神秘目录"
            && res1.Source == PurposeSource.Ai,
            $"{res1.PurposeName}/{res1.Source}");

        int afterStore = calls();
        // 全新实例 + 不同扫描代次：落盘键不含代次，应该照样命中
        var svcB = new FolderPurposeService(null, new RecognitionStore(path));
        var id9 = new FolderId(dir.FullPath, 999);
        var res2 = svcB.RecognizeAsync(dir, id9, 0, "mystery-folder", allowAi: true,
            provider: Provider(), model: "m", sendFullPath: false, configSignature: cfg,
            ct: CancellationToken.None).GetAwaiter().GetResult();
        Check("新实例跨代次读回同一结论，且没有发请求",
            calls() == afterStore && res2.HasConclusion && res2.PurposeName == "神秘目录",
            res2.PurposeName);
        Check("结论挂到当前代次的标识上", res2.Id.ScanGeneration == 999, res2.Id.ScanGeneration.ToString());
        Check("新实例的 AI 预算计数仍为 0（恢复不算一次识别）", svcB.AiRequestsUsed == 0,
            svcB.AiRequestsUsed.ToString());
        Check("来源仍如实标为 AI（不冒充本地/用户）", res2.Source == PurposeSource.Ai);
        Check("待确认标记被保留", res2.NeedsConfirm);

        // 重扫只清内存，不该丢掉落盘结论
        svcA.ResetForScan();
        var res3 = svcA.RecognizeAsync(dir, id1, 0, "mystery-folder", allowAi: true,
            provider: Provider(), model: "m", sendFullPath: false, configSignature: cfg,
            ct: CancellationToken.None).GetAwaiter().GetResult();
        Check("重扫（ResetForScan）后仍能从落盘恢复，不再重问",
            calls() == afterStore && res3.HasConclusion && res3.PurposeName == "神秘目录");
        AiClient.Handler = null;
    }

    // ---------------- 5. 证据变化失效 ----------------

    static void ChangedEvidenceInvalidationTests()
    {
        Section("证据变化 ⇒ 旧结论失效（不会贴到已经变了的目录上）");

        string path = StorePath("evidence");
        var req = ItemReq(@"C:\changed");
        string cfg = "cfg-evidence";

        var store = new RecognitionStore(path);
        long v1 = ItemAiPrompt.VersionOf(req, cfg);
        store.PutItem(RecognitionKey.Item(req.ScopeKey, v1), v1, ItemResult("旧结论"));

        var same = new ItemAiService(new RecognitionStore(path));
        Check("内容没变 ⇒ 命中",
            same.TryGetRestored(req, cfg)?.Purpose == "旧结论");

        var changed = ItemReq(@"C:\changed", size: 4096);
        long v2 = ItemAiPrompt.VersionOf(changed, cfg);
        Check("版本随内容指纹变化", v1 != v2);
        Check("内容变了 ⇒ 不再命中旧结论",
            new ItemAiService(new RecognitionStore(path)).TryGetRestored(changed, cfg) == null);
        Check("配置签名变了 ⇒ 不再命中旧结论",
            new ItemAiService(new RecognitionStore(path)).TryGetRestored(req, "cfg-other") == null);

        // 用途侧同理
        var dir = Folder(@"D:\evidence-folder", "x.dat");
        var id = new FolderId(dir.FullPath, 3);
        var sum = FolderPurposeRules.Summarize(dir, id, 0, "evidence-folder");
        var storeP = new RecognitionStore(StorePath("evidence-purpose"));
        storeP.PutPurpose(FolderPurposeService.PurposeKey(sum, cfg),
            new FolderPurposeResult(id, "旧用途", "旧类别", "旧依据", PurposeSource.Ai, true, FolderKind.Concrete));
        var svcP = new FolderPurposeService(null, new RecognitionStore(StorePath("evidence-purpose")));
        Check("用途：内容没变 ⇒ 命中", svcP.TryGetCached(id, sum, cfg)?.PurposeName == "旧用途");
        var sumChanged = sum with { Size = sum.Size + 8 * 1024 * 1024 };
        Check("用途：内容变了 ⇒ 不再命中旧用途",
            new FolderPurposeService(null, new RecognitionStore(StorePath("evidence-purpose")))
                .TryGetCached(id, sumChanged, cfg) == null);
    }

    // ---------------- 6. 损坏容忍 ----------------

    static void CorruptionToleranceTests()
    {
        Section("损坏容忍：坏文件当空库，坏记录只跳过自己");

        string path = StorePath("corrupt");
        File.WriteAllText(path, "{ this is not json at all ");
        var store = new RecognitionStore(path);
        Check("非法 JSON 不抛异常且当空库", store.Count == 0, store.Count.ToString());

        long v = 11;
        store.PutItem(RecognitionKey.Item(@"C:\after-corrupt", v), v, ItemResult("恢复"));
        var back = new RecognitionStore(path);
        Check("坏文件被下一次写入原子覆盖，数据可读回",
            back.TryGetItem(RecognitionKey.Item(@"C:\after-corrupt", v), out var r) && r.Purpose == "恢复",
            r?.Purpose ?? "(missing)");

        // 结构正确、但混了一条坏记录：好记录要留下，坏记录只跳过
        string path2 = StorePath("mixed");
        File.WriteAllText(path2,
            "{\"Version\":1,\"Entries\":["
            + "{\"Kind\":\"item\",\"Key\":\"k-good\",\"Purpose\":\"good\"},"
            + "{\"Kind\":\"bogus\",\"Key\":\"k-bad\",\"Purpose\":\"bad\"},"
            + "{\"Kind\":\"purpose\",\"Key\":\"k-empty\",\"Purpose\":\"\"}"
            + "]}");
        var mixed = new RecognitionStore(path2);
        Check("坏记录只跳过自己，好记录保留", mixed.Count == 1, mixed.Count.ToString());
        Check("保留的正是好记录", mixed.TryGetItem("k-good", out var good) && good.Purpose == "good");

        // 未知 schema 版本：整份当损坏，不解析
        string path3 = StorePath("schema");
        File.WriteAllText(path3, "{\"Version\":999,\"Entries\":[{\"Kind\":\"item\",\"Key\":\"k\",\"Purpose\":\"x\"}]}");
        Check("未知 schema 版本当空库", new RecognitionStore(path3).Count == 0);

        // 超长文本字段：写入时被截断，不会撑大文件
        string path4 = StorePath("longfield");
        var longStore = new RecognitionStore(path4);
        long v4 = 21;
        longStore.PutItem(RecognitionKey.Item(@"C:\long", v4), v4, ItemResult(new string('甲', 5000)));
        var lr = new RecognitionStore(path4);
        Check("单字段长度被限制在上限内",
            lr.TryGetItem(RecognitionKey.Item(@"C:\long", v4), out var longRes)
            && longRes.Purpose.Length <= RecognitionStore.MaxFieldChars,
            lr.TryGetItem(RecognitionKey.Item(@"C:\long", v4), out var l2) ? l2.Purpose.Length.ToString() : "0");
    }

    // ---------------- 7. 无密钥 / 无原始回复 ----------------

    static void NoSecretsTests()
    {
        Section("无密钥：落盘前统一脱敏，原始模型回复根本不落盘");

        const string secret = "sk-abcdefghijklmnopqrstuvwxyz0123456789";
        const string rawSecret = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        string path = StorePath("secrets");
        long v = 31;

        var store = new RecognitionStore(path);
        store.PutItem(RecognitionKey.Item(@"C:\secret", v), v,
            new ItemAiResult(ItemAiSuggestion.CanConsider, "用途里有 " + secret, "影响 " + secret,
                "依据 apiKey=" + secret, "缺少", rawSecret, false, 0, 0, 0, "", v));

        string text = File.ReadAllText(path);
        Check("落盘文件不含 sk- 密钥", !text.Contains(secret, StringComparison.Ordinal));
        Check("落盘文件不含模型原始回复里的密钥", !text.Contains(rawSecret, StringComparison.Ordinal));
        Check("落盘文件里没有密钥自检能认出的内容", !LogRedactor.LooksSecret(text));

        var back = new RecognitionStore(path);
        Check("恢复出的文本也不再带密钥",
            back.TryGetItem(RecognitionKey.Item(@"C:\secret", v), out var r)
            && !r.Purpose.Contains(secret, StringComparison.Ordinal)
            && !r.Impact.Contains(secret, StringComparison.Ordinal)
            && !r.Basis.Contains(secret, StringComparison.Ordinal),
            "restored");
        Check("原始模型回复字段恢复后为空（从不落盘）", r.Raw.Length == 0);
    }

    // ---------------- 8. 来源隔离 / 无清理能力 ----------------

    static void OrganizeIsolationTests()
    {
        Section("来源隔离：整理树拿不到清理树的落盘结果，也没有勾选/删除能力");

        string path = StorePath("isolation");
        string cfg = "cfg-iso";
        var cleanReq = ItemReq(@"C:\Same", src: ItemAiSource.Clean);
        var orgReq = ItemReq(@"C:\Same", src: ItemAiSource.Organize);

        long vClean = ItemAiPrompt.VersionOf(cleanReq, cfg);
        var store = new RecognitionStore(path);
        store.PutItem(RecognitionKey.Item(cleanReq.ScopeKey, vClean), vClean, ItemResult("清理结论"));

        var svc = new ItemAiService(new RecognitionStore(path));
        Check("清理来源可命中", svc.TryGetRestored(cleanReq, cfg)?.Purpose == "清理结论");
        Check("整理来源不会命中清理来源的落盘结果（键含来源版本）",
            svc.TryGetRestored(orgReq, cfg) == null);

        // 就算把恢复结果硬塞给整理视图，也不能出现勾选 / 定位清理明细能力
        var items = new List<CleanItem>
        {
            new() { Name = "Same", FullPath = @"C:\Same", Size = 10, Reason = "r", CanDelete = true,
                    Risk = CleanRisk.Safe, Purpose = CleanPurpose.Other },
        };
        var verdict = AiVerdict.Build(items, "Same", null);
        Check("本地判定本身确实可勾选（对照组）", verdict.CanSelect);

        var orgView = new ItemAiView { ScopeKey = @"C:\Same", Source = ItemAiSource.Organize };
        orgView.Result = svc.TryGetRestored(cleanReq, cfg);
        orgView.Verdict = verdict;   // 硬塞：能力仍必须被来源挡住
        Check("整理视图有恢复结果 + 可勾选判定，仍然不能勾选", !orgView.CanSelect);
        Check("整理视图不提供清理明细定位", !orgView.CanViewFiles);

        // 恢复载体本身不能携带清理条目 / 选择 / 删除字段
        Check("ItemAiResult 不含 CleanItem / 选择 / 删除字段",
            !typeof(ItemAiResult).GetProperties().Any(p =>
                p.PropertyType == typeof(CleanItem)
                || p.Name.Contains("Select", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("CleanItem", StringComparison.Ordinal)));
        Check("RecognitionStore 的公开 API 不返回 CleanItem",
            !typeof(RecognitionStore).GetMethods().Any(m => m.ReturnType == typeof(CleanItem)
                || m.GetParameters().Any(p => p.ParameterType == typeof(CleanItem))));
        Check("FolderPurposeResult 不含选择 / 删除字段",
            !typeof(FolderPurposeResult).GetProperties().Any(p =>
                p.Name.Contains("Select", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)));

        // 落盘 JSON 里不能出现判定 / 选择 / 删除授权这些字样
        string saved = File.ReadAllText(path);
        Check("落盘内容不含 verdict/selected/delete 授权字段",
            !saved.Contains("Verdict", StringComparison.OrdinalIgnoreCase)
            && !saved.Contains("CanSelect", StringComparison.OrdinalIgnoreCase)
            && !saved.Contains("CanDelete", StringComparison.OrdinalIgnoreCase)
            && !saved.Contains("Selected", StringComparison.OrdinalIgnoreCase));
        Check("落盘内容不含原始模型回复",
            !saved.Contains("RAW-MODEL-REPLY", StringComparison.Ordinal));
    }

    // ---------------- 9. 界面接线（partial 真被编译并真的调用） ----------------

    static void WiringHookTests()
    {
        Section("界面接线：MainWindow.RecognitionPersistence 能编译，钩子真能把结论贴出来");

        const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
        var win = typeof(AiDiskCleaner.MainWindow);

        Check("构造钩子 InitRecognitionPersistence() 存在",
            win.GetMethod("InitRecognitionPersistence", P) != null);
        Check("重建恢复钩子 RestoreRecognizedFolders() 存在且返回条数",
            win.GetMethod("RestoreRecognizedFolders", P)?.ReturnType == typeof(int));
        Check("逐项行恢复钩子签名正确",
            win.GetMethod("TryRestoreItemAi", P)?.GetParameters() is { Length: 2 } ip
            && ip[0].ParameterType == typeof(ItemAiView) && ip[1].ParameterType == typeof(ItemAiRequest));
        Check("侧栏恢复钩子签名正确",
            win.GetMethod("TryRestorePurpose", P)?.GetParameters() is { Length: 1 } pp
            && pp[0].ParameterType == typeof(FileEntry));

        // 模拟「上次启动留下了一份落盘结果」，再打开一次应用
        string cfg = "cfg";
        var req = ItemReq(@"C:\wired-item");
        long v = ItemAiPrompt.VersionOf(req, cfg);
        var dir = Folder(@"D:\wired-purpose", "a.dat");
        var sum = FolderPurposeRules.Summarize(dir, new FolderId(dir.FullPath, 1), 0, dir.FullPath);

        var seed = new RecognitionStore(RecognitionStore.DefaultPath);
        seed.PutItem(RecognitionKey.Item(req.ScopeKey, v), v, ItemResult("上次的用途"));
        seed.PutPurpose(FolderPurposeService.PurposeKey(sum, cfg),
            new FolderPurposeResult(new FolderId(dir.FullPath, 1), "上次的目录用途", "类别", "依据",
                PurposeSource.Ai, true, FolderKind.Concrete));

        int calls = 0;
        AiClient.Handler = (_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new AiReply { Text = "" });
        };
        try
        {
            var window = new AiDiskCleaner.MainWindow();
            win.GetMethod("InitRecognitionPersistence", P)!.Invoke(window, null);
            var store = win.GetField("_recognitionStore", P)?.GetValue(window) as RecognitionStore;
            Check("构造钩子建立并接上了落盘缓存", store != null && store.Count >= 2,
                store?.Count.ToString() ?? "(null)");

            var view = new ItemAiView { ScopeKey = req.ScopeKey };
            bool itemOk = (bool)win.GetMethod("TryRestoreItemAi", P)!
                .Invoke(window, new object[] { view, req })!;
            Check("逐项钩子把上次结论贴进视图（状态 Done）",
                itemOk && view.Result?.Purpose == "上次的用途" && view.Status == ItemAiStatus.Done,
                view.Result?.Purpose ?? "(null)");

            var purpose = win.GetMethod("TryRestorePurpose", P)!
                .Invoke(window, new object[] { dir }) as FolderPurposeResult;
            Check("侧栏钩子把上次目录用途贴出来",
                purpose?.PurposeName == "上次的目录用途", purpose?.PurposeName ?? "(null)");

            var list = (List<OrganizeNode>)win.GetField("_organizeAll", P)!.GetValue(window)!;
            list.Add(new OrganizeNode(dir, new FolderId(dir.FullPath, 1), 0, dir.FullPath));
            int restored = (int)win.GetMethod("RestoreRecognizedFolders", P)!.Invoke(window, null)!;
            Check("重建钩子把落盘结论贴回尚未有结论的对象",
                restored == 1 && list[0].HasConclusion,
                $"{restored}/{list[0].PurposeName}");

            Check("整个恢复过程一次请求都没发（纯离线）", calls == 0, calls.ToString());
        }
        finally
        {
            AiClient.Handler = null;
            try { seed.Clear(); } catch { /* 清理失败不影响结论 */ }
        }
    }
}
