using System.IO;
using System.Windows;
using System.Windows.Controls;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

public partial class MainWindow : Window
{
    private readonly IScanService _scanner = new RecursiveScanService();
    private FileEntry _root = null!;
    private FileEntry _current = null!;
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
            var root = await Task.Run(() => _scanner.Scan(drive, progress, _cts.Token));
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
        _root = root;
        _current = root;
        PopulateTree();
        ShowDirectory(root);
        var all = CollectFiles(root);
        ElapsedText.Text = "扫描耗时 " + (DateTime.Now - _scanStart).TotalSeconds.ToString("0.00") + "s";
        HeaderStats.Text = all.Count.ToString("N0") + " 个文件";
        var cleanable = all.Where(f => f.Category is "临时" or "日志").ToList();
        CleanHintText.Text = cleanable.Count > 0
            ? $"🧠 AI 建议：{cleanable.Count} 个临时/日志文件可清理，约 {FileEntry.FormatSize(cleanable.Sum(f => f.Size))}"
            : "🧠 AI 建议：磁盘很干净";
    }

    private static List<FileEntry> CollectFiles(FileEntry node)
    {
        var list = new List<FileEntry>();
        void Walk(FileEntry n)
        {
            foreach (var c in n.Children)
            {
                if (c.IsDirectory) Walk(c);
                else list.Add(c);
            }
        }
        Walk(node);
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
        foreach (var d in entry.Children.Where(c => c.IsDirectory))
        {
            var item = new TreeViewItem { Header = d.Name, Tag = d };
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
        var files = CollectFiles(dir);
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            var q = SearchBox.Text.Trim().ToLower();
            files = files.Where(f => f.Name.ToLower().Contains(q) || f.FullPath.ToLower().Contains(q)).ToList();
        }
        FileGrid.ItemsSource = files;
        FileCountText.Text = files.Count.ToString("N0") + " 个文件";
        TotalSizeText.Text = FileEntry.FormatSize(files.Sum(f => f.Size));
        TreeMap.SetItems(dir.Children);
        TreemapCrumb.Text = dir.FullPath;
    }

    private void SelectTreeNode(FileEntry dir)
    {
        foreach (var item in DirTree.Items.OfType<TreeViewItem>())
            if (SelectRecursive(item, dir)) return;
    }

    private static bool SelectRecursive(TreeViewItem item, FileEntry target)
    {
        if (ReferenceEquals(item.Tag, target))
        {
            item.IsSelected = true;
            item.IsExpanded = true;
            item.BringIntoView();
            return true;
        }
        foreach (var child in item.Items.OfType<TreeViewItem>())
            if (SelectRecursive(child, target)) return true;
        return false;
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e) => RunScan();

    private void StopButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void DirTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DirTree.SelectedItem is TreeViewItem { Tag: FileEntry dir })
            ShowDirectory(dir);
    }

    private void TreeMap_DirectoryClicked(FileEntry dir)
    {
        SelectTreeNode(dir);
        ShowDirectory(dir);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_current != null) ShowDirectory(_current);
    }
}
