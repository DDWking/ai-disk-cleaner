using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

/// <summary>
/// 「文件夹整理」主工作区。
///
/// 这一页是**实际工作区**，不是说明页：
/// <list type="bullet">
/// <item>扫描后直接把本地整理出来的文件夹对象列出来（名称 / 用途 / 容量 / 必要操作）；</item>
/// <item>本地能判断的**自动**认出，不需要逐个点按钮；</item>
/// <item>看不出来的才需要显式点「识别这些文件夹」，并有范围、数量、预算、进度与取消；</item>
/// <item>默认铺两层，收纳目录继续直接子目录，具体对象停止内部逐项分类；</item>
/// <item>平台游戏库这类容器**保留往里找游戏的入口**。</item>
/// </list>
///
/// 安全边界：这里**没有**任何删除/选择代码。识别不改 Risk / CanDelete / Selected，
/// 不自动选择、不移动、不归档真实文件；「在资源管理器中打开」只打开目录，不执行任何程序。
/// </summary>
public partial class MainWindow
{
    /// <summary>列表数据源（扁平：展开时把子对象插到父对象后面）。</summary>
    private readonly ObservableCollection<OrganizeNode> _organizeRows = new();

    /// <summary>所有已经材料化出来的对象（批量识别的范围就是它）。</summary>
    private readonly List<OrganizeNode> _organizeAll = new();

    /// <summary>按真实目录索引，保证同一个目录只有一个对象（不重复建节点）。</summary>
    private readonly Dictionary<FileEntry, OrganizeNode> _organizeByDir = new();

    /// <summary>顶层对象（扁平顺序从这里出发）。</summary>
    private readonly List<OrganizeNode> _organizeRoots = new();

    /// <summary>系统解析出来的识别入口（一次解析，批量识别复用，不再逐行碰磁盘）。</summary>
    private IReadOnlyList<string> _organizeEntryPoints = Array.Empty<string>();

    /// <summary>「待确认」筛选：只看还没认出来的（避免满屏重复的「未识别」）。</summary>
    private bool _organizePendingOnly;

    /// <summary>正在批量识别（同一时刻只允许一个，避免重复请求）。</summary>
    private bool _organizeBusy;

    private CancellationTokenSource? _organizeStop;

    /// <summary>重建代次：展开/重扫会让旧请求的回写作废，绝不覆盖新结果。</summary>
    private int _organizeGeneration;

    /// <summary>整理列表是按哪一代扫描建的；每代只建一次，避免重复铺开。</summary>
    private int _organizeBuiltForScan = -1;

    /// <summary>一次性的短提示（打开失败、纠正完成之类），跟着统计行显示。</summary>
    private string _organizeNote = "";

    /// <summary>右键目标。</summary>
    private OrganizeNode? _organizeMenuNode;

    private enum OrganizeStateKind { None, NoScan, Scanning, Empty, NoModel, Failed, AllDone }

    /// <summary>状态框按钮当前要做什么（没按钮时为 null）。</summary>
    private Action? _organizeStateAction;

    // ==================== 初始化 / 文案 ====================

    private void ApplyOrganizeUi()
    {
        if (OrganizeGrid == null) return;
        TabOrganizeBtn.Content = Loc.TabOrganize;
        OrganizeGrid.ItemsSource = _organizeRows;
        ColOrgName.Header = Loc.OrganizeColName;
        ColOrgPurpose.Header = Loc.OrganizeColPurpose;
        ColOrgSize.Header = Loc.OrganizeColSize;
        ColOrgAction.Header = Loc.OrganizeColAction;
        OrganizeTitle.Text = Loc.TabOrganize;
        OrganizeIdentifyAllBtn.Content = Loc.OrganizeIdentifyAll;
        OrganizeStopBtn.Content = Loc.OrganizeStop;
        OrganizeFilterPendingBtn.ToolTip = Loc.OrganizeFilterPendingTip;
        System.Windows.Automation.AutomationProperties.SetName(
            OrganizeFilterPendingBtn, Loc.OrganizeFilterPendingTip);
        OrgCtxOpen.Header = Loc.OrganizeOpen;
        OrgCtxCopy.Header = Loc.OrganizeCopyPath;
        OrgCtxIdentify.Header = Loc.PurposeIdentify;
        OrgCtxCorrect.Header = Loc.PurposeCorrect;
        OrgCtxExpand.Header = Loc.OrganizeExpand;
        ApplyOrganizeColumnPriority(OrganizeContentWidth());
        UpdateOrganizeHeader();
    }

