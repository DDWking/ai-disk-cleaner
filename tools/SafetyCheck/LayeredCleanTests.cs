using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

namespace SafetyCheck;

/// <summary>
/// 「清理结果分层归类」的回归检查：分组正确性、删除安全边界、三态选择、
/// 分页/搜索状态保持，以及 30 万条量级的性能。
///
/// 全部离线、纯模型层，不碰 WPF，也不碰真实用户文件。
/// </summary>
public static class LayeredCleanTests
{
    /// <summary>带可选诊断信息的断言回调（对齐 Program.cs 的 Check）。</summary>
    public delegate void CheckFn(string name, bool ok, string? detail = null);

    public static void Run(CheckFn check, Action<string> section)
    {
        GroupingCorrectness(check, section);
        LocationAnchoring(check, section);
        RiskSplit(check, section);
        SelectionSafety(check, section);
        DuplicateGrouping(check, section);
        PagerTests(check, section);
        ScaleTests(check, section);
        CancellationTests(check, section);
        NavigationStateTests(check, section);
        RiskOwnershipTests(check, section);
        SectionContainerTests(check, section);
        PreflightTests(check, section);
        AiDiagnosticsTests(check, section);
        AiEndToEndTests(check, section);
        PathBoundaryTests(check, section);
        DefaultSelectionTests(check, section);
        AiCannotSelectTests(check, section);
        ItemAiTests(check, section);
        GuardedPathTests(check, section);
        LocationNamingTests(check, section);
        ShellRevealTests(check, section);
        AiVerdictTests(check, section);
        FolderPurposeTests(check, section);
        FolderOrganizeTests(check, section);
    }

    // ------------------------------------------------------------------ 文件夹用途识别

    /// <summary>造一棵内存目录树（不碰磁盘）。</summary>
    static FileEntry DirNode(string name, string path, long size = 0, FileEntry? parent = null)
    {
        var e = new FileEntry { Name = name, FullPath = path, Kind = EntryKind.Directory, Size = size, Parent = parent };
        parent?.Children.Add(e);
        return e;
    }

    static FileEntry FileNode(string name, string path, long size, FileEntry parent)
    {
        var e = new FileEntry { Name = name, FullPath = path, Kind = EntryKind.File, Size = size, Parent = parent };
        parent.Children.Add(e);
        return e;
    }

    static void FolderPurposeTests(CheckFn check, Action<string> section)
    {
        section("文件夹用途：自适应识别、平台不吞内部游戏、预算与缓存");

        // ---- 1) 收纳目录：多个独立子目录 ⇒ Container，且应该继续看 ----
        var root = DirNode("Apps", @"D:\Apps", 100_000_000);
        for (int i = 0; i < 6; i++)
            DirNode("app" + i, $@"D:\Apps\app{i}", 10_000_000, root);
        var id = new FolderId(root.FullPath, 1);
        var sum = FolderPurposeRules.Summarize(root, id, 1, "D:\\Apps");
        var kind = FolderPurposeRules.ClassifyKind(root);
        check("多子目录的收纳目录被判为容器/混合",
            kind is FolderKind.Container or FolderKind.Mixed, kind.ToString());
        check("容器应该继续往下识别",
            FolderPurposeService.ShouldDescend(root, kind, 1));
        check("摘要含直接子目录数", sum.DirectFolderCount == 6, sum.DirectFolderCount.ToString());

        // ---- 2) 具体对象：认出软件就停，不逐个贴标签 ----
        var steam = DirNode("steamapps", @"C:\Program Files (x86)\Steam\steamapps", 5_000_000_000);
        DirNode("common", @"C:\Program Files (x86)\Steam\steamapps\common", 4_000_000_000, steam);
        var steamId = new FolderId(steam.FullPath, 1);
        var sSum = FolderPurposeRules.Summarize(steam, steamId, 1, steam.FullPath);
        var sRes = FolderPurposeRules.RecognizeLocally(steam, sSum);
        check("Steam 库被本地规则认成游戏库",
            sRes.PurposeName == Loc.PurposeGameLibrary, sRes.PurposeName);
        check("游戏库来源标为本地", sRes.Source == PurposeSource.Local, sRes.Source.ToString());
        check("认出平台后**仍然允许往里找游戏**",
            FolderPurposeRules.IsObjectContainer(steam.FullPath) &&
            FolderPurposeService.ShouldDescend(steam, sRes.Kind, 1));

        // ---- 3) 普通应用内部：认出具体对象后应停止深入 ----
        var gitDir = DirNode(".git", @"D:\proj\.git", 1_000_000);
        DirNode("objects", @"D:\proj\.git\objects", 900_000, gitDir);
        var gitKind = FolderPurposeRules.ClassifyKind(gitDir);
        check(".git 被判为具体对象", gitKind == FolderKind.Concrete, gitKind.ToString());
        var nm = DirNode("node_modules", @"D:\proj\\node_modules", 1);
        check("node_modules 也判为具体对象且不下钻",
            FolderPurposeRules.ClassifyKind(nm) == FolderKind.Concrete &&
            !FolderPurposeService.ShouldDescend(nm, FolderKind.Concrete, 1));
        check("具体对象默认不再往下逐个分类",
            !FolderPurposeService.ShouldDescend(gitDir, gitKind, 1));

        // ---- 4) 开发项目：只看结构标志 ----
        var proj = DirNode("proj", @"D:\proj", 2_000_000);
        FileNode(".git", @"D:\proj\.git", 10, proj);
        FileNode("app.sln", @"D:\proj\app.sln", 20, proj);
        var pSum = FolderPurposeRules.Summarize(proj, new FolderId(proj.FullPath, 1), 0, proj.FullPath);
        var pRes = FolderPurposeRules.RecognizeLocally(proj, pSum);
        check("有工程标志的目录认成开发项目",
            pRes.PurposeName == Loc.PurposeDevProject, pRes.PurposeName);
        check("开发项目依据写出命中的标志",
            pRes.Basis.Contains(".git") || pRes.Basis.Contains(".sln"), pRes.Basis);

        // ---- 5) 未知目录：不硬给结论，且允许待确认 ----
        var mystery = DirNode("mystery", @"D:\mystery", 3_000_000);
        FileNode("a.bin", @"D:\mystery\a.bin", 100, mystery);
        var mSum = FolderPurposeRules.Summarize(mystery, new FolderId(mystery.FullPath, 1), 0, mystery.FullPath);
        var mRes = FolderPurposeRules.RecognizeLocally(mystery, mSum);
        check("说不清的目录不给结论", !mRes.HasConclusion, mRes.PurposeName);
        check("没有结论时状态是未识别",
            mRes.State == PurposeState.Unrecognized, mRes.State.ToString());

        // ---- 6) 摘要与容量：停止分类不等于停止统计 ----
        var big = DirNode("big", @"D:\big", 123_456_789);
        for (int i = 0; i < 20; i++) FileNode($"f{i}.tmp", $@"D:\big\f{i}.tmp", 1000, big);
        var bSum = FolderPurposeRules.Summarize(big, new FolderId(big.FullPath, 1), 0, big.FullPath);
        check("摘要容量仍是整个子树的大小", bSum.Size == big.Size, bSum.Size.ToString());
        check("样本数量有上限", bSum.SampleCount <= FolderPurposeRules.MaxSamples, bSum.SampleCount.ToString());
        check("类型分布有上限", bSum.TypeMix.Count <= FolderPurposeRules.MaxTypeMix);
        check("超出上限时标记为截断", bSum.Truncated);

        // ---- 7) 稳定标识：路径 + 扫描代次，与显示名无关 ----
        var idA = new FolderId(@"D:\x\cache", 1);
        var idB = new FolderId(@"D:\x\cache", 2);
        check("代次不同 ⇒ 标识不同", idA != idB);
        check("同名不同路径 ⇒ 标识不同",
            new FolderId(@"D:\a\cache", 1) != new FolderId(@"D:\b\cache", 1));

        // ---- 8) 缓存：相同摘要命中；内容变了就失效 ----
        var svc = new FolderPurposeService();
        var cSum = FolderPurposeRules.Summarize(proj, new FolderId(proj.FullPath, 1), 0, proj.FullPath);
        check("未缓存时取不到", svc.TryGetCached(cSum.Id, cSum, "cfg") == null);
        svc.ResetForScan();
        check("重置后缓存为空", svc.CacheCount == 0 && svc.AiRequestsUsed == 0);

        // ---- 9) 用户纠正优先，且不被重扫清掉 ----
        var uid = new FolderId(@"D:\mine", 1);
        svc.SetUserCorrection(uid, "我的资料", "文档");
        check("用户纠正可读回", svc.TryGetUserCorrection(uid)?.PurposeName == "我的资料");
        check("来源标为「你确认的」",
            svc.TryGetUserCorrection(uid)?.Source == PurposeSource.User);
        svc.ResetForScan();
        check("重扫不清除用户纠正",
            svc.TryGetUserCorrection(uid)?.PurposeName == "我的资料");

        // ---- 10) AI 解析：只认约定格式，认不出就没有结论 ----
        var aiOk = FolderPurposeRules.ParseAi("PURPOSE: Steam 游戏库\nCATEGORY: 游戏\nBASIS: 有 steamapps", uid, FolderKind.Container);
        check("合规回复解析出结论", aiOk.PurposeName == "Steam 游戏库" && aiOk.Source == PurposeSource.Ai);
        check("AI 结论默认需要确认", aiOk.NeedsConfirm && aiOk.State == PurposeState.NeedsConfirm);
        check("空回复不产生结论",
            !FolderPurposeRules.ParseAi("", uid, FolderKind.Unknown).HasConclusion);
        check("答非所问不产生结论",
            !FolderPurposeRules.ParseAi("这个目录很安全可以删", uid, FolderKind.Unknown).HasConclusion);
        check("格式错误不产生结论",
            !FolderPurposeRules.ParseAi("{\"purpose\":\"x\"}", uid, FolderKind.Unknown).HasConclusion);

        // ---- 11) 来源不伪装 ----
        check("本地结论来源是本地", pRes.SourceText == Loc.PurposeFromLocal);
        check("AI 结论来源是 AI 推测", aiOk.SourceText == Loc.PurposeFromAi);
        check("没有结论时不显示来源", mRes.SourceText.Length == 0);
        check("没有结论时不能算已识别",
            mRes.State != PurposeState.Recognized);

        // ---- 12) 预算：输入长度受限、请求数有上限 ----
        string aiInput = FolderPurposeRules.BuildAiInput(bSum, false, FolderPurposeRules.MaxSamples);
        check("发给模型的输入有长度上限",
            aiInput.Length <= FolderPurposeService.MaxInputChars + 200, aiInput.Length.ToString());
        check("整次任务的请求数上限是有限值",
            FolderPurposeService.MaxAiRequests > 0 && FolderPurposeService.MaxAiRequests <= 100);
        check("探索深度上限是有限值",
            FolderPurposeService.MaxDepth > 0 && FolderPurposeService.MaxDepth <= 8);

        // ---- 13) 链接循环 / 重解析点不跟随 ----
        var link = DirNode("link", @"D:\loop\link", 100);
        link.IsReparsePoint = true;
        check("重解析点不继续往下", !FolderPurposeService.ShouldDescend(link, FolderKind.Container, 1));
        check("深度到上限就停",
            !FolderPurposeService.ShouldDescend(root, FolderKind.Container, FolderPurposeService.MaxDepth));

        // ---- 14) 用途识别绝不改清理相关字段 ----
        var item = Temp(@"C:\Windows\Temp\a.tmp", 100);
        var risk0 = item.Risk; var can0 = item.CanDelete; var sel0 = item.Selected;
        var _ = FolderPurposeRules.RecognizeLocally(proj, pSum);
        check("识别不修改 Risk/CanDelete/Selected",
            item.Risk == risk0 && item.CanDelete == can0 && item.Selected == sel0);
        check("识别结果里没有删除建议字段",
            !typeof(FolderPurposeResult).GetProperties().Any(p =>
                p.Name.Contains("Delete") || p.Name.Contains("Risk") || p.Name.Contains("Selected")));

        // ---- 15) 系统入口说明（不是清理结论，需要确认）----
        string[] entries = FolderPurposeRules.EntryPoints().ToArray();
        check("系统入口由系统路径解析得到（绝对路径）",
            entries.Length > 0 && entries.All(e => System.IO.Path.IsPathRooted(e)),
            string.Join(" | ", entries));
        if (entries.Length > 0)
        {
            var ep = DirNode("ProgramData", entries[0], 1);
            var eRes = FolderPurposeRules.RecognizeLocally(ep,
                FolderPurposeRules.Summarize(ep, new FolderId(ep.FullPath, 1), 0, ep.FullPath));
            check("系统入口标为需要确认",
                eRes.NeedsConfirm || !eRes.HasConclusion, eRes.State.ToString());
        }
    }

    // ------------------------------------------------------------------ 文件夹整理

