using System.IO;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

namespace ItemAiIsolationCheck;

/// <summary>
/// 逐项 AI 的**来源 / 能力隔离**离线检查。覆盖四件事：
/// <list type="number">
/// <item>整理树的结果绝不携带清理动作能力（不能勾选、不能定位清理明细），即使被硬塞进同一个结论；</item>
/// <item>清理页的一个文件与整理页的一个文件夹**同路径**时，请求登记键不同，取消/移除互不误伤；</item>
/// <item>同路径的清理/整理请求不共享模型缓存（来源进缓存版本）；</item>
/// <item>「有用途/影响但建议档位认不出来」仍然算有用，不丢成「没有可用结果」；</item>
/// <item>用户主动取消与超时被正确区分（不拿「是否正在扫描」当取消原因）。</item>
/// </list>
/// 复用了 SafetyCheck 的离线桩面（Loc / AiClient / App），不联网、不碰真实用户数据。
/// 运行：dotnet run --project tools/ItemAiIsolationCheck
/// </summary>
public static class Program
{
    static int _pass, _fail;
    static readonly List<string> Failures = new();

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

    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 干净配置目录：不碰用户真实配置，也不写用户日志
        string cfg = Path.Combine(Path.GetTempPath(), "dashao-itemai-iso-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cfg);
        Environment.SetEnvironmentVariable("DASHAOHUO_CONFIG_DIR", cfg);

        try
        {
            SourceCapabilityTests();
            IsolationKeyTests();
            RunningRegistryTests();
            CacheIsolationTests();
            UsabilityContractTests();
            CancelStatusTests();
            WindowWiringTests();
        }
        finally
        {
            try { Directory.Delete(cfg, true); } catch { /* 清理失败不影响结论 */ }
        }

        Console.WriteLine();
        Console.WriteLine($"PASS {_pass}   FAIL {_fail}");
        foreach (var f in Failures) Console.WriteLine("  - " + f);
        return _fail == 0 ? 0 : 1;
    }

    static CleanItem Cleanable(string path) => new()
    {
        Name = Path.GetFileName(path),
        FullPath = path,
        Size = 100,
        Reason = "matched local rule",
        CanDelete = true,
        Risk = CleanRisk.Safe,
        Purpose = CleanPurpose.Other,
    };

    // ---------------- 1. 来源 → 能力 ----------------

    static void SourceCapabilityTests()
    {
        Section("来源隔离：整理结果不携带清理动作能力");

        var items = new List<CleanItem> { Cleanable(@"C:\Same") };
        var verdict = AiVerdict.Build(items, "Same", null);

        Check("本地结论本身确实给了可勾选集合", verdict.CanSelect && verdict.SelectableItems.Count == 1,
            $"canSelect={verdict.CanSelect} items={verdict.SelectableItems.Count}");

        var cleanView = new ItemAiView { ScopeKey = @"C:\Same", Source = ItemAiSource.Clean };
        cleanView.Verdict = verdict;
        Check("清理树视图：结论与「选择这些文件」照常可用", cleanView.CanSelect);
        Check("清理树视图：可定位清理明细", cleanView.CanViewFiles);

        var orgView = new ItemAiView { ScopeKey = @"C:\Same", Source = ItemAiSource.Organize };
        orgView.Verdict = verdict;   // 硬塞同一个可勾选结论：能力也必须被来源挡住
        Check("整理树视图：即使拿到同一个可勾选结论，也绝不暴露勾选能力", !orgView.CanSelect);
        Check("整理树视图：不提供清理明细定位", !orgView.CanViewFiles);
        Check("整理树视图：结论/用途展示仍然可用（隔离的是动作，不是信息）",
            orgView.HasVerdict && orgView.Headline.Length > 0 && orgView.Note.Length > 0,
            $"has={orgView.HasVerdict} headline={orgView.Headline}");

        var flip = new ItemAiView { ScopeKey = "k" };
        flip.Verdict = verdict;
        Check("默认来源=清理：既有调用方行为不变", flip.CanSelect);
        flip.Source = ItemAiSource.Organize;
        Check("运行时标成整理后，勾选能力立即收回", !flip.CanSelect);
        flip.Source = ItemAiSource.Clean;
        Check("改回清理后能力恢复（属性通知闭环）", flip.CanSelect);
    }

    // ---------------- 2. 登记键 ----------------

    static void IsolationKeyTests()
    {
        Section("同路径清理/整理：请求登记键必须不同");

        var clean = new ItemAiView { ScopeKey = @"C:\Same" };
        var org = new ItemAiView { ScopeKey = @"C:\Same", Source = ItemAiSource.Organize };

        Check("同路径的清理/整理登记键不同", clean.IsolationKey != org.IsolationKey,
            clean.IsolationKey + " vs " + org.IsolationKey);
        Check("清理树的键保持原样（不破坏既有位置查找）", clean.IsolationKey == @"C:\Same");
        Check("整理树的键带来源前缀且仍含路径",
            org.IsolationKey.EndsWith(@"C:\Same", StringComparison.Ordinal)
            && org.IsolationKey.Contains("organize", StringComparison.Ordinal));
    }

