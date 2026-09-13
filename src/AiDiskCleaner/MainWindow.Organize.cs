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
/// <item>扫描一结束就直接列出本地整理出来的文件夹对象（名称 / 用途 / 容量 / 真实路径）；</item>
/// <item>**自动识别一、二级**：本地能判断的当场认出，本地说不清的扫描后在后台批量补 AI，
/// 用户不需要逐个点按钮；</item>
/// <item>**三级及更深一律不自动调用 AI** —— 进入到具体文件夹后，才用「识别当前文件夹」
/// 显式分析**当前这一层**的直接子目录（有范围说明、请求数量、取消、超时、重试）；</item>
/// <item>平台游戏库这类容器保留往里找游戏的入口，但同样只铺到二级。</item>
/// </list>
///
/// 安全边界：这里**没有**任何删除/选择代码。识别不改 Risk / CanDelete / Selected，
/// 不自动选择、不移动、不归档真实文件；「在资源管理器中打开」只打开目录，不执行任何程序。
/// 送给模型的内容有硬上限并强制脱敏（见 <see cref="SourceSnippetReader"/>）。
/// </summary>
public partial class MainWindow
{
    /// <summary>列表数据源（扁平：展开时把子对象插到父对象后面）。</summary>
    private readonly ObservableCollection<OrganizeNode> _organizeRows = new();

    /// <summary>所有已经材料化出来的对象（自动识别的范围就是它）。</summary>
    private readonly List<OrganizeNode> _organizeAll = new();

    /// <summary>按真实目录索引，保证同一个目录只有一个对象（不重复建节点）。</summary>
    private readonly Dictionary<FileEntry, OrganizeNode> _organizeByDir = new();

    /// <summary>顶层对象（扁平顺序从这里出发）。</summary>
    private readonly List<OrganizeNode> _organizeRoots = new();

    /// <summary>系统解析出来的识别入口（一次解析，批量识别复用，不再逐行碰磁盘）。</summary>
    private IReadOnlyList<string> _organizeEntryPoints = Array.Empty<string>();

    /// <summary>「待确认」筛选：只看还没认出来的（避免满屏重复的「未识别」）。</summary>
    private bool _organizePendingOnly;

    /// <summary>正在识别（自动批量或当前文件夹，同一时刻只允许一个，避免重复请求）。</summary>
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

    /// <summary>「当前文件夹」：三级手动识别的目标（展开/点选都会更新它）。</summary>
    private OrganizeNode? _organizeCurrent;

    /// <summary>自动识别是否已经跑过一次（同一次扫描不重复自动跑）。</summary>
    private bool _organizeAutoStarted;

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
        OrganizeIdentifyAllBtn.Content = Loc.OrganizeRetryPending;
        OrganizeIdentifyAllBtn.ToolTip = Loc.OrganizeRetryPendingTip;
        System.Windows.Automation.AutomationProperties.SetName(
            OrganizeIdentifyAllBtn, Loc.OrganizeRetryPendingTip);
        OrganizeStopBtn.Content = Loc.OrganizeStop;
        OrganizeFilterPendingBtn.ToolTip = Loc.OrganizeFilterPendingTip;
        System.Windows.Automation.AutomationProperties.SetName(
            OrganizeFilterPendingBtn, Loc.OrganizeFilterPendingTip);
        if (OrganizeIdentifyCurrentBtn != null)
        {
            OrganizeIdentifyCurrentBtn.Content = Loc.OrganizeIdentifyThisLevel;
            OrganizeIdentifyCurrentBtn.ToolTip = Loc.OrganizeThisLevelOnly;
            System.Windows.Automation.AutomationProperties.SetName(
                OrganizeIdentifyCurrentBtn, Loc.OrganizeIdentifyThisLevel);
        }
        OrgCtxOpen.Header = Loc.OrganizeOpen;
        OrgCtxCopy.Header = Loc.OrganizeCopyPath;
        OrgCtxCorrect.Header = Loc.PurposeCorrect;
        OrgCtxExpand.Header = Loc.OrganizeExpand;
        OrgCtxIdentifyCurrent.Header = Loc.OrganizeIdentifyThisLevel;
        ApplyOrganizeColumnPriority(OrganizeContentWidth());
        UpdateOrganizeHeader();
        UpdateOrganizeWorkBar();
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
        ColOrgAction.Width = new DataGridLength(tight ? 104 : 132);
        ColOrgSize.Visibility = width > 0 && width < 620 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ==================== 重建对象列表 ====================

