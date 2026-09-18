using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.CleanRules;
using UninstallTools;
using UninstallTools.Uninstaller;

namespace AiDiskCleaner;

public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>建议卸载和可以考虑默认展开，建议保留默认折叠。</summary>
public sealed class GroupExpandedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is 0 or 1;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// 详情列表的**展示分组表头**。
///
/// 共同说明只在这里出现一次，组内文件行不再重复它 —— 说明列因此可以整个去掉，
/// 把宽度让给文件名。
/// </summary>
public sealed record DetailGroupHeader(
    string Key, string Title, string Stat, string Note, string SelectedText)
{
    public bool HasNote => Note.Length > 0;
    public bool HasSelection => SelectedText.Length > 0;
    /// <summary>说明较长时才给「展开全文」，不给每个文件加按钮。</summary>
    public bool CanExpandNote => Note.Length > DetailGroupHeaderConverter.NoteClamp;
}

/// <summary>组表头：标题 / 数量·空间 / 共同说明 /（有选择时）已选数量。</summary>
public sealed class DetailGroupHeaderConverter : IValueConverter
{
    /// <summary>共同说明默认最多两行（约 46 个全角字）。</summary>
    public const int NoteClamp = 46;

    /// <summary>组统计按**完整过滤结果**算，不是当前已加载的那一页。</summary>
    public Dictionary<string, DetailGroupHeader> Stats { get; } =
        new(StringComparer.Ordinal);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CollectionViewGroup g) return null;
        string key = GroupKeyOf(g);
        if (Stats.TryGetValue(key, out var h)) return h;
        // 兜底：统计还没算到时至少给出标题，不显示空组头
        string title = g.Items.Count > 0 && g.Items[0] is CleanItem it ? it.DetailGroupTitle : "";
        return new DetailGroupHeader(key, title, "", "", "");
    }

    static string GroupKeyOf(CollectionViewGroup g)
    {
        foreach (var i in g.Items)
            if (i is CleanItem c) return c.DetailGroupKey;
        return g.Name?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// 分组展开状态：**按分组键记住**，所以搜索/刷新后仍能恢复用户之前的折叠状态。
/// 单个分组默认展开；多个分组默认只展开第一组。
/// </summary>
public sealed class DetailGroupExpandedConverter : IValueConverter
{
    public HashSet<string> Expanded { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Collapsed { get; } = new(StringComparer.Ordinal);
    /// <summary>搜索进行中：命中的组一律展开。</summary>
    public bool ExpandAll { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CollectionViewGroup g) return false;
        string key = KeyOf(g);
        if (Collapsed.Contains(key)) return false;
        if (Expanded.Contains(key) || ExpandAll) return true;
        return false;   // 「默认展开第一组」由代码写进 Expanded，见 MarkFirstGroupExpanded
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    static string KeyOf(CollectionViewGroup g)
    {
        foreach (var i in g.Items)
            if (i is CleanItem c) return c.DetailGroupKey;
        return g.Name?.ToString() ?? "";
    }

    /// <summary>把「第一组」标记为展开（单组时它就是第一组）。由 BindDetailPage 调用。</summary>
    public void MarkFirstGroupExpanded(string? firstKey)
    {
        if (!string.IsNullOrEmpty(firstKey) && !Collapsed.Contains(firstKey)) Expanded.Add(firstKey);
    }
}


public sealed class AppGroupConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int key = 0, n = 0;
        if (value is CollectionViewGroup g)
        {
            key = g.Name is int i ? i : 0;
            n = g.ItemCount;
        }
        else if (value is int i) key = i;
        return key switch
        {
            0 => Loc.UninstallGroupRecommend(n),
            1 => Loc.UninstallGroupConsider(n),
            2 => Loc.UninstallGroupKeep(n),
            _ => Loc.UninstallGroupNeutral(n),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>空字符串 → Collapsed。用于「有内容才显示」的分隔符/提示。</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// 资源键 → 图标 <see cref="Geometry"/>。
///
/// 图标只有一套（XAML 里的 `IconChevronDown` 等），代码里只说「用哪个键」，
/// 不复制第二套矢量图形。先查应用资源，再查窗口资源（图标定义在窗口里）。
/// </summary>
public sealed class IconKeyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value as string ?? "";
        if (key.Length == 0) return null;
        if (Application.Current?.TryFindResource(key) is Geometry app) return app;
        return Application.Current?.MainWindow?.TryFindResource(key) as Geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public partial class MainWindow : Window, IAnalystHost
{
    private const int MaxDisplayRows = 50000; // 文件列表最多渲染的行数，超出只显示前 N 行

    private readonly IScanService _scanner = new MftScanService();
    private readonly IScanService _fallback = new RecursiveScanService();
    // 协调模块：窗口只管 UI 绑定和事件转发，流程都在这些类里（可独立测试）
    private readonly ScanCoordinator _scanCoordinator;
    private readonly DeletionCoordinator _deleteCoordinator = new();
    private readonly AiCoordinator _aiCoordinator = new();
    private FileEntry _root = null!;
    private List<FileEntry> _allFiles = new(); // 缓存：根目录下所有文件（避免重复递归收集）
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _analyzeCts;
    private CancellationTokenSource? _dupCts;
    private CancellationTokenSource? _snapshotCts;
    private CancellationTokenSource? _uninstallCts;
    private CancellationTokenSource? _aiConfigCts;
    private CancellationTokenSource? _aiClassifyCts;
    /// <summary>扫描代次。旧任务跑完时如果代次已经不是自己那一代，结果直接丢弃，不覆盖新结果。</summary>
    private int _scanGeneration;
    private int _analyzeGeneration;
    private int _dupGeneration;
    private int _uninstallGeneration;
    /// <summary>
    /// 可取消阶段登记簿。停止按钮的可见性由它决定 ——
    /// 只要还有阶段在跑（扫描/分析/重复检测/收尾），停止入口就不能消失。
    /// </summary>
    private readonly WorkState _work = new();
    /// <summary>分层结果（用途 → 位置 → 明细）取代了旧的平铺快照。</summary>
    /// <summary>上一次绑定后实际生成的行容器数（验证虚拟化有没有生效）。</summary>
    private int _lastRowContainerCount;
    /// <summary>扩展名统计的代次与缓存：切目录时旧结果不能覆盖新结果。</summary>
    /// <summary>上一次扫描的质量报告（来源/完整度/跳过统计），界面据此显示完整度。</summary>
    private ScanQuality? _scanQuality;
    private bool _scanning;
    private bool _scanUsedFallback;
    private string _scanFallbackReason = "";
    private DateTime _scanStart;
    private CleanReport? _report;
    private Action? _confirmYes;
    private List<AppUninstallItem> _apps = new();
    /// <summary>卸载页有没有被打开过。没打开过就不去扫软件清单。</summary>
    private bool _uninstallTabVisited;
    private bool _listingApps;
    private BulkUninstallTask? _uninstallTask;
    private bool _aiAppsBusy;
    private CancellationTokenSource? _aiAppsStop;
    private string _uninstallResultNote = "";
    private BulkUninstallTask? _handledUninstallTask;
    private int _appInventoryVersion;
    private List<JunkItem> _junk = new();
    private bool _showingJunk;
    private long _volumeTotal;
    private long _volumeUsed;
    private bool _aiModelLock;
    private bool _aiBusy;
    private bool _aiOk;

    FileEntry? IAnalystHost.Root => _root;
    CleanReport? IAnalystHost.Report => _report;
    private static readonly AiProtocol[] AiProtos =
        { AiProtocol.Completions, AiProtocol.Responses, AiProtocol.Anthropic, AiProtocol.Decisions };

    public MainWindow()
    {
        InitializeComponent();
        _scanCoordinator = new ScanCoordinator(_scanner, _fallback);
        // 启动时**只建立一次**系统 KnownFolder 快照（重定向下载/桌面/图片等），之后只查内存；
        // 已安装清单在卸载页清点软件后（ListApps 结果处）再叠到**同一个**证据服务上。
        _systemPathSnapshot = SystemPathSnapshot.Capture();
        _localEvidence = new LocalEvidenceService(InstalledLocationSnapshot.Empty, _systemPathSnapshot);
        _folderPurpose = new FolderPurposeService(_localEvidence);
        // 跨重启识别缓存：必须在第一次 RebuildOrganize / LocalRecognize 之前接上，
        // 这样整理树第一次本地识别就能命中落盘结论，不再发请求。
        InitRecognitionPersistence();
        // 停止按钮的可见性由阶段登记簿驱动，不再由某个流程的 finally 决定
        _work.Changed += Work_Changed;
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
        DriveBox.ItemsSource = drives;
        if (drives.Count > 0) DriveBox.SelectedIndex = 0;
        UpdateVolumeInfo();
        DriveBox.SelectionChanged += (_, _) => UpdateVolumeInfo();
        StateChanged += (_, _) =>
        {
            MaxButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            BorderThickness = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(1);
        };
        // 窗口尺寸变化时重算文件明细的停靠/整页模式（2.11 起已没有侧栏布局要重算）。
        SizeChanged += (_, _) => UpdateDetailLayout();
        BorderBrush = ThemeService.Brush("Border");
        BorderThickness = new Thickness(1);
        // 把代码里的实例接到 XAML：组头统计与折叠状态都在这两个对象上
        Resources["DetailGroupHeader"] = _detailGroups;
        Resources["DetailGroupExpanded"] = _detailGroupsExpanded;
        ApplyUi();
        // 默认进「清理中心」：主工作区是清理，文件夹整理退到辅助入口
        ShowRightTab(RightTab.Clean);
        // 用户之前手动纠正过的用途：跨重启仍然有效，且优先于本地/AI 结论
        try { _folderPurpose.LoadCorrections(); } catch (Exception ex) { AppLog.Record("Purpose", ex, "load corrections"); }
        RebuildOrganize();
        // sidecar 的工具回调落到这里（this 实现了 IAnalystHost），
        // 勾选/删除等动作仍在 C# 侧执行。
        try { SidecarClient.AttachToolHost(this); }
        catch (Exception ex)
        {
            // 挂不上工具回调只影响 sidecar 的工具循环，内置 HTTP 通道照常用。
            AppLog.Record("Ai", ex, "attach tool host");
        }
        PickDrive("C:\\");
        // 构造彻底完成：所有 x:Name 控件都已赋值。
        // 崩溃处理器靠这个标记区分「窗口已就绪」和「半初始化」——
        // 半初始化时绝不能去碰 AlertText 这类还没赋值的控件（那会把崩溃变成死循环）。
        IsUiReady = true;
    }

    /// <summary>
    /// 主窗口是否已经完成构造（`InitializeComponent` 成功、所有命名控件都已赋值）。
    /// 构造函数中途抛异常时它会保持 false。
    /// </summary>
    public bool IsUiReady { get; private set; }


    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_scanning && _root == null)
            RunScan();
    }

    void PickDrive(string name)
    {
        if (DriveBox.ItemsSource is not IEnumerable<string> drives) return;
        var hit = drives.FirstOrDefault(d => d.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        if (hit != null) DriveBox.SelectedItem = hit;
    }

    public void ApplyUi()
    {
        Title = Loc.AppName;
        TitleText.Text = Loc.AppName;
        ScanButton.Content = Loc.Scan;
        StopButton.Content = Loc.Stop;
        // SettingsButton 是齿轮**图标**按钮：这里只更新语义，绝不设 Content。
        // 一旦设 Content，WPF 会用文字顶掉 XAML 里的 <Path> 图标 —— 实测过。
        SettingsButton ??= null;
        if (SettingsButton != null)
        {
            SettingsButton.ToolTip = Loc.SettingsTitle;
            System.Windows.Automation.AutomationProperties.SetName(SettingsButton, Loc.OpenSettings);
        }
        if (AboutLinkBtn != null) AboutLinkBtn.Content = Loc.AboutDashaoHuo;
        if (HeaderStats.Text is "就绪" or "Ready") HeaderStats.Text = Loc.Ready;
        if (CleanOpenItem != null) CleanOpenItem.Header = Loc.OpenInExplorer;
        // 主导航：按文件夹删除 / 清理中心 / 卸载
        TabOrganizeBtn.Content = Loc.TabOrganize;
        TabCleanBtn.Content = Loc.TabClean;
        TabUninstallBtn.Content = Loc.TabUninstall;
        ClearScopeBtn.Content = Loc.ClearScopeAction;
        UninstallRunBtn.Content = Loc.UninstallRun;
        UninstallRetryItem.Header = Loc.UninstallRetry;
        UninstallOpenOfficialItem.Header = Loc.UninstallOpenOfficial;
        JunkSafeBtn.Content = Loc.JunkSafe;
        JunkDeleteBtn.Content = Loc.JunkDelete;
        ColAppName.Header = Loc.ColName;
        // 发布者 / 版本 / 状态三列已从可见表格里移除（Collapsed）。这里逐个判空：
        // 将来真的把列元素删掉时，ApplyUi 不会因为少了某个 x:Name 字段而 NRE。
        if (ColAppPub != null) ColAppPub.Header = Loc.Publisher;
        if (ColAppVersion != null) ColAppVersion.Header = Loc.ColVersion;
        ColAppInstallDate.Header = Loc.ColInstallDate;
        ColAppPurpose.Header = Loc.AppPurposeHeader;
        ColAppSize.Header = Loc.AppSizeColumnHeader;
        if (ColAppStatus != null) ColAppStatus.Header = Loc.Status;
        ColAppAction.Header = Loc.UninstallRowActions;
        if (UninstallSortNote != null) UninstallSortNote.Text = Loc.UninstallSortNote;
        ColJunkApp.Header = Loc.ColName;
        ColJunkKind.Header = Loc.ColCategory;
        ColJunkConf.Header = Loc.ColConfidence;
        ColJunkPath.Header = Loc.Path;
        RefreshUninstallPaneText();
        if (ColPick.Header is CheckBox pickAll)
        {
            pickAll.ToolTip = Loc.SelectAllTip;
            _pickAllBox = pickAll;
        }
        ColCleanType.Header = Loc.ColType;
        ColCleanName.Header = Loc.ColName;
        ColCleanSize.Header = Loc.Size;

        // 分层视图：页头 / 底部操作栏 / 明细面板的文案
        // 这两个现在是**图标**按钮：只设 ToolTip 与可访问名称，不设 Content（否则图标被文字顶掉）。
        if (ScanDetailsBtn != null)
        {
            ScanDetailsBtn.ToolTip = Loc.ScanDetails;
            System.Windows.Automation.AutomationProperties.SetName(ScanDetailsBtn, "查看扫描详情");
        }
        if (CleanMoreBtn != null)
        {
            CleanMoreBtn.ToolTip = Loc.MoreMenu;
            System.Windows.Automation.AutomationProperties.SetName(CleanMoreBtn, "更多操作");
        }
        // 底部「全选」是单个切换按钮：文字恒为 Loc.SelectAll，状态（幽灵 / 主按钮、
        // 提示、可访问名称、可用性）统一由 ApplySelectAllButton 维护，ApplyUi 之后
        // 会走 RefreshCleanUi → UpdateSelectionUi 把当前状态刷新一次。
        if (CleanSelectAllBtn != null)
        {
            CleanSelectAllBtn.Content = Loc.SelectAll;
            System.Windows.Automation.AutomationProperties.SetName(CleanSelectAllBtn, Loc.SelectAll);
        }
        if (CheckAndCleanBtn != null) CheckAndCleanBtn.Content = Loc.CleanSelectedItems;
        // ViewSelectedBtn 是「清单图标 + 数量」，文字与数量由 UpdateSelectionUi 统一维护，
        // 这里**不能**再覆盖它的 Content —— 那会把图标换成纯文字。
        if (ViewSelectedText != null) ViewSelectedText.Text = Loc.ViewSelected;
        // SettingsButton 是齿轮图标按钮：只设语义，**绝不设 Content**，否则图标会被文字顶掉。
        UpdateViewSelectedLabel();
        if (DetailCloseBtn != null) DetailCloseBtn.Content = Loc.CloseDetail;
        if (DetailMoreBtn != null) DetailMoreBtn.Content = Loc.LoadMore;
        if (DetailSearchBox != null) DetailSearchBox.Tag = Loc.SearchInScope;
        UpdateDetailPathButtons();
        UpdateScanStateLine();

        ApplyOrganizeUi();
        RefreshCleanUi();
        DialogClose.Content = Loc.Close;
        ConfirmYesBtn.Content = Loc.Yes;
        ConfirmNoBtn.Content = Loc.No;
        LangLabel.Text = Loc.Language;
        if (DiagExportBtn != null) DiagExportBtn.Content = Loc.DiagExport;
        if (DiagHint != null) DiagHint.Text = Loc.DiagHint;
        LangZhBtn.Content = Loc.LangZh;
        LangEnBtn.Content = Loc.LangEn;
        AiSectionLabel.Text = Loc.AiSection;
        AiSectionHint.Text = Loc.AiSectionHint;
        if (AiFullPathBox != null)
        {
            AiFullPathBox.Content = Loc.AiSendFullPaths;
            AiFullPathBox.IsChecked = App.Settings.AiSendFullPaths;
        }
        if (AiFullPathHint != null) AiFullPathHint.Text = Loc.AiSendFullPathsHint;
        AiNameLabel.Text = Loc.AiName;
        AiUrlLabel.Text = Loc.AiBaseUrl;
        AiProtoLabel.Text = Loc.AiProtocolTitle;
        AiModelLabel.Text = Loc.AiModel;
        AiKeyLabel.Text = Loc.AiApiKey;
        AiTestBtn.Content = Loc.AiTest;
        AiFetchBtn.Content = Loc.AiFetchModels;
        AiNameBox.Tag = Loc.AiNameHint;
        AiUrlBox.Tag = Loc.AiUrlHint;
        AiModelBox.Tag = Loc.AiModelHintBox;
        AiModelHint.Text = Loc.AiModelsEmpty;
        AiAddProvBtn.Content = Loc.AiAddCustom;
        if (AiPickModelLabel != null) AiPickModelLabel.Text = Loc.AiPickModel;
        FillAiProtoBox();
        FillRunModels();
        RefreshAiLamp();   // 顶栏胶囊的模型名 / 可访问名称随语言与配置一起刷新
        AboutText.Text = Loc.AboutBody;
        RepoLink.Text = Loc.Repo;
        if (_root == null)
        {
            CleanSummarySub.Text = Loc.AnalyzeAfterScan;
            ScanProgressText.Text = Loc.ScanningEllipsis;
        }
        BorderBrush = ThemeService.Brush("Border");
        HighlightThemeButtons();
        UpdateVolumeInfo();
    }

    private void HighlightThemeButtons()
    {
        void Mark(Button b, bool on)
        {
            b.BorderBrush = ThemeService.Brush(on ? "Accent" : "Border");
            b.Foreground = ThemeService.Brush(on ? "Accent" : "TextDim");
        }
        Mark(LangZhBtn, Loc.Lang == AppLang.Zh);
        Mark(LangEnBtn, Loc.Lang == AppLang.En);
    }

    /// <summary>
    /// 一次可取消操作的作用域：自己的 CancellationTokenSource + 一条结构化日志。
    /// 调用方用 <c>using</c> 接住，作用域结束只收尾日志；CTS 的取消/替换由
    /// <see cref="StartOperation"/> 负责，避免把别人正在用的 token 释放掉。
    /// </summary>
    private sealed class OperationScope : IDisposable
    {
        private readonly LogOperation _log;

        public OperationScope(LogOperation log, CancellationToken token)
        {
            _log = log;
            Token = token;
        }

        public CancellationToken Token { get; }
        public void Stage(string stage, string? message = null, int? count = null) => _log.Stage(stage, message, count);
        public void Note(string message) => _log.Note(message);
        public void Done(string? message = null, int? count = null) => _log.Done(message, count);
        public void Canceled(string? message = null) => _log.Canceled(message);
        public void Fail(Exception ex, string? stage = null) => _log.Fail(ex, stage);
        public void Dispose() => _log.Dispose();
    }

    /// <summary>
    /// 开一代新操作：取消上一代（不 Dispose —— 在飞的任务可能还持有它的 token），
    /// 把新的 CancellationTokenSource 放进 slot。
    /// </summary>
    private static OperationScope StartOperation(ref CancellationTokenSource? slot, string module)
    {
        var old = slot;
        var fresh = new CancellationTokenSource();
        slot = fresh;
        if (old != null)
        {
            // 上一代可能已经结束并释放过，取消抛 ObjectDisposedException 属正常竞态。
            try { old.Cancel(); } catch (ObjectDisposedException) { }
        }
        return new OperationScope(AppLog.Begin(module), fresh.Token);
    }

    /// <summary>
    /// 作废上一轮所有后续阶段：取消它们的 CTS 并把代次往前推。
    /// 新扫描开始时必须调 —— 否则上一轮的重复检测/快照还在跑，
    /// 完成时会往已经清空的 _report 上写，或者把旧结果盖到新界面上。
    /// </summary>
    private void InvalidateFollowUpStages()
    {
        CancelQuietly(_dupCts);
        CancelQuietly(_snapshotCts);
        CancelQuietly(_analyzeCts);
        _dupGeneration++;
        _analyzeGeneration++;
    }

    private static void CancelQuietly(CancellationTokenSource? cts)
    {
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { /* 已经结束并释放过，正常竞态 */ }
    }

    /// <summary>
    /// 第三层状态：操作进度 / 结果短句。**不写顶部**（顶部只留工具栏），
    /// 卸载页借它自己的摘要显示，互不串台。
    /// </summary>
    private void SetStatus(string text)
    {
        _workStatus = text ?? "";
        ApplyStatusToPage();
    }

    /// <summary>当前操作状态短句（切页时用来决定各页显示什么）。</summary>
    private string _workStatus = "";

    /// <summary>把当前操作状态同步到正在显示的页面上。</summary>
    private void ApplyStatusToPage()
    {
        if (HeaderStats == null) return;
        // 文件夹整理页也有自己的进度与取消，顶栏同样要能看到状态
        HeaderStats.Text = _rightTab is RightTab.Clean or RightTab.Organize ? _workStatus : "";
        HeaderStats.Visibility = HeaderStats.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>用户点「停止」：所有进行中的阶段一起取消。取消不是异常，不当故障报。</summary>
    private void StopEverything()
    {
        CancelQuietly(_scanCts);
        CancelQuietly(_analyzeCts);
        CancelQuietly(_dupCts);
        CancelQuietly(_snapshotCts);
        CancelQuietly(_uninstallCts);
        CancelQuietly(_aiStop);
        CancelQuietly(_aiAppsStop);
        CancelQuietly(_aiConfigCts);
        // 整理页已无批量/自动识别任务：模型请求只由用户对单项发起，走逐项取消按钮
        // 标记「正在取消」：按钮立刻变成不可再点，并给出反馈，不等任务真正退出。
        _work.RequestCancel();
        UpdateStopButton();
        SetStatus(_work.Busy ? Loc.Canceling : Loc.Aborted);
        AppLog.Info("UI", "user requested stop for all running operations");
    }

    /// <summary>
    /// 停止按钮的可见性只看登记簿：**只要还有可取消阶段在跑，停止入口就必须在**。
    /// 这是以前那个 bug 的根因 —— 扫描的 finally 无条件藏了按钮，而分析还在后台跑。
    /// </summary>
    private void UpdateStopButton()
    {
        if (StopButton == null) return;
        bool canStop = _work.Busy;
        StopButton.Visibility = canStop ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = _work.CanStop;
        StopButton.Content = _work.CancelRequested && canStop ? Loc.Canceling : Loc.Stop;
        ScanButton.IsEnabled = !_scanning;
    }

    private void Work_Changed(object? sender, EventArgs e)
    {
        // 事件可能来自后台线程的阶段收尾，切回 UI 线程再动控件。
        if (Dispatcher.CheckAccess()) UpdateStopButton();
        else Dispatcher.BeginInvoke(UpdateStopButton);
    }

    private async void RunScan()
    {
        if (_scanning || DriveBox.SelectedItem == null) return;
        _scanning = true;
        // 先把上一轮的后续阶段全部作废，再开始这一轮
        InvalidateFollowUpStages();
        _work.ResetCancel();
        _scanUsedFallback = false;
        _scanFallbackReason = "";
        _scanQuality = null;
        // 开新扫描就把上一轮的清理结果和分层丢掉：
        // 否则「旧列表还挂在界面上」，用户可能照着过期清单去勾选删除。
        _report = null;
        _dupIncomplete = 0;
        _layerGeneration++;
        ClearCleanLayers();
        CleanPageTitle.Text = Loc.PagePurposeTitle;
        CleanPageSub.Text = "";
        CleanSummarySub.Text = "";
        ShowCleanState(CleanStateKind.Scanning);
        UpdateSelectionUi();
        ScanButton.IsEnabled = false;
        _scanStart = DateTime.Now;
        // 每次扫描一个独立的 CTS；上一代扫描/分析会被取消，且代次守卫保证旧结果不覆盖新结果。
        using var scanOp = StartOperation(ref _scanCts, "Scan");
        var ct = scanOp.Token;
        int myGeneration = ++_scanGeneration;
        // 登记「扫盘」阶段：只要它还在，停止按钮就在
        using var scanStage = _work.Begin(StageNames.Scan, WorkPhase.Scanning);
        UpdateStopButton();
        SetStatus(Loc.Scanning);
        ScanProgressPanel.Visibility = Visibility.Visible;
        ScanProgressBar.IsIndeterminate = true;
        ScanProgressBar.Value = 0;
        ScanProgressText.Text = Loc.Preparing;
        // 不在这里切页：扫描开始/结束/重建都不许把用户从自己选的页面拽走
        ShowOrganizeState(OrganizeStateKind.Scanning);
        SetCleanProgress(0, Loc.CleanScan, determinate: false);
        // 2.11：目录侧栏已移除，扫描进度不再往树上铺占位行。

        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.Percent >= 0)
            {
                ScanProgressBar.IsIndeterminate = false;
                ScanProgressBar.Value = p.Percent;
                string stage = _scanUsedFallback
                    ? Loc.ScanFallbackProgress(p.Percent, p.CurrentDirectory, p.FileCount, _scanFallbackReason)
                    : Loc.ProgressLine(p.Percent, p.CurrentDirectory, p.FileCount);
                ScanProgressText.Text = stage;
                SetStatus(_scanUsedFallback
                    ? Loc.ScanFallbackProgress(p.Percent, p.CurrentDirectory, p.FileCount, _scanFallbackReason)
                    : Loc.ScanPct(p.Percent));
                SetCleanProgress(p.Percent, stage, determinate: true);
            }
            else
            {
                ScanProgressBar.IsIndeterminate = true;
                string stage = _scanUsedFallback
                    ? Loc.ScanFallbackProgress(-1, p.CurrentDirectory, p.FileCount, _scanFallbackReason)
                    : Loc.ProgressIndeterminate(p.CurrentDirectory, p.FileCount);
                ScanProgressText.Text = stage;
                SetStatus(_scanUsedFallback ? stage : Loc.ScanCount(p.FileCount));
                SetCleanProgress(0, stage, determinate: false);
            }
        });

        try
        {
            string drive = DriveBox.SelectedItem.ToString()!;
            // onFallback 让「MFT 不可用，正在用兼容扫描」立刻显示，而不是等扫完
            var outcome = await _scanCoordinator.RunAsync(drive, progress, ct, reason =>
            {
                _scanUsedFallback = true;
                _scanFallbackReason = reason;
                SetStatus(Loc.MftFallbackStarting(reason));
                ScanProgressText.Text = Loc.MftFallbackStarting(reason);
            });
            _scanUsedFallback = outcome.UsedFallback;
            _scanFallbackReason = outcome.FallbackReason;
            _scanQuality = outcome.Quality;
            if (myGeneration != _scanGeneration)
            {
                // 期间用户又发起了新扫描：这份结果已经过期，丢掉，绝不覆盖新结果。
                AppLog.Info("Scan", $"dropping stale scan result gen={myGeneration} current={_scanGeneration}");
                return;
            }
            // 不 await：首屏不能等后续流水线。阶段由 WorkState 登记，停止按钮照常可用。
            _ = FinishScanAsync(outcome.Root);
            scanOp.Done(outcome.UsedFallback ? "recursive" : "mft", outcome.Root.FileCount);
        }
        catch (OperationCanceledException)
        {
            // 取消是正常路径，不是故障：只清理状态。
            HideCleanProgress();
            SetStatus(Loc.Aborted);
            // 整理页也退回可重试的状态，不留一个假的「正在扫描」
            ShowOrganizeState(OrganizeStateKind.NoScan);
            scanOp.Canceled("scan canceled by user");
        }
        catch (Exception ex)
        {
            HideCleanProgress();
            string userMsg = AppLog.Record("Scan", ex, "scan failed");
            ShowAlert(Loc.ScanFailed, Loc.ScanFailedMsg(userMsg));
            SetStatus(Loc.ScanFailed);
            ShowOrganizeState(OrganizeStateKind.Failed);
            scanOp.Fail(ex, "scan");
        }
        finally
        {
            _scanning = false;
            // 注意：这里**不再**无条件藏停止按钮。
            // 扫描阶段自己会通过 using 注销；如果后续的分析/重复检测阶段还在跑，
            // 按钮必须继续留着 —— 以前就是在这里被藏掉，导致「分析还在跑却没有停止入口」。
            UpdateStopButton();
            ScanProgressPanel.Visibility = Visibility.Collapsed;
            ScanProgressBar.IsIndeterminate = false;
        }
    }

    /// <summary>
    /// 扫完之后的流水线，全程不阻塞界面：
    /// 1) 后台收集全盘文件清单；
    /// 2) 清理规则分析（不含重复检测）→ 立刻出列表；
    /// 3) 快照保存放到后台，不挡首屏；
    /// 4) 重复检测作为独立可取消阶段后跑，跑完再并进列表。
    /// </summary>
    private async Task FinishScanAsync(FileEntry root)
    {
        var perf = new PerfTrace("finish-scan");
        // 让出一拍再干重活：进度条 / 阶段切换先画出来，避免看起来像卡死
        await Task.Yield();
        _root = root;
        // 新一次扫描 = 新的占用口径，软件占用缓存必须丢
        AppRecommendationService.InvalidateUsageCache();

        UpdateVolumeInfo();
        // 2.11：目录树已移除，扫描完不再需要重建树/切换浏览目录。

        // 扫描耗时不再单独占一条底栏：进「扫描详情」。
        _scanElapsed = (DateTime.Now - _scanStart).TotalSeconds;
        ShowScanQuality();
        // 扫描完成后不再往顶部堆「MFT 扫描完成 · 1,585,218 个文件」这类长句：
        // 覆盖完整度由第三层的 CleanScanState 表达，明细进「扫描详情」。
        SetStatus(_scanUsedFallback ? Loc.ScanDoneFallback : Loc.ScanDone);
        perf.Flush();

        // 后续阶段一律不阻塞首屏
        _ = RunCleanPipelineAsync(root);
    }

    /// <summary>
    /// 扫描质量只在页头留一句短状态；完整细节进「扫描详情」对话框。
    /// **不完整绝不伪装成成功** —— 这里只决定那句话怎么说，诊断数据一条不删。
    /// </summary>
    private void ShowScanQuality()
    {
        var q = _scanQuality;
        if (q == null) return;
        UpdateScanStateLine();
        UpdateCandidateSummary();
        ShowCleanState(q.Canceled ? CleanStateKind.Canceled
            : q.Complete ? CleanStateKind.None
            : CleanStateKind.Partial);

        AppLog.Info("Scan", $"quality source={q.Source} complete={q.Complete} files={q.FilesRead} dirs={q.DirsRead} "
            + $"skipped={q.SkippedDirs} perm={q.PermissionErrors} path={q.PathErrors} read={q.ReadErrors} "
            + $"reparse={q.ReparsePoints} unparsed={q.UnparsedRecords} orphan={q.OrphanRecords} ms={q.DurationMs}");
    }

    /// <summary>
    /// 扫完后的清理流水线。三个独立可取消的阶段，任何一个还在跑，停止按钮就可用。
    ///
    /// 顺序是刻意的：**普通清理结果先显示**，重复检测（要读文件内容，最慢）后跑，
    /// 跑完再并进列表。用户不用等哈希就能看到东西，而且能单独把重复检测停掉。
    /// </summary>
    private async Task RunCleanPipelineAsync(FileEntry root)
    {
        var perf = new PerfTrace("clean-pipeline");

        // ---- 阶段 1：收文件清单 + 清理规则分析（不含重复检测）----
        ShowCleanState(CleanStateKind.Analyzing);
        CleanReport? report = await RunAnalyzeStageAsync(root, perf);
        if (report == null) return;

        // ---- 首屏：列表先出来 ----
        CleanSummarySub.Text = Loc.Analyzing;
        SetCleanProgress(0, Loc.Analyzing, determinate: false);
        await RebuildLayersAsync(root, perf, "first-paint");
        HideCleanProgress();

        UpdateCandidateSummary();
        ShowCleanState(_layered.TotalFiles == 0 ? CleanStateKind.Empty : CleanStateKind.None);
        RefreshAiLamp();

        // ---- 后台存快照：不挡首屏 ----
        _ = SaveSnapshotAsync(root);

        // ---- 隐藏面板按需加载：卸载页没打开过就先别扫软件清单 ----
        MaybeLoadAppsInBackground();

        // ---- 阶段 2：重复检测（独立可取消）----
        await RunDuplicateStageAsync(root, perf);

        perf.Flush();
    }

    /// <summary>阶段 1：收集文件清单 + 跑清理规则。返回 null 表示被取消或失败。</summary>
    private async Task<CleanReport?> RunAnalyzeStageAsync(FileEntry root, PerfTrace perf)
    {
        // 清理规则分析有自己的 CTS：用户点停止时它跟扫描一起停，而且旧分析不能覆盖新分析。
        using var analyzeOp = StartOperation(ref _analyzeCts, "Analyze");
        var ct = analyzeOp.Token;
        int myGeneration = ++_analyzeGeneration;
        using var stage = _work.Begin(StageNames.Analyze, WorkPhase.Analyzing);
        UpdateStopButton();

        string drive = root.FullPath;

        var analyzeProgress = new Progress<ScanProgress>(p =>
        {
            // 旧任务的进度回调不能动新任务的进度条
            if (myGeneration != _analyzeGeneration) return;
            int pct = p.Percent >= 0 ? p.Percent : 0;
            int step = pct <= 5 ? 1 : pct <= 20 ? 2 : pct <= 45 ? 3 : pct <= 70 ? 4 : pct < 100 ? 5 : 6;
            SetCleanProgress(pct, Loc.AnalyzeStep(step, Loc.AnalyzeSteps, p.CurrentDirectory), determinate: p.Percent >= 0);
        });

        try
        {
            // 快照读取 + 文件清单收集 + 规则分析，全部在后台
            var (files, previous, report) = await Task.Run(() =>
            {
                var prev = ScanSnapshot.Load(drive);
                var all = CollectFiles(root);
                var rep = CleanAnalyzer.Analyze(root, prev, ct, analyzeProgress, includeDuplicates: false);
                return (all, prev, rep);
            }, ct);

            if (!ReferenceEquals(_root, root) || myGeneration != _analyzeGeneration) return null;
            _allFiles = files;
            _report = report;
            analyzeOp.Done($"cleanable={report.Cleanable.Count}", report.Cleanable.Count);
            return report;
        }
        catch (OperationCanceledException)
        {
            analyzeOp.Canceled("clean analysis canceled by user");
            if (myGeneration == _analyzeGeneration) HideCleanProgress();
            return null;
        }
        catch (Exception ex)
        {
            analyzeOp.Fail(ex, "analyze");
            if (myGeneration == _analyzeGeneration)
            {
                HideCleanProgress();
                CleanSummarySub.Text = Loc.HintClean;
            }
            return null;
        }
    }

    /// <summary>
    /// 阶段 2：重复检测。独立 CTS + 独立代次，跑完把结果并进报告并重建快照。
    /// 「检测没跑完」会如实标出来，不是假装全部完成。
    /// </summary>
    private async Task RunDuplicateStageAsync(FileEntry root, PerfTrace perf)
    {
        if (_report == null || _allFiles.Count == 0) return;

        using var dupOp = StartOperation(ref _dupCts, "Duplicates");
        var ct = dupOp.Token;
        int myGeneration = ++_dupGeneration;
        using var stage = _work.Begin(StageNames.Duplicates, WorkPhase.Duplicates);
        UpdateStopButton();

        var service = new DuplicateScanService();
        var progress = new Progress<DuplicateProgress>(p =>
        {
            if (myGeneration != _dupGeneration) return;
            CleanSummarySub.Text = Loc.DupStage(p.Phase, p.FilesProcessed, p.TotalCandidates,
                FileEntry.FormatSize(p.BytesRead));
        });

        try
        {
            var files = _allFiles;
            var outcome = await Task.Run(() => service.Run(files, ct, progress), ct);
            // 代次/根节点任一变化（新扫描、又一轮重复检测）就整批丢弃
            if (!ReferenceEquals(_root, root) || myGeneration != _dupGeneration) return;
            var report = _report;
            if (report == null) return; // 新扫描已经把报告清空了

            // 并入报告，然后重建分层（后台）再绑定
            report.Duplicates.Clear();
            report.Duplicates.AddRange(outcome.Items);
            report.DupGroupCount = outcome.Result.Groups.Count;
            // 检测没跑完就不能在摘要里说「检测已完成」；已算出的部分照常显示
            _dupIncomplete = outcome.Result.Complete ? 0 : Math.Max(1, outcome.Items.Count);
            if (_dupIncomplete > 0)
                AppLog.Warn("Duplicates", "duplicate detection incomplete: " + outcome.Result.Note);

            perf.Mark("duplicates", outcome.Elapsed.TotalMilliseconds, outcome.Result.Groups.Count);
            await RebuildLayersAsync(root, perf, "after-duplicates");

            int groupCount = outcome.Result.Groups.Count;
            var stats = outcome.Result.Stats;
            dupOp.Done($"{stats} complete={outcome.Result.Complete}", groupCount);

            UpdateCleanHintAfterDuplicates();
            if (!outcome.Result.Complete)
                AppLog.Warn("Duplicates", "duplicate detection did not complete: " + outcome.Result.Note);
        }
        catch (OperationCanceledException)
        {
            // 取消：已经算出来的部分照常显示，并明确标注未完成
            dupOp.Canceled("duplicate detection canceled by user");
            _work.RequestCancel();
            _dupIncomplete = 1; // 没跑完 ⇒ 摘要必须标「检测未跑完」
            if (myGeneration == _dupGeneration)
            {
                RefreshCleanUi();
                CleanSummarySub.Text = Loc.CanceledPartial;
            }
        }
        catch (Exception ex)
        {
            dupOp.Fail(ex, "duplicates");
        }
    }

    /// <summary>
    /// 重复检测跑完。**不再往主页面写「找到 N 个重复项」** ——
    /// 重复组数已经进了「扫描详情」（§五.4/§五.5），主页面只留重要状态。
    /// 这里只把一次性提示收回，并同步 AI 面板（重复检测完成可能改变可分析范围）。
    /// </summary>
    private void UpdateCleanHintAfterDuplicates()
    {
        _aiTransientStatus = "";
        UpdateCandidateSummary();
    }

    /// <summary>
    /// 后台存快照。用临时文件替换，避免中断留下半截损坏的 json；
    /// 并且用任务代次约束，旧扫描不会覆盖新扫描的快照。
    /// </summary>
    private async Task SaveSnapshotAsync(FileEntry root)
    {
        using var op = StartOperation(ref _snapshotCts, "Snapshot");
        var ct = op.Token;
        int myGeneration = _scanGeneration;
        using var stage = _work.Begin(StageNames.Snapshot, WorkPhase.Finishing);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var snap = ScanSnapshot.Capture(root);
                ct.ThrowIfCancellationRequested();
                // 采完快照、写盘之前再确认一次代次：期间如果有更新的扫描开始了，
                // 它的快照才是对的，别让这次旧数据把它盖掉。
                if (myGeneration != _scanGeneration) return;
                snap.Save(ct);
            }, ct);
            if (myGeneration != _scanGeneration) return;
            op.Done("snapshot saved");
        }
        catch (OperationCanceledException)
        {
            op.Canceled("snapshot save canceled");
        }
        catch (Exception ex)
        {
            op.Fail(ex, "snapshot save");
        }
    }

    /// <summary>
    /// 隐藏面板按需加载：软件清单只在「卸载页被打开过」或「已经在列」时才去扫。
    /// 默认视图是可清理面板，用户根本看不到卸载页 —— 没必要每次扫描都陪跑一遍注册表。
    /// </summary>
    private void MaybeLoadAppsInBackground()
    {
        if (_apps.Count > 0)
        {
            // 已经有清单：只重算占用（便宜），仍然放后台
            _ = RefreshAppUsageAfterScanAsync(_apps, _allFiles, _root);
            return;
        }
        if (_listingApps) return;
        if (_uninstallTabVisited)
        {
            _ = LoadApps();
            return;
        }
        AppLog.Info("Uninstall", "skipping app inventory: uninstall tab has not been opened yet");
    }

    /// <summary>用户第一次进卸载页时再加载软件清单。</summary>
    private void EnsureAppsLoaded()
    {
        _uninstallTabVisited = true;
        if (_apps.Count == 0 && !_listingApps) _ = LoadApps();
    }

    private async Task RefreshAppUsageAfterScanAsync(
        List<AppUninstallItem> apps,
        List<FileEntry> files,
        FileEntry root)
    {
        try
        {
            var usage = await Task.Run(() => AppRecommendationService.CalculateUsage(apps, files));
            if (!ReferenceEquals(_root, root) || !ReferenceEquals(_apps, apps)) return;
            AppRecommendationService.ApplyUsage(usage);
            // 占用口径变了，重算一次「这是什么软件」的事实信息（只讲事实，不做建议）
            AppFactualInfoService.Apply(apps);
            if (!_showingJunk) BindAppList();
        }
        catch (Exception ex)
        {
            UiLog("刷新软件占用失败: " + ex.Message);
        }
    }

    private void SetCleanProgress(double value, string text, bool determinate)
    {
        CleanProgressPanel.Visibility = Visibility.Visible;
        CleanProgressBar.IsIndeterminate = !determinate;
        if (determinate) CleanProgressBar.Value = Math.Clamp(value, 0, 100);
        CleanProgressText.Text = text;
    }

    private void HideCleanProgress()
    {
        CleanProgressPanel.Visibility = Visibility.Collapsed;
        CleanProgressBar.IsIndeterminate = false;
        CleanProgressBar.Value = 0;
    }

    private static void UiLog(string msg)
    {
        try
        {
            AppLog.EnsureDirectory();
            File.AppendAllText(AppLog.UiTimingPath,
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
        }
        catch
        {
            // 界面计时日志写不了（磁盘满 / 无权限）不能影响界面本身。
        }
    }

    /// <summary>迭代式收集目录下所有文件（显式栈 + visited 防环，避免损坏数据导致无限循环）。</summary>
    private static List<FileEntry> CollectFiles(FileEntry node)
    {
        var list = new List<FileEntry>();
        var visited = new HashSet<FileEntry>();
        var stack = new Stack<FileEntry>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var c in n.ChildList)
            {
                if (c.IsDirectory)
                {
                    if (visited.Add(c)) stack.Push(c); // 防环：每个目录只入栈一次
                }
                else list.Add(c);
            }
        }
        return list;
    }



    private void UpdateVolumeInfo()
    {
        try
        {
            if (DriveBox.SelectedItem is not string name) return;
            var d = new DriveInfo(name);
            if (!d.IsReady) return;
            _volumeTotal = d.TotalSize;
            _volumeUsed = d.TotalSize - d.TotalFreeSpace;
            long used = _volumeUsed;
            double pct = d.TotalSize > 0 ? 100.0 * used / d.TotalSize : 0;
            VolumeText.Text = Loc.Volume(
                FileEntry.FormatSize(d.TotalSize),
                FileEntry.FormatSize(used),
                pct,
                FileEntry.FormatSize(d.TotalFreeSpace));
        }
        catch (Exception ex)
        {
            // 拿不到卷信息（盘被拔了 / 没权限）：顶部空间显示留空，不弹错误。
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "UI", "", "volume-info",
                ex.GetType().Name));
        }
    }

    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ScanButton_Click(object sender, RoutedEventArgs e) => RunScan();

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopEverything();



    private static void RecalcUp(FileEntry node)
    {
        for (var n = node; n != null; n = n.Parent)
        {
            long size = 0, alloc = 0;
            int files = 0, folders = 0;
            foreach (var c in n.ChildList)
            {
                size += c.Size;
                alloc += c.Allocated;
                if (c.IsDirectory)
                {
                    folders += 1 + c.FolderCount;
                    files += c.FileCount;
                }
                else files += 1;
            }
            if (n.IsDirectory)
            {
                n.Size = size;
                n.Allocated = alloc;
            }
            n.FileCount = files;
            n.FolderCount = folders;
        }
    }


    private void CleanOpen_Click(object sender, RoutedEventArgs e)
    {
        if (CleanGrid.SelectedItem is CleanItem item)
            Reveal(RealPathOf(item));
    }

    /// <summary>
    /// 「在资源管理器中打开」的唯一入口。
    /// 用**数据模型里的完整路径**（不是界面上的省略路径、展示名或脱敏路径），
    /// 并复用 <see cref="ShellReveal"/>；失败只给简短状态，不弹模态框、不改选择与导航。
    /// </summary>
    void Reveal(string? path)
    {
        var r = ShellReveal.Reveal(path);
        if (r.Ok) return;
        if (r.Kind == RevealKind.NoPath) return;
        // 简短、可理解、不打断：写进已有的状态行，同时已有日志线索
        SetAiStatus(r.Message);
        AppLog.Info("UI", $"reveal {r.Kind} for {LogRedactor.ScrubPath(r.Target)}");
    }

    /// <summary>条目的真实完整路径（文件用文件路径，目录用目录路径）。</summary>
    static string RealPathOf(CleanItem item) => item.FullPath ?? "";

    /// <summary>
    /// 位置行的「在资源管理器中打开」。
    ///
    /// 聚合位置（重复组这类逻辑位置）**不一定对应唯一真实目录**：
    /// 这时让用户自己挑一个，绝不静默打开某个样本路径。
    /// </summary>
    private void LocationReveal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanLocationNode node) return;

        var dirs = RealDirectoriesOf(node);
        if (dirs.Count == 0)
        {
            SetAiStatus(Loc.RevealNoFolder);
            return;
        }
        if (dirs.Count == 1)
        {
            Reveal(dirs[0]);
            return;
        }

        // 多个真实位置：列出来让用户选（沿用项目既有的 ContextMenu 弹层方式）
        var menu = new ContextMenu
        {
            PlacementTarget = sender as UIElement,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        foreach (var d in dirs.Take(12))
        {
            var mi = new MenuItem { Header = d, ToolTip = d };
            mi.Click += (_, _) => Reveal(d);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    /// <summary>一个位置对应的**真实目录**（去重）。逻辑位置可能对应多个或一个都没有。</summary>
    static List<string> RealDirectoriesOf(CleanLocationNode node)
    {
        var set = new List<string>();
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            string full = p.Trim().Trim('"');
            if (!Directory.Exists(full)) return;
            if (!set.Contains(full, StringComparer.OrdinalIgnoreCase)) set.Add(full);
        }

        // 位置自己的路径就是真实目录时优先
        Add(node.Path);
        if (set.Count == 0)
        {
            foreach (var x in node.Items)
            {
                if (x.IsDirectory) Add(x.FullPath);
                else Add(Path.GetDirectoryName(x.FullPath ?? ""));
            }
        }
        return set;
    }

    static void OpenExplorer(string? path, bool directory)
    {
        // 兼容旧调用点：统一走 ShellReveal（directory 参数不再影响判定，按真实存在性走）
        _ = directory;
        ShellReveal.Reveal(path);
    }


    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Esc 逐层返回：清理前检查页 → 文件明细。
            if (_onPreflightPage)
            {
                PreflightBack_Click(this, new RoutedEventArgs());
                e.Handled = true;
                base.OnKeyDown(e);
                return;
            }
            if (_openLocation != null)
            {
                CloseDetail();
                e.Handled = true;
                base.OnKeyDown(e);
                return;
            }
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => OpenOverlay(Loc.SettingsTitle, settings: true);

    private void AboutButton_Click(object sender, RoutedEventArgs e)
        => OpenOverlay(Loc.AboutTitle, about: true);

    private void OpenOverlay(string title, bool settings = false, bool about = false, bool alert = false,
        bool confirm = false, bool selection = false, bool ruleSelect = false)
    {
        DialogTitle.Text = title;
        SettingsBody.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        ProvEditBody.Visibility = Visibility.Collapsed;
        AboutBody.Visibility = about ? Visibility.Visible : Visibility.Collapsed;
        SelectionBody.Visibility = selection ? Visibility.Visible : Visibility.Collapsed;
        RuleSelectBody.Visibility = ruleSelect ? Visibility.Visible : Visibility.Collapsed;
        AlertBody.Visibility = alert || confirm ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButtons.Visibility = confirm || ruleSelect ? Visibility.Visible : Visibility.Collapsed;
        DialogClose.Visibility = confirm || ruleSelect ? Visibility.Collapsed : Visibility.Visible;
        if (settings)
        {
            HighlightThemeButtons();
            LoadAiFields();
        }
        ShowOverlay();
    }

    /// <summary>
    /// 弹层淡入淡出的代次。每次开/关都 +1，
    /// 这样上一次淡出挂在 <c>Completed</c> 上的 Collapse **不会**把刚打开的弹层收掉
    /// （以前没有这个守卫：淡出还没结束时再打开弹层，旧回调会把新弹层隐藏掉）。
    /// </summary>
    private int _overlayEpoch;

    void ShowOverlay()
    {
        _overlayEpoch++;
        int epoch = _overlayEpoch;

        Overlay.BeginAnimation(OpacityProperty, null);
        DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        OverlayRoot.Visibility = Visibility.Visible;
        // **只让遮罩做不透明度动画**。卡片保持完全不透明：
        // 以前动画加在包含卡片的父容器上，WPF 的 Opacity 会乘到子元素，
        // 于是弹层内容在动画期间是半透明的，后面的列表直接透出来。
        Overlay.Opacity = 0;
        DialogScale.ScaleX = 0.97;
        DialogScale.ScaleY = 0.97;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase() };
        fade.Completed += (_, _) =>
        {
            if (epoch != _overlayEpoch) return;
            Overlay.BeginAnimation(OpacityProperty, null);
            Overlay.Opacity = 1;   // 动画结束后落回确定值，避免留下动画时钟
        };
        Overlay.BeginAnimation(OpacityProperty, fade);

        var grow = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase() };
        DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow.Clone());

        // 遮罩挡住了鼠标，但键盘焦点默认还留在下面的列表上（Tab/方向键会误操作底层）。
        // 把焦点移进对话框，键盘操作就落在弹层内。
        DialogCard.Focus();
    }

    void HideOverlay()
    {
        SaveAiFields();
        // 确认 / 取消按钮的文案是「按弹层定的」，关掉时恢复默认，
        // 免得下一次 AskConfirm 顶着上一次的「确认勾选这些」。
        ConfirmYesBtn.Content = Loc.Yes;
        ConfirmNoBtn.Content = Loc.No;
        if (ProvEditBody.Visibility == Visibility.Visible && OverlayRoot.Visibility == Visibility.Visible)
        {
            ProvEditBody.Visibility = Visibility.Collapsed;
            SettingsBody.Visibility = Visibility.Visible;
            DialogTitle.Text = Loc.SettingsTitle;
            LoadAiFields();
            return;
        }

        int epoch = ++_overlayEpoch;
        Overlay.BeginAnimation(OpacityProperty, null);
        DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        var fade = new DoubleAnimation(Overlay.Opacity, 0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) =>
        {
            // 只有「还是同一次关闭」才允许收起来；否则说明期间又打开过，绝不能隐藏。
            if (epoch != _overlayEpoch) return;
            Overlay.BeginAnimation(OpacityProperty, null);
            Overlay.Opacity = 0;
            OverlayRoot.Visibility = Visibility.Collapsed;
            DialogScale.ScaleX = 0.97;
            DialogScale.ScaleY = 0.97;
        };
        Overlay.BeginAnimation(OpacityProperty, fade);
    }

    public void ShowCrash(string text) => ShowAlert(Loc.AppName, text);

    private void ShowAlert(string title, string text)
    {
        _confirmYes = null;
        AlertText.Text = text;
        OpenOverlay(title, alert: true);
    }

    private void AskConfirm(string title, string text, Action onYes)
    {
        _confirmYes = onYes;
        AlertText.Text = text;
        OpenOverlay(title, confirm: true);
    }

    private void ConfirmYes_Click(object sender, RoutedEventArgs e)
    {
        var act = _confirmYes;
        _confirmYes = null;
        HideOverlay();
        act?.Invoke();
    }

    private void ConfirmNo_Click(object sender, RoutedEventArgs e)
    {
        _confirmYes = null;
        // 取消 = 什么都没发生：预览直接丢掉，一个勾都不会写
        DiscardRuleSelectPreview();
        HideOverlay();
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        _confirmYes = null;
        DiscardRuleSelectPreview();
        HideOverlay();
    }

    private void Overlay_Click(object sender, MouseButtonEventArgs e)
    {
        _confirmYes = null;
        DiscardRuleSelectPreview();
        HideOverlay();
    }

    private void Dialog_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        SaveAiFields();
        App.SaveUi(tag == "En" ? AppLang.En : AppLang.Zh);
        LoadAiFields();
    }

    void FillAiProtoBox()
    {
        AiProtoBox.ItemsSource = AiProtos.Select(Loc.AiKindName).ToList();
        AiProtoBox.SelectionChanged += (_, _) => UpdateDecisionsHint();
    }

    /// <summary>
    /// 选中「结构化判定」时才显示地址说明：这条通道不走 /v1，
    /// 不说清楚用户照着 chat 的习惯填就会拿到 404。
    /// </summary>
    void UpdateDecisionsHint()
    {
        if (AiDecisionsHintText == null) return;
        int i = AiProtoBox.SelectedIndex;
        bool on = i >= 0 && i < AiProtos.Length && AiProtos[i] == AiProtocol.Decisions;
        AiDecisionsHintText.Text = Loc.AiDecisionsHint;
        AiDecisionsHintText.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    void LoadAiFields()
    {
        App.Settings.Migrate();
        AiProvCards.ItemsSource = null;
        AiProvCards.ItemsSource = App.Settings.AiProviders.ToList();
        FillRunModels();
        RefreshAiLamp();
    }

    void OpenProvEdit(AiProviderCfg p)
    {
        App.Settings.AiActiveId = p.Id;
        App.Settings.Save();
        SettingsBody.Visibility = Visibility.Collapsed;
        ProvEditBody.Visibility = Visibility.Visible;
        AboutBody.Visibility = Visibility.Collapsed;
        AlertBody.Visibility = Visibility.Collapsed;
        ConfirmButtons.Visibility = Visibility.Collapsed;
        DialogClose.Visibility = Visibility.Visible;
        DialogTitle.Text = Loc.AiEditTitle;
        _aiModelLock = true;
        ShowProv(p);
        AiTestHint.Text = "";
        _aiModelLock = false;
        FillRunModels();
        RefreshAiLamp();
    }

    void ShowProv(AiProviderCfg? p)
    {
        AiNameBox.Text = p?.Name ?? "";
        AiUrlBox.Text = p?.BaseUrl ?? "";
        var proto = AiClient.ParseProtocol(p?.Protocol);
        int i = Array.IndexOf(AiProtos, proto);
        AiProtoBox.SelectedIndex = i < 0 ? 0 : i;
        UpdateDecisionsHint();
        AiKeyBox.Password = p?.ApiKey ?? "";
        if (AiKeyHint != null)
        {
            AiKeyHint.Text = !SecretProtector.Available
                ? Loc.AiKeyUnavailable
                : (p?.HasStoredKey == true ? Loc.AiKeyStored : Loc.AiKeyMissing) + "  ·  " + Loc.AiKeyStoreHint;
        }
        if (AiKeyClearBtn != null) AiKeyClearBtn.Content = Loc.AiKeyClear;
        string model = App.Settings.AiModel ?? "";
        if (p != null && (string.IsNullOrEmpty(model) || !p.Models.Contains(model, StringComparer.OrdinalIgnoreCase)) && p.Models.Count > 0)
            model = p.Models[0];
        AiModelBox.Text = model;
        FillModelPick(p?.Models, model);
        AiModelHint.Text = (p?.Models.Count ?? 0) == 0 ? Loc.AiModelsEmpty : Loc.AiModelsOk(p!.Models.Count);
    }

    void SaveAiFields()
    {
        if (AiUrlBox == null || ProvEditBody.Visibility != Visibility.Visible) return;
        App.Settings.Migrate();
        var p = App.Settings.CurrentProvider();
        if (p == null) return;
        WriteProv(p);
        App.Settings.Save();
        FillRunModels();
        RefreshAiLamp();
    }

    static AiProviderCfg NewProv()
        => new()
        {
            Id = "p" + Guid.NewGuid().ToString("N")[..8],
            Name = Loc.IsEn ? "Provider" : "提供方",
            Protocol = "completions",
        };

    /// <summary>
    /// 导出诊断包：环境信息 + 最近日志，**全部先脱敏再落地**。
    /// 不含 API Key，也不含未脱敏的用户路径，可以直接贴到 issue 里。
    /// </summary>
    private void DiagExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string name = "dashuohuo-diag-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
            string path = Path.Combine(AppLog.DirectoryPath, name);
            bool ok = AppLog.ExportDiagnostics(path);
            ShowAlert(Loc.DiagExportTitle, ok ? Loc.DiagExportOk(path) : Loc.DiagExportFail);
        }
        catch (Exception ex)
        {
            AppLog.Record("Diag", ex, "export from settings");
            ShowAlert(Loc.DiagExportTitle, Loc.DiagExportFail);
        }
    }

    /// <summary>
    /// 隐私开关：默认只把脱敏路径发给 AI，完整路径必须用户在这里明确打开。
    /// 打开时会记一笔日志，方便事后对账「到底有没有把真实路径发出去」。
    /// </summary>
    private void AiFullPath_Click(object sender, RoutedEventArgs e)
    {
        bool on = AiFullPathBox.IsChecked == true;
        if (App.Settings.AiSendFullPaths == on) return;
        App.Settings.AiSendFullPaths = on;
        App.Settings.Save();
        AppLog.Info("Settings", "send full local paths to AI = " + on);
    }

    /// <summary>
    /// 「清除密钥」：把当前供应商的密钥从内存和加密仓库里都去掉，并立刻落盘。
    /// 只清当前这家，不动别的提供方。
    /// </summary>
    private void AiKeyClear_Click(object sender, RoutedEventArgs e)
    {
        var p = App.Settings.CurrentProvider();
        if (p != null)
        {
            p.ApiKey = "";
            App.Settings.Save();
        }
        AiKeyBox.Password = "";
        ShowProv(App.Settings.CurrentProvider());
        AiTestHint.Text = Loc.AiKeyCleared;
        RefreshAiLamp();
        AppLog.Info("Settings", "user cleared the stored API key for the active provider");
    }

    void WriteProv(AiProviderCfg p)
    {
        int i = AiProtoBox.SelectedIndex;
        p.Name = AiNameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(p.Name)) p.Name = p.Id;
        p.BaseUrl = AiUrlBox.Text?.Trim() ?? "";
        p.Protocol = AiClient.ProtocolId(i >= 0 && i < AiProtos.Length ? AiProtos[i] : AiProtocol.Completions);
        p.ApiKey = AiKeyBox.Password ?? "";
        p.Models = (AiModelPick.ItemsSource as IEnumerable<string>)?.ToList()
                   ?? AiModelPick.Items.OfType<string>().ToList();
        string model = AiModelBox.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(model) && !p.Models.Contains(model, StringComparer.OrdinalIgnoreCase))
            p.Models.Add(model);
        App.Settings.AiModel = model;
    }

    private void AiAddProv_Click(object sender, RoutedEventArgs e)
    {
        var p = NewProv();
        App.Settings.AiProviders.Add(p);
        App.Settings.AiActiveId = p.Id;
        App.Settings.Save();
        OpenProvEdit(p);
    }

    private void AiEditProv_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AiProviderCfg p })
            OpenProvEdit(p);
    }

    private void AiDelProvCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AiProviderCfg p }) return;
        App.Settings.AiProviders.Remove(p);
        if (App.Settings.AiActiveId == p.Id)
            App.Settings.AiActiveId = App.Settings.AiProviders.FirstOrDefault()?.Id ?? "";
        App.Settings.Save();
        LoadAiFields();
    }

    void FillModelPick(IEnumerable<string>? ids, string? current)
    {
        var list = (ids ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        AiModelPick.ItemsSource = list;
        if (list.Count == 0) return;
        string pick = current ?? "";
        var match = list.FirstOrDefault(x => x.Equals(pick, StringComparison.OrdinalIgnoreCase));
        AiModelPick.SelectedItem = match ?? list[0];
    }

    private void AiModelPick_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_aiModelLock) return;
        if (AiModelPick.SelectedItem is string id && !string.IsNullOrWhiteSpace(id))
            AiModelBox.Text = id;
    }

    private async void AiFetch_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy) return;
        SaveAiFields();
        _aiBusy = true;
        RefreshAiLamp();
        AiFetchBtn.IsEnabled = false;
        AiModelHint.Text = Loc.AiWorking;
        using var op = StartOperation(ref _aiConfigCts, "AiConfig");
        var ct = op.Token;
        try
        {
            var ids = await AiClient.ListModelsAsync(ct);
            _aiModelLock = true;
            string current = AiModelBox.Text?.Trim() ?? "";
            FillModelPick(ids, current);
            if (string.IsNullOrEmpty(current) && ids.Count > 0)
                AiModelBox.Text = ids[0];
            _aiModelLock = false;
            var p = App.Settings.CurrentProvider();
            if (p != null) p.Models = ids.ToList();
            App.Settings.AiModels = ids.ToList();
            App.Settings.Save();
            FillRunModels();
            AiModelHint.Text = ids.Count == 0 ? Loc.AiModelsEmpty : Loc.AiModelsOk(ids.Count);
            op.Done("models fetched", ids.Count);
        }
        catch (OperationCanceledException)
        {
            AiModelHint.Text = Loc.Aborted;
            op.Canceled("model list canceled");
        }
        catch (Exception ex)
        {
            // 界面上给分类过的短句，技术细节进日志（已脱敏，不含 Key）
            AiModelHint.Text = AppError.From(ex, "list models").UserMessage;
            op.Fail(ex, "list models");
        }
        finally
        {
            _aiBusy = false;
            AiFetchBtn.IsEnabled = true;
            RefreshAiLamp();
        }
    }

    private async void AiTest_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy) return;
        SaveAiFields();
        _aiBusy = true;
        RefreshAiLamp();
        AiTestBtn.IsEnabled = false;
        AiTestHint.Text = Loc.AiWorking;
        using var op = StartOperation(ref _aiConfigCts, "AiConfig");
        var ct = op.Token;
        try
        {
            string reply = await AiClient.TestAsync(ct);
            AiTestHint.Text = string.IsNullOrWhiteSpace(reply) ? Loc.AiOk : Loc.AiOk + "  " + reply.Trim();
            SetAiLamp(true);
            op.Done("ai test ok");
        }
        catch (OperationCanceledException)
        {
            AiTestHint.Text = Loc.Aborted;
            op.Canceled("ai test canceled");
        }
        catch (Exception ex)
        {
            AiTestHint.Text = AppError.From(ex, "ai test").UserMessage;
            SetAiLamp(false);
            op.Fail(ex, "ai test");
        }
        finally
        {
            _aiBusy = false;
            AiTestBtn.IsEnabled = true;
            RefreshAiLamp();
        }
    }

    bool AiConfigured()
    {
        var p = App.Settings.CurrentProvider();
        return p != null && !string.IsNullOrWhiteSpace(p.BaseUrl) && !string.IsNullOrWhiteSpace(App.Settings.AiModel);
    }

    void FillRunModels()
    {
        if (AiModelList == null || AiModelBtnText == null) return;
        var rows = new List<ModelRow>();
        string cur = (App.Settings.AiModel ?? "").Trim();
        string activeId = App.Settings.AiActiveId ?? "";

        foreach (var p in App.Settings.AiProviders)
        {
            var models = p.Models.Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // 当前模型还没进目录（手填的），也让它出现在列表里
            if (models.Count == 0 && p.Id == activeId && cur.Length > 0)
                models.Add(cur);
            if (models.Count == 0) continue;

            rows.Add(new ModelRow { IsHeader = true, Text = ProvName(p) });
            foreach (var m in models)
                rows.Add(new ModelRow
                {
                    Text = m,
                    ProviderId = p.Id,
                    Model = m,
                    IsCurrent = p.Id == activeId && string.Equals(m, cur, StringComparison.OrdinalIgnoreCase),
                });
        }

        AiModelList.ItemsSource = rows;

        // 按钮显示「提供方 / 模型」；没配任何东西时提示去设置
        var prov = App.Settings.CurrentProvider();
        string provName = prov == null ? "" : ProvName(prov);
        AiModelBtnText.Text = rows.Count == 0
            ? Loc.AiNoModel
            : (string.IsNullOrWhiteSpace(cur)
                ? (provName.Length > 0 ? provName + " / " + Loc.AiNoModel : Loc.AiNoModel)
                : (provName.Length > 0 ? provName + " / " + cur : cur));
    }

    static string ProvName(AiProviderCfg p)
        => string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name.Trim();

    private void AiModelBtn_Click(object sender, RoutedEventArgs e)
        => AiModelPopup.IsOpen = !AiModelPopup.IsOpen;

    private void AiModelRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        var border = Ancestor<Border>(src);
        if (border?.DataContext is not ModelRow row || row.IsHeader) return;
        if (string.IsNullOrWhiteSpace(row.Model)) return;

        App.Settings.AiActiveId = row.ProviderId;
        App.Settings.AiModel = row.Model;
        App.Settings.Save();
        AiModelPopup.IsOpen = false;
        FillRunModels();
        RefreshAiLamp();
    }

    /// <summary>
    /// 现在是否有 AI 在跑。三个来源任一为真就算忙：
    ///  · <c>_aiBusy</c>：设置里的取模型 / 测试连接；
    ///  · <c>_aiAppsBusy</c>：卸载页的软件分析（历史入口，保留兼容）；
    ///  · <c>_itemAiRunning</c>：逐项分析的在飞登记表 —— **按条目计数**，
    ///    所以并发多项时只有最后一项结束才会停下来。
    /// </summary>
    bool AiBusyNow => _aiBusy || _aiAppsBusy || _itemAiRunning.Count > 0;

    /// <summary>
    /// 顶栏右侧的 AI 胶囊是**主指示器**：
    ///  · 已配置：模型 ID + 7px 绿点；
    ///  · 正在分析：模型名保留，绿点换成旋转指示器（由 AiSpinner 模板触发）；
    ///  · 未配置：整块 Collapsed（不显示灰色「未配置」标记）。
    /// 清理页那次性状态行仍然保留，但不再是唯一指示。
    /// </summary>
    void RefreshAiLamp()
    {
        bool busy = AiBusyNow;
        bool configured = AiConfigured();

        if (AiChip != null)
        {
            AiChip.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
            if (AiChipSpin != null)
            {
                // IsIndeterminate 的模板触发器负责启停旋转时钟；未配置时一定停掉，
                // 免得收起来的控件上还挂着一个 Forever 动画。
                AiChipSpin.IsIndeterminate = configured && busy;
                AiChipSpin.Visibility = configured && busy ? Visibility.Visible : Visibility.Collapsed;
            }
            if (configured)
            {
                string model = (App.Settings.AiModel ?? "").Trim();
                if (AiChipModel != null) AiChipModel.Text = model;
                if (AiChipDot != null)
                    AiChipDot.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
                // 可访问名称必须同时说清「哪个模型」和「空闲 / 分析中」
                string name = Loc.AiChipName(model, busy);
                System.Windows.Automation.AutomationProperties.SetName(AiChip, name);
                AiChip.ToolTip = name;
            }
        }

        if (CleanSummarySub == null) return;
        if (busy)
        {
            _aiTransientStatus = Loc.AiLampBusy;
            CleanSummarySub.Text = _aiTransientStatus;
        }
        else if (_aiTransientStatus == Loc.AiLampBusy)
        {
            // 只收回**这一条** AI 忙状态；别的动作提示（例如「已加入选择」）不动
            _aiTransientStatus = "";
            UpdateCandidateSummary();
        }
    }

    /// <summary>
    /// 顶栏 AI 胶囊的点击入口：进设置里的 AI 区，并把已有的模型弹层打开。
    /// 模型清单仍然只有 FillRunModels 那一套 —— 这里不新建任何第二份列表。
    /// </summary>
    private void AiChip_Click(object sender, RoutedEventArgs e)
    {
        OpenOverlay(Loc.SettingsTitle, settings: true);
        // 弹层刚可见时按钮还没完成布局，PlacementTarget 要等这一帧过去。
        // 若此时仍未加载（或设置区不可见），就安静地停在设置页，不抛异常。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (SettingsBody.Visibility != Visibility.Visible) return;
            if (AiModelBtn == null || !AiModelBtn.IsLoaded) return;
            FillRunModels();
            AiModelPopup.IsOpen = true;
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>临时 AI 状态（跑完就清掉，不做常驻指示）。</summary>
    private string _aiTransientStatus = "";

    void SetAiStatus(string text)
    {
        _aiTransientStatus = text ?? "";
        if (CleanSummarySub != null) CleanSummarySub.Text = _aiTransientStatus;
    }

    void SetAiLamp(bool ok)
    {
        _aiOk = ok;
        RefreshAiLamp();
    }

    static string StripToolMarkup(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) { sb.AppendLine(); continue; }
            if (line.Contains("| DSML |", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Contains("<|") && line.Contains("|>")) continue;
            if (line.Contains("tool_calls", StringComparison.OrdinalIgnoreCase) && line.Contains('<')) continue;
            sb.AppendLine(raw);
        }
        return sb.ToString().Trim();
    }


    // ===== AI：**按需**分析单个文件 / 单个清理位置 =====
    //
    // 这里刻意不再有「首页全局分析」：扫描完成后不会自动发任何模型请求。
    // 只有用户点了某一项的 AI 按钮，才为**那一项**发一次请求，范围不扩大。

    /// <summary>逐项分析用的共享 CTS（顶栏「停止」与单项取消都会用到）。</summary>
    CancellationTokenSource? _aiStop;

    /// <summary>逐项分析服务：缓存 + 有限并发 + 超时都在这层。</summary>
    private readonly ItemAiService _itemAi = new();

    /// <summary>正在进行的逐项分析：来源+稳定标识 → 取消源。用于就地取消与去重。</summary>
    private readonly ItemAiRunningRegistry _itemAiRunning = new();

    /// <summary>配置签名：换了提供方/模型/脱敏设置后，旧缓存失效。</summary>
    private string AiConfigSignature()
    {
        var p = App.Settings.CurrentProvider();
        return $"{p?.Id}|{p?.BaseUrl}|{App.Settings.AiModel}|{App.Settings.AiSendFullPaths}";
    }

    /// <summary>取（或建）某一项的 AI 视图状态。挂在项目自身上，不依赖行控件。</summary>
    private static ItemAiView AiOf(string scopeKey, ItemAiView? existing)
        => existing ?? new ItemAiView { ScopeKey = scopeKey };
    /// <summary>
    /// 行上的 AI 按钮。**同一项再点一次 = 取消**（正在跑）或**展开/收起结果**（已有结果）。
    /// 换到别的项不会影响这一项的状态。
    /// </summary>
    public void ItemAi_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not { } ctx) return;

        var (view, request) = DescribeAiTarget(ctx);
        if (view == null || request == null) return;

        if (view.IsBusy)
        {
            // 取消这一项（不影响别的项的队列）
            if (_itemAiRunning.TryGet(view.IsolationKey, out var cts) && cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
            return;
        }
        if (view.HasResult)
        {
            // 已有结果：切换展开，**不再请求模型**
            view.IsExpanded = !view.IsExpanded;
            return;
        }
        _ = RunItemAiAsync(view, request);
    }

    /// <summary>把行数据上下文翻译成「AI 视图 + 分析范围」。文件只看文件，位置只看位置。</summary>
    private (ItemAiView?, ItemAiRequest?) DescribeAiTarget(object ctx)
    {
        switch (ctx)
        {
            case CleanItem item:
            {
                item.Ai.Source = ItemAiSource.Clean;   // 显式声明来源：清理树
                return (item.Ai, new ItemAiRequest(
                    ScopeKey: item.FullPath,
                    IsFolder: item.IsDirectory,
                    Path: item.FullPath,
                    Label: item.Name,
                    Size: item.Size,
                    Modified: item.Entry?.Modified ?? default,
                    Kind: item.IsDirectory ? Loc.Folder : (item.Entry?.Category ?? ""),
                    LocalReason: item.Reason,
                    FolderSummary: Array.Empty<string>(),
                    FolderSummaryShown: 0,
                    FolderChildTotal: 0));
            }
            case CleanLocationNode loc:
            {
                loc.Ai.Source = ItemAiSource.Clean;    // 显式声明来源：清理树
                var summary = BuildFolderSummary(loc, out int total);
                return (loc.Ai, new ItemAiRequest(
                    ScopeKey: loc.Key,
                    IsFolder: true,
                    Path: loc.Path,
                    Label: loc.DisplayName,
                    Size: loc.Bytes,
                    Modified: default,
                    Kind: Loc.Folder,
                    LocalReason: loc.Reason,
                    FolderSummary: summary,
                    FolderSummaryShown: summary.Count,
                    FolderChildTotal: total));
            }
            // 整理页的对象：**只分析这一个文件夹**，不碰子目录、不碰整层、不碰整盘。
            // 本地已经认出来的也可以问 —— 知道用途不等于知道删除影响，这是两件事。
            // 关键：来源标成整理树。它的路径可能和某个清理候选**完全一样**，
            // 但结果绝不携带清理动作能力（不能勾选、不能定位清理明细）。
            case OrganizeNode node:
            {
                node.Ai.Source = ItemAiSource.Organize;
                var summary = BuildFolderSummary(node.Dir, out int total);
                return (node.Ai, new ItemAiRequest(
                    ScopeKey: node.FullPath,
                    IsFolder: true,
                    Path: node.FullPath,
                    Label: node.Name,
                    Size: node.Size,
                    Modified: node.Dir.Modified,
                    Kind: Loc.Folder,
                    LocalReason: node.Basis.Length > 0 ? node.Basis : node.SourceText,
                    FolderSummary: summary,
                    FolderSummaryShown: summary.Count,
                    FolderChildTotal: total)
                { Source = ItemAiSource.Organize });
            }
            default:
                return (null, null);
        }
    }

    /// <summary>
    /// 整理页那一项的有限目录摘要：**只取扫描树里已经存在的直接子项**，最大的若干条。
    /// 不在点击时去遍历磁盘（深层目录可能有几十万文件）。
    /// </summary>
    private static List<string> BuildFolderSummary(FileEntry dir, out int total)
    {
        total = dir.FolderCount + dir.FileCount;
        var lines = new List<string>();
        foreach (var x in dir.ChildList
                     .OrderByDescending(c => c.Size)
                     .Take(ItemAiPrompt.MaxFolderSummary))
        {
            lines.Add($"{(x.IsDirectory ? Loc.Folder : "")}{FileEntry.FormatSize(x.Size)}  {x.Name}");
        }
        return lines;
    }

    /// <summary>
    /// 文件夹的有上限摘要：**只用已经扫出来的数据**，取最大的若干直接子项。
    /// 绝不在点击时同步遍历目录 —— 一个位置可能有几十万个文件。
    /// </summary>
    private static List<string> BuildFolderSummary(CleanLocationNode loc, out int total)
    {
        total = loc.FileCount;
        var lines = new List<string>();
        foreach (var x in loc.Items
                     .OrderByDescending(i => i.Size)
                     .Take(ItemAiPrompt.MaxFolderSummary))
        {
            lines.Add($"{FileEntry.FormatSize(x.Size)}  {x.Name}");
        }
        return lines;
    }

    /// <summary>跑一项分析。只更新这一项的状态；浏览、展开、勾选照常可用。</summary>
    private async Task RunItemAiAsync(ItemAiView view, ItemAiRequest request)
    {
        // 去重：同一项已经在跑就不重复提交
        if (view.IsBusy) return;

        // 过期（重新扫描过）就先复位，绝不用旧结果去动新数据
        if (view.IsStale) { view.IsStale = false; view.Result = null; view.Verdict = null; }
        // 新请求开始：上一轮的失败/提示语不再代表这一轮
        view.Error = "";
        view.Notice = "";

        // 「认领清理条目」只属于清理树：整理页路径可能和某个清理候选完全一样
        //（FindItemByPath 会命中），一旦共用查找就会带上勾选能力。
        // 来源在 DescribeAiTarget 里显式标好。识别本身不先用本地项数冒充结论。
        bool cleanSource = view.IsCleanSource;
        var node = cleanSource ? ResolveLocationNode(view.ScopeKey) : null;
        IReadOnlyList<CleanItem> items = cleanSource
            ? ItemsForScope(view.ScopeKey)
            : (IReadOnlyList<CleanItem>)Array.Empty<CleanItem>();
        if (items.Count > 0)
            view.CanViewFiles = ResolveLocationForItem(items[0]) != null;

        if (!AiConfigured())
        {
            // 不造一张本地项数卡。文件表照样能展开、能勾；这里只说模型不可用。
            view.Error = Loc.AiNeedConfigLocalStillWorks;
            view.Notice = Loc.AiNeedConfigLocalStillWorks;
            view.Status = ItemAiStatus.Failed;
            return;
        }

        var cts = new CancellationTokenSource();
        _itemAiRunning.Add(view.IsolationKey, cts);
        int myReq = ++view.RequestId;
        view.Status = ItemAiStatus.Queued;
        RefreshAiLamp();   // 顶栏胶囊开始转（并发多项时按登记表计数，最后一项结束才停）

        try
        {
            var result = await _itemAi.AnalyzeAsync(
                request, App.Settings.CurrentProvider(), App.Settings.AiModel,
                App.Settings.AiSendFullPaths, AiConfigSignature(), cts.Token);

            // 旧请求晚回来：不许覆盖新状态
            if (myReq != view.RequestId) return;

            view.Result = result;
            // 模型不能改 CanDelete / Risk / Selected。「哪些可清理」仍只由本地规则决定。
            if (items.Count > 0)
            {
                string identity = node != null
                    ? IdentityOf(node)
                    : IdentityOfItem(items[0], ResolveLocationForItem(items[0]));
                view.Verdict = AiVerdict.Build(items, identity, result);
            }

            // 「请求结束」≠「有可用结论」：解析失败/空响应要如实说，并给重试。
            // 判据与 ItemAiResult.Barren 合同一致：有用途/影响/依据就还算有用，
            // 不因为建议档位没认出来（Unknown）就把有效内容丢掉。
            bool usable = ItemAiPrompt.IsUsable(result);
            if (!usable)
            {
                view.Status = ItemAiStatus.NoUseful;
                view.Error = Loc.AiNoResultRetry;
                view.Notice = Loc.AiNoResultRetry;
                view.IsExpanded = false;
            }
            else
            {
                view.Status = ItemAiStatus.Done;
                view.IsExpanded = true;
            }
        }
        catch (OperationCanceledException)
        {
            if (myReq != view.RequestId) return;
            // 区分「用户取消」与「超时」
            view.Status = cts.IsCancellationRequested && !_scanning
                ? ItemAiStatus.Canceled
                : ItemAiStatus.Timeout;
        }
        catch (Exception ex)
        {
            if (myReq != view.RequestId) return;
            view.Error = AppError.From(ex, "item ai").UserMessage;
            view.Status = ItemAiStatus.Failed;
            AppLog.Record("Ai", ex, $"item-ai {request.ScopeKey}");
        }
        finally
        {
            // 只有登记的仍是自己这一条时才移除：晚到的旧请求不能删掉新请求的取消源
            _itemAiRunning.RemoveIfCurrent(view.IsolationKey, cts);
            try { cts.Dispose(); } catch { }
            // 登记表清空后胶囊才停：并发多项时中途不会闪回绿点
            RefreshAiLamp();
            RefreshOrganizeAfterItemAi(view);
        }
    }

    /// <summary>
    /// 整理页单项 AI 结束后，用途列 / 页头计数要跟上。
    /// 只刷新展示，不改 Risk / CanDelete / Selected。
    /// </summary>
    void RefreshOrganizeAfterItemAi(ItemAiView view)
    {
        if (view.Source != ItemAiSource.Organize) return;
        RefreshOrganizeRows();
    }

    /// <summary>
    /// 扫描内容变了：**已经建出来的**逐项 AI 视图标记为过期。
    ///
    /// 只遍历已存在的视图（位置数量有上限；条目视图是惰性创建的），
    /// 所以在 30 万候选下也不会批量创建对象。
    /// 过期后不显示旧结论、也不提供任何操作，用户重新分析即可。
    /// </summary>
    void InvalidateItemAiAfterScan()
    {
        _aiDataGeneration++;
        // 注意：这里**不**动用途识别的缓存与请求计数。
        // 逐项 AI 代次在**每次分层重建**都会 +1（包括重复检测完成后的那次），
        // 而用途缓存/计数属于**整理页那一遍**的生命周期：在这里清会导致
        // ①已识别的结果被丢掉重问、②请求计数被归零（60 次的预算形同失效）。
        // 现在只有真正换扫描时（RebuildOrganize）才 ResetForScan。
        foreach (var loc in _layered.Purposes.SelectMany(p => p.Locations))
        {
            var v = loc.ExistingAi;
            if (v == null) continue;
            if (v.ScanGeneration != _aiDataGeneration) v.IsStale = true;
        }
    }

    /// <summary>
    /// 清理数据代次：每次重建分层结果就 +1。
    /// 逐项 AI 结果据此判断是否过期（**独立于扫描任务守卫 `_scanGeneration`**）。
    /// </summary>
    private int _aiDataGeneration;

    /// <summary>按稳定键找回这个 AI 视图对应的清理位置。找不到返回 null。</summary>
    private CleanLocationNode? ResolveLocationNode(string scopeKey)
    {
        if (_openLocation != null
            && string.Equals(scopeKey, _openLocation.Key, StringComparison.OrdinalIgnoreCase))
            return _openLocation;
        return _layered.Purposes.SelectMany(p => p.Locations)
                   .FirstOrDefault(l => string.Equals(l.Key, scopeKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 这个 AI 视图**实际分析的对象**。
    ///
    /// 位置级：该位置的全部条目。
    /// 文件级：就是那一个文件 —— 以前这里只按「位置键」查，文件路径永远查不到，
    /// 于是文件级的结论是空的（界面上只剩按钮和大片空白），
    /// 「查看文件」也会报「找不到这一组对应的位置」。
    /// </summary>
    IReadOnlyList<CleanItem> ItemsForScope(string scopeKey)
    {
        var loc = ResolveLocationNode(scopeKey);
        if (loc != null) return loc.Items;
        var item = FindItemByPath(scopeKey);
        return item != null ? new[] { item } : Array.Empty<CleanItem>();
    }

    /// <summary>按完整路径找回条目（用对象身份，不用显示名或截断路径）。</summary>
    CleanItem? FindItemByPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return null;
        foreach (var l in _layered.Purposes.SelectMany(p => p.Locations))
            foreach (var x in l.Items)
                if (string.Equals(x.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)) return x;
        return _layered.AllItems.FirstOrDefault(
            x => string.Equals(x.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>某个条目所属的位置（用于「查看文件」定位）。按对象身份找，不靠路径猜。</summary>
    CleanLocationNode? ResolveLocationForItem(CleanItem item)
        => _layered.Purposes.SelectMany(p => p.Locations)
            .FirstOrDefault(l => l.Items.Any(x => ReferenceEquals(x, item)));

    /// <summary>结论里那句说明要用的「这是什么」：优先本地认得出来的身份。</summary>
    static string IdentityOf(CleanLocationNode node)
    {
        string sig = AppSignatures.FriendlyName(node.Path) ?? "";
        if (!string.IsNullOrWhiteSpace(sig)) return sig;
        return node.DisplayName;
    }

    /// <summary>文件级结论的「这是什么」：签名名 → 规则原因 → 位置名。</summary>
    static string IdentityOfItem(CleanItem item, CleanLocationNode? owner)
    {
        string sig = AppSignatures.FriendlyName(item.FullPath) ?? "";
        if (!string.IsNullOrWhiteSpace(sig)) return sig;
        if (!string.IsNullOrWhiteSpace(item.Reason)) return item.Reason.Trim();
        return owner?.DisplayName ?? item.Name;
    }

    /// <summary>重新分析：把这一项的状态清回未分析，再跑一次。</summary>
    public void ItemAiRetry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not { } ctx) return;
        var (view, request) = DescribeAiTarget(ctx);
        if (view == null || request == null || view.IsBusy) return;
        view.Result = null;
        view.Error = "";
        view.Status = ItemAiStatus.Idle;
        _ = RunItemAiAsync(view, request);
    }

    /// <summary>
    /// 「选择这些文件」：本地动作，不是 AI 意见。
    /// 只勾符合本地清理资格、且无需额外确认的项；不依赖识别是否跑过。
    ///
    /// 硬约束：
    /// <list type="bullet">
    /// <item>位置行芯片：该位置 <see cref="AiVerdict.IsCleanable"/> 的项；</item>
    /// <item>若仍带着旧的分组 Tag，只勾那一组里同样符合资格的项；</item>
    /// <item>整理树没有这个入口，误点也直接返回；</item>
    /// <item>不新建删除入口，结果照常汇入「查看已选」与既有预检/确认/执行链路。</item>
    /// </list>
    /// </summary>
    public void AiSelectBucket_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not { } ctx) return;
        if (ctx is OrganizeNode) return;

        IReadOnlyList<CleanItem> scope;
        if (ctx is CleanLocationNode loc)
        {
            scope = loc.Items.Where(AiVerdict.IsCleanable).ToList();
        }
        else
        {
            var (view, _) = DescribeAiTarget(ctx);
            if (view == null || !view.IsCleanSource) return;
            var verdict = view.Verdict;
            if (verdict == null) return;
            scope = verdict.SelectableItems;
            if ((sender as FrameworkElement)?.Tag is AiBucketResult bucket)
            {
                if (!bucket.CanSelect) { SetAiStatus(Loc.SelectBlockedByRule); return; }
                scope = bucket.Items.Where(AiVerdict.IsCleanable).ToList();
            }
        }

        int added = 0;
        long bytes = 0;
        foreach (var x in scope)
        {
            if (!AiVerdict.IsCleanable(x) || x.Selected) continue;
            x.Selected = true;
            added++;
            bytes += Math.Max(0, x.Size);
        }

        if (added == 0) { SetAiStatus(Loc.AiEverythingSelected); return; }

        // 走既有的选择刷新链路（同步统计、底部栏、范围提示），不另起一套
        RefreshAfterSelectionChange();
        SetAiStatus(Loc.SelectAdded(added, FileEntry.FormatSize(bytes)));
    }

    /// <summary>
    /// 「查看文件」：打开对应位置的明细，并**精确过滤到这些条目**。
    ///
    /// 文件级结果只定位那一个文件（不再去查一个「分组」）；
    /// 分组级结果只显示该组真实关联的候选项。定位失败**保持当前页面与选择不变**，
    /// 给一句可读原因，不跳到错误位置。
    /// </summary>
    public void AiViewBucket_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not { } ctx) return;
        var (view, _) = DescribeAiTarget(ctx);
        if (view == null || view.Verdict == null) return;

        // 按钮 Tag 是分组时只看这一组；顶部按钮看「可考虑清理」那部分（没有就退回全部已分析对象）
        IReadOnlyList<CleanItem> subset = view.Verdict.SelectableItems.Count > 0
            ? view.Verdict.SelectableItems
            : view.Verdict.Buckets.SelectMany(b => b.Items).ToList();
        if ((sender as FrameworkElement)?.Tag is AiBucketResult bucket) subset = bucket.Items;

        if (subset.Count == 0)
        {
            SetAiStatus(Loc.GroupViewNeedsLocation);
            return;
        }

        // 按**对象身份**找所属位置：位置级直接命中，文件级用条目反查
        var node = ResolveLocationNode(view.ScopeKey) ?? ResolveLocationForItem(subset[0]);
        if (node == null)
        {
            // 找不到就什么都不动（不改页面、不改选择），只说明原因
            SetAiStatus(Loc.GroupViewNeedsLocation);
            view.CanViewFiles = false;
            return;
        }

        _locationScrollOffset = PurposePage?.VerticalOffset ?? _locationScrollOffset;
        if (!_detailIsOverlay || !ReferenceEquals(_openLocation, node)) OpenDetail(node);

        _pager?.SetItemFilter(subset);
        BindDetailPage();
        SetAiStatus(Loc.AiFilteredToCount(subset.Count));
    }

    private void AiStop_Click(object sender, RoutedEventArgs e)
    {
        // 统一取消，但保留登记：各请求结束时会各自按身份移除（不会误删别人的）
        _itemAiRunning.CancelAll();
        try { _aiStop?.Cancel(); } catch (ObjectDisposedException) { }
        AppLog.Info("Ai", "user stopped AI analysis");
    }

    /// <summary>
    /// 2.11：目录侧栏（及其右键菜单「问 AI 这是什么」）已整体移除。
    /// 逐项 AI 仍然只从清理列表 / 按文件夹删除列表上那一项显式发起。
    /// </summary>

    private void RepoLink_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Loc.Repo) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // 没有默认浏览器 / 被策略拦：记日志，别弹窗打断。
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "UI", "", "open-repo",
                ex.GetType().Name));
        }
    }

    private void TabOrganize_Click(object sender, RoutedEventArgs e) => ShowRightTab(RightTab.Organize);
    private void TabClean_Click(object sender, RoutedEventArgs e) => ShowRightTab(RightTab.Clean);
    private void TabUninstall_Click(object sender, RoutedEventArgs e) => ShowRightTab(RightTab.Uninstall);

    /// <summary>
    /// 右侧主功能页。**用明确的枚举而不是数字索引** ——
    /// 以前是 0 清理 / 1 扩展名 / 2 卸载，删掉中间那页后
    /// 「卸载」的索引就会错位到别的页面。
    /// 默认进「文件夹整理」：先看清盘上有什么，再决定清什么。
    /// </summary>
    private enum RightTab { Organize, Clean, Uninstall }

    private RightTab _rightTab = RightTab.Clean;

    private void ShowRightTab(RightTab tab)
    {
        _rightTab = tab;
        if (CleanPane != null)
            CleanPane.Visibility = tab == RightTab.Clean ? Visibility.Visible : Visibility.Collapsed;
        if (OrganizePane != null)
            OrganizePane.Visibility = tab == RightTab.Organize ? Visibility.Visible : Visibility.Collapsed;
        UninstallPane.Visibility = tab == RightTab.Uninstall ? Visibility.Visible : Visibility.Collapsed;
        MarkTab(TabOrganizeBtn, tab == RightTab.Organize);
        MarkTab(TabCleanBtn, tab == RightTab.Clean);
        MarkTab(TabUninstallBtn, tab == RightTab.Uninstall);
        // 卸载页第一次被打开时才去扫软件清单（隐藏面板按需初始化）
        if (tab == RightTab.Uninstall) EnsureAppsLoaded();
        // 切页时把第三层状态同步过去（各页只显示自己的状态）
        UpdateScanStateLine();
    }

    private static void MarkTab(Button b, bool on)
    {
        b.BorderBrush = ThemeService.Brush(on ? "Accent" : "Border");
        b.Foreground = ThemeService.Brush(on ? "Accent" : "TextDim");
    }

    // ==================== 分层展示：一次只显示一层 ====================
    // 首页（用途） → 位置页（软件 / 实际文件夹） → 文件详情（右侧面板）。
    // 三张表不再纵向堆叠：主区域同一时刻只有一个主角，只有文件详情这一个滚动列表
    // 会额外出现，而且它是按需打开的侧面板，不占常驻纵向空间。

    /// <summary>当前分层结果（用途 / 位置 / 完整候选）。</summary>
    private CleanLayeredResult _layered = CleanLayeredResult.Empty;
    /// <summary>首页的两个风险分区（建议清理默认展开 / 需要你确认默认折叠）。</summary>
    private List<CleanPurposeSection> _sections = new();
    /// <summary>当前打开明细的位置（右侧明细面板 / 窄窗口整页）。</summary>
    private CleanLocationNode? _openLocation;
    /// <summary>明细分页器（搜索覆盖完整候选集，不只是已加载页）。</summary>
    private CleanItemPager? _pager;
    /// <summary>分层重建的代次，旧结果不能覆盖新状态。</summary>
    private int _layerGeneration;
    /// <summary>首页滚动位置：就地展开/收起后恢复，视口不跳走。</summary>
    private double _purposeScrollOffset;
    /// <summary>就地展开/收起之前记下的滚动位置（打开明细后恢复）。</summary>
    private double _locationScrollOffset;
    /// <summary>明细当前是覆盖整页（窄窗口）还是侧面板。</summary>
    private bool _detailIsOverlay;
    /// <summary>窄窗口阈值：低于它就把明细切成独立的整页。</summary>
    private const double DetailOverlayThreshold = 820;

    private void RefreshCleanUi()
    {
        if (CleanPageTitle == null) return;
        if (_report == null)
        {
            ClearCleanLayers();
            CleanPageTitle.Text = Loc.PagePurposeTitle;
            CleanPageSub.Text = "";
            CleanSummarySub.Text = "";
            ShowCleanState(CleanStateKind.None);
            UpdateSelectionUi();
            ShowRightTab(_rightTab);
            return;
        }
        // 报告有内容但分层还没建（异常兜底）：同步建一次，保证界面不空
        if (_layered.TotalFiles == 0 && _reportCleanableCount() > 0)
        {
            _layered = CleanGroupingService.Build(AllCandidates().ToList());
            BuildSections();
            ShowPurposePage(restoreScroll: false);
        }
        UpdateSelectionUi();
        ShowRightTab(_rightTab);
    }

    private void ClearCleanLayers()
    {
        _layered = CleanLayeredResult.Empty;
        _sections = new List<CleanPurposeSection>();
        _openLocation = null;
        _pager = null;
        _purposeScrollOffset = 0;
        _locationScrollOffset = 0;
        PurposeSections.ItemsSource = null;
        CloseDetail();
    }

    private int _reportCleanableCount()
    {
        if (_report == null) return 0;
        return _report.Cleanable.Count + _report.LargeFiles.Count + _report.OldFiles.Count
             + _report.Duplicates.Count + _report.EmptyFolders.Count
             + _report.BrokenShortcuts.Count + _report.LongPaths.Count;
    }

    /// <summary>
    /// 后台重建分层结果（去重 / 分用途 / 分位置 / 统计），完成后在 UI 线程一次性换掉。
    /// **不在后台碰任何控件或绑定视图** —— 只算数据。
    /// </summary>
    /// <param name="invalidateItemAi">
    /// 是否顺手把逐项 AI 结果标记过期。**只有真的换了扫描内容才该这么做** ——
    /// 批量归类只是给候选补了用途，扫描数据一个字没变，标过期等于白扔掉用户已经跑过的逐项分析。
    /// </param>
    private async Task RebuildLayersAsync(FileEntry root, PerfTrace? perf, string label, bool invalidateItemAi = true)
    {
        if (_report == null) return;
        int myGeneration = ++_layerGeneration;
        var candidates = AllCandidates().ToList();
        // 范围在后台算：过滤 + 重算位置/空间统计都是纯函数，不碰控件
        string scopePrefix = _cleanScopeRoot == null
            ? ""
            : CleanListSnapshot.FolderPrefix(_cleanScopeRoot.FullPath ?? "");

        CleanLayeredResult layered;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            layered = await Task.Run(() =>
            {
                var full = CleanGroupingService.Build(candidates);
                return string.IsNullOrEmpty(scopePrefix)
                    ? full
                    : CleanGroupingService.Scope(full, scopePrefix);
            });
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLog.Record("Clean", ex, "layer build (" + label + ")");
            return;
        }
        sw.Stop();
        perf?.Mark("layers:" + label, sw.Elapsed.TotalMilliseconds, layered.TotalFiles);

        // 代次 / 根节点守卫：旧任务的结果不能覆盖新状态
        if (!ReferenceEquals(_root, root) || myGeneration != _layerGeneration) return;

        _layered = layered;
        if (invalidateItemAi)
            InvalidateItemAiAfterScan();   // 新扫描 ⇒ 旧的逐项 AI 结果过期，不给旧结论也不给操作
        // 文件夹整理：**每次扫描只建一次**，而且建在扫描代次落定之后，
        // 这样对象标识里的代次与本次扫描一致（旧请求就不可能串到新列表里）。
        if (_organizeBuiltForScan != _scanGeneration) RebuildOrganize();
        BuildSections();
        RestoreOpenLayers();      // 用稳定键找回原来的页面/位置
        UpdateScopeChip();        // 范围提示里的「范围外已选」要跟着最新选择走
        UpdateSelectionUi();

        AppLog.Info("Clean", $"layers({label}) files={layered.TotalFiles} selectable={layered.SelectableTotal} "
            + $"purposes={layered.Purposes.Count} locations={layered.TotalLocations} "
            + $"bytes={layered.TotalBytes} overlap={layered.OverlapCount} dupHits={layered.DuplicateHitsRemoved} "
            + $"buildMs={sw.Elapsed.TotalMilliseconds:0.0}");
    }

    private void BuildSections() => _sections = CleanPurposeSection.Build(_layered.Purposes);

    /// <summary>重建后重新绑回首页，并恢复滚动位置。</summary>
    private void RestoreOpenLayers()
    {
        // 2.11：不再有「位置页」这个第二视图。展开状态挂在分类/位置实例上，
        // 重建会换实例，所以这里只负责把首页重新绑上并恢复滚动位置。
        ShowPurposePage(restoreScroll: true);
    }

    // ---------------- 清理范围 ----------------

    /// <summary>
    /// 用户显式设置的清理范围根目录。null = 全盘。
    /// 2.11 起目录侧栏已移除，不再有「浏览目录」这第二个状态。
    /// </summary>
    private FileEntry? _cleanScopeRoot;

    /// <summary>扫描耗时（秒），只进「扫描详情」。</summary>
    private double _scanElapsed;


    private void ClearScope_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanScopeRoot == null) return;
        _cleanScopeRoot = null;
        ApplyCleanScope();
    }

    /// <summary>
    /// 应用/清除清理范围。重建分层（后台）后回到用途首页。
    /// **不改动任何勾选**：范围只影响「显示什么」，全局选择始终保留并单独汇总。
    /// </summary>
    private void ApplyCleanScope()
    {
        UpdateScopeChip();
        if (_report == null) return;
        if (_root != null) _ = RebuildLayersAsync(_root, null, _cleanScopeRoot != null ? "scoped" : "unscoped");
    }

    /// <summary>清理页的范围提示条：只有设置了范围才出现。</summary>
    private void UpdateScopeChip()
    {
        if (CleanScopeBar == null) return;
        bool scoped = _cleanScopeRoot != null;
        CleanScopeBar.Visibility = scoped ? Visibility.Visible : Visibility.Collapsed;
        if (!scoped) { CleanScopeText.Text = ""; return; }
        string path = _cleanScopeRoot!.FullPath ?? "";
        // 范围之外的已选项仍然有效且会被执行，必须显式说明
        int outside = _layered.AllItems.Count(x => x.Selected && x.CanDelete
            && !CleanListSnapshot.UnderPrefix(x.FullPath, CleanListSnapshot.FolderPrefix(path)));
        CleanScopeText.Text = Loc.ScopeChip(path, outside);
    }

    // ---------------- 清理首页：分类 → 位置 → 文件 全部就地展开 ----------------

    private void ShowPurposePage(bool restoreScroll)
    {
        _onPreflightPage = false;
        PurposeSections.ItemsSource = _sections;
        PurposePage.Visibility = Visibility.Visible;
        if (PreflightPage != null) PreflightPage.Visibility = Visibility.Collapsed;
        UpdatePageHeader();
        ScheduleRowContainerAudit(PurposeSections, _sections.Sum(s => s.RowCount), "purpose-home");
        if (restoreScroll)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (PurposePage != null) PurposePage.ScrollToVerticalOffset(_purposeScrollOffset);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>分区展开/收起。默认：建议清理展开、需要你确认折叠。</summary>
    private void SectionToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanPurposeSection section) return;
        section.IsExpanded = !section.IsExpanded;
        // 展开状态影响行容器数量，重新审计一次
        ScheduleRowContainerAudit(PurposeSections, _sections.Sum(s => s.RowCount), "purpose-home");
    }

    /// <summary>
    /// 展开/收起这一类的清理位置 —— **就地展开，不换视图**。
    /// 勾选挂在 CleanLocationNode / CleanItem 实例上，来回展开一项都不会丢。
    /// </summary>
    private void PurposeExpand_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanPurposeNode node) return;
        TogglePurposeInline(node);
    }

    /// <summary>点用途名称那一块也就地展开；复选框仍然只管选择。</summary>
    private void PurposeExpandRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanPurposeNode node) return;
        TogglePurposeInline(node);
    }

    /// <summary>
    /// 分类行就地展开/收起。展开会改变内容高度，所以先记下滚动位置，
    /// 等布局跑完再把视口落回合理值 —— 用户不会因为展开而被甩到别处。
    /// </summary>
    private void TogglePurposeInline(CleanPurposeNode node)
    {
        _purposeScrollOffset = PurposePage?.VerticalOffset ?? 0;
        bool opening = !node.IsExpanded;
        node.IsExpanded = opening;
        AppLog.Info("Clean", $"op=inline-locations purpose={node.PurposeName} open={opening} "
            + $"locations={node.LocationCount}");
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (PurposePage == null) return;
            double target = Math.Min(_purposeScrollOffset, Math.Max(0, PurposePage.ScrollableHeight));
            PurposePage.ScrollToVerticalOffset(target);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>返回：只负责关掉文件明细（首页已经没有第二层页面可回）。</summary>
    private void CleanBack_Click(object sender, RoutedEventArgs e)
    {
        if (_openLocation != null) CloseDetail();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root == null) return null;
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur is ScrollViewer sv) return sv;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(cur);
            for (int i = 0; i < n; i++)
                stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(cur, i));
        }
        return null;
    }

    /// <summary>「查看路径」：默认不显示完整路径，点开才展开（可复制 / 打开）。</summary>
    private void LocationPathToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanLocationNode node) return;
        node.IsPathVisible = !node.IsPathVisible;
    }

    private void LocationCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanLocationNode node) return;
        CopyToClipboard(node.Path);
    }

    private void CopyToClipboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            Clipboard.SetText(text);
            CleanSummarySub.Text = Loc.PathCopied;
        }
        catch (Exception ex)
        {
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "UI", "", "clipboard", ex.GetType().Name));
        }
    }

    /// <summary>在资源管理器里打开这个清理位置（只打开，不做任何删除动作）。</summary>
    private void LocationOpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanLocationNode node) return;
        string path = node.Path;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            // 重复组这类逻辑位置没有单一目录，就打开第一项所在目录
            var first = node.Items.FirstOrDefault(x => !string.IsNullOrEmpty(x.FullPath));
            if (first == null) return;
            path = Directory.Exists(first.FullPath)
                ? first.FullPath
                : Path.GetDirectoryName(first.FullPath) ?? "";
        }
        OpenExplorer(path, directory: true);
    }

    // ---------------- 文件详情（右侧面板 / 窄窗口整页） ----------------

    private void LocationViewFiles_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanLocationNode node) return;
        ToggleLocationFiles(node);
    }

    /// <summary>
    /// 点位置行的**名称区域** = 就地展开这一处的候选文件。
    /// 复选框、AI 按钮、文件夹图标各自处理自己的点击（不同 Grid 列 + 各自 Click 处理器），
    /// 不会误触展开。
    /// </summary>
    private void LocationRowOpen_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanLocationNode node) return;
        ToggleLocationFiles(node);
    }

    /// <summary>
    /// 行内展开 / 收起这一处的候选文件。
    ///
    /// 展开是**惰性**的（第一次展开才建列表，且只取前 N 条）；
    /// 收起时把外层列表的滚动位置记下来再恢复，避免内容变矮之后视口跳走。
    /// 选择状态挂在 <see cref="CleanItem"/> 实例上，收起再展开**一项都不会丢**。
    /// 2.11：外层滚动容器就是首页本身（不再有位置页的第二层列表）。
    /// </summary>
    private void ToggleLocationFiles(CleanLocationNode node)
    {
        double before = PurposePage?.VerticalOffset ?? 0;
        bool opening = !node.IsFilesOpen;
        node.ToggleFiles();
        if (opening)
        {
            _locationScrollOffset = before;
            AppLog.Info("Clean", $"op=inline-files dir={node.DisplayName} open=True "
                + $"listed={node.VisibleFiles.Count} total={node.FileCount}");
        }
        else
        {
            // 收起后内容变矮：等布局跑完再把滚动位置落回合理值
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (PurposePage == null) return;
                double target = Math.Min(before, Math.Max(0, PurposePage.ScrollableHeight));
                PurposePage.ScrollToVerticalOffset(target);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>行内列表里的勾选框：TwoWay 绑定已写好 <c>CleanItem.Selected</c>，这里只刷新派生显示。</summary>
    private void InlineFileCheck_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanItem) return;
        e.Handled = true;
        RefreshAfterSelectionChange();
    }

    /// <summary>需要搜索 / 分页时才进右侧明细面板 —— 这是行内列表唯一的「更多」入口。</summary>
    private void LocationOpenFull_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanLocationNode node) return;
        _locationScrollOffset = PurposePage?.VerticalOffset ?? 0;
        OpenDetail(node);
    }

    /// <summary>
    /// 打开明细。分页器拿的是**该位置的完整候选**，所以：
    /// 搜索覆盖未加载页；勾选挂在条目实例上，翻页/搜索都不丢；
    /// 选择状态只有这一套，不存在「主界面与明细互相矛盾」的第二套状态。
    /// </summary>
    private void OpenDetail(CleanLocationNode node)
    {
        _openLocation = node;
        _pager = new CleanItemPager(node.Items, CleanItemPager.DefaultPageSize);
        DetailSearchBox.Text = "";
        DetailPathBox.Visibility = Visibility.Collapsed;
        DetailTitle.Text = node.DisplayName;
        DetailSubtitle.Text = Loc.DetailScopeCount(node.FileCount, node.SizeText);
        UpdateDetailPathButtons();
        BindDetailPage();
        UpdateDetailLayout();
        UpdatePageHeader();
    }

    private void CloseDetail()
    {
        _openLocation = null;
        _pager = null;
        CleanGrid.ItemsSource = null;
        UpdateDetailLayout();
        UpdatePageHeader();
    }

    /// <summary>明细的开关与宽度：宽窗口是右侧面板，窄窗口切成独立的整页。</summary>
    private void UpdateDetailLayout()
    {
        if (DetailPanel == null) return;
        bool open = _openLocation != null;
        if (!open)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            DetailCol.Width = new GridLength(0);
            MainCol.Width = new GridLength(1, GridUnitType.Star);
            CleanBackBtn.Visibility = Visibility.Collapsed;
            return;
        }

        // 用**主内容区的真实宽度**判断，而不是窗口宽度：DPI 会影响实际可用像素。
        double avail = CleanMain.ActualWidth;
        if (avail <= 0)
        {
            // 还没排过版（ActualWidth=0）：按窗口宽度估算，避免误判成宽屏而把详情压窄
            double win = ActualWidth > 0 ? ActualWidth : Width;
            if (double.IsNaN(win) || win <= 0) win = 1000;
            avail = win;
        }

        // 分栏要求**两侧都够用**：列表至少 ~520，详情至少 ~560。
        // 只够一边时就把详情切成占满主区域的整页（顶部返回箭头回列表）。
        _detailIsOverlay = avail < DetailDockMinWidth;
        DetailPanel.Visibility = Visibility.Visible;
        if (_detailIsOverlay)
        {
            MainCol.Width = new GridLength(0);
            DetailCol.Width = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            MainCol.Width = new GridLength(1, GridUnitType.Star);
            DetailCol.Width = new GridLength(Math.Clamp(avail * 0.54, DetailMinWidth, DetailMaxWidth));
        }

        ApplyDetailColumnPriority(_detailIsOverlay ? avail : Math.Max(DetailMinWidth, Math.Min(avail * 0.54, DetailMaxWidth)));

        // 整页模式：**页头已经有一套标题+统计+返回箭头**，详情内不再重复；
        // 分栏模式保留自己的标题与关闭图标。
        if (DetailTitle != null)
        {
            DetailTitle.Visibility = _detailIsOverlay ? Visibility.Collapsed : Visibility.Visible;
            DetailSubtitle.Visibility = _detailIsOverlay ? Visibility.Collapsed : Visibility.Visible;
        }
        CleanBackBtn.Visibility = Visibility.Visible;   // 覆盖模式下返回 = 关明细
    }

    /// <summary>分栏 / 整页的分界：低于它就整页显示详情。</summary>
    private const double DetailDockMinWidth = 1080;
    private const double DetailMinWidth = 560;
    private const double DetailMaxWidth = 760;

    /// <summary>
    /// 列优先级：**勾选、文件名、大小、AI 操作**永远保留（固定宽度，绝不被压缩）；
    /// 「类型」是次要列，空间不够就整列隐藏 —— 并把类型并入「说明」列，
    /// 而不是把它压成残缺文字（§6）。
    ///
    /// 宽度阈值按**内容实际需要**算，不是拍脑袋：
    /// 勾选 34 + 类型 64 + 大小 78 + AI 118 + 文件名 140 = 434，
    /// 再加说明最少 120 ⇒ 类型至少要到 620 才放得下两列都读得清。
    /// </summary>
    void ApplyDetailColumnPriority(double width)
    {
        if (ColCleanType == null || ColCleanName == null) return;
        if (width <= 0) width = DetailMinWidth;

        bool showType = width >= 620;
        ColCleanType.Visibility = showType ? Visibility.Visible : Visibility.Collapsed;
        ColCleanType.Width = showType ? new DataGridLength(64) : new DataGridLength(0);

        // 说明已经移到分组标题（共同说明只显示一次），这里只决定类型列是否显示。
        // 文件名吃满剩余空间。
        ColCleanName.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    private void DetailClose_Click(object sender, RoutedEventArgs e) => CloseDetail();

    /// <summary>
    /// 明细面板的「在资源管理器中打开」。
    /// 与位置行同一个入口：聚合位置没有唯一真实目录时让用户挑，不静默打开样本路径。
    /// </summary>
    private void DetailReveal_Click(object sender, RoutedEventArgs e)
    {
        if (_openLocation == null) return;
        var dirs = RealDirectoriesOf(_openLocation);
        if (dirs.Count == 0)
        {
            SetAiStatus(Loc.RevealNoFolder);
            return;
        }
        if (dirs.Count == 1)
        {
            Reveal(dirs[0]);
            return;
        }
        var menu = new ContextMenu
        {
            PlacementTarget = sender as UIElement,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        foreach (var d in dirs.Take(12))
        {
            var mi = new MenuItem { Header = d, ToolTip = d };
            mi.Click += (_, _) => Reveal(d);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    private void UpdateDetailPathButtons()
        => DetailPathBox.Text = _openLocation?.Path ?? "";

    private void DetailCopyPath_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_openLocation?.Path);

    /// <summary>把当前页绑到文件表格。源已排序，视图只加分组、不加排序。</summary>
    private void BindDetailPage()
    {
        var pager = _pager;
        if (pager == null)
        {
            CleanGrid.ItemsSource = null;
            return;
        }
        var perf = new PerfTrace("detail-bind");
        var page = pager.Visible;

        // 组统计按**完整过滤结果**算（不是已加载的那一页），
        // 折叠组的数量与空间才是真的全组总量，也不会重复计。
        BuildDetailGroupStats(pager.Matches);

        // 搜索进行中：命中的组一律展开；清空搜索后恢复用户之前的折叠状态
        _detailGroupsExpanded.ExpandAll = pager.Search.Length > 0;

        var view = CollectionViewSource.GetDefaultView(page);
        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Clear();
            view.SortDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CleanItem.DetailGroupKey)));
        }
        // 单一分组默认展开；多个分组默认只展开第一组
        _detailGroupsExpanded.MarkFirstGroupExpanded(_detailGroups.Stats.Keys.FirstOrDefault());
        CleanGrid.ItemsSource = view;

        // 「已加载 / 全部」始终可见，搜索范围说明紧跟其后
        DetailCountHint.Text = pager.StatusText;
        DetailScopeHint.Text = pager.ScopeText;
        DetailMoreBtn.IsEnabled = pager.HasMore;
        DetailMoreBtn.Visibility = pager.HasMore ? Visibility.Visible : Visibility.Collapsed;
        DetailMoreHint.Text = pager.HasMore ? "" : Loc.AllLoaded;
        perf.Mark("bind", 0, page.Count);
        perf.Flush();

        ScheduleRowContainerAudit(CleanGrid, pager.MatchCount, "detail-grid");
    }

    /// <summary>详情列表的分组表头与展开状态（按分组键记忆）。</summary>
    readonly DetailGroupHeaderConverter _detailGroups = new();
    /// <summary>文件夹用途识别服务（预算、缓存、AI 接缝都在这层）。</summary>
    readonly FolderPurposeService _folderPurpose;
    /// <summary>启动时建立一次的系统 KnownFolder 快照（重定向下载/桌面/图片等），之后只查内存。</summary>
    readonly SystemPathSnapshot _systemPathSnapshot;
    /// <summary>本地证据服务（系统语义 + 已安装位置）。系统快照只建一次，已安装清单在清点软件后叠上来。</summary>
    LocalEvidenceService _localEvidence;
    readonly DetailGroupExpandedConverter _detailGroupsExpanded = new();

    /// <summary>
    /// 按**完整过滤结果**算每个展示组的数量、空间与已选数。
    ///
    /// 只做一次线性遍历，不创建任何 UI 控件（§六）。折叠的组也有准确统计。
    /// </summary>
    void BuildDetailGroupStats(IReadOnlyList<CleanItem> all)
    {
        _detailGroups.Stats.Clear();
        var acc = new Dictionary<string, (int Count, long Bytes, int Sel, long SelBytes,
            string Title, string Note)>(StringComparer.Ordinal);
        foreach (var x in all)
        {
            string k = x.DetailGroupKey;
            acc.TryGetValue(k, out var cur);
            long size = Math.Max(0, x.Size);
            cur.Count++;
            cur.Bytes += size;
            if (x.Selected) { cur.Sel++; cur.SelBytes += size; }
            // 同组说明来源一致，取第一条即可（键里已含完整说明）
            if (cur.Title == null) { cur.Title = x.DetailGroupTitle; cur.Note = x.NoteText ?? ""; }
            acc[k] = cur;
        }

        foreach (var kv in acc)
        {
            var v = kv.Value;
            string note = (v.Note ?? "").Trim();
            _detailGroups.Stats[kv.Key] = new DetailGroupHeader(
                kv.Key,
                v.Title ?? "",
                Loc.GroupStatLine(v.Count, FileEntry.FormatSize(v.Bytes)),
                note,
                v.Sel > 0 ? Loc.GroupSelectedLine(v.Sel, FileEntry.FormatSize(v.SelBytes)) : "");
        }
    }

    /// <summary>
    /// 折叠/展开一个分组：**只改展示状态**，绝不碰选择、风险或清理资格。
    /// </summary>
    public void DetailGroupToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DetailGroupHeader h) return;
        if (_detailGroupsExpanded.Collapsed.Contains(h.Key))
        {
            _detailGroupsExpanded.Collapsed.Remove(h.Key);
            _detailGroupsExpanded.Expanded.Add(h.Key);
        }
        else
        {
            _detailGroupsExpanded.Expanded.Remove(h.Key);
            _detailGroupsExpanded.Collapsed.Add(h.Key);
        }
        if (_pager != null) BindDetailPage();
    }

    private void DetailMore_Click(object sender, RoutedEventArgs e)
    {
        if (_pager == null) return;
        if (_pager.LoadMore()) BindDetailPage();
    }

    private void DetailSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_pager == null) return;
        _pager.SetSearch(DetailSearchBox.Text);
        BindDetailPage();
    }

    /// <summary>
    /// 明细里的选择范围。三种范围**文案与范围必须一致**：
    /// 当前页 / 搜索结果全部 / 整组全部（都带条数）。
    /// </summary>
    private void DetailScope_Click(object sender, RoutedEventArgs e)
    {
        var pager = _pager;
        if (pager == null) return;

        var menu = new ContextMenu
        {
            PlacementTarget = DetailScopeBtn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        void Add(string header, IReadOnlyList<CleanItem> scope, bool on)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) =>
            {
                pager.Select(scope, on);
                RefreshAfterSelectionChange();
                BindDetailPage();
            };
            menu.Items.Add(mi);
        }
        Add(Loc.SelectCurrentPageCount(pager.SelectableOnPage.Count), pager.SelectableOnPage, true);
        Add(Loc.SelectAllInSearchCount(pager.SelectableMatches.Count), pager.SelectableMatches, true);
        Add(Loc.SelectWholeGroupCount(pager.SelectableAll.Count), pager.SelectableAll, true);
        menu.Items.Add(new Separator());
        Add(Loc.DeselectAllCount(pager.SelectableAll.Count), pager.SelectableAll, false);
        menu.IsOpen = true;
    }

    // ---------------- 勾选 / 汇总 ----------------

    /// <summary>
    /// 勾选框点击。TwoWay 绑定已经把新值写进节点（会只作用于可删候选），
    /// 这里负责刷新派生显示与全局总计。
    /// </summary>
    private void LayerCheck_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CleanGroupNodeBase node) return;
        node.SyncFromItems();
        RefreshAfterSelectionChange();
    }

    /// <summary>勾选变化后刷新：明细、各层三态、底部总计。不做全量遍历。</summary>
    private void RefreshAfterSelectionChange()
    {
        // 就地展开的分类/位置也同步三态（实例本来就同一个，这里只保证显示一致）
        foreach (var p in _layered.Purposes)
        {
            p.SyncFromItems();
            if (!p.IsExpanded) continue;
            foreach (var l in p.Locations) l.SyncFromItems();
        }
        foreach (var s in _sections) s.RaiseHeaderChanged();
        if (_pager != null) CleanGrid.Items.Refresh();
        UpdateSelectionUi();
        // 范围提示里的「范围外还有 N 个已选」要跟着选择变
        UpdateScopeChip();
    }

    private void CleanGrid_Click(object sender, MouseButtonEventArgs e) => RefreshAfterSelectionChange();

    /// <summary>
    /// 底部操作栏：左侧已选范围，右侧次要 + 主要操作。
    /// 数字口径：位置数 / 候选项数 / 预计处理空间，三者含义各自写清。
    /// </summary>
    private void UpdateSelectionUi()
    {
        if (CleanSelectionSummary == null) return;
        bool hasReport = _report != null;
        var picked = hasReport ? _layered.SelectedItems.ToList() : new List<CleanItem>();
        // 「全选」的作用域是**全局可清理候选**（和旧「清空选择」一致，不是当前页），
        // 所以按钮状态也按全局判断：只要有可清理项就有意义，一个没勾时也能点。
        int selectableTotal = hasReport ? _layered.AllItems.Count(x => x.CanDelete) : 0;
        bool allSelected = selectableTotal > 0 && picked.Count >= selectableTotal;

        // 执行中 / 刚完成时，主按钮由执行状态驱动，不让选择刷新把它改回去
        if (_cleanAction != CleanActionState.Idle)
        {
            if (_cleanAction == CleanActionState.Done)
            {
                CleanSelectionSummary.Text = Loc.DoneItems(_lastDoneOk, _lastDoneFailed);
                CleanSelectionNote.Text = "";
                ApplySelectAllButton(allSelected: false, enabled: false);
                CheckAndCleanBtn.IsEnabled = true;
            }
            return;
        }

        if (picked.Count == 0)
        {
            CleanSelectionSummary.Text = hasReport ? Loc.NothingSelectedYet : Loc.AnalyzeAfterScan;
            CleanSelectionNote.Text = "";
            ViewSelectedBtn.Visibility = Visibility.Collapsed;
            CheckAndCleanBtn.IsEnabled = false;
            // 没有可清理项才禁用；有候选但一个没勾时，「全选」正是用户要点的。
            ApplySelectAllButton(allSelected: false, enabled: selectableTotal > 0);
        }
        else
        {
            long bytes = 0;
            foreach (var x in picked) bytes += Math.Max(0, x.Size);
            int locations = CountSelectedLocations(picked);
            CleanSelectionSummary.Text = Loc.SelectionSummary(locations, picked.Count, FileEntry.FormatSize(bytes));
            // 跨分类/跨位置保留选择时，明确说明这是全局选择
            CleanSelectionNote.Text = _openLocation != null ? Loc.GlobalSelectionNote : "";
            ViewSelectedBtn.Visibility = Visibility.Visible;
            CheckAndCleanBtn.Content = Loc.CleanSelectedItems;
            CheckAndCleanBtn.IsEnabled = true;
            ApplySelectAllButton(allSelected, enabled: true);
            // 估算口径改成悬停提示，不再单独占一整行
            CleanSelectionSummary.ToolTip = Loc.EstimateTip;
            CheckAndCleanBtn.ToolTip = Loc.NeedsConfirmTip;
        }
        UpdateViewSelectedLabel();
        UpdateDetailScopeButton();
    }

    /// <summary>
    /// 底部「全选」切换按钮的外观与状态。
    ///
    /// 文字**恒为** <see cref="Loc.SelectAll"/>（全选 / Select all），绝不改成「取消全选」；
    /// 是否已全选只体现在样式（幽灵 / 主按钮）和提示文案上。
    /// 可访问名称也保持为同一个「全选」，这样屏幕阅读器读到的是稳定的按钮名。
    /// </summary>
    private void ApplySelectAllButton(bool allSelected, bool enabled)
    {
        if (CleanSelectAllBtn == null) return;
        CleanSelectAllBtn.Content = Loc.SelectAll;
        CleanSelectAllBtn.IsEnabled = enabled;
        CleanSelectAllBtn.ToolTip = allSelected ? Loc.SelectAllClearTip : Loc.SelectAllActionTip;
        System.Windows.Automation.AutomationProperties.SetName(CleanSelectAllBtn, Loc.SelectAll);
        var style = (Style)FindResource(allSelected ? "PrimaryButton" : "GhostButton");
        if (!ReferenceEquals(CleanSelectAllBtn.Style, style)) CleanSelectAllBtn.Style = style;
    }

    private void UpdateDetailScopeButton()
    {
        if (DetailScopeBtn == null) return;
        DetailScopeBtn.Content = _pager == null
            ? Loc.GroupSelectionTitle
            : Loc.GroupSelectionTitle + "（" + Loc.SelectedRatio(_pager.SelectableAll.Count(x => x.Selected),
                _pager.SelectableAll.Count) + "）";
    }

    /// <summary>
    /// 已选集合涉及多少个清理位置（按稳定键去重，风险拆组不重复计）。
    /// 与清理前检查页共用同一个纯函数，避免两处口径跑偏。
    /// </summary>
    private int CountSelectedLocations(List<CleanItem> picked)
        => CleanPreflight.Build(picked, _layered).Locations;

    /// <summary>
    /// 底部「全选」切换：范围是**全局可清理候选**（和旧「清空选择」同一口径，不是当前页）。
    /// <list type="bullet">
    /// <item>还没全选（一个没勾 / 部分勾选）：一次性勾上所有 <c>CanDelete</c> 项；</item>
    /// <item>已全选：再点一次取消全部勾选。</item>
    /// </list>
    /// 按钮文字恒为「全选」，只靠外观与提示区分当前处在哪一态。
    /// </summary>
    private void CleanSelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool allSelected = IsAllSelectableSelected();
        foreach (var item in _layered.AllItems)
        {
            if (!item.CanDelete) continue;
            item.Selected = !allSelected;
        }
        RefreshAfterSelectionChange();
    }

    /// <summary>全局可清理项是否已经全部勾上（一个可清理项都没有时返回 false）。</summary>
    private bool IsAllSelectableSelected()
    {
        bool any = false;
        foreach (var item in _layered.AllItems)
        {
            if (!item.CanDelete) continue;
            any = true;
            if (!item.Selected) return false;
        }
        return any;
    }

    // ---------------- 清理前检查页 ----------------

    /// <summary>
    /// 「清理已选项目」→ **进入清理前检查页**，不直接删。
    /// 只有用户在那一页点「确认清理」，才会走原有的删除预检与执行链路。
    /// </summary>
    private void CheckAndClean_Click(object sender, RoutedEventArgs e)
    {
        // 清理刚结束时，这个按钮变成「查看结果」
        if (_cleanAction == CleanActionState.Done) { ViewResults_Click(sender, e); return; }

        var picked = _layered?.SelectedItems
            .Where(x => x.CanDelete && !string.IsNullOrEmpty(x.FullPath))
            .ToList() ?? new List<CleanItem>();
        if (picked.Count == 0)
        {
            SetStatus(Loc.NothingToPreflight);
            return;
        }
        ShowPreflightPage(picked);
    }

    /// <summary>清理前检查页：显示将处理的范围，并说明程序还会再检查什么。</summary>
    private void ShowPreflightPage(List<CleanItem> picked)
    {
        _pendingClean = picked;
        _onPreflightPage = true;

        // 数字由纯函数算（可测）：只统计真正会处理的项，位置数按稳定键去重
        var facts = CleanPreflight.Build(picked, _layered);

        PreflightTitle.Text = Loc.PreflightTitle;
        PreflightSub.Text = Loc.PreflightSub;

        // 事实清单：位置数 / 候选项数 / 预计空间 / 需确认数
        PreflightFacts.Children.Clear();
        AddFact(Loc.PreflightLocations(facts.Locations), "Text");
        AddFact(Loc.PreflightItems(facts.Items), "Text");
        AddFact(Loc.PreflightSize(facts.Size), "Text");
        AddFact(facts.NeedsConfirm > 0
                ? Loc.PreflightNeedsConfirm(facts.NeedsConfirm)
                : Loc.PreflightNoConfirm,
            facts.NeedsConfirm > 0 ? "Text" : "TextDim");

        PreflightRecheck.Text = Loc.PreflightRecheck;
        PreflightBackBtn.Content = Loc.BackToEdit;
        PreflightConfirmBtn.Content = Loc.ConfirmClean;
        PreflightConfirmBtn.IsEnabled = !facts.HasNothing;

        // 主区域切到检查页；清理列表与明细都让位，避免同屏多套操作入口
        PurposePage.Visibility = Visibility.Collapsed;
        PreflightPage.Visibility = Visibility.Visible;
        CloseDetail();
        UpdatePageHeader();
    }

    /// <summary>事实清单的一行（键值风格，数字右对齐）。</summary>
    private void AddFact(string text, string brushKey)
    {
        PreflightFacts.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(brushKey == "Text" ? "Text" : "TextDim"),
        });
    }

    private void PreflightBack_Click(object sender, RoutedEventArgs e)
    {
        _pendingClean = null;
        _onPreflightPage = false;
        PreflightPage.Visibility = Visibility.Collapsed;
        // 回到首页：选择、就地展开状态、滚动位置都不动。
        ShowPurposePage(restoreScroll: true);
    }

    /// <summary>确认清理：回到**既有**的删除预检 → 确认 → 执行链路。</summary>
    private void PreflightConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingClean == null || _pendingClean.Count == 0)
        {
            SetStatus(Loc.NothingToPreflight);
            return;
        }
        var picked = _pendingClean;
        _pendingClean = null;
        _onPreflightPage = false;
        PreflightPage.Visibility = Visibility.Collapsed;
        ShowPurposePage(restoreScroll: true);

        // 复用原有链路：DeletionCoordinator.Plan → 确认框 → Execute → 逐项结果
        RunCleanFor(picked);
    }

    // ---------------- 查看已选（结构化清单） ----------------

    /// <summary>「查看已选」清单的一行：一个位置 / 名称 + 它贡献的候选项数与空间。</summary>
    public sealed class SelectedRow
    {
        public string Purpose { get; init; } = "";
        public string Label { get; init; } = "";
        /// <summary>完整路径（悬停与复制用；长路径只在列里截断显示）。</summary>
        public string FullPath { get; init; } = "";
        /// <summary>计数值 + 单位。口径是**候选项**，不是文件数，也不与位置数混用。</summary>
        public string ItemCountText { get; init; } = "";
        public string SizeText { get; init; } = "";
    }

    /// <summary>底部「查看已选」按钮上的数量：与摘要同口径（候选项数）。</summary>
    private void UpdateViewSelectedLabel()
    {
        if (ViewSelectedText == null) return;
        ViewSelectedText.Text = Loc.ViewSelected;
        int n = _layered?.SelectedItems.Count() ?? 0;
        ViewSelectedCount.Text = n > 0 ? $"（{n:N0}）" : "";
        ViewSelectedBtn.IsEnabled = n > 0;
        ViewSelectedBtn.ToolTip = n > 0
            ? Loc.ViewSelectedTip(n)
            : Loc.NothingSelectedYet;
    }

    /// <summary>
    /// 「查看已选」：结构化清单，替代原先一大段文字。
    /// **只读** —— 打开/关闭都不会改动选择；复制路径只走剪贴板。
    /// </summary>
    private void ViewSelected_Click(object sender, RoutedEventArgs e)
    {
        var picked = _layered?.SelectedItems.ToList() ?? new List<CleanItem>();
        if (picked.Count == 0)
        {
            ShowAlert(Loc.ViewSelectedTitle, Loc.NothingSelectedYet);
            return;
        }

        long bytes = 0;
        foreach (var x in picked) bytes += Math.Max(0, x.Size);
        int locations = CountSelectedLocations(picked);

        // 口径写清楚：位置数 / 候选项数 / 预计空间各自带量词
        SelectionSummaryText.Text = Loc.SelectionSummary(locations, picked.Count,
            FileEntry.FormatSize(bytes));
        SelectionSubText.Text = Loc.ViewSelectedSub;

        // 按「位置」聚合：一部分候选可能不属于任何位置（例如被截断没建行的位置），
        // 那就退回按用途聚合，保证看到的汇总与真实选择一致、不丢数。
        var rows = new List<SelectedRow>();
        var inLocation = new HashSet<CleanItem>();
        if (_layered != null)
        {
            foreach (var p in _layered.Purposes)
            {
                foreach (var loc in p.Locations)
                {
                    var mine = loc.Items.Where(x => x.Selected && x.CanDelete).ToList();
                    if (mine.Count == 0) continue;
                    foreach (var m in mine) inLocation.Add(m);
                    rows.Add(new SelectedRow
                    {
                        Purpose = p.PurposeName,
                        Label = loc.DisplayName,
                        FullPath = loc.Path,
                        ItemCountText = mine.Count.ToString("N0"),
                        SizeText = FileEntry.FormatSize(mine.Sum(x => x.Size)),
                    });
                }
            }
        }
        // 没归到位置的已选项：按用途归一行，避免「清单里看不到但确实会被处理」
        foreach (var g in picked.Where(x => !inLocation.Contains(x))
                     .GroupBy(x => x.Purpose)
                     .OrderByDescending(g => g.Sum(x => x.Size)))
        {
            rows.Add(new SelectedRow
            {
                Purpose = CleanPurposes.Name(g.Key),
                Label = Loc.OtherSelected,
                FullPath = "",
                ItemCountText = g.Count().ToString("N0"),
                SizeText = FileEntry.FormatSize(g.Sum(x => x.Size)),
            });
        }

        SelectionGrid.ItemsSource = rows
            .OrderByDescending(r => r.SizeText.Length)
            .ThenBy(r => r.Purpose, StringComparer.CurrentCulture)
            .ToList();

        OpenOverlay(Loc.ViewSelectedTitle, selection: true);
    }

    private void SelectionCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SelectedRow row) return;
        if (string.IsNullOrEmpty(row.FullPath)) return;
        CopyToClipboard(row.FullPath);
        SetStatus(Loc.PathCopied);
    }

    // ---------------- 更多菜单 / 扫描详情 ----------------

    /// <summary>
    /// 「更多」菜单。**不再提供整盘/整类的 AI 分析入口** ——
    /// AI 现在按需挂在具体项目旁边（位置行、文件行上的 AI 按钮），
    /// 这里只留扫描详情等不涉及模型调用的项。
    /// </summary>
    private void CleanMore_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = CleanMoreBtn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        var details = new MenuItem { Header = Loc.ScanDetails };
        details.Click += (_, _) => ScanDetails_Click(this, new RoutedEventArgs());
        menu.Items.Add(details);

        // 批量归类：只处理「规则没给出用途」的那批。数量写进标题，用户一眼知道值不值得点。
        int pending = _report == null ? 0 : AllCandidates().Count(AiPurposeBatchService.IsEligible);
        var classify = new MenuItem
        {
            Header = pending > 0 ? Loc.AiBatchClassifyWithCount(pending) : Loc.AiBatchClassify,
            IsEnabled = pending > 0 && !_aiBusy,
        };
        classify.Click += (_, _) => CleanClassifyPurposes_Click(this, new RoutedEventArgs());
        menu.Items.Add(classify);

        var hint = new MenuItem
        {
            Header = Loc.AiPerItemHint,
            IsEnabled = false,
        };
        menu.Items.Add(hint);
        menu.IsOpen = true;
    }

    /// <summary>
    /// 批量用途归类：把「规则没给出用途」的那批一次性交给结构化判定通道。
    ///
    /// 和逐项 AI 同一条规矩：**只由用户主动发起**。扫描、切页、展开、筛选都不会走到这里，
    /// 所以「这些动作 = 0 次模型请求」那条回归断言照样成立（计数在 AiGateway 里）。
    /// </summary>
    private async void CleanClassifyPurposes_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy || _root == null) return;

        var targets = AllCandidates().Where(AiPurposeBatchService.IsEligible).ToList();
        if (targets.Count == 0)
        {
            ShowAlert(Loc.AiBatchClassify, Loc.AiPurposeBatchNothing);
            return;
        }
        if (!App.Settings.DecisionConfigured())
        {
            ShowAlert(Loc.AiBatchClassify, Loc.AiPurposeBatchNotConfigured);
            return;
        }

        _aiBusy = true;
        RefreshAiLamp();
        SetAiStatus(Loc.AiPurposeBatchRunning);
        using var op = StartOperation(ref _aiClassifyCts, "AiClassify");
        var ct = op.Token;
        var progress = new Progress<string>(SetAiStatus);
        try
        {
            var outcome = await AiPurposeBatchService.RunAsync(targets, progress, ct);

            // Purpose 不是 INPC、分层是预计算的：改完用途必须重建，否则界面还按旧用途分组。
            // invalidateItemAi: false —— 扫描数据没变，不能把用户跑过的逐项分析标成过期。
            await RebuildLayersAsync(_root, null, "ai-classify", invalidateItemAi: false);
            ShowPurposePage(restoreScroll: true);

            SetAiStatus(Loc.AiPurposeBatchSummary(
                outcome.Applied, outcome.Hinted, outcome.Unknown, outcome.Calls, outcome.Cost));
            op.Done("classify", outcome.Applied);
        }
        catch (OperationCanceledException)
        {
            SetAiStatus(Loc.Aborted);
            op.Canceled("classify canceled");
        }
        catch (Exception ex)
        {
            SetAiStatus(Loc.AiPurposeBatchFailed(AppError.From(ex, "classify").UserMessage));
            SetAiLamp(false);
            op.Fail(ex, "classify");
        }
        finally
        {
            _aiBusy = false;
            RefreshAiLamp();
        }
    }

    /// <summary>扫描诊断：默认只在页头留一行，细节进这个对话框。数据一条不少。</summary>
    private void ScanDetails_Click(object sender, RoutedEventArgs e)
        => ShowAlert(Loc.ScanDetailsTitle, BuildScanDetailsText());

    private string BuildScanDetailsText()
    {
        var q = _scanQuality;
        if (q == null) return Loc.NoScanYet;
        var sb = new System.Text.StringBuilder();
        string source = q.Source switch
        {
            ScanSource.Mft => Loc.ScanSourceMft,
            ScanSource.Recursive => Loc.ScanSourceRecursive,
            _ => Loc.ScanSourceUnknown,
        };
        sb.AppendLine(Loc.ScanSourceLine(source));
        // 扫描耗时（原先在全局底栏）与文件/目录数都收进这里
        sb.AppendLine(Loc.ScanElapsedLine(_scanElapsed > 0 ? _scanElapsed : q.DurationSeconds));
        sb.AppendLine(Loc.ScanDurationLine(q.DurationSeconds));
        sb.AppendLine(Loc.ScanCountLine(q.FilesRead, q.DirsRead));
        sb.AppendLine(Loc.ScanSkipLine(q.SkippedDirs, q.PermissionErrors, q.PathErrors, q.ReadErrors));
        sb.AppendLine(Loc.ScanReparseLine(q.ReparsePoints));
        sb.AppendLine(Loc.ScanRecordLine(q.UnparsedRecords, q.OrphanRecords, q.HardLinks));
        // 候选统计（原先在清理页次级信息行）：总数、去重、重复命中、覆盖完整度
        sb.AppendLine();
        sb.AppendLine(Loc.CandidateTotalLine(_layered.TotalFiles, _report?.DupGroupCount ?? 0));
        sb.AppendLine(Loc.CandidateDedupLine(_layered.OverlapCount, _layered.DuplicateHitsRemoved));
        sb.AppendLine(_dupIncomplete > 0 ? Loc.DupNotFinished : Loc.DupFinished);
        if (_cleanScopeRoot != null)
            sb.AppendLine(Loc.ScopeChip(_cleanScopeRoot.FullPath ?? "", 0));
        sb.AppendLine();
        sb.AppendLine(q.Complete ? Loc.ScanCompleteLine : Loc.ScanIncompleteLine);
        if (!string.IsNullOrWhiteSpace(q.Note)) sb.AppendLine(q.Note);
        if (!string.IsNullOrWhiteSpace(_scanFallbackReason))
        {
            sb.AppendLine();
            sb.AppendLine(Loc.MftFallbackStarting(_scanFallbackReason));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 页头一行扫描状态。**不完整绝不伪装成成功** ——
    /// 只要有跳过/权限/路径错误或取消，就明确写「部分内容未检测」。
    /// </summary>
    private void UpdateScanStateLine()
    {
        if (CleanScanState == null) return;
        var q = _scanQuality;
        if (q == null) { CleanScanState.Text = ""; return; }
        bool incomplete = !q.Complete || q.HasSkips || _dupIncomplete > 0;
        // 成功的扫描不再反复显示「扫描完成」——那是每次都在说的废话。
        // 只有**异常**（覆盖不完整 / 用户取消）才留一句短警告入口，可展开看原因。
        string text = q.Canceled ? Loc.ScanStoppedShort
            : incomplete ? Loc.ScanPartialShort
            : "";
        CleanScanState.Text = text;
        CleanScanState.Foreground = ThemeService.Brush("Accent");
        CleanScanState.ToolTip = text.Length > 0 ? text : null;
        CleanScanState.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 页头：**返回一个箭头、标题只显示一次**。
    /// 不再把「清理中心 / 临时文件」面包屑和「临时文件」大标题并排重复。
    /// 第二行用「候选空间」——它是这一页所有候选的估算总量，不是用户已选的量。
    /// </summary>
    private void UpdatePageHeader()
    {
        if (CleanPageTitle == null) return;

        if (_onPreflightPage)
        {
            CleanPageTitle.Text = Loc.PreflightTitle;
            CleanPageSub.Text = Loc.PreflightSub;
            CleanBackBtn.ToolTip = Loc.BackToCleanCenter;
            CleanBackBtn.Visibility = Visibility.Visible;
            return;
        }

        if (_openLocation != null && _detailIsOverlay)
        {
            // 文件明细（窄窗口整页）：标题是位置名，返回回到首页
            CleanPageTitle.Text = _openLocation.DisplayName;
            CleanPageSub.Text = Loc.DetailScopeCount(_openLocation.FileCount, _openLocation.SizeText);
            CleanBackBtn.ToolTip = Loc.BackToCleanCenter;
            CleanBackBtn.Visibility = Visibility.Visible;
            return;
        }

        // 首页：导航项已经写着「清理中心」，标题不再重复一遍（§二）。
        // 第二行给出这一页的候选规模 —— 用「候选空间」而不是「预计处理」，
        // 因为用户还没选任何东西。
        CleanPageTitle.Text = "";
        CleanPageSub.Text = _layered.TotalLocations > 0
            ? Loc.CandidateScopeLine(_layered.TotalLocations, FileSizeText(_layered.TotalBytes))
            : "";
        CleanBackBtn.Visibility = Visibility.Collapsed;
        CleanBackBtn.ToolTip = Loc.BackToCleanCenter;
    }

    /// <summary>候选空间文案（估算口径）。</summary>
    static string FileSizeText(long bytes)
        => bytes > 0 ? Models.FileEntry.FormatSize(bytes) : Loc.EstUnknown;

    /// <summary>
    /// 候选项统计 / 去重 / 重复命中 / 完整扫描统计**都不在主页面显示**，全部进「扫描详情」（§五.4/§五.5）。
    /// 估算口径也不再单独占一行，改成底部摘要的悬停提示（§五.3）。
    /// 这里只负责把这行清空，让位给真正的一次性状态（AI 状态、删除结果等）。
    /// </summary>
    private void UpdateCandidateSummary()
    {
        if (CleanSummarySub == null) return;
        if (_aiTransientStatus.Length == 0) CleanSummarySub.Text = "";
    }

    /// <summary>重复检测没跑完的候选数（0 表示跑完了）。</summary>
    private int _dupIncomplete;
    /// <summary>正在显示清理前检查页。</summary>
    private bool _onPreflightPage;
    /// <summary>清理前检查页待确认的集合（进入执行后清空）。</summary>
    private List<CleanItem>? _pendingClean;
    /// <summary>上一次清理的逐项结果文本（「查看结果」用）。</summary>
    private string _lastResultText = "";

    private enum CleanStateKind { None, Scanning, Analyzing, Duplicates, Empty, Canceled, Failed, Partial }

    /// <summary>状态区：空 / 进行中 / 部分完成 / 取消 / 失败，都要有明确短句。</summary>
    private void ShowCleanState(CleanStateKind kind)
    {
        if (CleanStateBox == null) return;
        (string title, string body) = kind switch
        {
            CleanStateKind.Scanning => (Loc.StateScanning, ""),
            CleanStateKind.Analyzing => (Loc.StateAnalyzing, ""),
            CleanStateKind.Duplicates => (Loc.StateDup, ""),
            CleanStateKind.Empty => (Loc.EmptyAfterScan, ""),
            CleanStateKind.Canceled => (Loc.StateCanceledTitle, Loc.StateCanceledBody),
            CleanStateKind.Failed => (Loc.StateFailedTitle, Loc.CleanStateFailedBody),
            CleanStateKind.Partial => (Loc.StatePartialTitle, Loc.CleanStatePartialBody),
            _ => ("", ""),
        };
        bool show = title.Length > 0;
        CleanStateBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CleanStateTitle.Text = title;
        CleanStateBody.Text = body;
        CleanStateBody.Visibility = body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- 行容器审计（虚拟化是否真的生效） ----------------

    private void ScheduleRowContainerAudit(DependencyObject root, int candidateCount, string tag)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                int rows = CountRowContainers(root);
                _lastRowContainerCount = rows;
                bool suspicious = candidateCount > 300 && rows > candidateCount / 2;
                string line = $"{tag} rows created={rows} candidates={candidateCount}";
                if (suspicious) AppLog.Warn("Perf", "row virtualisation looks INACTIVE: " + line);
                else AppLog.Info("Perf", line);
            }
            catch (Exception ex)
            {
                AppLog.Record("Perf", ex, "row container audit " + tag);
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static int CountRowContainers(DependencyObject root)
    {
        int n = 0;
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(cur);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(cur, i);
                if (child is DataGridRow || child is ListBoxItem) n++;
                stack.Push(child);
            }
        }
        return n;
    }

    /// <summary>参与清理的全部候选条目（完整，不截断）。</summary>
    IEnumerable<CleanItem> AllCandidates()
    {
        foreach (var x in _report!.Cleanable) yield return x;
        foreach (var x in _report.LargeFiles) yield return x;
        foreach (var x in _report.OldFiles) yield return x;
        foreach (var x in _report.Duplicates) yield return x;
        foreach (var x in _report.EmptyFolders) yield return x;
        foreach (var x in _report.BrokenShortcuts) yield return x;
        foreach (var x in _report.LongPaths) yield return x;
    }

    static T? Ancestor<T>(DependencyObject start) where T : DependencyObject
    {
        var cur = start;
        while (cur != null)
        {
            if (cur is T hit) return hit;
            cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
        }
        return null;
    }

    /// <summary>
    /// 分层视图下不再维护「按目录过滤的扁平缓存」，保留空实现让调用点不用改。
    /// </summary>
    private void InvalidateFilteredCache()
    {
    }

    bool InCurrentFolder(FileEntry? dir)
        => dir != null && _root != null && !ReferenceEquals(dir, _root) && !string.IsNullOrEmpty(dir.FullPath);

    static string FolderPrefix(FileEntry dir) => CleanListSnapshot.FolderPrefix(dir.FullPath);

    static bool UnderPrefix(string? path, string prefix) => CleanListSnapshot.UnderPrefix(path, prefix);

    static string NormPath(string? p) => CleanListSnapshot.NormPath(p);

    private CheckBox? _pickAllBox;

    /// <summary>
    /// 明细表头复选框：只作用于**当前页**（文案已说明范围），
    /// 不会悄悄扩大成整组或整个搜索结果。
    /// </summary>
    private void PickAll_Click(object sender, RoutedEventArgs e)
    {
        _pickAllBox ??= sender as CheckBox;
        bool on = (sender as CheckBox)?.IsChecked == true;
        var pager = _pager;
        if (pager == null) return;
        pager.Select(pager.SelectableOnPage, on);
        RefreshAfterSelectionChange();
        BindDetailPage();
    }


    /// <summary>
    /// 用刚清点出来的已安装软件清单，**建立一次** <see cref="InstalledLocationSnapshot"/>，
    /// 并把它叠到启动时建好的**同一个** <see cref="LocalEvidenceService"/> 上（系统快照沿用）。
    /// 只在内存里做，不逐条扫注册表/磁盘。证据建不出来只影响「已安装目录」识别，不打断卸载列表。
    /// </summary>
    private void InjectInstalledEvidence(List<AppUninstallItem> apps)
    {
        try
        {
            var snapshot = InstalledLocationSnapshot.Build(
                apps.Select(a => new InstalledAppLocation(a.Name, a.InstallLocation)));
            _localEvidence = _localEvidence.WithInstalled(snapshot);
            _folderPurpose.Evidence = _localEvidence;
            AppLog.Info("Evidence", $"op=installed-snapshot apps={snapshot.AppCount} "
                + $"usable={snapshot.UsableLocationCount} skipped={snapshot.SkippedCount}");
        }
        catch (Exception ex)
        {
            AppLog.Record("Evidence", ex, "inject installed evidence");
        }
    }

    private async Task LoadApps()
    {
        if (_listingApps || _aiAppsBusy) return;
        // 软件清点 + 占用计算有自己的 CTS 和代次：卸载后重扫时旧结果不会覆盖新的。
        using var op = StartOperation(ref _uninstallCts, "Uninstall");
        var ct = op.Token;
        int myGeneration = ++_uninstallGeneration;
        _listingApps = true;
        UninstallProgressPanel.Visibility = Visibility.Visible;
        UninstallProgressBar.IsIndeterminate = true;
        UninstallProgressBar.Value = 0;
        UninstallProgressText.Text = Loc.UninstallListing;
        UninstallSummary.Text = Loc.UninstallListing;
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                UninstallProgressBar.IsIndeterminate = p.Percent < 0;
                if (p.Percent >= 0) UninstallProgressBar.Value = p.Percent;
                UninstallProgressText.Text = p.CurrentDirectory;
            });
            var list = await Task.Run(() => BcuUninstallService.ListApps(progress, ct), ct);
            var files = _allFiles;
            var usage = await Task.Run(() => AppRecommendationService.CalculateUsage(list, files, ct), ct);
            if (myGeneration != _uninstallGeneration)
            {
                op.Note("stale app inventory dropped");
                return;
            }
            AppRecommendationService.ApplyUsage(usage);
            // 只讲事实：这是什么软件 + 有效占用。没有建议、没有评分、没有 AI。
            AppFactualInfoService.Apply(list);
            InjectInstalledEvidence(list);
            foreach (var app in list)
            {
                try { app.Icon = BcuUninstallService.ToImage(app.IconBytes); }
                catch (Exception ex) { app.Icon = null; AppLog.Record("Uninstall", ex, "icon decode"); }
                app.IconBytes = null;
            }
            _apps = list;
            _appInventoryVersion++;
            _junk.Clear();
            ShowAppList();
            op.Done("apps listed", list.Count);
        }
        catch (OperationCanceledException)
        {
            op.Canceled("app listing canceled");
        }
        catch (Exception ex)
        {
            op.Fail(ex, "list apps");
            ShowAlert(Loc.TabUninstall, AppError.From(ex, "list apps").UserMessage);
        }
        finally
        {
            if (myGeneration == _uninstallGeneration)
            {
                _listingApps = false;
                UninstallProgressPanel.Visibility = Visibility.Collapsed;
                UninstallProgressBar.IsIndeterminate = false;
            }
        }
    }

    private void UninstallGrid_Click(object sender, MouseButtonEventArgs e) => UpdateUninstallSelHint();

    private void UninstallGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UninstallGrid.SelectedItem is not AppUninstallItem app) return;
        OpenFolder(app.InstallLocation);
    }

    AppUninstallItem? SelectedApp() => UninstallGrid.SelectedItem as AppUninstallItem;

    private void UninstallOpenOfficial_Click(object sender, RoutedEventArgs e)
    {
        // 行内按钮通过 DataContext 给出这一项；右键菜单走当前选中项。
        var app = (sender as FrameworkElement)?.DataContext as AppUninstallItem ?? SelectedApp();
        if (app is not { Entry: not null }) return;
        try { UninstallManager.RunUninstaller(app.Entry); }
        catch (Exception ex) { ShowAlert(Loc.TabUninstall, ex.Message); }
    }

    /// <summary>打开这一项的安装目录（只打开目录，不执行里面的程序）。</summary>
    private void UninstallReveal_Click(object sender, RoutedEventArgs e)
    {
        var app = (sender as FrameworkElement)?.DataContext as AppUninstallItem ?? SelectedApp();
        if (app == null) return;
        if (!string.IsNullOrWhiteSpace(app.InstallLocation)) { OpenFolder(app.InstallLocation); return; }
        if (app.Entry?.InstallLocation != null) { OpenFolder(app.Entry.InstallLocation); return; }
        SetStatus(Loc.UninstallProtectedNote);
    }

    private void UninstallRetry_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp() is not { } app || !app.CanUninstall) return;
        RunUninstall(new List<AppUninstallItem> { app });
    }

    private void JunkGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (JunkGrid.SelectedItem is not JunkItem junk) return;
        OpenFolder(junk.Path);
    }

    static void OpenFolder(string? path)
    {
        // 统一走 ShellReveal：目录打开、文件选中、不存在时给原因
        ShellReveal.Reveal(path);
    }

    private void UpdateUninstallSelHint()
    {
        var picked = _apps.Where(x => x.Selected && x.CanUninstall).ToList();
        UninstallSelHint.Text = picked.Count == 0 ? "" : Loc.UninstallCount(picked.Count);
    }

    private void UninstallRun_Click(object sender, RoutedEventArgs e)
    {
        var picked = _apps.Where(x => x.Selected && x.CanUninstall).ToList();
        if (picked.Count == 0)
        {
            ShowAlert(Loc.TabUninstall, Loc.NothingSelected);
            return;
        }
        // 有效占用（实测优先，其次安装记录）——只说数字，不带任何建议口径。
        long bytes = picked.Sum(x => x.EffectiveFootprintBytes > 0
            ? x.EffectiveFootprintBytes
            : x.ActualSizeBytes > 0 ? x.ActualSizeBytes : x.SizeBytes);
        var names = picked.Select(x => "- " + x.Name).Take(20);
        string msg = Loc.UninstallConfirmDetails(names, picked.Count, FileEntry.FormatSize(bytes));
        AskConfirm(Loc.TabUninstall, msg, () => RunUninstall(picked));
    }

    private void RunUninstall(List<AppUninstallItem> picked)
    {
        try
        {
            picked = picked
                .Where(x => x.Entry != null
                    && x.CanUninstall
                    && x.Entry.UninstallPossible
                    && !x.Entry.IsProtected
                    && !x.Entry.SystemComponent
                    && x.Entry.UninstallerKind != UninstallerType.WindowsFeature)
                .ToList();
            if (picked.Count == 0)
            {
                ShowAlert(Loc.TabUninstall, Loc.NothingSelected);
                return;
            }
            _uninstallTask?.Dispose();
            _handledUninstallTask = null;
            _uninstallResultNote = "";
            _uninstallTask = BcuUninstallService.StartUninstall(picked);
            UninstallProgressPanel.Visibility = Visibility.Visible;
            UninstallProgressBar.IsIndeterminate = true;
            UninstallProgressText.Text = Loc.UninstallRunning;
            UninstallRunBtn.IsEnabled = false;
            _uninstallTask.OnStatusChanged += (_, _) => Dispatcher.BeginInvoke(RefreshUninstallTask);
        }
        catch (Exception ex)
        {
            ShowAlert(Loc.TabUninstall, ex.Message);
        }
    }

    private void RefreshUninstallTask()
    {
        var task = _uninstallTask;
        if (task == null) return;
        if (task.Finished && ReferenceEquals(_handledUninstallTask, task)) return;
        var byEntry = task.AllUninstallersList.ToDictionary(x => x.UninstallerEntry);
        foreach (var app in _apps)
        {
            if (app.Entry == null || !byEntry.TryGetValue(app.Entry, out var row)) continue;
            app.Status = row.CurrentStatus switch
            {
                UninstallStatus.Waiting => Loc.UninstallWaiting,
                UninstallStatus.Uninstalling => Loc.UninstallRunning,
                UninstallStatus.Completed => Loc.UninstallDone,
                UninstallStatus.Failed => Loc.UninstallFailed,
                UninstallStatus.Protected => Loc.UninstallProtected,
                UninstallStatus.Skipped => Loc.UninstallSkipped,
                _ => app.Status,
            };
        }
        int done = task.AllUninstallersList.Count(x => x.Finished);
        int total = task.AllUninstallersList.Count;
        UninstallProgressBar.IsIndeterminate = false;
        UninstallProgressBar.Value = total == 0 ? 0 : done * 100.0 / total;
        UninstallProgressText.Text = Loc.UninstallRunning + $" {done}/{total}";
        if (!task.Finished) return;
        _handledUninstallTask = task;
        UninstallRunBtn.IsEnabled = true;
        int ok = task.AllUninstallersList.Count(x => x.CurrentStatus == UninstallStatus.Completed);
        int fail = task.AllUninstallersList.Count(x => x.CurrentStatus == UninstallStatus.Failed);
        int skip = task.AllUninstallersList.Count(x => x.CurrentStatus is UninstallStatus.Skipped or UninstallStatus.Protected);
        long freed = _apps
            .Where(x => x.Status == Loc.UninstallDone)
            .Sum(x => x.ActualSizeBytes > 0 ? x.ActualSizeBytes : x.SizeBytes);
        _uninstallResultNote = BuildUninstallResultNote(task, ok, fail, skip, freed);
        ReportUninstallResults(task, freed);
        SetStatus(fail == 0 ? Loc.UninstallDone : $"{Loc.UninstallDone} {ok}, {Loc.UninstallFailed} {fail}");
        var finished = task.AllUninstallersList
            .Where(x => x.CurrentStatus == UninstallStatus.Completed && x.UninstallerEntry != null)
            .Select(x => x.UninstallerEntry)
            .ToList();
        if (finished.Count > 0) _ = ScanLeftovers(finished);
        else
        {
            UninstallProgressPanel.Visibility = Visibility.Collapsed;
            _ = RefreshAppsOnlyAsync();
        }
    }

    /// <summary>
    /// 把 BCU 的逐项状态翻译成统一的卸载结果并写日志。
    /// 和删除一样：成功/失败/跳过/保留每一项都有记录，不是只报一个总数。
    /// </summary>
    private void ReportUninstallResults(BulkUninstallTask task, long freed)
    {
        var batch = new UninstallBatchResult();
        foreach (var row in task.AllUninstallersList)
        {
            var app = _apps.FirstOrDefault(a => ReferenceEquals(a.Entry, row.UninstallerEntry));
            var outcome = row.CurrentStatus switch
            {
                UninstallStatus.Completed => UninstallOutcome.Uninstalled,
                UninstallStatus.Failed => UninstallOutcome.Failed,
                UninstallStatus.Skipped => UninstallOutcome.SkippedByUser,
                UninstallStatus.Protected => UninstallOutcome.Protected,
                _ => UninstallOutcome.Kept,
            };
            string message = outcome switch
            {
                UninstallOutcome.Failed => "卸载程序报了错，可以右键重试",
                UninstallOutcome.SkippedByUser => "没有执行（用户跳过或卸载程序拒绝）",
                UninstallOutcome.Protected => "本地硬拦截：系统组件/驱动/运行库/安全软件，AI 不能越过",
                UninstallOutcome.Kept => "没有执行，保留",
                _ => "已卸载",
            };
            batch.Results.Add(new UninstallItemResult
            {
                AppId = app?.AppId ?? "",
                Name = (app?.Name ?? row.UninstallerEntry?.DisplayName ?? "?").Trim(),
                Outcome = outcome,
                Message = message,
                Detail = row.CurrentStatus.ToString(),
                FreedBytes = outcome == UninstallOutcome.Uninstalled
                    ? (app?.ActualSizeBytes > 0 ? app.ActualSizeBytes : app?.SizeBytes ?? 0)
                    : 0,
            });
        }

        // 每项一行，便于事后对账「到底哪个没卸掉、为什么」
        foreach (var r in batch.Results)
        {
            AppLog.Write(new LogEntry(DateTime.UtcNow,
                r.Outcome == UninstallOutcome.Uninstalled ? LogLevel.Info : LogLevel.Warn,
                "Uninstall", "", r.OutcomeText, r.Name + " | " + r.Message + " | " + r.Detail));
        }
        AppLog.Info("Uninstall",
            $"batch ok={batch.Uninstalled} fail={batch.Failed} skip={batch.Skipped} protected={batch.Protected} "
            + $"kept={batch.Kept} total={batch.Total} freed={FileEntry.FormatSize(batch.FreedBytes)}");
    }

    static string BuildUninstallResultNote(BulkUninstallTask task, int ok, int fail, int skip, long freed)
    {
        string summary = Loc.UninstallResultSummary(ok, fail, skip, FileEntry.FormatSize(freed));
        var pending = task.AllUninstallersList
            .Where(x => x.CurrentStatus is UninstallStatus.Failed or UninstallStatus.Skipped or UninstallStatus.Protected)
            .Select(x => (x.UninstallerEntry?.DisplayName ?? "?").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(12)
            .ToList();
        if (pending.Count == 0) return summary;
        return summary + Environment.NewLine + Loc.UninstallResultPending + " "
            + string.Join("、", pending);
    }

    private async Task RefreshAppsOnlyAsync()
    {
        using var op = StartOperation(ref _uninstallCts, "Uninstall");
        var ct = op.Token;
        int myGeneration = ++_uninstallGeneration;
        try
        {
            var list = await Task.Run(() => BcuUninstallService.ListApps(null, ct), ct);
            var files = _allFiles;
            var usage = await Task.Run(() => AppRecommendationService.CalculateUsage(list, files, ct), ct);
            if (myGeneration != _uninstallGeneration)
            {
                op.Note("stale app inventory dropped after uninstall");
                return;
            }
            AppRecommendationService.ApplyUsage(usage);
            AppFactualInfoService.Apply(list);
            InjectInstalledEvidence(list);
            foreach (var app in list)
            {
                try { app.Icon = BcuUninstallService.ToImage(app.IconBytes); }
                catch (Exception ex) { app.Icon = null; AppLog.Record("Uninstall", ex, "icon decode"); }
                app.IconBytes = null;
            }
            _apps = list;
            _appInventoryVersion++;
            if (!_showingJunk) ShowAppList();
            // 卸载后：立即刷新顶部可用空间，再后台重扫整盘，让清理面板跟着变。
            UpdateVolumeInfo();
            op.Done("apps refreshed after uninstall", list.Count);
            _ = ReScanAfterUninstallAsync();
        }
        catch (OperationCanceledException)
        {
            op.Canceled("post-uninstall refresh canceled");
        }
        catch (Exception ex)
        {
            op.Fail(ex, "post-uninstall refresh");
        }
    }

    /// <summary>
    /// 卸载后的自动重扫。只在「确实需要」时跑：盘扫过、当前没有别的扫描在进行。
    /// 用独立的 CTS，用户点停止能一起停掉；代次守卫保证它不会踩掉用户手动发起的新扫描。
    /// </summary>
    private async Task ReScanAfterUninstallAsync()
    {
        if (_scanning || _root == null) return;
        string drive = _root.FullPath;
        if (string.IsNullOrWhiteSpace(drive)) return;
        _scanning = true;
        _scanUsedFallback = false;
        _scanFallbackReason = "";
        _scanStart = DateTime.Now;
        ScanButton.IsEnabled = false;
        using var scanOp = StartOperation(ref _scanCts, "Scan");
        var ct = scanOp.Token;
        int myGeneration = ++_scanGeneration;
        using var scanLog = AppLog.Begin("Scan");
        scanOp.Stage("post-uninstall", drive);
        try
        {
            var outcome = await _scanCoordinator.RunAgainAsync(drive, null, ct, reason =>
            {
                _scanUsedFallback = true;
                _scanFallbackReason = reason;
            });
            _scanUsedFallback = outcome.UsedFallback;
            _scanFallbackReason = outcome.FallbackReason;
            _scanQuality = outcome.Quality;
            if (myGeneration != _scanGeneration) return;
            // 不 await：首屏不能等后续流水线。阶段由 WorkState 登记，停止按钮照常可用。
            _ = FinishScanAsync(outcome.Root);
            scanOp.Done("post-uninstall rescan", outcome.Root.FileCount);
        }
        catch (OperationCanceledException)
        {
            scanOp.Canceled("post-uninstall rescan canceled");
        }
        catch (Exception ex)
        {
            scanOp.Fail(ex, "post-uninstall rescan");
        }
        finally
        {
            if (myGeneration == _scanGeneration)
            {
                _scanning = false;
                ScanButton.IsEnabled = true;
            }
        }
    }

    private void ShowAppList()
    {
        _showingJunk = false;
        UninstallGrid.Visibility = Visibility.Visible;
        JunkGrid.Visibility = Visibility.Collapsed;
        UninstallRunBtn.Visibility = Visibility.Visible;
        JunkDeleteBtn.Visibility = Visibility.Collapsed;
        JunkSafeBtn.Visibility = Visibility.Collapsed;
        BindAppList();
    }

    /// <summary>
    /// 绑定软件清单。**不分组**：这一页只讲事实，没有「建议卸载 / 建议保留」这类分类。
    /// 默认排序 = 有效占用降序（后端给的 <c>EffectiveFootprintBytes</c>，
    /// 未知是 <see cref="long.MinValue"/>，自然排到最后），再按名称升序。
    /// </summary>
    private void BindAppList()
    {
        var view = CollectionViewSource.GetDefaultView(_apps);
        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Clear();
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(
                nameof(AppUninstallItem.EffectiveFootprintBytes), ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(nameof(AppUninstallItem.Name), ListSortDirection.Ascending));
            view.Filter = null;
        }
        UninstallGrid.ItemsSource = view;
        RefreshUninstallPaneText();
    }

    /// <summary>页头一句事实说明 + 一次性结果提示。排序口径在占用列悬停里。</summary>
    private void RefreshUninstallPaneText()
    {
        if (UninstallSummary == null) return;
        if (_showingJunk)
        {
            UninstallSummary.Text = _junk.Count == 0
                ? Loc.JunkNone
                : Loc.JunkHint(_junk.Count, _junk.Count(x => x.Safe));
            UpdateJunkSelHint();
            return;
        }
        UninstallSummary.Text = _apps.Count == 0
            ? Loc.UninstallFactsOnly + "  ·  " + Loc.UninstallEmpty
            : Loc.UninstallFactsOnly;
        if (!string.IsNullOrWhiteSpace(_uninstallResultNote))
            UninstallSummary.Text += "\n" + _uninstallResultNote;
        UpdateUninstallSelHint();
    }

    private async Task ScanLeftovers(List<ApplicationUninstallerEntry> finished)
    {
        using var op = StartOperation(ref _uninstallCts, "Uninstall");
        var ct = op.Token;
        UninstallProgressPanel.Visibility = Visibility.Visible;
        UninstallProgressBar.IsIndeterminate = true;
        UninstallProgressText.Text = Loc.JunkScanning;
        UninstallSummary.Text = Loc.JunkScanning;
        try
        {
            var all = _apps.Select(x => x.Entry).Where(x => x != null).Cast<ApplicationUninstallerEntry>().ToList();
            var progress = new Progress<ScanProgress>(p =>
            {
                UninstallProgressBar.IsIndeterminate = p.Percent < 0;
                if (p.Percent >= 0) UninstallProgressBar.Value = p.Percent;
                UninstallProgressText.Text = p.CurrentDirectory;
            });
            var list = await Task.Run(() => BcuUninstallService.FindLeftovers(finished, all, progress, ct), ct);
            _junk = list;
            ShowJunkList();
            op.Done("leftovers scanned", list.Count);
        }
        catch (OperationCanceledException)
        {
            op.Canceled("leftover scan canceled");
        }
        catch (Exception ex)
        {
            op.Fail(ex, "leftover scan");
            ShowAlert(Loc.TabUninstall, AppError.From(ex, "leftover scan").UserMessage);
        }
        finally
        {
            UninstallProgressPanel.Visibility = Visibility.Collapsed;
            UninstallProgressBar.IsIndeterminate = false;
            await RefreshAppsOnlyAsync();
        }
    }

    private void ShowJunkList()
    {
        _showingJunk = true;
        UninstallGrid.Visibility = Visibility.Collapsed;
        JunkGrid.Visibility = Visibility.Visible;
        UninstallRunBtn.Visibility = Visibility.Collapsed;
        JunkDeleteBtn.Visibility = Visibility.Visible;
        JunkSafeBtn.Visibility = Visibility.Visible;
        var view = CollectionViewSource.GetDefaultView(_junk);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(JunkItem.ConfidenceScore), ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(nameof(JunkItem.AppName), ListSortDirection.Ascending));
            view.Filter = null;
        }
        JunkGrid.ItemsSource = view;
        RefreshUninstallPaneText();
    }

    private void JunkSafe_Click(object sender, RoutedEventArgs e)
    {
        bool allSafe = _junk.Where(x => x.Safe).All(x => x.Selected) && _junk.Any(x => x.Safe);
        foreach (var j in _junk)
            j.Selected = !allSafe && j.Safe;
        UpdateJunkSelHint();
    }

    private void JunkGrid_Click(object sender, MouseButtonEventArgs e) => UpdateJunkSelHint();

    private void UpdateJunkSelHint()
    {
        var picked = _junk.Where(x => x.Selected).ToList();
        UninstallSelHint.Text = picked.Count == 0 ? "" : Loc.UninstallCount(picked.Count);
        bool allSafe = _junk.Where(x => x.Safe).All(x => x.Selected) && _junk.Any(x => x.Safe);
        JunkSafeBtn.BorderBrush = ThemeService.Brush(allSafe ? "Accent" : "Border");
        JunkSafeBtn.Foreground = ThemeService.Brush(allSafe ? "Accent" : "TextDim");
    }

    private void JunkDelete_Click(object sender, RoutedEventArgs e)
    {
        var picked = _junk.Where(x => x.Selected).ToList();
        if (picked.Count == 0)
        {
            ShowAlert(Loc.JunkDelete, Loc.NothingSelected);
            return;
        }
        AskConfirm(Loc.JunkDelete, Loc.JunkConfirm(picked.Count), () =>
        {
            var (ok, fail) = BcuUninstallService.DeleteLeftovers(picked);
            foreach (var item in picked) _junk.Remove(item);
            JunkGrid.ItemsSource = null;
            JunkGrid.ItemsSource = _junk;
            RefreshUninstallPaneText();
            SetStatus(Loc.JunkDeleted(ok, fail));
        });
    }

    private void CleanGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CleanGrid.SelectedItem is not CleanItem item) return;
        // 统一走 ShellReveal：目录就打开，文件就打开所在文件夹并选中（不执行文件）
        Reveal(RealPathOf(item));
    }

    /// <summary>
    /// 确认删除。
    ///
    /// 关键：选择范围是**全部已选候选**（可能跨多个用途/位置，也可能包含勾了整组但当前没显示的文件），
    /// 不是当前明细页。确认框上的位置数 / 候选数 / 预计空间 / 需确认风险都按这个集合算，
    /// 然后交回原有的删除预检与逐项结果 —— 分组归类只是一层视图，没有产生任何新的删除权限。
    /// </summary>
    private void RecycleSelected_Click(object sender, RoutedEventArgs e)
    {
        var picked = _layered.SelectedItems
            .Where(x => x.CanDelete && !string.IsNullOrEmpty(x.FullPath))
            .ToList();
        if (picked.Count == 0)
        {
            ShowAlert(Loc.DeleteToRecycle, Loc.NothingSelected);
            return;
        }
        RunCleanFor(picked);
    }

    /// <summary>
    /// 真正执行清理：预检 → 确认框 → 执行 → 逐项结果。
    /// 清理前检查页与本方法共用这条链路，保证两条入口的执行集合完全一致。
    /// </summary>
    private void RunCleanFor(List<CleanItem> picked)
    {
        if (picked.Count == 0) return;

        long bytes = picked.Sum(x => x.Size);
        // 位置数按「实际涉及的不同位置」算，不把分类数当位置数
        int locations = CountLocationsOf(picked);
        int sensitive = picked.Count(x => x.Risk != CleanRisk.Safe);

        var plan = _deleteCoordinator.Plan(ToTargets(picked));
        string message = Loc.DeleteScopeConfirm(locations, picked.Count, FileEntry.FormatSize(bytes), sensitive)
            + DeletionCoordinator.PlanNote(plan);

        AskConfirm(Loc.DeleteToRecycle, message, () =>
        {
            // 执行中：主按钮变成「正在清理……」不可点，停止入口由顶栏「停止」提供
            SetCleanActionState(CleanActionState.Running);
            try
            {
                var batch = _deleteCoordinator.Execute(plan, allowSensitive: true);
                ApplyDeletionToReport(batch);
                // 删除后按「只移除真正成功的项」刷新分组与总计
                RemoveDeletedFromLayers(batch);
                ReportDeletion(batch, single: false);
                // 完成后：已完成 N 项，M 项未处理 [查看结果]
                SetCleanActionState(CleanActionState.Done, batch.Recycled, batch.Total - batch.Recycled);
            }
            finally
            {
                if (_cleanAction == CleanActionState.Running)
                    SetCleanActionState(CleanActionState.Idle);
            }
        });
    }

    /// <summary>底部唯一操作栏的执行状态。</summary>
    private enum CleanActionState { Idle, Running, Done }
    private CleanActionState _cleanAction = CleanActionState.Idle;
    private int _lastDoneOk, _lastDoneFailed;

    private void SetCleanActionState(CleanActionState state, int ok = 0, int failed = 0)
    {
        _cleanAction = state;
        if (state == CleanActionState.Running)
        {
            _lastDoneOk = _lastDoneFailed = 0;
            CheckAndCleanBtn.Content = Loc.CleaningNow;
            CheckAndCleanBtn.IsEnabled = false;
            ViewSelectedBtn.Visibility = Visibility.Collapsed;
            CleanSelectionNote.Text = "";
            ApplySelectAllButton(allSelected: false, enabled: false);
            return;
        }
        if (state == CleanActionState.Done)
        {
            _lastDoneOk = ok;
            _lastDoneFailed = failed;
            CheckAndCleanBtn.Content = Loc.ViewResults;
            CheckAndCleanBtn.IsEnabled = true;
            ViewSelectedBtn.Visibility = Visibility.Collapsed;
            ApplySelectAllButton(allSelected: false, enabled: false);
            return;
        }
        CheckAndCleanBtn.Content = Loc.CleanSelectedItems;
    }

    private void ViewResults_Click(object sender, RoutedEventArgs e)
    {
        ShowAlert(Loc.DeleteResultTitle, _lastResultText.Length > 0 ? _lastResultText : Loc.DeleteOk);
    }

    /// <summary>选中集合涉及多少个清理位置（按稳定键去重，不重复计）。</summary>
    private int CountLocationsOf(List<CleanItem> picked)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _layered.Purposes)
            foreach (var loc in p.Locations)
            {
                if (loc.Items.Any(picked.Contains)) keys.Add(loc.Key);
            }
        return keys.Count > 0 ? keys.Count : 0;
    }

    /// <summary>
    /// 删除后只摘掉**真正成功**的候选，失败的留在原地并保留原因；
    /// 然后重建分层，让分组计数与总计跟着更新。
    /// </summary>
    private void RemoveDeletedFromLayers(DeletionBatchResult batch)
    {
        var gone = new HashSet<string>(batch.RecycledPaths.Select(CleanListSnapshot.NormPath),
            StringComparer.OrdinalIgnoreCase);

        // 从报告的各列表里摘掉成功项（失败项不动）
        if (_report != null && gone.Count > 0)
        {
            foreach (var list in new[]
            {
                _report.Cleanable, _report.LargeFiles, _report.OldFiles, _report.Duplicates,
                _report.EmptyFolders, _report.BrokenShortcuts, _report.LongPaths,
            })
                list.RemoveAll(x => x.Entry != null && gone.Contains(CleanListSnapshot.NormPath(x.FullPath)));
            _report.CleanableBytes = _report.Cleanable.Sum(x => x.Size);
        }

        // 同步扫描树，保证目录大小统计跟着变（2.11 起没有目录树要重建）
        if (_root != null && gone.Count > 0)
        {
            var byPath = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _allFiles) byPath[CleanListSnapshot.NormPath(f.FullPath)] = f;
            foreach (var path in batch.RecycledPaths)
            {
                if (!byPath.TryGetValue(CleanListSnapshot.NormPath(path), out var entry)) continue;
                entry.Parent?.Children.Remove(entry);
                if (entry.Parent != null) RecalcUp(entry.Parent);
            }
            _allFiles = CollectFiles(_root);
        }

        // 重建分层（后台）——计数与总计随之更新
        if (_root != null) _ = RebuildLayersAsync(_root, null, "after-delete");
        RefreshCleanUi();
    }

    /// <summary>把清理列表条目转成删除目标，带上扫描时的身份快照。</summary>
    private List<DeletionTarget> ToTargets(IEnumerable<CleanItem> items)
    {
        bool exact = _scanQuality?.Source == ScanSource.Recursive;
        var list = new List<DeletionTarget>();
        foreach (var x in items)
        {
            list.Add(new DeletionTarget
            {
                Path = x.FullPath,
                Label = x.Name,
                IsDirectory = x.IsDirectory,
                // 目录的 Size 是聚合值，和磁盘上的目录项对不上，不参与比对
                ExpectedSize = x.IsDirectory ? -1 : x.Size,
                ExpectedModified = x.Entry?.Modified ?? default,
                SnapshotIsExact = exact,
                Source = x,
            });
        }
        return list;
    }

    /// <summary>把真正删掉的项从报告里摘掉，并刷新树。</summary>
    private void ApplyDeletionToReport(DeletionBatchResult batch)
    {
        RemoveDeletedFromLayers(batch);
    }

    /// <summary>
    /// 删除结果汇报：文案由 <see cref="DeletionCoordinator.Summarize"/> 生成（那边可测），
    /// 这里只负责把它放到界面上。
    /// </summary>
    private void ReportDeletion(DeletionBatchResult batch, bool single)
    {
        // 保留逐项结果供「查看结果」再打开（部分失败要能保留失败项与原因）
        _lastResultText = string.Join(Environment.NewLine, DeletionCoordinator.Summarize(batch));
        SetStatus(Loc.DeleteResultHead(batch.Recycled, FileEntry.FormatSize(batch.FreedBytes), batch.Total));
        if (single && batch.Total == 1 && batch.Recycled == 1)
        {
            SetStatus(Loc.DeleteOk);
            return;
        }
        ShowAlert(Loc.DeleteResultTitle, _lastResultText);
    }

    /// <summary>
    /// 只按建议勾选并展示，绝不代替用户删除：AI/规则只负责分析，删不删由用户看过清单后确认。
    /// </summary>
    private async void ReviewSuggestions_Click(object sender, RoutedEventArgs e)
    {
        if (_scanning)
        {
            ShowAlert(Loc.ReviewSuggestions, Loc.ReviewScanning);
            return;
        }
        if (_report == null)
        {
            ShowAlert(Loc.ReviewSuggestions, Loc.ReviewScanFirst);
            RunScan();
            return;
        }

        // 确保软件列表已加载（本地规则会先标注建议卸载的项）。
        if (_apps.Count == 0 && !_listingApps)
            await LoadApps();

        // 预勾选建议卸载的软件（用户仍可取消）。
        int appCount = 0;
        long appBytes = 0;
        foreach (var app in _apps)
        {
            app.Selected = app.CanUninstall && app.Recommendation == AppRecommendationDecision.Recommend;
            if (app.Selected)
            {
                appCount++;
                appBytes += app.ActualSizeBytes > 0 ? app.ActualSizeBytes : app.SizeBytes;
            }
        }

        // 全盘「有把握」的项（不限于当前分类/目录视图）。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var safe = new List<CleanItem>();
        foreach (var item in AllCandidates())
        {
            if (!item.CanDelete || item.Risk != CleanRisk.Safe) continue;
            if (string.IsNullOrEmpty(item.FullPath)) continue;
            if (!seen.Add(NormPath(item.FullPath))) continue;
            safe.Add(item);
        }
        long junkBytes = safe.Sum(x => x.Size);

        if (safe.Count == 0 && appCount == 0)
        {
            ShowAlert(Loc.ReviewSuggestions, Loc.ReviewNothing);
            return;
        }

        // 只勾选，不删除。回到首页让用户看到完整的建议清单。
        foreach (var item in safe)
            item.Selected = true;

        ShowRightTab(RightTab.Clean);
        CloseDetail();
        ShowPurposePage(restoreScroll: true);
        RefreshAfterSelectionChange();
        CleanSummarySub.Text = Loc.ReviewHint(
            safe.Count, FileEntry.FormatSize(junkBytes),
            appCount, FileEntry.FormatSize(appBytes));
        UpdateSelectionUi();
        UpdateUninstallSelHint();
    }
}