    /// <summary>
    /// 「文件夹整理」工作区的回归检查：默认两层、自适应下钻、平台不吞内部游戏、
    /// 预算与去重、未知有限探索、缓存与用户纠正、状态不伪装、绝不参与删除/选择。
    /// 全部离线（不碰磁盘、不调模型）。
    /// </summary>
    static void FolderOrganizeTests(CheckFn check, Action<string> section)
    {
        section("文件夹整理：默认两层、自适应下钻、预算、平台游戏入口、状态不伪装");

        // ---- 1) 顶层对象：系统识别入口优先，重叠的只留最外层（去重） ----
        var root = DirNode("C:", @"C:\", 500_000_000_000);
        var pf = DirNode("Program Files", @"C:\Program Files", 60_000_000_000, root);
        DirNode("app1", @"C:\Program Files\app1", 20_000_000_000, pf);
        DirNode("app2", @"C:\Program Files\app2", 10_000_000_000, pf);
        DirNode("Windows", @"C:\Windows", 40_000_000_000, root);
        DirNode("Users", @"C:\Users", 100_000_000_000, root);

        var rs = FolderOrganize.BuildRoots(root);
        check("解析到系统识别入口（Program Files 在顶层对象里）",
            rs.Roots.Any(r => FolderOrganize.IsUnderOrEqual(r.FullPath, @"C:\Program Files")),
            string.Join(" | ", rs.Roots.Select(r => r.FullPath)));
        check("顶层对象里没有重复的祖先/后代对",
            !rs.Roots.Any(a => rs.Roots.Any(b => !ReferenceEquals(a, b)
                && FolderOrganize.IsUnderOrEqual(b.FullPath, a.FullPath))));
        check("盘符下的其它直接子目录也在（Windows / Users）",
            rs.Roots.Any(r => r.FullPath == @"C:\Windows") && rs.Roots.Any(r => r.FullPath == @"C:\Users"));
        check("顶层对象有上限（海量目录不会一次铺开）",
            FolderOrganize.MaxRoots > 0 && FolderOrganize.MaxRoots <= 200, FolderOrganize.MaxRoots.ToString());

        // ---- 2) 默认两层：收纳目录继续直接子目录，具体对象停止内部逐项分类 ----
        var appsRoot = DirNode("Apps", @"D:\Apps", 90_000_000);
        for (int i = 0; i < 6; i++)
            DirNode("app" + i, $@"D:\Apps\app{i}", 10_000_000, appsRoot);
        check("收纳目录默认继续铺一层子对象",
            FolderOrganize.ShouldAutoExpand(appsRoot, FolderPurposeRules.ClassifyKind(appsRoot), 0));
        check("到第二层就不再自动往下（默认两层）",
            !FolderOrganize.ShouldAutoExpand(appsRoot, FolderKind.Container, FolderOrganize.AutoLevels - 1));
        var concrete = DirNode("appX", @"D:\Apps\appX", 10_000_000);
        check("具体对象默认停止内部逐项分类",
            !FolderOrganize.ShouldAutoExpand(concrete, FolderKind.Concrete, 0));

        // ---- 3) 未知目录：有限探索到预算，看完还是未知就待确认 ----
        var mystery = DirNode("mystery", @"D:\mystery", 8_000_000);
        for (int i = 0; i < 20; i++) DirNode("sub" + i, $@"D:\mystery\sub{i}", 100_000, mystery);
        var probe = FolderOrganize.DirectChildDirs(mystery, FolderOrganize.UnknownProbeBudget);
        check("未知目录的探索有预算上限",
            FolderOrganize.UnknownProbeBudget > 0 && probe.Dirs.Count <= FolderOrganize.UnknownProbeBudget,
            probe.Dirs.Count.ToString());
        check("截断了就如实说明（不假装列全）",
            probe.Truncated && probe.Total == 20 && probe.Dirs.Count < probe.Total,
            $"shown={probe.Dirs.Count} total={probe.Total}");
        var mRes = FolderPurposeRules.RecognizeLocally(mystery,
            FolderPurposeRules.Summarize(mystery, new FolderId(mystery.FullPath, 1), 0, mystery.FullPath));
        check("探索完还是说不清就不给结论", !mRes.HasConclusion, mRes.PurposeName);
        check("整理页允许对未知目录多看一层（有限探索）",
            FolderOrganize.ShouldProbeUnknown(mystery, FolderKind.Unknown, 1));
        check("未知目录的探索也受深度上限约束",
            !FolderOrganize.ShouldProbeUnknown(mystery, FolderKind.Unknown, FolderPurposeService.MaxDepth));
        check("具体对象不做未知探索",
            !FolderOrganize.ShouldProbeUnknown(mystery, FolderKind.Concrete, 1));

        // ---- 4) 平台游戏库：认出平台后仍保留往里找游戏的入口；认不出就不猜 ----
        var steamLib = DirNode("steamapps", @"D:\Gameklll\steam\steamapps", 50_000_000_000);
        var common = DirNode("common", @"D:\Gameklll\steam\steamapps\common", 49_000_000_000, steamLib);
        DirNode("VolleyBall", @"D:\Gameklll\steam\steamapps\common\VolleyBall", 3_000_000_000, common);
        check("平台游戏库保留「里面有游戏」的入口",
            FolderOrganize.KeepsObjectEntries(steamLib)
            && FolderOrganize.ShouldAutoExpand(steamLib, FolderKind.Container, 0));
        var steamRes = FolderPurposeRules.RecognizeLocally(steamLib,
            FolderPurposeRules.Summarize(steamLib, new FolderId(steamLib.FullPath, 1), 0, steamLib.FullPath));
        check("游戏库结论来自本地规则且说清是游戏库",
            steamRes.Source == PurposeSource.Local && steamRes.PurposeName.Length > 0, steamRes.PurposeName);
        var plainGames = DirNode("games", @"D:\games", 9_000_000);
        for (int i = 0; i < 5; i++) DirNode("g" + i, $@"D:\games\g{i}", 1_000_000, plainGames);
        check("认不出是平台的目录不假装能列出里面的游戏",
            !FolderOrganize.KeepsObjectEntries(plainGames));

        // ---- 5) 开发项目 / 混合与未知：允许不硬给结论 ----
        var proj = DirNode("proj", @"D:\proj", 4_000_000);
        DirNode(".git", @"D:\proj\.git", 1_000_000, proj);
        FileNode("app.sln", @"D:\proj\app.sln", 20, proj);
        var projRes = FolderPurposeRules.RecognizeLocally(proj,
            FolderPurposeRules.Summarize(proj, new FolderId(proj.FullPath, 1), 0, proj.FullPath));
        check("工程结构标志（含 .git 目录）能认出开发项目",
            projRes.PurposeName == Loc.PurposeDevProject, projRes.PurposeName);
        var nodeModules = DirNode("node_modules", @"D:\proj\node_modules", 900_000);
        FileNode("package.json", @"D:\proj\node_modules\package.json", 10, nodeModules);
        check("已知内部目录不再继续下钻且说清是程序内部",
            FolderPurposeRules.ClassifyKind(nodeModules) == FolderKind.Concrete
            && !FolderPurposeService.ShouldDescend(nodeModules, FolderKind.Concrete, 1));

        // ---- 6) 容量统计与分类分离：停止分类不影响容量 ----
        var big = DirNode("big", @"D:\big", 123_456_789);
        FileNode("a.bin", @"D:\big\a.bin", 1000, big);
        var bigSum = FolderPurposeRules.Summarize(big, new FolderId(big.FullPath, 1), 0, big.FullPath);
        check("停止分类不影响容量统计", bigSum.Size == big.Size, bigSum.Size.ToString());
        check("子目录名样本也进摘要（只看名字，不读内容）",
            bigSum.SampleDirs.Count <= FolderPurposeRules.MaxSamples);

        // ---- 7) 系统入口路径：不按距盘符的固定层数截断 ----
        var deepEntries = new[] { @"C:\Users\bob\AppData\Local", @"C:\Users\bob\AppData\Roaming" };
        check("深层系统入口仍然被认成入口",
            FolderOrganize.IsEntryPoint(@"C:\Users\bob\AppData\Local", deepEntries));
        check("入口之上的祖先会顺着自动铺到（不受层数截断）",
            FolderOrganize.ShouldAutoFollowEntryPath(@"C:\Users\bob\AppData", 1, deepEntries)
            && FolderOrganize.ShouldAutoFollowEntryPath(@"C:\Users\bob", 0, deepEntries));
        check("入口自己不再自动往下钻",
            !FolderOrganize.ShouldAutoFollowEntryPath(@"C:\Users\bob\AppData\Local", 2, deepEntries));

        // ---- 8) 链接 / 重解析点：跳过并计数，绝不跟随 ----
        var withLink = DirNode("withlink", @"D:\withlink", 5_000_000);
        DirNode("real", @"D:\withlink\real", 1_000_000, withLink);
        var link = DirNode("loop", @"D:\withlink\loop", 100, withLink);
        link.IsReparsePoint = true;
        var kids = FolderOrganize.DirectChildDirs(withLink, 24);
        check("重解析点不进子对象列表，并且被计数",
            kids.Dirs.All(d => !d.IsReparsePoint) && kids.Skipped == 1, kids.Skipped.ToString());
        check("重解析点不继续下钻",
            !FolderOrganize.ShouldAutoExpand(link, FolderKind.Container, 0));

        // ---- 9) 缓存：同摘要命中；摘要变了就失效 ----
        var svc = new FolderPurposeService();
        var cRoot = DirNode("cache", @"D:\cache", 7_000_000);
        FileNode("x.tmp", @"D:\cache\x.tmp", 100, cRoot);
        var cId = new FolderId(cRoot.FullPath, 3);
        var cSum = FolderPurposeRules.Summarize(cRoot, cId, 0, cRoot.FullPath);
        check("未识别过时缓存里没有", svc.TryGetCached(cId, cSum, "cfg") == null);
        var local = FolderPurposeRules.RecognizeLocally(cRoot, cSum);
        check("判不出来的目录不会被硬塞一个用途",
            !local.HasConclusion || local.Source == PurposeSource.Local, local.PurposeName);

        // 摘要指纹：内容变了（大小/文件数/时间）缓存键就该变
        var cSum2 = FolderPurposeRules.Summarize(cRoot, cId, 0, cRoot.FullPath) with { Size = cSum.Size + 1 };
        check("摘要变化会让缓存键不同（不会拿旧结论糊弄）",
            svc.TryGetCached(cId, cSum2, "cfg") == null);

        // ---- 10) 用户纠正：按路径记，换代次 / 重扫都还在 ----
        var corrId = new FolderId(@"D:\mine", 1);
        svc.SetUserCorrection(corrId, "我的资料", "资料");
        svc.ResetForScan();
        check("重扫后用户纠正仍在",
            svc.TryGetUserCorrection(new FolderId(@"D:\mine", 99))?.PurposeName == "我的资料");
        check("用户纠正来源是「你确认的」",
            svc.TryGetUserCorrection(corrId)?.Source == PurposeSource.User);

        // ---- 11) AI 解析：说「未知」不算结论 ----
        var aiUnknown = FolderPurposeRules.ParseAi("PURPOSE: 未知\nCATEGORY: 未知\nBASIS: 资料不足",
            corrId, FolderKind.Unknown);
        check("模型回「未知」时没有结论（不冒充识别成功）", !aiUnknown.HasConclusion);
        check("模型回 unknown（英文）也不算结论",
            !FolderPurposeRules.ParseAi("PURPOSE: unknown", corrId, FolderKind.Unknown).HasConclusion);
        check("答非所问 / 空回复都不算结论",
            !FolderPurposeRules.ParseAi("", corrId, FolderKind.Unknown).HasConclusion
            && !FolderPurposeRules.ParseAi("这个可以删", corrId, FolderKind.Unknown).HasConclusion);

        // ---- 12) 状态不伪装：排队时不能显示成功结论 ----
        var stNode = new OrganizeNode(cRoot, cId, 0, "cache");
        check("没有结论时状态不是「已识别」",
            stNode.State == PurposeState.Unrecognized && !stNode.IsResolved);
        stNode.SetState(PurposeState.Queued);
        check("排队时显示的是排队，不是结论",
            stNode.State == PurposeState.Queued && stNode.PurposeText == Loc.PurposeQueued);
        stNode.SetState(PurposeState.Running);
        check("处理中显示的是处理中", stNode.PurposeText == Loc.PurposeRunning);
        stNode.ClearState();
        check("清掉流程态后落回未识别", stNode.State == PurposeState.Unrecognized);
        stNode.Apply(FolderPurposeRules.ParseAi("PURPOSE: 我的项目\nCATEGORY: 开发\nBASIS: 有源码",
            cId, FolderKind.Concrete));
        check("AI 结论来源标为 AI 且需要确认",
            stNode.Source == PurposeSource.Ai && stNode.NeedsConfirm
            && stNode.State == PurposeState.NeedsConfirm && stNode.IsPending);
        stNode.Apply(FolderPurposeResult.None(cId));
        check("空结论不会覆盖成「已识别」",
            !stNode.HasConclusion && stNode.State != PurposeState.Recognized);

        // ---- 13) 行数上限：海量目录不会把界面拖死 ----
        check("整页行数有上限", FolderOrganize.MaxRows > 0 && FolderOrganize.MaxRows <= 100_000,
            FolderOrganize.MaxRows.ToString());
        check("行数到顶就停自动铺开",
            FolderOrganize.RowBudgetReached(FolderOrganize.MaxRows)
            && !FolderOrganize.RowBudgetReached(FolderOrganize.MaxRows - 1));
        check("单个目录的直接子对象也有预算",
            FolderOrganize.ChildBudget > 0 && FolderOrganize.ChildBudget <= 200,
            FolderOrganize.ChildBudget.ToString());

        // ---- 14) 绝不参与删除 / 选择 / 提权 ----
        check("整理对象里没有删除/风险/选择字段",
            !typeof(OrganizeNode).GetProperties().Any(p =>
                p.Name.Contains("Delete") || p.Name.Contains("Risk") || p.Name.Contains("Selected")));
        check("整理服务里没有删除/移动/提权入口",
            !typeof(FolderOrganize).GetMethods().Any(m =>
                m.Name.Contains("Delete") || m.Name.Contains("Move") || m.Name.Contains("Elevate")
                || m.Name.Contains("RunAs") || m.Name.Contains("Select")));
        check("识别入口全部来自系统路径解析（可路径化）",
            typeof(FolderOrganize).GetMethod("BuildRoots") != null
            && typeof(FolderPurposeRules).GetMethod("EntryPoints") != null);

        // ---- 15) 用户纠正落盘 / 读回（用临时目录，不碰真实用户配置） ----
        string? oldDir = AppPaths.OverrideDirectory;
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "dashao-purpose-" + Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Directory.CreateDirectory(tmp);
            AppPaths.OverrideDirectory = tmp;
            var svc2 = new FolderPurposeService();
            svc2.SetUserCorrection(new FolderId(@"D:\saved", 5), "学习资料", "资料");
            svc2.SaveCorrections();
            var svc3 = new FolderPurposeService();
            check("未读盘前没有纠正", svc3.UserCorrectionCount == 0);
            svc3.LoadCorrections();
            check("落盘的纠正能读回来，跨重启仍优先",
                svc3.TryGetUserCorrection(new FolderId(@"D:\saved", 1))?.PurposeName == "学习资料");
        }
        catch (Exception ex)
        {
            check("用户纠正落盘/读回", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            AppPaths.OverrideDirectory = oldDir;
            try { System.IO.Directory.Delete(tmp, true); } catch { /* 清理失败不影响结论 */ }
        }

        // ---- 16) 对象与真实目录准确映射（不移动文件、不建虚拟树） ----
        var mapNode = new OrganizeNode(pf, new FolderId(pf.FullPath, 1), 0, "Program Files");
        check("对象路径就是真实目录路径",
            mapNode.FullPath == pf.FullPath && mapNode.Name == pf.Name);
        check("缩进只影响展示，不改路径", mapNode.IndentWidth >= 0);
    }

    // ------------------------------------------------------------------ AI 结论界面

    static void AiVerdictTests(CheckFn check, Action<string> section)
    {
        section("AI 结论：一句结论 + 一句说明 + 下一步，只有混合时才分组");

        // ---- 1) 内容统一的位置：只给一个结论，不强行拆成三张卡（§七） ----
        var uniform = new List<CleanItem>();
        for (int i = 0; i < 19; i++) uniform.Add(Temp($@"C:\U\w{i}.cab", 100L * 1024 * 1024));
        var v1 = AiVerdict.Build(uniform, "Windows 更新缓存", null);
        check("统一目录不分组", !v1.ShowBuckets && v1.Buckets.Count == 1, v1.Buckets.Count.ToString());
        check("结论写明确的项数", v1.Headline.Contains("19"), v1.Headline);
        check("说明里点出「这是什么」", v1.Note.Contains("Windows 更新缓存"), v1.Note);
        check("可直接清理的项数=19", v1.CleanableCount == 19, v1.CleanableCount.ToString());
        check("可以一键选择", v1.CanSelect);

        // ---- 2) 顶部数字与「选择这些文件」的范围必须一致（§五：不重复堆叠） ----
        check("可选集合就是可直接清理那部分",
            v1.SelectableItems.Count == v1.CleanableCount
            && v1.SelectableItems.All(AiVerdict.IsCleanable));
        check("可选集合的空间等于结论里的空间",
            v1.SelectableItems.Sum(x => x.Size) == v1.CleanableBytes);

        // ---- 3) 混合内容：三个用户能懂的分组（§三） ----
        var mixed = new List<CleanItem>
        {
            Temp(@"C:\M\a.tmp", 1000),            // 可直接清理
            TempConfirm(@"C:\M\b.tmp", 2000),     // 需要确认
            NoDeleteSafe(@"C:\M\c.tmp", 4000),    // 受保护 → 建议保留
        };
        var v2 = AiVerdict.Build(mixed, "混合目录", null);
        check("混合内容才分组", v2.ShowBuckets);
        check("分组名是普通用户的词",
            v2.Buckets.Select(b => b.Name).SequenceEqual(
                new[] { Loc.AiBucketCleanable, Loc.AiBucketReview, Loc.AiBucketKeep }),
            string.Join(" | ", v2.Buckets.Select(b => b.Name)));
        check("分组名不含技术词",
            v2.Buckets.All(b => !b.Name.Contains("本地") && !b.Name.Contains("资格")
                                && !b.Name.Contains("未标注") && !b.Name.Contains("拆分")),
            string.Join(" | ", v2.Buckets.Select(b => b.Name)));
        check("每组只报自己的数量与空间",
            v2.Buckets.All(b => b.StatText.Contains(b.Count.ToString())),
            string.Join(" | ", v2.Buckets.Select(b => b.StatText)));

        // ---- 4) 只有「可直接清理」能快捷选择（§四） ----
        var clean = v2.Buckets.Single(b => b.Kind == AiBucket.Cleanable);
        var rev = v2.Buckets.Single(b => b.Kind == AiBucket.Review);
        var keep = v2.Buckets.Single(b => b.Kind == AiBucket.Keep);
        check("可直接清理可一键选", clean.CanSelect);
        check("需要确认没有快捷选择", !rev.CanSelect);
        check("建议保留没有快捷选择", !keep.CanSelect);
        check("需要确认的项数正确", rev.Count == 1, rev.Count.ToString());
        check("受保护项进了建议保留", keep.Count == 1, keep.Count.ToString());

        // ---- 5) 受保护项永远不可能是可直接清理 ----
        check("受保护项不被判为可清理", !AiVerdict.IsCleanable(NoDeleteSafe(@"C:\P\x.tmp", 1)));
        check("保留档不被判为可清理", !AiVerdict.IsCleanable(Keep(@"C:\P\x.tmp", 1)));
        check("需确认档不被判为可清理", !AiVerdict.IsCleanable(TempConfirm(@"C:\P\x.tmp", 1)));
        check("可选集合里没有受保护项",
            v2.SelectableItems.All(x => x.CanDelete && x.Risk != CleanRisk.Keep));

        // ---- 6) 全受保护：不给安全结论 ----
        var allProtected = new List<CleanItem> { NoDeleteSafe(@"C:\X\a", 10), Keep(@"C:\X\b", 20) };
        var v3 = AiVerdict.Build(allProtected, "受保护位置", null);
        check("全受保护时没有可选集合", !v3.CanSelect && v3.SelectableItems.Count == 0);
        check("全受保护时不写「建议清理」", !v3.Headline.Contains("建议清理"), v3.Headline);

        // ---- 7) 只有需确认时如实说 ----
        var onlyConfirm = new List<CleanItem> { TempConfirm(@"C:\Y\a", 10) };
        var v4 = AiVerdict.Build(onlyConfirm, "未知用途", null);
        check("只有需确认时给出「需要确认」的结论，而不是「建议清理」",
            v4.Headline.Length > 0 && !v4.Headline.Contains("建议清理")
            && v4.Headline == Loc.AiHeadlineReview(1, FileEntry.FormatSize(10)),
            v4.Headline);
        check("只有需确认时不给一键选择", !v4.CanSelect);

        // ---- 8) 依据全部来自本地事实 ----
        check("依据里说明没有读取文件内容",
            v1.Why.Any(w => w.Contains("没有读取") || w.Contains("not read")),
            string.Join(" | ", v1.Why));
        check("依据里含本地事实", v1.Why.Count > 0);
        check("没有模型建议时如实说明",
            v1.Why.Any(w => w.Contains("本地判断") || w.Contains("no model")),
            string.Join(" | ", v1.Why));

        // ---- 9) 有模型结论时补上删除影响，但分类不变 ----
        var ai = new ItemAiResult(ItemAiSuggestion.CanConsider, "更新缓存", "删了下次更新会重新下载",
            "路径是 Windows 更新缓存", "", "raw", false, 0, 0, 0, "", 1);
        var v5 = AiVerdict.Build(uniform, "Windows 更新缓存", ai);
        check("说明补上了模型给的删除影响", v5.Note.Contains("重新下载"), v5.Note);
        check("模型不改变可清理项数", v5.CleanableCount == v1.CleanableCount);
        check("模型不改变可选集合", v5.SelectableItems.Count == v1.SelectableItems.Count);

        // ---- 10) 空列表不炸 ----
        var v0 = AiVerdict.Build(new List<CleanItem>(), "空", null);
        check("空位置返回空结论且不抛异常",
            v0.Headline.Length == 0 && v0.Buckets.Count == 0 && !v0.CanSelect);

        // ---- 11) 结论区不出现开发术语 ----
        foreach (var v in new[] { v1, v2, v3, v4 })
        {
            string all = v.Headline + " " + v.Note + " " + string.Join(" ", v.Buckets.Select(b => b.Name));
            check("结论区不含开发术语", 
                !all.Contains("本地规则") && !all.Contains("符合清理资格")
                && !all.Contains("候选空间") && !all.Contains("未标注")
                && !all.Contains("按组") && !all.Contains("token"),
                all);
        }

        // ---- 12) 文件级：单个条目也要给出非空结论（本轮修复的核心 bug） ----
        var one = new List<CleanItem> { Temp(@"C:\F\only.tmp", 5L * 1024 * 1024) };
        var vf = AiVerdict.Build(one, "临时文件", null);
        check("文件级结论非空", vf.Headline.Length > 0, vf.Headline);
        check("文件级说明非空", vf.Note.Length > 0, vf.Note);
        check("单个文件也算得出可考虑清理", vf.CleanableCount == 1, vf.CleanableCount.ToString());
        check("文件级不给空分组卡", !vf.ShowBuckets, vf.Buckets.Count.ToString());

        var oneProtected = new List<CleanItem> { NoDeleteSafe(@"C:\F\p.tmp", 10) };
        var vp = AiVerdict.Build(oneProtected, "受保护", null);
        check("文件级受保护时结论非空且不给选择",
            vp.Headline.Length > 0 && !vp.CanSelect, vp.Headline);

        // ---- 13) §五：不用绝对措辞，也不编造「没被占用」 ----
        check("分组名是「可考虑清理」而不是绝对说法",
            Loc.AiBucketCleanable.Contains("可考虑") || Loc.AiBucketCleanable.Contains("Could be"),
            Loc.AiBucketCleanable);
        check("说明不写「暂未发现正在使用」这种没检查过的结论",
            !v1.Note.Contains("暂未发现") && !v1.Note.Contains("not in use"), v1.Note);
        check("依据里如实说明没有做占用检查",
            v1.Why.Any(w => w.Contains("占用") || w.Contains("lock")),
            string.Join(" | ", v1.Why));
        check("说明里不出现「一定安全」这类保证",
            !v1.Note.Contains("安全") && !v1.Headline.Contains("安全"), v1.Headline + " / " + v1.Note);
    }



    // ------------------------------------------------------------------ 在资源管理器中打开

    static void ShellRevealTests(CheckFn check, Action<string> section)
    {
        section("在资源管理器中打开：结构化参数、路径形态与失败提示");

        var launched = new List<(string Exe, List<string> Args)>();
        var prev = ShellReveal.LaunchOverride;
        ShellReveal.LaunchOverride = (exe, args) =>
        {
            launched.Add((exe, args.ToList()));
            return true;
        };

        string baseDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-reveal-test");
        System.IO.Directory.CreateDirectory(baseDir);
        try
        {
            // 目录名带空格 + 中文 + 逗号：三种最容易踩的形态一起用
            string dir = System.IO.Path.Combine(baseDir, "中文 目录,带逗号");
            System.IO.Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, "文件 名,有逗号.txt");
            System.IO.File.WriteAllText(file, "x");

            // ---- 目录：打开它本身 ----
            launched.Clear();
            var r1 = ShellReveal.Reveal(dir);
            check("目录被识别为打开目录", r1.Kind == RevealKind.OpenedDirectory, r1.Kind.ToString());
            check("目录只用 1 个结构化参数（不拼命令行）",
                launched.Count == 1 && launched[0].Args.Count == 1, Describe(launched));
            check("目录参数是完整真实路径（未被截断/脱敏）",
                launched.Count == 1 && launched[0].Args[0] == dir, Describe(launched));
            check("目录参数保留了中文/空格/逗号",
                launched.Count == 1 && launched[0].Args[0].Contains("中文 目录,带逗号"), Describe(launched));

            // ---- 文件：打开所在文件夹并选中，不执行 ----
            launched.Clear();
            var r2 = ShellReveal.Reveal(file);
            check("文件被识别为「选中文件」", r2.Kind == RevealKind.SelectedFile, r2.Kind.ToString());
            check("文件只启动一次资源管理器", launched.Count == 1, Describe(launched));
            check("文件用 /select, 开关",
                launched.Count == 1 && launched[0].Args.Count == 1
                && launched[0].Args[0].StartsWith("/select,", StringComparison.Ordinal), Describe(launched));
            check("文件路径被引号包住（逗号路径也能正确定位）",
                launched.Count == 1 && launched[0].Args[0].Contains("\"" + file + "\""), Describe(launched));
            check("启动的是 explorer.exe，绝不执行文件本身",
                launched.Count == 1 && launched[0].Exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase),
                Describe(launched));

            // ---- 不存在 / 空路径：给可读原因，不猜父目录 ----
            launched.Clear();
            string gone = System.IO.Path.Combine(baseDir, "已经被删掉的文件夹");
            var r3 = ShellReveal.Reveal(gone);
            check("不存在的路径不启动任何进程", launched.Count == 0, Describe(launched));
            check("不存在的路径如实返回 NotFound", r3.Kind == RevealKind.NotFound, r3.Kind.ToString());
            check("不存在的路径有可读提示", r3.Message.Length > 0, r3.Message);

            launched.Clear();
            var r4 = ShellReveal.Reveal("   ");
            check("空路径不启动进程", launched.Count == 0 && r4.Kind == RevealKind.NoPath);

            // ---- 启动失败也要有可读结果，不抛异常 ----
            ShellReveal.LaunchOverride = (_, _) => false;
            var r5 = ShellReveal.Reveal(dir);
            check("启动失败返回 Failed 而不是抛异常", r5.Kind == RevealKind.Failed, r5.Kind.ToString());
            check("启动失败有可读提示", r5.Message.Length > 0, r5.Message);

            // ---- 带引号的路径（从界面复制来的）也能用 ----
            ShellReveal.LaunchOverride = (exe, args) =>
            {
                launched.Add((exe, args.ToList()));
                return true;
            };
            launched.Clear();
            var r6 = ShellReveal.Reveal("\"" + dir + "\"");
            check("路径两端多余引号会被去掉",
                r6.Ok && launched.Count == 1 && launched[0].Args[0] == dir, Describe(launched));
        }
        finally
        {
            ShellReveal.LaunchOverride = prev;
            try { System.IO.Directory.Delete(baseDir, true); } catch { }
        }

