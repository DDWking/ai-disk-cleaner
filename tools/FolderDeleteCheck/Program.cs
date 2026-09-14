using AiDiskCleaner.Models;
using AiDiskCleaner.Native;
using AiDiskCleaner.Services;

namespace FolderDeleteCheck;

/// <summary>
/// 「手动删除文件夹」的独立回归检查。全部离线运行：只用假探针与假回收站操作，
/// 不删任何真实文件；真实 shell 冒烟测试必须显式设置 FOLDERDEL_CHECK_REAL=1 才会跑。
/// </summary>
public static class Program
{
    private static int _pass, _fail;
    private static readonly List<string> Failures = new();

    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        SelectionTests();
        GuardTests();
        PreviewTests();
        ExecuteTests();
        FailureSemanticsTests();
        BarTests();

        if (Environment.GetEnvironmentVariable("FOLDERDEL_CHECK_REAL") == "1") RealSmokeTests();

        Console.WriteLine();
        Console.WriteLine($"PASS {_pass}   FAIL {_fail}");
        foreach (var f in Failures) Console.WriteLine("  - " + f);
        return _fail == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- tools

    private static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine("== " + name);
    }

    private static void Check(string name, bool ok)
    {
        if (ok) { _pass++; return; }
        _fail++;
        Failures.Add(name);
        Console.WriteLine("  FAIL " + name);
    }

    private static FolderSelection Sel(FakeProbe probe, params string[] paths)
    {
        var picks = new List<Pick>();
        foreach (var p in paths)
        {
            var n = probe.Get(p);
            picks.Add(new Pick { IsSelected = true, FullPath = p, Name = Path.GetFileName(p), Size = n?.Size ?? 0 });
        }
        return FolderSelectionService.Build(picks.Select(p => p.ToPick()));
    }

    private static FolderDeletePreview Preview(FolderDeleteService svc, FolderSelection sel)
        => svc.Preview(sel.Targets, sel.SelectedCount, sel.NestedCount);

    private static FolderDeletePreview PreviewOf(FolderDeleteService svc, FakeProbe probe, params string[] paths)
        => Preview(svc, Sel(probe, paths));

    private static FolderDeleteItemResult? Item(FolderDeleteResult r, string path)
        => r.Items.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));

    private static bool Blocked(string path) => FolderDeleteGuard.ClassifyFolder(path).Guard == PathGuard.Blocked;
    private static bool Confirm(string path) => FolderDeleteGuard.ClassifyFolder(path).Guard == PathGuard.NeedsConfirm;

    // ---------------------------------------------------------------- 选择 / 去重

    private static void SelectionTests()
    {
        Section("手动选择与父子去重");

        var probe = new FakeProbe();
        probe.Set(@"C:\Data", n => { n.Size = 500; });
        probe.Set(@"C:\Data\sub", n => { n.Size = 200; });
        probe.Set(@"C:\Data\sub\deep", n => { n.Size = 100; });
        probe.Set(@"C:\Data2", n => { n.Size = 50; });

        var picks = new List<Pick>
        {
            new() { IsSelected = true, FullPath = @"C:\Data", Name = "Data", Size = 500 },
            new() { IsSelected = true, FullPath = @"c:\data\sub", Name = "sub", Size = 200 },
            new() { IsSelected = true, FullPath = @"C:\Data\sub\deep", Name = "deep", Size = 100 },
            new() { IsSelected = true, FullPath = @"C:\Data", Name = "Data", Size = 500 },
        };
        var sel = FolderSelectionService.Build(picks.Select(p => p.ToPick()));
        Check("选择：父目录在，子项与重复项被合并", sel.KeptCount == 1 && sel.Targets[0].Path == @"C:\Data");
        Check("选择：已选 4 / 合并 3", sel.SelectedCount == 4 && sel.NestedCount == 3);
        Check("选择：容量不重复计", sel.TotalBytes == 500);
        Check("选择：目标按目录处理", sel.Targets[0].IsDirectory && sel.Targets[0].Source == null);

        var onlyAi = new List<Pick>
        {
            new() { IsSelected = false, FullPath = @"C:\Data\sub", Name = "sub", Size = 200, AiSaysDelete = true },
        };
        Check("选择：AI 说能删不会自动勾选",
            FolderSelectionService.Build(onlyAi.Select(p => p.ToPick())).IsEmpty);
        Check("选择：未勾选的项不进目标",
            FolderSelectionService.Selected(onlyAi.Select(p => p.ToPick())).Count == 0);

        var siblings = Sel(probe, @"C:\Data", @"C:\Data2");
        Check("选择：同级目录都保留", siblings.KeptCount == 2 && siblings.NestedCount == 0);

        var mixed = new List<Pick>
        {
            new() { IsSelected = true, FullPath = @"C:\x", Name = "x" },
            new() { IsSelected = false, FullPath = @"C:\y", Name = "y" },
        };
        Check("选择：只统计勾选项", FolderSelectionService.Selected(mixed.Select(p => p.ToPick())).Count == 1);

        var empty = FolderSelectionService.Build(null);
        Check("选择：空输入安全", empty.IsEmpty && empty.SelectedCount == 0 && empty.TotalBytes == 0);
    }

    // ---------------------------------------------------------------- 防护

    private static void GuardTests()
    {
        Section("文件夹专项防护");

        Check("防护：盘符根", Blocked(@"C:\"));
        Check("防护：Windows 目录", Blocked(@"C:\Windows"));
        Check("防护：Windows 深层（ProtectedPaths 不拦，本层要拦）", Blocked(@"C:\Windows\Foo\Bar"));
        Check("防护：Program Files 容器本身", Blocked(@"C:\Program Files"));
        Check("防护：ProgramData 容器本身", Blocked(@"C:\ProgramData"));
        Check("防护：Users 容器本身", Blocked(@"C:\Users"));
        Check("防护：$Recycle.Bin", Blocked(@"C:\$Recycle.Bin"));
        Check("防护：System Volume Information", Blocked(@"C:\System Volume Information"));
        Check("防护：用户配置根需要确认", Confirm(@"C:\Users\Alice"));
        Check("防护：普通目录放行", !Blocked(@"C:\Data\projects") && !Confirm(@"C:\Data\projects"));

        Check("防护：ProtectedPaths 拦路径段 System32",
            ProtectedPaths.Classify(@"C:\Data\System32\junk").Guard == PathGuard.Blocked);
        Check("防护：ProtectedPaths 拦盘符根",
            ProtectedPaths.Classify(@"D:\").Guard == PathGuard.Blocked);

        // 链接 / 重解析：目标与祖先都硬拦
        var probe = new FakeProbe();
        probe.Set(@"C:\Data\link", n => n.ReparsePoint = true);
        probe.Set(@"C:\Data\link\child");
        probe.Set(@"C:\Data\plain");
        Check("防护：目标本身是链接 → 硬拦",
            FolderDeleteGuard.ClassifyForFolderDelete(@"C:\Data\link", probe).Guard == PathGuard.Blocked);
        Check("防护：祖先里有链接 → 硬拦",
            FolderDeleteGuard.ClassifyForFolderDelete(@"C:\Data\link\child", probe).Guard == PathGuard.Blocked);
        Check("防护：普通路径在探针下放行",
            FolderDeleteGuard.ClassifyForFolderDelete(@"C:\Data\plain", probe).Guard == PathGuard.Allowed);

        Check("防护：取最严结论", FolderDeleteGuard.Strictest(
            PathGuardResult.Allowed,
            PathGuardResult.Confirm("x", "y"),
            PathGuardResult.Allowed).Guard == PathGuard.NeedsConfirm);
        Check("防护：硬拦优先", FolderDeleteGuard.Strictest(
            PathGuardResult.Confirm("x", "y"),
            PathGuardResult.Blocked("z", "w")).Guard == PathGuard.Blocked);
    }

    // ---------------------------------------------------------------- 预览

    private static void PreviewTests()
    {
        Section("预览（只读）");

        var probe = new FakeProbe();
        probe.Set(@"C:\Data\a", n => n.Size = 1000);

        var svc = new FolderDeleteService(probe, new FakeRecycle { Probe = probe });
        var pv = PreviewOf(svc, probe, @"C:\Data\a");
        Check("预览：可删", pv.Items.Count == 1 && pv.Items[0].State == FolderDeleteItemState.Ready
            && pv.DeletableCount == 1 && pv.HasAnything);
        Check("预览：容量统计", pv.DeletableBytes == 1000);
        Check("预览：无选择时为空", svc.Preview(null).Items.Count == 0
            && !svc.Preview(null).HasSelection && !svc.Preview(null).HasAnything);

        var noBin = new FakeRecycle { Probe = probe, DefaultCanRecycle = false };
        var pv2 = PreviewOf(new FolderDeleteService(probe, noBin), probe, @"C:\Data\a");
        Check("预览：回收站不可用 → 不列入可删",
            pv2.Items[0].State == FolderDeleteItemState.RecycleUnavailable
            && !pv2.HasAnything && pv2.RecycleUnavailableCount == 1);

        probe.Set(@"C:\Data\link", n => { n.ReparsePoint = true; n.Size = 10; });
        var pv3 = PreviewOf(svc, probe, @"C:\Data\link");
        Check("预览：链接目标 → 硬拦", pv3.Items[0].State == FolderDeleteItemState.Blocked && !pv3.HasAnything);

        probe.Set(@"C:\Users\Alice", n => n.Size = 200);
        var pv4 = PreviewOf(svc, probe, @"C:\Users\Alice");
        Check("预览：用户配置根 → 需要确认",
            pv4.Items[0].State == FolderDeleteItemState.NeedsConfirm
            && pv4.HasAnything && pv4.SensitiveCount == 1);

        probe.Set(@"C:\Program Files\Vendor", n => n.Size = 300);
        var pv5 = PreviewOf(svc, probe, @"C:\Program Files\Vendor");
        Check("预览：软件目录一级 → 需要确认", pv5.Items[0].State == FolderDeleteItemState.NeedsConfirm);

        probe.Set(@"C:\Users");
        var pv6 = PreviewOf(svc, probe, @"C:\Users");
        Check("预览：Users 容器 → 硬拦", pv6.Items[0].State == FolderDeleteItemState.Blocked);

        probe.Set(@"C:\Data\System32\junk");
        var pv7 = PreviewOf(svc, probe, @"C:\Data\System32\junk");
        Check("预览：路径段命中保护 → 硬拦", pv7.Items[0].State == FolderDeleteItemState.Blocked);

        probe.Set(@"C:\Data\nested");
        var picks = new List<Pick>
        {
            new() { IsSelected = true, FullPath = @"C:\Data\a", Name = "a", Size = 1000 },
            new() { IsSelected = true, FullPath = @"C:\Data\a\nested", Name = "nested", Size = 1 },
        };
        var sel = FolderSelectionService.Build(picks.Select(p => p.ToPick()));
        var pv8 = Preview(svc, sel);
        Check("预览：父/子已合并，只留父", sel.NestedCount == 1 && pv8.Items.Count == 1 && pv8.NestedCount == 1);

        // 每一项都有可读原因（界面不只给「失败」两个字）
        Check("预览：每行都有原因", pv4.Items[0].Reason.Length > 0 && pv7.Items[0].Reason.Length > 0);
    }

    // ---------------------------------------------------------------- 执行

    private static void ExecuteTests()
    {
        Section("执行：成功 / 身份 / 确认门槛");

        // 正常成功
        var probe = new FakeProbe();
        probe.Set(@"C:\Data\a", n => { n.Size = 1000; n.Identity = new FileIdentity(1, 10, 0); });
        var rec = new FakeRecycle { Probe = probe };
        var svc = new FolderDeleteService(probe, rec);
        var pv = PreviewOf(svc, probe, @"C:\Data\a");
        var res = svc.Execute(pv, FolderDeleteConsent.None, confirm: _ => true);
        Check("执行：成功进回收站", res.State == FolderDeleteState.Completed && res.Recycled == 1);
        Check("执行：后端只调用一次", rec.Calls.Count == 1);
        Check("执行：路径消失", !probe.Get(@"C:\Data\a")!.Exists);
        Check("执行：释放容量按扫描值", res.FreedBytes == 1000);
        Check("执行：单项结局", Item(res, @"C:\Data\a")!.Outcome == FolderDeleteOutcome.Recycled);

        // 确认前取消 => 零后端调用
        var probe2 = new FakeProbe();
        probe2.Set(@"C:\Data\b", n => n.Size = 10);
        var rec2 = new FakeRecycle { Probe = probe2 };
        var svc2 = new FolderDeleteService(probe2, rec2);
        var pv2 = PreviewOf(svc2, probe2, @"C:\Data\b");
        var res2 = svc2.Execute(pv2, FolderDeleteConsent.None, confirm: _ => false);
        Check("执行：确认前取消 = 零后端调用", rec2.Calls.Count == 0);
        Check("执行：确认前取消状态", res2.State == FolderDeleteState.Canceled && res2.Canceled
            && res2.Items.All(x => x.Outcome == FolderDeleteOutcome.SkippedByUser));
        Check("执行：确认前取消不碰文件", probe2.Get(@"C:\Data\b")!.Exists);

        // 预检到执行之间路径消失
        probe2.Set(@"C:\Data\gone", n => n.Size = 10);
        var pv3 = PreviewOf(svc2, probe2, @"C:\Data\gone");
        probe2.MarkDeleted(@"C:\Data\gone");
        var res3 = svc2.Execute(pv3, FolderDeleteConsent.None, confirm: _ => true);
        Check("执行：执行前消失 → NotFound",
            Item(res3, @"C:\Data\gone")!.Outcome == FolderDeleteOutcome.NotFound);
        Check("执行：消失项不调用后端", rec2.Calls.Count == 0);

        // 身份被换掉（同一个路径换成了别的文件夹）
        probe2.Set(@"C:\Data\swap", n => { n.Size = 10; n.Identity = new FileIdentity(1, 500, 0); });
        var pv4 = PreviewOf(svc2, probe2, @"C:\Data\swap");
        probe2.Get(@"C:\Data\swap")!.Identity = new FileIdentity(1, 501, 0);
        var res4 = svc2.Execute(pv4, FolderDeleteConsent.None, confirm: _ => true);
        Check("执行：身份变化 → PathChanged",
            Item(res4, @"C:\Data\swap")!.Outcome == FolderDeleteOutcome.PathChanged);
        Check("执行：身份变化不调用后端", rec2.Calls.Count == 0);

        // 位置变成文件
        probe2.Set(@"C:\Data\notdir", n => n.Size = 10);
        var pv5 = PreviewOf(svc2, probe2, @"C:\Data\notdir");
        probe2.Get(@"C:\Data\notdir")!.IsDirectory = false;
        var res5 = svc2.Execute(pv5, FolderDeleteConsent.None, confirm: _ => true);
        Check("执行：位置变成文件 → Replaced",
            Item(res5, @"C:\Data\notdir")!.Outcome == FolderDeleteOutcome.Replaced);
        Check("执行：变成文件不调用后端", rec2.Calls.Count == 0);

        // 敏感位置：未确认跳过 / 确认后执行
        probe2.Set(@"C:\Program Files\Vendor", n => n.Size = 300);
        var pv6 = PreviewOf(svc2, probe2, @"C:\Program Files\Vendor");
        var res6 = svc2.Execute(pv6, FolderDeleteConsent.None, confirm: _ => true);
        Check("执行：敏感位置未确认 → 跳过",
            Item(res6, @"C:\Program Files\Vendor")!.Outcome == FolderDeleteOutcome.SkippedByUser);
        Check("执行：敏感位置未确认不调用后端", rec2.Calls.Count == 0);
        var res7 = svc2.Execute(pv6, FolderDeleteConsent.SensitiveConfirmed, confirm: _ => true);
        Check("执行：敏感位置确认后执行",
            Item(res7, @"C:\Program Files\Vendor")!.Outcome == FolderDeleteOutcome.Recycled
            && rec2.Calls.Count == 1);

        // 回收站不可用：执行阶段也不调用
        var rec3 = new FakeRecycle { Probe = probe2, DefaultCanRecycle = false };
        probe2.Set(@"C:\Data\nobin", n => n.Size = 10);
        var svc3 = new FolderDeleteService(probe2, rec3);
        var pv7 = PreviewOf(svc3, probe2, @"C:\Data\nobin");
        var res8 = svc3.Execute(pv7, FolderDeleteConsent.SensitiveConfirmed, confirm: _ => true);
        Check("执行：回收站不可用 = 不调用后端", rec3.Calls.Count == 0);
        Check("执行：回收站不可用如实登记",
            res8.RecycleUnavailable == 1 && Item(res8, @"C:\Data\nobin")!.Outcome == FolderDeleteOutcome.RecycleUnavailable);

        // 部分失败：一个成功，一个回收站不可用
        var probe4 = new FakeProbe();
        probe4.Set(@"C:\Data\ok", n => n.Size = 100);
        probe4.Set(@"C:\Data\nobin2", n => n.Size = 50);
        var rec4 = new FakeRecycle { Probe = probe4 };
        rec4.CanRecycleMap[FakeProbe.Norm(@"C:\Data\nobin2")] = false;
        var svc4 = new FolderDeleteService(probe4, rec4);
        var sel4 = Sel(probe4, @"C:\Data\ok", @"C:\Data\nobin2");
        var pv8 = Preview(svc4, sel4);
        Check("执行：预览区分可删与不可删", pv8.DeletableCount == 1 && pv8.RecycleUnavailableCount == 1);
        var res9 = svc4.Execute(pv8, FolderDeleteConsent.None, confirm: _ => true);
        Check("执行：部分完成状态", res9.State == FolderDeleteState.PartialFailure
            && res9.Recycled == 1 && res9.RecycleUnavailable == 1);
        Check("执行：只对可删项调用后端", rec4.Calls.Count == 1 && rec4.Calls[0] == @"C:\Data\ok");

        // 执行中取消：停后续项，保留已处理项结果
        var probe5 = new FakeProbe();
        probe5.Set(@"C:\Data\aa", n => n.Size = 10);
        probe5.Set(@"C:\Data\bb", n => n.Size = 10);
        var rec5 = new FakeRecycle { Probe = probe5 };
        var svc5 = new FolderDeleteService(probe5, rec5);
        var pv9 = PreviewOf(svc5, probe5, @"C:\Data\aa", @"C:\Data\bb");
        using var cts = new CancellationTokenSource();
        var res10 = svc5.Execute(pv9, FolderDeleteConsent.None, confirm: _ => true,
            new ActionProgress<FolderDeleteItemResult>(r => { if (r.Recycled) cts.Cancel(); }), cts.Token);
        Check("执行：执行中取消 → Canceled", res10.State == FolderDeleteState.Canceled && res10.Canceled);
        Check("执行：取消保留已处理项结果", res10.Recycled == 1 && rec5.Calls.Count == 1);
        Check("执行：取消后的项如实登记未处理",
            res10.Items.Count == 2 && res10.Items.Count(x => x.Outcome == FolderDeleteOutcome.SkippedByUser) == 1);

        // 忙：执行中再次请求直接被拒绝
        var probe6 = new FakeProbe();
        probe6.Set(@"C:\Data\cc", n => n.Size = 10);
        var rec6 = new FakeRecycle { Probe = probe6 };
        var svc6 = new FolderDeleteService(probe6, rec6);
        var pv10 = PreviewOf(svc6, probe6, @"C:\Data\cc");
        FolderDeleteResult? inner = null;
        var res11 = svc6.Execute(pv10, FolderDeleteConsent.None, confirm: _ => true,
            new ActionProgress<FolderDeleteItemResult>(_ =>
            {
                if (inner == null) inner = svc6.Execute(pv10, FolderDeleteConsent.None, confirm: _ => true);
            }), CancellationToken.None);
        Check("执行：忙时第二次请求返回 Busy", inner != null && inner.State == FolderDeleteState.Busy);
        Check("执行：忙时第二次不产生额外后端调用", rec6.Calls.Count == 1 && res11.Recycled == 1);
        Check("执行：结束后不再忙", !svc6.IsBusy);
    }

    // ---------------------------------------------------------------- 失败语义

    private static void FailureSemanticsTests()
    {
        Section("失败语义：不确定 / 部分完成 / 无永久删除");

        // shell 成功但路径还在 => Failed（不能说「已回收」）
        var probe = new FakeProbe();
        probe.Set(@"C:\Data\stick", n => n.Size = 10);
        var rec = new FakeRecycle { Probe = probe, RemovePathOnCall = false };
        var svc = new FolderDeleteService(probe, rec);
        var pv = PreviewOf(svc, probe, @"C:\Data\stick");
        var res = svc.Execute(pv, FolderDeleteConsent.None, confirm: _ => true);
        Check("失败：shell 报成功但路径仍在 → Failed",
            Item(res, @"C:\Data\stick")!.Outcome == FolderDeleteOutcome.Failed && res.Recycled == 0);

        // shell 报错但路径已消失 => Uncertain（不能冒认回收，也不能说「拒绝了」）
        probe.Set(@"C:\Data\vanished", n => n.Size = 10);
        rec.RemovePathOnCall = true;
        rec.Results[FakeProbe.Norm(@"C:\Data\vanished")] = RecycleShellResult.Fail(unchecked((int)0x80070005), "denied");
        var pv2 = PreviewOf(svc, probe, @"C:\Data\vanished");
        var res2 = svc.Execute(pv2, FolderDeleteConsent.None, confirm: _ => true);
        var it2 = Item(res2, @"C:\Data\vanished")!;
        Check("失败：报错但路径已消失 → Uncertain", it2.Outcome == FolderDeleteOutcome.Uncertain);
        Check("失败：不确定不冒认回收", res2.Recycled == 0 && !it2.Recycled);
        Check("失败：不确定文案明确要核对回收站", it2.Message.Contains("回收站"));

        // shell 报错且路径仍在 => 按错误码分类，路径没动
        probe.Set(@"C:\Data\denied", n => n.Size = 10);
        rec.RemovePathOnCall = false;
        rec.Results[FakeProbe.Norm(@"C:\Data\denied")] = RecycleShellResult.Fail(unchecked((int)0x80070005), "denied");
        var pv3 = PreviewOf(svc, probe, @"C:\Data\denied");
        var res3 = svc.Execute(pv3, FolderDeleteConsent.None, confirm: _ => true);
        Check("失败：拒绝访问 → AccessDenied 且文件还在",
            Item(res3, @"C:\Data\denied")!.Outcome == FolderDeleteOutcome.AccessDenied
            && probe.Get(@"C:\Data\denied")!.Exists);

        // shell 中断且路径仍在 => PartiallyRemoved
        probe.Set(@"C:\Data\aborted", n => n.Size = 10);
        rec.Results[FakeProbe.Norm(@"C:\Data\aborted")] = RecycleShellResult.Abort(unchecked((int)0x800704C7), "aborted");
        var pv4 = PreviewOf(svc, probe, @"C:\Data\aborted");
        var res4 = svc.Execute(pv4, FolderDeleteConsent.None, confirm: _ => true);
        Check("失败：中断且路径仍在 → PartiallyRemoved",
            Item(res4, @"C:\Data\aborted")!.Outcome == FolderDeleteOutcome.PartiallyRemoved);

        // shell 中断但路径已消失 => Uncertain
        probe.Set(@"C:\Data\abortedgone", n => n.Size = 10);
        rec.RemovePathOnCall = true;
        rec.Results[FakeProbe.Norm(@"C:\Data\abortedgone")] = RecycleShellResult.Abort(unchecked((int)0x800704C7), "aborted");
        var pv5 = PreviewOf(svc, probe, @"C:\Data\abortedgone");
        var res5 = svc.Execute(pv5, FolderDeleteConsent.None, confirm: _ => true);
        Check("失败：中断但路径已消失 → Uncertain",
            Item(res5, @"C:\Data\abortedgone")!.Outcome == FolderDeleteOutcome.Uncertain);

        // 错误码分类
        Check("失败分类：5 → AccessDenied",
            FolderDeleteService.ClassifyShellFailure(unchecked((int)0x80070005)).Outcome == FolderDeleteOutcome.AccessDenied);
        Check("失败分类：32 → InUse",
            FolderDeleteService.ClassifyShellFailure(unchecked((int)0x80070020)).Outcome == FolderDeleteOutcome.InUse);
        Check("失败分类：其他 → Failed",
            FolderDeleteService.ClassifyShellFailure(unchecked((int)0x80070002)).Outcome == FolderDeleteOutcome.Failed);

        // 结构保证：回收接口只有「查可用」与「送回收站」，没有永久删除
        var methods = typeof(IFolderRecycleOperation).GetMethods().Select(m => m.Name).OrderBy(x => x).ToArray();
        Check("结构：回收接口没有永久删除方法",
            methods.Length == 2 && methods[0] == "CanRecycle" && methods[1] == "Recycle");
        Check("结构：回收必用标志包含 FOFX_RECYCLEONDELETE",
            (FolderRecycleNative.RecycleRequiredFlags & FolderRecycleNative.FOFX_RECYCLEONDELETE) != 0);
        Check("结构：包含「销毁前警告」标志",
            (FolderRecycleNative.RecycleRequiredFlags & FolderRecycleNative.FOF_WANTNUKEWARNING) != 0);
        Check("结构：包含早失败标志",
            (FolderRecycleNative.RecycleRequiredFlags & FolderRecycleNative.FOFX_EARLYFAILURE) != 0);
        Check("结构：不确定与回收是两种不同结局",
            FolderDeleteText.Outcome(FolderDeleteOutcome.Uncertain)
            != FolderDeleteText.Outcome(FolderDeleteOutcome.Recycled));
    }

    // ---------------------------------------------------------------- 底部栏

    private static void BarTests()
    {
        Section("底部操作栏状态");

        var bar = new FolderDeleteBar();
        bar.Refresh(0, 0, 0, 0);
        Check("栏：无选择不可删，也不占一行教学文案",
            !bar.HasSelection && !bar.CanDelete && bar.StateText.Length == 0);

        bar.Refresh(2, 1, 1, 1024);
        Check("栏：有选择可删", bar.HasSelection && bar.CanDelete);
        Check("栏：选择文案含合并数", bar.SelectedText.Contains("1"));

        bar.Begin(3);
        Check("栏：忙时禁用删除、可取消", bar.IsBusy && bar.CanCancel && !bar.CanDelete);
        bar.Report(1, 3);
        Check("栏：进度", bar.ProgressText == "1/3");
        bar.RequestCancel();
        Check("栏：请求取消后不可再取消", !bar.CanCancel && bar.StateText.Length > 0);

        var result = new FolderDeleteResult { State = FolderDeleteState.PartialFailure, Canceled = false };
        result.Items.Add(new FolderDeleteItemResult { Path = @"C:\a", Name = "a", Outcome = FolderDeleteOutcome.Recycled });
        result.Items.Add(new FolderDeleteItemResult
        {
            Path = @"C:\b", Name = "b", Outcome = FolderDeleteOutcome.Uncertain, Message = "报错但已消失",
        });
        bar.Finish(result);
        Check("栏：结束后不忙且有汇总", !bar.IsBusy && bar.StateText.Length > 0);
        Check("栏：不确定项进入提示", bar.HasNote && bar.NoteText.Contains("b"));
        string headline = bar.StateText;
        bar.Refresh(0, 0, 0, 0);
        Check("栏：删除后清空选择不覆盖结果标题", bar.StateText == headline);
        bar.Refresh(1, 1, 0, 10);
        Check("栏：重新勾选回到可删且清掉旧结果", bar.CanDelete && bar.StateText.Length == 0);
        Check("栏：文案齐全", bar.DeleteText.Length > 0 && bar.CancelText.Length > 0
            && bar.SelectAllText.Length > 0 && bar.ClearText.Length > 0);
    }

    // ---------------------------------------------------------------- 真实 shell 冒烟（显式开关）

    private static void RealSmokeTests()
    {
        Section("真实 shell 冒烟（仅 FOLDERDEL_CHECK_REAL=1）");

        var dir = Path.Combine(Path.GetTempPath(), "FolderDeleteCheck_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "probe.txt"), "x");
            var op = new ShellRecycleOperation();
            bool can = op.CanRecycle(dir, out var tech);
            Console.WriteLine($"  real: CanRecycle={can} tech={tech}");
            Check("真实：临时目录所在卷可回收", can);
            if (!can) return;

            var r = op.Recycle(dir);
            Console.WriteLine($"  real: status={r.Status} hr=0x{r.HResult & 0xFFFFFFFF:X8} msg={r.Message}");
            Check("真实：临时目录进回收站", r.Status == RecycleShellStatus.Recycled);
            Check("真实：临时目录已消失", !Directory.Exists(dir));
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* 清理失败不影响结果 */ }
        }
    }
}
