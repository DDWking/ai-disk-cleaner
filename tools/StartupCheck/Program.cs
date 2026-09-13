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
        Console.OutputEncoding = System.Text.Encoding.UTF8;

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
                         "AlertText", "Overlay", "OrganizeGrid", "OrganizeWorkBar",
                         "OrganizeIdentifyCurrentBtn", "OrganizeCounts", "OrgCtxIdentifyCurrent",
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
