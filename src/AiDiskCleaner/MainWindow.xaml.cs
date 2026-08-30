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
        HeaderStats.Text = "扫描中… 0 个文件";
        FileCountText.Text = "0 个文件";

        var progress = new Progress<ScanProgress>(p =>
        {
            HeaderStats.Text = $"扫描中… {p.FileCount:N0} 个文件";
            FileCountText.Text = p.FileCount.ToString("N0") + " 个文件";
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
                HeaderStats.Text = "MFT 不可用，回退到递归扫描…";
                root = await Task.Run(() => _fallback.Scan(drive, progress, _cts.Token));
            }
            FinishScan(root);
        }
        catch (OperationCanceledException)
        {
            HeaderStats.Text = "扫描已取消";
        }
        catch (Exception ex)
        {
            MessageBox.Show("扫描失败：" + ex.Message, "AI 磁盘清理",
                MessageBoxButton.OK, MessageBoxImage.Error);
            HeaderStats.Text = "扫描失败";
        }
        finally
        {
            _scanning = false;
            ScanButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }
    }

    private void FinishScan(FileEntry root)
    {
        UiLog("FinishScan 开始");
        _root = root;
        _current = root;
        _allFiles = CollectFiles(root); // 只收集一次，缓存
        UiLog($"CollectFiles 完成: {_allFiles.Count:N0}");
        PopulateTree();
        UiLog("PopulateTree 完成");
        ShowDirectory(root);
        UiLog("ShowDirectory 完成");
        ElapsedText.Text = "扫描耗时 " + (DateTime.Now - _scanStart).TotalSeconds.ToString("0.00") + "s";
        HeaderStats.Text = _allFiles.Count.ToString("N0") + " 个文件";
        var cleanable = _allFiles.Where(f => f.Category is "临时" or "日志").ToList();
        UiLog($"cleanable 计算完成: {cleanable.Count:N0}");
        CleanHintText.Text = cleanable.Count > 0
            ? $"🧠 AI 建议：{cleanable.Count} 个临时/日志文件可清理，约 {FileEntry.FormatSize(cleanable.Sum(f => f.Size))}"
            : "🧠 AI 建议：磁盘很干净";
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
        var root = new TreeViewItem { Header = _root.Name, Tag = _root, IsExpanded = true };
        DirTree.Items.Add(root);
        PopulateDirChildren(root);
        root.IsSelected = true;
    }

    private void PopulateDirChildren(TreeViewItem parent)
    {
        parent.Items.Clear();
        var entry = (FileEntry)parent.Tag;
        foreach (var d in entry.Children.Where(c => c.IsDirectory).OrderByDescending(c => c.Size))
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = d.Name, VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock
            {
                Text = FileEntry.FormatSize(d.Size),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 11,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var item = new TreeViewItem { Header = header, Tag = d };
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
        var files = ReferenceEquals(dir, _root) ? _allFiles : CollectFiles(dir);
        int total = files.Count;
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            var q = SearchBox.Text.Trim().ToLower();
            files = files.Where(f => f.Name.ToLower().Contains(q) || f.FullPath.ToLower().Contains(q)).ToList();
            total = files.Count;
        }
        // 默认按大小降序，截取时也取最大的那些
        files = files.OrderByDescending(f => f.Size).ToList();
        var display = files.Count > MaxDisplayRows ? files.Take(MaxDisplayRows).ToList() : files;
        long maxSize = display.Count > 0 ? display[0].Size : 1;
        foreach (var f in display)
            f.SizeBarWidth = maxSize > 0 ? Math.Max(2, 112.0 * f.Size / maxSize) : 0;
        FileGrid.ItemsSource = display;
        FileCountText.Text = total.ToString("N0") + (total > MaxDisplayRows ? $" 个文件（显示前 {MaxDisplayRows:N0}）" : " 个文件");
        TotalSizeText.Text = FileEntry.FormatSize(dir.Size);
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