    // ---------------- 3. 在飞请求登记表 ----------------

    static void RunningRegistryTests()
    {
        Section("在飞请求登记：晚到的旧请求不能删掉新请求的取消源");

        var reg = new ItemAiRunningRegistry();
        var oldCts = new CancellationTokenSource();
        var newCts = new CancellationTokenSource();

        reg.Add("k", oldCts);
        reg.Add("k", newCts);       // 新请求顶掉旧登记（模拟同键先后两次请求）
        Check("旧请求结束时不得移除新请求的登记", !reg.RemoveIfCurrent("k", oldCts));
        Check("新登记仍在，仍可被取消", reg.TryGet("k", out var cur) && ReferenceEquals(cur, newCts));
        Check("只有自己结束时才移除", reg.RemoveIfCurrent("k", newCts) && reg.Count == 0);

        var cleanKey = new ItemAiView { ScopeKey = @"C:\Same" }.IsolationKey;
        var orgKey = new ItemAiView { ScopeKey = @"C:\Same", Source = ItemAiSource.Organize }.IsolationKey;
        var cleanCts = new CancellationTokenSource();
        var orgCts = new CancellationTokenSource();
        reg.Add(cleanKey, cleanCts);
        reg.Add(orgKey, orgCts);
        Check("同路径两个来源各自登记（互不顶替）", reg.Count == 2);

        // 取消其中一个来源，不影响另一个
        if (reg.TryGet(cleanKey, out var onlyClean) && onlyClean != null) onlyClean.Cancel();
        Check("取消清理请求不影响整理请求",
            cleanCts.IsCancellationRequested && !orgCts.IsCancellationRequested);

        reg.CancelAll();
        Check("「全部停止」把两个来源都取消，且登记留待各自按身份移除",
            cleanCts.IsCancellationRequested && orgCts.IsCancellationRequested && reg.Count == 2);

        reg.Clear();
        Check("清空登记表", reg.Count == 0);
    }

    // ---------------- 4. 缓存隔离 ----------------

    static void CacheIsolationTests()
    {
        Section("模型缓存：同路径不同来源不共享");

        var baseReq = new ItemAiRequest(
            ScopeKey: @"C:\Same", IsFolder: true, Path: @"C:\Same", Label: "Same",
            Size: 100, Modified: new DateTime(2025, 1, 1), Kind: "folder",
            LocalReason: "rule", FolderSummary: new[] { "1 KB a" },
            FolderSummaryShown: 1, FolderChildTotal: 3);

        var cleanReq = baseReq;
        var orgReq = baseReq with { Source = ItemAiSource.Organize };

        Check("同路径不同来源的缓存版本不同",
            ItemAiPrompt.VersionOf(cleanReq, "cfg") != ItemAiPrompt.VersionOf(orgReq, "cfg"));
        Check("同路径同来源的缓存版本相同（缓存仍有效）",
            ItemAiPrompt.VersionOf(cleanReq, "cfg") == ItemAiPrompt.VersionOf(baseReq, "cfg"));

        int calls = 0;
        AiClient.Handler = (_, _, _) =>
        {
            calls++;
            return Task.FromResult(new AiReply { Text = "SUGGEST: 可考虑清理\nPURPOSE: cached" });
        };

        var svc = new ItemAiService();
        var provider = new AiProviderCfg { Id = "t", BaseUrl = "http://localhost", Models = { "m" } };

        var first = svc.AnalyzeAsync(cleanReq, provider, "m", false, "cfg", CancellationToken.None)
            .GetAwaiter().GetResult();
        Check("清理请求真的发了一次（走桩，不联网）", calls == 1 && first != null);

        int afterClean = calls;
        var second = svc.AnalyzeAsync(orgReq, provider, "m", false, "cfg", CancellationToken.None)
            .GetAwaiter().GetResult();
        Check("整理请求没有命中清理缓存（各自发一次，共两次）",
            afterClean == 1 && calls == 2 && second != null, $"calls={calls}");

        var third = svc.AnalyzeAsync(cleanReq, provider, "m", false, "cfg", CancellationToken.None)
            .GetAwaiter().GetResult();
        Check("同来源再次请求命中缓存", calls == 2 && third != null && third.FromCache, $"calls={calls}");

        AiClient.Handler = null;
    }

    // ---------------- 5. 可用性合同（保护有效用途/影响） ----------------

