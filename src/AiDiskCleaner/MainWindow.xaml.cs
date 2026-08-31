using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        SearchHint.Visibility = Visibility.Visible;
        UpdateVolumeInfo();
        DriveBox.SelectionChanged += (_, _) => UpdateVolumeInfo();
        Loaded += (_, _) => RunScan();
    }

    private async void RunScan()
    {
        if (_scanning || DriveBox.SelectedItem == null) return;
        _scanning = true;
        ScanButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        _scanStart = DateTime.Now;
        _cts = new CancellationTokenSource();
        HeaderStats.Text = "scanning";
        FileCountText.Text = "0 files";
        ScanProgressPanel.Visibility = Visibility.Visible;
        ScanProgressBar.IsIndeterminate = true;
        ScanProgressBar.Value = 0;
        ScanProgressText.Text = "init...";

        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.Percent >= 0)
            {
                ScanProgressBar.IsIndeterminate = false;
                ScanProgressBar.Value = p.Percent;
                ScanProgressText.Text = $"{p.Percent}%  {p.CurrentDirectory}  {p.FileCount:N0} files";
                HeaderStats.Text = $"scan {p.Percent}%";
            }
            else
            {
                ScanProgressBar.IsIndeterminate = true;
                ScanProgressText.Text = $"{p.CurrentDirectory}  {p.FileCount:N0} files";
                HeaderStats.Text = $"scan {p.FileCount:N0}";
            }
            FileCountText.Text = p.FileCount.ToString("N0") + " files";
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
                HeaderStats.Text = "mft fail, fallback";
                root = await Task.Run(() => _fallback.Scan(drive, progress, _cts.Token));
            }
            FinishScan(root);
        }
        catch (OperationCanceledException)
        {
            HeaderStats.Text = "aborted";
        }
        catch (Exception ex)
        {
            MessageBox.Show("扫描失败：" + ex.Message, "AI 磁盘清理",
                MessageBoxButton.OK, MessageBoxImage.Error);
            HeaderStats.Text = "scan failed";
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
        ElapsedText.Text = "elapsed " + (DateTime.Now - _scanStart).TotalSeconds.ToString("0.00") + "s";
        HeaderStats.Text = root.FileCount.ToString("N0") + " files";
        var cleanable = _allFiles.Where(f => f.Category is "临时" or "日志").ToList();
        UiLog($"cleanable 计算完成: {cleanable.Count:N0}");
        CleanHintText.Text = cleanable.Count > 0
            ? $"hint: {cleanable.Count} temp/log  ~ {FileEntry.FormatSize(cleanable.Sum(f => f.Size))}"
            : "hint: clean";
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
        var grid = new Grid { MinWidth = 300 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        var name = new TextBlock
        {
            Text = d.Name,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC8, 0xF5, 0xD0)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var pct = new TextBlock
        {
            Text = isRoot ? "100 %" : d.PercentText,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x8F, 0x74)),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var size = new TextBlock
        {
            Text = FileEntry.FormatSize(d.Size),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x39, 0xFF, 0x88)),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(pct, 1);
        Grid.SetColumn(size, 2);
        grid.Children.Add(name);
        grid.Children.Add(pct);
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
        PathCrumb.Text = dir.FullPath;
        IEnumerable<FileEntry> items = dir.Children;
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            var q = SearchBox.Text.Trim().ToLower();
            items = items.Where(f => f.Name.ToLower().Contains(q));
        }
        var list = items.OrderByDescending(f => f.Size).ToList();
        int total = list.Count;
        var display = list.Count > MaxDisplayRows ? list.Take(MaxDisplayRows).ToList() : list;
        long maxSize = display.Count > 0 ? display[0].Size : 1;
        foreach (var f in display)
            f.SizeBarWidth = maxSize > 0 ? Math.Max(2, 124.0 * f.Size / maxSize) : 0;
        FileGrid.ItemsSource = display;
        FileCountText.Text = dir.FileCount.ToString("N0") + " files  " + dir.FolderCount.ToString("N0") + " dirs"
            + (total > MaxDisplayRows ? $"  (top {MaxDisplayRows:N0})" : "");
        TotalSizeText.Text = FileEntry.FormatSize(dir.Size);
    }

    private void FileGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FileGrid.SelectedItem is FileEntry { IsDirectory: true } dir)
        {
            ShowDirectory(dir);
            SelectTreeNode(dir);
        }
    }

    private void SelectTreeNode(FileEntry dir)
    {
        foreach (var item in DirTree.Items.OfType<TreeViewItem>())
            if (SelectRecursive(item, dir)) return;
    }

    private bool SelectRecursive(TreeViewItem item, FileEntry target)
    {
        if (ReferenceEquals(item.Tag, target))
        {
            item.IsSelected = true;
            item.BringIntoView();
            return true;
        }
        if (item.Items.Count == 1 && item.Items[0] is TreeViewItem ph && ReferenceEquals(ph.Tag, Placeholder))
            PopulateDirChildren(item);
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            if (!SelectRecursive(child, target)) continue;
            item.IsExpanded = true;
            return true;
        }
        return false;
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
            VolumeText.Text = $"total {FileEntry.FormatSize(d.TotalSize)}  used {FileEntry.FormatSize(used)} ({pct:0.0}%)  free {FileEntry.FormatSize(d.TotalFreeSpace)}";
        }
        catch { }
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e) => RunScan();

    private void StopButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void DirTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DirTree.SelectedItem is TreeViewItem { Tag: FileEntry dir })
            ShowDirectory(dir);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_current != null) ShowDirectory(_current);
    }
}
