using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

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
        Loaded += (_, _) => RunScan();
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
        PathCrumb.Text = _current == null || string.IsNullOrEmpty(_current.FullPath) ? Loc.Path : _current.FullPath;
        PctHeader.Text = Loc.Pct;
        SizeHeader.Text = Loc.Size;
        ExtTitle.Text = Loc.ExtType;
        ColExt.Header = Loc.Ext;
        ColType.Header = Loc.Type;
        ColPct.Header = Loc.Pct;
        ColSize.Header = Loc.Size;
        DialogClose.Content = Loc.Close;
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

        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.Percent >= 0)
            {
                ScanProgressBar.IsIndeterminate = false;
                ScanProgressBar.Value = p.Percent;
                ScanProgressText.Text = Loc.ProgressLine(p.Percent, p.CurrentDirectory, p.FileCount);
                HeaderStats.Text = Loc.ScanPct(p.Percent);
            }
            else
            {
                ScanProgressBar.IsIndeterminate = true;
                ScanProgressText.Text = Loc.ProgressIndeterminate(p.CurrentDirectory, p.FileCount);
                HeaderStats.Text = Loc.ScanCount(p.FileCount);
            }
            FileCountText.Text = Loc.Files(p.FileCount);
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
            HeaderStats.Text = Loc.Aborted;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.ScanFailedMsg(ex.Message), Loc.AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
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
        PopulateTree();
        UiLog("PopulateTree 完成");
        ShowDirectory(root);
        UiLog("ShowDirectory 完成");
        ElapsedText.Text = Loc.Elapsed((DateTime.Now - _scanStart).TotalSeconds);
        HeaderStats.Text = Loc.Files(root.FileCount);
        var cleanable = _allFiles.Where(f => f.Category is "临时" or "日志" or "Temporary" or "Log").ToList();
        UiLog($"cleanable 计算完成: {cleanable.Count:N0}");
        CleanHintText.Text = cleanable.Count > 0
            ? Loc.HintTemp(cleanable.Count, FileEntry.FormatSize(cleanable.Sum(f => f.Size)))
            : Loc.HintClean;
        UiLog("FinishScan 全部完成");
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
        var root = new TreeViewItem { Header = MakeFolderHeader(_root, isRoot: true), Tag = _root, IsExpanded = true };
        DirTree.Items.Add(root);
        PopulateDirChildren(root);
        root.IsSelected = true;
    }

    private static FrameworkElement MakeFolderHeader(FileEntry d, bool isRoot = false)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 80 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        var name = new TextBlock
        {
            Text = d.Name,
            Foreground = ThemeService.Brush("Text"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        double barW = isRoot ? 72 : d.PercentBarWidth;
        var pctCell = new Grid { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        pctCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        pctCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        var track = new Border
        {
            Height = 6,
            Background = ThemeService.Brush("Border"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var fill = new Border
        {
            Height = 6,
            Width = barW,
            Background = ThemeService.Brush("Accent"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        track.Child = fill;
        var pctText = new TextBlock
        {
            Text = (isRoot ? 100 : d.PercentValue).ToString("0.0") + " %",
            Foreground = ThemeService.Brush("TextMuted"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(track, 0);
        Grid.SetColumn(pctText, 1);
        pctCell.Children.Add(track);
        pctCell.Children.Add(pctText);
        var size = new TextBlock
        {
            Text = FileEntry.FormatSize(d.Size),
            Foreground = ThemeService.Brush("AccentDim"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(pctCell, 1);
        Grid.SetColumn(size, 2);
        grid.Children.Add(name);
        grid.Children.Add(pctCell);
        grid.Children.Add(size);
        return grid;
    }

    private void PopulateDirChildren(TreeViewItem parent)
    {
        parent.Items.Clear();
        var entry = (FileEntry)parent.Tag;
        foreach (var d in entry.Children.Where(c => c.IsDirectory).OrderByDescending(c => c.Size))
        {
            var item = new TreeViewItem { Header = MakeFolderHeader(d), Tag = d };
            if (d.Children.Any(c => c.IsDirectory))
            {
                // 放占位符，展开时才真正加载子目录（懒加载，避免几十万节点卡死）
                item.Items.Add(new TreeViewItem { Header = "…", Tag = Placeholder });
                item.Expanded += DirItem_Expanded;
            }
            parent.Items.Add(item);
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
        PathCrumb.Text = string.IsNullOrEmpty(dir.FullPath) ? Loc.Path : dir.FullPath;
        FileCountText.Text = Loc.FileDirCount(dir.FileCount, dir.FolderCount);
        TotalSizeText.Text = FileEntry.FormatSize(dir.Size);
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

        ExtGrid.ItemsSource = list;
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
        if (DirTree.SelectedItem is TreeViewItem { Tag: FileEntry dir })
            ShowDirectory(dir);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_current != null) ShowDirectory(_current);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        DialogTitle.Text = Loc.SettingsTitle;
        SettingsBody.Visibility = Visibility.Visible;
        AboutBody.Visibility = Visibility.Collapsed;
        Overlay.Visibility = Visibility.Visible;
        HighlightThemeButtons();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        DialogTitle.Text = Loc.AboutTitle;
        SettingsBody.Visibility = Visibility.Collapsed;
        AboutBody.Visibility = Visibility.Visible;
        Overlay.Visibility = Visibility.Visible;
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e) => Overlay.Visibility = Visibility.Collapsed;
    private void Overlay_Click(object sender, MouseButtonEventArgs e) => Overlay.Visibility = Visibility.Collapsed;
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
}
