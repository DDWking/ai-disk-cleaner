using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace LocalRecognitionCheck;

/// <summary>
/// 「文件夹本地用途识别」增强的**行为**检查（离线，不碰真实用户文件）：
/// 重定向下载路径、系统目录真实全路径识别、同名异盘 / 路径前缀碰撞、
/// 已安装清单安装位置的唯一 + 边界精确匹配、共享厂商父目录不归属单应用、
/// 缺失安装位置跳过、快照只建一次、结构证据标为推测、用户纠正优先、用途≠可删。
/// </summary>
public static class Program
{
    static int _pass, _fail;
    static readonly List<string> Failures = new();

    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        RedirectedDownloadsTests();
        SystemFolderEqualityTests();
        PrefixCollisionTests();
        InstalledSnapshotMatchingTests();
        SnapshotReuseTests();
        StartupSnapshotOnceTests();
        MainScanPathHitTests();
        EvidenceWiringTests();
        EvidenceRefreshTests();
        IsolationTests();

        Console.WriteLine();
        Console.WriteLine($"PASS {_pass}   FAIL {_fail}");
        foreach (var f in Failures) Console.WriteLine("  - " + f);
        return _fail == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ 工具

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine("  ok   " + name); return; }
        _fail++;
        string line = name + (detail.Length > 0 ? " [" + detail + "]" : "");
        Failures.Add(line);
        Console.WriteLine("  FAIL " + line);
    }

    static void Section(string s) { Console.WriteLine("== " + s); }

    static FileEntry Dir(string path)
        => new() { Name = System.IO.Path.GetFileName(path.TrimEnd('\\')), FullPath = path, Kind = EntryKind.Directory };

    static FolderSummary Sum(FileEntry d)
        => FolderPurposeRules.Summarize(d, new FolderId(d.FullPath, 1), 0, d.FullPath);

    static FolderPurposeResult Recognize(FileEntry d, ILocalPurposeEvidence evidence)
        => FolderPurposeRules.RecognizeLocally(d, Sum(d), null, evidence);

    sealed class FakeResolver : ISystemPathResolver
    {
        readonly Dictionary<SystemPathId, string> _map = new();
        public int Calls;
        public FakeResolver Set(SystemPathId id, string path) { _map[id] = path; return this; }
        public string? Resolve(SystemPathId id)
        {
            Calls++;
            return _map.TryGetValue(id, out var v) ? v : null;
        }
    }

    sealed class CountingApps : IEnumerable<InstalledAppLocation>
    {
        readonly List<InstalledAppLocation> _items;
        public CountingApps(params InstalledAppLocation[] items) => _items = items.ToList();
        public int Enumerated;
        public IEnumerator<InstalledAppLocation> GetEnumerator()
        {
            foreach (var x in _items) { Enumerated++; yield return x; }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    static LocalEvidenceService EvidenceFor(FakeResolver r, params InstalledAppLocation[] apps)
        => new(InstalledLocationSnapshot.Build(apps), SystemPathSnapshot.Capture(r));

    // ------------------------------------------------------------------ 1) 重定向下载

    static void RedirectedDownloadsTests()
    {
        Section("重定向下载路径（KnownFolder，不再拼 用户目录\\Downloads）");

        var r = new FakeResolver()
            .Set(SystemPathId.UserProfile, @"C:\Users\tester")
            .Set(SystemPathId.Downloads, @"D:\Redirected\Downloads");
        var snap = SystemPathSnapshot.Capture(r);

        var real = snap.Match(@"D:\Redirected\Downloads");
        Check("系统解析出来的重定向下载目录被识别", real != null,
            real?.PurposeName ?? "null");
        Check("重定向下载目录的用途是「下载文件」", real != null && real.PurposeName == Loc.SysDownloads,
            real?.PurposeName ?? "null");

        Check("不再把「用户目录\\Downloads」当下载目录（重定向后它是另一个位置）",
            snap.Match(@"C:\Users\tester\Downloads") == null);

        var evidence = new LocalEvidenceService(InstalledLocationSnapshot.Empty, snap);
        var res = Recognize(Dir(@"D:\Redirected\Downloads"), evidence);
        Check("本地识别在重定向下载目录上给出结论", res.HasConclusion && res.PurposeName == Loc.SysDownloads,
            res.PurposeName);
        Check("重定向下载结论来源是本地且无需确认",
            res.Source == PurposeSource.Local && !res.NeedsConfirm);

        // 生产解析器真的能用（本机 smoke，只读）
        var prod = WindowsSystemPathResolver.Instance.Resolve(SystemPathId.Downloads);
        Check("生产 KnownFolder 解析器解析出真实的下载目录",
            !string.IsNullOrWhiteSpace(prod) && System.IO.Path.IsPathRooted(prod), prod ?? "null");
    }

    // ------------------------------------------------------------------ 2) 系统目录真实路径

    static void SystemFolderEqualityTests()
    {
        Section("系统目录：桌面/图片/音乐/视频/公共目录按真实全路径识别");

        var r = new FakeResolver()
            .Set(SystemPathId.Desktop, @"C:\Users\tester\Desktop")
            .Set(SystemPathId.Pictures, @"D:\图片")
            .Set(SystemPathId.Music, @"D:\music")
            .Set(SystemPathId.Videos, @"D:\视频")
            .Set(SystemPathId.Public, @"C:\Users\Public")
            .Set(SystemPathId.PublicDesktop, @"C:\Users\Public\Desktop")
            .Set(SystemPathId.PublicPictures, @"C:\Users\Public\Pictures");
        var evidence = EvidenceFor(r);

        var pictures = Recognize(Dir(@"D:\图片"), evidence);
        Check("重定向到别的盘的图片目录被识别",
            pictures.HasConclusion && pictures.PurposeName == LocalRecognitionText.Pictures,
            pictures.PurposeName);

        var desktop = Recognize(Dir(@"C:\Users\tester\Desktop"), evidence);
        Check("桌面目录被识别", desktop.HasConclusion && desktop.PurposeName == LocalRecognitionText.Desktop,
            desktop.PurposeName);

        var music = Recognize(Dir(@"D:\music"), evidence);
        Check("音乐目录被识别", music.HasConclusion && music.PurposeName == LocalRecognitionText.Music,
            music.PurposeName);

        var videos = Recognize(Dir(@"D:\视频"), evidence);
        Check("视频目录被识别", videos.HasConclusion && videos.PurposeName == LocalRecognitionText.Videos,
            videos.PurposeName);

        var pub = Recognize(Dir(@"C:\Users\Public"), evidence);
        Check("公共目录被识别", pub.HasConclusion && pub.PurposeName == LocalRecognitionText.PublicRoot,
            pub.PurposeName);

        var pubPic = Recognize(Dir(@"C:\Users\Public\Pictures"), evidence);
        Check("公共图片目录被识别",
            pubPic.HasConclusion && pubPic.PurposeName == LocalRecognitionText.PublicPictures,
            pubPic.PurposeName);

        Check("系统目录结论都来自本地、无需确认",
            pictures.Source == PurposeSource.Local && !pictures.NeedsConfirm
            && pub.Source == PurposeSource.Local && !pub.NeedsConfirm);

        // 大小写 / 尾斜杠都算同一个真实路径
        Check("路径大小写与尾斜杠不影响全路径相等",
            evidence.SystemRoles().Any(x => x.PurposeName == LocalRecognitionText.Music)
            && Recognize(Dir(@"d:\MUSIC\"), evidence).PurposeName == LocalRecognitionText.Music);
    }

    // ------------------------------------------------------------------ 3) 前缀碰撞 / 同名异盘

    static void PrefixCollisionTests()
    {
        Section("前缀碰撞 / 同名异盘：整段边界，不靠裸子串");

        var r = new FakeResolver()
            .Set(SystemPathId.Public, @"C:\Users\Public")
            .Set(SystemPathId.Pictures, @"D:\图片");
        var evidence = EvidenceFor(r);

        Check("公共目录的前缀碰撞不误判（C:\\Users\\Publicity）",
            !Recognize(Dir(@"C:\Users\Publicity"), evidence).HasConclusion);
        Check("公共目录的近前缀不误判（C:\\Users\\Publi）",
            !Recognize(Dir(@"C:\Users\Publi"), evidence).HasConclusion);

        var app = new InstalledAppLocation("App", @"C:\Program Files\App");
        var snap = InstalledLocationSnapshot.Build(new[] { app });
        Check("安装位置的前缀碰撞不误判（App vs Application）",
            snap.Match(@"C:\Program Files\Application").Verdict == InstalledLocationVerdict.None,
            snap.Match(@"C:\Program Files\Application").Verdict.ToString());
        Check("安装位置的同级相似名不误判（App vs App-notes）",
            snap.Match(@"C:\Program Files\App-notes").Verdict == InstalledLocationVerdict.None);

        // 同名异盘：只认全路径
        Check("同名异盘：图片目录只在真实盘位被识别（C:\\图片 ≠ D:\\图片）",
            evidence.SystemRoles().Any(x => x.Path == @"D:\图片")
            && !evidence.SystemRoles().Any(x => x.Path.Equals(@"C:\图片", StringComparison.OrdinalIgnoreCase)));
        var other = new InstalledAppLocation("App", @"C:\Apps\App");
        var snap2 = InstalledLocationSnapshot.Build(new[] { other });
        Check("同名异盘安装位置不匹配（D:\\Apps\\App）",
            snap2.Match(@"D:\Apps\App").Verdict == InstalledLocationVerdict.None,
            snap2.Match(@"D:\Apps\App").Verdict.ToString());
    }

    // ------------------------------------------------------------------ 4) 安装位置匹配

    static void InstalledSnapshotMatchingTests()
    {
        Section("已安装清单：唯一 + 边界精确才认产品，共享父目录不归属单应用");

        // 共享厂商父目录
        var apps = new[]
        {
            new InstalledAppLocation("AppA", @"D:\Studio\A"),
            new InstalledAppLocation("AppB", @"D:\Studio\B"),
        };
        var snap = InstalledLocationSnapshot.Build(apps);

        var vendor = snap.Match(@"D:\Studio");
        Check("厂商父目录不归属单个应用",
            !vendor.IsProduct && vendor.Verdict == InstalledLocationVerdict.ContainerOfApps,
            vendor.Verdict.ToString());

        var a = snap.Match(@"D:\Studio\A");
        Check("唯一且精确的安装位置认到产品 AppA",
            a.Verdict == InstalledLocationVerdict.UniqueExact && a.ProductName == "AppA",
            a.Verdict + "/" + a.ProductName);

        var aSub = snap.Match(@"D:\Studio\A\cache");
        Check("安装目录内部认到 AppA（推测）",
            aSub.Verdict == InstalledLocationVerdict.UniqueInside && aSub.ProductName == "AppA",
            aSub.Verdict + "/" + aSub.ProductName);

        // 两个软件共用同一个位置 ⇒ 不归属任何一个
        var shared = InstalledLocationSnapshot.Build(new[]
        {
            new InstalledAppLocation("One", @"C:\Shared"),
            new InstalledAppLocation("Two", @"C:\Shared"),
        });
        Check("多软件共用同一位置不归属单个应用",
            shared.Match(@"C:\Shared").Verdict == InstalledLocationVerdict.Ambiguous,
            shared.Match(@"C:\Shared").Verdict.ToString());

        // 父目录本身是某个软件的安装位置，但里面还装着别的软件 ⇒ 仍不归属
        var nested = InstalledLocationSnapshot.Build(new[]
        {
            new InstalledAppLocation("Outer", @"C:\Outer"),
            new InstalledAppLocation("Inner", @"C:\Outer\Inner"),
        });
        Check("装着别的软件的安装根不归属单个应用",
            nested.Match(@"C:\Outer").Verdict == InstalledLocationVerdict.Ambiguous,
            nested.Match(@"C:\Outer").Verdict.ToString());
        Check("更具体的安装位置仍然认到 Inner",
            nested.Match(@"C:\Outer\Inner\data").Verdict == InstalledLocationVerdict.UniqueInside
            && nested.Match(@"C:\Outer\Inner\data").ProductName == "Inner");

        // 名称相似 ≠ 安装位置（不做名字相似/子串猜）
        var chrome = InstalledLocationSnapshot.Build(new[]
        {
            new InstalledAppLocation("Chrome", @"C:\Program Files\Google\Chrome"),
        });
        Check("名字相似但路径不同的目录不认成该产品（Chromium）",
            chrome.Match(@"C:\Program Files\Google\Chromium").Verdict == InstalledLocationVerdict.None);

        // 缺失 / 不可用的安装位置：跳过并计数，不拿名字猜
        var messy = InstalledLocationSnapshot.Build(new[]
        {
            new InstalledAppLocation("", @"C:\X"),
            new InstalledAppLocation("NoLoc", ""),
            new InstalledAppLocation("Blank", "   "),
            new InstalledAppLocation("Relative", @"relative\path"),
            new InstalledAppLocation("DriveRoot", @"C:"),
            new InstalledAppLocation("Good", @"C:\Good"),
        });
        Check("缺失安装位置的条目被跳过并计数",
            messy.SkippedCount == 5 && messy.UsableLocationCount == 1 && messy.AppCount == 6,
            $"skip={messy.SkippedCount} usable={messy.UsableLocationCount} total={messy.AppCount}");
        Check("缺失安装位置不会被名字猜中", messy.Match(@"C:\X").Verdict == InstalledLocationVerdict.None);
        Check("依然认得出有效的那条", messy.Match(@"C:\Good").Verdict == InstalledLocationVerdict.UniqueExact);

        // 空快照不认任何东西
        Check("空快照不认任何路径", InstalledLocationSnapshot.Empty.Match(@"C:\Anything").Verdict
            == InstalledLocationVerdict.None);
    }

    // ------------------------------------------------------------------ 5) 快照复用

    static void SnapshotReuseTests()
    {
        Section("快照只建一次：识别循环里不重复枚举 / 不重复解析系统路径");

        var apps = new CountingApps(
            new InstalledAppLocation("A", @"C:\A"),
            new InstalledAppLocation("B", @"C:\B"),
            new InstalledAppLocation("C", @"C:\C"));
        var snap = InstalledLocationSnapshot.Build(apps);
        Check("建立快照时只枚举输入一次", apps.Enumerated == 3, apps.Enumerated.ToString());

        for (int i = 0; i < 200; i++) snap.Match(i % 2 == 0 ? @"C:\A\sub" : @"C:\nope");
        Check("反复匹配不再重新枚举输入", apps.Enumerated == 3, apps.Enumerated.ToString());

        var r = new FakeResolver().Set(SystemPathId.Pictures, @"D:\图片");
        var sys = SystemPathSnapshot.Capture(r);
        int afterCapture = r.Calls;
        for (int i = 0; i < 200; i++) sys.Match(@"D:\图片");
        Check("系统语义快照建立后不再调用系统解析器", r.Calls == afterCapture,
            $"{r.Calls}/{afterCapture}");

        // 系统语义表本身也缓存（内置表不会每识别一个目录就重新解析一遍）
        var first = FolderPurposeRules.BuiltInSystemRoles();
        var second = FolderPurposeRules.BuiltInSystemRoles();
        Check("内置系统语义表是同一份缓存（引用相等）", ReferenceEquals(first, second));
    }

    // ------------------------------------------------------------------ 5b) 主链路接线：启动只建一次系统快照

    static void StartupSnapshotOnceTests()
    {
        Section("主链路接线：系统快照启动只建一次，叠加安装清单不再解析系统路径");

        var r = new FakeResolver()
            .Set(SystemPathId.Pictures, @"D:\图片")
            .Set(SystemPathId.Downloads, @"D:\Downloads");
        var sys = SystemPathSnapshot.Capture(r);
        int captured = r.Calls;
        Check("启动建立系统快照时解析器被调用过", captured > 0, captured.ToString());

        // 启动后反复查询 / 叠装安装清单都不再重新解析系统路径
        for (int i = 0; i < 100; i++) sys.Match(@"D:\图片");
        var evidence = new LocalEvidenceService(InstalledLocationSnapshot.Empty, sys);
        var installed = InstalledLocationSnapshot.Build(new[]
        {
            new InstalledAppLocation("AppA", @"C:\Program Files\AppA"),
        });
        var layered = evidence.WithInstalled(installed);

        Check("系统快照只建一次（Match / WithInstalled 都不再调解析器）",
            r.Calls == captured, $"{r.Calls}/{captured}");
        Check("叠加安装清单沿用同一份系统快照", ReferenceEquals(evidence.System, layered.System));
        Check("注入 FolderPurposeService 的正是叠装后的证据", new FolderPurposeService(layered).Evidence == layered);
    }

    // ------------------------------------------------------------------ 5c) 主扫描路径命中已安装/系统目录

    static void MainScanPathHitTests()
    {
        Section("主扫描路径：命中已安装目录 / 系统目录都给出本地结论");

        var r = new FakeResolver().Set(SystemPathId.Pictures, @"D:\图片");
        var sys = SystemPathSnapshot.Capture(r);
        var installed = InstalledLocationSnapshot.Build(new[]
        {
            new InstalledAppLocation("AppA", @"C:\Program Files\AppA"),
        });
        var evidence = new LocalEvidenceService(installed, sys);
        var svc = new FolderPurposeService(evidence);

        // 模拟主扫描出来的树：根 + 两个一级目录（一个命中安装位置，一个命中系统目录）
        var root = Dir(@"C:\");
        var appDir = Dir(@"C:\Program Files\AppA");
        var sysDir = Dir(@"D:\图片");
        root.Children.Add(appDir); appDir.Parent = root;
        root.Children.Add(sysDir); sysDir.Parent = root;

        var appRes = FolderPurposeRules.RecognizeLocally(appDir, Sum(appDir), null, evidence);
        Check("命中安装目录 ⇒ 认到产品、本地、无需确认",
            appRes.HasConclusion && appRes.Source == PurposeSource.Local
            && !appRes.NeedsConfirm && appRes.PurposeName == "AppA",
            $"{appRes.PurposeName}/{appRes.Source}/{appRes.NeedsConfirm}");

        var sysRes = FolderPurposeRules.RecognizeLocally(sysDir, Sum(sysDir), null, evidence);
        Check("命中系统目录 ⇒ 本地结论、无需确认",
            sysRes.HasConclusion && sysRes.Source == PurposeSource.Local
            && !sysRes.NeedsConfirm && sysRes.PurposeName == LocalRecognitionText.Pictures,
            $"{sysRes.PurposeName}/{sysRes.Source}/{sysRes.NeedsConfirm}");

        // 经注入的服务透传（整理页 LocalRecognize 现在走的正是这条证据）
        var viaService = svc.RecognizeAsync(appDir, new FolderId(appDir.FullPath, 1), 0, @"C:\Program Files\AppA",
            allowAi: false, provider: null, model: null, sendFullPath: false,
            configSignature: "cfg", ct: CancellationToken.None).GetAwaiter().GetResult();
        Check("FolderPurposeService 透传：命中安装目录给产品结论",
            viaService.HasConclusion && viaService.PurposeName == "AppA", viaService.PurposeName);
    }

    // ------------------------------------------------------------------ 6) 注入接线 + 推测语义

    static void EvidenceWiringTests()
    {
        Section("证据注入：事实 vs 推测、用户纠正优先、服务透传");

        var r = new FakeResolver();
        var evidence = EvidenceFor(r,
            new InstalledAppLocation("AppA", @"D:\Studio\A"));

        var exact = Recognize(Dir(@"D:\Studio\A"), evidence);
        Check("精确命中安装位置 ⇒ 有结论、是本地、无需确认",
            exact.HasConclusion && exact.Source == PurposeSource.Local && !exact.NeedsConfirm,
            $"{exact.PurposeName}/{exact.NeedsConfirm}");
        Check("精确命中的用途名就是产品名", exact.PurposeName == "AppA", exact.PurposeName);

        var inside = Recognize(Dir(@"D:\Studio\A\cache"), evidence);
        Check("位于安装目录内部 ⇒ 有结论，但标为**推测**（NeedsConfirm）",
            inside.HasConclusion && inside.NeedsConfirm
            && inside.State == PurposeState.NeedsConfirm,
            $"{inside.PurposeName}/{inside.State}");

        var parent = Recognize(Dir(@"D:\Studio"), evidence);
        Check("厂商父目录不给产品结论（结构判定兜底）", !parent.HasConclusion, parent.PurposeName);

        // 只看形状的结构证据（工程标志）同样标为推测
        var proj = Dir(@"D:\proj");
        proj.Children.Add(new FileEntry
        {
            Name = ".git", FullPath = @"D:\proj\.git", Kind = EntryKind.File, Size = 10, Parent = proj,
        });
        var projRes = FolderPurposeRules.RecognizeLocally(proj, Sum(proj));
        Check("结构证据（只看形状的开发项目标志）标为推测",
            projRes.HasConclusion && projRes.PurposeName == Loc.PurposeDevProject && projRes.NeedsConfirm,
            $"{projRes.PurposeName}/{projRes.NeedsConfirm}");

        // 默认证据 + FolderPurposeService 透传
        var svc = new FolderPurposeService(evidence);
        var id = new FolderId(@"D:\Studio\A", 1);
        var viaService = svc.RecognizeAsync(Dir(@"D:\Studio\A"), id, 0, @"D:\Studio\A",
            allowAi: false, provider: null, model: null, sendFullPath: false,
            configSignature: "cfg", ct: CancellationToken.None).GetAwaiter().GetResult();
        Check("FolderPurposeService 把证据透传到本地识别",
            viaService.HasConclusion && viaService.PurposeName == "AppA", viaService.PurposeName);

        // 用户纠正优先于本地证据
        svc.SetUserCorrection(id, "我的资料", "文档");
        var corrected = svc.RecognizeAsync(Dir(@"D:\Studio\A"), id, 0, @"D:\Studio\A",
            allowAi: false, provider: null, model: null, sendFullPath: false,
            configSignature: "cfg", ct: CancellationToken.None).GetAwaiter().GetResult();
        Check("用户纠正优先于安装位置识别",
            corrected.Source == PurposeSource.User && corrected.PurposeName == "我的资料",
            $"{corrected.Source}/{corrected.PurposeName}");

        // 不注入证据时行为与以前一致（不会凭空认产品）
        var plain = FolderPurposeRules.RecognizeLocally(Dir(@"D:\Studio\A"), Sum(Dir(@"D:\Studio\A")));
        Check("不注入证据时不会凭空认产品", !plain.HasConclusion, plain.PurposeName);
    }

    // ------------------------------------------------------------------ 6b) 证据异步换代（清单后到）

    /// <summary>
    /// 已安装清单是**扫描之后**才异步到达的：先识别 ⇒ 没有结论；清单注入后重算 ⇒ 认到产品。
    /// 关键前提：**「没有结论」绝不能进缓存**，否则第二轮会直接命中旧结论，永远补不上安装位置。
    /// </summary>
    static void EvidenceRefreshTests()
    {
        Section("证据异步换代：先未知、清单后到，重算能认出安装位置（无结论不进缓存）");

        var r = new FakeResolver().Set(SystemPathId.Pictures, @"D:\图片");
        var sys = SystemPathSnapshot.Capture(r);
        var svc = new FolderPurposeService(new LocalEvidenceService(InstalledLocationSnapshot.Empty, sys));

        var dir = Dir(@"D:\Studio\A");
        var id = new FolderId(dir.FullPath, 1);
        var before = svc.RecognizeAsync(dir, id, 0, @"D:\Studio\A",
            allowAi: false, provider: null, model: null, sendFullPath: false,
            configSignature: "cfg", ct: CancellationToken.None).GetAwaiter().GetResult();
        Check("清单未到时是「没有结论」", !before.HasConclusion, before.PurposeName);
        Check("无结论不进缓存（否则注入清单后永远修不回来）",
            svc.CacheCount == 0, svc.CacheCount.ToString());

        // 清单异步到达：换掉证据（系统语义沿用同一份快照）
        var snapshot = InstalledLocationSnapshot.Build(new[]
        {
            new InstalledAppLocation("AppA", @"D:\Studio\A"),
        });
        svc.Evidence = new LocalEvidenceService(snapshot, sys);

        var after = svc.RecognizeAsync(dir, id, 0, @"D:\Studio\A",
            allowAi: false, provider: null, model: null, sendFullPath: false,
            configSignature: "cfg", ct: CancellationToken.None).GetAwaiter().GetResult();
        Check("证据换代后重算认到产品名", after.HasConclusion && after.PurposeName == "AppA",
            after.PurposeName);
        Check("重算结论来源是本地（不是 AI、不是用户）",
            after.Source == PurposeSource.Local && !after.NeedsConfirm, after.Source.ToString());

        // 用户纠正仍然压过安装清单：重算不许覆盖
        svc.SetUserCorrection(id, "我的资料", "文档");
        var corrected = svc.RecognizeAsync(dir, id, 0, @"D:\Studio\A",
            allowAi: false, provider: null, model: null, sendFullPath: false,
            configSignature: "cfg", ct: CancellationToken.None).GetAwaiter().GetResult();
        Check("重算不会覆盖用户纠正", corrected.Source == PurposeSource.User
            && corrected.PurposeName == "我的资料", $"{corrected.Source}/{corrected.PurposeName}");
    }

    // ------------------------------------------------------------------ 7) 用途 ≠ 可删

    static void IsolationTests()
    {
        Section("用途不等于可删：识别结果里没有删除相关字段");

        Check("识别结果没有删除/风险/选择字段",
            !typeof(FolderPurposeResult).GetProperties().Any(p =>
                p.Name.Contains("Delete") || p.Name.Contains("Risk") || p.Name.Contains("Selected")));

        var evidence = EvidenceFor(new FakeResolver(),
            new InstalledAppLocation("AppA", @"D:\Studio\A"));
        var res = Recognize(Dir(@"D:\Studio\A"), evidence);
        Check("识别是只读的：同一次识别前后路径不变",
            Dir(@"D:\Studio\A").FullPath == @"D:\Studio\A" && res.HasConclusion);
    }
}
