using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AiDiskCleaner;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

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

    /// <summary>带诊断信息的断言：失败时把实际值打出来（与其它检查工具一致）。</summary>
    static void CheckD(string name, bool ok, string? detail) => Check(name, ok, detail);

    /// <summary>
    /// 一个**非空 RoutedEvent** 的 RoutedEventArgs。
    /// 裸 <c>new RoutedEventArgs()</c>（不带事件）在 <c>e.Handled = true</c> 时会抛
    /// InvalidOperationException —— 那是 WPF 的要求，不是被测代码的 bug。
    /// </summary>
    static RoutedEventArgs ClickArgs()
        => new(System.Windows.Controls.Primitives.ButtonBase.ClickEvent);

    /// <summary>造一条有真实 FileEntry（含父目录）的候选，复检路径才走得通。</summary>
    static CleanItem WithEntry(CleanItem item, string parentDir)
    {
        item.Entry = new FileEntry
        {
            Name = item.Name,
            FullPath = item.FullPath,
            Kind = EntryKind.File,
            Size = item.Size,
            Modified = new DateTime(2026, 1, 2, 3, 4, 5),
            Parent = MakeDir("parent", parentDir, item.Size, null, 0),
        };
        return item;
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
                         "CleanSelectAllBtn",
                         // AiChip（顶栏 AI 胶囊）随聊天通道一起移除，不在这份清单里了
                         // ColOrgAction（「操作」列）已整体移除，不在这份清单里了
                         "ColAppInstallDate", "TabCleanBtn", "TabOrganizeBtn",
                         "OrganizeStageGrid", "OrganizeClassifyBar", "RadialRingRotate",
                     })
                Check($"x:Name={n} 已赋值", NamedField(win, n) != null);

            Check("纯软件内虚拟分类：已彻底移除物理搬移「整理归类」按钮与搬家弹窗",
                NamedField(win, "ClassifyActionBtn") == null
                && NamedField(win, "ClassifyReportActionBtn") == null
                && NamedField(win, "ClassifyModalBody") == null
                && NamedField(win, "ClassifySortModeChipBtn") == null);

            // 页头清理过的东西必须真的不在控件树里（"看不见"要靠不存在来保证，不是靠隐藏）：
            // 「未识别」筛选胶囊被移除；页面标题也被删掉（Tab 上写的就是「按文件夹删除」，
            // 标题重复出现一次是多余的），所以统计行才是页头第一行可见文字。
            // 整理页也不再有「全选」—— 勾选只走行内复选框。
            Check("页头不再有「未识别」筛选胶囊",
                NamedField(win, "OrganizeFilterPendingBtn") == null);
            Check("页头不再重复页面标题（统计成为第一行）",
                NamedField(win, "OrganizeTitle") == null);
            Check("整理页底部不再有「全选」按钮",
                NamedField(win, "OrganizeSelectAllBtn") == null);
            Check("清理栏不再有规则批选按钮", NamedField(win, "RuleSelectBtn") == null);
            Check("清理栏不再有「清空选择」按钮", NamedField(win, "ClearSelectionBtn") == null);

            var orgGrid = NamedField(win, "OrganizeGrid") as DataGrid;
            Check("整理表不允许点表头打散树序（容量排序在数据层）",
                orgGrid != null && !orgGrid.CanUserSortColumns);

            var pubCol = NamedField(win, "ColAppPub") as DataGridTextColumn;
            var verCol = NamedField(win, "ColAppVersion") as DataGridTextColumn;
            var statusCol = NamedField(win, "ColAppStatus") as DataGridTextColumn;
            var dateCol = NamedField(win, "ColAppInstallDate") as DataGridTextColumn;
            Check("卸载页发布者列已隐藏", pubCol != null && pubCol.Visibility == Visibility.Collapsed);
            Check("卸载页版本列已隐藏", verCol != null && verCol.Visibility == Visibility.Collapsed);
            Check("卸载页状态列已隐藏", statusCol != null && statusCol.Visibility == Visibility.Collapsed);
            Check("卸载页保留安装日期列", dateCol != null && dateCol.Visibility == Visibility.Visible);

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

            var blocked = new OrganizeNode(
                new FileEntry { Name = "Windows", FullPath = @"C:\Windows", Kind = EntryKind.Directory },
                new FolderId(@"C:\Windows", 1), 0, "Windows");
            Check("系统容器复选框禁用", blocked.IsSelectionProtected);
            Check("系统容器仍有禁用原因", blocked.SelectionNote.Length > 0);

            var profile = new OrganizeNode(
                new FileEntry { Name = "Alice", FullPath = @"C:\Users\Alice", Kind = EntryKind.Directory },
                new FolderId(@"C:\Users\Alice", 1), 0, "Users\\Alice");
            Check("用户配置根可勾（需确认不禁用复选框）", !profile.IsSelectionProtected);
            Check("用户配置根仍有确认说明", profile.SelectionNote.Length > 0);

            var plain = new OrganizeNode(
                new FileEntry { Name = "projects", FullPath = @"C:\Data\projects", Kind = EntryKind.Directory },
                new FolderId(@"C:\Data\projects", 1), 0, "projects");
            Check("普通目录可勾且无选择提示", !plain.IsSelectionProtected && plain.SelectionNote.Length == 0);
        }

        Console.WriteLine("== 6. 崩溃处理器：重入闸、半初始化、第一现场、日志失败 ==");
        CrashRecordTests(cfg);

        Console.WriteLine("== 7. 详情展开 / 不裁切：真实控件树布局断言 ==");
        if (win != null) DetailLayoutTests(win);

        Console.WriteLine("== 8. 整理页交互行为：首击展开 / 上限反馈 / 一级优先 / 筛选只读 / 代次生命周期 ==");
        if (win != null) OrganizeInteractionTests(win);
        if (win != null) ReviewFixesWiringTests(win);
        if (win != null) BigFolderExpandTests(win);

        Console.WriteLine("== 8b. 三色筛选视图：展开后父条目必须还在（真机 op=anchor idx=-1 回归） ==");
        if (win != null) FlatFilterExpandTests(win);

        Console.WriteLine("== 9. 新方向：默认清理页 / 零后台请求 / 无批量入口 / 单项 AI ==");
        if (win != null) ProductDirectionTests(win);

        Console.WriteLine("== 9b. 纯软件内虚拟分类：AI 识别 / 彩色分组折叠 / 本地持久化 / 零磁盘移动 ==");
        if (win != null) ClassifyReorganizeTests(win);

        // 进程内的「正常关闭」回归。**这不是 EXE 端到端关闭实测**：
        // 本会话 shell 不是管理员，而 AiDiskCleaner.exe 带 requireAdministrator，
        // 非提权进程给提权窗口发 WM_CLOSE 会被 UIPI 直接拒掉（实测 PostMessage 返回 false）。
        // 所以这里在**真 MainWindow + 真控件树**上跑一遍关闭路径，作为可自动化的那一半证据，
        // 端到端关闭仍然需要一次提权会话（如实写在交付说明里）。
        Console.WriteLine("== 10. 真 MainWindow 的正常关闭路径（进程内，真控件树） ==");
        if (win != null)
        {
            int crashesBefore = CrashFirstRecord.SaveCount;
            bool closed = false;
            string closeError = "";
            try
            {
                win.Close();
                closed = true;
            }
            catch (Exception ex) { closeError = Describe(ex); }
            CheckD("真 MainWindow 走正常关闭路径不抛异常", closed, closeError);
            CheckD("关闭后窗口不再可见", !win.IsVisible, win.IsVisible.ToString());
            CheckD("关闭过程没有触发崩溃处理器",
                CrashFirstRecord.SaveCount == crashesBefore,
                $"{crashesBefore} -> {CrashFirstRecord.SaveCount}");
        }

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

    /// <summary>
    /// 「点开大文件夹只渲染出子文件夹、外层那个大文件夹自己的头部条目没了」的布局回归。
    ///
    /// 数据层（<c>_organizeRows</c>）一定是父行先 Add、子行后 Add，所以这个现象只可能来自
    /// 行容器/视口：插入几百行之后父行被顶出可视区，或者行集合被整表替换过。
    /// 这里用真实 <c>OrganizeGrid</c> + 真实 Measure/Arrange，量「展开前后父行还在不在屏幕上」。
    /// </summary>
    static void BigFolderExpandTests(MainWindow win)
    {
        var grid = NamedField(win, "OrganizeGrid") as DataGrid;
        if (grid == null) { Check("大展开：拿不到 OrganizeGrid", false); return; }
        var rows = Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows");
        var all = Fld<List<OrganizeNode>>(win, "_organizeAll");
        var roots = Fld<List<OrganizeNode>>(win, "_organizeRoots");
        ResetOrganizeCollections(win);

        // 39 个普通根在前、300 个子目录的大文件夹排在最后：真实首屏就是这样，
        // 用户得先滚到「大文件夹」那一行才点得到箭头 —— 下面把这步显式做出来。
        var bigDir = MakeDir("BigFolder", @"X:\BigFolder", 900_000_000);
        for (int i = 0; i < 300; i++)
        {
            var d = MakeDir("sub" + i, @"X:\BigFolder\sub" + i, 3_000_000 + i, bigDir, 2);
            MakeDir("aa", @"X:\BigFolder\sub" + i + @"\aa", 1_400_000, d);
            MakeDir("bb", @"X:\BigFolder\sub" + i + @"\bb", 1_300_000, d);
        }

        OrganizeNode AddRoot(FileEntry dir, int childDirs)
        {
            var n = new OrganizeNode(dir, new FolderId(dir.FullPath ?? "", 1), 0, dir.Name ?? "");
            n.SetLevel(1);
            n.SetChildDirCount(childDirs);
            all.Add(n);
            roots.Add(n);
            return n;
        }

        for (int i = 0; i < 39; i++) AddRoot(MakeDir("Small" + i, @"X:\Small" + i, 9_000_000 - i, null), 0);
        var bigNode = AddRoot(bigDir, 300);

        win.Width = 1400;
        win.Height = 880;
        win.Measure(new Size(1400, 880));
        win.Arrange(new Rect(0, 0, 1400, 880));
        win.UpdateLayout();
        var tabForLayout = typeof(MainWindow).GetNestedType("RightTab", BindingFlags.NonPublic)!;
        Call(win, "ShowRightTab", Enum.Parse(tabForLayout, "Organize"));
        Call(win, "RefreshOrganizeRows");
        win.UpdateLayout();

        var sv = FindScrollViewer(grid);
        double viewport = sv?.ViewportHeight ?? 0;
        if (sv == null || viewport <= 0)
        {
            Console.WriteLine("       [skip] 没量到可视区（窗口未 Show），跳过视口断言");
            ResetOrganizeCollections(win);
            Call(win, "RefreshOrganizeRows");
            return;
        }

        Rect RowRect(OrganizeNode n)
        {
            if (grid.ItemContainerGenerator.ContainerFromItem(n) is not DataGridRow c) return Rect.Empty;
            return c.TransformToAncestor(grid).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight));
        }

        // 真实操作：用户先滚到「大文件夹」那一行（它总在眼前），这里把这一步做出来。
        // 未 Show 的窗口里直接改 ScrollViewer 偏移不生效，只能靠 DataGrid.ScrollIntoView，
        // 所以这一行会被摆在视口下沿 —— 这正是「用户点到列表最后一行」的真实起点。
        grid.ScrollIntoView(bigNode);
        win.UpdateLayout();

        var before = RowRect(bigNode);
        Console.WriteLine($"       [prep] top={before.Top:0.#} off={sv.VerticalOffset:0.#} vh={viewport:0.#} rows={rows.Count}");
        CheckD("大展开：准备状态——折叠时父行在视口里",
            !before.IsEmpty && before.Top >= -1 && before.Top < viewport,
            $"top={before.Top:0.#} vh={viewport:0.#} rows={rows.Count}");

        Call(win, "ToggleOrganize", bigNode);
        win.UpdateLayout();

        int bi = rows.IndexOf(bigNode);
        var firstKid = bigNode.Children.FirstOrDefault(c => c.Dir.IsDirectory);
        int ki = firstKid == null ? -1 : rows.IndexOf(firstKid);
        CheckD("大展开：父行仍在行集合里", bi >= 0, "index=" + bi);
        CheckD("大展开：父行排在子行前面", bi >= 0 && ki > bi, $"parent={bi} firstKid={ki} rows={rows.Count}");

        var after = RowRect(bigNode);
        CheckD("大展开：父行没有被挤出视口（用户还能看到它）",
            !after.IsEmpty && after.Top >= -1 && after.Top < viewport,
            $"top={after.Top:0.#} vh={viewport:0.#} offset={sv.VerticalOffset:0.#}");
        CheckD("大展开：点击不会把父行往下推（用户不会被甩到别处）",
            before.IsEmpty || after.IsEmpty || after.Top <= before.Top + 8,
            $"before={before.Top:0.#} after={after.Top:0.#} offset={sv.VerticalOffset:0.#}");

        var kidRect = firstKid == null ? Rect.Empty : RowRect(firstKid);
        // 远程的视口策略是绝对偏移原位恢复：用户在视口底部点开时，子行从视口下沿
        // 开始（存在、可滚到，但不保证整行在屏内）。「子行整行可见」是旧锚定时代的
        // 几何产物；现在的契约=子行紧随父行下方、且已经进入视口区域。
        CheckD("大展开：父行下面紧跟着它的子行（子行已进入视口区域）",
            !kidRect.IsEmpty && !after.IsEmpty
                && kidRect.Top > after.Top - 0.5
                && kidRect.Top <= viewport + 1,
            $"parentTop={after.Top:0.#} kid=[{kidRect.Top:0.#},{kidRect.Bottom:0.#}] vh={viewport:0.#}");

        int realized = 0, firstRealized = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (grid!.ItemContainerGenerator.ContainerFromItem(rows[i]) is DataGridRow)
            {
                realized++;
                if (firstRealized < 0) firstRealized = i;
            }
        }
        Console.WriteLine($"       [diag] rows={rows.Count} realized={realized} firstRealized={firstRealized} "
            + $"parentIdx={bi} parentTop={after.Top:0.#} vh={viewport:0.#} offset={sv?.VerticalOffset:0.#}");

        Call(win, "ToggleOrganize", bigNode);   // 收起，恢复现场
        win.UpdateLayout();
        ResetOrganizeCollections(win);
        Call(win, "RefreshOrganizeRows");
    }

    /// <summary>
    /// 三色筛选（平铺）视图里展开一个文件夹（真机日志 op=anchor idx=-1 row=none 的回归）：
    /// 点击前子目录还没进 _organizeAll，父项是列表里唯一同色节点、可以点；
    /// 点击后 Materialize 把同色子目录送进列表，父子重叠去重如果删的是父项，
    /// 用户点开的那个文件夹就会当场消失、只剩子目录。
    /// </summary>
    static void FlatFilterExpandTests(MainWindow win)
    {
        var rows = Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows");
        var all = Fld<List<OrganizeNode>>(win, "_organizeAll");
        var roots = Fld<List<OrganizeNode>>(win, "_organizeRoots");
        ResetOrganizeCollections(win);
        SetFld(win, "_organizeColorFilter", null);

        // 父 500MB、子 110MB：都过 BigUnknownThreshold（100MB）→ 自然算成黄色「待考虑」；
        // 子只有父的 22%，正好落进旧逻辑「删父项」的分支。
        // 远程 cca93f1 之后筛选分支的子目录走 EnsureFilterDrillChildren（从
        // _organizeByDir 登记 + 入口点规则挖，不再现场扫描）→ 按远程 8.8 的模式
        // 预建子节点并登记进 _organizeByDir。
        var pDir = MakeDir("FlatParent", @"X:\FlatParent", 500_000_000, null, 1);
        var kidDir = MakeDir("FlatChild", @"X:\FlatParent\FlatChild", 110_000_000, pDir, 0);

        var p = new OrganizeNode(pDir, new FolderId(pDir.FullPath ?? "", 1), 0, pDir.Name ?? "");
        p.SetLevel(1);
        p.SetChildDirCount(1);
        var kid = new OrganizeNode(kidDir, new FolderId(kidDir.FullPath ?? "", 1), 1, kidDir.Name ?? "");
        kid.SetLevel(2);
        all.Add(p);
        roots.Add(p);
        all.Add(kid);
        var byDir = Fld<Dictionary<FileEntry, OrganizeNode>>(win, "_organizeByDir");
        byDir[pDir] = p;
        byDir[kidDir] = kid;   // 不登记的话筛选深挖挖不到它（远程 8.8 同款前提）

        Call(win, "SetColorFilter", FolderColorTone.Yellow);   // 真入口：打开黄色平铺筛选
        win.UpdateLayout();

        CheckD("筛选展开：前提——父项是黄色（超过 100MB 的未明确目录）",
            p.ColorTone == FolderColorTone.Yellow, $"tone={p.ColorTone} size={p.Size}");
        int before = rows.IndexOf(p);
        CheckD("筛选展开：点击前父项在平铺列表里（用户点得到它）", before >= 0, $"idx={before} rows={rows.Count}");

        Call(win, "ToggleOrganize", p);   // ← 用户点开
        win.UpdateLayout();

        // kid 是预登记的节点：筛选深挖应当复用它（不能挖丢、也不能重复造一个）
        CheckD("筛选展开：前提——子项同为黄色且小于父项 85%",
            kid != null && kid.ColorTone == FolderColorTone.Yellow && kid.Size * 100 < p.Size * 85,
            kid == null ? "no kid" : $"kidTone={kid.ColorTone} kid={kid.Size} parent={p.Size}");

        int pi = rows.IndexOf(p);
        int ki = kid == null ? -1 : rows.IndexOf(kid);
        CheckD("筛选展开：父项没有被去重删除（点开之后还在列表里）", pi >= 0, $"idx={pi} rows={rows.Count}");
        CheckD("筛选展开：子项紧跟在父项下面（就地嵌套展示）", pi >= 0 && ki == pi + 1, $"parent={pi} kid={ki} rows={rows.Count}");
        int kidCount = kid == null ? 0 : rows.Count(n => ReferenceEquals(n, kid));
        CheckD("筛选展开：子项只渲染一次（平铺与嵌套不重复）", kid == null || kidCount == 1, $"count={kidCount}");

        Call(win, "ToggleOrganize", p);   // 收起
        win.UpdateLayout();
        CheckD("筛选收起：父项仍在列表里（不会收着收着把自己收没了）", rows.IndexOf(p) >= 0,
            $"idx={rows.IndexOf(p)} rows={rows.Count}");

        SetFld(win, "_organizeColorFilter", null);   // 关筛选，还原现场
        ResetOrganizeCollections(win);
        Call(win, "RefreshOrganizeRows");
    }

    static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer hit) return hit;
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var found = FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
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
    {
        var m = o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        // MethodInfo.Invoke 不会替你补默认值：目标方法一旦加了可选参数（如
        // RefreshOrganizeRows(anchorNode, restoreScrollOffset)），零参调用会直接
        // TargetParameterCountException。这里按参数表把缺的尾巴补上。
        var ps = m.GetParameters();
        if (args.Length < ps.Length)
        {
            var full = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < args.Length) { full[i] = args[i]; continue; }
                full[i] = ps[i].HasDefaultValue
                    ? ps[i].DefaultValue
                    : (ps[i].ParameterType.IsValueType
                        ? Activator.CreateInstance(ps[i].ParameterType)
                        : null);
            }
            args = full;
        }
        return m.Invoke(o, args);
    }

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

        Call(win, "LocalRecognize", n1);
        Call(win, "LocalRecognize", n2);
        Check("本地识别对一级、二级对象都跑（node_modules 被本地认出）",
            n2.HasConclusion && n2.Source == PurposeSource.Local,
            $"has={n2.HasConclusion} source={n2.Source}");
        Check("进程内已经没有 Jev 出站通道（本地识别不可能发请求）",
            typeof(MainWindow).Assembly.GetType("AiDiskCleaner.Services.AiGateway") == null);

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

        Call(win, "OrganizeFilter_Click", null, ClickArgs());
        Check("打开筛选：**不改变任何节点的展开状态**（原本收起的仍收起）",
            nodeD.IsExpanded && !nodeE.IsExpanded && nodeF.IsExpanded,
            $"D={nodeD.IsExpanded} E={nodeE.IsExpanded} F={nodeF.IsExpanded}");

        var rows = Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows")
            .ToList();
        var rootsNow = Fld<List<OrganizeNode>>(win, "_organizeRoots");
        Check("筛选下没有孤儿行（匹配子项的祖先作为上下文保留）",
            !HasOrphan(rows, rootsNow), string.Join(" > ", rows.Select(r => r.Name)));

        Call(win, "OrganizeFilter_Click", null, ClickArgs());
        Check("关闭筛选：回到打开前的展开状态（用户本来展开的仍展开）",
            nodeD.IsExpanded && !nodeE.IsExpanded && nodeF.IsExpanded,
            $"D={nodeD.IsExpanded} E={nodeE.IsExpanded} F={nodeF.IsExpanded}");
        int rowsAfter = Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows").Count;
        Check("关闭筛选：行集合回到打开前的样子", rowsAfter == rowsBefore, $"{rowsBefore} -> {rowsAfter}");

        // ---------- 8.6 节点身份代次**恒定**（原来每次分层重建都 +1） ----------
        // 逐项 AI 删掉之后，这个代次只用于「节点身份」。它必须恒定：
        // 用途缓存是「跨重启别再问一遍」的记忆，每次重建换代次会让缓存全部落空。
        ResetOrganizeCollections(win);
        var winSrc86 = ReadSource("src/AiDiskCleaner/MainWindow.xaml.cs");
        Check("节点身份代次是常量（换代次会让用途缓存全部落空）",
            winSrc86.Contains("private const int NodeGeneration = 0;", StringComparison.Ordinal));
        Check("旧的逐项 AI 代次字段已彻底移除",
            !winSrc86.Contains("_aiDataGeneration", StringComparison.Ordinal)
            && !ReadSource("src/AiDiskCleaner/MainWindow.Organize.cs").Contains("_aiDataGeneration", StringComparison.Ordinal)
            && !ReadSource("src/AiDiskCleaner/MainWindow.RecognitionPersistence.cs").Contains("_aiDataGeneration", StringComparison.Ordinal));
        Check("旧的逐项 AI 过期逻辑已随能力一起移除",
            typeof(MainWindow).GetMethod("InvalidateItemAiAfterScan",
                BindingFlags.Instance | BindingFlags.NonPublic) == null);

        // ---------- 8.7 用途缓存只随「真换扫描」复位 ----------
        // 先用一个**本地就能判定**的目录把缓存喂起来（本地路径绝不联网）
        var svc = Fld<FolderPurposeService>(win, "_folderPurpose");
        var nmDir = MakeDir("node_modules", @"X:\nodeproj\node_modules", 900_000, null);
        FileEntry? nmFile = new FileEntry
        {
            Name = "package.json", FullPath = @"X:\nodeproj\node_modules\package.json",
            Kind = EntryKind.File, Size = 10, Parent = nmDir,
        };
        nmDir.Children.Add(nmFile);
        var nmRes = svc.Recognize(nmDir, new FolderId(nmDir.FullPath, 1), 1, "node_modules", "cfg");
        Check("本地能判定的目录进了用途缓存（且没联网）",
            nmRes.HasConclusion && svc.CacheCount > 0, svc.CacheCount.ToString());
        // v3.1：本地路径根本不经过出站口 —— 出站口这个类型已经被删掉了。
        Check("本地识别全程 0 次出站请求（进程里已无出站网关）",
            typeof(MainWindow).Assembly.GetType("AiDiskCleaner.Services.AiGateway") == null);

        int cacheBefore = svc.CacheCount;
        // 原来这里调 InvalidateItemAiAfterScan 来模拟「重复检测完成后的分层重建」，
        // 确认那次重建不清用途缓存。那个方法随逐项 AI 一起删掉了。
        // 现在清缓存只剩 RebuildOrganize → ResetForScan 一处，所以改成源码断言 + 行为断言。
        var orgSrc720 = ReadSource("src/AiDiskCleaner/MainWindow.Organize.cs");
        Check("清用途缓存的入口只有「真换扫描」那一处",
            orgSrc720.Split(new[] { "ResetForScan" }, StringSplitOptions.None).Length - 1 == 1,
            "ResetForScan 出现次数");
        Check("分层重建**不得**清掉用途缓存（否则已识别的会重问）",
            svc.CacheCount == cacheBefore, $"{cacheBefore} -> {svc.CacheCount}");
        Call(win, "RebuildOrganize");               // 真正换扫描
        Check("真换扫描才把用途缓存归零", svc.CacheCount == 0, svc.CacheCount.ToString());

        // ---------- 8.8 颜色筛选下深挖不丢入口子目录、子行不显示灰色长路径、部分缓存按需补全 ----------
        ResetOrganizeCollections(win);
        var filtAll = Fld<List<OrganizeNode>>(win, "_organizeAll");
        var filtRoots = Fld<List<OrganizeNode>>(win, "_organizeRoots");
        var filtByDir = Fld<Dictionary<FileEntry, OrganizeNode>>(win, "_organizeByDir");

        var bigAppData = MakeDir("BigFolder", @"X:\Workspace\BigFolder", 500_000_000L, null, 1);
        var localEntry = MakeDir("LocalData", @"X:\Workspace\BigFolder\LocalData", 450_000_000L, bigAppData, 1);
        var deepLeaf = MakeDir("CacheLeaf", @"X:\Workspace\BigFolder\LocalData\CacheLeaf", 350_000_000L, localEntry, 0);

        var bigNode = new OrganizeNode(bigAppData, new FolderId(bigAppData.FullPath, 1), 0, @"Workspace\BigFolder");
        bigNode.SetLevel(1);
        bigNode.SetChildDirCount(1);
        var localNode = new OrganizeNode(localEntry, new FolderId(localEntry.FullPath, 1), 0, @"Workspace\BigFolder\LocalData");
        localNode.SetLevel(1);
        localNode.SetChildDirCount(1);
        filtAll.Add(bigNode);
        filtRoots.Add(bigNode);
        filtByDir[bigAppData] = bigNode;
        filtAll.Add(localNode);
        filtByDir[localEntry] = localNode; // 模拟子项也在 _organizeByDir 里登记，不能在筛选展开时失踪

        Call(win, "SetColorFilter", (FolderColorTone?)FolderColorTone.Yellow);
        Call(win, "ToggleOrganize", bigNode);
        var rowsAfterExpand1 = Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows").ToList();
        int idxBig = rowsAfterExpand1.IndexOf(bigNode);
        int idxLocal = rowsAfterExpand1.IndexOf(localNode);
        Check("筛选下展开原大文件夹不被 _organizeByDir 跳过（子目录出现在下方）",
            bigNode.IsFilterBranchRoot && idxBig >= 0 && idxLocal > idxBig,
            $"root={bigNode.IsFilterBranchRoot} idxBig={idxBig} idxLocal={idxLocal}");
        Check("筛选下顶层大文件夹保留灰色路径线索，展开的子行不再显示灰色路径",
            bigNode.ShowPathHint && !localNode.ShowPathHint && localNode.IsFilterBranchChild,
            $"parentHint={bigNode.ShowPathHint} childHint={localNode.ShowPathHint} childFlag={localNode.IsFilterBranchChild}");

        Call(win, "ToggleOrganize", localNode);
        var rowsAfterExpand2 = Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows").ToList();
        var leafNode = rowsAfterExpand2.FirstOrDefault(r => string.Equals(r.Name, "CacheLeaf", StringComparison.Ordinal));
        Check("筛选下可继续逐级展开深层子目录",
            leafNode != null && rowsAfterExpand2.IndexOf(leafNode) > rowsAfterExpand2.IndexOf(localNode) && !leafNode.ShowPathHint,
            $"leaf={(leafNode != null ? leafNode.FullPath : "null")}");

        // 临时目录模拟冷启动 Partial 缓存按需补全
        string tempCacheDir = Path.Combine(Path.GetTempPath(), "dsh-cache-hydrate-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(tempCacheDir, "SubA"));
            File.WriteAllBytes(Path.Combine(tempCacheDir, "big.bin"), new byte[1024]);
            var cachedDir = new FileEntry
            {
                Name = "TempCache",
                FullPath = tempCacheDir,
                Kind = EntryKind.Directory,
                FolderCount = 1,
                Size = 1024,
                IsCachePartial = true,
            };
            var cachedNode = new OrganizeNode(cachedDir, new FolderId(tempCacheDir, 1), 3, "TempCache");
            Check("冷启动 Partial 节点仍显示可展开", cachedNode.ContentKind == FolderContentKind.ChildDirs);
            Call(win, "HydratePartialCacheDirIfNeeded", cachedNode);
            Check("点击时按需从磁盘补全真实子目录与文件并清除 Partial 标记",
                !cachedDir.IsCachePartial && cachedDir.Children.Count == 2 && cachedNode.ChildDirCount == 1,
                $"partial={cachedDir.IsCachePartial} children={cachedDir.Children.Count} subdirs={cachedNode.ChildDirCount}");
        }
        finally
        {
            try { if (Directory.Exists(tempCacheDir)) Directory.Delete(tempCacheDir, true); } catch { }
        }

        // 视觉统一性回归：非空目录永不误显示「扫描无内容」、极小占比也有药丸条、单线链拆为柔和后缀
        var nonEmptyCached = MakeDir("NonEmptyLeaf", @"X:\Downloads\NonEmptyLeaf", 1_500_000_000L, null, 0);
        var nonEmptyNode = new OrganizeNode(nonEmptyCached, new FolderId(nonEmptyCached.FullPath, 1), 0, "NonEmptyLeaf");
        nonEmptyNode.SetChildDirCount(0);
        Check("有容量的目录即使未加载子项也不误显示「扫描无内容 / 空」",
            nonEmptyNode.SizeText != Loc.OrganizeSizeEmpty && nonEmptyNode.ContentStateText != Loc.OrganizeStateEmpty,
            $"size={nonEmptyNode.SizeText} state={nonEmptyNode.ContentStateText}");

        nonEmptyNode.SetSizeShare(0.0008);
        Check("小于 0.2% 的小体积文件夹仍画出可见占比条（SizeBarWidth >= 6）",
            nonEmptyNode.SizeBarWidth >= 6.0,
            nonEmptyNode.SizeBarWidth.ToString("0.0"));

        var chainTail = MakeDir("steamapps", @"X:\SteamLibrary\steamapps", 1_500_000_000L, nonEmptyCached, 0);
        nonEmptyNode.SetCollapsedChain(chainTail, "SteamLibrary / steamapps");
        Check("单线折叠拆出干净主名称与柔和后缀",
            nonEmptyNode.DisplayName == "SteamLibrary" && nonEmptyNode.ChainSuffixInline.Trim() == "› steamapps",
            $"name={nonEmptyNode.DisplayName} suffix={nonEmptyNode.ChainSuffixInline}");

        Call(win, "SetColorFilter", (FolderColorTone?)null);
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
    /// v3.1 起进程里已经没有出站网关，零请求用「类型不存在」做静态断言。
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

        // 行为验证：真正跑一遍「扫描完成后的整理重建」。这是
        // RunScan → FinishScanAsync → RunCleanPipelineAsync → RebuildOrganize 的最后一跳，
        // 用真实私有路径驱动，不靠搜字符串。用户在扫描前自己切到整理页，
        // 重建完成后必须还在整理页 —— 不许被拽回清理页。
        ResetOrganizeCollections(win);
        var pageRoot = MakeDir("KeepPageRoot", @"X:\KeepPageRoot", 4096, null, 1);
        MakeDir("kp1", @"X:\KeepPageRoot\kp1", 2048, pageRoot);
        var savedRoot = NamedField(win, "_root");
        var tabType = typeof(MainWindow).GetNestedType("RightTab", BindingFlags.NonPublic)!;
        try
        {
            SetFld(win, "_root", pageRoot);
            Call(win, "ShowRightTab", Enum.Parse(tabType, "Organize"));
            Check("扫描前用户停在整理页（准备状态）",
                Fld<object>(win, "_rightTab").ToString() == "Organize",
                Fld<object>(win, "_rightTab").ToString() ?? "");
            Call(win, "RebuildOrganize");
            Check("扫描重建后仍在整理页（不被拽回清理页）",
                Fld<object>(win, "_rightTab").ToString() == "Organize",
                Fld<object>(win, "_rightTab").ToString() ?? "");
        }
        finally
        {
            SetFld(win, "_root", savedRoot);
            Call(win, "ShowRightTab", Enum.Parse(tabType, "Clean"));
        }

        // ---------- 9.2 导航顺序：清理在前 ----------
        var xaml = ReadSource("src/AiDiskCleaner/MainWindow.xaml");
        int iClean = xaml.IndexOf("TabCleanBtn", StringComparison.Ordinal);
        int iOrg = xaml.IndexOf("TabOrganizeBtn", StringComparison.Ordinal);
        Check("导航把「清理中心」放在「文件夹整理」前面", iClean > 0 && iOrg > 0 && iClean < iOrg,
            $"clean@{iClean} organize@{iOrg}");

        // ---------- 9.3 整理页不再有批量归类入口（v3.1 已删 Jev 判定通道） ----------
        // 曾经这里守三条：批量入口只能有一个、必须用户主动点、只能写展示字段。
        // v3.1 把「AI 自动给文件夹打标签」整条能力删掉了（改成聊天式报告），
        // 所以对应用户入口、模型字段、落盘槽、出站网关都应当**不存在** —— 改用负向断言钉住。
        string org = ReadSource("src/AiDiskCleaner/MainWindow.Organize.cs");
        string orgNode = ReadSource("src/AiDiskCleaner/Models/OrganizeNode.cs");
        string persistSrc = ReadSource("src/AiDiskCleaner/MainWindow.RecognitionPersistence.cs");
        foreach (var bad in new[]
                 {
                     "OrganizeIdentifyAll", "OrganizeIdentifyCurrent", "OrgCtxIdentifyCurrent",
                     "AutoIdentifyTargets", "StartOrganizeAutoIdentify", "RunOrganizeIdentifyAsync",
                     "ProbeUnknownLocally", "OrganizeProgressPanel", "OrganizeScopeBar", "OrganizeStopBtn",
                     "OrganizeClassifyBtn", "OrganizeClassify_Click", "OrganizeForgetAiBtn",
                     "OrganizeForgetAi_Click", "OrganizeResultPanel", "AiPurposeBatchService",
                 })
        {
            Check($"整理页没有旧的批量/本层/Jev 入口：{bad}",
                !org.Contains(bad, StringComparison.Ordinal) && !xaml.Contains(bad, StringComparison.Ordinal));
        }
        Check("用途模型里不再有批量归类字段",
            !orgNode.Contains("BatchPurpose", StringComparison.Ordinal)
            && !orgNode.Contains("MarkBatchAsked", StringComparison.Ordinal)
            && !orgNode.Contains("NeedsPurposeClassification", StringComparison.Ordinal));
        Check("整理页没有 AI 归类/忘掉入口（只剩本地识别与展开）",
            !org.Contains("RestoreAiPurposes", StringComparison.Ordinal)
            && !org.Contains("TryRestoreAiPurpose", StringComparison.Ordinal)
            && !persistSrc.Contains("AiPurpose", StringComparison.Ordinal));
        Check("落盘层不再有 ai-purpose 槽（只剩 purpose）",
            !ReadSource("src/AiDiskCleaner/Services/RecognitionStore.cs")
                .Contains("PutAiPurpose", StringComparison.Ordinal)
            && !ReadSource("src/AiDiskCleaner/Services/RecognitionStore.cs")
                .Contains("TryGetAiPurpose", StringComparison.Ordinal));
        Check("Jev 判定通道类型全部移除（AiGateway / AiClient / DecisionRunner 都不在）",
            typeof(MainWindow).Assembly.GetType("AiDiskCleaner.Services.AiGateway") == null
            && typeof(MainWindow).Assembly.GetType("AiDiskCleaner.Services.AiClient") == null
            && typeof(MainWindow).Assembly.GetType("AiDiskCleaner.Services.Decisions.DecisionRunner") == null);
        // 「扫描重建 / 展开 / 筛选 / 切页会发请求」曾经用真实出站计数钉死；
        // v3.1 把出站口整个删掉了，这些路径**在类型层面**就不可能发请求。
        Check("整理页路径没有模型出站口可接（扫描/展开/筛选/切页不可能发请求）",
            typeof(MainWindow).Assembly.GetType("AiDiskCleaner.Services.AiGateway") == null);
        // 父子同时上榜（Users / Users\32098 容量一样）时，界面要解释一句：
        // 它们是父子、**勾了两个不会算两遍**（FolderSelectionService 会去重），
        // 但不解释用户会以为自己选了两份空间。
        Check("父子同时上榜会被标出来（免得用户以为重复）",
            org.Contains("MarkCoveredRows", StringComparison.Ordinal)
            && org.Contains("OrganizeBucket.HasAncestorIn", StringComparison.Ordinal)
            && xaml.Contains("IsCoveredByAncestor", StringComparison.Ordinal));
        Check("整理页仍然没有任何删除实现（删除只在 FolderDelete 合同里）",
            !org.Contains("SHFileOperation", StringComparison.Ordinal)
            && !org.Contains("Directory.Delete", StringComparison.Ordinal));

        // ---------- 9.5 逐项 AI 已整体移除 ----------
        Check("清理页 / 整理页都不再有逐项 AI 按钮",
            !xaml.Contains("ItemAiInline", StringComparison.Ordinal)
            && !xaml.Contains("ItemAi_Click", StringComparison.Ordinal)
            && !xaml.Contains("ItemAiResultOnly", StringComparison.Ordinal));
        Check("「操作」列已整体移除（两千多行里每行两个图标只是噪音）",
            !xaml.Contains("ColOrgAction", StringComparison.Ordinal)
            && !org.Contains("ColOrgAction", StringComparison.Ordinal));
        Check("「在资源管理器中打开」仍然有入口（右键菜单，不是没了）",
            xaml.Contains("OrganizeOpen_Click", StringComparison.Ordinal));
        Check("逐项 AI 的服务与视图类型都已经不存在了",
            !org.Contains("ItemAiService", StringComparison.Ordinal)
            && !org.Contains("DescribeAiTarget", StringComparison.Ordinal)
            && !orgNode.Contains("ItemAiView", StringComparison.Ordinal));

        // ---------- 9.6 本地证据**确实接线**（不是字符串存在） ----------
        // 启动时构造 MainWindow 就注入 LocalEvidenceService：系统 KnownFolder 快照 +
        // 已安装位置快照。这里走真实字段/真实识别路径验证，不搜源文件字符串。
        var purpose = Fld<FolderPurposeService>(win, "_folderPurpose");
        Check("FolderPurposeService 拿到本地证据（构造时注入，不是 null）",
            purpose.Evidence != null);
        Check("注入的是 LocalEvidenceService（真系统快照 + 安装位置服务，非占位类型）",
            purpose.Evidence is LocalEvidenceService,
            purpose.Evidence?.GetType().Name ?? "null");
        if (purpose.Evidence is LocalEvidenceService ev)
        {
            Check("启动即建立系统语义快照（重定向下载/桌面/图片等至少有一项）",
                ev.SystemRoles().Count > 0, ev.SystemRoles().Count.ToString());
        }

        // 安装清单叠加：走真实 InjectInstalledEvidence → _folderPurpose.Evidence → LocalRecognize，
        // 用「安装位置被本地认出」证明证据链路真的通了，而不是只存在一个类名。
        var appRoot = MakeDir("DemoApp", @"D:\DemoApp", 500_000, null, 0);
        var appList = new List<AppUninstallItem>
        {
            new AppUninstallItem { Name = "DemoApp", InstallLocation = @"D:\DemoApp" },
        };
        Call(win, "InjectInstalledEvidence", appList);
        var appNode = new OrganizeNode(appRoot, new FolderId(appRoot.FullPath, 1), 0, "DemoApp");
        Call(win, "LocalRecognize", appNode);
        Check("注入安装清单后：该安装位置被本地认出（真实证据链路，非字符串）",
            appNode.HasConclusion && appNode.PurposeName == "DemoApp"
            && appNode.Source == PurposeSource.Local,
            $"has={appNode.HasConclusion} name={appNode.PurposeName} source={appNode.Source}");
    }

    // ==================================================================
    // 10. 本轮（体验版）四项改动：驱动**真 MainWindow / 真控件**
    // ==================================================================

    static void ReviewFixesWiringTests(MainWindow win)
    {
        Console.WriteLine();
        Console.WriteLine("== 10. 本轮改动（卸载可信度 / 行内看文件 / 整理展开态 / 规则批选） ==");

        // ---------- 10.1 「选择规则明确的清理项」：行为保留，但按钮已从操作栏移除 ----------
        // 需求：用户不该再看到这个按钮；RuleSelect_Click 方法本身仍保留给既有行为测试。
        var ruleBtn = NamedField(win, "RuleSelectBtn");
        Check("清理首页操作栏已移除「选择规则明确的清理项」按钮（用户不可见）",
            ruleBtn == null,
            ruleBtn == null ? "gone" : "still present");
        // 方法没被删：用独立 sender 驱动（该方法不使用 sender）。
        var ruleSender = new System.Windows.Controls.Button();

        // 造一份真实分层结果：2 条规则明确 + 1 条启发式 + 1 条大文件
        var clearA = new CleanItem
        {
            Name = "a.bin", FullPath = @"C:\Users\x\AppData\Local\Temp\cache\a.bin", Size = 1000,
            Reason = "cache", Purpose = CleanPurpose.AppCache, Risk = CleanRisk.Safe, CanDelete = true,
            Evidence = EvidenceLevel.Signature,
        };
        var clearB = new CleanItem
        {
            Name = "b.dmp", FullPath = @"C:\Windows\Temp\b.dmp", Size = 4000,
            Reason = "dump", Purpose = CleanPurpose.Dump, Risk = CleanRisk.Safe, CanDelete = true,
            Evidence = EvidenceLevel.Signature,
        };
        var heuristicOnly = new CleanItem
        {
            Name = "c.bin", FullPath = @"C:\Users\x\AppData\Local\Temp\maybe\c.bin", Size = 2000,
            Reason = "looks like temp", Purpose = CleanPurpose.Temp, Risk = CleanRisk.Safe,
            CanDelete = true, Evidence = EvidenceLevel.Heuristic,
        };
        var bigFile = new CleanItem
        {
            Name = "movie.mkv", FullPath = @"C:\media\movie.mkv", Size = 9_000_000_000,
            Reason = "large", Purpose = CleanPurpose.Large, Risk = CleanRisk.Confirm, CanDelete = true,
            Evidence = EvidenceLevel.Signature,
        };
        WithEntry(clearA, @"C:\Users\x\AppData\Local\Temp\cache");
        WithEntry(clearB, @"C:\Windows\Temp");
        WithEntry(heuristicOnly, @"C:\Users\x\AppData\Local\Temp\maybe");
        var report = new CleanReport();
        report.Cleanable.Add(clearA);
        report.Cleanable.Add(clearB);
        report.Cleanable.Add(heuristicOnly);
        report.LargeFiles.Add(bigFile);
        var layered = CleanGroupingService.Build(
            new[] { clearA, clearB, heuristicOnly, bigFile });

        var savedReport = NamedField(win, "_report");
        var savedLayered = NamedField(win, "_layered");
        try
        {
            SetFld(win, "_report", report);
            SetFld(win, "_layered", layered);

            Call(win, "RuleSelect_Click", ruleSender, ClickArgs());
            var preview = NamedField(win, "_ruleSelectPreview");
            Check("点按钮只出预览，**一个都不勾**",
                preview != null && !clearA.Selected && !clearB.Selected
                && !heuristicOnly.Selected && !bigFile.Selected);
            Check("预览弹层真的打开了（OverlayRoot 可见）",
                (NamedField(win, "OverlayRoot") as System.Windows.FrameworkElement)?.Visibility
                    == Visibility.Visible);
            var ruleBody = NamedField(win, "RuleSelectBody") as System.Windows.FrameworkElement;
            Check("弹层里显示的是规则批选这一块", ruleBody?.Visibility == Visibility.Visible);

            var grid = NamedField(win, "RuleSelectGrid") as System.Windows.Controls.DataGrid;
            var groups = grid?.ItemsSource as System.Collections.IEnumerable;
            int groupCount = groups == null ? 0 : groups.Cast<object>().Count();
            Check("预览按类别列出（本次 2 类）", groupCount == 2, groupCount.ToString());

            // 「取消」= 不调用确认：必须什么都没发生
            Call(win, "DiscardRuleSelectPreview");
            Check("取消后不产生任何勾选",
                !clearA.Selected && !clearB.Selected && !heuristicOnly.Selected && !bigFile.Selected);

            // 「确认」：只有规则明确的那 2 条被勾上
            Call(win, "RuleSelect_Click", ruleSender, ClickArgs());
            Call(win, "ConfirmYes_Click", win, ClickArgs());
            Check("确认后只勾规则明确的两项",
                clearA.Selected && clearB.Selected && !heuristicOnly.Selected && !bigFile.Selected,
                $"{clearA.Selected}/{clearB.Selected}/{heuristicOnly.Selected}/{bigFile.Selected}");
            Check("大文件与启发式项没有被顺手带走（人工选择保留）",
                !bigFile.Selected && !heuristicOnly.Selected);
            var note = NamedField(win, "CleanSelectionNote") as System.Windows.Controls.TextBlock;
            CheckD("底部说明告诉用户「已按规则勾选 N 项，执行前仍会走检查」",
                note != null && note.Text.Contains("勾选", StringComparison.Ordinal), note?.Text ?? "null");
            // 原来是「AI 字段全程为空也没有影响」。逐项 AI 删掉后，候选条目上
            // **根本没有可写的 AI 槽位**了 —— 断言升级成结构断言，比运行时断言更硬。
            Check("AI 没有任何机会改变资格：候选条目上根本没有 AI 可写的槽位",
                typeof(CleanItem).GetProperty("AiNote") == null
                && typeof(CleanItem).GetProperty("AiSuggested") == null
                && typeof(CleanItem).GetProperty("Ai") == null);
        }
        finally
        {
            Call(win, "DiscardRuleSelectPreview");
            Call(win, "CloseOverlay_Click", win, ClickArgs());
            SetFld(win, "_report", savedReport);
            SetFld(win, "_layered", savedLayered);
        }

        // 没有规则明确的项 ⇒ 不给预览（只出一条说明）
        var onlyHeuristic = CleanGroupingService.Build(new[] { heuristicOnly, bigFile });
        Check("没有规则明确的项时不弹预览（CleanRuleSelectPreview.Build 返回 null）",
            CleanRuleSelectPreview.Build(onlyHeuristic) == null);

        // ---------- 10.1b 底部「全选」切换：文字恒定，两种外观 ----------------------
        // 需求：清空选择换成单个「全选」切换按钮 —— 未全选时点=全局全选，
        // 已全选时点=取消全部勾选；文字**永远**是「全选」，不改名。
        var toggleBtn = NamedField(win, "CleanSelectAllBtn") as System.Windows.Controls.Button;
        Check("底部操作栏有「全选」切换按钮，且旧的「清空选择」已移除",
            toggleBtn != null
            && NamedField(win, "ClearSelectionBtn") == null
            && toggleBtn.Content?.ToString() == Loc.SelectAll,
            toggleBtn?.Content?.ToString() ?? "null");

        var toggleA = new CleanItem
        {
            Name = "t1.bin", FullPath = @"C:\Users\x\AppData\Local\Temp\toggle\a.bin", Size = 10,
            Reason = "cache", Purpose = CleanPurpose.AppCache, Risk = CleanRisk.Safe, CanDelete = true,
            Evidence = EvidenceLevel.Signature,
        };
        var toggleB = new CleanItem
        {
            Name = "t2.dmp", FullPath = @"C:\Windows\Temp\toggle-b.dmp", Size = 20,
            Reason = "dump", Purpose = CleanPurpose.Dump, Risk = CleanRisk.Safe, CanDelete = true,
            Evidence = EvidenceLevel.Signature,
        };
        var toggleKeep = new CleanItem
        {
            Name = "keep.bin", FullPath = @"C:\media\keep.bin", Size = 30,
            Reason = "large", Purpose = CleanPurpose.Large, Risk = CleanRisk.Confirm, CanDelete = false,
        };
        var savedReport2 = NamedField(win, "_report");
        var savedLayered2 = NamedField(win, "_layered");
        try
        {
            SetFld(win, "_report", new CleanReport());
            SetFld(win, "_layered", CleanGroupingService.Build(new[] { toggleA, toggleB, toggleKeep }));

            // 一个都没勾：可用、幽灵态、文字「全选」、可访问名称也是「全选」
            Call(win, "UpdateSelectionUi");
            CheckD("一个都没勾时「全选」可用且是幽灵态",
                toggleBtn != null && toggleBtn.IsEnabled
                && ReferenceEquals(toggleBtn.Style, win.FindResource("GhostButton"))
                && System.Windows.Automation.AutomationProperties.GetName(toggleBtn) == Loc.SelectAll
                && (toggleBtn.ToolTip as string) == Loc.SelectAllActionTip,
                toggleBtn == null ? "null"
                    : $"enabled={toggleBtn.IsEnabled} style={toggleBtn.Style?.ToString()?.Length}");

            // 点一下：全局所有可清理项被勾上，受保护 / 不可删的项不动
            Call(win, "CleanSelectAll_Click", toggleBtn!, ClickArgs());
            Check("点「全选」一次性勾上全局所有可清理项",
                toggleA.Selected && toggleB.Selected && !toggleKeep.Selected,
                $"{toggleA.Selected}/{toggleB.Selected}/{toggleKeep.Selected}");
            CheckD("已全选时按钮换成主按钮态，但文字仍是「全选」、提示改为取消",
                ReferenceEquals(toggleBtn!.Style, win.FindResource("PrimaryButton"))
                && toggleBtn.Content?.ToString() == Loc.SelectAll
                && (toggleBtn.ToolTip as string) == Loc.SelectAllClearTip,
                toggleBtn?.Content?.ToString() ?? "null");

            // 再点一下：取消全部勾选，回到幽灵态
            Call(win, "CleanSelectAll_Click", toggleBtn!, ClickArgs());
            Check("已全选时再点「全选」= 取消全部勾选",
                !toggleA.Selected && !toggleB.Selected && !toggleKeep.Selected);
            Check("取消后回到幽灵态且文字不变",
                ReferenceEquals(toggleBtn!.Style, win.FindResource("GhostButton"))
                && toggleBtn.Content?.ToString() == Loc.SelectAll);

            // 没有可清理项时禁用
            SetFld(win, "_layered", CleanGroupingService.Build(new[] { toggleKeep }));
            Call(win, "UpdateSelectionUi");
            Check("没有任何可清理项时「全选」禁用", toggleBtn != null && !toggleBtn.IsEnabled);
        }
        finally
        {
            SetFld(win, "_report", savedReport2);
            SetFld(win, "_layered", savedLayered2);
        }

        // ---------- 10.2 行内看文件：点位置行就地展开，不绕 AI、不进明细面板 ----------
        var locItem = new CleanItem
        {
            Name = "x.bin", FullPath = @"C:\Users\x\AppData\Local\Temp\cache\x.bin", Size = 1234,
            Purpose = CleanPurpose.AppCache, Risk = CleanRisk.Safe, CanDelete = true,
            Evidence = EvidenceLevel.Signature,
            Entry = new FileEntry
            {
                Name = "x.bin", FullPath = @"C:\Users\x\AppData\Local\Temp\cache\x.bin",
                Kind = EntryKind.File, Size = 1234, Modified = new DateTime(2026, 1, 2, 3, 4, 5),
                Parent = MakeDir("cache", @"C:\Users\x\AppData\Local\Temp\cache", 1234, null, 0),
            },
        };
        // 位置节点通过**真实分组服务**产生，不手工拼 —— 保证用的就是界面那一份
        var locNode = CleanGroupingService.Build(new[] { locItem })
            .Purposes.SelectMany(p => p.Locations).First();
        var rowHost = new System.Windows.Controls.Button { DataContext = locNode };
        Call(win, "LocationRowOpen_Click", rowHost,
            new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left));
        Check("点位置行 = 就地展开候选文件（不是跳明细面板）",
            locNode.IsFilesOpen && NamedField(win, "_openLocation") == null,
            $"open={locNode.IsFilesOpen} detail={(NamedField(win, "_openLocation") != null)}");
        Check("行内列表显示文件名/大小/修改时间",
            locNode.VisibleFiles.Count == 1
            && locNode.VisibleFiles[0].ModifiedText.Length > 0
            && locNode.VisibleFiles[0].SizeText.Length > 0,
            locNode.VisibleFiles.Count.ToString());
        Check("行内看文件**不需要 AI**（没有建立任何 AI 视图）",
            NamedField(locItem, "_ai") == null);
        var inlineHost = new System.Windows.Controls.CheckBox { DataContext = locItem };
        locItem.Selected = true;
        Call(win, "InlineFileCheck_Click", inlineHost, ClickArgs());
        Check("行内勾选写回同一份状态（位置三态同步）", locNode.SelectedCount == 1);
        Call(win, "LocationViewFiles_Click", rowHost, ClickArgs());
        Check("再点一次收起，且选择不丢", !locNode.IsFilesOpen && locItem.Selected);
        Call(win, "LocationOpenFull_Click", rowHost, ClickArgs());
        Check("只有需要搜索时才进右侧明细面板（单一「更多」入口）",
            NamedField(win, "_openLocation") != null);
        Call(win, "CloseDetail");

        // 行内列表刻意不做嵌套滚动：外层虚拟化列表不能被内层滚动区抢滚轮
        var xaml = ReadSource("src/AiDiskCleaner/MainWindow.xaml");
        int inlineStart = xaml.IndexOf("行内展开：这一处的候选文件", StringComparison.Ordinal);
        // 模型瘦提示已挪到文件表上方；这段以位置行模板结束为界。
        int inlineEnd = inlineStart < 0 ? -1 : xaml.IndexOf("x:Key=\"TermScrollThumb\"", inlineStart, StringComparison.Ordinal);
        string inlineBlock = inlineStart >= 0 && inlineEnd > inlineStart
            ? xaml[inlineStart..inlineEnd] : "";
        Check("行内文件块是有的（定位到了模板片段）", inlineBlock.Length > 0);
        Check("行内文件块里没有自己的 ScrollViewer / 可滚动 ListBox（避免嵌套滚动）",
            inlineBlock.Length > 0
            && !inlineBlock.Contains("<ScrollViewer", StringComparison.Ordinal)
            && !inlineBlock.Contains("<ListBox", StringComparison.Ordinal));
        Check("行内文件块绑的是真正的 CleanItem 复选框（能勾选）",
            inlineBlock.Contains("Mode=TwoWay", StringComparison.Ordinal)
            && inlineBlock.Contains("CanDelete", StringComparison.Ordinal));
        Check("行内文件块明确区分「应用内查看」与「资源管理器定位」",
            xaml.Contains("IconFolderOpen", StringComparison.Ordinal)
            && xaml.Contains("在资源管理器中打开", StringComparison.Ordinal));

        // ---------- 10.3 整理页：无箭头的原因、只有文件的入口、未知 ≠ 0KB ----------
        // 「只有文件」：不伪造箭头，但有一个**真的能点**的文件入口
        var filesOnlyDir = MakeDir("PerfLogsLike", @"X:\PerfLogsLike", 2048, null, 0);
        var rf = new FileEntry
        {
            Name = "one.log", FullPath = @"X:\PerfLogsLike\one.log", Kind = EntryKind.File,
            Size = 2048, Modified = new DateTime(2026, 2, 3, 4, 5, 6), Parent = filesOnlyDir,
        };
        filesOnlyDir.Children.Add(rf);
        ResetOrganizeCollections(win);
        var filesOnlyNode = new OrganizeNode(filesOnlyDir, new FolderId(filesOnlyDir.FullPath, 1), 0, "PerfLogsLike");
        filesOnlyNode.SetChildDirCount(FolderOrganize.DirectChildDirs(filesOnlyDir, 0).Total);
        var filesHost = new System.Windows.Controls.Button { DataContext = filesOnlyNode };
        Check("只有文件：形态判定为 FilesOnly 且没有展开箭头",
            filesOnlyNode.ContentKind == FolderContentKind.FilesOnly && !filesOnlyNode.CanExpand);
        Call(win, "OrganizeFiles_Click", filesHost, ClickArgs());
        Check("只有文件：文件入口真的能展开，并且列出文件",
            filesOnlyNode.IsFilesOpen && filesOnlyNode.VisibleFiles.Count == 1,
            filesOnlyNode.VisibleFiles.Count.ToString());
        Check("只有文件：展开文件**不动**展开状态、也不触发子对象材料化",
            !filesOnlyNode.IsExpanded && !filesOnlyNode.ChildrenLoaded);
        Call(win, "OrganizeFiles_Click", filesHost, ClickArgs());
        Check("只有文件：再点一次收起", !filesOnlyNode.IsFilesOpen);

        // 链接 / 重解析点：本次没进去 ⇒ 未知，且**不是 0 KB**
        var linkDir = MakeDir("LinkedDir", @"X:\LinkedDir", 0, null, 0);
        linkDir.IsReparsePoint = true;
        var linkNode = new OrganizeNode(linkDir, new FolderId(linkDir.FullPath, 1), 0, "LinkedDir");
        linkNode.SetChildDirCount(0);
        CheckD("链接：容量写「未知（未扫描）」，不是 0 KB",
            linkNode.SizeText == Loc.OrganizeSizeNotScanned && !linkNode.SizeText.Contains("0 KB"),
            linkNode.SizeText);
        Check("链接：不给展开箭头、也不给文件入口",
            !linkNode.CanExpand && !linkNode.CanViewFiles);
        CheckD("链接：状态字与空目录**明确区分**",
            linkNode.ContentStateText != Loc.OrganizeStateEmpty,
            linkNode.ContentStateText);

        var emptyDir = MakeDir("EmptyDir", @"X:\EmptyDir", 0, null, 0);
        var emptyNode = new OrganizeNode(emptyDir, new FolderId(emptyDir.FullPath, 1), 0, "EmptyDir");
        emptyNode.SetChildDirCount(0);
        CheckD("空目录：容量写「扫描无内容」，不是 0 KB",
            emptyNode.SizeText == Loc.OrganizeSizeEmpty, emptyNode.SizeText);

        // 整理页对象**没有清理能力**（结构性断言，防止以后有人往上加字段）
        var organizeProps = typeof(OrganizeNode).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Check("整理对象没有 Selected / CanDelete / Risk / Verdict 这类清理能力",
            !organizeProps.Contains("Selected") && !organizeProps.Contains("CanDelete")
            && !organizeProps.Contains("Risk") && !organizeProps.Contains("Verdict"),
            string.Join(",", organizeProps.Where(p => p is "Selected" or "CanDelete" or "Risk" or "Verdict")));

        // 整理页仍然只有「单项 AI」这一个模型入口（不能因为本轮改动多出批量入口）
        var organizeSource = ReadSource("src/AiDiskCleaner/MainWindow.Organize.cs");
        Check("整理页没有多出批量识别入口（仍然只有单项 AI）",
            !organizeSource.Contains("StartOrganizeAutoIdentify", StringComparison.Ordinal)
            && !organizeSource.Contains("OrganizeIdentifyAllBtn", StringComparison.Ordinal));

        // ---------- 10.4 卸载页：只讲事实，不给建议 ----------
        //
        // 这一节以前守「卸载建议」那套档位：中性档（未评估）、系统/驱动条目落「建议保留」、
        // 「按建议勾选」只覆盖「建议卸载」那一档。卸载页固定为**事实页**
        // （名称 / 安装日期 / 用途 / 占用 / 操作）之后，那套机制在生产里没有任何调用方，
        // 已整体删除；这些断言随能力一起删除（不是隐藏，是连代码带文案一起没了）。
        // 结构判定「Windows 自带组件」的能力没有丢，断言在
        // AppRecommendationCheck（IsInboxComponent）与 AppPurposeCheck（WindowsInboxComponent）里。
        // 事实口径（占用三态文案）在 10.5。

        // ---------- 10.5 占用三态在真条目上的显示 ----------
        var measured = new AppUninstallItem
        {
            AppId = "m", Name = "measured", ActualSizeBytes = 1_500_000, HasMeasuredSize = true,
        };
        var recordOnly = new AppUninstallItem { AppId = "e", Name = "record", SizeBytes = 900_000 };
        var noNumber = new AppUninstallItem { AppId = "u", Name = "unknown" };
        CheckD("实测：直接写数字，不带「估算」",
            measured.ActualSizeText == AppUninstallItem.FormatFootprint(1_500_000),
            measured.ActualSizeText);
        CheckD("只有安装记录：格子只写数字，来源进悬停",
            recordOnly.ActualSizeText == AppUninstallItem.FormatFootprint(900_000)
            && recordOnly.FootprintHint.Contains(Loc.AppSizeSourceRecord, StringComparison.Ordinal),
            recordOnly.ActualSizeText + " | " + recordOnly.FootprintHint);
        CheckD("两样都没有：写「未知」而不是 0",
            noNumber.ActualSizeText == Loc.AppSizeUnknown, noNumber.ActualSizeText);
        Check("三种来源的排序权重是 实测 < 记录 < 未知",
            measured.SizeConfidenceRank < recordOnly.SizeConfidenceRank
            && recordOnly.SizeConfidenceRank < noNumber.SizeConfidenceRank);
        CheckD("占用悬停里写明「磁盘占用不等于卸载能释放的量」",
            measured.FootprintHint.Contains(Loc.AppFootprintNotEqualFree),
            measured.FootprintHint);
    }

    private sealed class FakeClassifyChatModel : AiDiskCleaner.Services.Agent.IChatModel
    {
        public string ModelId => "fake-classify";
        public List<AiDiskCleaner.Services.Agent.ChatRequest> Requests { get; } = new();
        public Func<AiDiskCleaner.Services.Agent.ChatRequest, string> ReplyFactory { get; set; } = _ => "{\"items\":[]}";
        public string Describe() => "fake://classify";

        public Task<AiDiskCleaner.Services.Agent.ChatResponse> CompleteAsync(
            AiDiskCleaner.Services.Agent.ChatRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            // 故意切到后台线程池返回，还原真实 HttpClient 线程切换场景，守住 WPF 跨线程写 UI 属性不崩溃
            return Task.Run(() => new AiDiskCleaner.Services.Agent.ChatResponse
            {
                Content = ReplyFactory(request),
                Model = ModelId,
                FinishReason = "stop",
            }, ct);
        }
    }

    static void ClassifyReorganizeTests(MainWindow win)
    {
        var tabType = typeof(MainWindow).GetNestedType("RightTab", BindingFlags.NonPublic)!;
        Check("已移除独立 Classify 同级 Tab，主导航回归双页简洁结构（Clean / Organize / Uninstall）",
            !Enum.GetNames(tabType).Contains("Classify"));
        Call(win, "ShowRightTab", Enum.Parse(tabType, "Clean"));
        Check("清理中心默认展现极净双栏目录表格（OrganizeStageGrid 可见，排序条折叠待命，无搬家弹窗）",
            (NamedField(win, "OrganizeStageGrid") as UIElement)?.Visibility == Visibility.Visible
            && (NamedField(win, "OrganizeClassifyBar") as UIElement)?.Visibility == Visibility.Collapsed
            && NamedField(win, "ClassifyModalBody") == null);

        // 构造真实临时目录与内存 MFT 树，离线端到端测透纯软件内虚拟分类、AI 脱敏、彩色组头折叠、本地持久化，以及零文件移动
        string tempDrive = Path.Combine(Path.GetTempPath(), "dsh-classify-" + Guid.NewGuid().ToString("N")[..8]);
        string cacheDir = Path.Combine(tempDrive, "_cache");
        Directory.CreateDirectory(tempDrive);
        try
        {
            string animeDir = Path.Combine(tempDrive, "[Skymoon-Raws][Anime]");
            string greenGameDir = Path.Combine(tempDrive, "IndieGame");
            string unityDevDir = Path.Combine(tempDrive, "Unity");
            string steamLibDir = Path.Combine(tempDrive, "SteamLibrary");
            string docsDir = Path.Combine(tempDrive, "文档");
            string aiWeightsDir = Path.Combine(tempDrive, "LoraModels");
            string recycleDir = Path.Combine(tempDrive, "$RECYCLE.BIN");
            Directory.CreateDirectory(animeDir);
            Directory.CreateDirectory(greenGameDir);
            Directory.CreateDirectory(unityDevDir);
            Directory.CreateDirectory(steamLibDir);
            Directory.CreateDirectory(docsDir);
            Directory.CreateDirectory(aiWeightsDir);
            Directory.CreateDirectory(recycleDir);
            File.WriteAllBytes(Path.Combine(animeDir, "ep01.mkv"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(animeDir, "ep01.ass"), new byte[128]);
            File.WriteAllBytes(Path.Combine(greenGameDir, "UnityPlayer.dll"), new byte[512]);
            File.WriteAllBytes(Path.Combine(greenGameDir, "Play.exe"), new byte[512]);
            File.WriteAllBytes(Path.Combine(unityDevDir, "UnityPlayer.dll"), new byte[512]);
            File.WriteAllBytes(Path.Combine(unityDevDir, "Unity.exe"), new byte[512]);
            File.WriteAllBytes(Path.Combine(aiWeightsDir, "flux.safetensors"), new byte[512]);

            var rootEntry = MakeDir("TempRoot", tempDrive, 4096, null, 7);
            var eAnime = MakeDir("[Skymoon-Raws][Anime]", animeDir, 100_000_000, rootEntry, 0);
            MakeFile("ep01.mkv", Path.Combine(animeDir, "ep01.mkv"), 99_000_000, eAnime);
            MakeFile("ep01.ass", Path.Combine(animeDir, "ep01.ass"), 1_000_000, eAnime);

            var eGame = MakeDir("IndieGame", greenGameDir, 50_000_000, rootEntry, 0);
            MakeFile("UnityPlayer.dll", Path.Combine(greenGameDir, "UnityPlayer.dll"), 10_000_000, eGame);
            MakeFile("Play.exe", Path.Combine(greenGameDir, "Play.exe"), 40_000_000, eGame);

            var eUnity = MakeDir("Unity", unityDevDir, 90_000_000, rootEntry, 0);
            MakeFile("UnityPlayer.dll", Path.Combine(unityDevDir, "UnityPlayer.dll"), 10_000_000, eUnity);
            MakeFile("Unity.exe", Path.Combine(unityDevDir, "Unity.exe"), 80_000_000, eUnity);

            var eSteam = MakeDir("SteamLibrary", steamLibDir, 120_000_000, rootEntry, 0);
            var eDocs = MakeDir("文档", docsDir, 150_000_000, rootEntry, 0);

            var eLora = MakeDir("LoraModels", aiWeightsDir, 80_000_000, rootEntry, 0);
            MakeFile("flux.safetensors", Path.Combine(aiWeightsDir, "flux.safetensors"), 80_000_000, eLora);

            var eRecycle = MakeDir("$RECYCLE.BIN", recycleDir, 60_000_000, rootEntry, 0);
            _ = eSteam; _ = eDocs; _ = eRecycle;

            var customCats = ClassifyCategoryRule.DefaultPresets();
            customCats.Add(new ClassifyCategoryRule
            {
                Id = "ai_weights",
                Name = "AI模型",
                FolderName = "AIModels",
                Description = "safetensors / gguf / LoRA 权重文件",
                Enabled = true,
            });

            var ctx = new FolderClassificationService.ContextSnapshot(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CleanListSnapshot.NormPath(docsDir) },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [CleanListSnapshot.NormPath(steamLibDir)] = "Steam" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var items = FolderClassificationService.BuildCandidates(
                rootEntry, customCats, ctx, includeDownloadsChildren: false);

            var animeItem = items.FirstOrDefault(x => x.SourceName == "[Skymoon-Raws][Anime]");
            var gameItem = items.FirstOrDefault(x => x.SourceName == "IndieGame");
            var unityItem = items.FirstOrDefault(x => x.SourceName == "Unity");
            var steamItem = items.FirstOrDefault(x => x.SourceName == "SteamLibrary");
            var docsItem = items.FirstOrDefault(x => x.SourceName == "文档");
            var loraItem = items.FirstOrDefault(x => x.SourceName == "LoraModels");
            var recycleItem = items.FirstOrDefault(x => x.SourceName == "$RECYCLE.BIN");

            Check("纯媒体番剧目录判为 Safe 并归入影音娱乐(media)",
                animeItem != null && animeItem.Tier == ClassifySafetyTier.Safe && animeItem.CategoryId == "media",
                animeItem?.Tier + "/" + animeItem?.CategoryId);
            Check("单机游戏归入游戏(games)，开发引擎 Unity 优先归入开发工具(dev)而不误判游戏",
                gameItem != null && gameItem.CategoryId == "games"
                && unityItem != null && unityItem.CategoryId == "dev",
                $"game={gameItem?.CategoryId} unity={unityItem?.CategoryId}");
            Check("软件内虚拟视图不锁死游戏库(SteamLibrary)与用户目录(文档)，均自动归入对应色组",
                steamItem != null && !steamItem.IsBlocked && steamItem.CategoryId == "games"
                && docsItem != null && !docsItem.IsBlocked && docsItem.CategoryId == "docs",
                $"steam={steamItem?.Tier}/{steamItem?.CategoryId} docs={docsItem?.Tier}/{docsItem?.CategoryId}");
            Check("仅真实系统核心目录($RECYCLE.BIN)留在系统核心分组",
                recycleItem is { Tier: ClassifySafetyTier.BlockedConservative, IsBlocked: true, IsKeepInPlace: true });

            // 验证 AI 分类按自定义规则归类，且即使 AI 越权尝试给系统核心改分类也被安全夹紧
            var fakeModel = new FakeClassifyChatModel
            {
                ReplyFactory = _ =>
                    "```json\n" +
                    "{\"items\":[" +
                    (loraItem != null ? $"{{\"folderId\":\"{loraItem.Id}\",\"categoryId\":\"ai_weights\",\"reason\":\"包含 safetensors 权重\"}}," : "") +
                    (recycleItem != null ? $"{{\"folderId\":\"{recycleItem.Id}\",\"categoryId\":\"games\",\"reason\":\"误尝试归类回收站\"}}" : "") +
                    "]}\n```"
            };
            var svc = new FolderClassificationService();
            int uiThreadId = Thread.CurrentThread.ManagedThreadId;
            bool propertyChangedOnWrongThread = false;
            foreach (var it in items)
                it.PropertyChanged += (_, _) =>
                {
                    if (Thread.CurrentThread.ManagedThreadId != uiThreadId)
                        propertyChangedOnWrongThread = true;
                };

            var frame = new System.Windows.Threading.DispatcherFrame();
            AiDiskCleaner.Services.ClassifyAiResult? aiRes = null;
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                try { aiRes = await svc.ClassifyWithAiAsync(fakeModel, items, customCats, sendFullPaths: false); }
                finally { frame.Continue = false; }
            });
            System.Windows.Threading.Dispatcher.PushFrame(frame);

            Check("AI 异步调用即使在后台线程池返回，也能安全回到 UI 线程更新行属性不崩溃",
                aiRes != null && aiRes.Ok && !propertyChangedOnWrongThread,
                $"ok={aiRes?.Ok} wrongThread={propertyChangedOnWrongThread}");

            Check("AI 识别自定义规则并将 safetensors 目录归入「AI模型」",
                aiRes != null && aiRes.Ok && loraItem != null && loraItem.CategoryId == "ai_weights" && loraItem.FromAi,
                $"ok={aiRes?.Ok} cat={loraItem?.CategoryId}");
            Check("系统核心目录($RECYCLE.BIN)即使 AI 返回其他分组仍保持锁定原位",
                recycleItem != null && recycleItem.IsBlocked && recycleItem.IsKeepInPlace);
            Check("路径出站默认经 %USER% 脱敏",
                FolderClassificationService.RedactUser(@"C:\Users\SecretName\Downloads\Demo") == @"C:\Users\%USER%\Downloads\Demo");

            // 验证左环扫描生长动画状态机 + AI 分析后同表原位彩色虚拟分组 + 折叠/展开
            Call(win, "BeginRadialScanGrowth");
            Call(win, "UpdateRadialScanGrowth", 55, 1234L);
            Check("圆环本体扫盘生长时中心文本实时报进度",
                (NamedField(win, "RadialHeroValue") as TextBlock)?.Text == "55%");
            Call(win, "CompleteRadialScanGrowth");

            var outcome = new MainWindow.UnifiedClassifyOutcome(
                aiRes!.Ok, true, aiRes.ClassifiedCount, aiRes.Error, items, customCats);
            ((Task)Call(win, "PresentClassifySortPreviewAsync", outcome, false, System.Threading.CancellationToken.None)!)
                .GetAwaiter().GetResult();
            var stageRows = Fld<System.Collections.ObjectModel.ObservableCollection<OrganizeNode>>(win, "_organizeRows");
            var hdrs = stageRows.Where(r => r.IsClassifyGroupHeader).ToList();
            var sortedChildren = stageRows.Where(r => r.IsClassifySortedItem && !r.IsClassifyGroupHeader).ToList();
            int firstKeepIdx = stageRows.ToList().FindIndex(r => r.IsClassifyGroupHeader && r.ClassifyGroupId == ClassifyCategoryRule.KeepId);
            int lastMovableIdx = stageRows.ToList().FindLastIndex(r => r.IsClassifySortedItem && !r.IsClassifyGroupHeader && r.ClassifyGroupId != ClassifyCategoryRule.KeepId);
            Check("AI 分类完成后就在原 OrganizeGrid 内聚拢生成纯虚拟彩色分组（无搬家箭头、无物理移动按钮）",
                (NamedField(win, "OrganizeGrid") as UIElement)?.Visibility == Visibility.Visible
                && (NamedField(win, "OrganizeReportPanel") as UIElement)?.Visibility == Visibility.Collapsed
                && NamedField(win, "ClassifyActionBtn") == null
                && hdrs.Count >= 2
                && hdrs.All(h => !string.IsNullOrWhiteSpace(h.ClassifyAccentHex) && h.ClassifyAccentHex.StartsWith("#") && string.IsNullOrEmpty(h.ClassifyGroupTarget))
                && sortedChildren.Any(c => c.HasClassifyTint && c.Name == "LoraModels")
                && firstKeepIdx > lastMovableIdx && lastMovableIdx >= 0,
                $"headers={hdrs.Count} sorted={sortedChildren.Count} firstKeep={firstKeepIdx} lastMov={lastMovableIdx}");

            // 验证分组折叠/展开
            int rowsBeforeCollapse = stageRows.Count;
            Call(win, "ToggleClassifyGroupCollapsed", "games");
            int rowsAfterCollapse = stageRows.Count;
            Check("点击彩色分组标题可折叠对应分组行",
                rowsAfterCollapse < rowsBeforeCollapse,
                $"before={rowsBeforeCollapse} after={rowsAfterCollapse}");

            // 验证本地持久化缓存往返（保存分类与折叠状态 -> 重新加载完全一致），且磁盘真实文件纹丝不动
            VirtualClassifyCache.Save("Z:\\", items, customCats, new[] { "games" }, hasAiResults: true, directoryOverride: cacheDir);
            var loadedSnap = VirtualClassifyCache.TryLoad("Z:\\", directoryOverride: cacheDir);
            VirtualClassifyCache.UpdateCollapsedGroups("Z:\\", new[] { "games", "media" }, directoryOverride: cacheDir);
            var loadedSnap2 = VirtualClassifyCache.TryLoad("Z:\\", directoryOverride: cacheDir);
            string loraNorm = CleanListSnapshot.NormPath(aiWeightsDir);
            Check("虚拟分类本地持久化缓存可完整往返恢复类别、AI理由与折叠分组状态",
                loadedSnap != null && loadedSnap.HasAiResults
                && loadedSnap.Entries.TryGetValue(loraNorm, out var savedLora)
                && savedLora.CategoryId == "ai_weights" && savedLora.FromAi
                && loadedSnap.CollapsedGroups.Contains("games")
                && loadedSnap2 != null && loadedSnap2.CollapsedGroups.Contains("media"),
                $"entries={loadedSnap?.Entries.Count} collapsed2={loadedSnap2?.CollapsedGroups.Count}");

            Check("全流程 100% 只读虚拟视图：磁盘上所有原目录与文件保持原位零改动",
                File.Exists(Path.Combine(animeDir, "ep01.mkv"))
                && File.Exists(Path.Combine(aiWeightsDir, "flux.safetensors"))
                && File.Exists(Path.Combine(greenGameDir, "Play.exe"))
                && !Directory.Exists(Path.Combine(tempDrive, "AIModels"))
                && !Directory.Exists(Path.Combine(tempDrive, "Media")));

            Call(win, "ExitClassifySortMode", false);
            Call(win, "RefreshOrganizeRows", null, null);
            Check("清除分类状态可干净恢复常规目录列表",
                !stageRows.Any(r => r.IsClassifyGroupHeader || r.HasClassifyTint));
            Call(win, "ResetClassifyStageOnScanChange");
        }
        finally
        {
            try { if (Directory.Exists(tempDrive)) Directory.Delete(tempDrive, true); } catch { }
        }
    }

    static void MakeFile(string name, string fullPath, long size, FileEntry parent)
    {
        var f = new FileEntry
        {
            Name = name,
            FullPath = fullPath,
            Kind = EntryKind.File,
            Parent = parent,
            Size = size,
            Allocated = size,
        };
        parent.Children.Add(f);
    }

    /// <summary>读仓库里的源文件（从 bin 往上找到仓库根）：用于断言"某个入口不存在"。</summary>
    static string ReadSource(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.targets"))) dir = dir.Parent;
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
