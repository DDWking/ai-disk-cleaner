using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AiDiskCleaner.Controls;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
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

/// <summary>GroupKey 0/1 默认展开，2/3（Windows 功能、受保护）折叠。</summary>
public sealed class GroupExpandedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is 0 or 1;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
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
            1 => Loc.UninstallGroupSteam(n),
            2 => Loc.UninstallGroupFeatures(n),
            3 => Loc.UninstallGroupProtected(n),
            _ => Loc.UninstallGroupOk,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class ShareWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double share = value is double d ? Math.Clamp(d, 0, 1) : 0;
        bool remainder = parameter?.ToString() == "rest";
        double star = remainder ? 1 - share : share;
        if (star <= 0) return new GridLength(0);
        return new GridLength(star, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public partial class MainWindow : Window, IAnalystHost
{
    private const int MaxDisplayRows = 50000; // 文件列表最多渲染的行数，超出只显示前 N 行

    private readonly IScanService _scanner = new MftScanService();
    private readonly IScanService _fallback = new RecursiveScanService();
    private FileEntry _root = null!;
    private FileEntry _current = null!;
    private List<FileEntry> _allFiles = new(); // 缓存：根目录下所有文件（避免重复递归收集）
    private CancellationTokenSource? _cts;
    private bool _scanning;
    private DateTime _scanStart;
    private string _search = "";
    private string? _extFilter;
    private SortKey _sort = SortKey.Size;
    private TreeViewItem? _liveRoot;
    private int _liveShown;
    private int _liveExtShown;
    private CleanReport? _report;
    private int _rightTab; // 0 清理 1 扩展名 2 卸载
    private int _page; // 0 浏览 1 AI 分析
    private Action? _confirmYes;
    private List<AppUninstallItem> _apps = new();
    private bool _listingApps;
    private BulkUninstallTask? _uninstallTask;
    private List<JunkItem> _junk = new();
    private bool _showingJunk;
    private long _volumeTotal;
    private long _volumeUsed;
    private bool _aiModelLock;
    private bool _aiBusy;
    private bool _aiOk;
    private bool _aiTried;
    private readonly Dictionary<string, string> _aiNotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ChatLine> _chat = new();
    private readonly List<AiMsg> _turns = new();
    private string? _need;
    private List<VoteItem> _votes = new();
    private List<CleanItem> _aiItems = new();
    // 按用途分好的分类（名称 + 条目 + 占比），分类列表和「分析当前分类」都用它
    private List<CatRow> _cats = new();
    private int _catIndex;
    private readonly ObservableCollection<JuryPane> _juryPanes = new();
    FileEntry? IAnalystHost.Root => _root;
    CleanReport? IAnalystHost.Report => _report;
    private static readonly AiProtocol[] AiProtos =
        { AiProtocol.Completions, AiProtocol.Responses, AiProtocol.Anthropic };

    private enum SortKey { Size, Name, Allocated, Files, Folders, Modified }

    private static readonly object Placeholder = new();

    public MainWindow()
    {
        InitializeComponent();
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
        DriveBox.ItemsSource = drives;
        if (drives.Count > 0) DriveBox.SelectedIndex = 0;
        UpdateVolumeInfo();
        DriveBox.SelectionChanged += (_, _) => UpdateVolumeInfo();
        SearchBox.GotFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(SearchBox.Text))
                SearchBox.CaretIndex = 0;
        };
        StateChanged += (_, _) =>
        {
            MaxButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            BorderThickness = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(1);
        };
        // 右侧面板拉太宽会被挤出可视区（左列有 MinWidth=420，挤到头就把右列顶没了）。
        // 这里按窗口宽度动态封顶，保证拖到任何位置右列都还在。
        SizeChanged += (_, _) => UpdateRightColLimit();
        BorderBrush = ThemeService.Brush("Border");
        BorderThickness = new Thickness(1);
        ApplyUi();
        ShowRightTab(0);
        // sidecar 的工具回调落到这里（this 实现了 IAnalystHost），
        // 勾选/删除等动作仍在 C# 侧执行。
        try { SidecarClient.AttachToolHost(this); } catch { }
        PickDrive("C:\\");
    }

    /// <summary>右列最大宽度 = 窗口宽 - 左列最小宽 - 分隔条 - 余量，防止右列被挤出消失。</summary>
    void UpdateRightColLimit()
    {
        if (RightCol == null) return;
        double max = ActualWidth - 420 - 12 - 40;   // 左列 MinWidth + splitter + 余量
        RightCol.MaxWidth = Math.Max(320, max);
    }

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
        SettingsButton.Content = Loc.Settings;
        AboutButton.Content = Loc.About;
        if (HeaderStats.Text is "就绪" or "Ready") HeaderStats.Text = Loc.Ready;
        PathCrumb.Text = _current == null || string.IsNullOrEmpty(_current.FullPath) ? "" : _current.FullPath;
        NameHeader.Text = Loc.Path;
        PctHeader.Text = Loc.Pct;
        SizeHeader.Text = Loc.Size;
        AllocHeader.Text = Loc.Allocated;
        FilesHeader.Text = Loc.FilesCol;
        FoldersHeader.Text = Loc.FoldersCol;
        CtxOpen.Header = Loc.OpenInExplorer;
        if (CleanOpenItem != null) CleanOpenItem.Header = Loc.OpenInExplorer;
        CtxCopyPath.Header = Loc.CopyPath;
        CtxCopyName.Header = Loc.CopyName;
        CtxDelete.Header = Loc.DeleteToRecycle;
        CtxAskAi.Header = Loc.AskAiFolder;
        CtxProps.Header = Loc.Properties;
        ExtTitle.Text = Loc.ExtType;
        ColExt.Header = Loc.Ext;
        ColType.Header = Loc.Type;
        ColPct.Header = Loc.Pct;
        ColSize.Header = Loc.Size;
        TabCleanBtn.Content = Loc.TabClean;
        TabExtBtn.Content = Loc.TabExt;
        TabUninstallBtn.Content = Loc.TabUninstall;
        UninstallRefreshBtn.Content = Loc.Refresh;
        UninstallAllBtn.Content = Loc.SelectAll;
        UninstallRunBtn.Content = Loc.UninstallRun;
        UninstallSearchHint.Text = Loc.UninstallSearchHint;
        JunkSafeBtn.Content = Loc.JunkSafe;
        JunkDeleteBtn.Content = Loc.JunkDelete;
        ColAppName.Header = Loc.ColName;
        ColAppPub.Header = Loc.Publisher;
        ColAppSize.Header = Loc.Size;
        ColAppStatus.Header = Loc.Status;
        ColJunkApp.Header = Loc.ColName;
        ColJunkKind.Header = Loc.ColCategory;
        ColJunkConf.Header = Loc.ColConfidence;
        ColJunkPath.Header = Loc.Path;
        RefreshUninstallPaneText();
        SelectAllBtn.Content = Loc.SelectAll;
        SelectSafeBtn.Content = Loc.SelectSafe;
        HighlightSelectMode();
        RecycleSelBtn.Content = Loc.RecycleSelected;
        ColCleanName.Header = Loc.ColName;
        ColCleanSize.Header = Loc.Size;
        ColCleanWhy.Header = Loc.ColReason;
        ColCleanAiNote.Header = Loc.AiColNote;
        RefreshCleanUi();
        DialogClose.Content = Loc.Close;
        ConfirmYesBtn.Content = Loc.Yes;
        ConfirmNoBtn.Content = Loc.No;
        LangLabel.Text = Loc.Language;
        LangZhBtn.Content = Loc.LangZh;
        LangEnBtn.Content = Loc.LangEn;
        AiSectionLabel.Text = Loc.AiSection;
        AiSectionHint.Text = Loc.AiSectionHint;
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
        AiExplainBtn.Content = Loc.AiExplain;
        AiAnalyzeCatBtn.Content = Loc.AiAnalyze;
        AiAddProvBtn.Content = Loc.AiAddCustom;
        FillAiProtoBox();
        FillRunModels();
        FillJuryPicks();
        RefreshJuryUi();
        AboutText.Text = Loc.AboutBody;
        RepoLink.Text = Loc.Repo;
        if (_current == null)
        {
            FileCountText.Text = Loc.Files(0);
            CleanHintText.Text = Loc.AnalyzeAfterScan;
            ScanProgressText.Text = Loc.ScanningEllipsis;
        }
        BorderBrush = ThemeService.Brush("Border");
        HighlightThemeButtons();
        if (_current != null)
        {
            PopulateTree();
            ShowDirectory(_current);
        }
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

    private async void RunScan()
    {
        if (_scanning || DriveBox.SelectedItem == null) return;
        _scanning = true;
        ScanButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        if (AiAnalyzeCatBtn != null) AiAnalyzeCatBtn.IsEnabled = false;
        _scanStart = DateTime.Now;
        _cts = new CancellationTokenSource();
        HeaderStats.Text = Loc.Scanning;
        FileCountText.Text = Loc.Files(0);
        ScanProgressPanel.Visibility = Visibility.Visible;
        ScanProgressBar.IsIndeterminate = true;
        ScanProgressBar.Value = 0;
        ScanProgressText.Text = Loc.Preparing;
        ShowRightTab(0);
        SetCleanProgress(0, Loc.CleanScan, determinate: false);
        BeginLiveScan(DriveBox.SelectedItem.ToString()!);

        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.Percent >= 0)
            {
                ScanProgressBar.IsIndeterminate = false;
                ScanProgressBar.Value = p.Percent;
                ScanProgressText.Text = Loc.ProgressLine(p.Percent, p.CurrentDirectory, p.FileCount);
                HeaderStats.Text = Loc.ScanPct(p.Percent);
                SetCleanProgress(p.Percent, Loc.ProgressLine(p.Percent, p.CurrentDirectory, p.FileCount), determinate: true);
            }
            else
            {
                ScanProgressBar.IsIndeterminate = true;
                ScanProgressText.Text = Loc.ProgressIndeterminate(p.CurrentDirectory, p.FileCount);
                HeaderStats.Text = Loc.ScanCount(p.FileCount);
                SetCleanProgress(0, Loc.ProgressIndeterminate(p.CurrentDirectory, p.FileCount), determinate: false);
            }
            FileCountText.Text = Loc.Files(p.FileCount);
            GrowLiveScan(p.Percent >= 0 ? p.Percent : Math.Min(90, p.FileCount / 8000));
        });

        try
        {
            string drive = DriveBox.SelectedItem.ToString()!;
            FileEntry root;
            try
            {
                root = await Task.Run(() => _scanner.Scan(drive, progress, _cts.Token));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                HeaderStats.Text = Loc.MftFail;
                root = await Task.Run(() => _fallback.Scan(drive, progress, _cts.Token));
            }
            FinishScan(root);
        }
        catch (OperationCanceledException)
        {
            ClearLiveScan();
            HideCleanProgress();
            HeaderStats.Text = Loc.Aborted;
        }
        catch (Exception ex)
        {
            HideCleanProgress();
            ShowAlert(Loc.ScanFailed, Loc.ScanFailedMsg(ex.Message));
            HeaderStats.Text = Loc.ScanFailed;
        }
        finally
        {
            _scanning = false;
            ScanButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            if (AiAnalyzeCatBtn != null) AiAnalyzeCatBtn.IsEnabled = true;
            ScanProgressPanel.Visibility = Visibility.Collapsed;
            ScanProgressBar.IsIndeterminate = false;
        }
    }

    private void FinishScan(FileEntry root)
    {
        UiLog("FinishScan 开始");
        _root = root;
        _current = root;
        _allFiles = CollectFiles(root); // 只收集一次，缓存
        UiLog($"CollectFiles 完成: {_allFiles.Count:N0}");
        UpdateVolumeInfo();
        ClearLiveScan();
        PopulateTree();
        ShowDirectory(root);
        UiLog("PopulateTree 完成");
        ElapsedText.Text = Loc.Elapsed((DateTime.Now - _scanStart).TotalSeconds);
        HeaderStats.Text = Loc.Files(root.FileCount);
        CleanHintText.Text = Loc.Analyzing;
        SetCleanProgress(5, Loc.Analyzing, determinate: true);
        _ = RunAnalyze(root);
        UiLog("FinishScan 全部完成");
    }

    private async Task RunAnalyze(FileEntry root)
    {
        string drive = root.FullPath;
        var previous = ScanSnapshot.Load(drive);
        CleanReport report;
        var analyzeProgress = new Progress<ScanProgress>(p =>
        {
            int pct = p.Percent >= 0 ? p.Percent : 0;
            SetCleanProgress(pct, p.CurrentDirectory, determinate: p.Percent >= 0);
        });
        try
        {
            report = await Task.Run(() => CleanAnalyzer.Analyze(root, previous, CancellationToken.None, analyzeProgress));
        }
        catch (Exception ex)
        {
            UiLog("分析失败: " + ex.Message);
            HideCleanProgress();
            CleanHintText.Text = Loc.HintClean;
            return;
        }
        if (!ReferenceEquals(_root, root)) return;
        _report = report;
        try { ScanSnapshot.Capture(root).Save(); } catch { }
        HideCleanProgress();
        RefreshCleanUi();
        CleanHintText.Text = report.Cleanable.Count > 0
            ? Loc.CleanHintReady(report.Cleanable.Count, FileEntry.FormatSize(report.CleanableBytes))
            : Loc.HintClean;
        UiLog($"分析完成: cleanable={report.Cleanable.Count} dup={report.Duplicates.Count}");
        _ = AutoAnalyze(root, report);
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
            File.AppendAllText(@"D:\ssssswiztree\ui-timing.log",
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
        }
        catch { }
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
            foreach (var c in n.Children)
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

    private void PopulateTree()
    {
        DirTree.Items.Clear();
        if (_root == null) return;
        UpdateFilterHint();
        var root = new TreeViewItem { Header = MakeFolderHeader(_root, isRoot: true), Tag = _root, IsExpanded = true };
        DirTree.Items.Add(root);
        PopulateDirChildren(root);
        root.IsSelected = true;
    }

    private void UpdateFilterHint()
    {
        if (!string.IsNullOrEmpty(_extFilter) || !string.IsNullOrWhiteSpace(_search))
        {
            var bits = new List<string>();
            if (!string.IsNullOrEmpty(_extFilter)) bits.Add(Loc.FilterExt(_extFilter));
            if (!string.IsNullOrWhiteSpace(_search)) bits.Add(_search);
            FilterHint.Text = string.Join("  ·  ", bits) + "   (Esc / " + Loc.FilterOff + ")";
            FilterHint.Visibility = Visibility.Visible;
        }
        else
        {
            FilterHint.Text = "";
            FilterHint.Visibility = Visibility.Collapsed;
        }
    }

    private FrameworkElement MakeFolderHeader(FileEntry d, bool isRoot = false)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 80 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        var mark = MarkFor(d.FullPath);
        string note = mark.Note;
        bool marked = mark.Hit;
        var name = new TextBlock
        {
            Text = string.IsNullOrEmpty(note) ? d.Name : d.Name + "  ·  " + note,
            Foreground = marked
                ? new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0xFF))
                : ThemeService.Brush(d.IsDimmed ? "TextMuted" : "Text"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = string.IsNullOrEmpty(note) ? null : note,
        };
        double pct = isRoot && _volumeTotal > 0
            ? 100.0 * _volumeUsed / _volumeTotal
            : d.PercentValue;
        double share = isRoot && _volumeTotal > 0
            ? Math.Clamp(_volumeUsed / (double)_volumeTotal, 0, 1)
            : d.PercentShare;
        var pctCell = MakePctBar(share, pct, d.IsDimmed);
        var size = ColText(FileEntry.FormatSize(d.Size), d.IsDimmed ? "TextMuted" : "AccentDim");
        var alloc = ColText(FileEntry.FormatSize(d.Allocated), "TextMuted");
        var files = ColText(d.IsDirectory || d.IsFilesGroup ? d.FileCount.ToString("N0") : "", "TextMuted");
        var folders = ColText(d.IsDirectory ? d.FolderCount.ToString("N0") : "", "TextMuted");
        Grid.SetColumn(name, 0);
        Grid.SetColumn(pctCell, 1);
        Grid.SetColumn(size, 2);
        Grid.SetColumn(alloc, 3);
        Grid.SetColumn(files, 4);
        Grid.SetColumn(folders, 5);
        grid.Children.Add(name);
        grid.Children.Add(pctCell);
        grid.Children.Add(size);
        grid.Children.Add(alloc);
        grid.Children.Add(files);
        grid.Children.Add(folders);
        if (marked)
            grid.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x1A, 0x5A, 0x90));
        return grid;
    }

    private static TextBlock ColText(string text, string brush)
        => new()
        {
            Text = text,
            Foreground = ThemeService.Brush(brush),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            TextAlignment = TextAlignment.Right,
        };

    private static Grid MakePctBar(double share, double pct, bool dim)
    {
        var pctCell = new Grid { Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Height = 22 };
        pctCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pctCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
        double rest = Math.Max(0, 1 - share);
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = share <= 0 ? new GridLength(0) : new GridLength(share, GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = rest <= 0 ? new GridLength(0) : new GridLength(rest, GridUnitType.Star) });
        var fill = new Border { Background = ThemeService.Brush(dim ? "TextMuted" : "Accent") };
        var bg = new Border { Background = ThemeService.Brush("Border") };
        Grid.SetColumn(bg, 1);
        track.Children.Add(fill);
        track.Children.Add(bg);
        var pctText = new TextBlock
        {
            Text = pct.ToString("0.0") + " %",
            Foreground = ThemeService.Brush(dim ? "Placeholder" : "TextMuted"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(pctText, 1);
        pctCell.Children.Add(track);
        pctCell.Children.Add(pctText);
        return pctCell;
    }

    private IEnumerable<FileEntry> VisibleChildren(FileEntry entry)
    {
        IEnumerable<FileEntry> kids = entry.Children;
        if (!string.IsNullOrWhiteSpace(_search) || !string.IsNullOrEmpty(_extFilter))
            kids = kids.Where(MatchesFilter);
        return _sort switch
        {
            SortKey.Name => kids.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            SortKey.Allocated => kids.OrderByDescending(c => c.Allocated),
            SortKey.Files => kids.OrderByDescending(c => c.FileCount),
            SortKey.Folders => kids.OrderByDescending(c => c.FolderCount),
            SortKey.Modified => kids.OrderByDescending(c => c.Modified),
            _ => kids.OrderByDescending(c => c.Size),
        };
    }

    private bool MatchesFilter(FileEntry e)
    {
        if (!string.IsNullOrWhiteSpace(_search))
        {
            if (e.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase)
                || (e.FullPath?.Contains(_search, StringComparison.CurrentCultureIgnoreCase) ?? false))
                return true;
            return e.IsDirectory && SubtreeHasName(e, _search);
        }
        if (!string.IsNullOrEmpty(_extFilter))
        {
            if (e.IsDirectory) return SubtreeHasExt(e, _extFilter);
            return ExtOf(e).Equals(_extFilter, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    private static bool SubtreeHasName(FileEntry dir, string q)
    {
        var stack = new Stack<FileEntry>();
        stack.Push(dir);
        int n = 0;
        while (stack.Count > 0 && n++ < 20000)
        {
            var x = stack.Pop();
            foreach (var c in x.Children)
            {
                if (c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                    || (c.FullPath?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false))
                    return true;
                if (c.IsDirectory) stack.Push(c);
            }
        }
        return false;
    }

    private static bool SubtreeHasExt(FileEntry dir, string ext)
    {
        var stack = new Stack<FileEntry>();
        stack.Push(dir);
        int n = 0;
        while (stack.Count > 0 && n++ < 40000)
        {
            var x = stack.Pop();
            foreach (var c in x.Children)
            {
                if (!c.IsDirectory && ExtOf(c).Equals(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (c.IsDirectory) stack.Push(c);
            }
        }
        return false;
    }

    private static string ExtOf(FileEntry e)
    {
        string ext = Path.GetExtension(e.Name);
        if (e.Name.StartsWith('$') && ext.Length == 0) ext = e.Name;
        return string.IsNullOrEmpty(ext) ? Loc.NoExt : ext;
    }

    private void PopulateDirChildren(TreeViewItem parent)
    {
        parent.Items.Clear();
        var entry = (FileEntry)parent.Tag;
        const int maxFiles = 400;
        var visible = VisibleChildren(entry).ToList();
        var dirs = visible.Where(c => c.IsDirectory);
        var files = visible.Where(c => !c.IsDirectory).ToList();
        foreach (var d in dirs)
        {
            var item = new TreeViewItem { Header = MakeFolderHeader(d), Tag = d };
            bool hasKids = d.Children.Count > 0 && (string.IsNullOrWhiteSpace(_search) && _extFilter == null
                ? true
                : d.Children.Any(MatchesFilter));
            if (hasKids)
            {
                item.Items.Add(new TreeViewItem { Header = "…", Tag = Placeholder });
                item.Expanded += DirItem_Expanded;
            }
            parent.Items.Add(item);
        }
        int shown = 0;
        foreach (var f in files)
        {
            if (shown++ >= maxFiles) break;
            parent.Items.Add(new TreeViewItem { Header = MakeFolderHeader(f), Tag = f });
        }
        if (files.Count > maxFiles)
        {
            var more = new FileEntry
            {
                Name = Loc.MoreFiles(files.Count - maxFiles),
                Size = files.Skip(maxFiles).Sum(x => x.Size),
                Allocated = files.Skip(maxFiles).Sum(x => x.Allocated),
                Kind = EntryKind.File,
                IsHidden = true,
            };
            more.Parent = entry;
            parent.Items.Add(new TreeViewItem { Header = MakeFolderHeader(more), Tag = more });
        }
    }

    private void DirItem_Expanded(object sender, RoutedEventArgs e)
    {
        var item = (TreeViewItem)sender;
        if (item.Items.Count == 1 && item.Items[0] is TreeViewItem ph && ReferenceEquals(ph.Tag, Placeholder))
            PopulateDirChildren(item);
        PaintItem(item);
    }

    private void ShowDirectory(FileEntry dir)
    {
        _current = dir;
        PathCrumb.Text = dir.FullPath ?? "";
        FileCountText.Text = Loc.FileDirCount(dir.FileCount, dir.FolderCount);
        TotalSizeText.Text = FileEntry.FormatSize(dir.Size) + "  /  " + FileEntry.FormatSize(dir.Allocated);
        ShowExtStats(dir);
    }

    private static readonly Color[] ExtPalette =
    {
        Color.FromRgb(0x8F, 0xE8, 0xB0),
        Color.FromRgb(0x5F, 0xC9, 0x88),
        Color.FromRgb(0x3A, 0xA8, 0x68),
        Color.FromRgb(0xB7, 0xD4, 0xBE),
        Color.FromRgb(0x7A, 0x9A, 0x82),
        Color.FromRgb(0x2E, 0x6B, 0x45),
        Color.FromRgb(0xA8, 0xE0, 0xB8),
        Color.FromRgb(0x4C, 0x8F, 0x66),
        Color.FromRgb(0xC5, 0xE8, 0xCE),
        Color.FromRgb(0x3C, 0x7A, 0x54),
        Color.FromRgb(0x6B, 0xB8, 0x86),
        Color.FromRgb(0x9A, 0xC4, 0xA6),
    };

    /// <summary>当前目录（含子目录）按扩展名汇总占用，和 WizTree 右侧一致。</summary>
    private void ShowExtStats(FileEntry dir)
    {
        ExtGrid.ItemsSource = BuildExtStats(dir);
    }

    private List<ExtStat> BuildExtStats(FileEntry dir)
    {
        var map = new Dictionary<string, (long Size, int Count)>(StringComparer.OrdinalIgnoreCase);
        CollectExt(dir, map);
        long total = 0;
        foreach (var v in map.Values) total += v.Size;
        if (total <= 0) total = 1;

        var list = map
            .Select(kv =>
            {
                string ext = kv.Key;
                string shown = ext.Length == 0 ? Loc.NoExt : ext;
                return new ExtStat
                {
                    Extension = shown,
                    TypeName = Loc.TypeName(ext),
                    Size = kv.Value.Size,
                    Count = kv.Value.Count,
                    Percent = 100.0 * kv.Value.Size / total,
                    PercentText = (100.0 * kv.Value.Size / total).ToString("0.0") + " %",
                };
            })
            .OrderByDescending(x => x.Size)
            .Take(40)
            .ToList();

        for (int i = 0; i < list.Count; i++)
            list[i].Color = new SolidColorBrush(ExtPalette[i % ExtPalette.Length]);
        return list;
    }

    private static readonly string[] LiveFolders =
    {
        "Users", "Program Files", "Windows", "Program Files (x86)", "ProgramData",
        "SteamLibrary", "Recovery", "System Volume Information", "$Recycle.Bin",
        "$Extend", "pagefile.sys", "hiberfil.sys", "swapfile.sys", "Documents and Settings",
        "PerfLogs", "inetpub", "AppData", "Downloads", "Temp",
    };

    private static readonly string[] LiveExts =
    {
        ".dll", ".exe", ".sys", ".zip", ".mkv", ".vhdx", ".tmp", ".log", ".png", ".dat",
        ".msi", ".iso", ".pak", ".db", ".mp4", ".jpg",
    };

    private void BeginLiveScan(string drive)
    {
        ClearLiveScan();
        var root = new FileEntry { Name = drive, Kind = EntryKind.Directory };
        _liveRoot = new TreeViewItem { Header = MakeFolderHeader(root, isRoot: true), Tag = root, IsExpanded = true };
        DirTree.Items.Clear();
        DirTree.Items.Add(_liveRoot);
        ExtGrid.ItemsSource = new List<ExtStat>();
        _liveShown = 0;
        _liveExtShown = 0;
        GrowLiveScan(1);
    }

    private void GrowLiveScan(int percent)
    {
        if (_liveRoot == null) return;
        percent = Math.Clamp(percent, 0, 100);
        int wantLeft = Math.Max(1, percent * LiveFolders.Length / 90);
        int wantRight = Math.Max(0, percent * LiveExts.Length / 90);
        while (_liveShown < wantLeft && _liveShown < LiveFolders.Length)
        {
            string name = LiveFolders[_liveShown++];
            bool file = name.Contains('.');
            var fake = new FileEntry
            {
                Name = name,
                Kind = file ? EntryKind.File : EntryKind.Directory,
                IsHidden = name.StartsWith('$') || name is "pagefile.sys" or "hiberfil.sys" or "swapfile.sys",
                IsSystem = name.StartsWith('$') || name is "Windows" or "System Volume Information",
            };
            var item = new TreeViewItem { Header = MakeFolderHeader(fake), Tag = fake };
            if (!file) item.Items.Add(new TreeViewItem { Header = "…", Tag = Placeholder });
            _liveRoot.Items.Add(item);
        }
        if (wantRight > _liveExtShown)
        {
            var list = new List<ExtStat>();
            for (int i = 0; i < wantRight && i < LiveExts.Length; i++)
            {
                string ext = LiveExts[i];
                list.Add(new ExtStat
                {
                    Extension = ext,
                    TypeName = Loc.TypeName(ext),
                    Percent = Math.Max(1, 22 - i * 1.4),
                    PercentText = Math.Max(1, 22 - i * 1.4).ToString("0.0") + " %",
                    Size = (22 - i) * 400L * 1024 * 1024,
                });
            }
            _liveExtShown = list.Count;
            ExtGrid.ItemsSource = list;
        }
    }

    private void ClearLiveScan()
    {
        _liveRoot = null;
        _liveShown = 0;
        _liveExtShown = 0;
    }

    private static void CollectExt(FileEntry node, Dictionary<string, (long Size, int Count)> map)
    {
        var stack = new Stack<FileEntry>();
        var visited = new HashSet<FileEntry>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!visited.Add(n)) continue;
            foreach (var c in n.Children)
            {
                if (c.IsDirectory)
                {
                    stack.Push(c);
                    continue;
                }
                string ext = Path.GetExtension(c.Name);
                if (c.Name.StartsWith('$') && ext.Length == 0)
                    ext = c.Name; // $MFT / $LogFile 单独成类
                if (!map.TryGetValue(ext, out var cur))
                    map[ext] = (c.Size, 1);
                else
                    map[ext] = (cur.Size + c.Size, cur.Count + 1);
            }
        }
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
        catch { }
    }

    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ScanButton_Click(object sender, RoutedEventArgs e) => RunScan();

    private void StopButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void DirTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DirTree.SelectedItem is TreeViewItem { Tag: FileEntry entry })
            ShowDirectory(entry.IsDirectory ? entry : entry.Parent ?? entry);
    }

    private void DirTree_PreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        while (src != null && src is not TreeViewItem)
            src = VisualTreeHelper.GetParent(src);
        if (src is TreeViewItem item)
            item.IsSelected = true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text?.Trim() ?? "";
        if (_root == null) return;
        PopulateTree();
        if (!string.IsNullOrWhiteSpace(_search) && DirTree.Items.Count > 0 && DirTree.Items[0] is TreeViewItem root)
            ExpandMatches(root, 0);
    }

    private void ExpandMatches(TreeViewItem item, int depth)
    {
        if (depth > 4 || item.Tag is not FileEntry e) return;
        if (e.IsDirectory && e.Children.Any(MatchesFilter))
        {
            item.IsExpanded = true;
            if (item.Items.Count == 1 && item.Items[0] is TreeViewItem ph && ReferenceEquals(ph.Tag, Placeholder))
                PopulateDirChildren(item);
            foreach (var obj in item.Items)
            {
                if (obj is TreeViewItem child)
                    ExpandMatches(child, depth + 1);
            }
        }
    }

    private FileEntry? ContextEntry()
        => (DirTree.SelectedItem as TreeViewItem)?.Tag as FileEntry;

    private void TreeMenu_Opened(object sender, RoutedEventArgs e)
    {
        var entry = ContextEntry();
        bool ok = entry != null && !RecycleService.IsProtected(entry);
        CtxDelete.IsEnabled = ok;
        CtxDelete.Header = ok ? Loc.DeleteToRecycle : Loc.DeleteBlocked;
        CtxAskAi.IsEnabled = entry is { IsDirectory: true } && !entry.IsFilesGroup;
    }

    private void CtxDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = DirTree.SelectedItem as TreeViewItem;
        if (item?.Tag is not FileEntry entry) return;
        if (RecycleService.IsProtected(entry))
        {
            ShowAlert(Loc.DeleteToRecycle, Loc.DeleteBlocked);
            return;
        }
        string path = entry.FullPath;
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            ShowAlert(Loc.DeleteToRecycle, Loc.DeleteFailed(path));
            return;
        }
        AskConfirm(
            Loc.DeleteToRecycle,
            Loc.DeleteConfirm(entry.Name, FileEntry.FormatSize(entry.Allocated > 0 ? entry.Allocated : entry.Size)),
            () =>
            {
                try
                {
                    RecycleService.SendToRecycle(path);
                    var parent = entry.Parent;
                    parent?.Children.Remove(entry);
                    if (item.Parent is TreeViewItem treeParent)
                        treeParent.Items.Remove(item);
                    else
                        DirTree.Items.Remove(item);
                    if (parent != null)
                    {
                        RecalcUp(parent);
                        ShowDirectory(parent);
                    }
                    HeaderStats.Text = Loc.DeleteOk;
                }
                catch (Exception ex)
                {
                    ShowAlert(Loc.DeleteToRecycle, Loc.DeleteFailed(ex.Message));
                }
            });
    }

    private static void RecalcUp(FileEntry node)
    {
        for (var n = node; n != null; n = n.Parent)
        {
            long size = 0, alloc = 0;
            int files = 0, folders = 0;
            foreach (var c in n.Children)
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

    private void CtxOpen_Click(object sender, RoutedEventArgs e)
    {
        var entry = ContextEntry();
        if (entry == null) return;
        OpenExplorer(entry.FullPath, entry.IsDirectory);
    }

    private void CleanOpen_Click(object sender, RoutedEventArgs e)
    {
        if (CleanGrid.SelectedItem is CleanItem item)
            OpenExplorer(item.FullPath, item.IsDirectory);
    }

    static void OpenExplorer(string? path, bool directory)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (Directory.Exists(path) || directory)
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path.TrimEnd('\\') + "\"") { UseShellExecute = true });
            else if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            else
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
        }
        catch { }
    }

    private void CtxCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var entry = ContextEntry();
        if (entry == null) return;
        try { Clipboard.SetText(entry.FullPath ?? entry.Name); } catch { }
    }

    private void CtxCopyName_Click(object sender, RoutedEventArgs e)
    {
        var entry = ContextEntry();
        if (entry == null) return;
        try { Clipboard.SetText(entry.Name); } catch { }
    }

    private void CtxProps_Click(object sender, RoutedEventArgs e)
    {
        var entry = ContextEntry();
        if (entry == null) return;
        ShowAlert(Loc.Properties, Loc.PropBody(entry));
    }

    private void SortName_Click(object sender, MouseButtonEventArgs e) => SetSort(SortKey.Name);
    private void SortSize_Click(object sender, MouseButtonEventArgs e) => SetSort(SortKey.Size);
    private void SortAlloc_Click(object sender, MouseButtonEventArgs e) => SetSort(SortKey.Allocated);
    private void SortFiles_Click(object sender, MouseButtonEventArgs e) => SetSort(SortKey.Files);
    private void SortFolders_Click(object sender, MouseButtonEventArgs e) => SetSort(SortKey.Folders);

    private void SetSort(SortKey key)
    {
        _sort = key;
        if (_root != null) PopulateTree();
    }

    private void ExtGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ExtGrid.SelectedItem is not ExtStat stat) return;
        _extFilter = stat.Extension;
        if (_root != null) PopulateTree();
    }

    private void ExtGrid_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (_extFilter == null) return;
        _extFilter = null;
        if (_root != null) PopulateTree();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            bool changed = false;
            if (!string.IsNullOrEmpty(_extFilter)) { _extFilter = null; changed = true; }
            if (!string.IsNullOrWhiteSpace(_search))
            {
                _search = "";
                SearchBox.Text = "";
                changed = true;
            }
            if (changed && _root != null) PopulateTree();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => OpenOverlay(Loc.SettingsTitle, settings: true);

    private void AboutButton_Click(object sender, RoutedEventArgs e)
        => OpenOverlay(Loc.AboutTitle, about: true);

    private void OpenOverlay(string title, bool settings = false, bool about = false, bool alert = false, bool confirm = false)
    {
        DialogTitle.Text = title;
        SettingsBody.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        ProvEditBody.Visibility = Visibility.Collapsed;
        AboutBody.Visibility = about ? Visibility.Visible : Visibility.Collapsed;
        AlertBody.Visibility = alert || confirm ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButtons.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        DialogClose.Visibility = confirm ? Visibility.Collapsed : Visibility.Visible;
        if (settings)
        {
            HighlightThemeButtons();
            LoadAiFields();
        }
        ShowOverlay();
    }

    void ShowOverlay()
    {
        Overlay.BeginAnimation(OpacityProperty, null);
        DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Overlay.Visibility = Visibility.Visible;
        Overlay.Opacity = 0;
        DialogScale.ScaleX = 0.97;
        DialogScale.ScaleY = 0.97;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase() };
        var grow = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase() };
        Overlay.BeginAnimation(OpacityProperty, fade);
        DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow.Clone());
    }

    void HideOverlay()
    {
        SaveAiFields();
        if (ProvEditBody.Visibility == Visibility.Visible && Overlay.Visibility == Visibility.Visible)
        {
            ProvEditBody.Visibility = Visibility.Collapsed;
            SettingsBody.Visibility = Visibility.Visible;
            DialogTitle.Text = Loc.SettingsTitle;
            LoadAiFields();
            return;
        }
        Overlay.BeginAnimation(OpacityProperty, null);
        var fade = new DoubleAnimation(Overlay.Opacity, 0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) =>
        {
            Overlay.Visibility = Visibility.Collapsed;
            Overlay.BeginAnimation(OpacityProperty, null);
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
        HideOverlay();
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        _confirmYes = null;
        HideOverlay();
    }

    private void Overlay_Click(object sender, MouseButtonEventArgs e)
    {
        _confirmYes = null;
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
    }

    void LoadAiFields()
    {
        App.Settings.Migrate();
        AiProvCards.ItemsSource = null;
        AiProvCards.ItemsSource = App.Settings.AiProviders.ToList();
        FillRunModels();
        FillJuryPicks();
        RefreshJuryUi();
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
        AiKeyBox.Password = p?.ApiKey ?? "";
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
        AiFetchBtn.IsEnabled = false;
        AiModelHint.Text = Loc.AiWorking;
        try
        {
            var ids = await AiClient.ListModelsAsync(CancellationToken.None);
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
        }
        catch (Exception ex)
        {
            AiModelHint.Text = ex.Message;
        }
        finally
        {
            _aiBusy = false;
            AiFetchBtn.IsEnabled = true;
        }
    }

    private async void AiTest_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy) return;
        SaveAiFields();
        _aiBusy = true;
        AiTestBtn.IsEnabled = false;
        AiTestHint.Text = Loc.AiWorking;
        try
        {
            string reply = await AiClient.TestAsync(CancellationToken.None);
            AiTestHint.Text = string.IsNullOrWhiteSpace(reply) ? Loc.AiOk : Loc.AiOk + "  " + reply.Trim();
            SetAiLamp(true);
        }
        catch (Exception ex)
        {
            AiTestHint.Text = ex.Message;
            SetAiLamp(false);
        }
        finally
        {
            _aiBusy = false;
            AiTestBtn.IsEnabled = true;
        }
    }

    private async void AiExplain_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy) return;
        var picked = CurrentCleanList().Where(x => x.Selected).ToList();
        if (picked.Count == 0)
        {
            ShowAlert(Loc.AiTitle, Loc.AiNeedItems);
            return;
        }
        // 和「AI 分析此类」走同一条路：只写「这是什么」那一列，
        // 不改风险档位、不自动勾选任何东西（以前这条会顺手把项勾上，很危险）。
        await ExplainItems(picked);
    }

    bool AiConfigured()
    {
        var p = App.Settings.CurrentProvider();
        return p != null && !string.IsNullOrWhiteSpace(p.BaseUrl) && !string.IsNullOrWhiteSpace(App.Settings.AiModel);
    }

    async Task AutoAnalyze(FileEntry root, CleanReport report)
    {
        if (_chat.Count == 0)
            AddChat(Loc.AiBot, Loc.AiScanSkip);
        RefreshAiLamp();
        await Task.CompletedTask;
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

        FillJuryPicks();
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

    void FillJuryPicks()
    {
        if (JuryPickList == null) return;
        App.Settings.Migrate();
        var chosen = new HashSet<string>(App.Settings.AiJury, StringComparer.OrdinalIgnoreCase);
        var groups = new List<JuryGroup>();
        foreach (var p in App.Settings.AiProviders)
        {
            var models = p.Models.Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (models.Count == 0 && p.Id == App.Settings.AiActiveId && !string.IsNullOrWhiteSpace(App.Settings.AiModel))
                models.Add(App.Settings.AiModel);
            if (models.Count == 0) continue;
            groups.Add(new JuryGroup
            {
                Name = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name,
                Models = models.Select(m => new JuryPick
                {
                    Id = Jury.SeatId(p, m),
                    Label = m,
                    On = chosen.Contains(Jury.SeatId(p, m)),
                }).ToList(),
            });
        }
        JuryPickList.ItemsSource = groups;
        RefreshJuryUi();
        Dispatcher.BeginInvoke(PaintJuryButtons, System.Windows.Threading.DispatcherPriority.Background);
    }

    IEnumerable<JuryPick> AllJuryPicks()
        => JuryPickList.ItemsSource is IEnumerable<JuryGroup> groups
            ? groups.SelectMany(g => g.Models)
            : Enumerable.Empty<JuryPick>();

    private void JuryToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.AiJuryOn = false;
        App.Settings.Save();
        RefreshJuryUi();
        PaintJuryButtons();
        AddChat(Loc.AiBot, Loc.JuryPaused, log: true);
    }

    void RefreshJuryUi()
    {
        // 多模型陪审已废弃（分析固定走当前这一个模型），相关面板一律隐藏
        if (JuryPanel != null)
            JuryPanel.Visibility = Visibility.Collapsed;
    }

    private void JuryPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: JuryPick pick }) return;
        if (!pick.On && AllJuryPicks().Count(x => x.On) >= 4) return;
        pick.On = !pick.On;
        App.Settings.AiJury = AllJuryPicks().Where(x => x.On).Select(x => x.Id).ToList();
        App.Settings.Save();
        PaintJuryButtons();
        RefreshJuryUi();
    }

    void PaintJuryButtons()
    {
        if (JuryPickList == null) return;
        foreach (var btn in FindButtons(JuryPickList))
        {
            if (btn.Tag is not JuryPick pick) continue;
            btn.BorderBrush = ThemeService.Brush(pick.On ? "Accent" : "Border");
            btn.Foreground = ThemeService.Brush(pick.On ? "Accent" : "TextDim");
        }
        if (JuryToggleBtn != null)
        {
            bool on = App.Settings.AiJuryOn;
            JuryToggleBtn.BorderBrush = ThemeService.Brush(on ? "Accent" : "Border");
            JuryToggleBtn.Foreground = ThemeService.Brush(on ? "Accent" : "TextDim");
        }
    }

    static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button b) yield return b;
            foreach (var inner in FindButtons(child)) yield return inner;
        }
    }

    private async void AiRun_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy) return;
        if (_report == null || _root == null)
        {
            AddChat(Loc.AiBot, Loc.AiNeedScanFirst);
            return;
        }
        if (!AiConfigured())
        {
            AddChat(Loc.AiBot, Loc.AiScanSkip);
            return;
        }
        DiskAnalyst.ResetSession();
        _aiNotes.Clear();
        ClearAiSuggested();
        ResetJuryPanes();
        _need = Loc.JuryDefaultNeed;
        _votes.Clear();
        _aiItems.Clear();
        ShowPage(1);
        RefreshCleanUi();
        AddChat(Loc.AiYou, Loc.AiAnalyze);
        ResetJuryPanes();
        await AskAnalyst(DiskAnalyst.Opening(_root, _report, _volumeUsed, _volumeTotal));
    }

    ChatLine AddChat(string who, string text, bool log = false)
    {
        var line = new ChatLine
        {
            Who = who,
            Text = text,
            Log = log,
            Parts = log ? new() : ChatFormat.Parse(text),
        };
        _chat.Add(line);
        return line;
    }

    ChatLine AddPane(JuryPane pane, string who, string text, bool log = false)
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(() => AddPane(pane, who, text, log));
        var line = new ChatLine
        {
            Who = who,
            Text = text,
            Log = log,
            Parts = log ? new() : ChatFormat.Parse(text),
        };
        pane.Lines.Add(line);
        return line;
    }

    void ShowJuryPanes(bool on)
    {
        // 陪审面板已随 AI 分析页一起移除，保留空实现避免到处改调用点
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowJuryPanes(on));
            return;
        }
    }

    void ResetJuryPanes()
    {
        _juryPanes.Clear();
        ShowJuryPanes(false);
    }

    List<AiMsg> PackedTurns()
    {
        if (_turns.Count <= 24) return _turns.ToList();
        return _turns.Skip(_turns.Count - 24).ToList();
    }

    void RefreshAiLamp()
    {
        if (AiStatusText == null) return;
        if (_aiBusy)
        {
            AiStatusText.Text = Loc.AiLampBusy;
            AiStatusText.Foreground = ThemeService.Brush("Accent");
        }
        else if (_aiOk)
        {
            AiStatusText.Text = Loc.AiLampOn;
            AiStatusText.Foreground = ThemeService.Brush("TextDim");
        }
        else if (_aiTried)
        {
            AiStatusText.Text = Loc.AiLampFail;
            AiStatusText.Foreground = ThemeService.Brush("TextDim");
        }
        else if (AiConfigured())
        {
            AiStatusText.Text = Loc.AiReady;
            AiStatusText.Foreground = ThemeService.Brush("TextDim");
        }
        else
        {
            AiStatusText.Text = Loc.AiLampOff;
            AiStatusText.Foreground = ThemeService.Brush("TextDim");
        }
    }

    void SetAiLamp(bool ok)
    {
        _aiTried = true;
        _aiOk = ok;
        RefreshAiLamp();
    }

    async Task<string> AskAnalyst(string user)
    {
        if (_aiBusy) return "";
        _aiBusy = true;
        RefreshAiLamp();
        AiAnalyzeCatBtn.IsEnabled = false;
        AiExplainBtn.IsEnabled = false;
        try
        {
            _turns.Add(new AiMsg { Role = "user", Text = user });
            var live = AddChat(Loc.AiBot, Loc.JuryWaiting, log: true);
            var buf = new System.Text.StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            tick.Tick += (_, _) =>
            {
                if (buf.Length == 0)
                    live.Text = Loc.AiWaiting(sw.Elapsed.Seconds);
            };
            tick.Start();
            void Push(string delta)
            {
                buf.Append(delta);
                string snap = buf.ToString();
                Dispatcher.BeginInvoke(() => live.Text = snap, System.Windows.Threading.DispatcherPriority.Background);
            }
            try
            {
                var reply = await AiClient.StreamAsync(App.Settings.CurrentProvider(), App.Settings.AiModel, DiskAnalyst.SystemPrompt(), PackedTurns(), Push, CancellationToken.None);
                string last = string.IsNullOrWhiteSpace(reply.Text) ? buf.ToString().Trim() : reply.Text.Trim();
                FinishAnalyst(live, last);
                return last;
            }
            finally
            {
                tick.Stop();
            }
        }
        catch (Exception ex)
        {
            if (_turns.Count > 0 && _turns[^1].Role == "user")
                _turns.RemoveAt(_turns.Count - 1);
            SetAiLamp(false);
            AddChat(Loc.AiBot, Loc.AiPartial(AiClient.Pretty(ex)), log: true);
            CollectAiFromNotes();
            ShowAiSuggested();
            return "";
        }
        finally
        {
            _aiBusy = false;
            RefreshAiLamp();
            AiAnalyzeCatBtn.IsEnabled = true;
            AiExplainBtn.IsEnabled = true;
        }
    }

    void FinishAnalyst(ChatLine? live, string last)
    {
        last = StripToolMarkup(last);
        if (string.IsNullOrWhiteSpace(last)) last = Loc.AiOk;
        _turns.Add(new AiMsg { Role = "assistant", Text = last });
        HarvestNotes(last, check: true);
        CollectAiFromNotes();
        SetAiLamp(true);
        if (live != null)
        {
            live.Log = false;
            live.Text = last;
            live.Parts = ChatFormat.Parse(last);
        }
        else
            AddChat(Loc.AiBot, last);
        ShowAiSuggested();
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

    void IAnalystHost.OnChecksChanged(bool showLarge)
    {
        ShowPage(0);
        ShowRightTab(0);
        // 分类现在按用途动态生成，没有固定的「大文件」位置，保持当前分类即可
        RefreshCleanUi();
        PaintAiNotes();
    }

    void CollectAiFromNotes()
    {
        var list = new List<CleanItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in AllCleanItems().Where(x => x.AiSuggested))
        {
            if (!seen.Add(NormPath(item.FullPath))) continue;
            list.Add(item);
        }
        foreach (var (path, note) in _aiNotes)
        {
            if (!seen.Add(path) || IsProtectedSuggest(path)) continue;
            var hit = AllCleanItems().FirstOrDefault(x => string.Equals(NormPath(x.FullPath), path, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
            {
                hit.AiSuggested = true;
                if (!string.IsNullOrWhiteSpace(note)) hit.Reason = note;
                list.Add(hit);
                continue;
            }
            list.Add(new CleanItem
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                Reason = note,
                Group = Loc.CatAi,
                AiSuggested = true,
                CanDelete = true,
            });
        }
        _aiItems = list;
    }

    void HarvestNotes(string text, bool check)
    {
        if (string.IsNullOrWhiteSpace(text) || _report == null) return;
        int n = 0;
        foreach (var part in ChatFormat.Parse(text))
        {
            if (part.Kind != ChatPartKind.Path || string.IsNullOrEmpty(part.Path)) continue;
            if (n++ >= 40) break;
            string key = NormPath(part.Path);
            if (key.Length < 4) continue;
            bool keep = part.Section == Loc.SecKeep || part.Section == Loc.SecFolders;
            string note = string.IsNullOrWhiteSpace(part.Note) ? Loc.AiMark : part.Note;
            ApplySuggest(key, note, check: check && !keep);
        }
        Dispatcher.BeginInvoke(() => { PaintAiNotes(); CollectAiFromNotes(); ShowAiSuggested(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    void IAnalystHost.OnSuggest(string path, string note)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        ApplySuggest(path, string.IsNullOrWhiteSpace(note) ? Loc.AiMark : note.Trim(), check: true);
        Dispatcher.BeginInvoke(() => { PaintAiNotes(); CollectAiFromNotes(); ShowAiSuggested(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    void ShowAiSuggested()
    {
        RefreshCleanUi();
        ShowRightTab(0);
    }

    void ApplySuggest(string path, string note, bool check)
    {
        string key = NormPath(path);
        if (key.Length == 0 || IsProtectedSuggest(key)) return;
        _aiNotes[key] = ClipNote(note);
        string? parent = Path.GetDirectoryName(key);
        int depth = 0;
        while (!string.IsNullOrEmpty(parent) && parent.Length >= 3 && depth++ < 24)
        {
            string p = NormPath(parent);
            if (p.Length <= 3 || IsProtectedSuggest(p)) break;
            if (!_aiNotes.ContainsKey(p) || _aiNotes[p] == Loc.AiMark)
                _aiNotes[p] = Loc.AiInside(ClipNote(note));
            parent = Path.GetDirectoryName(p);
        }
        if (!check || _report == null) return;
        var item = AllCleanItems().FirstOrDefault(x => string.Equals(NormPath(x.FullPath), key, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            item = AllCleanItems().FirstOrDefault(x =>
                !string.IsNullOrEmpty(x.FullPath)
                && key.StartsWith(NormPath(x.FullPath) + "\\", StringComparison.OrdinalIgnoreCase)
                && DiskAnalyst.CanAiCheck(x));
        }
        if (item == null || !DiskAnalyst.CanAiCheck(item)) return;
        item.Selected = true;
        item.AiSuggested = true;
        if (!string.IsNullOrWhiteSpace(note) && note != Loc.AiMark)
            item.Reason = ClipNote(note);
    }

    IEnumerable<CleanItem> AllCleanItems()
    {
        if (_report == null) yield break;
        foreach (var x in _report.Cleanable) yield return x;
        foreach (var x in _report.LargeFiles) yield return x;
        foreach (var x in _report.OldFiles) yield return x;
        foreach (var x in _report.Duplicates) yield return x;
    }

    static bool IsProtectedSuggest(string path)
    {
        string p = path.ToLowerInvariant();
        if (p is "c:" or "c:\\" or "d:" or "d:\\") return true;
        if (p is @"c:\windows" or @"c:\users" or @"c:\program files" or @"c:\program files (x86)" or @"c:\programdata")
            return true;
        if (p.Contains(@"\windows\winsxs")) return true;
        return false;
    }

    static string ClipNote(string note)
    {
        string t = (note ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (t.Equals("AI", StringComparison.OrdinalIgnoreCase) || t == Loc.AiMark) return Loc.AiMark;
        return t.Length > 40 ? t[..37] + "…" : t;
    }

    static string NormPath(string? p)
        => (p ?? "").Replace('/', '\\').Trim().TrimEnd('\\');

    (bool Hit, string Note) MarkFor(string? path)
    {
        string key = NormPath(path);
        if (key.Length == 0) return (false, "");
        if (_aiNotes.TryGetValue(key, out var exact))
            return (true, exact);
        return (false, "");
    }

    void PaintAiNotes()
    {
        foreach (var obj in DirTree.Items)
            if (obj is TreeViewItem item)
                PaintItem(item);
    }

    void PaintItem(TreeViewItem item)
    {
        if (item.Tag is FileEntry e)
            item.Header = MakeFolderHeader(e, ReferenceEquals(e, _root));
        foreach (var child in item.Items)
            if (child is TreeViewItem t && t.Tag is FileEntry)
                PaintItem(t);
    }

    private void ChatPath_Click(object sender, RoutedEventArgs e)
    {
        if (e is not PathClickEventArgs { Path: string path } || string.IsNullOrWhiteSpace(path)) return;
        bool dir = Directory.Exists(path) || path.EndsWith('\\');
        OpenExplorer(path, dir);
    }

    void JumpToPath(string path)
    {
        string key = NormPath(path);
        if (key.Length < 3 || _root == null) return;
        if (!_aiNotes.ContainsKey(key))
            ApplySuggest(key, NearbyNote(key) ?? Loc.AiMark, check: false);
        var item = RevealInTree(key);
        if (item == null) return;
        item.IsSelected = true;
        item.BringIntoView();
        if (item.Tag is FileEntry e)
            ShowDirectory(e.IsDirectory ? e : e.Parent ?? e);
        PaintAiNotes();
    }

    string? NearbyNote(string key)
    {
        if (_aiNotes.TryGetValue(key, out var n) && n != Loc.AiMark) return n;
        string? parent = Path.GetDirectoryName(key);
        while (!string.IsNullOrEmpty(parent) && parent.Length >= 3)
        {
            string p = NormPath(parent);
            if (_aiNotes.TryGetValue(p, out var note) && note != Loc.AiMark && !note.StartsWith("内有") && !note.StartsWith("inside:"))
                return note;
            parent = Path.GetDirectoryName(p);
        }
        return null;
    }

    TreeViewItem? RevealInTree(string path)
    {
        if (DirTree.Items.Count == 0 || DirTree.Items[0] is not TreeViewItem rootItem) return null;
        string want = NormPath(path);
        var item = rootItem;
        EnsureTreeChildren(item);
        while (true)
        {
            TreeViewItem? next = null;
            foreach (var obj in item.Items)
            {
                if (obj is not TreeViewItem child || child.Tag is not FileEntry e) continue;
                string cur = NormPath(e.FullPath);
                if (string.Equals(cur, want, StringComparison.OrdinalIgnoreCase)
                    || want.StartsWith(cur + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }
            if (next == null) return item;
            item = next;
            string here = NormPath((item.Tag as FileEntry)?.FullPath);
            if (string.Equals(here, want, StringComparison.OrdinalIgnoreCase)) return item;
            item.IsExpanded = true;
            EnsureTreeChildren(item);
        }
    }

    void EnsureTreeChildren(TreeViewItem item)
    {
        if (item.Tag is not FileEntry e || !e.IsDirectory) return;
        bool stub = item.Items.Count == 1 && (item.Items[0] as TreeViewItem)?.Tag == Placeholder;
        if (stub || (item.Items.Count == 0 && e.Children.Count > 0))
            PopulateDirChildren(item);
    }

    // ===== AI：分析当前选中的分类 =====

    CancellationTokenSource? _aiStop;

    private async void AiAnalyzeCat_Click(object sender, RoutedEventArgs e)
        => await AnalyzeCurrentCategory();

    private void AiStop_Click(object sender, RoutedEventArgs e)
    {
        try { _aiStop?.Cancel(); } catch { }
    }

    async Task AnalyzeCurrentCategory()
    {
        if (_aiBusy) return;
        if (_report == null)
        {
            SetAiStatus(Loc.AiNeedScanFirst);
            return;
        }
        var items = CurrentCleanList();
        if (items.Count == 0)
        {
            SetAiStatus(Loc.AiCatEmpty);
            return;
        }
        await ExplainItems(items);
    }

    void SetAiStatus(string text)
    {
        if (AiStatusText == null) return;
        AiStatusText.Text = text;
        AiStatusText.Foreground = ThemeService.Brush("TextDim");
    }

    /// <summary>
    /// 把条目交给 AI，让它逐条写一句「这是什么」，写回条目的 AiNote。
    /// AI 不碰风险档位，也不碰勾选：风险全部由规则（AppSignatures / CleanAnalyzer）判定，
    /// 可审计、可复现，不会出现「同一份盘两次分析给出两种颜色」，更不会偷偷帮你勾上东西。
    /// </summary>
    async Task ExplainItems(List<CleanItem> items)
    {
        if (!AiConfigured())
        {
            SetAiStatus(Loc.AiScanSkip);
            return;
        }

        _aiBusy = true;
        _aiStop = new CancellationTokenSource();
        RefreshAiLamp();
        AiAnalyzeCatBtn.IsEnabled = false;
        AiExplainBtn.IsEnabled = false;
        AiStopBtn.Visibility = Visibility.Visible;
        SetCleanProgress(0, Loc.AiWorking, determinate: false);

        try
        {
            // 只送前 60 条，避免 prompt 过大把中转打挂。
            // 输入用「- 路径 (类型, 大小)」，输出才用 GOTO，两种格式刻意不同，模型不容易照抄输入。
            var batch = items.OrderByDescending(x => x.Size).Take(60).ToList();
            var lines = batch.Select(x =>
                $"- {x.FullPath} ({(x.IsDirectory ? Loc.Folder : Loc.FilesCol)}, {x.SizeText})");
            string user = Loc.AiCatListHeader + Environment.NewLine + string.Join(Environment.NewLine, lines);

            var turns = new List<AiMsg> { new() { Role = "user", Text = user } };
            var buf = new System.Text.StringBuilder();
            void Push(string delta) => buf.Append(delta);

            var reply = await AiClient.StreamAsync(
                App.Settings.CurrentProvider(), App.Settings.AiModel,
                Loc.AiCatSystem, turns, Push, _aiStop.Token);

            if (_aiStop.IsCancellationRequested)
            {
                SetAiStatus(Loc.AiCatStopped);
                return;
            }

            string text = string.IsNullOrWhiteSpace(reply.Text) ? buf.ToString() : reply.Text;
            int applied = AiNoteParser.Apply(batch, text);
            SetAiLamp(true);
            SetAiStatus(applied > 0 ? Loc.AiCatDone(applied) : Loc.AiNoItems);
        }
        catch (Exception ex)
        {
            SetAiLamp(false);
            SetAiStatus(Loc.AiPartial(AiClient.Pretty(ex)));
        }
        finally
        {
            _aiBusy = false;
            _aiStop?.Dispose();
            _aiStop = null;
            RefreshAiLamp();
            AiAnalyzeCatBtn.IsEnabled = true;
            AiExplainBtn.IsEnabled = true;
            AiStopBtn.Visibility = Visibility.Collapsed;
            HideCleanProgress();
            // AI 只写 AiNote，风险/分组没变，所以不重建分类、不重设 ItemsSource
            // （重设会把表格滚动位置和勾选状态弄丢）。AiNote 走属性通知，单元格自己刷新。
            UpdateCleanSelHint();
        }
    }

    static bool LooksLikeConfirm(string text)
    {
        string t = text.Trim().ToLowerInvariant();
        return t is "确认" or "好" or "可以" or "勾上" or "ok" or "yes" or "confirm" or "do it";
    }

    async Task RunJury()
    {
        if (_root == null || _report == null) return;
        var seats = Jury.Seats();
        if (seats.Count == 0)
        {
            AddChat(Loc.AiBot, Loc.AiScanSkip);
            return;
        }
        _aiBusy = true;
        RefreshAiLamp();
        AiAnalyzeCatBtn.IsEnabled = false;
        AiExplainBtn.IsEnabled = false;
        try
        {
            _juryPanes.Clear();
            var panes = seats.Select(s => new JuryPane { Title = s.Model }).ToList();
            foreach (var pane in panes) _juryPanes.Add(pane);
            ShowJuryPanes(true);
            string opening = DiskAnalyst.Opening(_root, _report, _volumeUsed, _volumeTotal);
            string user = Loc.SecNeed + "\n" + (_need ?? "") + "\n\n" + opening;
            var tasks = seats.Select((seat, i) => RunSeat(seat, panes[i], user)).ToList();
            var results = await Task.WhenAll(tasks);
            var ok = new List<(JurySeat Seat, string Text)>();
            foreach (var r in results)
            {
                if (string.IsNullOrEmpty(r.Text)) continue;
                ok.Add((r.Seat, r.Text));
            }
            _votes = Jury.Tally(ok);
            string board = Jury.Render(seats, ok, _votes);
            foreach (var v in _votes)
                ApplySuggest(v.Path, $"{v.Grade} · {v.Note}", check: false);
            RebuildAiItems(seats);
            ApplyJuryChecks();
            PaintAiNotes();
            RefreshCleanUi();
            SetAiLamp(ok.Count > 0);
            var merge = new JuryPane { Title = Loc.JuryMerge, LiveText = string.IsNullOrEmpty(board) ? Loc.JuryNone : board };
            _juryPanes.Add(merge);
            _turns.Clear();
            _turns.Add(new AiMsg { Role = "user", Text = user });
            _turns.Add(new AiMsg { Role = "assistant", Text = board });
        }
        catch (Exception ex)
        {
            SetAiLamp(false);
            AddChat(Loc.AiBot, ex.Message);
        }
        finally
        {
            _aiBusy = false;
            RefreshAiLamp();
            AiAnalyzeCatBtn.IsEnabled = true;
            AiExplainBtn.IsEnabled = true;
        }
    }

    async Task<(JurySeat Seat, string Text)> RunSeat(JurySeat seat, JuryPane pane, string user)
    {
        Dispatcher.Invoke(() => pane.LiveText = Loc.JuryThinking + "\n" + (seat.Provider.BaseUrl ?? ""));
        try
        {
            Dispatcher.Invoke(() => pane.LiveText = Loc.JuryWaiting + "\n" + (seat.Provider.BaseUrl ?? "") + "\n" + seat.Model);
            var buf = new System.Text.StringBuilder();
            void Push(string delta)
            {
                if (delta.StartsWith("流式失败", StringComparison.Ordinal) || delta.StartsWith("stream failed", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.BeginInvoke(() => pane.LiveText = delta, System.Windows.Threading.DispatcherPriority.Background);
                    return;
                }
                buf.Append(delta);
                string snap = buf.ToString();
                Dispatcher.BeginInvoke(() => pane.LiveText = snap, System.Windows.Threading.DispatcherPriority.Background);
            }
            var reply = await AiClient.StreamAsync(seat.Provider, seat.Model, Loc.JurySystem,
                new[] { new AiMsg { Role = "user", Text = user } }, Push, CancellationToken.None);
            string text = string.IsNullOrWhiteSpace(reply.Text) ? buf.ToString().Trim() : reply.Text.Trim();
            Dispatcher.Invoke(() => pane.LiveText = string.IsNullOrEmpty(text) ? Loc.JurySeatEmpty(seat.Model) : text);
            return (seat, text);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => pane.LiveText = Loc.JurySeatFail(seat.Label, AiClient.Pretty(ex)));
            return (seat, "");
        }
    }

    void ApplyJuryChecks()
    {
        foreach (var v in _votes.Where(x => x.Grade == Loc.GradeHigh || (x.Grade == Loc.GradeMid && _votes.Count(y => y.Grade == Loc.GradeHigh) == 0)))
            ApplySuggest(v.Path, $"{v.Grade} · {v.Note}", check: true);
        foreach (var item in _aiItems)
            item.Selected = _votes.Any(v => string.Equals(NormPath(v.Path), NormPath(item.FullPath), StringComparison.OrdinalIgnoreCase)
                && (v.Grade == Loc.GradeHigh || (v.Grade == Loc.GradeMid && _votes.Count(y => y.Grade == Loc.GradeHigh) == 0)));
        PaintAiNotes();
        RefreshCleanUi();
    }

    void RebuildAiItems(IReadOnlyList<JurySeat> seats)
    {
        var list = new List<CleanItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in _votes)
        {
            if (!seen.Add(v.Path)) continue;
            string why = $"{v.Grade} · {v.Votes}/{seats.Count} · {Loc.JuryVotedYes(v.Voters.Select(s => s.Contains('/') ? s[(s.LastIndexOf('/') + 1)..].Trim() : s))}";
            if (!string.IsNullOrWhiteSpace(v.Note)) why += " · " + v.Note;
            var hit = AllCleanItems().FirstOrDefault(x => string.Equals(NormPath(x.FullPath), v.Path, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
            {
                hit.AiSuggested = true;
                hit.Reason = why;
                list.Add(hit);
                continue;
            }
            list.Add(new CleanItem
            {
                Name = v.Name,
                FullPath = v.Path,
                Size = LookupSize(v.Path),
                Reason = why,
                Group = Loc.CatAi,
                AiSuggested = true,
                CanDelete = true,
            });
        }
        _aiItems = list;
    }

    long LookupSize(string path)
    {
        var hit = AllCleanItems().FirstOrDefault(x => string.Equals(NormPath(x.FullPath), path, StringComparison.OrdinalIgnoreCase));
        return hit?.Size ?? 0;
    }

    private void AiChatClear_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy) return;
        _chat.Clear();
        _turns.Clear();
        _aiNotes.Clear();
        ClearAiSuggested();
        ResetJuryPanes();
        _need = null;
        _votes.Clear();
        _aiItems.Clear();
        DiskAnalyst.ResetSession();
        if (_root != null) PaintAiNotes();
        RefreshCleanUi();
    }

    void ClearAiSuggested()
    {
        foreach (var x in AllCleanItems())
        {
            if (!x.AiSuggested) continue;
            x.AiSuggested = false;
            x.Selected = false;
        }
    }

    /// <summary>
    /// 右键「问 AI 这是什么」：单轮问答，答案用弹窗显示。
    /// 不再走 AskAnalyst —— 那条路会按 DELETABLE/KEEP 自动帮你勾选，
    /// 而且 AI 分析页已经删掉了，回答根本没地方显示。
    /// </summary>
    private async void CtxAskAi_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy) return;
        if (ContextEntry() is not { IsDirectory: true } dir || dir.IsFilesGroup) return;
        if (!AiConfigured())
        {
            ShowAlert(Loc.AiColNote, Loc.AiScanSkip);
            return;
        }

        _aiBusy = true;
        RefreshAiLamp();
        SetAiStatus(Loc.AiWorking);
        try
        {
            var turns = new List<AiMsg> { new() { Role = "user", Text = DiskAnalyst.FolderAsk(dir) } };
            var reply = await AiClient.StreamAsync(
                App.Settings.CurrentProvider(), App.Settings.AiModel,
                Loc.AiFolderAskSystem, turns, _ => { }, CancellationToken.None);

            string text = StripToolMarkup(reply.Text ?? "").Trim();
            SetAiLamp(true);
            SetAiStatus(text.Length > 0 ? Loc.AiOk : Loc.AiNoItems);
            ShowAlert(dir.Name + " · " + Loc.AiColNote, text.Length > 0 ? text : Loc.AiNoItems);
        }
        catch (Exception ex)
        {
            SetAiLamp(false);
            SetAiStatus(Loc.AiPartial(AiClient.Pretty(ex)));
        }
        finally
        {
            _aiBusy = false;
            RefreshAiLamp();
        }
    }

    private void RepoLink_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Loc.Repo) { UseShellExecute = true });
        }
        catch { }
    }

    private void NavBrowse_Click(object sender, RoutedEventArgs e) => ShowPage(0);
    private void NavAi_Click(object sender, RoutedEventArgs e) => ShowPage(1);

    void ShowPage(int page)
    {
        _page = page;
        // AI 分析页和左侧导航都已移除，当前只有浏览这一页，
        // 分析入口在右侧清理面板里。
        if (BrowsePage != null)
            BrowsePage.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (page == 0) RefreshCleanUi();
    }

    private void TabClean_Click(object sender, RoutedEventArgs e) => ShowRightTab(0);
    private void TabExt_Click(object sender, RoutedEventArgs e) => ShowRightTab(1);
    private void TabUninstall_Click(object sender, RoutedEventArgs e)
    {
        ShowRightTab(2);
        if (_apps.Count == 0 && !_listingApps) _ = LoadApps();
    }

    private void ShowRightTab(int tab)
    {
        _rightTab = tab;
        if (CleanPane != null)
            CleanPane.Visibility = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExtPane.Visibility = tab == 1 ? Visibility.Visible : Visibility.Collapsed;
        UninstallPane.Visibility = tab == 2 ? Visibility.Visible : Visibility.Collapsed;
        MarkTab(TabCleanBtn, tab == 0);
        MarkTab(TabExtBtn, tab == 1);
        MarkTab(TabUninstallBtn, tab == 2);
    }

    private static void MarkTab(Button b, bool on)
    {
        b.BorderBrush = ThemeService.Brush(on ? "Accent" : "Border");
        b.Foreground = ThemeService.Brush(on ? "Accent" : "TextDim");
    }

    private void RefreshCleanUi()
    {
        if (CatList == null) return;
        int keep = _catIndex;
        BuildCategories();
        CatList.ItemsSource = _cats;
        _catIndex = keep >= 0 && keep < _cats.Count ? keep : 0;
        ShowCleanCat();
        if (_report == null)
            CleanSummary.Text = Loc.AnalyzeAfterScan;
        else
            CleanSummary.Text = Loc.CleanHintReady(_report.Cleanable.Count, FileEntry.FormatSize(_report.CleanableBytes));
        UpdateCleanSelHint();
        ShowRightTab(_rightTab);
    }

    /// <summary>把所有可清理候选按用途归类，只保留有内容的分类，算出占比，按空间降序。</summary>
    void BuildCategories()
    {
        _cats.Clear();
        if (_report == null) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<CleanItem>();
        foreach (var x in AllCandidates())
        {
            if (string.IsNullOrEmpty(x.FullPath)) continue;
            if (!seen.Add(NormPath(x.FullPath))) continue;
            all.Add(x);
        }

        var groups = all
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Group) ? Loc.CatOther : x.Group)
            .OrderByDescending(g => g.Sum(x => x.Size))
            .ToList();

        long total = groups.Sum(g => g.Sum(x => x.Size));
        foreach (var g in groups)
        {
            long bytes = g.Sum(x => x.Size);
            _cats.Add(new CatRow
            {
                Name = g.Key,
                Items = g.OrderByDescending(x => x.Size).ToList(),
                Percent = total > 0 ? bytes * 100.0 / total : 0,
            });
        }
    }

    /// <summary>点分类列表里的一行，切换当前分类。</summary>
    private void CatList_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        // 从点到的数据行取回对应的 CatRow
        var row = Ancestor<Border>(src);
        if (row?.DataContext is not CatRow cat) return;
        int idx = _cats.IndexOf(cat);
        if (idx < 0 || idx == _catIndex) return;
        _catIndex = idx;
        ShowCleanCat();
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

    /// <summary>参与「按用途分类」的全部候选条目。</summary>
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

    private static string Label(string title, List<CleanItem>? items)
    {
        if (items == null || items.Count == 0) return title + "  ·  0";
        return title + "  ·  " + Loc.CatCount(items.Count, FileEntry.FormatSize(items.Sum(x => x.Size)));
    }

    private void ShowCleanCat()
    {
        if (_report == null)
        {
            CleanGrid.ItemsSource = null;
            return;
        }
        CleanGrid.ItemsSource = CurrentCleanList();
        var list = CurrentCleanList();
        int safe = list.Count(x => x.Risk == CleanRisk.Safe);
        int confirm = list.Count(x => x.Risk == CleanRisk.Confirm);
        int keep = list.Count(x => x.Risk == CleanRisk.Keep);
        CleanSummary.Text = Label(CurrentCatName(), list) + "   " + Loc.RiskSummary(safe, confirm, keep);
        UpdateCleanSelHint();
    }

    string CurrentCatName()
        => _catIndex >= 0 && _catIndex < _cats.Count
            ? _cats[_catIndex].Name
            : Loc.CatOther;

    private List<CleanItem> CurrentCleanList()
    {
        if (_report == null) return new List<CleanItem>();
        if (_catIndex >= 0 && _catIndex < _cats.Count)
            return _cats[_catIndex].Items;
        return new List<CleanItem>();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool on = !IsAllSelected();
        foreach (var item in CurrentCleanList())
            item.Selected = on && item.CanDelete;
        UpdateCleanSelHint();
        CleanGrid.Items.Refresh();
    }

    private void SelectSafe_Click(object sender, RoutedEventArgs e)
    {
        bool on = !IsSafeSelected();
        foreach (var item in CurrentCleanList())
            item.Selected = on && item.CanDelete && item.Risk == CleanRisk.Safe;
        UpdateCleanSelHint();
        CleanGrid.Items.Refresh();
    }

    private bool IsSafeSelected()
    {
        var list = CurrentCleanList().Where(x => x.CanDelete && x.Risk == CleanRisk.Safe).ToList();
        return list.Count > 0 && list.All(x => x.Selected);
    }

    private bool IsAllSelected()
    {
        var list = CurrentCleanList().Where(x => x.CanDelete).ToList();
        return list.Count > 0 && list.All(x => x.Selected);
    }

    private void HighlightSelectMode()
    {
        bool all = IsAllSelected();
        bool safe = !all && IsSafeSelected();
        MarkSelectBtn(SelectAllBtn, all);
        MarkSelectBtn(SelectSafeBtn, safe);
    }

    private static void MarkSelectBtn(Button b, bool on)
    {
        b.BorderBrush = ThemeService.Brush(on ? "Accent" : "Border");
        b.Foreground = ThemeService.Brush(on ? "Accent" : "TextDim");
    }

    private void UpdateCleanSelHint()
    {
        var picked = CurrentCleanList().Where(x => x.Selected && x.CanDelete).ToList();
        CleanSelHint.Text = picked.Count == 0
            ? ""
            : Loc.CatCount(picked.Count, FileEntry.FormatSize(picked.Sum(x => x.Size)));
        HighlightSelectMode();
    }

    private void CleanGrid_Click(object sender, MouseButtonEventArgs e) => UpdateCleanSelHint();

    private async void UninstallRefresh_Click(object sender, RoutedEventArgs e) => await LoadApps();

    private async Task LoadApps()
    {
        if (_listingApps) return;
        _listingApps = true;
        UninstallRefreshBtn.IsEnabled = false;
        UninstallRunBtn.IsEnabled = false;
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
            var list = await Task.Run(() => BcuUninstallService.ListApps(progress, CancellationToken.None));
            foreach (var app in list)
            {
                try { app.Icon = BcuUninstallService.ToImage(app.IconBytes); }
                catch { app.Icon = null; }
                app.IconBytes = null;
            }
            _apps = list;
            _junk.Clear();
            ShowAppList();
        }
        catch (Exception ex)
        {
            ShowAlert(Loc.TabUninstall, ex.Message);
        }
        finally
        {
            _listingApps = false;
            UninstallRefreshBtn.IsEnabled = true;
            UninstallRunBtn.IsEnabled = true;
            UninstallProgressPanel.Visibility = Visibility.Collapsed;
            UninstallProgressBar.IsIndeterminate = false;
        }
    }

    private IEnumerable<AppUninstallItem> VisibleApps()
        => UninstallGrid.Items.OfType<AppUninstallItem>();

    private void UninstallSelectAll_Click(object sender, RoutedEventArgs e)
    {
        var vis = VisibleApps().Where(x => x.CanUninstall).ToList();
        bool allOn = vis.Count > 0 && vis.All(x => x.Selected);
        foreach (var a in vis)
            a.Selected = !allOn;
        UpdateUninstallSelHint();
    }

    private void UninstallGrid_Click(object sender, MouseButtonEventArgs e) => UpdateUninstallSelHint();

    private void UninstallGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UninstallGrid.SelectedItem is not AppUninstallItem app) return;
        OpenFolder(app.InstallLocation);
    }

    private void JunkGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (JunkGrid.SelectedItem is not JunkItem junk) return;
        OpenFolder(junk.Path);
    }

    static void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
                return;
            }
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                return;
            }
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void UpdateUninstallSelHint()
    {
        var picked = _apps.Where(x => x.Selected && x.CanUninstall).ToList();
        UninstallSelHint.Text = picked.Count == 0 ? "" : Loc.UninstallCount(picked.Count);
        var vis = VisibleApps().Where(x => x.CanUninstall).ToList();
        bool allOn = vis.Count > 0 && vis.All(x => x.Selected);
        UninstallAllBtn.BorderBrush = ThemeService.Brush(allOn ? "Accent" : "Border");
        UninstallAllBtn.Foreground = ThemeService.Brush(allOn ? "Accent" : "TextDim");
    }

    private void UninstallRun_Click(object sender, RoutedEventArgs e)
    {
        var picked = _apps.Where(x => x.Selected && x.CanUninstall).ToList();
        if (picked.Count == 0)
        {
            ShowAlert(Loc.TabUninstall, Loc.NothingSelected);
            return;
        }
        int features = picked.Count(x => x.GroupKey == 2);
        string msg = features > 0
            ? Loc.UninstallConfirmFeatures(picked.Count, features)
            : Loc.UninstallConfirm(picked.Count);
        AskConfirm(Loc.TabUninstall, msg, () => RunUninstall(picked));
    }

    private void RunUninstall(List<AppUninstallItem> picked)
    {
        try
        {
            _uninstallTask?.Dispose();
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
                UninstallStatus.Skipped => Loc.UninstallFailed,
                _ => app.Status,
            };
        }
        int done = task.AllUninstallersList.Count(x => x.Finished);
        int total = task.AllUninstallersList.Count;
        UninstallProgressBar.IsIndeterminate = false;
        UninstallProgressBar.Value = total == 0 ? 0 : done * 100.0 / total;
        UninstallProgressText.Text = Loc.UninstallRunning + $" {done}/{total}";
        if (!task.Finished) return;
        UninstallRunBtn.IsEnabled = true;
        int ok = task.AllUninstallersList.Count(x => x.CurrentStatus == UninstallStatus.Completed);
        int fail = task.AllUninstallersList.Count(x => x.CurrentStatus == UninstallStatus.Failed);
        HeaderStats.Text = fail == 0 ? Loc.UninstallDone : $"{Loc.UninstallDone} {ok}, {Loc.UninstallFailed} {fail}";
        var finished = task.AllUninstallersList
            .Where(x => x.CurrentStatus == UninstallStatus.Completed && x.UninstallerEntry != null)
            .Select(x => x.UninstallerEntry)
            .ToList();
        if (finished.Count > 0) _ = ScanLeftovers(finished);
        else UninstallProgressPanel.Visibility = Visibility.Collapsed;
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

    private void BindAppList()
    {
        var view = CollectionViewSource.GetDefaultView(_apps);
        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Clear();
            view.SortDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AppUninstallItem.GroupKey)));
            view.SortDescriptions.Add(new SortDescription(nameof(AppUninstallItem.GroupKey), ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription(nameof(AppUninstallItem.SizeBytes), ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(nameof(AppUninstallItem.Name), ListSortDirection.Ascending));
            view.Filter = FilterApp;
        }
        UninstallGrid.ItemsSource = view;
        ApplyUninstallFilter();
    }

    private bool FilterApp(object obj)
    {
        if (obj is not AppUninstallItem a) return false;
        string q = UninstallSearchBox.Text?.Trim() ?? "";
        if (q.Length == 0) return true;
        return a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || (a.Publisher?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || (a.InstallLocation?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || (a.Status?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private bool FilterJunk(object obj)
    {
        if (obj is not JunkItem j) return false;
        string q = UninstallSearchBox.Text?.Trim() ?? "";
        if (q.Length == 0) return true;
        return j.AppName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || (j.Path?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || (j.Category?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private void UninstallSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        UninstallSearchHint.Visibility = string.IsNullOrEmpty(UninstallSearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyUninstallFilter();
    }

    private void ApplyUninstallFilter()
    {
        if (_showingJunk)
        {
            if (JunkGrid.ItemsSource is ICollectionView jv)
            {
                jv.Filter = FilterJunk;
                jv.Refresh();
            }
            int shown = JunkGrid.Items.Count;
            UninstallSummary.Text = _junk.Count == 0
                ? Loc.JunkNone
                : Loc.JunkHint(_junk.Count, _junk.Count(x => x.Safe))
                  + (string.IsNullOrWhiteSpace(UninstallSearchBox.Text) ? "" : "  ·  " + Loc.UninstallFiltered(shown, _junk.Count));
            UpdateJunkSelHint();
            return;
        }
        if (UninstallGrid.ItemsSource is ICollectionView av)
        {
            av.Filter = FilterApp;
            av.Refresh();
        }
        int n = UninstallGrid.Items.Count;
        if (_apps.Count == 0) UninstallSummary.Text = Loc.UninstallHint;
        else if (string.IsNullOrWhiteSpace(UninstallSearchBox.Text)) UninstallSummary.Text = Loc.UninstallCount(_apps.Count);
        else UninstallSummary.Text = Loc.UninstallFiltered(n, _apps.Count);
        UpdateUninstallSelHint();
    }

    private void RefreshUninstallPaneText() => ApplyUninstallFilter();

    private async Task ScanLeftovers(List<ApplicationUninstallerEntry> finished)
    {
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
            var list = await Task.Run(() => BcuUninstallService.FindLeftovers(finished, all, progress, CancellationToken.None));
            _junk = list;
            ShowJunkList();
        }
        catch (Exception ex)
        {
            ShowAlert(Loc.TabUninstall, ex.Message);
        }
        finally
        {
            UninstallProgressPanel.Visibility = Visibility.Collapsed;
            UninstallProgressBar.IsIndeterminate = false;
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
            view.Filter = FilterJunk;
        }
        JunkGrid.ItemsSource = view;
        ApplyUninstallFilter();
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
            int safe = _junk.Count(x => x.Safe);
            UninstallSummary.Text = _junk.Count == 0 ? Loc.JunkNone : Loc.JunkHint(_junk.Count, safe);
            UpdateJunkSelHint();
            HeaderStats.Text = Loc.JunkDeleted(ok, fail);
        });
    }

    private void CleanGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CleanGrid.SelectedItem is not CleanItem item) return;
        string path = item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath) ?? item.FullPath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (item.IsDirectory)
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + item.FullPath + "\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void RecycleSelected_Click(object sender, RoutedEventArgs e)
    {
        var picked = CurrentCleanList().Where(x => x.Selected && x.CanDelete && !string.IsNullOrEmpty(x.FullPath)).ToList();
        if (picked.Count == 0)
        {
            ShowAlert(Loc.DeleteToRecycle, Loc.NothingSelected);
            return;
        }
        long bytes = picked.Sum(x => x.Size);
        AskConfirm(
            Loc.DeleteToRecycle,
            Loc.RecycleManyConfirm(picked.Count, FileEntry.FormatSize(bytes)),
            () => RecyclePicked(picked));
    }

    private void RecyclePicked(List<CleanItem> picked)
    {
        int ok = 0;
        var failed = new List<string>();
        var gone = new HashSet<CleanItem>();
        foreach (var item in picked)
        {
            if (item.Entry != null && RecycleService.IsProtected(item.Entry))
            {
                failed.Add(item.Name);
                continue;
            }
            try
            {
                RecycleService.SendToRecycle(item.FullPath);
                ok++;
                gone.Add(item);
                if (item.Entry?.Parent != null)
                {
                    item.Entry.Parent.Children.Remove(item.Entry);
                    RecalcUp(item.Entry.Parent);
                }
            }
            catch
            {
                failed.Add(item.Name);
            }
        }

        if (_report != null)
        {
            foreach (var list in new[]
            {
                _report.Cleanable, _report.LargeFiles, _report.OldFiles, _report.Duplicates,
                _report.EmptyFolders, _report.BrokenShortcuts, _report.LongPaths,
            })
                list.RemoveAll(gone.Contains);
            _report.CleanableBytes = _report.Cleanable.Sum(x => x.Size);
        }
        if (_root != null)
        {
            PopulateTree();
            ShowDirectory(_current ?? _root);
        }
        RefreshCleanUi();
        HeaderStats.Text = Loc.RecycleManyOk(ok);
        if (failed.Count > 0)
            ShowAlert(Loc.DeleteToRecycle, Loc.DeleteFailed(string.Join(", ", failed.Take(8))));
    }
}