    static void UsabilityContractTests()
    {
        Section("可用性合同：Unknown 档位不得丢掉有效用途/影响");

        var barrenUnknown = new ItemAiResult(ItemAiSuggestion.Unknown,
            "", "", "", "", "raw", false, 0, 0, 0, "", 1);
        var purposeImpactUnknown = barrenUnknown with
        {
            Purpose = "临时文件", Impact = "删了下次运行会重建", Basis = "命中本地规则",
        };
        var impactOnlyUnknown = barrenUnknown with { Impact = "删了会重新下载" };
        var barrenKeep = barrenUnknown with { Suggestion = ItemAiSuggestion.SuggestKeep };

        Check("空内容 + Unknown：才是「没有可用结论」", !ItemAiPrompt.IsUsable(barrenUnknown));
        Check("合同：空内容确实是 Barren", barrenUnknown.Barren);
        Check("有用途/影响 + Unknown：仍算有用（不被丢成没有结果）",
            !purposeImpactUnknown.Barren && ItemAiPrompt.IsUsable(purposeImpactUnknown));
        Check("只有影响 + Unknown：仍算有用", ItemAiPrompt.IsUsable(impactOnlyUnknown));
        Check("只有档位、没有解释：也算有用（至少有可读档位）", ItemAiPrompt.IsUsable(barrenKeep));
        Check("null 不算有用", !ItemAiPrompt.IsUsable(null));

        // 端到端：模型给出认不出的档位词，但用途/影响有效 —— 解析后必须保留
        AiClient.Handler = (_, _, _) =>
            Task.FromResult(new AiReply { Text = "SUGGEST: 可以安全删除\nPURPOSE: 临时文件\nIMPACT: 删了会重建" });
        var svc = new ItemAiService();
        var provider = new AiProviderCfg { Id = "t", BaseUrl = "http://localhost", Models = { "m" } };
        var req = new ItemAiRequest(@"C:\U", true, @"C:\U", "U", 10, default, "folder",
            "rule", Array.Empty<string>(), 0, 0);
        var parsed = svc.AnalyzeAsync(req, provider, "m", false, "cfg-usable", CancellationToken.None)
            .GetAwaiter().GetResult();
        Check("认不出的档位降级成 Unknown（不放行「可以安全删除」）",
            parsed != null && parsed.Suggestion == ItemAiSuggestion.Unknown,
            parsed?.Suggestion.ToString());
        Check("但用途/影响/依据被完整保留",
            parsed != null && parsed.Purpose == "临时文件" && parsed.Impact == "删了会重建"
            && parsed.Basis.Length == 0 && ItemAiPrompt.IsUsable(parsed),
            parsed?.Purpose);
        AiClient.Handler = null;
    }

    // ---------------- 6. 取消 / 超时的归属 ----------------

    /// <summary>
    /// 行为用例：逐项请求抛 OperationCanceledException 之后，界面该记 Canceled 还是 Timeout。
    ///
    /// 两种取消来自不同的取消源：
    /// <list type="bullet">
    /// <item>用户取消 = 窗口持有的**外层 cts** 被 Cancel（点「取消这一项」/「全部停止」）；</item>
    /// <item>超时 = <see cref="ItemAiService.Timeout"/> 到点时，取消的是
    ///       ItemAiService 里 <c>CreateLinkedTokenSource(ct)</c> 派生出的 timeoutCts，
    ///       **外层 cts 不会被取消**。</item>
    /// </list>
    /// 「是否正在扫描」不属于取消原因：扫描用另一条取消链，既不会取消逐项请求，
    /// 也不能说明这一项为什么停。判据里混进 _scanning，用户取消就会被误报成超时。
    /// </summary>
    static void CancelStatusTests()
    {
        Section("取消/超时归属：用户取消是 Canceled，链接超时源触发才是 Timeout");

        // 判据只接受「外层（用户）取消源」。若有人再把「是否正在扫描」加回参数，
        // 这里就先红：扫描状态本来就不属于取消原因。
        var resolveParams = typeof(ItemAiCancelStatus).GetMethod("Resolve")?.GetParameters();
        Check("判定函数只接受「外层取消源」一个输入（扫描状态不得参与）",
            resolveParams is { Length: 1 }
            && resolveParams[0].ParameterType == typeof(CancellationToken),
            resolveParams is null ? "找不到 Resolve" : resolveParams.Length.ToString());

        // 场景一：用户主动取消（外层 cts.Cancel），**且此刻正在扫描**。
        // 现状（把 _scanning 掺进判据）会在这里落进 Timeout —— 本次修的就是它。
        using (var scanningCts = new CancellationTokenSource())
        {
            scanningCts.Cancel();
            var st = ItemAiCancelStatus.Resolve(scanningCts.Token);
            Check("用户取消 + 正在扫描 ⇒ Canceled（不得标成超时）",
                st == ItemAiStatus.Canceled, st.ToString());
        }

        // 场景二：用户主动取消，且当前没有在扫描。
        using (var idleCts = new CancellationTokenSource())
        {
            idleCts.Cancel();
            var st = ItemAiCancelStatus.Resolve(idleCts.Token);
            Check("用户取消 + 未扫描 ⇒ Canceled", st == ItemAiStatus.Canceled, st.ToString());
        }

        // 场景三：真超时。用**真实的链接取消源**复现 ItemAiService 的超时路径：
        // timeoutCts 触发，外层 cts 保持未取消。
        using (var outerCts = new CancellationTokenSource())
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(outerCts.Token))
        {
            timeoutCts.CancelAfter(TimeSpan.Zero);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!timeoutCts.IsCancellationRequested && sw.ElapsedMilliseconds < 2000)
                Thread.Sleep(5);

