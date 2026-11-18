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
/// 安全边界：这里**没有**任何删除代码 —— 删除一律走底部执行栏与后端
/// MainWindow.FolderDelete.cs 合同（预览 → 明确确认 → 回收站）。勾选只写在
/// 行内复选框上（<see cref="OrganizeNode.IsChecked"/>）。识别不改 Risk / CanDelete / Selected，
/// 也不自动选择、不移动、不归档真实文件；「在资源管理器中打开」只打开目录，不执行任何程序。
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

    /// <summary>
    /// 「待确认筛选」：只看还没认出来的（避免满屏重复的「未识别」）。
    ///
    /// **页面上已经没有这个入口了**（「未识别」胶囊按钮已移除），所以它永远保持 false；
    /// 视图筛选本身（<see cref="RefreshOrganizeRows"/> 里的祖先+命中集合）留着，
    /// 既不影响任何展开状态，也是这套「筛选只是视图」逻辑的回归检查对象。
    /// </summary>
    private bool _organizePendingOnly;

    /// <summary>重建代次：展开/重扫会让旧请求的回写作废，绝不覆盖新结果。</summary>
    private int _organizeGeneration;

    /// <summary>整理列表是按哪一代扫描建的；每代只建一次，避免重复铺开。</summary>
    private int _organizeBuiltForScan = -1;

    /// <summary>一次性的短提示（打开失败、纠正完成之类），跟着统计行显示。</summary>
    private string _organizeNote = "";

    /// <summary>右键目标。</summary>
    private OrganizeNode? _organizeMenuNode;

    /// <summary>三色筛选（null = 全部；只过滤视图，不改展开状态）。</summary>
    private FolderColorTone? _organizeColorFilter;

    /// <summary>最近一次从右侧 AI 报告跳转高亮的目录行。</summary>
    private OrganizeNode? _lastJumpNode;

    /// <summary>按规范化完整路径索引的 AI 报告标色与摘要，供延迟材料化的节点自动同步。</summary>
    private readonly Dictionary<string, (FolderColorTone Tone, string Badge, string Summary, string Brief)> _aiReportAnnotations
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>「当前文件夹」：三级手动识别的目标（展开/点选都会更新它）。</summary>

    /// <summary>自动识别是否已经跑过一次（同一次扫描不重复自动跑）。</summary>

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
        // 「操作」列已整体移除：每行两个图标在两千多行的列表里只是噪音。
        // 「在资源管理器中打开」右键菜单里本来就有。
        // 页头**没有标题**：Tab 上写的就是「按文件夹删除」，这里不再重复一遍。
        // 右键菜单只剩两个只读动作：打开目录 / 复制路径。
        // 「纠正用途」与「展开子文件夹」已移除 —— 展开箭头就在行里，纠正不再是这一页的能力。
        OrgCtxOpen.Header = Loc.OrganizeOpen;
        OrgCtxCopy.Header = Loc.OrganizeCopyPath;
        ApplyOrganizeColumnPriority(OrganizeContentWidth());
        UpdateOrganizeHeader();
        UpdateColorFilterButtons();
        // 后端 FolderDelete 合同（MainWindow.FolderDelete.cs）在这里刷新底部执行栏
        RefreshFolderDeleteBar();
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
        if (OrganizeGrid == null || ColOrgSize == null) return;
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

        // **真正换扫描**才把用途缓存与请求计数归零。
        // 以前这活在 InvalidateItemAiAfterScan 里，而它每次分层重建都会跑，
        // 于是整理页这一遍的缓存被清掉重问、请求计数被归零（预算形同失效）。
        // 用户纠正单独存放，重扫不动它。
        try { _folderPurpose.ResetForScan(); } catch (Exception ex) { AppLog.Record("Organize", ex, "reset purpose for scan"); }

        _organizeRows.Clear();
        _organizeAll.Clear();
        _organizeByDir.Clear();
        _organizeRoots.Clear();
        _organizeNote = "";
        _organizePendingOnly = false;
        _organizeColorFilter = null;
        _lastJumpNode = null;
        _aiReportAnnotations.Clear();
        _organizeUnlistedTotal = 0;

        if (_root == null)
        {
            ShowOrganizeState(OrganizeStateKind.NoScan);
            UpdateOrganizeHeader();
            RefreshDockedReportPlaceholder();
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
                RefreshDockedReportPlaceholder();
                return;
            }

            // 后台把一、二级对象建出来（不展开，首屏只见根的一级）
            AutoMaterializeAll();

            if (rs.Skipped > 0) _organizeNote = Loc.OrganizeSkippedNoAccess(rs.Skipped);
            ShowOrganizeState(OrganizeStateKind.None);
            RefreshOrganizeRows();
            RefreshDockedReportPlaceholder();
            AppLog.Info("Organize", $"op=build roots={_organizeRoots.Count} objects={_organizeAll.Count} "
                + $"rows={_organizeRows.Count} entries={_organizeEntryPoints.Count} skipped={rs.Skipped} "
                + $"levels=1-{OrganizeLevelPolicy.AutoChildLevel}");
        }
        catch (Exception ex)
        {
            AppLog.Record("Organize", ex, "organize build");
            ShowOrganizeState(OrganizeStateKind.Failed);
            UpdateOrganizeHeader();
            return;
        }

        // 扫描完成即自动识别一、二级：本地这一遍就地完成，AI 在后台补
    }

    private OrganizeNode CreateOrganizeNode(FileEntry dir, int depth, OrganizeLevelPolicy policy)
    {
        if (_organizeByDir.TryGetValue(dir, out var existing)) return existing;

        string rel = RelativeOf(dir);
        bool isEntry = FolderOrganize.IsEntryPoint(dir.FullPath, _organizeEntryPoints);
        var node = new OrganizeNode(dir, new FolderId(dir.FullPath, NodeGeneration), depth, rel)
        {
            IsSystemEntry = isEntry,
            IsPlatformContainer = FolderOrganize.KeepsObjectEntries(dir),
        };
        node.SetLevel(policy.Level);
        node.SetChildDirCount(FolderOrganize.DirectChildDirs(dir, 0).Total);
        _organizeByDir[dir] = node;
        _organizeAll.Add(node);
        LocalRecognize(node);
        if (_aiReportAnnotations.TryGetValue(CleanListSnapshot.NormPath(dir.FullPath), out var ann))
            node.ApplyAiAnnotation(ann.Tone, ann.Badge, ann.Summary, ann.Brief);
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
        var local = FolderPurposeRules.RecognizeLocally(node.Dir, sum, _organizeEntryPoints, _folderPurpose.Evidence);
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

    /// <summary>
    /// 一次材料化尝试的结果。**必须有返回值**：点了箭头却什么都没有时，
    /// 界面要说清是"到上限了 / 没有子文件夹 / 子项已经在首屏那一级"，
    /// 不能给一个没有反应的空点击，也不能假装展开了。
    /// </summary>
    private enum OrganizeMaterializeResult
    {
        /// <summary>子项已经在列表里了（新材料化出来的，或之前就材料化过的）。</summary>
        Loaded,
        /// <summary>这个文件夹没有子文件夹。</summary>
        NoChildren,
        /// <summary>子文件夹已经作为顶层对象列在首屏那一级了，这一层没有新东西可展开。</summary>
        AlreadyElsewhere,
        /// <summary>对象数量到上限了，这次没法材料化。</summary>
        BudgetReached,
    }

    private void TryAutoMaterialize(OrganizeNode node)
    {
        if (FolderOrganize.ObjectBudgetReached(_organizeAll.Count)) return;
        if (!FolderOrganize.ShouldAutoMaterializeChildren(
                new OrganizeLevelPolicy(node.Level, node.Size, node.FileCount), node.Dir, node.Kind,
                _organizeEntryPoints)) return;
        // 后台材料化**绝不展开**（autoExpand 默认 false）：首屏只显示根的一级
        Materialize(node, FolderOrganize.ChildBudget, FolderOrganize.AutoMaterializeBudget(node.Depth == 0));
    }

    /// <summary>
    /// 把一个目录的直接子对象材料化出来（有预算、有去重、有上限）。
    /// 子对象的层级由父对象 + 子对象是不是系统入口一起决定（入口的直接子目录与入口同级）。
    ///
    /// <paramref name="autoExpand"/> 为 true 时才展开 —— **只有用户点箭头**会传 true，
    /// 而且只在真的建出了子项时展开（否则会出现"展开了个空的"这种假成功）。
    /// </summary>
    private OrganizeMaterializeResult Materialize(
        OrganizeNode node, int childBudget, int? materializeBudget = null, bool autoExpand = false)
    {
        if (node.ChildrenLoaded)
        {
            if (autoExpand) node.Expand();
            return OrganizeMaterializeResult.Loaded;
        }
        if (FolderOrganize.ObjectBudgetReached(_organizeAll.Count))
            return OrganizeMaterializeResult.BudgetReached;

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
        node.SetChildren(kids, set.Truncated || unlisted > 0, set.Total, unlisted,
            autoExpand && kids.Count > 0);
        RegisterUnlisted(node, unlisted);

        if (kids.Count > 0) return OrganizeMaterializeResult.Loaded;
        if (set.Total == 0) return OrganizeMaterializeResult.NoChildren;
        return OrganizeMaterializeResult.AlreadyElsewhere;
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

    /// <summary>
    /// 「待确认筛选」的可见集合：命中的行 **加上它们的祖先**（保留上下文，不出现孤儿行）。
    /// null = 筛选没开。**它只是视图** —— 绝不改 <see cref="OrganizeNode.IsExpanded"/>，
    /// 所以关掉筛选就回到打开前的样子（用户本来展开的仍然展开）。
    /// </summary>
    private HashSet<OrganizeNode>? _organizeFilterSet;

    private void RefreshOrganizeRows()
    {
        if (OrganizeGrid == null) return;
        _organizeFilterSet = null;
        if (_organizePendingOnly || _organizeColorFilter.HasValue)
        {
            var set = new HashSet<OrganizeNode>();
            bool Matches(OrganizeNode n)
            {
                if (_organizePendingOnly && !n.IsPending) return false;
                if (_organizeColorFilter is { } tone && n.ColorTone != tone) return false;
                return true;
            }
            // 只遍历已经材料化出来的层：筛选不会去碰磁盘、也不会触发识别
            bool Mark(OrganizeNode n)
            {
                bool keep = Matches(n);
                if (n.ChildrenLoaded)
                    foreach (var c in n.Children)
                        if (Mark(c)) keep = true;
                if (keep) set.Add(n);
                return keep;
            }
            foreach (var r in _organizeRoots) Mark(r);
            _organizeFilterSet = set;
        }

        _organizeRows.Clear();
        int n2 = 0;
        foreach (var r in _organizeRoots)
        {
            AddVisibleRows(r, ref n2);
            if (FolderOrganize.RowBudgetReached(n2)) break;
        }
        MarkCoveredRows();
        UpdateOrganizeHeader();
        // 行集合变了：底部执行栏的「已选 / 可删除 / 状态」跟着重算（后端合同提供）
        RefreshFolderDeleteBar();
    }

    /// <summary>
    /// 标记「列表里上面那一项已经把它整个包住」的行。
    ///
    /// 用户看到 `Users`(179 G) 和 `Users\32098`(179 G) 会以为重复了 —— 它们是父子，
    /// 而这个盘上 `32098` 是 `Users` 里唯一的东西，所以容量一样。**勾了两个不会算两遍**
    /// （<see cref="FolderSelectionService"/> 按父目录覆盖子目录去重），但列表得说清楚。
    ///
    /// 算法：把这一屏的路径收进集合，再让每一行顺着自己的父目录往上查 —— O(行数×深度)，
    /// 不做两两比较（2645 行两两比是 700 万次）。
    /// </summary>
    void MarkCoveredRows()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in _organizeRows) paths.Add(n.FullPath);
        foreach (var n in _organizeRows)
            n.IsCoveredByAncestor = OrganizeBucket.HasAncestorIn(n.FullPath, paths);
    }

    private void AddVisibleRows(OrganizeNode node, ref int count)
    {
        if (count >= FolderOrganize.MaxRows) return;
        if (_organizeFilterSet != null)
        {
            // 筛选模式：祖先与命中项都显示，顺序即树序 => 不会出现孤儿行
            if (!_organizeFilterSet.Contains(node)) return;
            _organizeRows.Add(node);
            count++;
            foreach (var c in node.Children) AddVisibleRows(c, ref count);
            return;
        }
        _organizeRows.Add(node);
        count++;
        if (!node.IsExpanded) return;
        foreach (var c in node.Children) AddVisibleRows(c, ref count);
    }

    /// <summary>页头统计：对象数 / 容量 / 已认出 / 待确认 / 失败，外加一次性短提示与三色筛选。</summary>
    private void UpdateOrganizeHeader()
    {
        if (OrganizeSub == null) return;

        if (_root == null)
        {
            OrganizeSub.Text = Loc.OrganizeNoScan;
            OrganizeCounts.Text = "";
            if (OrganizeNoteText != null) OrganizeNoteText.Text = "";
            UpdateColorFilterButtons();
            return;
        }

        // 顶层对象之间现在允许「入口与它的祖先同时出现」，
        // 所以**不能把根的容量相加**（会重复计算）。直接用扫描根的真实总量。
        long bytes = _root.Size;
        // 分档计数：本地认出（含用户确认）/ AI 有结论 / 未知 / 失败。
        // **不把它们混成一句「待确认」** —— 那会让人看不出到底哪一类有多少。
        int local = _organizeAll.Count(x => x.HasConclusion
            && x.Source is PurposeSource.Local or PurposeSource.User);
        int unknown = _organizeAll.Count(x => !x.HasConclusion && x.State != PurposeState.Failed);
        int failed = _organizeAll.Count(x => x.State == PurposeState.Failed);

        OrganizeSub.Text = Loc.OrganizeTotals(_organizeAll.Count, bytes);
        string line = Loc.OrganizeCountsLine(local, unknown, failed);
        if (_organizeUnlistedTotal > 0) line += " · " + Loc.OrganizeUnlistedTotal(_organizeUnlistedTotal);
        if (_organizePendingOnly) line += " · " + Loc.OrganizeFilterActive(_organizeRows.Count, _organizeAll.Count);
        if (_organizeColorFilter.HasValue && _organizeRows.Count == 0) line += " · " + Loc.ColorFilterEmpty;
        if (_organizeNote.Length > 0) line += " · " + _organizeNote;
        OrganizeCounts.Text = line;
        if (OrganizeNoteText != null)
        {
            string shortNote = _organizeNote;
            if (shortNote.Length == 0 && _organizeColorFilter.HasValue && _organizeRows.Count == 0)
                shortNote = Loc.ColorFilterEmpty;
            OrganizeNoteText.Text = shortNote;
        }
        UpdateColorFilterButtons();
    }

    private void ColorFilterAll_Click(object sender, RoutedEventArgs e) => SetColorFilter(null);
    private void ColorFilterGreen_Click(object sender, RoutedEventArgs e) => SetColorFilter(FolderColorTone.Green);
    private void ColorFilterYellow_Click(object sender, RoutedEventArgs e) => SetColorFilter(FolderColorTone.Yellow);
    private void ColorFilterRed_Click(object sender, RoutedEventArgs e) => SetColorFilter(FolderColorTone.Red);

    private void SetColorFilter(FolderColorTone? tone)
    {
        _organizeColorFilter = _organizeColorFilter == tone && tone != null ? null : tone;
        RefreshOrganizeRows();
    }

    private void UpdateColorFilterButtons()
    {
        if (ColorFilterAllBtn == null) return;
        int total = _organizeAll.Count;
        int green = _organizeAll.Count(x => x.ColorTone == FolderColorTone.Green);
        int yellow = _organizeAll.Count(x => x.ColorTone == FolderColorTone.Yellow);
        int red = _organizeAll.Count(x => x.ColorTone == FolderColorTone.Red);

        ColorFilterAllBtn.Content = total > 0 ? $"{Loc.ColorFilterAll} ({total})" : Loc.ColorFilterAll;
        if (ColorFilterGreenText != null)
            ColorFilterGreenText.Text = green > 0 ? $"{Loc.ColorFilterGreen} ({green})" : Loc.ColorFilterGreen;
        if (ColorFilterYellowText != null)
            ColorFilterYellowText.Text = yellow > 0 ? $"{Loc.ColorFilterYellow} ({yellow})" : Loc.ColorFilterYellow;
        if (ColorFilterRedText != null)
            ColorFilterRedText.Text = red > 0 ? $"{Loc.ColorFilterRed} ({red})" : Loc.ColorFilterRed;

        ApplyFilterPillStyle(ColorFilterAllBtn, _organizeColorFilter == null, "SelectBg", "Accent", "Accent");
        ApplyFilterPillStyle(ColorFilterGreenBtn, _organizeColorFilter == FolderColorTone.Green,
            "ToneGreenBg", "ToneGreenAccent", "ToneGreenAccent");
        ApplyFilterPillStyle(ColorFilterYellowBtn, _organizeColorFilter == FolderColorTone.Yellow,
            "ToneYellowBg", "ToneYellowAccent", "ToneYellowAccent");
        ApplyFilterPillStyle(ColorFilterRedBtn, _organizeColorFilter == FolderColorTone.Red,
            "ToneRedBg", "ToneRedAccent", "ToneRedAccent");
    }

    private static void ApplyFilterPillStyle(Button? btn, bool active, string bgKey, string borderKey, string fgKey)
    {
        if (btn == null) return;
        btn.Background = ThemeService.Brush(active ? bgKey : "PanelLeft");
        btn.BorderBrush = ThemeService.Brush(active ? borderKey : "Border");
        btn.Foreground = ThemeService.Brush(active ? fgKey : "TextDim");
        btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
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
            if (_lastAgentReport == null) RefreshDockedReportPlaceholder();
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
        if (_lastAgentReport == null) RefreshDockedReportPlaceholder();
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

    // 说明：按文件夹删除的删除入口（OrganizeSelect_Click / FolderDeletePreview_Click /
    // FolderDeleteCancel_Click）与底部执行栏的 FolderDelete / RefreshFolderDeleteBar()
    // 由后端合同 MainWindow.FolderDelete.cs 提供；这一页只负责在合适的位置调用与绑定。

    /// <summary>
    /// 「只有文件」那一行的文件按钮：就地列出这个文件夹自己的文件。
    /// **纯只读展示** —— 不改展开状态、不触发识别、不碰任何清理字段。
    /// </summary>
    private void OrganizeFiles_Click(object sender, RoutedEventArgs e)
    {
        var node = (sender as FrameworkElement)?.Tag as OrganizeNode
            ?? (sender as FrameworkElement)?.DataContext as OrganizeNode;
        if (node == null) return;
        node.ToggleFiles();
        AppLog.Info("Organize", $"op=files dir={node.Name} open={node.IsFilesOpen} "
            + $"listed={node.VisibleFiles.Count} direct={node.DirectFileCount} kind={node.ContentKind}");
    }

    private void ToggleOrganize(OrganizeNode node)
    {
        if (node.IsExpanded)
        {
            node.Collapse();
        }
        else if (node.ChildrenLoaded)
        {
            // 展开不重复请求：结果还在节点上，直接显示
            node.Expand();
        }
        else
        {
            // 首次展开：先材料化，**成功之后再显式展开**。
            // 材料化与展开是两件事 —— 后台材料化（TryAutoMaterialize）刻意不展开，
            // 所以这里必须自己展开，否则第一次点箭头看起来毫无反应。
            var r = Materialize(node, FolderOrganize.ChildBudget, null, autoExpand: true);
            switch (r)
            {
                case OrganizeMaterializeResult.BudgetReached:
                    SetOrganizeNote(Loc.OrganizeExpandBudgetReached);
                    break;
                case OrganizeMaterializeResult.AlreadyElsewhere:
                    SetOrganizeNote(Loc.OrganizeExpandAlreadyListed);
                    break;
                case OrganizeMaterializeResult.NoChildren:
                    SetOrganizeNote(Loc.OrganizeExpandNoChildren);
                    break;
            }
        }
        RefreshOrganizeRows();
        AppLog.Info("Organize", $"op=toggle dir={node.Name} level={node.Level} "
            + $"expanded={node.IsExpanded} loaded={node.ChildrenLoaded} kids={node.Children.Count} "
            + $"rows={_organizeRows.Count}");
    }

    // ==================== 定位（供 AI 报告里的「去处理」使用） ====================

    private enum OrganizeRevealKind { Located, Partial, NotFound }

    private readonly record struct OrganizeRevealResult(OrganizeRevealKind Kind, OrganizeNode? Node);

    /// <summary>
    /// 把整理列表定位到一个真实目录：展开祖先、选中并滚动到该行。
    /// **只改视图** —— 不写 <see cref="OrganizeNode.IsChecked"/>、不触发识别、不碰任何清理字段；
    /// 目标不在列表里（受保护 / 超出层级 / 对象预算）时退回到最近的上级并如实返回 Partial。
    /// </summary>
    private OrganizeRevealResult RevealOrganizeFolder(FileEntry folder)
    {
        if (folder == null || _organizeRoots.Count == 0 || OrganizeGrid == null)
            return new OrganizeRevealResult(OrganizeRevealKind.NotFound, null);

        // 对象已经建过：直接找回它在树里的链，不需要再材料化一遍
        if (_organizeByDir.TryGetValue(folder, out var known))
        {
            var knownChain = new List<OrganizeNode>();
            foreach (var r in _organizeRoots)
                if (TryFindOrganizeChain(r, known, knownChain)) break;
            if (knownChain.Count > 0)
            {
                ExpandOrganizeAncestors(knownChain);
                return SelectOrganizeRow(knownChain[^1]);
            }
        }

        // 还没建过：从最深的根开始逐级材料化，直到走到目标目录
        OrganizeNode? root = null;
        foreach (var r in _organizeRoots)
        {
            if (!OrganizePathIsUnder(folder.FullPath, r.FullPath)) continue;
            if (root == null || r.FullPath.Length > root.FullPath.Length) root = r;
        }
        if (root == null) return new OrganizeRevealResult(OrganizeRevealKind.NotFound, null);

        var chain = new List<OrganizeNode> { root };
        var current = root;
        for (int guard = 0; guard < 64 && !OrganizePathIsSame(current.FullPath, folder.FullPath); guard++)
        {
            Materialize(current, FolderOrganize.ChildBudget, null, autoExpand: false);
            OrganizeNode? next = null;
            foreach (var c in current.Children)
            {
                if (!OrganizePathIsUnder(folder.FullPath, c.FullPath)) continue;
                next = c;
                break;
            }
            if (next == null)
            {
                // 因为对象预算/去重没有挂在下面：停在最近的上级
                ExpandOrganizeAncestors(chain);
                return SelectOrganizeRow(current) with { Kind = OrganizeRevealKind.Partial };
            }
            chain.Add(next);
            current = next;
        }

        ExpandOrganizeAncestors(chain);
        return SelectOrganizeRow(current);
    }

    private static bool TryFindOrganizeChain(OrganizeNode node, OrganizeNode target, List<OrganizeNode> chain)
    {
        chain.Add(node);
        if (ReferenceEquals(node, target)) return true;
        foreach (var c in node.Children)
            if (TryFindOrganizeChain(c, target, chain)) return true;
        chain.RemoveAt(chain.Count - 1);
        return false;
    }

    private static void ExpandOrganizeAncestors(IReadOnlyList<OrganizeNode> chain)
    {
        for (int i = 0; i < chain.Count - 1; i++) chain[i].Expand();
    }

    private OrganizeRevealResult SelectOrganizeRow(OrganizeNode node)
    {
        if (_organizeColorFilter.HasValue && node.ColorTone != _organizeColorFilter.Value)
            _organizeColorFilter = null;
        RefreshOrganizeRows();
        if (!_organizeRows.Contains(node))
            return new OrganizeRevealResult(OrganizeRevealKind.Partial, node);
        if (_lastJumpNode != null && !ReferenceEquals(_lastJumpNode, node))
            _lastJumpNode.IsJumpHighlighted = false;
        node.IsJumpHighlighted = true;
        _lastJumpNode = node;
        OrganizeGrid.SelectedItem = node;
        OrganizeGrid.ScrollIntoView(node);
        OrganizeGrid.Focus();
        return new OrganizeRevealResult(OrganizeRevealKind.Located, node);
    }

    private void OrganizeGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OrganizeGrid == null || _lastJumpNode == null) return;
        if (!ReferenceEquals(OrganizeGrid.SelectedItem, _lastJumpNode))
        {
            _lastJumpNode.IsJumpHighlighted = false;
            _lastJumpNode = null;
        }
    }

    private static string OrganizeNormalizePath(string? path) => (path ?? "").TrimEnd('\\');

    private static bool OrganizePathIsSame(string? a, string? b) =>
        string.Equals(OrganizeNormalizePath(a), OrganizeNormalizePath(b), StringComparison.OrdinalIgnoreCase);

    private static bool OrganizePathIsUnder(string? path, string? ancestor)
    {
        string p = OrganizeNormalizePath(path);
        string a = OrganizeNormalizePath(ancestor);
        if (p.Length == 0 || a.Length == 0) return false;
        if (string.Equals(p, a, StringComparison.OrdinalIgnoreCase)) return true;
        return p.Length > a.Length && p[a.Length] == '\\'
            && p.StartsWith(a, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>双击一行 = 就地展开/收起这个文件夹（不换视图、不触发识别）。</summary>
    private void OrganizeGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is OrganizeNode n)
        {
            if (!n.IsExpanded) ToggleOrganize(n);
        }
    }

    // ==================== 待确认筛选（页面上已无入口） ====================
    // 「未识别」胶囊按钮已从页头移除：用户看不到、也点不到这个筛选。
    // 视图筛选本身留在 RefreshOrganizeRows 里（祖先 + 命中集合），它只是视图、不改任何展开状态，
    // 也是「筛选不会篡改展开状态 / 不留孤儿行」那一组回归检查的对象。

    private void OrganizeFilter_Click(object sender, RoutedEventArgs e)
    {
        // 筛选**只是视图**：这里一次都不碰 IsExpanded。
        // 以前打开筛选会把所有已材料化的节点全部展开、关掉又不收回，
        // 等于悄悄改掉了用户（和首屏）的展开状态 —— 那是错的。
        _organizePendingOnly = !_organizePendingOnly;
        RefreshOrganizeRows();
        ShowOrganizeState(_organizePendingOnly && _organizeRows.Count == 0
            ? OrganizeStateKind.AllDone
            : OrganizeStateKind.None);
    }

    // ==================== 右键菜单 / 打开 ====================
    // 2.11：右键只剩「在资源管理器中打开」「复制完整路径」两个只读动作。
    // 「展开子文件夹」由行里的展开箭头承担，「纠正用途」不再属于这一页。

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
        if (OrgCtxOpen != null) OrgCtxOpen.IsEnabled = n != null;
        if (OrgCtxCopy != null) OrgCtxCopy.IsEnabled = n != null;
    }

    private void OrganizeOpen_Click(object sender, RoutedEventArgs e)
    {
        var node = _organizeMenuNode
            ?? (sender as FrameworkElement)?.Tag as OrganizeNode
            ?? (sender as FrameworkElement)?.DataContext as OrganizeNode;
        if (node != null) RevealOrganize(node);
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
