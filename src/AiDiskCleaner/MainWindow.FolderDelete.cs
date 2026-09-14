using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

/// <summary>
/// 「文件夹整理」页的手动删除入口（后端 partial）。
///
/// 与页面其余部分的分工：
/// <list type="bullet">
/// <item><see cref="OrganizeNode.IsChecked"/> 是**用户手动**勾选，AI 永远不写；</item>
/// <item>这里只负责把勾选交给 <see cref="FolderDeleteService"/>：预览 → 明确确认 → 执行；</item>
/// <item>确认框取消 = 什么都没发生（零后端调用）；执行中取消 = 停掉后续项，已处理项保留真实结果；</item>
/// <item>删除一律走「必须进回收站」的操作，回收站不可用就拒绝，**没有永久删除路径**。</item>
/// </list>
/// 界面元素名不在这里出现：XAML 只需把底部栏绑定到 <see cref="FolderDelete"/> 并接上这几个 Click。
/// </summary>
public partial class MainWindow
{
    private readonly FolderDeleteService _folderDeleteService = new();
    private CancellationTokenSource? _folderDeleteCts;
    private int _folderDeleteDone;

    /// <summary>「文件夹整理」底部操作栏的视图模型（XAML 绑定它，不直接碰服务）。</summary>
    public FolderDeleteBar FolderDelete { get; } = new();

    // ==================== 勾选 ====================

    /// <summary>行内复选框：按 CheckBox 的新状态写入节点；点文字之类则切换。</summary>
    private void OrganizeSelect_Click(object sender, RoutedEventArgs e)
    {
        if (_folderDeleteService.IsBusy) return;
        var node = (sender as FrameworkElement)?.DataContext as OrganizeNode
            ?? (sender as FrameworkElement)?.Tag as OrganizeNode;
        if (node == null) return;

        if (sender is ToggleButton toggle && toggle.IsChecked.HasValue)
            node.IsChecked = toggle.IsChecked.Value;
        else
            node.ToggleSelect();

        RefreshFolderDeleteBar();
    }