        static string Describe(List<(string Exe, List<string> Args)> l)
            => l.Count == 0 ? "(no launch)" : string.Join(" | ", l.Select(x => x.Exe + " " + string.Join(" ", x.Args)));
    }

    // ------------------------------------------------------------------ 保护与清理资格一致

    static void GuardedPathTests(CheckFn check, Action<string> section)
    {
        section("受保护项不得进入清理候选（显示 / 统计 / 全选 / 预检同一口径）");

        var root = Dir(@"C:\");

        bool Offerable(string path)
        {
            var f = File(path, 1024);
            f.Parent = root;
            return CleanRuleHelpers.CanOffer(f);
        }

        // ---- 1) 名称里写着「别删」的位置，必须真的拦住 ----
        // Windows Installer 缓存：删了程序修不好/卸不掉，微软不建议清理。
        check("Windows\\Installer 不再可勾选", !Offerable(@"C:\Windows\Installer\a.msi"));
        check("Windows\\Installer 深层也一样", !Offerable(@"C:\Windows\Installer\Cache\a.msi"));
        check("Windows\\WinSxS 仍被拦", !Offerable(@"C:\Windows\WinSxS\Temp\x"));
        check("Windows\\servicing 被拦", !Offerable(@"C:\Windows\Servicing\LCU\x"));
        check("System Volume Information 被拦", !Offerable(@"C:\System Volume Information\x"));
        check("WindowsApps 被拦", !Offerable(@"C:\Program Files\WindowsApps\x\y"));
        check("$Recycle.Bin 被拦", !Offerable(@"C:\$Recycle.Bin\S-1-5-21\x"));

        // ---- 2) 合法候选不能被误伤 ----
        check("Windows\\Temp 下的文件仍可候选", Offerable(@"C:\Windows\Temp\a.tmp"));
        check("Windows\\Logs 下的日志仍可候选", Offerable(@"C:\Windows\Logs\CBS\a.log"));
        check("用户临时目录仍可候选", Offerable(@"C:\Users\me\AppData\Local\Temp\a.tmp"));
        check("npm 缓存仍可候选", Offerable(@"C:\Users\me\AppData\Local\npm-cache\_cacache\x"));

        // ---- 3) 资格判据必须与删除时的保护规则同一口径 ----
        foreach (var p in new[]
                 {
                     @"C:\Windows\Installer\a.msi", @"C:\System Volume Information\x",
                     @"C:\Windows\Temp\a.tmp", @"C:\Windows\Logs\CBS\a.log",
                     @"C:\Users\me\AppData\Local\npm-cache\x",
                 })
        {
            var f = File(p, 10);
            f.Parent = root;
            bool blockedAtDelete = ProtectedPaths.Classify(p).Guard == PathGuard.Blocked;
            bool canOffer = CleanRuleHelpers.CanOffer(f);
            check($"资格与删除口径一致: {p}", canOffer == !blockedAtDelete,
                $"offer={canOffer} blockedAtDelete={blockedAtDelete}");
        }

        // ---- 4) 混合目录：只产出合格候选 ----
        var sink = new CaptureSink();
        var files = new List<FileEntry>();
        foreach (var p in new[]
                 {
                     @"C:\Windows\Temp\ok1.tmp", @"C:\Windows\Temp\ok2.tmp",
                     @"C:\Windows\Installer\bad.msi",
                 })
        {
            var f = File(p, 2048);
            f.Parent = root;
            files.Add(f);
        }
        new TempCacheRule().Evaluate(new CleanRuleContext
        {
            Root = root, Files = files, Dirs = Array.Empty<FileEntry>(), Ct = CancellationToken.None,
        }, sink);
        check("受保护项不会混进候选",
            sink.Hits.All(h => !h.Entry.FullPath.Contains("Installer")),
            string.Join(",", sink.Hits.Select(h => h.Entry.FullPath)));
        check("合格候选仍然产出", sink.Hits.Count >= 2, sink.Hits.Count.ToString());
    }

    // ------------------------------------------------------------------ 位置命名

    static void LocationNamingTests(CheckFn check, Action<string> section)
    {
        section("位置命名：只表达身份，不带保护措辞，不笼统叫「Windows 系统」");

        check("StripWarning 去掉（别删）",
            KnownPaths.StripWarning("Windows 系统（别删）") == "Windows 系统",
            KnownPaths.StripWarning("Windows 系统（别删）"));
        check("StripWarning 去掉（别乱删）",
            KnownPaths.StripWarning("Windows 安装缓存（别乱删）") == "Windows 安装缓存");
        check("StripWarning 对没有括号的标签不变",
            KnownPaths.StripWarning("npm 缓存") == "npm 缓存");
        check("HasWarning 能认出保护措辞",
            KnownPaths.HasWarning("Windows 系统（别删）") && !KnownPaths.HasWarning("Windows 更新下载"));

        // 深层位置用**真实文件夹名**，而不是笼统的「Windows 系统」
        var items = new List<CleanItem>
        {
            Temp(@"C:\Windows\Logs\CBS\a.log", 1000),
            Temp(@"C:\Windows\Logs\DISM\b.log", 2000),
        };
        var r = CleanGroupingService.Build(items);
        var locs0 = r.Purposes.SelectMany(p => p.Locations).ToList();
        var names = locs0.Select(l => l.DisplayName).ToList();
        check("不再笼统叫「Windows 系统」",
            names.All(n => !n.Contains("Windows 系统")), string.Join(",", names));
        check("位置名里不出现保护措辞",
            names.All(n => !KnownPaths.HasWarning(n)), string.Join(",", names));
        // 名字必须是**真实路径的最后一段**（大小写也要对），不是大写键
        check("位置名取自真实路径而非大写键",
            locs0.All(l =>
            {
                int i = l.Path.LastIndexOf('\\');
                string leaf = i >= 0 ? l.Path[(i + 1)..] : l.Path;
                return l.IsUnidentified ? l.DisplayName == leaf : true;
            }),
            string.Join(" | ", locs0.Select(l => $"{l.DisplayName} <- {l.Path}")));
        check("路径保留真实大小写（不是全大写）",
            locs0.All(l => l.Path != l.Path.ToUpperInvariant()),
            string.Join(" | ", locs0.Select(l => l.Path)));

        // 同名文件夹靠父路径区分
        var dup = new List<CleanItem>
        {
            Temp(@"D:\proj1\build\cache\a.bin", 100),
            Temp(@"D:\proj2\build\cache\b.bin", 100),
        };
        var rd = CleanGroupingService.Build(dup);
        var locs = rd.Purposes.SelectMany(p => p.Locations).ToList();
        var dnames = locs.Select(l => l.DisplayName).ToList();
        check("同名文件夹得到同名标题", dnames.Count(n => n == "cache") == 2, string.Join(",", dnames));
        var hints = locs.Select(l => l.ParentHint).ToList();
        check("父路径提示能区分同名文件夹", hints.Distinct().Count() == 2, string.Join(" | ", hints));
        check("父路径提示有长度上限（不会撑破行）",
            hints.All(h => h.Length <= 60), hints.OrderByDescending(h => h.Length).FirstOrDefault() ?? "");

        // 未选择时不该显示「已选 0 / N 项」
        var loc0 = locs.First();
        check("没选择时选择标记为空", loc0.SelectionBadge.Length == 0 && !loc0.HasSelectionBadge);
        check("第二行统计含父路径与文件数",
            loc0.RowSubText.Contains(loc0.ParentHint)
            && loc0.RowSubText.Any(char.IsDigit), loc0.RowSubText);
        check("认不出用途时第二行标「用途待确认」",
            !loc0.IsUnidentified || loc0.RowSubText.Contains(Loc.PurposeUnclear), loc0.RowSubText);

        foreach (var x in loc0.Items) x.Selected = true;
        loc0.SyncFromItems();
        check("有选择时显示已选数量", loc0.HasSelectionBadge && loc0.SelectionBadge.Length > 0,
            loc0.SelectionBadge);
        check("已选数量带量词不裸写比例",
            loc0.SelectionBadge.Contains('项') || loc0.SelectionBadge.Contains("selected"),
            loc0.SelectionBadge);
    }

    // ------------------------------------------------------------------ 逐项 AI

    static void ItemAiTests(CheckFn check, Action<string> section)
    {
        section("逐项 AI：范围、摘要上限、解析与缓存失效");

        var file = new ItemAiRequest("C:\\a.tmp", false, @"C:\Users\me\a.tmp", "a.tmp", 100,
            new DateTime(2024, 1, 1), "Temporary", "temp file",
            Array.Empty<string>(), 0, 0);
        var folder = new ItemAiRequest("dir|C:\\x", true, @"C:\x", "x", 500,
            default, "folder", "cache dir",
            new[] { "1 GB a", "2 GB b" }, 2, 900);

        // ---- 范围隔离：文件与文件夹不能算出同一个缓存键 ----
        check("文件与文件夹的缓存键不同",
            ItemAiPrompt.VersionOf(file, "cfg") != ItemAiPrompt.VersionOf(folder, "cfg"));
        check("不同路径的缓存键不同",
            ItemAiPrompt.VersionOf(file, "cfg") != ItemAiPrompt.VersionOf(file with { Path = @"C:\other.tmp" }, "cfg"));

        // ---- 缓存失效：元数据、配置、提示词版本任一变化都要过期 ----
        string v0 = ItemAiPrompt.VersionOf(file, "cfg").ToString();
        check("大小变了缓存键就变",
            v0 != ItemAiPrompt.VersionOf(file with { Size = 101 }, "cfg").ToString());
        check("修改时间变了缓存键就变",
            v0 != ItemAiPrompt.VersionOf(file with { Modified = new DateTime(2025, 1, 1) }, "cfg").ToString());
        check("本地规则变了缓存键就变",
            v0 != ItemAiPrompt.VersionOf(file with { LocalReason = "other" }, "cfg").ToString());
        check("配置变了缓存键就变",
            v0 != ItemAiPrompt.VersionOf(file, "cfg2").ToString());
        check("文件夹摘要变了缓存键就变",
            ItemAiPrompt.VersionOf(folder, "cfg") != ItemAiPrompt.VersionOf(folder with { FolderSummaryShown = 1 }, "cfg"));

        // ---- 摘要上限：不能声称看过全部 ----
        check("文件夹摘要上限是有界的", ItemAiPrompt.MaxFolderSummary > 0 && ItemAiPrompt.MaxFolderSummary <= 20);
        string folderPrompt = ItemAiPrompt.BuildUser(folder, sendFullPaths: false);
        check("提示词如实说明摘要只覆盖前几项",
            folderPrompt.Contains("2") && folderPrompt.Contains("900"), folderPrompt.Replace("\n", " | "));
        check("提示词不含文件内容（只有元数据）",
            !folderPrompt.Contains("content") && !folderPrompt.Contains("base64"));

        // ---- 脱敏：默认不发真实用户名 ----
        string p = ItemAiPrompt.BuildUser(file, sendFullPaths: false);
        check("默认发送脱敏路径", !p.Contains(@"\Users\me\"), p.Replace("\n", " | "));
        check("明确允许时才发完整路径",
            ItemAiPrompt.BuildUser(file, sendFullPaths: true).Contains(@"\Users\me\"));

        // ---- 解析：完整、缺字段、非法建议、空响应 ----
        var full = ItemAiPrompt.Parse(
            "SUGGEST: 可考虑清理\nPURPOSE: 临时文件\nIMPACT: 大概没影响\nBASIS: 路径是 Temp\nMISSING: 不知道谁在用",
            1, 1, 2, 0.5, "http");
        check("完整回复五段都解析出来",
            full.Suggestion == ItemAiSuggestion.CanConsider && full.Purpose == "临时文件"
            && full.Impact.Length > 0 && full.Basis.Length > 0 && full.Missing.Length > 0);

        var keep = ItemAiPrompt.Parse("SUGGEST: 建议保留\nPURPOSE: x", 1, 0, 0, 0, "");
        check("「建议保留」被正确识别", keep.Suggestion == ItemAiSuggestion.SuggestKeep);
        var confirm = ItemAiPrompt.Parse("SUGGEST: 需要确认\nPURPOSE: x", 1, 0, 0, 0, "");
        check("「需要确认」被正确识别", confirm.Suggestion == ItemAiSuggestion.NeedsConfirm);

        // 模型说「安全可以删」不在允许档位里 —— 必须降级，不能变成可删结论
        var risky = ItemAiPrompt.Parse("SUGGEST: 可以安全删除\nPURPOSE: x", 1, 0, 0, 0, "");
        check("非法/越权的建议降级为信息不足",
            risky.Suggestion == ItemAiSuggestion.Unknown, risky.Suggestion.ToString());
        var empty = ItemAiPrompt.Parse("", 1, 0, 0, 0, "");
        check("空响应不产生结论", empty.Suggestion == ItemAiSuggestion.Unknown && empty.Barren);
        var junk = ItemAiPrompt.Parse("Sure, your disk is fine!", 1, 0, 0, 0, "");
        check("答非所问不产生结论", junk.Suggestion == ItemAiSuggestion.Unknown && junk.Barren);

        // 长字段要截断，先给短结论
        var longNote = ItemAiPrompt.Parse("SUGGEST: 可考虑清理\nPURPOSE: " + new string('字', 500), 1, 0, 0, 0, "");
        check("字段长度被封顶", longNote.Purpose.Length <= ItemAiPrompt.MaxFieldLength,
            longNote.Purpose.Length.ToString());

        // ---- 视图状态：状态与按钮文案自洽 ----
        var view = new ItemAiView { ScopeKey = "k" };
        check("初始未分析", view.Status == ItemAiStatus.Idle && !view.HasResult && !view.IsBusy);
        view.Status = ItemAiStatus.Running;
        check("分析中算忙碌且不能再点", view.IsBusy && !view.CanAnalyze);
        view.Result = full;
        view.Status = ItemAiStatus.Done;
        check("有结果时可查看", view.HasResult && view.CanAnalyze);
        check("结果文案含建议档位", view.SuggestionText.Contains(Loc.AiSuggestCanConsider),
            view.SuggestionText);
        view.Status = ItemAiStatus.NoUseful;
        check("有回复但无可用时不算 Done", !view.HasResult || view.Status == ItemAiStatus.NoUseful);
        check("无可用结果时状态文案明确", view.StatusText == Loc.ItemAiNoUseful, view.StatusText);
    }

    // ------------------------------------------------------------------ 路径段边界匹配

    static void PathBoundaryTests(CheckFn check, Action<string> section)
    {
        section("路径签名必须整段匹配（防误判成缓存）");

        // ---- 1) 子串匹配时代会误命中的路径，现在都不能命中 ----
        check("\\trae 不再命中 \\traefik",
            AppSignatures.Match(@"C:\tools\traefik\config.yml") == null,
            AppSignatures.Match(@"C:\tools\traefik\config.yml")?.Sig.Name ?? "(null)");
        check("\\trae 不再命中 trae-notes",
            AppSignatures.Match(@"D:\docs\trae-notes\a.md") == null);
        check("\\npm-cache 不再命中 npm-cache-old",
            AppSignatures.Match(@"D:\backup\npm-cache-old\x.tgz") == null);
        check("\\go\\pkg\\mod 不再命中 pkg\\models",
            AppSignatures.Match(@"C:\go\pkg\models\a.go") == null);
        check("\\node_modules 不再命中 node_modules_backup",
            AppSignatures.Match(@"D:\x\node_modules_backup\a.js") == null);
        // 命中签名 ≠ 是缓存：`mycache` 不是 `cache` 段，所以子目录没命中，
        // 这条路径不该被当成可清理的缓存。（签名本身会命中 VS Code，风险交给规则定。）
        check("\\cache 子目录不再命中 mycache",
            !AppSignatures.IsSafeCache(@"C:\Users\me\AppData\Roaming\Code\mycache\x"));

        // ---- 2) 正常路径仍然要命中（不能把规则修死） ----
        check("真实 trae 缓存仍命中",
            AppSignatures.Match(@"C:\Users\me\AppData\Roaming\Trae\Cache\a")?.Sig.Name == "Trae 缓存",
            AppSignatures.Match(@"C:\Users\me\AppData\Roaming\Trae\Cache\a")?.Sig.Name ?? "(null)");
        check("真实 npm 缓存仍命中",
            AppSignatures.Match(@"C:\Users\me\AppData\Local\npm-cache\_cacache\a")?.Sig.Name == "npm",
            AppSignatures.Match(@"C:\Users\me\AppData\Local\npm-cache\_cacache\a")?.Sig.Name ?? "(null)");
        check("真实 node_modules 仍命中",
            AppSignatures.Match(@"D:\proj\node_modules\react\index.js")?.Sig.Name == "node_modules",
            AppSignatures.Match(@"D:\proj\node_modules\react\index.js")?.Sig.Name ?? "(null)");
        check("go\\pkg\\mod 本体仍命中",
            AppSignatures.Match(@"C:\Users\me\go\pkg\mod\github.com\x")?.Sig.Name == "Go modules");

        // ---- 3) npm：全局安装目录 ≠ 缓存 ----
        string globalNpm = @"C:\Users\me\AppData\Roaming\npm\node_modules\typescript\bin\tsc";
        var g = AppSignatures.Match(globalNpm);
        check("npm 全局安装目录单独成一条签名",
            g?.Sig.Name == "npm 全局工具", g?.Sig.Name ?? "(null)");
        check("npm 全局安装目录不再是 Safe",
            g?.Sig.Risk != SigRisk.Safe, g?.Sig.Risk.ToString() ?? "(null)");
        check("npm 全局安装目录映射为保留（不可删）",
            g != null && AppSignatures.ToCleanRisk(g.Value.Sig.Risk) == CleanRisk.Keep);
        check("npm 全局安装目录不再被当成安全缓存",
            !AppSignatures.IsSafeCache(globalNpm));
        check("roaming 下的 npm-cache 仍算缓存",
            AppSignatures.IsSafeCache(@"C:\Users\me\AppData\Roaming\npm-cache\_cacache\a"));
        check("local 下的 npm-cache 仍算缓存",
            AppSignatures.IsSafeCache(@"C:\Users\me\AppData\Local\npm-cache\_cacache\a"));
        check("npm 全局目录与缓存目录得到不同签名",
            AppSignatures.Match(globalNpm)?.Sig.Name
            != AppSignatures.Match(@"C:\Users\me\AppData\Local\npm-cache\x")?.Sig.Name);

        // ---- 4) pip：只认 cache，不把整个 pip 目录当缓存 ----
        check("pip\\cache 仍算缓存",
            AppSignatures.IsSafeCache(@"C:\Users\me\AppData\Local\pip\cache\wheels\a"));
        check("pip 目录本身不再算缓存",
            !AppSignatures.IsSafeCache(@"C:\Users\me\AppData\Local\pip\selfcheck.json"));

        // ---- 5) 用户数据仍然不能被当成缓存（回归上一阶段的修复） ----
        check("Chrome 用户数据仍不是缓存",
            !AppSignatures.IsSafeCache(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\History"));
        check("Chrome 缓存子目录仍是缓存",
            AppSignatures.IsSafeCache(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache\f_1"));
    }

    // ------------------------------------------------------------------ 默认不勾选

    static void DefaultSelectionTests(CheckFn check, Action<string> section)
    {
        section("新扫描默认不勾选任何内容");

        var sink = new CaptureSink();
        // 规则吃的是 FileEntry，不是 CleanItem —— 这里用真实的扫描条目形状。
        // 注意：必须挂上 Parent。`ProtectedPaths.IsProtectedEntry` 见到 Parent==null
        // 会当成盘符根而判为受保护（真实扫描里由 MFT/递归扫描负责挂父子关系）。
        var root = Dir(@"C:\");
        FileEntry F(string p, long size)
        {
            var f = File(p, size);
            f.Parent = root;
            return f;
        }
        var files = new List<FileEntry>
        {
            F(@"C:\Windows\Temp\a.tmp", 1000),
            F(@"C:\Users\me\AppData\Local\CrashDumps\app.dmp", 2000),
            F(@"C:\Users\me\AppData\Local\npm-cache\_cacache\x", 3000),
            F(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache\f_1", 4000),
        };
        var ctx = new CleanRuleContext
        {
            Root = root,
            Files = files,
            Dirs = Array.Empty<FileEntry>(),
            Ct = CancellationToken.None,
        };
        new TempCacheRule().Evaluate(ctx, sink);

        check("规则确实投出了候选（否则这条测试没意义）", sink.Hits.Count > 0,
            sink.Hits.Count.ToString());
        check("包括原来标 Safe 的候选",
            sink.Hits.Any(h => h.Risk == CleanRisk.Safe), "no Safe hit");
        check("所有候选默认都没有勾选",
            sink.Hits.All(h => !h.Selected),
            string.Join(",", sink.Hits.Select(h => $"{h.Risk}:{h.Selected}")));

        var items = sink.Hits.Select(h => CleanItemFactory.Create(h)).ToList();
        check("经工厂后依然没有勾选", items.All(x => !x.Selected));
        check("候选仍然可删（默认不勾选 ≠ 不允许）", items.Any(x => x.CanDelete));

        var layered = CleanGroupingService.Build(items);
        check("分层后默认已选数为 0", layered.SelectedItems.Count() == 0,
            layered.SelectedItems.Count().ToString());
        check("首页分区默认统计为 0 已选",
            CleanPurposeSection.Build(layered.Purposes).All(s => s.SelectedCount == 0));
    }

    /// <summary>把规则投出来的命中收集起来，供测试断言。</summary>
    sealed class CaptureSink : ICleanRuleSink
    {
        public List<CleanRuleHit> Hits { get; } = new();
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        public bool Add(CleanRuleHit hit)
        {
            string key = hit.Entry.FullPath ?? "";
            if (key.Length == 0 || !_seen.Add(key)) return false;
            Hits.Add(hit);
            return true;
        }

        public int Count(CleanRuleTarget target) => Hits.Count(h => h.Target == target);
        public void CountDuplicateGroup() { }
    }

    // ------------------------------------------------------------------ AI 不得改选择

    static void AiCannotSelectTests(CheckFn check, Action<string> section)
    {
        section("AI 不得修改选择（工具面已关闭）");

        // 这两个工具即便被调用，也必须什么都不做
        var host = new NullHost();
        string r1 = DiskAnalyst.Run("set_checked", "{\"paths\":[\"C:\\\\x\"],\"checked\":true}", host);
        string r2 = DiskAnalyst.Run("suggest", "{\"items\":[{\"path\":\"C:\\\\x\",\"note\":\"n\"}]}", host);
        check("set_checked 已被拒绝", r1.Contains("removed"), r1);
        check("suggest 已被拒绝", r2.Contains("removed"), r2);

        // 工具清单里不能再出现这两个会改状态的名字
        var tools = DiskAnalyst.Tools(AiProtocol.Completions);
        string json = System.Text.Json.JsonSerializer.Serialize(tools);
        check("工具清单不含 set_checked", !json.Contains("set_checked"));
        check("工具清单不含 suggest", !json.Contains("\"suggest\""));
        check("工具清单保留只读工具 search_clean", json.Contains("search_clean"));

        // 宿主接口本身也不该再暴露任何写入选择的方法
        var methods = typeof(IAnalystHost).GetMethods().Select(m => m.Name).ToList();
        check("IAnalystHost 不再有 OnChecksChanged", !methods.Contains("OnChecksChanged"));
        check("IAnalystHost 不再有 OnSuggest", !methods.Contains("OnSuggest"));
    }

    sealed class NullHost : IAnalystHost
    {
        public FileEntry? Root => null;
        public CleanReport? Report => null;
    }

    // ------------------------------------------------------------------ AI 端到端（模型用假响应）

    /// <summary>
    /// 用假响应驱动**真实的** AiCoordinator + AiNoteParser + 计数链路。
    ///
    /// 边界说明：这里没有联网、没有真实模型；被替换的只有「模型返回什么文本」这一步。
    /// 提交/解析/计数/脱敏/状态判定全部走真实代码，所以「为什么只应用了 15 条」
    /// 这类问题可以在离线确定性地复现与验证。
    /// </summary>
    static void AiEndToEndTests(CheckFn check, Action<string> section)
    {
        section("AI 端到端（假模型响应，真实解析与计数）");

        var saved = AiClient.Handler;
        try
        {
            var items = Enumerable.Range(0, 60)
                .Select(i => Temp($@"C:\Users\me\AppData\Local\t\{i:D2}.tmp", 1000 + i))
                .ToList();
            var provider = new AiProviderCfg { Id = "t", Name = "mock", BaseUrl = "http://127.0.0.1:1/v1" };

            // ---- 1) 模型完整回 60 条 ----
            string Full(ICollection<CleanItem> its) => string.Join("\n",
                its.Select(x => "GOTO " + PathRedactor.Redact(x.FullPath) + "\t这是第 " + x.Name + " 条说明"));
            AiClient.Handler = (req, _, _) =>
            {
                var list = items.ToList();
                return Task.FromResult(new AiReply { Text = Full(list) });
            };
            var r1 = new AiCoordinator()
                .ExplainAsync(items, provider, "m", sendFullPaths: false, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            check("全部合规时 60 条都应用", r1.Sent == 60 && r1.Applied == 60,
                $"sent={r1.Sent} applied={r1.Applied}");
            check("没有无说明的条目", r1.WithoutNote == 0, r1.WithoutNote.ToString());
            check("解析计数与提交数一致", r1.Parse.PathHits == 60, r1.Parse.PathHits.ToString());
            check("结果可用时不算 Unusable", r1.AnyApplied && !r1.Unusable);
            check("耗时字段有值", r1.SendMs >= 0 && r1.BuildMs >= 0);

            // ---- 2) 复现实测症状：只回 15 条 ----
            AiClient.Handler = (_, _, _) =>
                Task.FromResult(new AiReply { Text = Full(items.Take(15).ToList()) });
            var r2 = new AiCoordinator()
                .ExplainAsync(items, provider, "m", sendFullPaths: false, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            check("只回 15 条时提交数仍是 60", r2.Sent == 60, r2.Sent.ToString());
            check("只回 15 条时可用说明是 15", r2.Applied == 15, r2.Applied.ToString());
            check("算得出 45 条没有说明", r2.WithoutNote == 45, r2.WithoutNote.ToString());
            check("部分可用仍然算有结果", r2.AnyApplied);
            check("部分可用时报告提交/可用两个数", r2.Sent != r2.Applied);

            // ---- 3) 模型胡言乱语 → 不能用 ----
            AiClient.Handler = (_, _, _) =>
                Task.FromResult(new AiReply { Text = "Sure! Your disk looks fine overall." });
            var r3 = new AiCoordinator()
                .ExplainAsync(items, provider, "m", sendFullPaths: false, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            check("答非所问时不应用任何说明", r3.Applied == 0);
            check("答非所问标记为不可用", r3.Unusable);
            check("答非所问不算有结果", !r3.AnyApplied);

            // ---- 4) 空回复 → 也不能说成功 ----
            AiClient.Handler = (_, _, _) => Task.FromResult(new AiReply { Text = "" });
            var r4 = new AiCoordinator()
                .ExplainAsync(items, provider, "m", sendFullPaths: false, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            check("空回复零应用", r4.Applied == 0);
            check("空回复不算有结果", !r4.AnyApplied);

            // ---- 5) 超过单批上限 → 如实标记部分覆盖 ----
            var many = Enumerable.Range(0, 100)
                .Select(i => Temp($@"C:\Users\me\AppData\Local\t\{i:D3}.tmp", 1000 + i))
                .ToList();
            AiClient.Handler = (req, _, _) =>
            {
                var prompt = string.Join("\n", req.Turns.Select(t => t.Text));
                var lines = prompt.Split('\n')
                    .Where(l => l.Contains(".tmp"))
                    .Select(l => "GOTO " + l.TrimStart('-', ' ').Split(' ')[0] + "\t说明");
                return Task.FromResult(new AiReply { Text = string.Join("\n", lines) });
            };
            var r5 = new AiCoordinator()
                .ExplainAsync(many, provider, "m", sendFullPaths: false, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            check("超过上限时只提交单批上限条数", r5.Sent == AiCoordinator.MaxBatch, r5.Sent.ToString());
            check("被截掉的条数被如实记下", r5.SkippedByLimit == 100 - AiCoordinator.MaxBatch,
                r5.SkippedByLimit.ToString());
            check("部分覆盖被标记", r5.PartialCoverage);

            // ---- 6) 默认脱敏：发给模型的内容不含用户名 ----
            string sentPrompt = "";
            AiClient.Handler = (req, _, _) =>
            {
                sentPrompt = string.Join("\n", req.Turns.Select(t => t.Text));
                return Task.FromResult(new AiReply { Text = "" });
            };
            new AiCoordinator().ExplainAsync(items, provider, "m", sendFullPaths: false, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            check("默认发送脱敏路径", PathRedactor.IsRedacted(sentPrompt) && !sentPrompt.Contains("me\\AppData"));

            // ---- 7) 取消：协调器不吞掉取消，交给上层收尾 ----
            using var cts = new CancellationTokenSource();
            AiClient.Handler = async (_, _, ct) =>
            {
                await Task.Delay(50, ct);
                return new AiReply { Text = "" };
            };
            cts.Cancel();
            bool canceled = false;
            try
            {
                new AiCoordinator().ExplainAsync(items, provider, "m", false, null, cts.Token)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { canceled = true; }
            check("取消会向上抛出，不被当成成功", canceled);

            // ---- 8) AI 不碰风险/勾选/可删（端到端再确认一次） ----
            var snapshot = items.Select(x => (x.Risk, x.Selected, x.CanDelete, x.Purpose)).ToList();
            AiClient.Handler = (_, _, _) => Task.FromResult(new AiReply { Text = Full(items) });
            new AiCoordinator().ExplainAsync(items, provider, "m", false, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            check("端到端后风险/勾选/可删/用途都没变",
                items.Select(x => (x.Risk, x.Selected, x.CanDelete, x.Purpose)).SequenceEqual(snapshot));
            check("端到端后确实写进了 AiNote", items.Count(x => x.HasAiNote) == 60,
                items.Count(x => x.HasAiNote).ToString());
        }
        finally
        {
            AiClient.Handler = saved;
        }
    }

    // ------------------------------------------------------------------ AI 结果诊断

    static void AiDiagnosticsTests(CheckFn check, Action<string> section)
    {
        section("AI 结果诊断：区分「提交」「可用说明」与失败原因");

        var items = new List<CleanItem>
        {
            Temp(@"C:\Users\me\AppData\Local\a.tmp", 100),
            Temp(@"C:\Users\me\AppData\Local\b.tmp", 200),
            Temp(@"C:\Users\me\AppData\Local\c.tmp", 300),
        };

        // 1) 模型只回了 1 条 → 计数必须能看出「提交 3、可用 1」
        int applied = AiNoteParser.ApplyWithStats(items,
            "GOTO C:\\Users\\me\\AppData\\Local\\a.tmp\ta 的缓存", null).Applied;
        check("只回一条时只应用一条", applied == 1, applied.ToString());
        check("提交数 != 可用数时可以算出差额", items.Count - applied == 2);

        var s2 = AiNoteParser.ApplyWithStats(items,
            "GOTO C:\\Users\\me\\AppData\\Local\\a.tmp\t缓存\n" +
            "GOTO C:\\Users\\me\\AppData\\Local\\b.tmp\t缓存\n" +
            "GOTO C:\\Users\\me\\AppData\\Local\\c.tmp\t缓存", null);
        check("三条都合规时全部应用", s2.Applied == 3, s2.Applied.ToString());
        check("候选行计数正确", s2.CandidateLines == 3, s2.CandidateLines.ToString());
        check("命中路径计数正确", s2.PathHits == 3, s2.PathHits.ToString());

        // 2) 路径对不上 ⇒ PathHits 为 0，能定位成「匹配失败」而不是「模型没回」
        var s3 = AiNoteParser.ApplyWithStats(items,
            "GOTO D:\\elsewhere\\x.tmp\t别的路径\nGOTO D:\\elsewhere\\y.tmp\t别的路径", null);
        check("路径对不上时不应用任何条目", s3.Applied == 0);
        check("路径对不上时候选行仍有计数（区别于模型没回）",
            s3.CandidateLines == 2 && s3.PathHits == 0,
            $"cand={s3.CandidateLines} hits={s3.PathHits}");

        // 3) 完全不成格式 ⇒ CandidateLines=0，可判定为「格式不合规」
        var s4 = AiNoteParser.ApplyWithStats(items, "Here is a summary of your disk.\nAll good.", null);
        check("格式不合规时没有候选行", s4.CandidateLines == 0, s4.CandidateLines.ToString());
        check("格式不合规会被标记", s4.LooksUnformatted);

        // 4) 噪音列被跳过时单独计数（模型把大小/风险词塞进第二列）
        var s5 = AiNoteParser.ApplyWithStats(items,
            "GOTO C:\\Users\\me\\AppData\\Local\\a.tmp\tsafe\t2.1G\ta 的真实说明", null);
        check("噪音列被跳过但仍然拿到说明", s5.Applied == 1 && s5.NoiseSkipped == 2,
            $"applied={s5.Applied} noise={s5.NoiseSkipped}");

        // 5) 空回复不产生任何计数
        var s6 = AiNoteParser.ApplyWithStats(items, "", null);
        check("空回复全零", s6.Lines == 0 && s6.Applied == 0 && !s6.LooksUnformatted);

        // 6) 「请求成功但无可用说明」是一个独立结论，不能算成功
        var unusable = new AiExplainResult(60, 0, "some reply", s4, 10, 20000, 5, "sidecar", 1);
        check("有回复但零可用 ⇒ Unusable", unusable.Unusable && !unusable.AnyApplied);
        check("可用数差额算得对", unusable.WithoutNote == 60, unusable.WithoutNote.ToString());

        // 7) 部分覆盖要如实标出（单批上限截掉了剩下的）
        var partial = new AiExplainResult(60, 15, "x", s2, 1, 2, 3, "sidecar", 1)
        { PartialCoverage = true, SkippedByLimit = 40 };
        check("部分覆盖被标记", partial.PartialCoverage);
        check("被上限截掉的条数被记下", partial.SkippedByLimit == 40);
        check("可用数小于提交数时不宣称全部完成", partial.Applied < partial.Sent);

        // 8) 解析统计必须来自真实解析器，且 AI 仍然不碰风险/勾选
        var before = items.Select(x => (x.Risk, x.Selected, x.CanDelete)).ToList();
        AiNoteParser.ApplyWithStats(items,
            "GOTO C:\\Users\\me\\AppData\\Local\\a.tmp\tsafe\tsomething", null);
        check("AI 只写 AiNote，不改风险/勾选/可删",
            items.Select(x => (x.Risk, x.Selected, x.CanDelete)).SequenceEqual(before));
    }

    // ------------------------------------------------------------------ 清理前检查

    static void PreflightTests(CheckFn check, Action<string> section)
    {
        section("清理前检查页：范围、需确认数与执行集合一致");

        var items = new List<CleanItem>
        {
            Temp(@"C:\Windows\Temp\a.tmp", 1_000),
            Temp(@"C:\Windows\Temp\b.tmp", 2_000),
            Large(@"D:\video\big.mkv", 5_000_000),
            Keep(@"D:\keep\untouched.dat", 9_000_000),
            NoDeleteSafe(@"D:\locked\ro.dat", 7_000_000),
        };
        var layered = CleanGroupingService.Build(items);

        // 全选后进入清理前检查：只统计**真正会处理**的项
        foreach (var x in items) x.Selected = true;
        foreach (var p in layered.Purposes) p.SyncFromItems();

        var picked = layered.SelectedItems.ToList();
        var facts = CleanPreflight.Build(picked, layered);

        check("检查页只统计可删项（排除保留项）",
            facts.Items == picked.Count && picked.All(x => x.CanDelete), facts.Items.ToString());
        check("不可删的项不进检查范围",
            picked.All(x => x.FullPath != @"D:\keep\untouched.dat"
                            && x.FullPath != @"D:\locked\ro.dat"));
        check("预计空间只累计将处理的项",
            facts.Bytes == picked.Sum(x => x.Size), facts.Bytes.ToString());
        check("需确认数等于非建议清理的项",
            facts.NeedsConfirm == picked.Count(x => x.Risk != CleanRisk.Safe),
            facts.NeedsConfirm.ToString());
        check("有需确认项时必须如实报出", facts.NeedsConfirm > 0);
        check("位置数按稳定键去重且不超过项数",
            facts.Locations > 0 && facts.Locations <= facts.Items,
            $"{facts.Locations} vs {facts.Items}");
        check("检查页范围与执行集合完全一致",
            facts.Items == layered.SelectedItems.Count(x => x.CanDelete && x.FullPath != ""));

        // 空选择：明确「没有可检查的内容」，主按钮应禁用
        foreach (var x in items) x.Selected = false;
        foreach (var p in layered.Purposes) p.SyncFromItems();
        var empty = CleanPreflight.Build(layered.SelectedItems.ToList(), layered);
        check("没选内容时检查页为空", empty.HasNothing && empty.Items == 0);

        // 保留项（CanDelete=false）永远不能进检查范围，即使被硬设为已选
        var keep = items.First(x => !x.CanDelete);
        keep.Selected = true;
        var guarded = CleanPreflight.Build(new[] { keep }, layered);
        check("保留项即使被标记选中也不进检查范围", guarded.Items == 0, guarded.Items.ToString());
        check("保留项不计空间", guarded.Bytes == 0);
    }

    // ------------------------------------------------------------------ 风险分区容器

    static void SectionContainerTests(CheckFn check, Action<string> section)
    {
        section("风险分区的独立视觉区域与语义");

        var items = new List<CleanItem>
        {
            Temp(@"C:\Windows\Temp\a.tmp", 100),
            Large(@"D:\video\big.mkv", 9_000_000_000),
        };
        var r = CleanGroupingService.Build(items);
        var sections = CleanPurposeSection.Build(r.Purposes);
        var safe = sections.FirstOrDefault(s => s.RiskTier == 0);
        var confirm = sections.FirstOrDefault(s => s.RiskTier == 1);
        check("建议清理与需要你确认各自成区", safe != null && confirm != null);
        if (safe == null || confirm == null) return;

        // §六.1-6：两块区域必须有可区分的视觉属性，且**不能只靠颜色**
        check("两区图标不同（不只靠颜色）", safe.Icon != confirm.Icon,
            safe.Icon + " vs " + confirm.Icon);
        check("两区副标题不同且非空",
            safe.Subtitle.Length > 0 && confirm.Subtitle.Length > 0 && safe.Subtitle != confirm.Subtitle);
        check("两区色带键不同", safe.AccentKey != confirm.AccentKey);
        check("两区背景层级不同", safe.SurfaceKey != confirm.SurfaceKey);

        // §六.10：需要你确认默认折叠；建议清理默认展开
        check("需要你确认默认折叠", !confirm.IsExpanded);
        check("建议清理默认展开", safe.IsExpanded);

        // §六.13：大文件不能因为「大」进建议清理
        check("大文件不在建议清理区", safe.Rows.All(x => x.Purpose != CleanPurpose.Large));
        check("大文件在需要你确认区", confirm.Rows.Any(x => x.Purpose == CleanPurpose.Large));

        // §六.11：折叠组有已选时必须报「已选 N」
        var pick = confirm.Rows.SelectMany(x => x.Items).FirstOrDefault(x => x.CanDelete);
        if (pick != null)
        {
            pick.Selected = true;
            confirm.RaiseHeaderChanged();
            check("折叠组有已选时标题报已选数", confirm.HasCollapsedSelection);
            check("已选提示含数量",
                confirm.CollapsedSelectionNote.Contains("selected", StringComparison.OrdinalIgnoreCase),
                confirm.CollapsedSelectionNote);
            pick.Selected = false;
            confirm.RaiseHeaderChanged();
        }

        // 分区标题就是分组名，不含统计（统计在右侧，分成两栏）
        check("分区标题不含统计数字",
            safe.HeaderTitle.Length > 0 && !safe.HeaderTitle.Contains("kinds", StringComparison.OrdinalIgnoreCase),
            safe.HeaderTitle);
        check("分区统计含类数与位置数",
            safe.HeaderStats.Contains("kinds") && safe.HeaderStats.Contains("locations"), safe.HeaderStats);
    }

    // ------------------------------------------------------------------ 风险归属

    static void RiskOwnershipTests(CheckFn check, Action<string> section)
    {
        section("风险归属（大 / 旧 / 长路径 不得混进建议清理）");

        // 1) 需要 Sub 命中的签名，Sub 没命中就必须「认不出来」。
        //    Chrome 的 Needle 到 \google\chrome\user data，真正的缓存在 \cache 等子目录。
        string chromeCache = @"c:\users\u\appdata\local\google\chrome\user data\default\cache\cache_data\f_0001";
        string chromeHistory = @"c:\users\u\appdata\local\google\chrome\user data\default\history";

        check("缓存子目录命中签名", AppSignatures.Classify(chromeCache) != null);
        check("缓存子目录被认成浏览器缓存",
            AppSignatures.Classify(chromeCache)?.Key == "browser",
            AppSignatures.Classify(chromeCache)?.Key ?? "(null)");
        // 关键安全断言：User Data 下的用户数据（历史/书签）不能被当成缓存
        check("用户数据（History）不再被认成缓存",
            AppSignatures.Classify(chromeHistory) == null,
            AppSignatures.Classify(chromeHistory)?.Name ?? "(null)");
        check("用户数据不会被标成 Safe",
            AppSignatures.Classify(chromeHistory)?.Risk != CleanRisk.Safe);

        // IsSafeCache 与 Classify 必须同一口径
        check("IsSafeCache 与 Classify 口径一致（缓存）", AppSignatures.IsSafeCache(chromeCache));
        check("IsSafeCache 与 Classify 口径一致（用户数据）", !AppSignatures.IsSafeCache(chromeHistory));

        // 2) 「大 / 旧 / 长路径」规则的风险不受签名降级。
        //    注意 RiskIsAuthoritative 是接口默认实现，所以按接口读。
        var large = new LargeFileRule();
        var old = new OldFileRule();
        var longPath = new LongPathRule();
        var temp = new TempCacheRule();
        check("大文件规则的风险说了算", ((ICleanRule)large).RiskIsAuthoritative);
        check("旧文件规则的风险说了算", ((ICleanRule)old).RiskIsAuthoritative);
        check("超长路径规则的风险说了算", ((ICleanRule)longPath).RiskIsAuthoritative);
        check("临时/缓存规则允许签名细化风险", !((ICleanRule)temp).RiskIsAuthoritative);

        // 端到端：一个大文件即使落在缓存路径下，也必须留在「需要你确认」
        var bigInCache = new FileEntry
        {
            Name = "f_0001",
            FullPath = chromeCache,
            Size = 9_000_000_000,
            Kind = EntryKind.File,
        };
        var hit = new CleanRuleHit
        {
            Entry = bigInCache,
            Target = CleanRuleTarget.Large,
            Reason = Loc.ReasonLarge,
            Group = Loc.GroupLarge,
            Purpose = CleanPurpose.Large,
            Risk = CleanRisk.Confirm,
            CanDelete = true,
            Selected = false,
        };
        var asLarge = CleanItemFactory.Create(hit, riskIsAuthoritative: true);
        check("缓存路径下的大文件仍是需确认", asLarge.Risk == CleanRisk.Confirm, asLarge.Risk.ToString());
        check("缓存路径下的大文件不会被预勾选", !asLarge.Selected);
        check("缓存路径下的大文件用途仍是「大文件」", asLarge.Purpose == CleanPurpose.Large,
            asLarge.Purpose.ToString());
        check("风险归规则时说明也用规则原因",
            asLarge.Reason == Loc.ReasonLarge, asLarge.Reason);

        // 对照：同一条目若按「签名说了算」（缓存规则），才允许变成 Safe
        var asCache = CleanItemFactory.Create(new CleanRuleHit
        {
            Entry = bigInCache,
            Target = CleanRuleTarget.Cleanable,
            Reason = "temp",
            Group = Loc.GroupTemp,
            Purpose = CleanPurpose.AppCache,
            Risk = CleanRisk.Safe,
            CanDelete = true,
            Selected = true,
        }, riskIsAuthoritative: false);
        check("缓存规则命中才允许是建议清理", asCache.Risk == CleanRisk.Safe, asCache.Risk.ToString());

        // 3) 大/旧 单独存在时永远进「需要你确认」分区
        var largeItems = new List<CleanItem>
        {
            Large(@"D:\videos\movie.mkv", 9_000_000_000),
            Old(@"E:\archive\old.zip", 8_000_000),
        };
        var r = CleanGroupingService.Build(largeItems);
        check("大文件/旧文件都落在需确认档",
            r.Purposes.All(p => p.RiskTier == 1),
            string.Join(",", r.Purposes.Select(p => $"{p.PurposeName}:{p.RiskTier}")));
        check("大文件/旧文件默认不勾选", r.SelectedItems.Count() == 0);

        // 4) 同一用途同时有 Safe 与 Confirm 时，必须分成两行（各自子集）
        var mixed = new List<CleanItem>
        {
            Temp(@"C:\Windows\Temp\a.tmp", 100),
            TempConfirm(@"C:\Windows\Temp\b.tmp", 200),
        };
        var rm = CleanGroupingService.Build(mixed);
        var two = rm.Purposes.Where(p => p.Purpose == CleanPurpose.Temp).ToList();
        check("同用途不同风险拆成两行", two.Count == 2, two.Count.ToString());
        check("两行的候选子集互不重叠",
            two[0].Items.All(x => !two[1].Items.Contains(x)));
        check("两行候选合计等于原始集",
            two[0].FileCount + two[1].FileCount == 2);
    }

    // ------------------------------------------------------------------ 页面/分区模型

    static void NavigationStateTests(CheckFn check, Action<string> section)
    {
        section("分层视图：分区与单层页面");

        var items = new List<CleanItem>
        {
            Temp(@"C:\Windows\Temp\a.tmp", 100),
            Temp(@"C:\Windows\Temp\b.tmp", 200),
            TempConfirm(@"C:\Windows\Temp\risky.tmp", 300),
            Large(@"D:\videos\big.mkv", 9_000_000_000),
        };
        var r = CleanGroupingService.Build(items);
        var sections = CleanPurposeSection.Build(r.Purposes);

        check("首页只分两个风险分区", sections.Count <= 2, sections.Count.ToString());
        var safe = sections.FirstOrDefault(s => s.RiskTier == 0);
        var confirm = sections.FirstOrDefault(s => s.RiskTier == 1);
        check("有「建议清理」分区", safe != null);
        check("有「需要你确认」分区", confirm != null);

        // 默认展开/折叠：需确认分区默认折叠，避免被顺手带选
        check("建议清理默认展开", safe!.IsExpanded);
        check("需要你确认默认折叠", !confirm!.IsExpanded);

        // 分区标题左标题 / 右统计：风险只在标题里说，行里不再重复
        check("分区标题是纯标题（不含统计）",
            safe.HeaderTitle.Length > 0 && !safe.HeaderTitle.Contains("kinds", StringComparison.OrdinalIgnoreCase),
            safe.HeaderTitle);
        check("分区右侧统计含类数/位置数/空间",
            safe.HeaderStats.Contains($"{safe.RowCount}") && safe.HeaderStats.Contains($"{safe.LocationCount}")
            && safe.HeaderStats.Contains("about", StringComparison.OrdinalIgnoreCase),
            safe.HeaderStats);
        // 分组空间必须标成估算（约），不能表示整组都能放心删
        check("分区空间标为约/估算",
            safe.HeaderStats.Contains("about", StringComparison.OrdinalIgnoreCase),
            safe.HeaderStats);
        // 展开箭头不靠颜色表达状态
        check("折叠时箭头朝右", confirm.Chevron == "▸", confirm.Chevron);
        check("展开时箭头朝下", safe.Chevron == "▾", safe.Chevron);
        check("折叠且未选时不显示已选提示", !confirm.HasCollapsedSelection);

        // §六.5：折叠的分区里若有已选项，标题必须明确写出来
        var confirmItem = confirm.Rows.SelectMany(x => x.Items).FirstOrDefault(x => x.CanDelete);
        if (confirmItem != null)
        {
            confirmItem.Selected = true;
            confirm.RaiseHeaderChanged();
            check("折叠分区有已选时给出提示", confirm.HasCollapsedSelection);
            check("折叠提示写明已选数量",
                confirm.CollapsedSelectionNote.Contains("selected", StringComparison.OrdinalIgnoreCase),
                confirm.CollapsedSelectionNote);
            confirmItem.Selected = false;
            confirm.RaiseHeaderChanged();
        }
        check("清掉选择后折叠提示消失", !confirm.HasCollapsedSelection);
        // 分区统计：同一位置在两个风险分区里各出现一次是**展示**需要，
        // 但全盘位置数按去重后的稳定键算（§六.7），所以分区相加 ≥ 总位置数。
        check("分区相加不少于（去重后的）总位置数",
            safe.LocationCount + confirm.LocationCount >= r.TotalLocations,
            $"{safe.LocationCount}+{confirm.LocationCount} vs {r.TotalLocations}");
        check("同一位置不因风险拆组重复计入总数",
            r.TotalLocations == 2, r.TotalLocations.ToString());

        // 首页行 = 用途，行数远小于候选数
        int homeRows = sections.Sum(s => s.RowCount);
        check("首页行数等于用途数（不是候选数）", homeRows == r.Purposes.Count);
        check("首页不暴露候选级行模型", homeRows < items.Count);

        // 用途行不显示实际路径（模型只给名称/影响/空间/位置数）
        var row = r.Purposes.First(p => p.Purpose == CleanPurpose.Temp);
        check("用途行有影响说明", row.Impact.Length > 0);
        check("用途行位置数是位置不是分类", row.LocationCount == 1, row.LocationCount.ToString());
        check("位置比例带量词", row.SelectionRatioText.Contains("selected", StringComparison.OrdinalIgnoreCase)
            || row.SelectionRatioText.Contains('项'), row.SelectionRatioText);

        // 位置行：路径默认收起，"查看路径"可切换
        var loc = r.Purposes.SelectMany(p => p.Locations).First();
        check("位置路径默认收起", !loc.IsPathVisible);
        check("路径切换文案", loc.PathToggleText.Length > 0);
        loc.IsPathVisible = true;
        check("路径可展开", loc.IsPathVisible);
        check("位置行有影响/原因说明", loc.Reason.Length > 0);

        // 组勾选范围提示：说清是整组
        check("勾选框提示整组范围",
            loc.GroupScopeHint.Length > 0, loc.GroupScopeHint);

        // 三态在用途级同样成立
        row.IsChecked = true;
        check("用途级三态：全选", row.IsChecked == true);
        row.Items.First(x => x.CanDelete).Selected = false;
        row.SyncFromItems();
        check("用途级三态：半选", row.IsChecked == null);
        check("半选时比例文案正确",
            row.SelectionRatioText.Contains(row.SelectedCount.ToString()),
            row.SelectionRatioText);

        // 需确认分区默认折叠，其候选不会被「建议清理」整组选中带入
        var confirmRow = confirm.Rows.First();
        check("需确认分区里没有因建议分区勾选而被选中", confirmRow.SelectedCount == 0);

        // 分区列表可重复构建且稳定
        var again = CleanPurposeSection.Build(r.Purposes);
        check("分区构建稳定", again.Count == sections.Count
            && again[0].RiskTier == sections[0].RiskTier);
    }

    // ------------------------------------------------------------------ 分组正确性

    static void GroupingCorrectness(CheckFn check, Action<string> section)
    {
        section("分层归类：基本分组");

        var items = new List<CleanItem>
        {
            Temp(@"C:\Windows\Temp\a.tmp", 100),
            Temp(@"C:\Windows\Temp\sub\b.tmp", 200),
            Temp(@"C:\Windows\Temp\sub\deep\c.tmp", 300),
            Temp(@"C:\Users\x\AppData\Local\Temp\d.tmp", 400),
            Browser(@"C:\Users\x\AppData\Local\Google\Chrome\User Data\Default\Cache\Cache_Data\f1", 500),
            Browser(@"C:\Users\x\AppData\Local\Google\Chrome\User Data\Default\Cache\Cache_Data\f2", 600),
            Large(@"D:\videos\movie.mkv", 9_000_000_000),
            Old(@"E:\archive\old.zip", 8_000_000),
        };

        var result = CleanGroupingService.Build(items);

        check("完整候选一条不丢", result.TotalFiles == items.Count,
            $"{result.TotalFiles} vs {items.Count}");
        check("没有重复命中去重丢项", result.DuplicateHitsRemoved == 0,
            result.DuplicateHitsRemoved.ToString());

        // 同一缓存根下的多个子目录必须汇总成一个位置，而不是每个父目录一行
        var tempPurpose = result.Purposes.FirstOrDefault(p => p.Purpose == CleanPurpose.Temp && p.RiskTier == 0)
                          ?? result.Purposes.FirstOrDefault(p => p.Purpose == CleanPurpose.Temp);
        check("有「临时文件」用途", tempPurpose != null);
        var windowsTemp = tempPurpose!.Locations.FirstOrDefault(l => l.Path.Contains(@"\Windows\Temp", StringComparison.OrdinalIgnoreCase));
        check("Windows\\Temp 聚成一个位置", windowsTemp != null);
        check("该位置吸收了子目录里的候选", windowsTemp!.FileCount >= 3, windowsTemp.FileCount.ToString());

        // 用户临时目录应当是**另一个**位置（锚点不同）
        var userTemp = tempPurpose.Locations.FirstOrDefault(l => l.Path.Contains(@"\Local\Temp", StringComparison.OrdinalIgnoreCase));
        check("用户临时目录是独立位置", userTemp != null);
        check("两个临时位置没有合并", tempPurpose.LocationCount >= 2, tempPurpose.LocationCount.ToString());

        // 浏览器缓存按签名识别，两个文件归到一个位置
        var browser = result.Purposes.FirstOrDefault(p => p.Purpose == CleanPurpose.BrowserCache);
        check("浏览器缓存单独成用途", browser != null);
        check("Chrome 缓存的两个文件在同一位置", browser!.Locations.Any(l => l.FileCount == 2),
            string.Join(",", browser.Locations.Select(l => l.FileCount)));

        // 大文件必须是「需确认」，且不能因为归类而降级
        var large = result.Purposes.FirstOrDefault(p => p.Purpose == CleanPurpose.Large);
        check("大文件单独成用途", large != null);
        check("大文件落在「需要你确认」档", large!.RiskTier == 1, large.RiskTier.ToString());
        check("大文件默认不勾选", large.SelectedCount == 0);
    }

    // ------------------------------------------------------------------ 锚点

    static void LocationAnchoring(CheckFn check, Action<string> section)
    {
        section("分层归类：位置锚点只收窄不放大");

        // 未识别软件：回退到实际文件夹，且标记「未识别」
        string unk = CleanGroupingService.FindAnchor(@"D:\SomeApp\data\sub\x.bin");
        check("未识别时锚点是父目录", unk.Length > 0 && !unk.EndsWith(@"\SomeApp", StringComparison.OrdinalIgnoreCase),
            unk);
        check("锚点不会上卷到盘符根", unk != @"D:\");

        // 宽泛位置永远不做锚点
        foreach (var wide in new[]
                 {
                     @"C:\Users\Bob",
                     @"C:\Users\Bob\AppData",
                     @"C:\Users\Bob\AppData\Local",
                     @"C:\Program Files\SomeApp",
                     @"C:\ProgramData",
                     @"C:\Windows",
                 })
        {
            string a = CleanGroupingService.FindAnchor(wide + @"\deep\thing.bin");
            check($"锚点不是宽泛位置：{wide}", !a.Equals(wide, StringComparison.OrdinalIgnoreCase), a);
        }

        // 已知缓存目录要能聚合整棵子树
        string winTemp = CleanGroupingService.FindAnchor(@"C:\Windows\Temp\a\b\c\d.tmp");
        check("Windows\\Temp 子树汇总到同一锚点",
            winTemp.Equals(@"C:\Windows\Temp", StringComparison.OrdinalIgnoreCase), winTemp);

        // 未识别软件按文件夹回退 + 用文件夹名显示
        var unidentified = new List<CleanItem>
        {
            Unidentified(@"D:\MyStuff\build\out\a.tmp", 10),
            Unidentified(@"D:\MyStuff\build\out\b.tmp", 20),
        };
        var r = CleanGroupingService.Build(unidentified);
        var loc = r.Purposes.SelectMany(p => p.Locations).FirstOrDefault();
        check("未识别位置有节点", loc != null);
        check("未识别位置被标记", loc!.IsUnidentified);

        // 不同软件同名目录不能误合并：键是完整路径，不是名字
        var sameName = new List<CleanItem>
        {
            Unidentified(@"D:\CompanyA\cache\x.tmp", 10),
            Unidentified(@"D:\CompanyB\cache\x.tmp", 20),
        };
        var r2 = CleanGroupingService.Build(sameName);
        check("同名但不同路径的目录不会合并",
            r2.TotalLocations == 2, r2.TotalLocations.ToString());

        // 位置键不含显示名（改文案/改语言键不变）
        var k1 = CleanGroupingService.LocationKey(Unidentified(@"D:\A\cache\x.tmp", 1), CleanPurpose.Temp);
        var k2 = CleanGroupingService.LocationKey(Unidentified(@"D:\A\cache\y.tmp", 1), CleanPurpose.Temp);
        check("同目录不同文件共用位置键", k1 == k2, k1 + " vs " + k2);
    }

    // ------------------------------------------------------------------ 风险拆组

    static void RiskSplit(CheckFn check, Action<string> section)
    {
        section("分层归类：风险拆组");

        // 同一用途里既有安全也有需确认 → 必须拆成两个首页行
        var mixed = new List<CleanItem>
        {
            Temp(@"C:\Windows\Temp\safe.tmp", 100),            // Safe
            TempConfirm(@"C:\Windows\Temp\unknown.tmp", 200),  // Confirm
        };
        var r = CleanGroupingService.Build(mixed);
        var tempNodes = r.Purposes.Where(p => p.Purpose == CleanPurpose.Temp).ToList();
        check("同用途不同风险拆成两行", tempNodes.Count == 2, tempNodes.Count.ToString());
        check("安全档在前", tempNodes[0].RiskTier == 0);
        check("需确认档在后", tempNodes[1].RiskTier == 1);

        // 同一位置里不同风险也要拆成两个位置
        check("同位置不同风险拆成两个位置",
            tempNodes[0].LocationCount == 1 && tempNodes[1].LocationCount == 1,
            $"{tempNodes[0].LocationCount}/{tempNodes[1].LocationCount}");

        // 勾选安全档不能带动需确认档
        tempNodes[0].IsChecked = true;
        check("勾选安全档不会选到需确认档", tempNodes[1].SelectedCount == 0,
            tempNodes[1].SelectedCount.ToString());
        check("安全档确实被选中", tempNodes[0].SelectedCount == 1);
    }

    // ------------------------------------------------------------------ 选择安全

    static void SelectionSafety(CheckFn check, Action<string> section)
    {
        section("分层归类：选择与删除安全");

        var items = new List<CleanItem>
        {
            Temp(@"C:\Windows\Temp\a.tmp", 100),
            Temp(@"C:\Windows\Temp\b.tmp", 100),
            // 同组内不可删的项（类似重复文件的保留项 / 受保护项）——风险档与可删项相同，
            // 这样才真的能验证「整组勾选只选可删的」。
            NoDeleteSafe(@"C:\Windows\Temp\keep.dat", 500),
            // 长路径这类规则本来就不允许删
            NoDeleteSafe(@"C:\Windows\Temp\verylong.lnk", 50),
        };
        var r = CleanGroupingService.Build(items);
        var node = r.Purposes.First(p => p.Purpose == CleanPurpose.Temp && p.RiskTier == 0);

        check("可删计数排除不可删项", node.SelectableCount == 2, node.SelectableCount.ToString());
        check("文件数包含不可删项", node.FileCount == 4, node.FileCount.ToString());

        node.IsChecked = true;
        check("整组勾选只选中可删项", node.SelectedCount == 2, node.SelectedCount.ToString());
        check("不可删项保持未选", items.Where(x => !x.CanDelete).All(x => !x.Selected));

        // 组勾选范围 = 组内完整候选，而不是当前显示页
        var loc = node.Locations[0];
        check("位置勾选范围是完整候选", loc.Items.Count == 4, loc.Items.Count.ToString());

        // 位置标题不能带 safe cache / 内部风险词这类技术标签（§四.5）
        check("位置标题不含 safe cache 之类技术标签",
            !loc.DisplayName.Contains("safe cache", StringComparison.OrdinalIgnoreCase)
            && !loc.DisplayName.Contains("confirm", StringComparison.OrdinalIgnoreCase)
            && !loc.DisplayName.Contains("bloatware", StringComparison.OrdinalIgnoreCase),
            loc.DisplayName);
        // 技术信息降级到悬停，但数据没丢
        check("技术细节仍在悬停可查", loc.HintText.Length > 0, loc.HintText);

        // 「实际执行集合」就是从完整候选里筛 Selected && CanDelete
        var exec = r.SelectedItems.ToList();
        check("执行集合与选择范围一致", exec.Count == 2, exec.Count.ToString());
        check("执行集合不含不可删项", exec.All(x => x.CanDelete));

        // 三态
        exec[0].Selected = false;
        node.SyncFromItems();
        check("部分选中显示半选", node.IsChecked == null);
        exec[1].Selected = false;
        node.SyncFromItems();
        check("全部取消显示未选", node.IsChecked == false);
        node.ToggleFrom(TriState.Unchecked);
        check("半选/未选点一下变全选", node.IsChecked == true && node.SelectedCount == 2);
        node.ToggleFrom(TriState.Checked);
        check("全选点一下变取消", node.IsChecked == false && node.SelectedCount == 0);

        // 父子目录重叠：空间不能重复计
        var overlap = new List<CleanItem>
        {
            Dir(@"C:\Windows\Temp\sub", 1000),
            Temp(@"C:\Windows\Temp\sub\inside.tmp", 300),
        };
        var ro = CleanGroupingService.Build(overlap);
        var nodeO = ro.Purposes.First(p => p.Purpose == CleanPurpose.Temp);
        check("父子重叠时空间不重复计", nodeO.Bytes == 1000, nodeO.Bytes.ToString());
        check("重叠被如实计数", nodeO.OverlapCount == 1, nodeO.OverlapCount.ToString());
        check("重叠不影响候选总数", nodeO.FileCount == 2);

        // 同一路径重复命中：去重且空间不重复计
        var dupes = new List<CleanItem>
        {
            Temp(@"C:\Windows\Temp\same.tmp", 100),
            Temp(@"C:\WINDOWS\TEMP\SAME.TMP", 100),
        };
        var rd = CleanGroupingService.Build(dupes);
        check("同路径大小写不敏感去重", rd.TotalFiles == 1, rd.TotalFiles.ToString());
        check("记录被去掉的重复命中", rd.DuplicateHitsRemoved == 1);

        // 「别删」永远不进列表
        var keep = new List<CleanItem> { Keep(@"C:\Windows\System32\kernel32.dll", 999) };
        check("别删项不进分层结果", CleanGroupingService.Build(keep).TotalFiles == 0);
    }

    // ------------------------------------------------------------------ 重复文件

    static void DuplicateGrouping(CheckFn check, Action<string> section)
    {
        section("分层归类：重复文件保留组结构");

        var groupKey = "12|C:\\a\\x.bin|C:\\b\\x.bin";
        var dups = new List<CleanItem>
        {
            Dup(@"C:\a\x.bin", 6000, "dup-keep|" + groupKey, canDelete: false, selected: false),
            Dup(@"C:\b\x.bin", 6000, "dup-extra|" + groupKey, canDelete: true, selected: true),
            Dup(@"C:\c\x.bin", 6000, "dup-extra|" + groupKey, canDelete: true, selected: true),
        };
        var r = CleanGroupingService.Build(dups);
        var dup = r.Purposes.First(p => p.Purpose == CleanPurpose.Duplicate);

        check("重复项归到「重复文件」用途", dup != null);
        check("保留项与多余项拆成两个位置", dup.LocationCount == 2, dup.LocationCount.ToString());

        var keepLoc = dup.Locations.FirstOrDefault(l => l.Handling.StartsWith("dup-keep", StringComparison.Ordinal));
        var extraLoc = dup.Locations.FirstOrDefault(l => l.Handling.StartsWith("dup-extra", StringComparison.Ordinal));
        check("有保留位置", keepLoc != null);
        check("有多余位置", extraLoc != null);
        check("保留位置没有可删项", keepLoc!.SelectableCount == 0, keepLoc.SelectableCount.ToString());
        check("多余位置有 2 个可删项", extraLoc!.SelectableCount == 2, extraLoc.SelectableCount.ToString());

        // 整组勾选不能把保留项删掉
        dup.IsChecked = true;
        check("整组勾选不动保留项", !dups[0].Selected);
        check("整组勾选选中多余副本", dups[1].Selected && dups[2].Selected);
        check("保留项永远不可删", !dups[0].CanDelete);

        // 未完成验证的重复候选不参与删除（CanDelete=false 且未勾选）
        var incomplete = new List<CleanItem>
        {
            Dup(@"C:\a\y.bin", 6000, "dup-extra|g2", canDelete: false, selected: false),
        };
        var ri = CleanGroupingService.Build(incomplete);
        var di = ri.Purposes.First(p => p.Purpose == CleanPurpose.Duplicate);
        check("未验证重复项不可删", di.SelectableCount == 0);
        di.IsChecked = true;
        check("未验证重复项不会被勾选", !incomplete[0].Selected);

        // 同一重复组在同一位置键下（不同组不会混）
        var k1 = CleanGroupingService.LocationKey(dups[1], CleanPurpose.Duplicate);
        var k2 = CleanGroupingService.LocationKey(dups[2], CleanPurpose.Duplicate);
        check("同组同处理方式共用位置键", k1 == k2, k1);
        check("位置键含组标识", k1.Contains(groupKey, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ 分页与搜索

    static void PagerTests(CheckFn check, Action<string> section)
    {
        section("明细按需加载：分页 / 搜索 / 状态保持");

        var items = new List<CleanItem>();
        for (int i = 0; i < 82_310; i++)
            items.Add(Temp($@"C:\Windows\Temp\file{i:D6}.tmp", i));
        // 埋一个只在很后面才出现的名字，用来验证搜索覆盖未加载页
        items[81_000].Name = "zzz-needle.tmp";

        var pager = new CleanItemPager(items, 100);

        check("首批只显示 100 条", pager.Shown == 100, pager.Shown.ToString());
        check("可见项不超过一页", pager.Visible.Count == 100);
        check("总数是完整候选", pager.TotalCount == 82_310, pager.TotalCount.ToString());
        check("状态文案给出 100 / 全部", pager.StatusText.Contains("100") && pager.StatusText.Contains("82,310"),
            pager.StatusText);
        check("还有更多", pager.HasMore);

        pager.LoadMore();
        check("加载更多后 200 条", pager.Shown == 200, pager.Shown.ToString());

        // 搜索必须命中还没加载的页
        pager.SetSearch("zzz-needle");
        check("搜索命中未加载页", pager.MatchCount == 1, pager.MatchCount.ToString());
        check("搜索后显示命中项", pager.Visible.Count == 1 && pager.Visible[0].Name == "zzz-needle.tmp");
        check("搜索不改变完整候选数", pager.TotalCount == 82_310);

        // 空搜索恢复全量
        pager.SetSearch("");
        check("清空搜索恢复全量", pager.MatchCount == 82_310);
        check("清空搜索后回到一页", pager.Shown == 100);

        // 勾选状态在翻页/搜索/折叠后保持
        pager.Visible[0].Selected = true;
        pager.Visible[1].Selected = true;
        var markedPath = pager.Visible[0].FullPath;
        pager.SetSearch("file0000");
        check("搜索不回退勾选", items.Any(x => x.FullPath == markedPath && x.Selected));
        pager.SetSearch("");
        check("清空搜索后勾选仍在", items.Any(x => x.FullPath == markedPath && x.Selected));
        check("勾选计数正确", pager.SelectableAll.Count(x => x.Selected) == 2,
            pager.SelectableAll.Count(x => x.Selected).ToString());

        // 范围明确区分：当前页 / 搜索结果 / 整组
        var page = pager.SelectableOnPage;
        var matches = pager.SelectableMatches;
        var all = pager.SelectableAll;
        check("当前页范围是一页", page.Count == 100, page.Count.ToString());
        check("搜索范围是完整匹配集", matches.Count == 82_310, matches.Count.ToString());
        check("整组范围是全部可删候选", all.Count == 82_310, all.Count.ToString());

        // 按范围勾选只作用于可删项
        var withKeep = new List<CleanItem>
        {
            Temp(@"C:\t\a.tmp", 1),
            NoDelete(@"C:\t\keep.dat", 1),
        };
        var p2 = new CleanItemPager(withKeep, 100);
        p2.Select(p2.SelectableAll, true);
        check("整组勾选跳过不可删项", !withKeep[1].Selected && withKeep[0].Selected);

        // 显示范围 ⊆ 搜索范围 ⊆ 全部（数量关系必须自洽）
        check("显示范围不超过搜索范围", pager.Shown <= pager.MatchCount);
        check("搜索范围不超过全部", pager.MatchCount <= pager.TotalCount);
    }

    // ------------------------------------------------------------------ 规模

    static void ScaleTests(CheckFn check, Action<string> section)
    {
        section("分层归类：30 万条规模");

        var items = new List<CleanItem>(300_000);
        var purposes = new[]
        {
            CleanPurpose.Temp, CleanPurpose.BrowserCache, CleanPurpose.AppCache,
            CleanPurpose.AppLog, CleanPurpose.Recycle, CleanPurpose.Large, CleanPurpose.Old,
        };
        for (int i = 0; i < 300_000; i++)
        {
            var purpose = purposes[i % purposes.Length];
            bool safe = i % 3 != 0;
            // 1 万个不同目录 × 30 个文件，模拟真实盘
            items.Add(new CleanItem
            {
                Name = $"f{i:D6}.tmp",
                FullPath = $@"C:\bulk\dir{i % 10_000:D5}\f{i:D6}.tmp",
                Size = i % 5000,
                Purpose = purpose,
                Group = "Temp",
                Risk = safe ? CleanRisk.Safe : CleanRisk.Confirm,
                CanDelete = true,
                Selected = false,
            });
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = CleanGroupingService.Build(items);
        sw.Stop();

        check("30 万条全部保留", r.TotalFiles == 300_000, r.TotalFiles.ToString());
        check("首页行数是用途数（不是候选数）", r.Purposes.Count <= 20, r.Purposes.Count.ToString());
        check("首页不暴露 30 万个行模型", r.Purposes.Count < 100);
        check("位置总数有统计", r.TotalLocations > 0);
        check("位置数远小于候选数", r.TotalLocations < 300_000, r.TotalLocations.ToString());
        check("30 万条分组在合理时间内", sw.ElapsedMilliseconds < 15_000, $"{sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"       (300k grouping: {sw.ElapsedMilliseconds} ms, "
            + $"purposes={r.Purposes.Count}, locations={r.TotalLocations})");

        // 单个位置 10 万条：分页后不创建全量视图
        var huge = new List<CleanItem>();
        for (int i = 0; i < 100_000; i++) huge.Add(Temp($@"C:\Windows\Temp\f{i:D6}.tmp", i));
        var hr = CleanGroupingService.Build(huge);
        var hloc = hr.Purposes.SelectMany(p => p.Locations).First();
        check("10 万条聚成一个位置", hloc.FileCount == 100_000, hloc.FileCount.ToString());

        var pagerSw = System.Diagnostics.Stopwatch.StartNew();
        var pager = new CleanItemPager(hloc.Items, 100);
        pagerSw.Stop();
        check("10 万条位置展开只渲染一页", pager.Visible.Count == 100, pager.Visible.Count.ToString());
        check("展开不创建全量行", pager.Shown == 100);
        check("展开耗时合理", pagerSw.ElapsedMilliseconds < 5_000, $"{pagerSw.ElapsedMilliseconds} ms");
        Console.WriteLine($"       (100k single location expand: {pagerSw.ElapsedMilliseconds} ms, shown={pager.Shown})");

        // 位置数量上限：超出时截断显示但不丢候选
        var many = new List<CleanItem>();
        for (int i = 0; i < 6_000; i++) many.Add(Unidentified($@"C:\scatter\p{i:D5}\x.tmp", 10));
        var mr = CleanGroupingService.Build(many);
        var mp = mr.Purposes.First();
        check("位置数被限制（避免建几万个行）",
            mp.Locations.Count <= CleanGroupingService.MaxLocationsPerPurpose,
            mp.Locations.Count.ToString());
        check("位置被限制时候选总数不丢", mr.TotalFiles == 6_000, mr.TotalFiles.ToString());
        // 关键：位置行被截断时，「几处位置」和「预计空间」不能跟着少算
        check("位置总数含未建行的部分", mr.TotalLocations == 6_000, mr.TotalLocations.ToString());
        check("被截断时仍报告未列出数量", mp.HasHiddenLocations && mp.LocationsNotShown > 0,
            mp.LocationsNotShown.ToString());
        check("被截断时预计空间仍按全量算", mr.TotalBytes == 60_000, mr.TotalBytes.ToString());

        // 同一位置在「建议清理 / 需要你确认」各出现一次时，位置数不能翻倍
        var splitRisk = new List<CleanItem>
        {
            Temp(@"C:\Windows\Temp\a.tmp", 100),
            TempConfirm(@"C:\Windows\Temp\b.tmp", 200),
        };
        var rs = CleanGroupingService.Build(splitRisk);
        check("风险拆组不重复计同一位置", rs.TotalLocations == 1, rs.TotalLocations.ToString());
        check("风险拆组仍保留两行展示", rs.Purposes.Count == 2, rs.Purposes.Count.ToString());
        check("空间按两个风险档合计", rs.TotalBytes == 300, rs.TotalBytes.ToString());
    }

    // ------------------------------------------------------------------ 取消

    static void CancellationTests(CheckFn check, Action<string> section)
    {
        section("分层归类：取消与旧结果丢弃");

        var items = new List<CleanItem>();
        for (int i = 0; i < 50_000; i++) items.Add(Temp($@"C:\Windows\Temp\f{i}.tmp", i));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool canceled = false;
        try { CleanGroupingService.Build(items, cts.Token); }
        catch (OperationCanceledException) { canceled = true; }
        check("分组运算响应取消", canceled);

        // 旧结果的丢弃由界面代次守卫负责（UiRegressionCheck 静态校验），
        // 这里验证模型层是可重复构建的纯函数：同样的输入给同样的结果
        var a = CleanGroupingService.Build(items);
        var b = CleanGroupingService.Build(items);
        check("重复构建结果稳定（纯函数）",
            a.TotalFiles == b.TotalFiles && a.TotalLocations == b.TotalLocations);
        check("多次构建不会互相污染勾选",
            a.Purposes.All(p => p.SelectedCount == 0) && b.Purposes.All(p => p.SelectedCount == 0));
    }

    // ------------------------------------------------------------------ 构造助手

    /// <summary>规则要的 FileEntry：模拟扫描出来的一条文件。</summary>
    static FileEntry File(string path, long size) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Kind = EntryKind.File,
        Modified = new DateTime(2024, 1, 1),
    };

    static FileEntry Dir(string path) => new()
    {
        Name = path,
        FullPath = path,
        Kind = EntryKind.Directory,
    };

    static CleanItem Temp(string path, long size) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Reason = "apps recreate these",
        Purpose = CleanPurpose.Temp,
        Group = "临时/缓存",
        Risk = CleanRisk.Safe,
        CanDelete = true,
    };

    static CleanItem TempConfirm(string path, long size)
    {
        var x = Temp(path, size);
        x.Risk = CleanRisk.Confirm;
        return x;
    }

    static CleanItem Browser(string path, long size) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Purpose = CleanPurpose.BrowserCache,
        Category = "browser",
        Group = "Chrome 缓存",
        Risk = CleanRisk.Safe,
        CanDelete = true,
    };

    static CleanItem Large(string path, long size) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Purpose = CleanPurpose.Large,
        Group = "大文件",
        Risk = CleanRisk.Confirm,
        CanDelete = true,
    };

    static CleanItem Old(string path, long size) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Purpose = CleanPurpose.Old,
        Group = "老文件",
        Risk = CleanRisk.Confirm,
        CanDelete = true,
    };

    static CleanItem Unidentified(string path, long size) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Reason = "apps recreate these",
        Purpose = CleanPurpose.Temp,
        Group = "临时/缓存",
        Risk = CleanRisk.Safe,
        CanDelete = true,
    };

    static CleanItem Dir(string path, long size) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Purpose = CleanPurpose.Temp,
        Group = "临时/缓存",
        Risk = CleanRisk.Safe,
        CanDelete = true,
        IsDirectory = true,
    };

    static CleanItem NoDelete(string path, long size) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Purpose = CleanPurpose.Temp,
        Group = "临时/缓存",
        Risk = CleanRisk.Confirm,
        CanDelete = false,
    };

    static CleanItem NoDeleteSafe(string path, long size) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Purpose = CleanPurpose.Temp,
        Group = "临时/缓存",
        Risk = CleanRisk.Safe,
        CanDelete = false,
    };

    static CleanItem Keep(string path, long size) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Purpose = CleanPurpose.Temp,
        Group = "系统",
        Risk = CleanRisk.Keep,
        CanDelete = false,
    };

    static CleanItem Dup(string path, long size, string handling, bool canDelete, bool selected) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = size,
        Purpose = CleanPurpose.Duplicate,
        Group = "重复",
        Handling = handling,
        Risk = CleanRisk.Confirm,
        CanDelete = canDelete,
        Selected = selected,
    };
}
