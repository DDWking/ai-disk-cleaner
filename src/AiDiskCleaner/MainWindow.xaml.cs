using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

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

public partial class MainWindow : Window
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
    private bool _cleanTab = true;
    private Action? _confirmYes;

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
        BorderBrush = ThemeService.Brush("Border");
        BorderThickness = new Thickness(1);
        ApplyUi();
        ShowRightTab(true);
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
        CtxCopyPath.Header = Loc.CopyPath;
        CtxCopyName.Header = Loc.CopyName;
        CtxDelete.Header = Loc.DeleteToRecycle;
        CtxProps.Header = Loc.Properties;
        ExtTitle.Text = Loc.ExtType;
        ColExt.Header = Loc.Ext;
        ColType.Header = Loc.Type;
        ColPct.Header = Loc.Pct;
        ColSize.Header = Loc.Size;
        TabExtBtn.Content = Loc.TabExt;
        TabCleanBtn.Content = Loc.TabClean;
        SelectSafeBtn.Content = Loc.SelectSafe;
        SelectNoneBtn.Content = Loc.SelectNone;
        RecycleSelBtn.Content = Loc.RecycleSelected;
        ColCleanName.Header = Loc.ColName;
        ColCleanSize.Header = Loc.Size;
        ColCleanWhy.Header = Loc.ColReason;
        RefreshCleanUi();
        DialogClose.Content = Loc.Close;
        ConfirmYesBtn.Content = Loc.Yes;
        ConfirmNoBtn.Content = Loc.No;
        ThemeLabel.Text = Loc.Theme;
        LangLabel.Text = Loc.Language;
        ThemeTerminalBtn.Content = Loc.ThemeTerminal;
        ThemeMonoBtn.Content = Loc.ThemeMono;
        ThemeCyberBtn.Content = Loc.ThemeCyber;
        LangZhBtn.Content = Loc.LangZh;
        LangEnBtn.Content = Loc.LangEn;
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
        var t = App.Settings.Theme;
        Mark(ThemeTerminalBtn, t == AppTheme.Terminal);
        Mark(ThemeMonoBtn, t == AppTheme.Mono);
        Mark(ThemeCyberBtn, t == AppTheme.Cyberpunk);
        Mark(LangZhBtn, Loc.Lang == AppLang.Zh);
        Mark(LangEnBtn, Loc.Lang == AppLang.En);
    }

    private async void RunScan()
    {
        if (_scanning || DriveBox.SelectedItem == null) return;
        _scanning = true;
        ScanButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        _scanStart = DateTime.Now;
        _cts = new CancellationTokenSource();
        HeaderStats.Text = Loc.Scanning;
        FileCountText.Text = Loc.Files(0);
        ScanProgressPanel.Visibility = Visibility.Visible;
        ScanProgressBar.IsIndeterminate = true;
        ScanProgressBar.Value = 0;
        ScanProgressText.Text = Loc.Preparing;
        ShowRightTab(true);
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

    private static FrameworkElement MakeFolderHeader(FileEntry d, bool isRoot = false)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 80 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        var name = new TextBlock
        {
            Text = d.Name,
            Foreground = ThemeService.Brush(d.IsDimmed ? "TextMuted" : "Text"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        double share = isRoot ? 1 : d.PercentShare;
        var pctCell = MakePctBar(share, isRoot ? 100 : d.PercentValue, d.IsDimmed);
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
            long used = d.TotalSize - d.TotalFreeSpace;
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
        string path = entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath) ?? entry.FullPath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (entry.IsDirectory)
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + entry.FullPath + "\"") { UseShellExecute = true });
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
        AboutBody.Visibility = about ? Visibility.Visible : Visibility.Collapsed;
        AlertBody.Visibility = alert || confirm ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButtons.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        DialogClose.Visibility = confirm ? Visibility.Collapsed : Visibility.Visible;
        Overlay.Visibility = Visibility.Visible;
        if (settings) HighlightThemeButtons();
    }

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
        Overlay.Visibility = Visibility.Collapsed;
        act?.Invoke();
    }

    private void ConfirmNo_Click(object sender, RoutedEventArgs e)
    {
        _confirmYes = null;
        Overlay.Visibility = Visibility.Collapsed;
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        _confirmYes = null;
        Overlay.Visibility = Visibility.Collapsed;
    }

    private void Overlay_Click(object sender, MouseButtonEventArgs e)
    {
        _confirmYes = null;
        Overlay.Visibility = Visibility.Collapsed;
    }

    private void Dialog_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var theme = tag switch
        {
            "Mono" => AppTheme.Mono,
            "Cyberpunk" => AppTheme.Cyberpunk,
            _ => AppTheme.Terminal,
        };
        App.SaveUi(theme, Loc.Lang);
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        App.SaveUi(App.Settings.Theme, tag == "En" ? AppLang.En : AppLang.Zh);
    }

    private void RepoLink_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Loc.Repo) { UseShellExecute = true });
        }
        catch { }
    }

    private void TabExt_Click(object sender, RoutedEventArgs e) => ShowRightTab(clean: false);
    private void TabClean_Click(object sender, RoutedEventArgs e) => ShowRightTab(clean: true);

    private void ShowRightTab(bool clean)
    {
        _cleanTab = clean;
        ExtPane.Visibility = clean ? Visibility.Collapsed : Visibility.Visible;
        CleanPane.Visibility = clean ? Visibility.Visible : Visibility.Collapsed;
        TabExtBtn.BorderBrush = ThemeService.Brush(clean ? "Border" : "Accent");
        TabCleanBtn.BorderBrush = ThemeService.Brush(clean ? "Accent" : "Border");
        TabExtBtn.Foreground = ThemeService.Brush(clean ? "TextDim" : "Accent");
        TabCleanBtn.Foreground = ThemeService.Brush(clean ? "Accent" : "TextDim");
    }

    private void RefreshCleanUi()
    {
        if (CleanCatBox == null) return;
        int keep = CleanCatBox.SelectedIndex;
        var cats = new List<string>
        {
            Label(Loc.CatCleanable, _report?.Cleanable),
            Label(Loc.CatLarge, _report?.LargeFiles),
            Label(Loc.CatOld, _report?.OldFiles),
            Label(Loc.CatDup, _report?.Duplicates),
            Label(Loc.CatEmpty, _report?.EmptyFolders),
            Label(Loc.CatShortcut, _report?.BrokenShortcuts),
            Label(Loc.CatLong, _report?.LongPaths),
            Label(Loc.CatCompare, _report?.Compare),
        };
        CleanCatBox.ItemsSource = cats;
        CleanCatBox.SelectedIndex = keep >= 0 && keep < cats.Count ? keep : 0;
        ShowCleanCat();
        if (_report == null)
            CleanSummary.Text = Loc.AnalyzeAfterScan;
        else if (CleanCatBox.SelectedIndex == 7)
            CleanSummary.Text = _report.CompareNote;
        else
            CleanSummary.Text = Loc.CleanHintReady(_report.Cleanable.Count, FileEntry.FormatSize(_report.CleanableBytes));
        UpdateCleanSelHint();
        ShowRightTab(_cleanTab);
    }

    private static string Label(string title, List<CleanItem>? items)
    {
        if (items == null || items.Count == 0) return title + "  ·  0";
        return title + "  ·  " + Loc.CatCount(items.Count, FileEntry.FormatSize(items.Sum(x => x.Size)));
    }

    private void CleanCat_Changed(object sender, SelectionChangedEventArgs e) => ShowCleanCat();

    private void ShowCleanCat()
    {
        if (_report == null)
        {
            CleanGrid.ItemsSource = null;
            return;
        }
        CleanGrid.ItemsSource = CurrentCleanList();
        if (CleanCatBox.SelectedIndex == 7)
            CleanSummary.Text = _report.CompareNote;
        UpdateCleanSelHint();
    }

    private List<CleanItem> CurrentCleanList()
        => CleanCatBox.SelectedIndex switch
        {
            1 => _report?.LargeFiles ?? new(),
            2 => _report?.OldFiles ?? new(),
            3 => _report?.Duplicates ?? new(),
            4 => _report?.EmptyFolders ?? new(),
            5 => _report?.BrokenShortcuts ?? new(),
            6 => _report?.LongPaths ?? new(),
            7 => _report?.Compare ?? new(),
            _ => _report?.Cleanable ?? new(),
        };

    private void SelectSafe_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in CurrentCleanList())
            item.Selected = item.CanDelete && (item.Group == Loc.GroupTemp || item.Group == Loc.GroupDump || item.Group == Loc.GroupRecycle);
        UpdateCleanSelHint();
        CleanGrid.Items.Refresh();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in CurrentCleanList()) item.Selected = false;
        UpdateCleanSelHint();
        CleanGrid.Items.Refresh();
    }

    private void UpdateCleanSelHint()
    {
        var picked = CurrentCleanList().Where(x => x.Selected && x.CanDelete).ToList();
        CleanSelHint.Text = picked.Count == 0
            ? ""
            : Loc.CatCount(picked.Count, FileEntry.FormatSize(picked.Sum(x => x.Size)));
    }

    private void CleanGrid_Click(object sender, MouseButtonEventArgs e) => UpdateCleanSelHint();

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