    /// <summary>主内容区可用宽度（窄窗口下按列让位，不让文字被裁成残缺）。</summary>
    private double OrganizeContentWidth()
    {
        double w = RightPanel?.ActualWidth ?? 0;
        if (w <= 0) w = Math.Max(0, ActualWidth - 40);
        return w;
    }

    /// <summary>
    /// 极窄时才收窄次要列：名称与用途是主列，**永远保留**。
    /// </summary>
    private void ApplyOrganizeColumnPriority(double width)
    {
        if (OrganizeGrid == null || ColOrgAction == null || ColOrgSize == null) return;
        bool tight = width > 0 && width < 780;
        ColOrgAction.Width = new DataGridLength(tight ? 132 : 196);
        ColOrgSize.Visibility = width > 0 && width < 620 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ==================== 重建对象列表 ====================

    /// <summary>
    /// 用当前扫描结果重建整理对象。**只读扫描树**，不碰磁盘、不调模型。
    /// 每次重建都推进代次，旧识别结果不会再回写到新列表。
    /// </summary>
    private void RebuildOrganize()
    {
        if (OrganizeGrid == null) return;
        _organizeGeneration++;
        _organizeBuiltForScan = _scanGeneration;
        CancelOrganizeWork(clearState: true);

        _organizeRows.Clear();
        _organizeAll.Clear();
        _organizeByDir.Clear();
        _organizeRoots.Clear();
        _organizeNote = "";
        _organizePendingOnly = false;
        UpdateOrganizeFilterLabel();

        if (_root == null)
        {
            ShowOrganizeState(OrganizeStateKind.NoScan);
            UpdateOrganizeHeader();
            return;
        }

        try
        {
            var rs = FolderOrganize.BuildRoots(_root);
            _organizeEntryPoints = rs.EntryPoints;
            foreach (var d in rs.Roots) _organizeRoots.Add(CreateOrganizeNode(d, 0));

            if (_organizeRoots.Count == 0)
            {
                ShowOrganizeState(OrganizeStateKind.Empty);
                UpdateOrganizeHeader();
                return;
            }

            // 默认两层 + 系统入口路径例外：本地能判断的这里就认出来了，无需逐个点
            foreach (var n in _organizeRoots) AutoMaterialize(n);

            if (rs.Skipped > 0) _organizeNote = Loc.OrganizeSkippedNoAccess(rs.Skipped);
            ShowOrganizeState(OrganizeStateKind.None);
            RefreshOrganizeRows();
            AppLog.Info("Organize", $"op=build roots={_organizeRoots.Count} objects={_organizeAll.Count} "
                + $"rows={_organizeRows.Count} entries={_organizeEntryPoints.Count} skipped={rs.Skipped}");
        }
        catch (Exception ex)
        {
            AppLog.Record("Organize", ex, "organize build");
            ShowOrganizeState(OrganizeStateKind.Failed);
            UpdateOrganizeHeader();
        }
    }

    private OrganizeNode CreateOrganizeNode(FileEntry dir, int depth)
    {
        if (_organizeByDir.TryGetValue(dir, out var existing)) return existing;

        string rel = RelativeOf(dir);
        var node = new OrganizeNode(dir, new FolderId(dir.FullPath, _aiDataGeneration), depth, rel)
        {
            IsSystemEntry = FolderOrganize.IsEntryPoint(dir.FullPath, _organizeEntryPoints),
            IsPlatformContainer = FolderOrganize.KeepsObjectEntries(dir),
        };
        node.SetChildDirCount(FolderOrganize.DirectChildDirs(dir, 0).Total);
        _organizeByDir[dir] = node;
        _organizeAll.Add(node);
        LocalRecognize(node);
        return node;
    }

    private string RelativeOf(FileEntry dir)
    {
        string rootPath = _root?.FullPath ?? "";
        if (rootPath.Length > 0 && dir.FullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return dir.FullPath[rootPath.Length..].TrimStart('\\');
        return dir.FullPath;
    }

    /// <summary>
    /// **本地**先认一遍：用户纠正 → 缓存 → 本地规则。
    /// 本地就能说清的绝不留到批量识别里，也不会因此向模型发任何东西。
    /// </summary>
    private void LocalRecognize(OrganizeNode node)
    {
        if (_folderPurpose.TryGetUserCorrection(node.Id) is { } user) { node.Apply(user); return; }

        var sum = FolderPurposeRules.Summarize(node.Dir, node.Id, node.Depth, node.RelativePath);
        if (_folderPurpose.TryGetCached(node.Id, sum, AiConfigSignature()) is { } hit)
        {
            node.Apply(hit);
            return;
        }
        var local = FolderPurposeRules.RecognizeLocally(node.Dir, sum, _organizeEntryPoints);
        node.SetKind(local.Kind);
        if (local.HasConclusion) node.Apply(local);
    }

    /// <summary>自动铺开：默认两层 + 系统入口路径例外；到行数上限就停（用户仍可手动展开）。</summary>
    private void AutoMaterialize(OrganizeNode node)
    {
        if (FolderOrganize.RowBudgetReached(_organizeAll.Count)) return;
        if (!FolderOrganize.ShouldAutoMaterialize(
                node.Dir, node.Kind, node.Depth, _organizeEntryPoints)) return;
        Materialize(node, FolderOrganize.ChildBudget);
        foreach (var c in node.Children) AutoMaterialize(c);
    }

    /// <summary>把一个目录的直接子对象材料化出来（有预算、有去重、有上限）。</summary>
    private void Materialize(OrganizeNode node, int budget)
    {
        if (FolderOrganize.RowBudgetReached(_organizeAll.Count)) return;
        var set = FolderOrganize.DirectChildDirs(node.Dir, budget);
        var kids = new List<OrganizeNode>(set.Dirs.Count);
        foreach (var d in set.Dirs)
        {
            if (FolderOrganize.RowBudgetReached(_organizeAll.Count)) break;
            kids.Add(CreateOrganizeNode(d, node.Depth + 1));
        }
        node.SetChildren(kids, set.Truncated || kids.Count < set.Total, set.Total);
    }

    // ==================== 行渲染 ====================

    private void RefreshOrganizeRows()
    {
        if (OrganizeGrid == null) return;
        _organizeRows.Clear();
        int n = 0;
        foreach (var r in _organizeRoots)
        {
            AddVisibleRows(r, ref n);
            if (FolderOrganize.RowBudgetReached(n)) break;
        }
        UpdateOrganizeHeader();
    }

    private void AddVisibleRows(OrganizeNode node, ref int count)
    {
        if (count >= FolderOrganize.MaxRows) return;
        if (!_organizePendingOnly || node.IsPending) { _organizeRows.Add(node); count++; }
        if (!node.IsExpanded) return;
        foreach (var c in node.Children) AddVisibleRows(c, ref count);
    }

    private void UpdateOrganizeFilterLabel()
    {
        if (OrganizeFilterPendingBtn == null) return;
        int pending = _organizeAll.Count(x => x.IsPending);
        OrganizeFilterPendingBtn.Content = _organizePendingOnly
            ? Loc.OrganizeFilterPending + " · " + pending
            : Loc.OrganizeFilterPending;
    }

    /// <summary>页头统计：对象数 / 容量 / 已认出 / 待确认，外加一次性短提示。</summary>
    private void UpdateOrganizeHeader()
    {
        if (OrganizeSub == null) return;
        UpdateOrganizeFilterLabel();

        if (_root == null)
        {
            OrganizeSub.Text = Loc.OrganizeNoScan;
            OrganizeCounts.Text = "";
            OrganizeIdentifyAllBtn.IsEnabled = false;
            return;
        }

        long bytes = 0;
        foreach (var r in _organizeRoots) bytes += r.Size;
        int resolved = _organizeAll.Count(x => x.IsResolved);
        int pending = _organizeAll.Count(x => x.IsPending);

        OrganizeSub.Text = Loc.OrganizeTotals(_organizeAll.Count, bytes);
        string line = Loc.OrganizeCounts(resolved, pending);
        if (_organizePendingOnly) line += " · " + Loc.OrganizeFilterActive(_organizeRows.Count, _organizeAll.Count);
        if (_organizeNote.Length > 0) line += " · " + _organizeNote;
        OrganizeCounts.Text = line;
        OrganizeIdentifyAllBtn.IsEnabled = !_organizeBusy && pending > 0;
    }

    private void SetOrganizeNote(string text)
    {
        _organizeNote = text ?? "";
        UpdateOrganizeHeader();
    }

    // ==================== 状态框 ====================

    private void ShowOrganizeState(OrganizeStateKind kind)
    {
        if (OrganizeStateBox == null) return;
        if (kind == OrganizeStateKind.None)
        {
            OrganizeStateBox.Visibility = Visibility.Collapsed;
            _organizeStateAction = null;
            return;
        }
        OrganizeStateBox.Visibility = Visibility.Visible;
        OrganizeStateBtn.Visibility = Visibility.Visible;
        switch (kind)
        {
            case OrganizeStateKind.NoScan:
                OrganizeStateTitle.Text = Loc.OrganizeNoScan;
                OrganizeStateBody.Text = Loc.OrganizeNoScanBody;
                OrganizeStateBtn.Content = Loc.Scan;
                _organizeStateAction = () => RunScan();
                break;
            case OrganizeStateKind.Scanning:
                OrganizeStateTitle.Text = Loc.OrganizeScanning;
                OrganizeStateBody.Text = Loc.OrganizeScanningBody;
                OrganizeStateBtn.Visibility = Visibility.Collapsed;
                _organizeStateAction = null;
                break;
            case OrganizeStateKind.Empty:
                OrganizeStateTitle.Text = Loc.OrganizeEmpty;
                OrganizeStateBody.Text = Loc.OrganizeEmptyBody;
                OrganizeStateBtn.Content = Loc.Scan;
                _organizeStateAction = () => RunScan();
                break;
            case OrganizeStateKind.NoModel:
                OrganizeStateTitle.Text = Loc.OrganizeNoModel;
                OrganizeStateBody.Text = Loc.OrganizeNoModelBody;
                OrganizeStateBtn.Content = Loc.OrganizeOpenSettings;
                _organizeStateAction = () => OpenSettingsFromOrganize();
                break;
            case OrganizeStateKind.Failed:
                OrganizeStateTitle.Text = Loc.OrganizeFailed;
                OrganizeStateBody.Text = Loc.OrganizeFailedBody;
                OrganizeStateBtn.Content = Loc.Scan;
                _organizeStateAction = () => RunScan();
                break;
            default:
                OrganizeStateTitle.Text = Loc.OrganizeAllDone;
                OrganizeStateBody.Text = Loc.OrganizeAllDoneBody;
                OrganizeStateBtn.Visibility = Visibility.Collapsed;
                _organizeStateAction = null;
                break;
        }
    }

    private void OrganizeStateAction_Click(object sender, RoutedEventArgs e)
        => _organizeStateAction?.Invoke();

    private void OpenSettingsFromOrganize()
    {
        try { SettingsButton_Click(SettingsButton, new RoutedEventArgs()); }
        catch (Exception ex) { AppLog.Record("Organize", ex, "open settings"); }
    }

    // ==================== 展开 / 收起 ====================

    private void OrganizeExpand_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is OrganizeNode t) { ToggleOrganize(t); return; }
        if ((sender as FrameworkElement)?.DataContext is OrganizeNode n) ToggleOrganize(n);
    }