    /// <summary>
    /// 用当前扫描结果重建整理对象。**只读扫描树**，不碰磁盘、不调模型。
    /// 每次重建都推进代次，旧识别结果不会再回写到新列表。
    /// 建完就启动**一、二级的自动识别**（本地立即完成，AI 在后台批量补）。
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
        _organizeCurrent = null;
        _organizeAutoStarted = false;
        _organizeUnlistedTotal = 0;
        UpdateOrganizeFilterLabel();

        if (_root == null)
        {
            ShowOrganizeState(OrganizeStateKind.NoScan);
            UpdateOrganizeHeader();
            UpdateOrganizeWorkBar();
            return;
        }

        try
        {
            var rs = FolderOrganize.BuildRoots(_root);
            _organizeEntryPoints = rs.EntryPoints;
            foreach (var d in rs.Roots) _organizeRoots.Add(CreateOrganizeNode(d, 0, FolderOrganize.ForRoot(d)));

            if (_organizeRoots.Count == 0)
            {
                ShowOrganizeState(OrganizeStateKind.Empty);
                UpdateOrganizeHeader();
                UpdateOrganizeWorkBar();
                return;
            }

            // 后台把一、二级对象建出来（不展开，首屏只见根的一级）
            AutoMaterializeAll();

            if (rs.Skipped > 0) _organizeNote = Loc.OrganizeSkippedNoAccess(rs.Skipped);
            ShowOrganizeState(OrganizeStateKind.None);
            RefreshOrganizeRows();
            AppLog.Info("Organize", $"op=build roots={_organizeRoots.Count} objects={_organizeAll.Count} "
                + $"rows={_organizeRows.Count} entries={_organizeEntryPoints.Count} skipped={rs.Skipped} "
                + $"levels=1-{OrganizeLevelPolicy.AutoChildLevel}");
        }
        catch (Exception ex)
        {
            AppLog.Record("Organize", ex, "organize build");
            ShowOrganizeState(OrganizeStateKind.Failed);
            UpdateOrganizeHeader();
            UpdateOrganizeWorkBar();
            return;
        }

