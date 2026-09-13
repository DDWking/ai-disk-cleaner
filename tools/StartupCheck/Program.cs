using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AiDiskCleaner;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace StartupCheck;

/// <summary>
/// 启动回归检查。
///
/// 为什么单独做这个：2026-09-13 的 2.8.0 在 <c>MainWindow.xaml</c> 把
/// <c>RowHeight="46"</c> 写成 <c>RowHeight="Auto"</c>。XAML **编译通过**，
/// 但 <c>DataGrid.RowHeight</c> 是普通 <c>double</c>、没有 TypeConverter，
/// 于是运行时解析 BAML 时抛 <c>XamlParseException</c>：窗口永远建不出来，
/// 半初始化窗口又把崩溃处理器带进死循环。
/// 这类缺陷只有**真的把编译后的 BAML 加载一遍**才拦得住 —— 字符串断言拦不住。
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

    [STAThread]
    public static int Main()
    {
        // 测试宿主没有消息循环：给主线程装一个 Dispatcher 同步上下文，
        // 这样应用里 `new Progress<T>(...)` 的回调会**排队**而不是在线程池线程上
        // 直接碰 UI 对象（那会抛"另一个线程拥有该对象"，把整个测试进程打掉）。
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { });
        SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext(
                System.Windows.Threading.Dispatcher.CurrentDispatcher));

        // 干净配置目录：不碰用户真实配置，也不写用户日志
        string cfg = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "dashao-startupcheck-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(cfg);
        Environment.SetEnvironmentVariable("DASHAOHUO_CONFIG_DIR", cfg);

        Console.WriteLine("== 1. 编译产物里确实有编译后的 BAML ==");
        var asm = typeof(MainWindow).Assembly;
        Check("主程序集就是 AiDiskCleaner", asm.GetName().Name == "AiDiskCleaner", asm.GetName().Name);
        // 编译后的 XAML 以 .baml 形式打进 <Assembly>.g.resources；
        // 用 .xaml 这个 URI 去取（WPF 的常规映射），拿得到就证明测的是真编译产物。
        Check("mainwindow.xaml（编译后 BAML）资源存在",
            ResourceExists("/AiDiskCleaner;component/mainwindow.xaml"),
            "GetResourceStream 返回空");
        Check("app.xaml（编译后 BAML）资源存在",
            ResourceExists("/AiDiskCleaner;component/app.xaml"));

        Console.WriteLine("== 2. 真实加载 App.xaml 资源 + MainWindow（StartupUri 同一条路） ==");
        App? app = null;
        try
        {
            app = new App();
            app.InitializeComponent();     // 加载 App.xaml 资源（StaticResource 要靠它）
            Check("App.xaml 加载", true);
        }
        catch (Exception ex)
        {
            Check("App.xaml 加载", false, Describe(ex));
        }

        MainWindow? win = null;
        try
        {
            // 与 StartupUri="MainWindow.xaml" 完全等价：构造窗口 ⇒ InitializeComponent ⇒ 解析真实 BAML
            win = new MainWindow();
            // 默认页必须在**刚构造完**时取，后面几节会自己切页
            DefaultRightTab = Fld<object>(win, "_rightTab").ToString() ?? "";
            Check("MainWindow 构造 / BAML 解析（2.8.0 就是在这里炸的）", true);
            Check("构造完成后 IsUiReady 为真", win.IsUiReady);
        }
        catch (Exception ex)
        {
            Check("MainWindow 构造 / BAML 解析（2.8.0 就是在这里炸的）", false, Describe(ex));
        }

        if (win != null)
        {
            Console.WriteLine("== 3. 半初始化防线：关键控件必须都已赋值 ==");
            foreach (var n in new[]
                     {
                         "AlertText", "Overlay", "OrganizeGrid", "OrganizeCounts",
                         "OrganizeFilterPendingBtn", "ColOrgAction", "TabCleanBtn", "TabOrganizeBtn",
                     })
                Check($"x:Name={n} 已赋值", NamedField(win, n) != null);

            Console.WriteLine("== 4. 行高：自动增高但仍然有下限（详情展开不被裁切） ==");
            var grid = NamedField(win, "OrganizeGrid") as DataGrid;
            Check("OrganizeGrid 是 DataGrid", grid != null);
            if (grid != null)
            {
                Check("RowHeight 是自动（double.NaN），允许行按内容增高",
                    double.IsNaN(grid.RowHeight), grid.RowHeight.ToString());
                Check("MinRowHeight >= 46（不会压扁两行内容）",
                    grid.MinRowHeight >= 46, grid.MinRowHeight.ToString());
                Check("RowHeight 不是非法字面量（能算出实际像素）",
                    grid.RowHeight is double.NaN || grid.RowHeight > 0);
            }

            Console.WriteLine("== 5. 行内详情绑定的属性存在且可用 ==");
            var node = new OrganizeNode(
                new FileEntry { Name = "demo", FullPath = @"D:\demo", Kind = EntryKind.Directory },
                new FolderId(@"D:\demo", 1), 1, "demo");
            Check("详情默认收起", !node.IsDetailOpen);
            node.ToggleDetail();
            Check("点一下能展开详情", node.IsDetailOpen);
            node.Apply(new FolderPurposeResult(node.Id, "示例目录", "应用", "本地认出这是：demo",
                PurposeSource.Local, NeedsConfirm: false, FolderKind.Concrete));
            Check("详情有内容（这是什么 / 为什么）",
                node.DetailText.Length > 0
                && node.DetailText.Contains(Loc.OrganizeDetailWhat, StringComparison.Ordinal)
                && node.DetailText.Contains(Loc.OrganizeDetailWhy, StringComparison.Ordinal));
            Check("详情标题与来源都能取到", node.PurposeText.Length > 0 && node.SourceText.Length > 0);
        }

        Console.WriteLine("== 6. 崩溃处理器：重入闸、半初始化、第一现场、日志失败 ==");
        CrashRecordTests(cfg);

        Console.WriteLine("== 7. 详情展开 / 不裁切：真实控件树布局断言 ==");
        if (win != null) DetailLayoutTests(win);

        Console.WriteLine("== 8. 整理页交互行为：首击展开 / 上限反馈 / 一级优先 / 筛选只读 / 代次生命周期 ==");
        if (win != null) OrganizeInteractionTests(win);

        Console.WriteLine("== 9. 新方向：默认清理页 / 零后台请求 / 无批量入口 / 单项 AI ==");
        if (win != null) ProductDirectionTests(win);

        Console.WriteLine();
        Console.WriteLine($"PASS {_pass}   FAIL {_fail}");
        foreach (var f in Failures) Console.WriteLine("  - " + f);

        try { System.IO.Directory.Delete(cfg, true); } catch { /* 清理失败不影响结论 */ }
        return _fail == 0 ? 0 : 1;
    }

    static void CrashRecordTests(string cfg)
    {
        // 用钩子接管提示：既能断言"提示真的走了安全通道"，又不会被模态对话框卡住
        var notices = new List<string>();
        App.NoticeHookForTest = (ex, fatal) =>
            notices.Add((fatal ? "fatal:" : "runtime:") + ex.GetType().Name + ":" + ex.Message);

        var ex1 = new InvalidOperationException("启动期原始异常-ABC");
        App.ResetCrashStateForTest();
        CrashFirstRecord.ResetForTest(cfg);
        notices.Clear();
        var o1 = App.HandleCrashForTest(ex1, uiReady: false);
        Check("半初始化窗口 + 启动期异常 ⇒ 判定为启动期致命",
            o1 == App.CrashOutcome.FatalStartup, o1.ToString());
        Check("走安全提示通道（不碰半初始化控件）",
            notices.Count == 1 && notices[0].StartsWith("fatal:", StringComparison.Ordinal),
            string.Join(" | ", notices));
        Check("安全提示里带的是原始异常",
            notices.Count == 1 && notices[0].Contains("启动期原始异常-ABC", StringComparison.Ordinal));
        Check("第一现场被记下来", CrashFirstRecord.SaveCount == 1, CrashFirstRecord.SaveCount.ToString());
        Check("第一现场文件存在", System.IO.File.Exists(CrashFirstRecord.FilePath));
        string body = System.IO.File.Exists(CrashFirstRecord.FilePath)
            ? System.IO.File.ReadAllText(CrashFirstRecord.FilePath) : "";
        Check("第一现场包含原始异常类型", body.Contains("InvalidOperationException", StringComparison.Ordinal));
        Check("第一现场包含原始异常消息", body.Contains("启动期原始异常-ABC", StringComparison.Ordinal));
        Check("第一现场是独立文件（不是 app.log / crash.log）",
            System.IO.Path.GetFileName(CrashFirstRecord.FilePath) == "first-crash.log",
            CrashFirstRecord.FilePath);

        // 模拟那场风暴：后面再来两百条也只记一次、只处理一次（不递归、不弹两百次）
        int suppressed = 0;
        for (int i = 0; i < 200; i++)
            if (App.HandleCrashForTest(new NullReferenceException("风暴"), uiReady: false)
                == App.CrashOutcome.Suppressed) suppressed++;
        Check("风暴里的后续异常全部被压制（不会递归）", suppressed == 200, suppressed.ToString());
        Check("风暴没有再多弹提示", notices.Count == 1, notices.Count.ToString());
        Check("第一现场没有被风暴覆盖（仍只有 1 条）",
            CrashFirstRecord.SaveCount == 1, CrashFirstRecord.SaveCount.ToString());
        Check("第一现场内容仍是原始异常",
            System.IO.File.ReadAllText(CrashFirstRecord.FilePath)
                .Contains("启动期原始异常-ABC", StringComparison.Ordinal));

        // 运行期异常：窗口已就绪 ⇒ 走提示，不判致命、不退出。
        // 注意：测试宿主里步骤 2 造的那个 MainWindow 就是 Application.MainWindow，
        // 而且它已经构造完成，所以这里走的是**应用内提示**（与生产一致）。
        App.ResetCrashStateForTest();
        CrashFirstRecord.ResetForTest(cfg);
        notices.Clear();
        var o2 = App.HandleCrashForTest(new InvalidOperationException("运行期异常"), uiReady: true);
        Check("窗口已就绪 ⇒ 运行期处理（不当启动期致命）",
            o2 == App.CrashOutcome.RuntimeHandled, o2.ToString());

        // 没有可用主窗口时（半初始化 / 窗口已销毁）必须退回安全提示通道
        var savedMain = Application.Current.MainWindow;
        try
        {
            Application.Current.MainWindow = null;
            App.ResetCrashStateForTest();
            notices.Clear();
            var o2b = App.HandleCrashForTest(new InvalidOperationException("没有主窗口"), uiReady: false);
            Check("没有主窗口 ⇒ 判定启动期致命并走安全提示", o2b == App.CrashOutcome.FatalStartup);
            Check("没有主窗口时安全提示被调用",
                notices.Count == 1 && notices[0].Contains("没有主窗口", StringComparison.Ordinal),
                string.Join(" | ", notices));
        }
        finally
        {
            Application.Current.MainWindow = savedMain;
        }

        // 运行期异常风暴：计数有界
        App.ResetCrashStateForTest();
        notices.Clear();
        int suppressedRuntime = 0;
        for (int i = 0; i < 50; i++)
            if (App.HandleCrashForTest(new InvalidOperationException("运行期风暴"), uiReady: true)
                == App.CrashOutcome.Suppressed) suppressedRuntime++;
        Check("运行期异常风暴也会被压制（有界）", suppressedRuntime > 0, suppressedRuntime.ToString());

        // null 异常不能炸
        App.ResetCrashStateForTest();
        Check("null 异常不处理也不抛",
            App.HandleCrashForTest(null, uiReady: false) == App.CrashOutcome.None);

        // 提示钩子自己抛异常：绝不能把异常带回崩溃路径，也绝不能递归
        App.ResetCrashStateForTest();
        CrashFirstRecord.ResetForTest(cfg);
        App.NoticeHookForTest = (_, _) => throw new InvalidOperationException("提示本身炸了");
        string? thrown3 = null;
        App.CrashOutcome o3 = App.CrashOutcome.None;
        try { o3 = App.HandleCrashForTest(new InvalidOperationException("提示失败"), uiReady: false); }
        catch (Exception ex) { thrown3 = ex.GetType().Name + ": " + ex.Message; }
        Check("安全提示自身抛异常时不会再次抛出", thrown3 is null, thrown3);
        Check("安全提示自身抛异常时判定依然正确",
            o3 == App.CrashOutcome.FatalStartup, o3.ToString());
        App.NoticeHookForTest = (ex, fatal) =>
            notices.Add((fatal ? "fatal:" : "runtime:") + ex.GetType().Name + ":" + ex.Message);

        // 日志目录不可写：记录失败也必须安静返回，绝不再抛
        App.ResetCrashStateForTest();
        CrashFirstRecord.ResetForTest(@"\\?\Z:\definitely-not-writable\dashao");
        string? thrown = null;
        App.CrashOutcome o4 = App.CrashOutcome.None;
        try { o4 = App.HandleCrashForTest(new InvalidOperationException("日志写不了"), uiReady: false); }
        catch (Exception ex) { thrown = ex.GetType().Name + ": " + ex.Message; }
        Check("第一现场写不了时不会再次抛异常", thrown is null, thrown);
        Check("第一现场写不了时判定不受影响",
            o4 == App.CrashOutcome.FatalStartup, o4.ToString());
        Check("不可写路径上确实没有写出文件",
            !System.IO.File.Exists(System.IO.Path.Combine(@"\\?\Z:\definitely-not-writable\dashao",
                "first-crash.log")));

        // 回到干净状态，避免影响宿主
        App.ResetCrashStateForTest();
        App.NoticeHookForTest = null;
        CrashFirstRecord.ResetForTest(cfg);
    }

    /// <summary>
    /// 「详情能展开且不裁切」的**真实控件树**验证：用真实的 OrganizeGrid / DataGridRow /
    /// 详情 Border，走真实的 Measure/Arrange/UpdateLayout，量行高与元素边界。
    /// 这比字符串断言强，也比截图更能回答"有没有被裁掉"。
    /// </summary>
    static void DetailLayoutTests(MainWindow win)
    {
        var grid = NamedField(win, "OrganizeGrid") as DataGrid;
        var rows = NamedField(win, "_organizeRows") as System.Collections.ObjectModel.ObservableCollection<OrganizeNode>;
        Check("拿到真实 OrganizeGrid 与绑定集合", grid != null && rows != null);
        if (grid == null || rows == null) return;

        // 真实目录对象 + 真实本地结论，放进真实绑定集合
        var dir = new FileEntry
        {
            Name = "DemoFolder", FullPath = @"D:\DemoFolder",
            Kind = EntryKind.Directory, Size = 12_000_000, FileCount = 1, FolderCount = 1,
        };
        dir.Children.Add(new FileEntry
        {
            Name = "big.dat", FullPath = @"D:\DemoFolder\big.dat", Kind = EntryKind.File,
            Size = 8_000_000, Parent = dir,
        });
        var node = new OrganizeNode(dir, new FolderId(dir.FullPath, 1), 0, "DemoFolder");
        node.SetLevel(1);
        node.SetChildDirCount(2);
        node.SetEvidence(FolderPurposeRules.Summarize(dir, node.Id, 0, "DemoFolder"));
        node.Apply(new FolderPurposeResult(node.Id, "演示目录", "应用", "本地认出这是：DemoFolder",
            PurposeSource.Local, NeedsConfirm: false, FolderKind.Concrete));
        rows.Add(node);

        // 给窗口一个真实尺寸并强制走一遍布局（不显示窗口，但走真实 Measure/Arrange）
        win.Width = 1400;
        win.Height = 880;
        win.Measure(new Size(1400, 880));
        win.Arrange(new Rect(0, 0, 1400, 880));
        win.UpdateLayout();
        if (win.Content is FrameworkElement content)
        {
            content.Measure(new Size(1400, 880));
            content.Arrange(new Rect(0, 0, 1400, 880));
            content.UpdateLayout();
        }

        // 默认页现在是「清理中心」，整理页是折叠的 —— 要测它的行布局，
        // 先把整理页显示出来（不可见的 DataGrid 不会生成行容器）。
        var tabForLayout = typeof(MainWindow).GetNestedType("RightTab", BindingFlags.NonPublic)!;
        Call(win, "ShowRightTab", Enum.Parse(tabForLayout, "Organize"));
        if (win.Content is FrameworkElement content2)
        {
            content2.Measure(new Size(1400, 880));
            content2.Arrange(new Rect(0, 0, 1400, 880));
            content2.UpdateLayout();
        }
        win.UpdateLayout();

        var container = grid.ItemContainerGenerator.ContainerFromItem(node) as DataGridRow;
        Check("行容器已生成（真实 DataGridRow）", container != null);
        if (container == null) return;

        double before = container.ActualHeight;
        Check("折叠时行高 >= MinRowHeight(46)", before >= 46, before.ToString("0.#"));
        var detailClosed = FindByBinding(container, "DetailText");
        Check("详情块默认不可见（折叠）",
            detailClosed == null || detailClosed.ActualHeight < 1 || !detailClosed.IsVisible,
            detailClosed == null ? "not found" : detailClosed.ActualHeight.ToString("0.#"));

        node.ToggleDetail();
        win.UpdateLayout();
        container.UpdateLayout();
        if (win.Content is FrameworkElement c2) { c2.UpdateLayout(); }

        double after = container.ActualHeight;
        var detailOpen = FindByBinding(container, "DetailText");
        Check("展开后行确实变高了（RowHeight=NaN 生效）", after > before + 5,
            $"{before:0.#} -> {after:0.#}");
        Check("详情块被渲染出来且有高度",
            detailOpen != null && detailOpen.ActualHeight > 10,
            detailOpen == null ? "not found" : detailOpen.ActualHeight.ToString("0.#"));

        if (detailOpen != null)
        {
            var rowRect = container.TransformToAncestor(grid)
                .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            var detRect = detailOpen.TransformToAncestor(grid)
                .TransformBounds(new Rect(0, 0, detailOpen.ActualWidth, detailOpen.ActualHeight));
            // 不裁切 = 详情块完全落在行内（行的可视区域没有切掉它）
            bool inside = detRect.Bottom <= rowRect.Bottom + 1.5 && detRect.Top >= rowRect.Top - 1.5;
            Check("详情块完全落在行内（没有被裁切）", inside,
                $"detailBottom={detRect.Bottom:0.#} rowBottom={rowRect.Bottom:0.#}");
            Check("详情文本确实有内容（这是什么 / 为什么）",
                node.DetailText.Contains(Loc.OrganizeDetailWhat, StringComparison.Ordinal)
                && node.DetailText.Contains(Loc.OrganizeDetailWhy, StringComparison.Ordinal));
        }

        // 收起来应该回到原来的高度
        node.ToggleDetail();
        win.UpdateLayout();
        container.UpdateLayout();
        Check("再点一下收起，行高回到折叠状态", Math.Abs(container.ActualHeight - before) < 1.5,
            $"{before:0.#} vs {container.ActualHeight:0.#}");

        rows.Remove(node);
    }

    /// <summary>在可视树里找绑定了某个属性的元素（用来定位真实的详情块）。</summary>
    static FrameworkElement? FindByBinding(DependencyObject root, string path)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe)
            {
                var be = fe switch
                {
                    TextBlock tb => tb.GetBindingExpression(TextBlock.TextProperty),
                    _ => null,
                };
                if (be?.ParentBinding.Path.Path == path) return fe;
            }
            var hit = FindByBinding(child, path);
            if (hit != null) return hit;
        }
        return null;
    }

    // ---------------- 整理页交互行为（真 MainWindow，反射驱动真实私有路径） ----------------

    static T Fld<T>(object o, string name)
        => (T)(o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)
               ?? throw new InvalidOperationException("field null: " + name));

    static void SetFld(object o, string name, object? v)
        => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, v);

    static object? Call(object o, string name, params object?[] args)
        => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);

    static void ResetOrganizeCollections(MainWindow win)
    {
        Fld<List<OrganizeNode>>(win, "_organizeAll").Clear();
        Fld<List<OrganizeNode>>(win, "_organizeRoots").Clear();
        Fld<Dictionary<FileEntry, OrganizeNode>>(win, "_organizeByDir").Clear();
        Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows").Clear();
        SetFld(win, "_organizeNote", "");
    }

    static FileEntry MakeDir(string name, string path, long size, FileEntry? parent = null, int childDirs = 0)
    {
        var e = new FileEntry
        {
            Name = name, FullPath = path, Kind = EntryKind.Directory, Size = size,
            Parent = parent, FolderCount = childDirs,
        };
        parent?.Children.Add(e);
        return e;
    }

    /// <summary>
    /// 这些是**行为**用例（真的调 ToggleOrganize / LocalRecognize / 筛选 / 代次判据），
    /// 不是字符串断言。它们先按现状跑红，修完转绿。
    /// </summary>
    static void OrganizeInteractionTests(MainWindow win)
    {
        // ---------- 8.1 首次点击箭头：懒加载之后必须真的展开 ----------
        ResetOrganizeCollections(win);
        var rootA = MakeDir("RootA", @"X:\RootA", 1000, null, 2);
        MakeDir("a1", @"X:\RootA\a1", 600, rootA);
        MakeDir("a2", @"X:\RootA\a2", 400, rootA);
        var nodeA = new OrganizeNode(rootA, new FolderId(rootA.FullPath, 1), 0, "RootA");
        nodeA.SetLevel(1);
        nodeA.SetChildDirCount(2);
        Fld<List<OrganizeNode>>(win, "_organizeAll").Add(nodeA);
        Fld<List<OrganizeNode>>(win, "_organizeRoots").Add(nodeA);

        Check("懒加载前 ChildrenLoaded=false（确实还没材料化）", !nodeA.ChildrenLoaded);
        Call(win, "ToggleOrganize", nodeA);
        Check("首击箭头：子项被材料化出来",
            nodeA.ChildrenLoaded && nodeA.Children.Count == 2, nodeA.Children.Count.ToString());
        Check("首击箭头：节点**确实展开**了（v2.8.0/2.8.1 就是在这里失效）", nodeA.IsExpanded);

        var kidsFirst = nodeA.Children.ToList();
        Call(win, "ToggleOrganize", nodeA);
        Check("再点一次：收起", !nodeA.IsExpanded);
        Call(win, "ToggleOrganize", nodeA);
        Check("第三次：展开且复用同一批子节点（展开不重新材料化）",
            nodeA.IsExpanded && nodeA.Children.Count == 2
            && nodeA.Children.All(k => kidsFirst.Contains(k)),
            nodeA.Children.Count.ToString());
        Check("展开状态会通知界面（箭头方向跟着变）",
            nodeA.ExpandGlyphKey == "IconChevronDown", nodeA.ExpandGlyphKey);

        // ---------- 8.2 对象上限：必须明确反馈，不能无反应也不能假装展开 ----------
        ResetOrganizeCollections(win);
        var all = Fld<List<OrganizeNode>>(win, "_organizeAll");
        var backup = all.ToList();
        all.Clear();
        var filler = new OrganizeNode(
            MakeDir("filler", @"X:\filler", 1), new FolderId(@"X:\filler", 1), 0, "filler");
        for (int i = 0; i < FolderOrganize.MaxObjects; i++) all.Add(filler);

        var rootB = MakeDir("RootB", @"X:\RootB", 1000, null, 1);
        MakeDir("b1", @"X:\RootB\b1", 500, rootB);
        var nodeB = new OrganizeNode(rootB, new FolderId(rootB.FullPath, 1), 0, "RootB");
        nodeB.SetLevel(1);
        nodeB.SetChildDirCount(1);
        SetFld(win, "_organizeNote", "");
        Call(win, "ToggleOrganize", nodeB);
        Check("对象到上限：不假装展开（不显示空展开态）", !nodeB.IsExpanded);
        Check("对象到上限：给出明确反馈（不是点了没反应）",
            Fld<string>(win, "_organizeNote").Length > 0, Fld<string>(win, "_organizeNote"));
        all.Clear();
        all.AddRange(backup);

        // ---------- 8.3 子目录全在首屏（没有新东西可展开）：也要有反馈 ----------
        ResetOrganizeCollections(win);
        var rootC = MakeDir("RootC", @"X:\RootC", 1000, null, 1);
        var c1 = MakeDir("c1", @"X:\RootC\c1", 500, rootC);
        var nodeC = new OrganizeNode(rootC, new FolderId(rootC.FullPath, 1), 0, "RootC");
        nodeC.SetLevel(1);
        nodeC.SetChildDirCount(1);
        Fld<List<OrganizeNode>>(win, "_organizeAll").Add(nodeC);
        Fld<List<OrganizeNode>>(win, "_organizeRoots").Add(nodeC);
        // 先把它唯一的子项当成「已经是顶层对象」登记，模拟"子项就在首屏"
        var childTop = new OrganizeNode(c1, new FolderId(c1.FullPath, 1), 0, "c1");
        Fld<Dictionary<FileEntry, OrganizeNode>>(win, "_organizeByDir")[c1] = childTop;
        SetFld(win, "_organizeNote", "");
        Call(win, "ToggleOrganize", nodeC);
        Check("子项已在首屏：不显示一个空的展开态", !nodeC.IsExpanded);
        Check("子项已在首屏：给出明确反馈",
            Fld<string>(win, "_organizeNote").Length > 0, Fld<string>(win, "_organizeNote"));

        // ---------- 8.4 取消两级自动 AI 之后：本地识别照跑，且不发任何请求 ----------
        ResetOrganizeCollections(win);
        var all2 = Fld<List<OrganizeNode>>(win, "_organizeAll");
        var roots2 = Fld<List<OrganizeNode>>(win, "_organizeRoots");
        var lvl1 = MakeDir("L1small", @"X:\L1small", 10, null, 1);
        MakeDir("s", @"X:\L1small\s", 5, lvl1);
        var n1 = new OrganizeNode(lvl1, new FolderId(lvl1.FullPath, 1), 0, "L1small");
        n1.SetLevel(1); n1.SetChildDirCount(1);
        // 一个本地规则一定认得出的目录（node_modules），用来证明本地那一遍确实跑了
        var known = MakeDir("node_modules", @"X:\proj\node_modules", 900_000, null);
        var n2 = new OrganizeNode(known, new FolderId(known.FullPath, 1), 1, "node_modules");
        n2.SetLevel(2); n2.SetChildDirCount(0);
        all2.Add(n1); all2.Add(n2); roots2.Add(n1); roots2.Add(n2);

        AiGateway.ResetSentCountForTest();
        Call(win, "LocalRecognize", n1);
        Call(win, "LocalRecognize", n2);
        Check("本地识别对一级、二级对象都跑（node_modules 被本地认出）",
            n2.HasConclusion && n2.Source == PurposeSource.Local,
            $"has={n2.HasConclusion} source={n2.Source}");
        Check("本地识别不发任何模型请求（真实出站计数为 0）",
            AiGateway.SentCount == 0, "SentCount=" + AiGateway.SentCount);

        // 已有结论的对象再跑一次本地识别也不会重复下结论/丢来源
        var before = n2.PurposeName;
        Call(win, "LocalRecognize", n2);
        Check("本地识别是幂等的（不会把已有结论改坏）", n2.PurposeName == before && n2.Source == PurposeSource.Local,
            before + " -> " + n2.PurposeName);
        // ---------- 8.5 筛选只是视图：不永久篡改展开状态、不留孤儿行 ----------
        ResetOrganizeCollections(win);
        // D：展开；E：材料化但收起（用来验证"筛选不会偷偷展开它"）
        var rootD = MakeDir("RootD", @"X:\RootD", 1000, null, 1);
        MakeDir("d1", @"X:\RootD\d1", 900, rootD);            // 没有结论 => pending
        var rootE = MakeDir("RootE", @"X:\RootE", 500, null, 1);
        MakeDir("e1", @"X:\RootE\e1", 400, rootE);
        // F：父项**有结论**（不 pending），子项没有结论（pending）=> 必须保留父项做上下文
        var rootF = MakeDir("RootF", @"X:\RootF", 300, null, 1);
        var f1 = MakeDir("f1", @"X:\RootF\f1", 200, rootF, 1);
        MakeDir("f11", @"X:\RootF\f1\f11", 100, f1);

        OrganizeNode Mk(FileEntry d, int level, int childDirs)
        {
            var n = new OrganizeNode(d, new FolderId(d.FullPath, 1), 0, d.Name ?? "");
            n.SetLevel(level);
            n.SetChildDirCount(childDirs);
            Fld<List<OrganizeNode>>(win, "_organizeAll").Add(n);
            Fld<List<OrganizeNode>>(win, "_organizeRoots").Add(n);
            return n;
        }
        var nodeD = Mk(rootD, 1, 1);
        var nodeE = Mk(rootE, 1, 1);
        var nodeF = Mk(rootF, 1, 1);

        Call(win, "ToggleOrganize", nodeD);       // 材料化 + 展开
        Call(win, "ToggleOrganize", nodeE);       // 材料化 + 展开
        Call(win, "ToggleOrganize", nodeE);       // 再收起 => 材料化但收起
        Call(win, "ToggleOrganize", nodeF);       // 材料化 + 展开
        // 给 f1 一个结论（不 pending），它的子项 f11 仍然 pending
        var f1Node = nodeF.Children.FirstOrDefault(c => c.Name == "f1");
        if (f1Node != null)
            f1Node.Apply(new FolderPurposeResult(f1Node.Id, "已知", "应用", "本地",
                PurposeSource.Local, false, FolderKind.Concrete));

        Check("准备状态：D 展开 / E 材料化但收起 / F 展开",
            nodeD.IsExpanded && nodeE.ChildrenLoaded && !nodeE.IsExpanded && nodeF.IsExpanded,
            $"D={nodeD.IsExpanded} E.Loaded={nodeE.ChildrenLoaded} E={nodeE.IsExpanded} F={nodeF.IsExpanded}");

        int rowsBefore = Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows").Count;

        Call(win, "OrganizeFilter_Click", null, new RoutedEventArgs());
        Check("打开筛选：**不改变任何节点的展开状态**（原本收起的仍收起）",
            nodeD.IsExpanded && !nodeE.IsExpanded && nodeF.IsExpanded,
            $"D={nodeD.IsExpanded} E={nodeE.IsExpanded} F={nodeF.IsExpanded}");

        var rows = Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows")
            .ToList();
        var rootsNow = Fld<List<OrganizeNode>>(win, "_organizeRoots");
        Check("筛选下没有孤儿行（匹配子项的祖先作为上下文保留）",
            !HasOrphan(rows, rootsNow), string.Join(" > ", rows.Select(r => r.Name)));

        Call(win, "OrganizeFilter_Click", null, new RoutedEventArgs());
        Check("关闭筛选：回到打开前的展开状态（用户本来展开的仍展开）",
            nodeD.IsExpanded && !nodeE.IsExpanded && nodeF.IsExpanded,
            $"D={nodeD.IsExpanded} E={nodeE.IsExpanded} F={nodeF.IsExpanded}");
        int rowsAfter = Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows").Count;
        Check("关闭筛选：行集合回到打开前的样子", rowsAfter == rowsBefore, $"{rowsBefore} -> {rowsAfter}");

        // ---------- 8.6 逐项AI结果的代次隔离（批量任务已取消，隔离发生在逐项结果上） ----------
        ResetOrganizeCollections(win);
        int aiGenBefore = Fld<int>(win, "_aiDataGeneration");
        var iso = new OrganizeNode(MakeDir("iso", @"X:\iso", 10, null), new FolderId(@"X:\iso", 1), 0, "iso");
        Call(win, "InvalidateItemAiAfterScan");     // 模拟「重复检测完成后的分层重建」
        Check("逐项AI代次变化会推进（旧结果据此判过期）",
            Fld<int>(win, "_aiDataGeneration") == aiGenBefore + 1,
            $"{aiGenBefore} -> {Fld<int>(win, "_aiDataGeneration")}");
        Check("旧代次的逐项结果会被判过期，不会冒充新结果",
            !iso.Ai.HasResult && iso.Ai.SuggestionText.Length == 0,
            $"has={iso.Ai.HasResult}");

        // ---------- 8.7 用途缓存与请求计数只随「真换扫描」复位 ----------
        // 先用一个**本地就能判定**的目录把缓存喂起来（allowAi:false ⇒ 绝不联网）
        var svc = Fld<FolderPurposeService>(win, "_folderPurpose");
        var nmDir = MakeDir("node_modules", @"X:\nodeproj\node_modules", 900_000, null);
        FileEntry? nmFile = new FileEntry
        {
            Name = "package.json", FullPath = @"X:\nodeproj\node_modules\package.json",
            Kind = EntryKind.File, Size = 10, Parent = nmDir,
        };
        nmDir.Children.Add(nmFile);
        var nmRes = svc.RecognizeAsync(nmDir, new FolderId(nmDir.FullPath, 1), 1, "node_modules",
            allowAi: false, provider: null, model: null, sendFullPath: false,
            configSignature: "cfg", ct: CancellationToken.None).GetAwaiter().GetResult();
        Check("本地能判定的目录进了用途缓存（且没联网）",
            nmRes.HasConclusion && svc.CacheCount > 0, svc.CacheCount.ToString());
        Check("本地结论不消耗请求预算", svc.AiRequestsUsed == 0, svc.AiRequestsUsed.ToString());

        int cacheBefore = svc.CacheCount;
        Call(win, "InvalidateItemAiAfterScan");     // after-duplicates 那次重建
        Check("分层重建**不得**清掉用途缓存（否则已识别的会重问）",
            svc.CacheCount == cacheBefore, $"{cacheBefore} -> {svc.CacheCount}");
        Call(win, "RebuildOrganize");               // 真正换扫描
        Check("真换扫描才把用途缓存归零", svc.CacheCount == 0, svc.CacheCount.ToString());

        ResetOrganizeCollections(win);
    }

    /// <summary>行里出现"父项不在行里"或"父项排在子项之后"就是孤儿行。</summary>
    static bool HasOrphan(List<OrganizeNode> rows, List<OrganizeNode> roots)
    {
        var parent = new Dictionary<OrganizeNode, OrganizeNode>();
        void Map(OrganizeNode n)
        {
            foreach (var c in n.Children) { parent[c] = n; Map(c); }
        }
        foreach (var r in roots) Map(r);

        var index = new Dictionary<OrganizeNode, int>();
        for (int i = 0; i < rows.Count; i++) index[rows[i]] = i;

        foreach (var n in rows)
        {
            if (!parent.TryGetValue(n, out var p)) continue;   // 顶层，正常
            if (!index.TryGetValue(p, out int pi)) return true; // 父项没出现在行里 => 孤儿
            if (pi > index[n]) return true;                     // 父项排在子项之后 => 顺序错乱
        }
        return false;
    }

    /// <summary>
    /// 新方向的**行为**验证：默认页、零后台请求、没有批量入口、单项 AI 的作用域。
    /// 请求次数用 <c>AiGateway.SentCount</c>（真实出站计数），不是搜日志。
    /// </summary>
    static void ProductDirectionTests(MainWindow win)
    {
        // ---------- 9.1 默认进清理页 ----------
        Check("默认页是「清理中心」（文件夹整理退为辅助入口）",
            DefaultRightTab == "Clean", DefaultRightTab);

        // 扫描开始不许把用户从自己选的页面拽走
        var scan = ReadSource("src/AiDiskCleaner/MainWindow.xaml.cs");
        int runScanStart = scan.IndexOf("private async void RunScan()", StringComparison.Ordinal);
        int runScanEnd = runScanStart < 0 ? -1 : scan.IndexOf("\n    private ", runScanStart + 10, StringComparison.Ordinal);
        string runScan = runScanStart < 0 ? "" : scan[runScanStart..(runScanEnd < 0 ? scan.Length : runScanEnd)];
        Check("扫描开始不会强制切页（RunScanAsync 里没有 ShowRightTab）",
            runScan.Length > 0 && !runScan.Contains("ShowRightTab(", StringComparison.Ordinal));

        // ---------- 9.2 导航顺序：清理在前 ----------
        var xaml = ReadSource("src/AiDiskCleaner/MainWindow.xaml");
        int iClean = xaml.IndexOf("TabCleanBtn", StringComparison.Ordinal);
        int iOrg = xaml.IndexOf("TabOrganizeBtn", StringComparison.Ordinal);
        Check("导航把「清理中心」放在「文件夹整理」前面", iClean > 0 && iOrg > 0 && iClean < iOrg,
            $"clean@{iClean} organize@{iOrg}");

        // ---------- 9.3 没有任何批量 / 本层 / 重试全部的 AI 入口 ----------
        string org = ReadSource("src/AiDiskCleaner/MainWindow.Organize.cs");
        foreach (var bad in new[]
                 {
                     "OrganizeIdentifyAll", "OrganizeIdentifyCurrent", "OrgCtxIdentifyCurrent",
                     "AutoIdentifyTargets", "StartOrganizeAutoIdentify", "RunOrganizeIdentifyAsync",
                     "ProbeUnknownLocally", "OrganizeProgressPanel", "OrganizeScopeBar", "OrganizeStopBtn",
                 })
        {
            Check($"没有批量/本层识别入口：{bad}",
                !org.Contains(bad, StringComparison.Ordinal) && !xaml.Contains(bad, StringComparison.Ordinal));
        }
        Check("侧栏不再有「识别用途」按钮（只剩导航）",
            !scan.Contains("PurposeIdentify_Click", StringComparison.Ordinal)
            && !scan.Contains("RunPurposeAsync", StringComparison.Ordinal)
            && !xaml.Contains("CtxPurposeIdentify", StringComparison.Ordinal));

        // ---------- 9.4 扫描 / 切页 / 展开 / 筛选 = 0 次模型请求 ----------
        // 让「有模型配置」这件事成立，否则零请求没有说服力（没配模型本来就不会发）。
        var settings = App.Settings;
        bool hadModel = !string.IsNullOrWhiteSpace(settings.AiModel);
        string savedModel = settings.AiModel;
        settings.AiModel = "test-model-not-called";     // 只改名字，不发请求；下面用计数断言

        AiGateway.ResetSentCountForTest();
        ResetOrganizeCollections(win);

        // 造一棵真树：根 → 两个子目录（深浅各一），让展开/筛选真的有东西可做
        var root = MakeDir("ZeroRoot", @"X:\ZeroRoot", 4096, null, 1);
        MakeDir("z1", @"X:\ZeroRoot\z1", 2048, root);
        var node = new OrganizeNode(root, new FolderId(root.FullPath, 1), 0, "ZeroRoot");
        node.SetLevel(1);
        node.SetChildDirCount(1);
        Fld<List<OrganizeNode>>(win, "_organizeAll").Add(node);
        Fld<List<OrganizeNode>>(win, "_organizeRoots").Add(node);

        // 1) 扫描完成后的重建（本地识别那一遍）
        Call(win, "RebuildOrganize");
        // 2) 展开一个对象（懒加载 + 展开）
        Call(win, "ToggleOrganize", node);
        // 3) 收起 / 再展开
        Call(win, "ToggleOrganize", node);
        Call(win, "ToggleOrganize", node);
        // 4) 筛选开 / 关
        Call(win, "OrganizeFilter_Click", null, new RoutedEventArgs());
        Call(win, "OrganizeFilter_Click", null, new RoutedEventArgs());
        // 5) 切页来回
        Call(win, "ShowRightTab", Enum.Parse(typeof(MainWindow).GetNestedType("RightTab", BindingFlags.NonPublic)!, "Clean"));
        Call(win, "ShowRightTab", Enum.Parse(typeof(MainWindow).GetNestedType("RightTab", BindingFlags.NonPublic)!, "Organize"));
        Call(win, "ShowRightTab", Enum.Parse(typeof(MainWindow).GetNestedType("RightTab", BindingFlags.NonPublic)!, "Uninstall"));

        int sent = AiGateway.SentCount;
        Check("扫描重建 + 展开/收起 + 筛选开关 + 切页 全程 0 次模型请求（真实出站计数）",
            sent == 0, "SentCount=" + sent);
        Check("本地识别确实跑了（不是「什么都没做」才 0 请求）",
            node.ChildrenLoaded || node.HasConclusion || node.IsExpanded,
            $"loaded={node.ChildrenLoaded} expanded={node.IsExpanded} conclusion={node.HasConclusion}");

        settings.AiModel = hadModel ? savedModel : "";
        AiGateway.ResetSentCountForTest();
        ResetOrganizeCollections(win);

        // ---------- 9.5 单项 AI：组织出来的请求只针对那一个对象 ----------
        var node2 = new OrganizeNode(MakeDir("One", @"X:\One", 100, null, 0),
            new FolderId(@"X:\One", 1), 0, "One");
        node2.SetLevel(1);
        var described = Call(win, "DescribeAiTarget", node2);
        var req = described!.GetType().GetField("Item2")!.GetValue(described);
        Check("单项分析认得出整理页对象，并且只带这一个文件夹",
            req != null && (bool)req.GetType().GetProperty("IsFolder")!.GetValue(req)!,
            req == null ? "null" : req.ToString()!);
        Check("单项请求的范围键就是这个文件夹的完整路径",
            req != null && (string)req.GetType().GetProperty("ScopeKey")!.GetValue(req)! == @"X:\One",
            req == null ? "null" : (string)req.GetType().GetProperty("ScopeKey")!.GetValue(req)!);
        Check("单项请求带的是有上限的摘要，不是整棵树",
            req != null && (int)req.GetType().GetProperty("FolderSummaryShown")!.GetValue(req)!
                <= AiDiskCleaner.Services.ItemAiPrompt.MaxFolderSummary);
        Check("整理页对象有自己的 AI 展示态（结果缓存就在这里，不会串到别项）",
            !ReferenceEquals(node2.Ai, node.Ai) && node2.Ai.ScopeKey == @"X:\One",
            node2.Ai.ScopeKey + " vs " + node.Ai.ScopeKey);
    }

    /// <summary>读仓库里的源文件（从 bin 往上找到仓库根）：用于断言"某个入口不存在"。</summary>
    static string ReadSource(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PROGRESS.md"))) dir = dir.Parent;
        if (dir == null) return "";
        string full = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? File.ReadAllText(full) : "";
    }

    static string ReadResourceText(string name)
    {
        var asm = typeof(MainWindow).Assembly;
        foreach (var res in asm.GetManifestResourceNames())
        {
            if (!res.EndsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            using var s = asm.GetManifestResourceStream(res)!;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        return "";
    }

    /// <summary>刚构造完 MainWindow 时的默认页（后面几节会切页，所以必须早点取）。</summary>
    static string DefaultRightTab = "";

    static bool ResourceExists(string uri)
    {
        try
        {
            var s = Application.GetResourceStream(new Uri(uri, UriKind.Relative));
            if (s?.Stream is null) return false;
            s.Stream.Dispose();
            return true;
        }
        catch { return false; }
    }

    static object? NamedField(object target, string name)
        => target.GetType().GetField(name,
               BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?.GetValue(target);

    static string Describe(Exception ex)
    {
        var e = ex;
        while (e.InnerException != null) e = e.InnerException;
        return e.GetType().Name + ": " + (e.Message ?? "");
    }
}
