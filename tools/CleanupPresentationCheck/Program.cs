using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AiDiskCleaner;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;

namespace CleanupPresentationCheck;

/// <summary>
/// 清理页呈现回归检查。
///
/// 为什么单独做：清理首页的分区标题/背景边界/留白/对比、窄窗口下的列宽，
/// 都不是字符串断言能覆盖的 —— 它们要么在编译后的 BAML 里，要么只有真的
/// Measure/Arrange 一遍才知道。这里：
///   1. 真的 `new MainWindow()`（加载编译后的 BAML）；
///   2. 用**真实的分区数据模板**渲染两级分区，量标题与统计的宽度；
///   3. 渲染一张独立测试窗口截图（隔离配置 + 合成数据，不碰真实扫描/清理）。
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
        // 免得应用里 `new Progress<T>(...)` 的回调在线程池线程上直接碰 UI 对象。
        Dispatcher.CurrentDispatcher.Invoke(() => { });
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

        // 干净配置目录：不碰用户真实配置，也不写用户日志
        string cfg = Path.Combine(Path.GetTempPath(), "dashao-cleanuppres-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cfg);
        Environment.SetEnvironmentVariable("DASHAOHUO_CONFIG_DIR", cfg);

        Console.WriteLine("== 1. 真实加载 App.xaml + MainWindow（BAML 路径） ==");
        App? app = null;
        try
        {
            app = new App();
            app.InitializeComponent();
            Check("App.xaml 加载", true);
        }
        catch (Exception ex) { Check("App.xaml 加载", false, Describe(ex)); }

        MainWindow? win = null;
        try
        {
            win = new MainWindow();
            Check("MainWindow 构造 / BAML 解析", true);
        }
        catch (Exception ex) { Check("MainWindow 构造 / BAML 解析", false, Describe(ex)); }

        if (win == null) return Summary();

        Console.WriteLine("== 2. 文案：不替用户打包票，两个分区分得清 ==");
        string groupSafe = Loc.CleanGroupSafe(3, "1 G");
        Check("候选组标题不再一概声称「删了会自动重建」",
            !groupSafe.Contains("自动重建") && !groupSafe.Contains("rebuilds itself", StringComparison.OrdinalIgnoreCase),
            groupSafe);
        Check("候选分区副标题不再声称自动重建",
            !Loc.SectionSubtitleSafe.Contains("自动重建")
            && !Loc.SectionSubtitleSafe.Contains("rebuilds", StringComparison.OrdinalIgnoreCase),
            Loc.SectionSubtitleSafe);
        Check("候选分区副标题写明「默认没有勾选」",
            Loc.SectionSubtitleSafe.Contains("没有勾选")
            || Loc.SectionSubtitleSafe.Contains("ticked", StringComparison.OrdinalIgnoreCase),
            Loc.SectionSubtitleSafe);
        Check("确认分区副标题点名大文件/旧文件/下载这类「可能还有用」的内容",
            Loc.SectionSubtitleConfirm.Contains("下载") || Loc.SectionSubtitleConfirm.Contains("大文件")
            || Loc.SectionSubtitleConfirm.Contains("download", StringComparison.OrdinalIgnoreCase)
            || Loc.SectionSubtitleConfirm.Contains("large", StringComparison.OrdinalIgnoreCase),
            Loc.SectionSubtitleConfirm);
        Check("两个分区的英文标题不再是「review first」这种小写半句",
            !string.Equals(Loc.LayerConfirm, "review first", StringComparison.Ordinal), Loc.LayerConfirm);

        Console.WriteLine("== 3. 分区模型：两个风险档 + 独立视觉键 + 可访问名称 ==");
        var sections = BuildSections();
        Check("风险档各建一个分区", sections.Count == 2, sections.Count.ToString());
        if (sections.Count == 2)
        {
            var safe = sections[0];
            var confirm = sections[1];
            Check("安全档在前、确认档在后", safe.RiskTier == 0 && confirm.RiskTier == 1);
            Check("两分区背景键不同", safe.SurfaceKey != confirm.SurfaceKey,
                safe.SurfaceKey + " / " + confirm.SurfaceKey);
            Check("两分区色带键不同", safe.AccentKey != confirm.AccentKey,
                safe.AccentKey + " / " + confirm.AccentKey);
            Check("分区可访问名称包含标题与统计（折叠时也能听到里面有多少）",
                safe.HeaderAccessibleName.StartsWith(safe.HeaderTitle, StringComparison.Ordinal)
                && safe.HeaderAccessibleName.Contains("·"),
                safe.HeaderAccessibleName);
        }

        Console.WriteLine("== 4. 真实模板：矢量箭头 / 折叠不渲染行 / 无 › 字符 ==");
        var sectionsControl = NamedField(win, "PurposeSections") as ItemsControl;
        Check("首页分区列表控件存在", sectionsControl != null);
        if (sectionsControl == null) return Summary();

        sectionsControl.ItemsSource = sections;
        Loc.Lang = AppLang.Zh;
        Layout(win, 1000, 700);

        var cp0 = sectionsControl.ItemContainerGenerator.ContainerFromIndex(0) as ContentPresenter;
        Check("分区容器已生成（真实 DataTemplate）", cp0 != null);

        if (cp0 != null)
        {
            var down = FindName(cp0, "SectionChevronDown") as FrameworkElement;
            var right = FindName(cp0, "SectionChevronRight") as FrameworkElement;
            Check("分区标题用的是现有矢量箭头（下 / 右）", down != null && right != null);

            sections[0].IsExpanded = false;
            Layout(win, 1000, 700);
            Check("折叠分区：显示右向箭头、隐藏下向箭头",
                right?.Visibility == Visibility.Visible && down?.Visibility != Visibility.Visible,
                $"right={right?.Visibility} down={down?.Visibility}");

            bool glyph = Descendants(sectionsControl).OfType<TextBlock>()
                .Any(tb => tb.Text == "›");
            Check("清理首页不再用 › 字符当箭头", !glyph);

            var rowsHost = Descendants(cp0).OfType<ItemsControl>()
                .FirstOrDefault(ic => ReferenceEquals(ic.ItemsSource, sections[0].Rows));
            Check("折叠分区不渲染用途行",
                rowsHost != null && rowsHost.Visibility != Visibility.Visible,
                rowsHost?.Visibility.ToString() ?? "not found");

            sections[0].IsExpanded = true;
            Layout(win, 1000, 700);
            Check("展开分区：显示下向箭头、隐藏右向箭头",
                down?.Visibility == Visibility.Visible && right?.Visibility != Visibility.Visible,
                $"right={right?.Visibility} down={down?.Visibility}");
            Check("展开分区渲染用途行",
                rowsHost != null && rowsHost.Visibility == Visibility.Visible,
                rowsHost?.Visibility.ToString() ?? "not found");

            var headerBtn = Descendants(cp0).OfType<Button>()
                .FirstOrDefault(b => AutomationProperties.GetName(b)
                    .StartsWith(sections[0].HeaderTitle, StringComparison.Ordinal));
            Check("分区标题按钮有可访问名称（标题 + 统计）",
                headerBtn != null && AutomationProperties.GetName(headerBtn).Contains("·"),
                headerBtn == null ? "not found" : AutomationProperties.GetName(headerBtn));
        }

        Console.WriteLine("== 5. 窄窗口：长中文 / 英文列不溢出 ==");
        foreach (var lang in new[] { AppLang.Zh, AppLang.En })
        {
            Loc.Lang = lang;
            var localized = BuildSections();
            sectionsControl.ItemsSource = localized;
            Layout(win, 860, 620);

            var cp = sectionsControl.ItemContainerGenerator.ContainerFromIndex(0) as ContentPresenter;
            if (cp == null)
            {
                Check($"[{lang}] 窄窗：分区容器已生成", false);
                continue;
            }
            var stats = FindByBinding(cp, "HeaderStats") as TextBlock;
            var title = FindByBinding(cp, "HeaderTitle") as TextBlock;
            string tag = lang == AppLang.En ? "EN" : "ZH";
            Check($"[{tag}] 860 宽：分区容器不超出可用宽度",
                cp.ActualWidth > 0 && cp.ActualWidth <= 860.5, cp.ActualWidth.ToString("0.#"));
            Check($"[{tag}] 860 宽：标题有实际宽度（没被右侧统计挤成 0）",
                title != null && title.ActualWidth > 10,
                title?.ActualWidth.ToString("0.#") ?? "not found");
            Check($"[{tag}] 860 宽：统计有实际宽度且被截断而不是溢出",
                stats != null && stats.ActualWidth > 0 && stats.ActualWidth <= 237,
                stats?.ActualWidth.ToString("0.#") ?? "not found");
            Check($"[{tag}] 统计声明了截断（TextTrimming=CharacterEllipsis）",
                stats != null && stats.TextTrimming == TextTrimming.CharacterEllipsis);
        }
        Loc.Lang = AppLang.Zh;

        Console.WriteLine("== 6. 不回归：行高合法值 / 最小行高 / 虚拟化 ==");
        var orgGrid = NamedField(win, "OrganizeGrid") as DataGrid;
        Check("OrganizeGrid.RowHeight 仍是 NaN（自动行高）",
            orgGrid != null && double.IsNaN(orgGrid.RowHeight),
            orgGrid?.RowHeight.ToString() ?? "null");
        Check("OrganizeGrid.MinRowHeight >= 46",
            orgGrid != null && orgGrid.MinRowHeight >= 46);
        Check("OrganizeGrid 虚拟化开启",
            orgGrid != null && orgGrid.EnableRowVirtualization
            && VirtualizingPanel.GetIsVirtualizing(orgGrid));

        var cleanGrid = NamedField(win, "CleanGrid") as DataGrid;
        Check("CleanGrid.RowHeight 是合法正数（不是 Auto 这类非法字面量）",
            cleanGrid != null && !double.IsNaN(cleanGrid.RowHeight) && cleanGrid.RowHeight > 0,
            cleanGrid?.RowHeight.ToString() ?? "null");
        Check("CleanGrid 行虚拟化 + 分组虚拟化都开着",
            cleanGrid != null && cleanGrid.EnableRowVirtualization
            && VirtualizingPanel.GetIsVirtualizing(cleanGrid)
            && cleanGrid.GetValue(VirtualizingPanel.IsVirtualizingWhenGroupingProperty) is true);

        Console.WriteLine("== 7. 独立截图（测试窗口 + 隔离配置 + 合成分区，不是真实扫描数据） ==");
        try
        {
            Loc.Lang = AppLang.Zh;
            sectionsControl.ItemsSource = BuildSections();
            Layout(win, 1000, 700);
            var target = win.Content as FrameworkElement;
            Check("窗口内容已布局", target != null && target.ActualWidth > 800);
            if (target != null)
            {
                int w = (int)Math.Ceiling(target.ActualWidth);
                int h = (int)Math.Ceiling(target.ActualHeight);
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(target);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                string outDir = Path.Combine(Path.GetTempPath(), "dashao-cleanup-presentation");
                Directory.CreateDirectory(outDir);
                string file = Path.Combine(outDir, $"cleanup-home-{w}x{h}.png");
                using (var fs = File.Create(file)) enc.Save(fs);
                var fi = new FileInfo(file);
                Check("渲染出非空 PNG", fi.Exists && fi.Length > 5000, file + " · " + fi.Length + " B");
            }
        }
        catch (Exception ex) { Check("渲染出非空 PNG", false, Describe(ex)); }

        return Summary();
    }

    /// <summary>真实的两级分区（合成候选，不读盘、不发请求）。</summary>
    static List<CleanPurposeSection> BuildSections()
    {
        var purposes = new List<CleanPurposeNode>
        {
            new CleanPurposeNode
            {
                Purpose = CleanPurpose.Temp, RiskTier = 0,
                PurposeName = Loc.PurposeTemp, Impact = Loc.ImpactTemp,
            },
            new CleanPurposeNode
            {
                Purpose = CleanPurpose.Large, RiskTier = 1,
                PurposeName = Loc.PurposeLarge, Impact = Loc.ImpactLarge,
            },
        };
        return CleanPurposeSection.Build(purposes);
    }

    /// <summary>给窗口一个真实尺寸并强制走一遍布局（不显示窗口，但走真实 Measure/Arrange）。</summary>
    static void Layout(MainWindow win, double w, double h)
    {
        win.Width = w;
        win.Height = h;
        win.Measure(new Size(w, h));
        win.Arrange(new Rect(0, 0, w, h));
        win.UpdateLayout();
        if (win.Content is FrameworkElement c)
        {
            c.Measure(new Size(w, h));
            c.Arrange(new Rect(0, 0, w, h));
            c.UpdateLayout();
        }
        win.UpdateLayout();
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    static FrameworkElement? FindName(DependencyObject root, string name)
    {
        if (root is FrameworkElement fe && fe.Name == name) return fe;
        foreach (var d in Descendants(root))
            if (d is FrameworkElement f && f.Name == name) return f;
        return null;
    }

    static FrameworkElement? FindByBinding(DependencyObject root, string path)
    {
        if (root is FrameworkElement fe
            && fe.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path == path)
            return fe;
        foreach (var d in Descendants(root))
        {
            if (d is FrameworkElement f
                && f.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path == path)
                return f;
        }
        return null;
    }

    static object? NamedField(object target, string name)
        => target.GetType().GetField(name,
               BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?.GetValue(target);

    static string Describe(Exception ex)
    {
        var e = ex;
        while (e.InnerException != null) e = e.InnerException;
        return e.GetType().Name + ": " + e.Message;
    }

    static int Summary()
    {
        Console.WriteLine();
        Console.WriteLine($"CleanupPresentationCheck: {_pass} PASS / {_fail} FAIL");
        foreach (var f in Failures) Console.WriteLine("  FAIL " + f);
        return _fail == 0 ? 0 : 1;
    }
}
