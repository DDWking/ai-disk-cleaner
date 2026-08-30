using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

public partial class MainWindow : Window
{
    private FileEntry _root = null!;
    private FileEntry _current = null!;
    private DispatcherTimer? _scanTimer;
    private int _progress;
    private DateTime _scanStart;

    public MainWindow()
    {
        InitializeComponent();
        DriveBox.ItemsSource = new[] { "C:", "D:", "E:" };
        DriveBox.SelectedIndex = 0;
        Loaded += (_, _) => RunScan();
    }

    private void RunScan()
    {
        ScanButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        _progress = 0;
        _scanStart = DateTime.Now;
        HeaderStats.Text = "扫描中…";
        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _scanTimer.Tick += (_, _) =>
        {
            _progress += Random.Shared.Next(3, 12);
            if (_progress >= 100)
            {
                _progress = 100;
                _scanTimer.Stop();
                FinishScan();
            }
            HeaderStats.Text = $"扫描中… {_progress}%";
        };
        _scanTimer.Start();
    }

    private void FinishScan()
    {
        _root = MockScanService.Scan(DriveBox.Text);
        _current = _root;
        PopulateTree();
        ShowDirectory(_root);
        var all = CollectFiles(_root);
        ElapsedText.Text = "扫描耗时 " + (DateTime.Now - _scanStart).TotalSeconds.ToString("0.00") + "s";
        HeaderStats.Text = all.Count.ToString("N0") + " 个文件";
        var cleanable = all.Where(f => f.Category is "临时" or "日志").ToList();
        CleanHintText.Text = cleanable.Count > 0
            ? $"🧠 AI 建议：{cleanable.Count} 个临时/日志文件可清理，约 {FileEntry.FormatSize(cleanable.Sum(f => f.Size))}"
            : "🧠 AI 建议：磁盘很干净";
        ScanButton.IsEnabled = true;
        StopButton.IsEnabled = false;
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
        void Add(FileEntry node, ItemsControl parent)
        {
            foreach (var d in node.Children.Where(c => c.IsDirectory))
            {
                var item = new TreeViewItem { Header = d.Name, Tag = d };
                parent.Items.Add(item);
                Add(d, item);
            }
        }
        Add(_root, root);
        root.IsSelected = true;
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

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _scanTimer?.Stop();
        FinishScan();
    }

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