    /// <summary>全选**当前可见**的行（展开的层级与筛选后的列表），不偷偷扩展到没显示的对象。</summary>
    private void OrganizeSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_folderDeleteService.IsBusy) return;
        foreach (var node in _organizeRows) node.IsChecked = true;
        RefreshFolderDeleteBar();
    }

    private void OrganizeClearSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_folderDeleteService.IsBusy) return;
        foreach (var node in _organizeAll) node.IsChecked = false;
        RefreshFolderDeleteBar();
    }

    /// <summary>把整理对象投影成选择/去重逻辑需要的最小结构。</summary>
    private static List<FolderPick> PicksOf(IEnumerable<OrganizeNode> nodes)
    {
        var picks = new List<FolderPick>();
        foreach (var n in nodes) picks.Add(new FolderPick(n.IsChecked, n.FullPath, n.Name, n.Size));
        return picks;
    }

    // ==================== 预览 → 确认 → 执行 ====================

    private void FolderDeletePreview_Click(object sender, RoutedEventArgs e)
    {
        if (OrganizeGrid == null) return;
        if (_folderDeleteService.IsBusy)
        {
            FolderDelete.SetNote(FolderDeleteText.Busy);
            return;
        }

        var selection = FolderSelectionService.Build(PicksOf(_organizeAll));
        FolderDelete.Refresh(selection.SelectedCount, selection.KeptCount, selection.NestedCount, selection.TotalBytes);
        if (selection.IsEmpty)
        {
            FolderDelete.SetNote(FolderDeleteText.NothingSelected);
            return;
        }

        FolderDeletePreview preview;
        try
        {
            preview = _folderDeleteService.Preview(selection.Targets, selection.SelectedCount, selection.NestedCount);
        }
        catch (Exception ex)
        {
            AppLog.Record("Organize", ex, "folder delete preview");
            FolderDelete.SetNote(Loc.OrganizeFailedBody);
            return;
        }

        if (!preview.HasAnything)
        {
            FolderDelete.SetNote(FolderDeleteText.NothingDeletable);
            return;
        }

        // 用户点「确定」之前，一个删除调用都不会发；点取消等于丢弃这次预览。
        AskConfirm(FolderDeleteText.ConfirmTitle, FolderDeleteText.ConfirmBody(preview),
            () => RunFolderDeleteConfirmed(preview));
    }

    private void RunFolderDeleteConfirmed(FolderDeletePreview preview)
    {
        _folderDeleteCts?.Dispose();
        var cts = new CancellationTokenSource();
        _folderDeleteCts = cts;
        _folderDeleteDone = 0;
        FolderDelete.Begin(preview.Items.Count);

        var progress = new ActionProgress<FolderDeleteItemResult>(_ =>
        {
            _folderDeleteDone++;
            FolderDelete.Report(_folderDeleteDone, preview.Items.Count);
            // 执行在 UI 线程上，同步泵一下消息队列，让「停止」按钮真的点得到。
            PumpUi();
        });

        FolderDeleteResult result;
        try
        {
            // 确认框已经确认过整份清单（含敏感位置）；这里的 confirm 是执行前的最后一道门槛。
            result = _folderDeleteService.Execute(
                preview, FolderDeleteConsent.SensitiveConfirmed, confirm: _ => true, progress, cts.Token);
        }
        catch (Exception ex)
        {
            AppLog.Record("Organize", ex, "folder delete execute");
            FolderDelete.SetNote(FolderDeleteText.ResultTitle + " · " + ex.GetType().Name);
            RefreshFolderDeleteBar();
            return;
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(_folderDeleteCts, cts)) _folderDeleteCts = null;
        }

        FolderDelete.Finish(result);
        ClearSelectionOfRecycled(result);
        string problems = FolderDeleteText.ResultProblems(result);
        if (problems.Length > 0 || result.Canceled)
            ShowAlert(FolderDeleteText.ResultTitle, result.SummaryText + (problems.Length > 0 ? "\n\n" + problems : ""));
        else
            SetStatus(result.Headline);
        AppLog.Info("Organize", $"op=folder-delete state={result.State} recycled={result.Recycled} "
            + $"uncertain={result.Uncertain} failed={result.Failed} blocked={result.Protected} "
            + $"skipped={result.Skipped} total={result.Total}");
        RefreshFolderDeleteBar();
    }

    private void FolderDeleteCancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_folderDeleteService.IsBusy) return;
        FolderDelete.RequestCancel();
        _folderDeleteCts?.Cancel();
        FolderDelete.SetNote(FolderDeleteText.CancelRequested);
    }

    /// <summary>删除成功的项取消勾选（失败/不确定的保持勾选，方便复查）。</summary>
    private void ClearSelectionOfRecycled(FolderDeleteResult result)
    {
        var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in result.Items)
            if (item.Recycled && item.Path.Length > 0) gone.Add(item.Path);
        if (gone.Count == 0) return;
        foreach (var node in _organizeAll)
            if (node.IsChecked && gone.Contains(node.FullPath)) node.IsChecked = false;
    }

    // ==================== 底部栏刷新 ====================

    /// <summary>重建/展开/筛选/勾选之后都要调一次（界面在那些流程末尾调用）。</summary>
    private void RefreshFolderDeleteBar()
    {
        if (OrganizeGrid == null) return;
        var selection = FolderSelectionService.Build(PicksOf(_organizeAll));
        FolderDelete.Refresh(selection.SelectedCount, selection.KeptCount, selection.NestedCount, selection.TotalBytes);
    }

    /// <summary>在不阻塞「停止」的前提下让消息队列跑一轮（执行仍在 UI 线程，COM 需要 STA）。</summary>
    private void PumpUi()
    {
        try { Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); }
        catch (Exception ex) { AppLog.Record("Organize", ex, "folder delete pump"); }
    }
}