    private void ToggleOrganize(OrganizeNode node)
    {
        if (node.IsExpanded)
        {
            node.Collapse();
        }
        else
        {
            // 展开不重复请求：已经材料化过就直接显示（结果仍在节点上）
            if (!node.ChildrenLoaded) Materialize(node, FolderOrganize.ChildBudget);
            else node.Expand();
        }
        RefreshOrganizeRows();
        AppLog.Info("Organize", $"op=toggle dir={node.Name} expanded={node.IsExpanded} rows={_organizeRows.Count}");
    }

    private void OrganizeExpandMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_organizeMenuNode != null) ToggleOrganize(_organizeMenuNode);
    }

    // ==================== 待确认筛选 ====================

    private void OrganizeFilter_Click(object sender, RoutedEventArgs e)
    {
        _organizePendingOnly = !_organizePendingOnly;
        // 开筛选时把已经材料化过的层级摊开：否则待确认的对象藏在折叠节点里看不见
        if (_organizePendingOnly)
            foreach (var n in _organizeAll) if (n.ChildrenLoaded) n.Expand();
        RefreshOrganizeRows();
        ShowOrganizeState(_organizePendingOnly && _organizeRows.Count == 0
            ? OrganizeStateKind.AllDone
            : OrganizeStateKind.None);
    }

    // ==================== 识别（单项 + 批量） ====================

    private void OrganizeIdentifyAll_Click(object sender, RoutedEventArgs e)
    {
        if (_organizeBusy) { CancelOrganizeWork(clearState: false); return; }
        var targets = _organizeAll.Where(x => x.IsPending)
            .OrderByDescending(x => x.Size).ToList();
        if (targets.Count == 0) { ShowOrganizeState(OrganizeStateKind.AllDone); return; }
        _ = RunOrganizeIdentifyAsync(targets);
    }

    private void OrganizeIdentifyOne_Click(object sender, RoutedEventArgs e)
    {
        OrganizeNode? node = (sender as FrameworkElement)?.Tag as OrganizeNode
            ?? (sender as FrameworkElement)?.DataContext as OrganizeNode
            ?? _organizeMenuNode;
        if (node == null) return;
        if (_organizeBusy) { SetOrganizeNote(Loc.OrganizeWaitNoResult); return; }
        _ = RunOrganizeIdentifyAsync(new List<OrganizeNode> { node });
    }

    private void OrganizeStop_Click(object sender, RoutedEventArgs e)
        => CancelOrganizeWork(clearState: false);

    /// <summary>
    /// 批量识别：**只处理用户点名的那一批**，不自动扩大到整盘。
    ///
    /// 先跑一遍本地规则（不花请求）；剩下的确实需要模型时才问，
    /// 而且受请求总量、超时、取消与扫描代次三重约束。
    /// </summary>
    private async Task RunOrganizeIdentifyAsync(List<OrganizeNode> targets)
    {
        if (_root == null) { ShowOrganizeState(OrganizeStateKind.NoScan); return; }

        int myGen = _organizeGeneration;
        int myData = _aiDataGeneration;
        bool allowAi = AiConfigured();
        int budget = FolderOrganize.MaxAiRequests;

        _organizeStop?.Dispose();
        _organizeStop = new CancellationTokenSource();
        var ct = _organizeStop.Token;
        _organizeBusy = true;
        OrganizeProgressPanel.Visibility = Visibility.Visible;
        OrganizeStopBtn.Visibility = Visibility.Visible;
        OrganizeIdentifyAllBtn.IsEnabled = false;
        OrganizeProgressBar.Value = 0;
        ShowOrganizeState(OrganizeStateKind.None);

        // 排队：**排队期间不显示任何成功结论**（文案由 State 决定）
        foreach (var t in targets) if (!t.HasConclusion) t.SetState(PurposeState.Queued);
        OrganizeScopeText.Text = Loc.OrganizeIdentifyScope(targets.Count) + " · " + Loc.OrganizeBudget(budget);
        OrganizeScopeBar.Visibility = Visibility.Visible;

        int done = 0;
        int aiBefore = _folderPurpose.AiRequestsUsed;
        try
        {
            // 1) 本地 + 未知有限探索（不花任何请求）
            int i = 0;
            foreach (var t in targets)
            {
                if (ct.IsCancellationRequested) break;
                if (Stale(myGen, myData)) break;
                if (!t.HasConclusion) LocalRecognize(t);
                if (!t.HasConclusion) ProbeUnknownLocally(t);
                done++;
                // 一屏上千个对象时也让出时间片，界面不会被这一遍拖住
                if (++i % 32 == 0)
                {
                    SetOrganizeProgress(done, targets.Count, _folderPurpose.AiRequestsUsed, budget);
                    await Task.Yield();
                }
            }
            SetOrganizeProgress(done, targets.Count, _folderPurpose.AiRequestsUsed, budget);

            // 2) 还说不清的才用模型；没配模型就如实说，不假装成功
            if (!allowAi)
            {
                foreach (var t in targets) if (!t.HasConclusion) t.ClearState();
                int left = targets.Count(t => t.IsPending);
                if (left > 0) ShowOrganizeState(OrganizeStateKind.NoModel);
                AppLog.Info("Organize", $"op=identify local-only targets={targets.Count} pending={left}");
            }
            else
            {
                foreach (var t in targets)
                {
                    if (ct.IsCancellationRequested || Stale(myGen, myData)) break;
                    if (t.HasConclusion) continue;
                    if (_folderPurpose.AiRequestsUsed >= budget) break;

                    t.SetState(PurposeState.Running);
                    var res = await RecognizeWithAsync(t.Dir, allowAi: true, ct).ConfigureAwait(true);
                    if (Stale(myGen, myData)) break;     // 旧请求绝不覆盖新扫描
                    // 用户在这期间纠正过 ⇒ 用户结论优先
                    var user = _folderPurpose.TryGetUserCorrection(t.Id);
                    t.Apply(user ?? res);
                }
            }

            if (ct.IsCancellationRequested)
            {
                foreach (var t in targets) if (!t.HasConclusion) t.ClearState();
                SetOrganizeNote(Loc.PurposeCancelled);
            }
            else if (Stale(myGen, myData))
            {
                // 结果已经过期：什么都不写回，列表马上会被重建
                return;
            }
            else
            {
                int aiUsed = Math.Max(0, _folderPurpose.AiRequestsUsed - aiBefore);
                int aiCount = targets.Count(x => x.Source == PurposeSource.Ai);
                int localCount = targets.Count(x => x.Source == PurposeSource.Local);
                SetOrganizeNote(Loc.OrganizeResultNote(aiCount, localCount)
                    + (aiUsed > 0 ? " · " + Loc.OrganizeProgress(done, targets.Count,
                        _folderPurpose.AiRequestsUsed, budget) : ""));
                // 请求上限到了：把「为什么剩下的没结果」说清楚，不让人以为卡住了
                if (_folderPurpose.AiRequestsUsed >= budget && targets.Any(x => x.IsPending))
                    SetOrganizeNote(Loc.OrganizeBudgetUsed(_folderPurpose.AiRequestsUsed));
                if (_organizeAll.All(x => !x.IsPending)) ShowOrganizeState(OrganizeStateKind.AllDone);
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var t in targets) if (!t.HasConclusion) t.ClearState();
            SetOrganizeNote(Loc.PurposeCancelled);
        }
        catch (Exception ex)
        {
            AppLog.Record("Organize", ex, "organize identify");
            foreach (var t in targets) if (!t.HasConclusion) t.ClearState();
            ShowOrganizeState(OrganizeStateKind.Failed);
        }
        finally
        {
            _organizeBusy = false;
            // 预算用完 / 提前退出时，没结论的对象绝不能停在「排队中」——
            // 那会让人以为还在跑。清掉流程态，落回「未识别 / 待确认」。
            foreach (var t in targets)
                if (!t.HasConclusion && t.State is PurposeState.Queued or PurposeState.Running)
                    t.ClearState();
            OrganizeProgressPanel.Visibility = Visibility.Collapsed;
            OrganizeStopBtn.Visibility = Visibility.Collapsed;
            OrganizeScopeBar.Visibility = Visibility.Collapsed;
            RefreshOrganizeRows();
        }
    }

    /// <summary>代次守卫：列表被重建或扫描换代以后，旧请求的结果一律丢弃。</summary>
    private bool Stale(int myGen, int myData)
        => myGen != _organizeGeneration || myData != _aiDataGeneration;

    private void SetOrganizeProgress(int done, int total, int used, int budget)
    {
        OrganizeProgressBar.Value = total <= 0 ? 0 : Math.Clamp(done * 100.0 / total, 0, 100);
        OrganizeProgressText.Text = Loc.OrganizeProgress(done, total, used, budget);
    }

    /// <summary>
    /// 未知目录的**有限探索**：只看几个直接子目录（本地规则，不发请求），
    /// 看完还是说不清就老老实实标「待确认」。容量统计完全不受影响。
    /// </summary>
    private void ProbeUnknownLocally(OrganizeNode node)
    {
        // 收纳/混合走共享规则；**未知目录**只有整理页在用户点名识别时多看一层
        bool allowed = FolderOrganize.ShouldProbeUnknown(node.Dir, node.Kind, node.Depth)
                       || FolderPurposeService.ShouldDescend(node.Dir, node.Kind, node.Depth);
        if (!allowed) return;
        if (FolderOrganize.RowBudgetReached(_organizeAll.Count)) return;
        Materialize(node, FolderOrganize.UnknownProbeBudget);
        // 本身就是未知：不硬给结论（也不假装成功），只把子对象摊开给人看
        node.ClearState();
    }

    private void CancelOrganizeWork(bool clearState)
    {
        try { _organizeStop?.Cancel(); } catch { /* 取消失败不影响界面状态 */ }
        _organizeStop?.Dispose();
        _organizeStop = null;
        _organizeBusy = false;
        if (OrganizeProgressPanel != null) OrganizeProgressPanel.Visibility = Visibility.Collapsed;
        if (OrganizeStopBtn != null) OrganizeStopBtn.Visibility = Visibility.Collapsed;
        if (OrganizeScopeBar != null) OrganizeScopeBar.Visibility = Visibility.Collapsed;
        if (clearState)
        {
            foreach (var n in _organizeAll) n.ClearState();
            if (OrganizeIdentifyAllBtn != null) OrganizeIdentifyAllBtn.IsEnabled = true;
        }
    }

    // ==================== 右键菜单 / 打开 / 纠正 ====================

    private void OrganizeGrid_PreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        _organizeMenuNode = null;
        if (e.OriginalSource is not DependencyObject src) return;
        var row = Ancestor<DataGridRow>(src);
        if (row?.Item is OrganizeNode n) _organizeMenuNode = n;
        else if ((e.OriginalSource as FrameworkElement)?.DataContext is OrganizeNode d) _organizeMenuNode = d;
    }

    private void OrganizeMenu_Opened(object sender, RoutedEventArgs e)
    {
        var n = _organizeMenuNode;
        if (OrgCtxIdentify != null) OrgCtxIdentify.IsEnabled = n is { HasConclusion: false };
        if (OrgCtxOpen != null) OrgCtxOpen.IsEnabled = n != null;
        if (OrgCtxCopy != null) OrgCtxCopy.IsEnabled = n != null;
        if (OrgCtxExpand != null)
        {
            OrgCtxExpand.IsEnabled = n?.CanExpand == true;
            OrgCtxExpand.Header = n?.IsExpanded == true ? Loc.OrganizeCollapse : Loc.OrganizeExpand;
        }
        FillOrganizeCorrectMenu(n);
    }

    private void FillOrganizeCorrectMenu(OrganizeNode? node)
    {
        if (OrgCtxCorrect == null) return;
        OrgCtxCorrect.Items.Clear();
        OrgCtxCorrect.IsEnabled = node != null;
        if (node == null) return;
        foreach (var name in Loc.PurposeCorrections)
        {
            var mi = new MenuItem { Header = name, Tag = node };
            mi.Click += OrganizeCorrect_Click;
            OrgCtxCorrect.Items.Add(mi);
        }
    }

    /// <summary>
    /// 用户纠正：**优先保留**，重扫不丢，并且落盘，下次启动仍然有效。
    /// 只改用途展示，不碰任何清理字段。
    /// </summary>
    private void OrganizeCorrect_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not OrganizeNode node) return;
        string picked = (sender as MenuItem)?.Header?.ToString() ?? "";
        if (picked.Length == 0) return;
        _folderPurpose.SetUserCorrection(node.Id, picked, picked);
        _folderPurpose.SaveCorrections();
        var res = _folderPurpose.TryGetUserCorrection(node.Id);
        if (res != null) node.Apply(res);
        SetOrganizeNote(Loc.PurposeCorrected(picked));
        RefreshOrganizeRows();
        AppLog.Info("Organize", $"op=correct dir={node.Name} value={picked}");
    }

    private void OrganizeOpen_Click(object sender, RoutedEventArgs e)
    {
        var node = _organizeMenuNode
            ?? (sender as FrameworkElement)?.Tag as OrganizeNode
            ?? (sender as FrameworkElement)?.DataContext as OrganizeNode;
        if (node != null) RevealOrganize(node);
    }

    private void OrganizeReveal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is OrganizeNode node) RevealOrganize(node);
    }

    /// <summary>只打开真实目录（<see cref="ShellReveal"/>），**不执行**里面的任何程序。</summary>
    private void RevealOrganize(OrganizeNode node)
    {
        var r = ShellReveal.Reveal(node.FullPath);
        if (r.Ok) return;
        if (r.Kind == RevealKind.NoPath) return;
        SetOrganizeNote(r.Message);
        AppLog.Info("Organize", $"reveal {r.Kind} for {LogRedactor.ScrubPath(r.Target)}");
    }

    private void OrganizeCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var node = _organizeMenuNode;
        if (node == null) return;
        try
        {
            Clipboard.SetText(node.FullPath);
            SetOrganizeNote(Loc.CopyPath);
        }
        catch (Exception ex)
        {
            // 剪贴板被别的进程占用：只记日志，不打断
            AppLog.Record("Organize", ex, "copy path");
        }
    }
}
