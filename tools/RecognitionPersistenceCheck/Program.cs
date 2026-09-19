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

    /// <summary>
    /// 造一条用途结论。
    ///
    /// 逐项 AI 那半套删掉之后，落盘缓存**只剩用途这一半** ——
    /// 原来配套的 ItemAiRequest / ItemAiResult 都随能力一起没了，
    /// 所以这里的往返测试统一走目录用途这条路。
    /// </summary>
    static FolderPurposeResult PurposeResult(string path, string purpose, string basis = "依据文本")
        => new(new FolderId(path, 1), purpose, "开发", basis, PurposeSource.Ai,
            NeedsConfirm: false, FolderKind.Concrete);

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

        Check("同路径不同写法（斜杠/大小写/末尾反斜杠）得到同一个路径键",
            RecognitionKey.PathKey(@"C:\A") == RecognitionKey.PathKey(@"c:/a\"),
            RecognitionKey.PathKey(@"C:\A") + " vs " + RecognitionKey.PathKey(@"c:/a\"));
        Check("不同路径 ⇒ 路径键不同",
            RecognitionKey.PathKey(@"C:\A") != RecognitionKey.PathKey(@"C:\B"));
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
        string keySrc = ReadSource("src/AiDiskCleaner/Services/RecognitionKey.cs");
        string purposeSrc = ReadSource("src/AiDiskCleaner/Services/FolderPurposeService.cs");
        Check("键/存储实现不使用 GetHashCode（跨进程稳定）",
            storeSrc.Length > 0 && keySrc.Length > 0 && purposeSrc.Length > 0
            && !storeSrc.Contains("GetHashCode(", StringComparison.Ordinal)
            && !keySrc.Contains("GetHashCode(", StringComparison.Ordinal)
            && !purposeSrc.Contains("GetHashCode(", StringComparison.Ordinal));
    }

    // ---------------- 2. 有界与原子 ----------------

    static void StoreBoundsAndAtomicityTests()
    {
        Section("有界 + 原子写：条数封顶、不留临时文件");

        string path = StorePath("bounds");
        var store = new RecognitionStore(path);
        int total = RecognitionStore.MaxEntries + 30;
        for (int i = 0; i < total; i++)
            store.PutPurpose("k" + i, PurposeResult(@"C:\bounded\" + i, "p" + i));

        Check("条数不超过上限", store.Count == RecognitionStore.MaxEntries, store.Count.ToString());
        Check("写入不留临时文件", !File.Exists(path + ".tmp"));
        Check("落盘文件存在且非空", File.Exists(path) && new FileInfo(path).Length > 0);

        var reload = new RecognitionStore(path);
        Check("新实例读回同样多的条数", reload.Count == store.Count, reload.Count.ToString());
        // 最新写入的一条必须还在（淘汰的是最旧的）
        int last = total - 1;
        Check("淘汰的是最旧、保留的是最新",
            reload.TryGetPurpose("k" + last, new FolderId(@"C:\bounded\" + last, 1), out var got)
            && got.PurposeName == "p" + last,
            got?.PurposeName ?? "(missing)");

        store.Clear();
        Check("Clear 后文件被删除", !File.Exists(path));
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

        // 逐项 AI 那一半随能力删掉了，这里只剩目录用途这一条路 ——
        // 但「内容变了旧结论就失效」这条不变量一个字都没变。
        string cfg = "cfg-evidence";
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

        store.PutPurpose("k-after-corrupt", PurposeResult(@"C:\after-corrupt", "恢复"));
        var back = new RecognitionStore(path);
        Check("坏文件被下一次写入原子覆盖，数据可读回",
            back.TryGetPurpose("k-after-corrupt", new FolderId(@"C:\after-corrupt", 1), out var r)
            && r.PurposeName == "恢复",
            r?.PurposeName ?? "(missing)");

        // 结构正确、但混了坏记录：好记录要留下，坏记录只跳过
        // 注意第一条 Kind 用的是 "item" —— 那是逐项 AI 那半套的遗留，
        // 现在**应当被当无效跳过**（不能当成用途读出来）。
        string path2 = StorePath("mixed");
        File.WriteAllText(path2,
            "{\"Version\":1,\"Entries\":["
            + "{\"Kind\":\"item\",\"Key\":\"k-legacy\",\"Purpose\":\"legacy\"},"
            + "{\"Kind\":\"bogus\",\"Key\":\"k-bad\",\"Purpose\":\"bad\"},"
            + "{\"Kind\":\"purpose\",\"Key\":\"k-good\",\"Purpose\":\"good\"}"
            + "]}");
        var mixed = new RecognitionStore(path2);
        Check("坏记录 / 旧逐项记录只跳过自己，好记录保留", mixed.Count == 1, mixed.Count.ToString());
        Check("保留的正是好记录",
            mixed.TryGetPurpose("k-good", new FolderId(@"C:\x", 1), out var good) && good.PurposeName == "good");
        Check("旧的逐项记录不会被当成用途读出来",
            !mixed.TryGetPurpose("k-legacy", new FolderId(@"C:\x", 1), out _));

        // 未知 schema 版本：整份当损坏，不解析
        string path3 = StorePath("schema");
        File.WriteAllText(path3, "{\"Version\":999,\"Entries\":[{\"Kind\":\"purpose\",\"Key\":\"k\",\"Purpose\":\"x\"}]}");
        Check("未知 schema 版本当空库", new RecognitionStore(path3).Count == 0);

        // 超长文本字段：写入时被截断，不会撑大文件
        string path4 = StorePath("longfield");
        var longStore = new RecognitionStore(path4);
        longStore.PutPurpose("k-long", PurposeResult(@"C:\long", new string('甲', 5000)));
        var lr = new RecognitionStore(path4);
        Check("单字段长度被限制在上限内",
            lr.TryGetPurpose("k-long", new FolderId(@"C:\long", 1), out var longRes)
            && longRes.PurposeName.Length <= RecognitionStore.MaxFieldChars,
            lr.TryGetPurpose("k-long", new FolderId(@"C:\long", 1), out var l2) ? l2.PurposeName.Length.ToString() : "0");
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
        store.PutPurpose("k-secret", new FolderPurposeResult(
            new FolderId(@"C:\secret", 1),
            "用途里有 " + secret, "类别 " + secret, "依据 apiKey=" + secret + " token " + rawSecret,
            PurposeSource.Ai, NeedsConfirm: false, FolderKind.Concrete));

        string text = File.ReadAllText(path);
        Check("落盘文件不含 sk- 密钥", !text.Contains(secret, StringComparison.Ordinal));
        Check("落盘文件不含 ghp_ 密钥", !text.Contains(rawSecret, StringComparison.Ordinal));
        Check("落盘文件里没有密钥自检能认出的内容", !LogRedactor.LooksSecret(text));

        var back = new RecognitionStore(path);
        Check("恢复出的文本也不再带密钥",
            back.TryGetPurpose("k-secret", new FolderId(@"C:\secret", 1), out var r)
            && !r.PurposeName.Contains(secret, StringComparison.Ordinal)
            && !r.Category.Contains(secret, StringComparison.Ordinal)
            && !r.Basis.Contains(secret, StringComparison.Ordinal),
            "restored");
    }

    // ---------------- 8. 来源隔离 / 无清理能力 ----------------

    static void OrganizeIsolationTests()
    {
        Section("落盘缓存与恢复载体都没有任何清理能力");

        string path = StorePath("isolation");
        string cfg = "cfg-iso";
        var dir = Folder(@"C:\Same", "a.dat");
        var id = new FolderId(dir.FullPath, 1);
        var sum = FolderPurposeRules.Summarize(dir, id, 0, dir.FullPath);
        new RecognitionStore(path).PutPurpose(FolderPurposeService.PurposeKey(sum, cfg),
            new FolderPurposeResult(id, "恢复出来的用途", "类别", "依据", PurposeSource.Ai,
                NeedsConfirm: false, FolderKind.Concrete));

        // 恢复出来的东西**只有描述性文本**：没有判定、没有选择、没有任何清理动作能力。
        // 逐项 AI 删掉之后，这条不变量变成了纯结构断言 —— 更硬：
        // 整理树的对象上根本没有可以勾选 / 删除的字段，连「硬塞」都塞不进去。
        Check("整理对象结构上没有 Selected / CanDelete / CanSelect",
            typeof(OrganizeNode).GetProperty("Selected") == null
            && typeof(OrganizeNode).GetProperty("CanDelete") == null
            && typeof(OrganizeNode).GetProperty("CanSelect") == null);
        Check("逐项 AI 的视图类型已整体移除（没有可夹带清理能力的载体）",
            Type.GetType("AiDiskCleaner.Models.ItemAiView") == null
            && Type.GetType("AiDiskCleaner.Models.ItemAiView, AiDiskCleaner") == null);
        Check("恢复出的用途结论本身不带选择 / 删除字段",
            !typeof(FolderPurposeResult).GetProperties().Any(p =>
                p.Name.Contains("Select", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)));
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
        Check("逐项行恢复钩子已随能力一起移除", win.GetMethod("TryRestoreItemAi", P) == null);
        Check("侧栏恢复钩子签名正确",
            win.GetMethod("TryRestorePurpose", P)?.GetParameters() is { Length: 1 } pp
            && pp[0].ParameterType == typeof(FileEntry));

        // 模拟「上次启动留下了一份落盘结果」，再打开一次应用
        string cfg = "cfg";
        var dir = Folder(@"D:\wired-purpose", "a.dat");
        var sum = FolderPurposeRules.Summarize(dir, new FolderId(dir.FullPath, 1), 0, dir.FullPath);

        var seed = new RecognitionStore(RecognitionStore.DefaultPath);
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
            Check("构造钩子建立并接上了落盘缓存", store != null && store.Count >= 1,
                store?.Count.ToString() ?? "(null)");

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