        // 扫描完成即自动识别一、二级：本地这一遍就地完成，AI 在后台补
        StartOrganizeAutoIdentify();
    }

    private OrganizeNode CreateOrganizeNode(FileEntry dir, int depth, OrganizeLevelPolicy policy)
    {
        if (_organizeByDir.TryGetValue(dir, out var existing)) return existing;

        string rel = RelativeOf(dir);
        bool isEntry = FolderOrganize.IsEntryPoint(dir.FullPath, _organizeEntryPoints);
        var node = new OrganizeNode(dir, new FolderId(dir.FullPath, _aiDataGeneration), depth, rel)
        {
            IsSystemEntry = isEntry,
            IsPlatformContainer = FolderOrganize.KeepsObjectEntries(dir),
        };
        node.SetLevel(policy.Level);
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
        node.SetEvidence(sum);
        node.SetKind(local.Kind);
        if (local.HasConclusion) node.Apply(local);
    }

    /// <summary>
    /// 后台把一、二级对象建出来（**材料化 ≠ 展开**）。
    ///
    /// 分两趟做，是刻意的：**先给每个根铺直接子项，再铺第 1 级对象的下一层**。
    /// 如果按「一个根一路铺到底」，第一个大目录（比如 Program Files）就会把对象预算
    /// 全部吃掉，后面的根连直接子项都建不出来 —— 那样识别覆盖会严重不均。
    /// 两趟遍历保证每个根都先拿到自己那一层。
    ///
    /// 首屏可见树只显示根的一级：这里建好的子对象默认都不展开。
    /// </summary>
    private void AutoMaterializeAll()
    {
        // 第 1 趟：每个根的直接子项
        foreach (var r in _organizeRoots) TryAutoMaterialize(r);
        // 第 2 趟：第 1 级对象的下一层（第 2 级对象）
        var level1 = _organizeAll
            .Where(n => n.Level == OrganizeLevelPolicy.AutoLevel)
            .OrderByDescending(n => n.Size)
            .ToList();
        foreach (var n in level1) TryAutoMaterialize(n);
    }

    private void TryAutoMaterialize(OrganizeNode node)
    {
        if (FolderOrganize.ObjectBudgetReached(_organizeAll.Count)) return;
        if (!FolderOrganize.ShouldAutoMaterializeChildren(
                new OrganizeLevelPolicy(node.Level, node.Size, node.FileCount), node.Dir, node.Kind,
                _organizeEntryPoints)) return;
        Materialize(node, FolderOrganize.ChildBudget, FolderOrganize.AutoMaterializeBudget(node.Depth == 0));
    }

    /// <summary>
    /// 把一个目录的直接子对象材料化出来（有预算、有去重、有上限）。
    /// 子对象的层级由父对象 + 子对象是不是系统入口一起决定（入口的直接子目录与入口同级）。
    /// </summary>
    private void Materialize(OrganizeNode node, int childBudget, int? materializeBudget = null)
    {
        if (FolderOrganize.ObjectBudgetReached(_organizeAll.Count)) return;
        var parentPolicy = new OrganizeLevelPolicy(node.Level, node.Size, node.FileCount);
        bool parentIsEntry = FolderOrganize.IsEntryPoint(node.FullPath, _organizeEntryPoints);
        int cap = Math.Max(childBudget, materializeBudget ?? childBudget);
        var set = FolderOrganize.DirectChildDirs(node.Dir, cap);
        var kids = new List<OrganizeNode>(set.Dirs.Count);
        int alreadyTop = 0;
        foreach (var d in set.Dirs)
        {
            if (FolderOrganize.ObjectBudgetReached(_organizeAll.Count)) break;
            // 已经是顶层对象的目录不再挂到父节点下面：同一个真实目录只出现一次
            if (_organizeByDir.ContainsKey(d)) { alreadyTop++; continue; }
            bool isEntry = FolderOrganize.IsEntryPoint(d.FullPath, _organizeEntryPoints);
            kids.Add(CreateOrganizeNode(d, node.Depth + 1,
                FolderOrganize.ForChild(parentPolicy, d, isEntry, parentIsEntry)));
        }
        // 没材料化出来的子目录：如实计数 —— 它们**没有结论**，也不是「已识别」。
        // 已经是顶层对象的不算「未列出」（它们就在首页那一级）。
        int unlisted = Math.Max(0, set.Total - kids.Count - alreadyTop);
        node.SetChildren(kids, set.Truncated || unlisted > 0, set.Total, unlisted);
        RegisterUnlisted(node, unlisted);
    }

    /// <summary>累计「没有材料化、也没有结论」的直接子目录数（页头如实展示）。</summary>
    private int _organizeUnlistedTotal;

    private void RegisterUnlisted(OrganizeNode node, int unlisted)
    {
        int before = node.UnlistedChildCount;
        if (unlisted == before) return;
        _organizeUnlistedTotal += unlisted - before;
        if (_organizeUnlistedTotal < 0) _organizeUnlistedTotal = 0;
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

    /// <summary>页头统计：对象数 / 容量 / 已认出 / 待确认 / 失败，外加一次性短提示。</summary>
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

        // 顶层对象之间现在允许「入口与它的祖先同时出现」，
        // 所以**不能把根的容量相加**（会重复计算）。直接用扫描根的真实总量。
        long bytes = _root.Size;
        int resolved = _organizeAll.Count(x => x.IsResolved);
        int pending = _organizeAll.Count(x => x.IsPending);
        int failed = _organizeAll.Count(x => x.State == PurposeState.Failed);

        OrganizeSub.Text = Loc.OrganizeTotals(_organizeAll.Count, bytes);
        string line = Loc.OrganizeCounts(resolved, pending, failed);
        if (_organizeUnlistedTotal > 0) line += " · " + Loc.OrganizeUnlistedTotal(_organizeUnlistedTotal);
        if (_organizePendingOnly) line += " · " + Loc.OrganizeFilterActive(_organizeRows.Count, _organizeAll.Count);
        if (_organizeNote.Length > 0) line += " · " + _organizeNote;
        OrganizeCounts.Text = line;
        // 统一重试入口：只要还有待确认/失败的，就允许再跑一遍（受同一个预算约束）
        OrganizeIdentifyAllBtn.IsEnabled = !_organizeBusy && pending > 0;
    }

    private void SetOrganizeNote(string text)
    {
        _organizeNote = text ?? "";
        UpdateOrganizeHeader();
    }

    // ==================== 「当前文件夹」工作区（三级手动识别） ====================

    /// <summary>展开 / 点选一个对象 ⇒ 它成为「当前文件夹」，工作区跟着更新。</summary>
    private void SetOrganizeCurrent(OrganizeNode? node)
    {
        _organizeCurrent = node;
        UpdateOrganizeWorkBar();
    }

    /// <summary>
    /// 工作条：**只在深层（三级及更深）出现**。
    ///
    /// 一、二级是后台自动识别范围，整表列的就是这一层；如果在整表上方再写一句
    /// 「当前文件夹：xxx」，用户会以为下面列的是那个文件夹的内容 —— 语义是错的。
    /// 所以自动层**不显示这个条**，只有进入三级及更深、需要用户手动识别本层时
    /// 才出现，并且明确写清「只识别这个文件夹的直接子文件夹」。
    /// </summary>
    private void UpdateOrganizeWorkBar()
    {
        if (OrganizeWorkBar == null) return;
        var node = _organizeCurrent;
        if (node == null || _root == null || !node.IsDeepLevel)
        {
            OrganizeWorkBar.Visibility = Visibility.Collapsed;
            return;
        }
        OrganizeWorkBar.Visibility = Visibility.Visible;
        OrganizeWorkTitle.Text = Loc.OrganizeWorkTitleFixed(node.Name, node.Level);
        OrganizeWorkPath.Text = node.FullPath;

        var plan = FolderOrganize.PlanFor(node.Name, node.FullPath, node.Level, node.ChildDirCount);
        OrganizeWorkScope.Text = Loc.OrganizeWorkBarScope(plan.DirectChildTotal, plan.RequestBudget)
            + " · " + Loc.OrganizeThisLevelOnly;
        OrganizeIdentifyCurrentBtn.IsEnabled = !_organizeBusy && plan.DirectChildTotal > 0;
    }

    /// <summary>进入当前文件夹：只把**这一层**的直接子目录材料化出来，不继续往下展开。</summary>
    private List<OrganizeNode> OpenCurrentFolderChildren(int budget)
    {
        var node = _organizeCurrent;
        if (node == null) return new List<OrganizeNode>();
        if (!FolderPurposeRules.CanDescend(node.Dir)) return new List<OrganizeNode>();
        Materialize(node, Math.Max(1, budget));
        return node.Children.ToList();
    }

    private void OrganizeIdentifyCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_organizeBusy) { SetOrganizeNote(Loc.OrganizeWaitNoResult); return; }
        var node = _organizeCurrent;
        if (node == null) { SetOrganizeNote(Loc.OrganizeWorkNone); return; }

        // 先把这一层铺出来，再对**这一层直接子目录**做识别：范围与界面写的完全一致
        int budget = FolderOrganize.RequestBudgetFor(node.ChildDirCount);
        var scope = OpenCurrentFolderChildren(budget);
        if (scope.Count == 0) { SetOrganizeNote(Loc.OrganizeWorkNone); return; }
        _ = RunOrganizeIdentifyAsync(scope, Loc.OrganizeWorkBarScope(scope.Count, budget), auto: false);
    }

    private void OrganizeIdentifyCurrentMenu_Click(object sender, RoutedEventArgs e)
    {
        var node = _organizeMenuNode ?? _organizeCurrent;
        if (node == null) return;
        SetOrganizeCurrent(node);
        OrganizeIdentifyCurrent_Click(sender, e);
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
        // 用户展开谁，谁就是「当前文件夹」：三级及更深的手动识别就作用在它这一层
        SetOrganizeCurrent(node);
        RefreshOrganizeRows();
        AppLog.Info("Organize", $"op=toggle dir={node.Name} level={node.Level} "
            + $"expanded={node.IsExpanded} rows={_organizeRows.Count}");
    }

    /// <summary>
    /// 点用途文字：展开 / 收起这一行的说明（这是什么 · 为什么这么判断 · 来源）。
    /// **只改展示**：不触发识别、不动结论、不碰任何清理字段。
    /// </summary>
    private void OrganizePurpose_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var node = (sender as FrameworkElement)?.DataContext as OrganizeNode;
        if (node == null) return;
        // PropertyChanged 会自己刷新这一行的高度（RowHeight=Auto），不需要整表重绘
        node.ToggleDetail();
        e.Handled = true;
    }

    private void OrganizeExpandMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_organizeMenuNode != null) ToggleOrganize(_organizeMenuNode);
    }

    /// <summary>双击一行 = 进入这个文件夹（展开它，并把「识别当前文件夹」指向它）。</summary>
    private void OrganizeGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is OrganizeNode n)
        {
            if (!n.IsExpanded) ToggleOrganize(n);
            else SetOrganizeCurrent(n);
        }
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

    // ==================== 识别（自动批量 + 当前文件夹） ====================

    /// <summary>
    /// 扫描完成后自动跑一遍：范围是**一、二级里还没认出来的对象**。
    /// 本地这一遍立即完成；本地说不清的才排队问模型（后台、有预算、可取消）。
    /// </summary>
    private void StartOrganizeAutoIdentify()
    {
        if (_organizeAutoStarted) return;
        _organizeAutoStarted = true;
        if (_organizeBusy) return;

        var targets = AutoIdentifyTargets();
        if (targets.Count == 0) return;

        bool allowAi = AiConfigured();
        int budget = FolderOrganize.MaxAiRequests;
        // 没有配置模型、或本地已经全认出来了 ⇒ 不发任何请求，也不显示进度条
        bool anyNeedsModel = targets.Any(t => !t.HasConclusion);
        if (!allowAi && anyNeedsModel)
        {
            ShowOrganizeState(OrganizeStateKind.NoModel);
            SetOrganizeNote(Loc.OrganizeDeepOnly);
            return;
        }
        if (!allowAi) return;

        SetOrganizeNote(Loc.OrganizeAutoStart(targets.Count, budget));
        _ = RunOrganizeIdentifyAsync(targets, Loc.OrganizeAutoStart(targets.Count, budget), auto: true);
    }

    /// <summary>自动识别范围：一、二级 + 还没有结论 + 还能往下看（重解析点不碰）。</summary>
    private List<OrganizeNode> AutoIdentifyTargets()
        => _organizeAll
            .Where(x => x.IsAutoLevel
                        && FolderOrganize.ShouldAutoIdentify(
                            new OrganizeLevelPolicy(x.Level, x.Size, x.FileCount),
                            x.HasConclusion, FolderPurposeRules.CanDescend(x.Dir)))
            .OrderByDescending(x => x.Size)
            .ToList();

    private void OrganizeIdentifyAll_Click(object sender, RoutedEventArgs e)
    {
        // 统一重试入口：已经在跑就当作取消
        if (_organizeBusy) { CancelOrganizeWork(clearState: false); return; }
        var targets = _organizeAll.Where(x => x.IsPending)
            .OrderByDescending(x => x.Size).ToList();
        if (targets.Count == 0) { ShowOrganizeState(OrganizeStateKind.AllDone); return; }
        _ = RunOrganizeIdentifyAsync(targets, Loc.OrganizeIdentifyScope(targets.Count), auto: false);
    }

    private void OrganizeStop_Click(object sender, RoutedEventArgs e)
        => CancelOrganizeWork(clearState: false);

    /// <summary>
    /// 批量识别：**只处理传进来的那一批**，不自动扩大到整盘、也不自动往更深层扩散。
    ///
    /// 先跑一遍本地规则（不花请求）；剩下的确实需要模型时才问，而且受请求总量、
    /// 单次超时、请求间隔、取消与扫描/重建代次多重约束。
    /// </summary>
    private async Task RunOrganizeIdentifyAsync(List<OrganizeNode> targets, string scopeText, bool auto)
    {
        if (_root == null) { ShowOrganizeState(OrganizeStateKind.NoScan); return; }
        if (targets.Count == 0) return;

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
        OrganizeIdentifyCurrentBtn.IsEnabled = false;
        OrganizeProgressBar.Value = 0;
        ShowOrganizeState(OrganizeStateKind.None);

        // 排队：**排队期间不显示任何成功结论**（文案由 State 决定）
        foreach (var t in targets) if (!t.HasConclusion) t.SetState(PurposeState.Queued);
        OrganizeScopeText.Text = scopeText + " · " + Loc.OrganizeBudget(budget);
        OrganizeScopeBar.Visibility = Visibility.Visible;
        // 顶栏这时只能报「识别中」，绝不能报完成
        SetOrganizeNote(Loc.OrganizeRunRunning + " · " + Loc.OrganizeRunCovered(0, targets.Count));

        int done = 0, failed = 0;
        try
        {
            // 1) 本地 + 未知有限探索（不花任何请求）。
            //    覆盖范围是**传进来的整批**（一、二级全量），与界面显示上限无关；
            //    每 32 个让出一次时间片，界面不会被这一遍拖住。
            int i = 0;
            foreach (var t in targets)
            {
                if (ct.IsCancellationRequested) break;
                if (Stale(myGen, myData)) break;
                if (!t.HasConclusion) LocalRecognize(t);
                if (!t.HasConclusion && !auto) ProbeUnknownLocally(t);
                done++;
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
                int waiting = targets.Count(t => t.IsPending);
                if (waiting > 0)
                {
                    ShowOrganizeState(OrganizeStateKind.NoModel);
                    SetOrganizeNote(Loc.OrganizeNoModelHonest(waiting));
                }
                AppLog.Info("Organize", $"op=identify local-only targets={targets.Count} pending={waiting} auto={auto}");
            }
            else
            {
                foreach (var t in targets)
                {
                    if (ct.IsCancellationRequested || Stale(myGen, myData)) break;
                    if (t.HasConclusion) continue;
                    if (_folderPurpose.AiRequestsUsed >= budget) break;

                    t.SetState(PurposeState.Running);
                    UpdateOrganizeWorkBar();
                    var res = await RecognizeWithAsync(t.Dir, allowAi: true, ct).ConfigureAwait(true);
                    if (Stale(myGen, myData)) break;     // 旧请求绝不覆盖新扫描
                    // 用户在这期间纠正过 ⇒ 用户结论优先
                    var user = _folderPurpose.TryGetUserCorrection(t.Id);
                    t.Apply(user ?? res);
                    // 注意：Apply 已经把「失败/超时」落成 Failed；这里**只对真正失败**补一次，
                    // 「判不出来」是未知，不能算失败（否则页头会把未知说成故障）。
                    if (!t.HasConclusion && res.Failed) t.SetState(PurposeState.Failed);
                    done = Math.Min(targets.Count, done + 1);
                    SetOrganizeProgress(done, targets.Count, _folderPurpose.AiRequestsUsed, budget);
                }
            }

            if (ct.IsCancellationRequested)
            {
                // 取消：排队项回到「未识别」，不留假进度
                foreach (var t in targets)
                    if (!t.HasConclusion && t.State is PurposeState.Queued or PurposeState.Running) t.ClearState();
                SetOrganizeNote(Loc.OrganizeRunCanceled + " · " + CountsLine(targets));
            }
            else if (Stale(myGen, myData))
            {
                // 结果已经过期：什么都不写回，列表马上会被重建
                return;
            }
            else
            {
                int aiCount = targets.Count(x => x.Source == PurposeSource.Ai);
                int localCount = targets.Count(x => x.Source == PurposeSource.Local);
                failed = targets.Count(x => x.State == PurposeState.Failed);
                int named = targets.Count(x => x.HasConclusion);
                int unknown = targets.Count - named;
                int notCovered = Math.Max(0, _organizeUnlistedTotal);

                // 真实终态：只有「所有目标都跑到、且没有未覆盖的条目」才算完成
                bool allReached = done >= targets.Count;
                bool complete = allReached && notCovered == 0 && failed == 0;
                string head = ct.IsCancellationRequested ? Loc.OrganizeRunCanceled
                    : complete ? Loc.OrganizeRunDone
                    : Loc.OrganizeRunIncomplete;

                // 顶栏：一句短结论 + 计数；请求预算与「没跑到多少」放 Tooltip，不和文件夹数混一行
                string summary = head + " · " + Loc.OrganizeRunCounts(named, aiCount, unknown, failed)
                    + " · " + Loc.OrganizeRunCovered(named, targets.Count);
                if (notCovered > 0) summary += " · " + Loc.OrganizeRunNotCovered(notCovered);
                SetOrganizeNote(summary);

                var tip = new List<string>
                {
                    Loc.OrganizeRunRequests(_folderPurpose.AiRequestsUsed, budget),
                    Loc.OrganizeProgress(done, targets.Count, _folderPurpose.AiRequestsUsed, budget),
                };
                if (failed > 0) tip.Add(Loc.OrganizePartFailed(failed));
                if (_folderPurpose.AiRequestsUsed >= budget && unknown > 0) tip.Add(Loc.OrganizeBudgetLeft(unknown));
                if (notCovered > 0) tip.Add(Loc.OrganizeUnlistedTotal(notCovered));
                if (OrganizeCounts != null) OrganizeCounts.ToolTip = string.Join("\n", tip);

                // 终态只报「完成 / 未完成 / 失败」，绝不用「已处理」把待确认说成成功
                if (named == 0 && failed > 0 && targets.Count > 0)
                    ShowOrganizeState(OrganizeStateKind.Failed);
                else if (complete && _organizeAll.All(x => !x.IsPending))
                    ShowOrganizeState(OrganizeStateKind.AllDone);
                else
                    ShowOrganizeState(OrganizeStateKind.None);

                AppLog.Info("Organize", $"op=identify-done auto={auto} targets={targets.Count} "
                    + $"reached={done} named={named} unknown={unknown} failed={failed} "
                    + $"aiUsed={_folderPurpose.AiRequestsUsed}/{budget} ai={aiCount} local={localCount} "
                    + $"notCovered={notCovered} complete={complete}");
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var t in targets) if (!t.HasConclusion) t.ClearState();
            SetOrganizeNote(Loc.OrganizeRunCanceled);
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
            // 那会让人以为还在跑。清掉流程态，落回「未识别 / 待确认」；
            // **失败/取消的状态保留**，不被这里抹掉。
            foreach (var t in targets)
                if (!t.HasConclusion && t.State is PurposeState.Queued or PurposeState.Running)
                    t.ClearState();
            OrganizeProgressPanel.Visibility = Visibility.Collapsed;
            OrganizeStopBtn.Visibility = Visibility.Collapsed;
            OrganizeScopeBar.Visibility = Visibility.Collapsed;
            RefreshOrganizeRows();
            UpdateOrganizeWorkBar();
        }
    }

    /// <summary>取消时的计数短句（不写「完成」）。</summary>
    private static string CountsLine(List<OrganizeNode> targets)
    {
        int named = targets.Count(x => x.HasConclusion);
        int failed = targets.Count(x => x.State == PurposeState.Failed);
        int unknown = targets.Count - named;
        int ai = targets.Count(x => x.Source == PurposeSource.Ai);
        return Loc.OrganizeRunCounts(named, ai, unknown, failed);
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
        // 收纳/混合走共享规则；**未知目录**只有用户点名识别时多看一层
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
        if (n != null) SetOrganizeCurrent(n);
        if (OrgCtxOpen != null) OrgCtxOpen.IsEnabled = n != null;
        if (OrgCtxCopy != null) OrgCtxCopy.IsEnabled = n != null;
        if (OrgCtxExpand != null)
        {
            OrgCtxExpand.IsEnabled = n?.CanExpand == true;
            OrgCtxExpand.Header = n?.IsExpanded == true ? Loc.OrganizeCollapse : Loc.OrganizeExpand;
        }
        // 「识别当前文件夹」只在还能往下看的对象上出现
        if (OrgCtxIdentifyCurrent != null)
            OrgCtxIdentifyCurrent.IsEnabled = n is { CanExpand: true };
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
        AppLog.Info("Organize", $"op=correct dir={node.Name} level={node.Level} value={picked}");
    }

    private void OrganizeOpen_Click(object sender, RoutedEventArgs e)
    {
        var node = _organizeMenuNode
            ?? (sender as FrameworkElement)?.Tag as OrganizeNode
            ?? (sender as FrameworkElement)?.DataContext as OrganizeNode;
        if (node != null) { SetOrganizeCurrent(node); RevealOrganize(node); }
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