            Check("超时源确实触发了（链接源已取消）", timeoutCts.IsCancellationRequested);
            Check("超时不会反向取消外层（用户）取消源", !outerCts.IsCancellationRequested);

            var st = ItemAiCancelStatus.Resolve(outerCts.Token);
            Check("真超时（外层未取消）⇒ Timeout，即使正在扫描", st == ItemAiStatus.Timeout, st.ToString());
        }

        // 边界：两个源都没取消却抛了 OperationCanceledException（例如底层 HttpClient
        // 自己取消）—— 不是用户取消，归 Timeout，不能反过来记成 Canceled。
        using (var noneCts = new CancellationTokenSource())
        {
            var st = ItemAiCancelStatus.Resolve(noneCts.Token);
            Check("外层未取消 ⇒ Timeout（不冤枉成用户取消）", st == ItemAiStatus.Timeout, st.ToString());
        }
    }

    // ---------------- 7. 窗口接线（离线无法编译主工程，做接线守卫） ----------------

    static void WindowWiringTests()
    {
        Section("窗口接线守卫（主工程依赖缺失的 BCU 子模块，离线编不了，故查源接线）");

        string cs = ReadSource("src/AiDiskCleaner/MainWindow.xaml.cs");
        if (cs.Length == 0)
        {
            Check("找得到 MainWindow.xaml.cs", false, "仓库根没定位到");
            return;
        }

        Check("清理/整理来源在 DescribeAiTarget 里显式标注",
            cs.Contains("item.Ai.Source = ItemAiSource.Clean", StringComparison.Ordinal)
            && cs.Contains("loc.Ai.Source = ItemAiSource.Clean", StringComparison.Ordinal)
            && cs.Contains("node.Ai.Source = ItemAiSource.Organize", StringComparison.Ordinal)
            && cs.Contains("Source = ItemAiSource.Organize", StringComparison.Ordinal));
        Check("RunItemAiAsync 只对清理树认领清理条目",
            cs.Contains("bool cleanSource = view.IsCleanSource;", StringComparison.Ordinal)
            && cs.Contains("cleanSource ? ResolveLocationNode(view.ScopeKey) : null", StringComparison.Ordinal));
        Check("请求登记/取消/移除都走来源隔离键",
            cs.Contains("_itemAiRunning.Add(view.IsolationKey, cts)", StringComparison.Ordinal)
            && cs.Contains("_itemAiRunning.TryGet(view.IsolationKey", StringComparison.Ordinal)
            && cs.Contains("_itemAiRunning.RemoveIfCurrent(view.IsolationKey, cts)", StringComparison.Ordinal));
        Check("不再按裸 ScopeKey 移除在飞请求（修误删新请求）",
            !cs.Contains("_itemAiRunning.Remove(view.ScopeKey)", StringComparison.Ordinal));
        Check("可用性判定走合同函数（不因 Unknown 丢用途/影响）",
            cs.Contains("bool usable = ItemAiPrompt.IsUsable(result);", StringComparison.Ordinal));
        Check("取消/超时判定走 ItemAiCancelStatus，且只看外层（用户）取消源",
            cs.Contains("ItemAiCancelStatus.Resolve(cts.Token)", StringComparison.Ordinal));
        Check("不再把 _scanning 混进取消原因",
            !cs.Contains("cts.IsCancellationRequested && !_scanning", StringComparison.Ordinal));
        Check("清理树的本地结论路径仍在（未误伤清理页）",
            cs.Contains("AiVerdict.Build(items", StringComparison.Ordinal)
            && cs.Contains("case OrganizeNode node:", StringComparison.Ordinal));
        Check("没有新增自动/批量请求入口",
            !cs.Contains("AnalyzeCurrentCategory", StringComparison.Ordinal));
    }

    /// <summary>从 bin 往上找到仓库根（PROGRESS.md 所在），读源文件。</summary>
    static string ReadSource(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PROGRESS.md"))) dir = dir.Parent;
        if (dir == null) return "";
        string full = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? File.ReadAllText(full) : "";
    }
}
