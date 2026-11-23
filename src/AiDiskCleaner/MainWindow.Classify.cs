using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AiDiskCleaner.Models;
using AiDiskCleaner.Native;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.Agent;
using Path = System.IO.Path;

namespace AiDiskCleaner;

/// <summary>
/// 「清理中心」全单画面 · 60fps 原位逐个接力排序动画引擎：
/// 彻底零 UI 线程磁盘 I/O、预冻结笔刷缓存、绝不提前沉底系统目录、绝不 Clear() 闪屏：
/// 1) 点击 AI 分析后保持右侧原有容量顺序不动，左侧亮起双层呼吸伸缩流光弧；
/// 2) 每判出一个可整理目录：先原位轻亮分类色条并触发左环同色共振脉冲，再以 520ms 弹性缓动 + 140ms 逐个接力，
///    从原位置（或直接从 Downloads / 迅雷下载父行展开飞出）丝滑滑升到顶部对应色组，沿途目录同步丝滑下让；
/// 3) 飞行中支持像素级连续重定向 FLIP（零跳变零卡顿），所有彩色目录落定后再轻盈浮现「保守保护」分组细栏。
/// </summary>
public partial class MainWindow
{
    public sealed record UnifiedClassifyOutcome(
        bool Ok,
        bool HasViewableContent,
        int ClassifiedCount,
        string Error,
        List<ClassifyItem> Items,
        List<ClassifyCategoryRule> Categories);

    private static readonly ConcurrentDictionary<string, Brush> _opaqueSlideBrushCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<ClassifyItem> _classifyAllItems = new();
    private readonly ObservableCollection<ClassifyItem> _classifyModalItems = new();
    private readonly ObservableCollection<ClassifyRuleDraft> _classifyRuleDrafts = new();
    private readonly FolderClassificationService _classifyService = new();
    private readonly FolderReorganizeService _reorganizeService = new();
    private readonly List<FileEntry> _classifyPinnedDirs = new();

    private readonly Dictionary<string, ClassifyItem> _classifyItemsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrganizeNode> _classifyNodesByItemId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrganizeNode> _classifyParentAnchorByItemId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrganizeNode> _classifyGroupHeaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _classifyCollapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _classifySettledIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OrganizeNode> _classifyOriginalOrder = new();
    private readonly List<OrganizeNode> _classifyExtraKeepNodes = new();
    private readonly Dictionary<string, List<OrganizeNode>> _classifyExistingBucketNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<OrganizeNode, long> _activeFlightEndTick = new(ReferenceEqualityComparer.Instance);

    private List<ClassifyCategoryRule> _classifyCachedCats = new();
    private Dictionary<string, string> _classifyCachedResolvedTargets = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _classifyCts;
    private CancellationTokenSource? _classifyExecCts;
    private bool _classifyBusy;
    private bool _classifyInitialized;
    private bool _classifyTargetDriveSyncing;
    private bool _classifyStreamingInProgress;
    private bool _classifySortActive;
    private bool _classifyAnimInFlight;
    private bool _classifyHasAiPlan;
    private bool _classifyKeepHeaderVisible;
    private int _sortFlyZIndex = 100;
    private int _classifyBuiltForScan = -1;
    private string _classifyBuiltForDrive = "";
    private string _classifyBannerMsg = "";
    private bool _classifyBannerError;
    private Dictionary<string, string>? _classifyFastInstalled;

    private sealed record ClassifyDriveOption(string DriveRoot, string Label, bool IsSameDrive, long FreeBytes);

    private sealed class ClassifyRuleDraft : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _enabled = true;
        private string _name = "";
        private string _folderName = "";
        private string _description = "";

        public string Id { get; init; } = "";
        public List<string> Aliases { get; init; } = new();

        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled))); } }
        }

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); } }
        }

        public string FolderName
        {
            get => _folderName;
            set { if (_folderName != value) { _folderName = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FolderName))); } }
        }

        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description))); } }
        }

        public static ClassifyRuleDraft FromRule(ClassifyCategoryRule r) => new()
        {
            Id = r.Id,
            Enabled = r.Enabled,
            Name = r.Name,
            FolderName = r.FolderName,
            Description = r.Description,
            Aliases = new List<string>(r.Aliases),
        };

        public ClassifyCategoryRule ToRule() => new()
        {
            Id = string.IsNullOrWhiteSpace(Id) ? "cat-" + Guid.NewGuid().ToString("N")[..6] : Id,
            Enabled = Enabled,
            Name = string.IsNullOrWhiteSpace(Name) ? "未命名分类" : Name.Trim(),
            FolderName = string.IsNullOrWhiteSpace(FolderName) ? "Sorted" : FolderName.Trim().Trim('\\', '/'),
            Description = (Description ?? "").Trim(),
            Aliases = new List<string>(Aliases),
        };
    }

    private void EnsureClassifyBindingsInitialized()
    {
        if (_classifyInitialized) return;
        _classifyInitialized = true;

        if (ClassifyModalItems != null) ClassifyModalItems.ItemsSource = _classifyModalItems;
        if (ClassifyRulesList != null) ClassifyRulesList.ItemsSource = _classifyRuleDrafts;

        if (ClassifyIncludeDownloadsChk != null)
            ClassifyIncludeDownloadsChk.IsChecked = App.Settings.ClassifyIncludeDownloadsChildren;
        if (ClassifyUpdateShortcutsChk != null)
            ClassifyUpdateShortcutsChk.IsChecked = App.Settings.ClassifyUpdateShortcuts;

        LoadClassifyRuleDrafts();
    }

    private void RefreshClassifyCategoryMemo(FileEntry? root = null)
    {
        _classifyCachedCats = App.Settings.EffectiveClassifyCategories();
        var r = root ?? _root;
        _classifyCachedResolvedTargets = r != null
            ? FolderClassificationService.ResolveCategoryTargetNames(r, _classifyCachedCats)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void OnScanCompletedRefreshClassify()
    {
        EnsureClassifyBindingsInitialized();
        ResetClassifyStageOnScanChange();
    }

    private void OnAppsLoadedRefreshClassify()
    {
        if (!_classifyInitialized) return;
        if (!_classifyHasAiPlan || _classifyBuiltForScan != _scanGeneration || _classifyAllItems.Count == 0)
        {
            RefreshOrganizeClassifyShortcutBar();
            return;
        }

        var context = FolderClassificationService.BuildContext(
            _folderPurpose.Evidence, _apps, App.Settings.ClassifyIgnoredPaths);
        if (context.InstalledLocations.Count == 0) return;

        var clamped = new List<ClassifyItem>();
        foreach (var item in _classifyAllItems)
        {
            if (item.Executed || item.Tier == ClassifySafetyTier.BlockedConservative) continue;
            string normSrc = CleanListSnapshot.NormPath(item.SourcePath);
            string? hitApp = context.InstalledLocations.TryGetValue(normSrc, out var exactApp)
                ? exactApp
                : null;
            if (hitApp == null)
            {
                string prefix = normSrc.TrimEnd('\\') + "\\";
                foreach (var kv in context.InstalledLocations)
                {
                    if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        hitApp = kv.Value;
                        break;
                    }
                }
            }

            if (hitApp != null)
            {
                bool canJunction = !FolderClassificationService.IsHardNoJunctionPath(item.SourcePath);
                item.InstalledProductHint = hitApp;
                item.SupportsJunctionMigration = canJunction;
                item.Tier = ClassifySafetyTier.BlockedConservative;
                item.SafetyNote = canJunction
                    ? $"命中已安装程序「{hitApp}」；直接移动会破坏注册表，可点「启用软链搬家」以隐藏 NTFS Junction 无损迁移"
                    : $"已安装程序：{hitApp}";
                if (!item.UseJunction)
                {
                    item.CategoryId = ClassifyCategoryRule.KeepId;
                    item.IsChecked = false;
                }
                clamped.Add(item);
            }
        }

        if (clamped.Count > 0 && _classifySortActive)
            _ = ApplyBatchSortWaveAsync(clamped, animate: false, CancellationToken.None);
        RefreshOrganizeClassifyShortcutBar();
    }

    private void OnBeforeStandardOrganizeRefresh()
    {
        if (_classifySortActive)
            ExitClassifySortMode(clearState: false);
    }

    private void RefreshClassifySortColorsForTheme()
    {
        if (!_classifySortActive) return;
        foreach (var item in _classifyAllItems)
        {
            if (_classifyNodesByItemId.TryGetValue(item.Id, out var node))
                ApplyClassifyItemStyleToNode(node, item, pendingProbe: !_classifySettledIds.Contains(item.Id));
        }
        foreach (var kv in _classifyExistingBucketNodes)
        {
            foreach (var bNode in kv.Value)
                StyleExistingBucketNode(bNode, kv.Key);
        }
        foreach (var kNode in _classifyExtraKeepNodes)
            StyleExtraKeepNode(kNode);
        RefreshAllSortHeaderNodes();
    }

    private static void StyleExtraKeepNode(OrganizeNode kNode)
    {
        bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;
        var keepTheme = ClassifyCategoryPalette.KeepTheme;
        string reason = kNode.IsSystemEntry
            ? "系统入口 · 保持原位"
            : (!string.IsNullOrWhiteSpace(kNode.PurposeText) ? kNode.PurposeText : "原位保护目录");
        kNode.ApplyClassifyVisualState(
            "", ClassifyCategoryRule.KeepId,
            keepTheme.AccentHex, keepTheme.RowWashHex(isDark), keepTheme.PillWashHex(isDark),
            "保守保护", reason, "🔒", false, false);
    }

    private void StyleExistingBucketNode(OrganizeNode node, string catId)
    {
        bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;
        var cats = _classifyCachedCats.Count > 0 ? _classifyCachedCats : App.Settings.EffectiveClassifyCategories();
        int idx = cats.FindIndex(c => string.Equals(c.Id, catId, StringComparison.OrdinalIgnoreCase));
        var theme = ClassifyCategoryPalette.For(catId, Math.Max(0, idx));
        node.ApplyClassifyVisualState(
            "", catId,
            theme.AccentHex, theme.RowWashHex(isDark), theme.PillWashHex(isDark),
            "本类归档目录", "当前分类落地目录 · 已在原位", "", false, false);
    }

    private static void EnableAutoJunctionForEligibleCandidates(
        IReadOnlyList<ClassifyItem> items, IReadOnlyList<ClassifyCategoryRule> cats)
    {
        foreach (var item in items)
        {
            if (item.Executed || item.Tier != ClassifySafetyTier.BlockedConservative || !item.SupportsJunctionMigration)
                continue;
            if (!JunctionNative.IsNtfsVolume(item.DriveRoot))
                continue;

            string suggested = FolderClassificationService.SuggestJunctionCategoryId(item, cats);
            if (string.Equals(suggested, ClassifyCategoryRule.KeepId, StringComparison.OrdinalIgnoreCase))
                continue;

            item.UseJunction = true;
            FolderClassificationService.ApplyChoice(item, suggested, item.Choices);
            item.FallbackCategoryId = suggested;
            item.IsChecked = false;
        }
    }

    private void LoadClassifyRuleDrafts()
    {
        _classifyRuleDrafts.Clear();
        foreach (var r in App.Settings.EffectiveClassifyCategories())
            _classifyRuleDrafts.Add(ClassifyRuleDraft.FromRule(r));
    }

    private string CurrentClassifyDriveRoot()
    {
        if (_root != null && !string.IsNullOrWhiteSpace(_root.FullPath))
            return Path.GetPathRoot(_root.FullPath) ?? _root.FullPath;
        return DriveBox?.SelectedItem?.ToString() ?? "";
    }

    // ==================== 流式 AI 分类与 60fps 原位逐个接力排序引擎 ====================

    private async Task<UnifiedClassifyOutcome> RunUnifiedClassifyStreamingAsync(
        IChatModel model, FileEntry root, CancellationToken ct)
    {
        EnsureClassifyBindingsInitialized();
        CancelStartupChoreographyAndFlush();
        CancelQuietly(_classifyCts);
        var sortCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _classifyCts = sortCts;
        _classifyStreamingInProgress = true;

        try
        {
            RefreshClassifyCategoryMemo(root);
            var cats = _classifyCachedCats;
            bool includeDl = ClassifyIncludeDownloadsChk?.IsChecked ?? App.Settings.ClassifyIncludeDownloadsChildren;
            bool fullPaths = App.Settings.AiSendFullPaths;

            var context = FolderClassificationService.BuildContext(
                _folderPurpose.Evidence, _apps, App.Settings.ClassifyIgnoredPaths);
            if (_apps.Count == 0)
            {
                _classifyFastInstalled ??= await Task.Run(FolderClassificationService.ReadInstalledLocationsFast, sortCts.Token);
                foreach (var kv in _classifyFastInstalled)
                    context.InstalledLocations.TryAdd(kv.Key, kv.Value);
            }

            var built = await Task.Run(() =>
                FolderClassificationService.BuildCandidates(root, cats, context, includeDl, _classifyPinnedDirs), sortCts.Token);

            // 为非系统/反作弊红线的软件与游戏平台库自动解锁「NTFS 隐藏软链兜底」预归类（不自动勾选）
            EnableAutoJunctionForEligibleCandidates(built, cats);

            // 后台线程完成磁盘存在性预检，零阻塞 UI
            await FolderReorganizeService.PreflightAsync(built, sortCts.Token);
            bool hasContent = built.Any(x => !x.IsBlocked);

            ReplaceClassifyItems(built);
            _classifyHasAiPlan = true;
            _classifyBuiltForScan = _scanGeneration;
            _classifyBuiltForDrive = CurrentClassifyDriveRoot();

            if (built.Count == 0)
                return new UnifiedClassifyOutcome(true, false, 0, "", built, cats);

            // 保持原大小排序列表纹丝不动，绝不提前把系统目录沉底，让每个彩色目录拥有真实可见的飞升轨迹
            _classifyCollapsedGroups.Clear();
            PrepareInPlaceSortStage(built);
            SetClassifyBanner("");
            RefreshOrganizeClassifyShortcutBar(isSortingNow: true);

            // Wave 0：仅对本地高确定性命中的「彩色可整理目录（含已开启软链的游戏平台库）」立刻逐个接力起飞
            var wave0Movables = built
                .Where(x => !x.IsKeepInPlace && !x.IsBlocked)
                .OrderByDescending(x => x.SizeBytes)
                .ToList();
            if (wave0Movables.Count > 0)
            {
                await RelayGlideMovableItemsAsync(
                    wave0Movables, animate: CanAnimate, relayMs: 360, glideMs: 1200, sortCts.Token);
            }

            // Waves 1..N：AI 每批 8 个目录流式返回后，后台异步预检 + 彩色目录逐个接力飞升（下一批请求并行在飞）
            var aiRes = await _classifyService.ClassifyWithAiAsync(
                model,
                built,
                cats,
                fullPaths,
                progress: new Progress<ClassifyProgress>(p =>
                {
                    if (OrganizeClassifyBarText != null && p.Phase == "calling")
                    {
                        OrganizeClassifyBarText.Text =
                            $"正在深度解读目录并逐个按色飞升（第 {Math.Min(p.Completed + 1, p.Total)}/{p.Total} 批）…";
                    }
                }),
                ct: sortCts.Token,
                onBatchReady: async slice =>
                {
                    await FolderReorganizeService.PreflightAsync(built, sortCts.Token);

                    // 刷新本批已在 Wave 0 落定的项的 AI 理由或重排位置
                    foreach (var settled in slice.Where(x => _classifySettledIds.Contains(x.Id)))
                    {
                        if (_classifyNodesByItemId.TryGetValue(settled.Id, out var sNode))
                            ApplyClassifyItemStyleToNode(sNode, settled);
                    }

                    var batchMovables = slice
                        .Where(x => !x.IsKeepInPlace
                                    && !x.IsBlocked
                                    && !_classifySettledIds.Contains(x.Id))
                        .ToList();
                    var batchKeeps = slice
                        .Where(x => (x.IsKeepInPlace || x.IsBlocked)
                                    && !_classifySettledIds.Contains(x.Id))
                        .ToList();

                    // 判为保留原位的行静静在原地更新状态，绝不打乱下方队列
                    foreach (var keepItem in batchKeeps)
                    {
                        _classifySettledIds.Add(keepItem.Id);
                        if (_classifyNodesByItemId.TryGetValue(keepItem.Id, out var keepNode)
                            && _organizeRows.Contains(keepNode))
                        {
                            ApplyClassifyItemStyleToNode(keepNode, keepItem);
                        }
                    }

                    if (batchMovables.Count > 0)
                    {
                        await RelayGlideMovableItemsAsync(
                            batchMovables, animate: CanAnimate, relayMs: 360, glideMs: 1200, sortCts.Token);
                    }
                    RefreshOrganizeClassifyShortcutBar(isSortingNow: true);
                });

            // 补齐任何尚未起飞的已归类可整理目录，确保 100% 飞入对应色组
            var remainingMovables = built
                .Where(x => !x.IsKeepInPlace && !x.IsBlocked && !_classifySettledIds.Contains(x.Id))
                .ToList();
            if (remainingMovables.Count > 0)
            {
                await RelayGlideMovableItemsAsync(
                    remainingMovables, animate: CanAnimate, relayMs: 260, glideMs: 1100, sortCts.Token);
            }

            // 等最后一个在空中的彩色目录丝滑落定，将盘上已有归档目录归入对应色组并浮现「系统核心与原位保护」细分隔栏
            await WaitForActiveFlightsAsync(sortCts.Token);
            await RevealKeepSectionHeaderSmoothAsync(animate: CanAnimate, sortCts.Token);

            if (!aiRes.Ok && !string.IsNullOrWhiteSpace(aiRes.Error))
                SetClassifyBanner("AI 目录分类部分受限（本地安全识别已就绪）：" + aiRes.Error, isError: true);

            RefreshClassifyModalItems();
            RefreshOrganizeClassifyShortcutBar(isSortingNow: false);

            return new UnifiedClassifyOutcome(aiRes.Ok, hasContent, aiRes.ClassifiedCount, aiRes.Error, built, cats);
        }
        catch (OperationCanceledException)
        {
            return new UnifiedClassifyOutcome(false, false, 0, "已取消", new(), new());
        }
        catch (Exception ex)
        {
            AppLog.Record("Classify", ex, "streaming classify");
            return new UnifiedClassifyOutcome(false, false, 0, ex.Message, new(), new());
        }
        finally
        {
            _classifyStreamingInProgress = false;
        }
    }

    /// <summary>供离线回归测试或回放直接展开同表彩色排序状态。</summary>
    private async Task PresentClassifySortPreviewAsync(
        UnifiedClassifyOutcome outcome, bool animateSteps, CancellationToken ct = default)
    {
        EnsureClassifyBindingsInitialized();
        RefreshClassifyCategoryMemo();
        ReplaceClassifyItems(outcome.Items);
        _classifyHasAiPlan = true;
        _classifyBuiltForScan = _scanGeneration;
        _classifyBuiltForDrive = CurrentClassifyDriveRoot();

        PrepareInPlaceSortStage(outcome.Items);
        bool anim = animateSteps && CanAnimate;
        var movables = outcome.Items
            .Where(x => !x.IsKeepInPlace && x.Tier != ClassifySafetyTier.BlockedConservative)
            .ToList();
        if (movables.Count > 0)
            await RelayGlideMovableItemsAsync(movables, animate: anim, relayMs: 115, glideMs: 480, ct);
        if (anim)
            await WaitForActiveFlightsAsync(ct);
        await RevealKeepSectionHeaderSmoothAsync(animate: anim, ct);
        SetClassifyBanner("");
        RefreshOrganizeClassifyShortcutBar(isSortingNow: false);
    }

    private void ReplaceClassifyItems(List<ClassifyItem> nextItems)
    {
        foreach (var old in _classifyAllItems)
            old.PropertyChanged -= OnClassifyItemPropertyChanged;
        _classifyAllItems.Clear();
        _classifyItemsById.Clear();

        foreach (var item in nextItems)
        {
            item.PropertyChanged += OnClassifyItemPropertyChanged;
            _classifyAllItems.Add(item);
            _classifyItemsById[item.Id] = item;
        }
        RefreshClassifyModalItems();
    }

    private void RefreshClassifyModalItems()
    {
        static int SortBucket(ClassifyItem x)
        {
            if (!x.IsKeepInPlace) return 0; // 已分配分类的可移动/软链迁移项置顶
            if (x.Tier == ClassifySafetyTier.BlockedConservative && x.SupportsJunctionMigration) return 1; // 注册表软件/游戏紧随其后，方便点「启用软链搬家」
            if (!x.IsBlocked) return 2; // 其余暂留原位项
            return 3; // 系统/反作弊永久红线沉底
        }

        _classifyModalItems.Clear();
        foreach (var item in _classifyAllItems
                     .OrderBy(SortBucket)
                     .ThenByDescending(x => x.SizeBytes))
        {
            _classifyModalItems.Add(item);
        }
    }

    private void PrepareInPlaceSortStage(IReadOnlyList<ClassifyItem> candidates)
    {
        if (OrganizeGrid == null) return;

        // 若上次已处于按色分组状态，先干净还原为容量排序，让每次点击 AI 分析都能完整演绎原位飞升排序
        if (_classifySortActive)
        {
            ExitClassifySortMode(clearState: false);
            RefreshOrganizeRows();
        }

        _classifySortActive = true;
        _classifyKeepHeaderVisible = false;
        _sortFlyZIndex = 100;
        _classifyNodesByItemId.Clear();
        _classifyParentAnchorByItemId.Clear();
        _classifyGroupHeaders.Clear();
        _classifySettledIds.Clear();
        _classifyOriginalOrder.Clear();
        _classifyExtraKeepNodes.Clear();
        _classifyExistingBucketNodes.Clear();
        _activeFlightEndTick.Clear();

        ShowOrganizeState(OrganizeStateKind.None);
        foreach (var n in _organizeAll) n.ClearClassifyVisualState();

        string driveRoot = CurrentClassifyDriveRoot().TrimEnd('\\');
        var bucketCatByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(driveRoot))
        {
            foreach (var kv in _classifyCachedResolvedTargets)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                string fullBucket = CleanListSnapshot.NormPath(Path.Combine(driveRoot + "\\", kv.Value));
                if (fullBucket.Length > 0 && !bucketCatByPath.ContainsKey(fullBucket))
                    bucketCatByPath[fullBucket] = kv.Key;
            }
        }

        var usedNodes = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);
        long maxRowSize = 0;

        // 1) 完整冻结当前屏幕上的原始容量顺序（绝不提前把系统目录沉底，也绝不增删行引起首帧跳变）
        foreach (var row in _organizeRows)
        {
            if (row.IsClassifyGroupHeader) continue;
            if (!usedNodes.Add(row)) continue;
            _classifyOriginalOrder.Add(row);
            if (row.Size > maxRowSize) maxRowSize = row.Size;

            var hit = candidates.FirstOrDefault(c => c.SourceEntry == row.Dir);
            if (hit != null)
            {
                _classifyNodesByItemId[hit.Id] = row;
            }
            else
            {
                string rowNorm = CleanListSnapshot.NormPath(row.FullPath);
                if (bucketCatByPath.TryGetValue(rowNorm, out var bucketCatId)
                    && !FolderClassificationService.IsHardNoJunctionPath(rowNorm))
                {
                    if (!_classifyExistingBucketNodes.TryGetValue(bucketCatId, out var bList))
                    {
                        bList = new List<OrganizeNode>();
                        _classifyExistingBucketNodes[bucketCatId] = bList;
                    }
                    bList.Add(row);
                }
                else if (row.IsRoot)
                {
                    _classifyExtraKeepNodes.Add(row);
                }
            }
        }

        // 2) 为 Downloads / 迅雷下载 等深层子候选提前建好节点，并绑定其「可见父行锚点」（稍后被归类时直接从父行位置展开飞出）
        foreach (var item in candidates.OrderByDescending(x => x.SizeBytes))
        {
            if (_classifyNodesByItemId.ContainsKey(item.Id)) continue;
            if (!_organizeByDir.TryGetValue(item.SourceEntry, out var node))
                node = CreateTransientClassifyNode(item);

            node.SetFlatFilterMode(0);
            node.SetSizeShare(maxRowSize > 0 && node.Size > 0 ? (double)node.Size / maxRowSize : 0.0);
            _classifyNodesByItemId[item.Id] = node;

            var anchor = FindBestVisibleAncestorRow(item.SourcePath, _classifyOriginalOrder);
            if (anchor != null)
                _classifyParentAnchorByItemId[item.Id] = anchor;
        }

        OrganizeGrid.Visibility = Visibility.Visible;
        FindScrollViewer(OrganizeGrid)?.ScrollToTop();
        if (OrganizeClassifyBar != null) OrganizeClassifyBar.Visibility = Visibility.Visible;
    }

    private static OrganizeNode? FindBestVisibleAncestorRow(string fullPath, IReadOnlyList<OrganizeNode> visibleOrder)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return null;
        string normTarget = CleanListSnapshot.NormPath(fullPath);
        OrganizeNode? best = null;
        int bestLen = -1;

        for (int i = 0; i < visibleOrder.Count; i++)
        {
            var cand = visibleOrder[i];
            if (cand.IsClassifyGroupHeader || string.IsNullOrWhiteSpace(cand.FullPath)) continue;
            string normAnc = CleanListSnapshot.NormPath(cand.FullPath).TrimEnd('\\');
            if (normAnc.Length == 0) continue;

            if (normTarget.StartsWith(normAnc + "\\", StringComparison.OrdinalIgnoreCase) && normAnc.Length > bestLen)
            {
                best = cand;
                bestLen = normAnc.Length;
            }
        }
        return best;
    }

    private OrganizeNode CreateTransientClassifyNode(ClassifyItem item)
    {
        string driveRoot = CurrentClassifyDriveRoot();
        string rel = item.SourcePath;
        if (driveRoot.Length > 0 && item.SourcePath.StartsWith(driveRoot, StringComparison.OrdinalIgnoreCase))
            rel = item.SourcePath[driveRoot.Length..].TrimStart('\\');

        var node = new OrganizeNode(item.SourceEntry, new FolderId(item.SourcePath, _scanGeneration), 1, rel);
        node.SetLevel(OrganizeLevelPolicy.AutoChildLevel);
        node.SetChildDirCount(item.SourceEntry.ChildList.Count(c => c.IsDirectory && !c.IsFilesGroup));
        var sum = FolderPurposeRules.Summarize(item.SourceEntry, node.Id, 1, rel);
        node.SetEvidence(sum);
        return node;
    }

    private static void MarkNodeAsPendingProbe(OrganizeNode node, ClassifyItem item)
    {
        bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;
        if (item.Tier == ClassifySafetyTier.BlockedConservative)
        {
            var t = ClassifyCategoryPalette.KeepTheme;
            node.ApplyClassifyVisualState(
                item.Id, ClassifyCategoryRule.KeepId,
                t.AccentHex, t.RowWashHex(isDark), t.PillWashHex(isDark),
                "保守保护", item.SafetyNote, "🔒", false, false);
        }
        else
        {
            string probeAccent = "#94A3B8";
            string probeBg = isDark ? "#0A94A3B8" : "#0694A3B8";
            string probePill = isDark ? "#1E94A3B8" : "#1494A3B8";
            node.ApplyClassifyVisualState(
                item.Id, "__pending__",
                probeAccent, probeBg, probePill,
                "分析中", item.ContentDigestDisplay, "", item.CanCheck, item.IsChecked);
        }
    }

    private void ApplyClassifyItemStyleToNode(OrganizeNode node, ClassifyItem item, bool pendingProbe = false)
    {
        if (pendingProbe)
        {
            MarkNodeAsPendingProbe(node, item);
            return;
        }

        bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;
        var cats = _classifyCachedCats.Count > 0 ? _classifyCachedCats : App.Settings.EffectiveClassifyCategories();
        int idx = cats.FindIndex(c => string.Equals(c.Id, item.CategoryId, StringComparison.OrdinalIgnoreCase));
        var theme = item.IsKeepInPlace
            ? ClassifyCategoryPalette.KeepTheme
            : ClassifyCategoryPalette.For(item.CategoryId, Math.Max(0, idx));

        string catLabel = item.IsBlocked
            ? "保守保护"
            : (item.IsKeepInPlace
                ? "保留原位"
                : (item.UseJunction ? $"🔗 {item.CategoryName}" : item.CategoryName));

        string reason = item.UseJunction
            ? (item.IsCrossDrive ? $"跨盘软链 → {item.DestinationPath}" : $"隐藏软链 → {item.DestinationPath}")
            : (!string.IsNullOrWhiteSpace(item.AiReason)
                ? (item.FromAi ? $"AI：{item.AiReason}" : item.AiReason)
                : (item.IsBlocked ? item.SafetyNote : item.ContentDigest));

        node.ApplyClassifyVisualState(
            item.Id,
            EffectiveGroupKey(item),
            theme.AccentHex,
            theme.RowWashHex(isDark),
            theme.PillWashHex(isDark),
            catLabel,
            reason,
            "",
            item.CanCheck,
            item.IsChecked);
    }

    private static string EffectiveGroupKey(ClassifyItem item)
        => item.IsKeepInPlace ? ClassifyCategoryRule.KeepId : item.CategoryId;

    // ==================== 60fps 原位逐个接力排序动画引擎（像素级飞行中连续重定向 FLIP） ====================

    private async Task RelayGlideMovableItemsAsync(
        IReadOnlyList<ClassifyItem> movableItems,
        bool animate,
        int relayMs,
        int glideMs,
        CancellationToken ct)
    {
        if (OrganizeGrid == null || !_classifySortActive || movableItems.Count == 0) return;

        // 按所属色组总容量降序、组内按目录大小降序依次起飞，视觉上像磁吸一样同色逐个聚拢
        var groupBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in _classifyAllItems)
        {
            if (it.IsKeepInPlace || it.Tier == ClassifySafetyTier.BlockedConservative) continue;
            string gk = EffectiveGroupKey(it);
            groupBytes.TryGetValue(gk, out long cur);
            groupBytes[gk] = cur + Math.Max(0, it.SizeBytes);
        }

        var ordered = movableItems
            .Where(x => !x.IsKeepInPlace && x.Tier != ClassifySafetyTier.BlockedConservative && !_classifySettledIds.Contains(x.Id))
            .OrderByDescending(x => groupBytes.TryGetValue(EffectiveGroupKey(x), out long gb) ? gb : 0)
            .ThenByDescending(x => x.SizeBytes)
            .ThenBy(x => x.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count == 0) return;

        if (!animate)
        {
            foreach (var item in ordered)
            {
                _classifySettledIds.Add(item.Id);
                if (_classifyNodesByItemId.TryGetValue(item.Id, out var node))
                    ApplyClassifyItemStyleToNode(node, item);
            }
            var desired = BuildInPlaceProgressiveOrder(_classifyKeepHeaderVisible);
            ApplyMinimalCollectionDiff(_organizeRows, desired);
            RefreshAllSortHeaderNodes();
            return;
        }

        for (int idx = 0; idx < ordered.Count; idx++)
        {
            ct.ThrowIfCancellationRequested();
            var item = ordered[idx];
            await GlideSingleItemIntoColorGroupAsync(item, glideMs, ct);

            // 接力节奏：前 8 个保持从容可观赏的 ~360ms 间隔，超过 8 个逐渐收敛到 ~240ms
            if (idx < ordered.Count - 1)
            {
                int stepDelay = idx < 8 ? relayMs : Math.Max(240, relayMs - 100);
                await Task.Delay(stepDelay, ct);
            }
        }
    }

    private async Task GlideSingleItemIntoColorGroupAsync(ClassifyItem item, int glideMs, CancellationToken ct)
    {
        if (OrganizeGrid == null || !_classifySortActive) return;
        if (!_classifyNodesByItemId.TryGetValue(item.Id, out var node)) return;

        bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;
        bool wasAlreadyInGrid = _organizeRows.Contains(node);

        // 1) 原位亮色预备拍：先亮起分类主题色停留 ~145ms + 轻浮起，或轻脉冲父行（Downloads / 迅雷下载），让视线自然锁定起飞点
        ApplyClassifyItemStyleToNode(node, item);
        if (wasAlreadyInGrid)
        {
            int curIdx = _organizeRows.IndexOf(node);
            if (curIdx >= 0 && OrganizeGrid.ItemContainerGenerator.ContainerFromIndex(curIdx) is DataGridRow spotRow && spotRow.IsVisible)
            {
                spotRow.RenderTransformOrigin = new Point(0.5, 0.5);
                var (_, s) = EnsureCompositeTransform(spotRow);
                var prePop = new DoubleAnimation(1.0, 1.022, TimeSpan.FromMilliseconds(120))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                s.BeginAnimation(ScaleTransform.ScaleXProperty, prePop);
                s.BeginAnimation(ScaleTransform.ScaleYProperty, prePop.Clone());
            }
            await Task.Delay(145, ct);
        }
        else if (_classifyParentAnchorByItemId.TryGetValue(item.Id, out var parentAnc))
        {
            int ancIdx = _organizeRows.IndexOf(parentAnc);
            if (ancIdx >= 0 && OrganizeGrid.ItemContainerGenerator.ContainerFromIndex(ancIdx) is DataGridRow ancRow && ancRow.IsVisible)
                AnimateSpringPop(ancRow, fromScale: 0.99, offsetY: 0, durationMs: 180);
            await Task.Delay(110, ct);
        }

        ct.ThrowIfCancellationRequested();

        // 2) 左侧呼吸流光环同步同色共振脉冲
        PulseRadialAiRing(node.ClassifyAccentHex);

        // 3) 同一帧内完成：拍当前所有可见行真实视觉像素 -> 清理旧变换 -> 差量移入新槽位 -> 单次 UpdateLayout -> 像素级连续重定向 FLIP
        _classifySettledIds.Add(item.Id);

        var liveTopY = SnapshotVisibleRowVisualTops(clearTransformsAfterSnapshot: true);
        double gridH = Math.Max(360, OrganizeGrid.ActualHeight > 0 ? OrganizeGrid.ActualHeight : 560);

        double originY;
        if (liveTopY.TryGetValue(node, out double selfY))
        {
            originY = selfY;
        }
        else if (_classifyParentAnchorByItemId.TryGetValue(item.Id, out var anchorNode)
                 && liveTopY.TryGetValue(anchorNode, out double ancY))
        {
            originY = ancY + 8.0;
        }
        else
        {
            originY = Math.Min(gridH - 48.0, Math.Max(260.0, gridH * 0.74));
        }

        var existingHeaderKeys = new HashSet<string>(_classifyGroupHeaders.Keys, StringComparer.OrdinalIgnoreCase);
        var desired = BuildInPlaceProgressiveOrder(_classifyKeepHeaderVisible);
        var newlyCreatedHeaders = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);
        foreach (var kv in _classifyGroupHeaders)
        {
            if (!existingHeaderKeys.Contains(kv.Key))
                newlyCreatedHeaders.Add(kv.Value);
        }

        _classifyAnimInFlight = true;
        try
        {
            ApplyMinimalCollectionDiff(_organizeRows, desired);
            RefreshAllSortHeaderNodes();
            OrganizeGrid.UpdateLayout();

            long nowTick = Environment.TickCount64;
            var launchEase = new QuarticEase { EasingMode = EasingMode.EaseInOut };
            var continueEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var yieldEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            for (int i = 0; i < _organizeRows.Count; i++)
            {
                var rowNode = _organizeRows[i];
                if (OrganizeGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row || !row.IsVisible)
                    continue;

                double afterY = MeasureRowLayoutTop(row);

                // A) 本次新诞生的彩色分组细标题行：轻盈淡入浮现
                if (newlyCreatedHeaders.Contains(rowNode))
                {
                    int hdrEpoch = NextMotionEpoch(row);
                    var (tr, _) = EnsureCompositeTransform(row);
                    tr.BeginAnimation(TranslateTransform.YProperty, null);
                    row.BeginAnimation(OpacityProperty, null);
                    tr.Y = -10.0;
                    row.Opacity = 0.0;

                    var animY = new DoubleAnimation(-10.0, 0.0, TimeSpan.FromMilliseconds(320))
                    { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = continueEase };
                    var animO = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320))
                    { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = continueEase };
                    animO.Completed += (_, _) =>
                    {
                        if (!IsMotionEpoch(row, hdrEpoch)) return;
                        ResetRowMotionTransform(row);
                    };
                    tr.BeginAnimation(TranslateTransform.YProperty, animY);
                    row.BeginAnimation(OpacityProperty, animO);
                    continue;
                }

                // B) 本次新起飞的主角彩色目录：从真实原位 / Downloads 父行一路丝滑飞升至对应色组
                if (ReferenceEquals(rowNode, node))
                {
                    double rawDy = originY - afterY;
                    if (!wasAlreadyInGrid && Math.Abs(rawDy) < 28.0)
                        rawDy = 104.0;
                    double dy = Math.Clamp(rawDy, -gridH, gridH);

                    int rowEpoch = NextMotionEpoch(row);
                    row.RenderTransformOrigin = new Point(0.5, 0.5);
                    var (translate, scale) = EnsureCompositeTransform(row);
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    row.BeginAnimation(OpacityProperty, null);

                    translate.Y = dy;
                    row.Background = GetCachedOpaqueSlideBrush(rowNode.ClassifyAccentHex, isDark);
                    Panel.SetZIndex(row, ++_sortFlyZIndex);

                    if (!wasAlreadyInGrid)
                    {
                        // 从父行（如 Downloads）展开飞出：由微缩半透自然舒展升起
                        row.Opacity = 0.2;
                        scale.ScaleX = 0.94;
                        scale.ScaleY = 0.94;
                        row.BeginAnimation(OpacityProperty,
                            new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = continueEase });
                        var sKf = new DoubleAnimationUsingKeyFrames();
                        sKf.KeyFrames.Add(new EasingDoubleKeyFrame(1.022, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), continueEase));
                        sKf.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(glideMs)), launchEase));
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, sKf);
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, sKf.Clone());
                    }
                    else
                    {
                        row.Opacity = 1.0;
                        scale.ScaleX = 1.022;
                        scale.ScaleY = 1.022;
                        var sBack = new DoubleAnimation(1.022, 1.0, TimeSpan.FromMilliseconds(glideMs)) { EasingFunction = launchEase };
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, sBack);
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, sBack.Clone());
                    }

                    _activeFlightEndTick[rowNode] = nowTick + glideMs;
                    var animY = new DoubleAnimation(dy, 0.0, TimeSpan.FromMilliseconds(glideMs)) { EasingFunction = launchEase };
                    animY.Completed += (_, _) =>
                    {
                        if (!IsMotionEpoch(row, rowEpoch)) return;
                        _activeFlightEndTick.Remove(rowNode);
                        row.ClearValue(Control.BackgroundProperty);
                        ResetRowMotionTransform(row);
                    };
                    translate.BeginAnimation(TranslateTransform.YProperty, animY);
                    continue;
                }

                if (!liveTopY.TryGetValue(rowNode, out double prevY))
                    continue;

                double deltaY = Math.Clamp(prevY - afterY, -gridH, gridH);
                bool stillAirborne = _activeFlightEndTick.TryGetValue(rowNode, out long endTick) && endTick > nowTick;

                if (!stillAirborne && Math.Abs(deltaY) <= 0.8)
                    continue;

                int nEpoch = NextMotionEpoch(row);
                row.RenderTransformOrigin = new Point(0.5, 0.5);
                var (trN, scN) = EnsureCompositeTransform(row);
                trN.BeginAnimation(TranslateTransform.YProperty, null);
                trN.Y = deltaY;

                if (stillAirborne)
                {
                    // C) 上一拍尚在空中的接力行：从当前真实视觉像素无缝续飞至新槽位，绝无顿挫或跳帧
                    int remainMs = (int)Math.Clamp(endTick - nowTick, 220, glideMs);
                    row.Background = GetCachedOpaqueSlideBrush(rowNode.ClassifyAccentHex, isDark);
                    if (Panel.GetZIndex(row) < 100) Panel.SetZIndex(row, ++_sortFlyZIndex);

                    var contY = new DoubleAnimation(deltaY, 0.0, TimeSpan.FromMilliseconds(remainMs)) { EasingFunction = continueEase };
                    contY.Completed += (_, _) =>
                    {
                        if (!IsMotionEpoch(row, nEpoch)) return;
                        _activeFlightEndTick.Remove(rowNode);
                        row.ClearValue(Control.BackgroundProperty);
                        ResetRowMotionTransform(row);
                    };
                    trN.BeginAnimation(TranslateTransform.YProperty, contY);
                }
                else
                {
                    // D) 沿途系统 / 相邻目录行：从当前像素顺滑下让一个行高，如水银般给主角让出走廊
                    Panel.SetZIndex(row, 0);
                    scN.ScaleX = 1.0;
                    scN.ScaleY = 1.0;
                    int yieldDurMs = Math.Clamp((int)(glideMs * 0.78), 460, 960);
                    var yieldY = new DoubleAnimation(deltaY, 0.0, TimeSpan.FromMilliseconds(yieldDurMs)) { EasingFunction = yieldEase };
                    yieldY.Completed += (_, _) =>
                    {
                        if (!IsMotionEpoch(row, nEpoch)) return;
                        ResetRowMotionTransform(row);
                    };
                    trN.BeginAnimation(TranslateTransform.YProperty, yieldY);
                }
            }
        }
        finally
        {
            _classifyAnimInFlight = false;
        }
    }

    private async Task WaitForActiveFlightsAsync(CancellationToken ct)
    {
        if (!CanAnimate || _activeFlightEndTick.Count == 0) return;
        long now = Environment.TickCount64;
        long latest = 0;
        foreach (var end in _activeFlightEndTick.Values)
            if (end > latest) latest = end;
        int waitMs = (int)(latest - now) + 45;
        if (waitMs > 0)
            await Task.Delay(Math.Min(waitMs, 1450), ct);
    }

    /// <summary>
    /// 全部彩色目录飞升落定后：将盘上已有归档目标（如文档/视频/Games）纳入对应色组，并为下方系统与原位目录补上细分隔栏与静音灰标（零 Clear、零重排、零闪屏）。
    /// </summary>
    private async Task RevealKeepSectionHeaderSmoothAsync(bool animate, CancellationToken ct)
    {
        if (OrganizeGrid == null || !_classifySortActive) return;

        foreach (var item in _classifyAllItems)
        {
            if (!_classifySettledIds.Contains(item.Id))
                _classifySettledIds.Add(item.Id);

            if (_classifyNodesByItemId.TryGetValue(item.Id, out var node)
                && (item.IsKeepInPlace || item.IsBlocked))
            {
                ApplyClassifyItemStyleToNode(node, item);
            }
        }
        foreach (var kv in _classifyExistingBucketNodes)
        {
            foreach (var bNode in kv.Value)
                StyleExistingBucketNode(bNode, kv.Key);
        }
        foreach (var extra in _classifyExtraKeepNodes)
            StyleExtraKeepNode(extra);

        _classifyKeepHeaderVisible = true;
        var existingHeaderKeys = new HashSet<string>(_classifyGroupHeaders.Keys, StringComparer.OrdinalIgnoreCase);

        if (!animate)
        {
            var desiredSync = BuildInPlaceProgressiveOrder(includeKeepHeader: true);
            ApplyMinimalCollectionDiff(_organizeRows, desiredSync);
            RefreshAllSortHeaderNodes();
            return;
        }

        var liveTopY = SnapshotVisibleRowVisualTops(clearTransformsAfterSnapshot: true);
        double gridH = Math.Max(360, OrganizeGrid.ActualHeight > 0 ? OrganizeGrid.ActualHeight : 560);
        var desired = BuildInPlaceProgressiveOrder(includeKeepHeader: true);
        bool keepHeaderNewlyAdded = !existingHeaderKeys.Contains(ClassifyCategoryRule.KeepId)
            && _classifyGroupHeaders.TryGetValue(ClassifyCategoryRule.KeepId, out _);

        _classifyAnimInFlight = true;
        try
        {
            ApplyMinimalCollectionDiff(_organizeRows, desired);
            RefreshAllSortHeaderNodes();
            OrganizeGrid.UpdateLayout();

            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            for (int i = 0; i < _organizeRows.Count; i++)
            {
                var rowNode = _organizeRows[i];
                if (OrganizeGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row || !row.IsVisible)
                    continue;

                if (keepHeaderNewlyAdded && rowNode.IsClassifyGroupHeader
                    && string.Equals(rowNode.ClassifyGroupId, ClassifyCategoryRule.KeepId, StringComparison.OrdinalIgnoreCase))
                {
                    int hEpoch = NextMotionEpoch(row);
                    var (tr, _) = EnsureCompositeTransform(row);
                    tr.Y = -8.0;
                    row.Opacity = 0.0;
                    var aY = new DoubleAnimation(-8.0, 0.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = easeOut };
                    var aO = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = easeOut };
                    aO.Completed += (_, _) => { if (IsMotionEpoch(row, hEpoch)) ResetRowMotionTransform(row); };
                    tr.BeginAnimation(TranslateTransform.YProperty, aY);
                    row.BeginAnimation(OpacityProperty, aO);
                    continue;
                }

                if (!liveTopY.TryGetValue(rowNode, out double prevY)) continue;
                double afterY = MeasureRowLayoutTop(row);
                double dy = Math.Clamp(prevY - afterY, -gridH, gridH);
                if (Math.Abs(dy) <= 0.8) continue;

                int rEpoch = NextMotionEpoch(row);
                var (trN, _) = EnsureCompositeTransform(row);
                trN.Y = dy;
                var aSlide = new DoubleAnimation(dy, 0.0, TimeSpan.FromMilliseconds(360)) { EasingFunction = easeOut };
                aSlide.Completed += (_, _) => { if (IsMotionEpoch(row, rEpoch)) ResetRowMotionTransform(row); };
                trN.BeginAnimation(TranslateTransform.YProperty, aSlide);
            }
        }
        finally
        {
            _classifyAnimInFlight = false;
        }

        await Task.Delay(340, ct);
    }

    /// <summary>供弹窗手动改分类或应用加载校准触发的平滑单项重排。</summary>
    private async Task ApplyBatchSortWaveAsync(
        IReadOnlyList<ClassifyItem> batchItems, bool animate, CancellationToken ct)
    {
        if (OrganizeGrid == null || !_classifySortActive || batchItems.Count == 0) return;

        foreach (var item in batchItems)
        {
            _classifySettledIds.Add(item.Id);
            if (_classifyNodesByItemId.TryGetValue(item.Id, out var node))
                ApplyClassifyItemStyleToNode(node, item);
        }

        if (!animate)
        {
            var desiredSync = BuildInPlaceProgressiveOrder(_classifyKeepHeaderVisible);
            ApplyMinimalCollectionDiff(_organizeRows, desiredSync);
            RefreshAllSortHeaderNodes();
            return;
        }

        var liveTopY = SnapshotVisibleRowVisualTops(clearTransformsAfterSnapshot: true);
        double gridH = Math.Max(360, OrganizeGrid.ActualHeight > 0 ? OrganizeGrid.ActualHeight : 560);
        var desired = BuildInPlaceProgressiveOrder(_classifyKeepHeaderVisible);
        var movedSet = new HashSet<OrganizeNode>(
            batchItems.Select(x => _classifyNodesByItemId.TryGetValue(x.Id, out var n) ? n : null).Where(n => n != null)!,
            ReferenceEqualityComparer.Instance);
        bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;

        _classifyAnimInFlight = true;
        try
        {
            ApplyMinimalCollectionDiff(_organizeRows, desired);
            RefreshAllSortHeaderNodes();
            OrganizeGrid.UpdateLayout();

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            for (int i = 0; i < _organizeRows.Count; i++)
            {
                var rowNode = _organizeRows[i];
                if (OrganizeGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row || !row.IsVisible)
                    continue;
                if (!liveTopY.TryGetValue(rowNode, out double prevY)) continue;

                double afterY = MeasureRowLayoutTop(row);
                double dy = Math.Clamp(prevY - afterY, -gridH, gridH);
                if (Math.Abs(dy) <= 0.8) continue;

                int epoch = NextMotionEpoch(row);
                var (tr, _) = EnsureCompositeTransform(row);
                tr.Y = dy;
                bool isStar = movedSet.Contains(rowNode);
                if (isStar)
                {
                    row.Background = GetCachedOpaqueSlideBrush(rowNode.ClassifyAccentHex, isDark);
                    Panel.SetZIndex(row, ++_sortFlyZIndex);
                }
                else
                {
                    Panel.SetZIndex(row, 0);
                }

                var anim = new DoubleAnimation(dy, 0.0, TimeSpan.FromMilliseconds(isStar ? 460 : 380)) { EasingFunction = ease };
                anim.Completed += (_, _) =>
                {
                    if (!IsMotionEpoch(row, epoch)) return;
                    if (isStar) row.ClearValue(Control.BackgroundProperty);
                    ResetRowMotionTransform(row);
                };
                tr.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }
        finally
        {
            _classifyAnimInFlight = false;
        }

        await Task.Delay(480, ct);
    }

    private Dictionary<OrganizeNode, double> SnapshotVisibleRowVisualTops(bool clearTransformsAfterSnapshot)
    {
        var map = new Dictionary<OrganizeNode, double>(ReferenceEqualityComparer.Instance);
        if (OrganizeGrid == null) return map;

        for (int i = 0; i < _organizeRows.Count; i++)
        {
            if (OrganizeGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row
                || !row.IsVisible || row.ActualHeight <= 0)
                continue;

            // 用行中心点反推顶部坐标，自动抵消 ScaleTransform(0.5, 0.5) 带来的微像素偏差
            double halfH = row.ActualHeight * 0.5;
            double visualTop = row.TransformToAncestor(OrganizeGrid).Transform(new Point(0, halfH)).Y - halfH;
            map[_organizeRows[i]] = visualTop;

            if (clearTransformsAfterSnapshot)
            {
                var (tr, sc) = EnsureCompositeTransform(row);
                tr.BeginAnimation(TranslateTransform.YProperty, null);
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                tr.Y = 0;
                sc.ScaleX = 1.0;
                sc.ScaleY = 1.0;
            }
        }
        return map;
    }

    private double MeasureRowLayoutTop(DataGridRow row)
    {
        double halfH = row.ActualHeight * 0.5;
        return row.TransformToAncestor(OrganizeGrid!).Transform(new Point(0, halfH)).Y - halfH;
    }

    /// <summary>
    /// 构造原位渐进式目标序列：
    /// 1) 顶部：所有已落定的彩色可整理分组（以及盘上已有的对应分类落地目录，按组总大小降序），支持行内展开子目录；
    /// 2) 下方：所有尚未飞走的原始列表行（含系统核心与未判项）严格保持原始顺序不动，绝不提前打乱或沉底。
    /// </summary>
    private List<OrganizeNode> BuildInPlaceProgressiveOrder(bool includeKeepHeader)
    {
        var cats = _classifyCachedCats.Count > 0 ? _classifyCachedCats : App.Settings.EffectiveClassifyCategories();
        var settledMovable = new List<ClassifyItem>();
        var settledMovableNodeSet = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);

        foreach (var item in _classifyAllItems)
        {
            if (!_classifySettledIds.Contains(item.Id)) continue;
            if (item.IsKeepInPlace || item.IsBlocked) continue;
            if (_classifyNodesByItemId.TryGetValue(item.Id, out var node))
            {
                settledMovable.Add(item);
                settledMovableNodeSet.Add(node);
            }
        }

        // 若已进入收尾阶段或即时布局，把盘上已有的分类落地桶节点（如 D:\文档、D:\视频、D:\Games）也归入对应色组并从底部原位队列移出
        var settledBucketNodeSet = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);
        if (includeKeepHeader)
        {
            foreach (var list in _classifyExistingBucketNodes.Values)
            {
                foreach (var bn in list)
                    settledBucketNodeSet.Add(bn);
            }
        }

        var result = new List<OrganizeNode>(_organizeRows.Count + 8);
        var activeGroupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void AppendExpandedDescendants(List<OrganizeNode> target, OrganizeNode parent, int baseDepth)
        {
            if (!parent.IsExpanded || !parent.ChildrenLoaded) return;
            foreach (var child in parent.Children)
            {
                child.SetFlatFilterMode(baseDepth + 1);
                target.Add(child);
                if (child.IsExpanded && child.ChildrenLoaded)
                    AppendExpandedDescendants(target, child, baseDepth + 1);
            }
        }

        // 1) 上半区：所有已飞升落定的彩色可整理分组 + 已有分类落地目录
        var movableByGroup = settledMovable
            .GroupBy(EffectiveGroupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var candidateGroupKeys = new HashSet<string>(movableByGroup.Keys, StringComparer.OrdinalIgnoreCase);
        if (includeKeepHeader)
        {
            foreach (var kv in _classifyExistingBucketNodes)
                if (kv.Value.Count > 0) candidateGroupKeys.Add(kv.Key);
        }

        long GroupTotalBytes(string gk)
        {
            long bytes = 0;
            if (movableByGroup.TryGetValue(gk, out var mList))
                bytes += mList.Sum(x => Math.Max(0, x.SizeBytes));
            if (includeKeepHeader && _classifyExistingBucketNodes.TryGetValue(gk, out var bList))
                bytes += bList.Sum(n => Math.Max(0, n.Size));
            return bytes;
        }

        foreach (var gk in candidateGroupKeys
                     .OrderByDescending(GroupTotalBytes)
                     .ThenBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            activeGroupKeys.Add(gk);
            var hdr = EnsureOrCreateGroupHeaderNode(gk, cats);
            bool collapsed = _classifyCollapsedGroups.Contains(gk);
            hdr.SetClassifyGroupCollapsed(collapsed);
            result.Add(hdr);
            if (!collapsed)
            {
                if (movableByGroup.TryGetValue(gk, out var mList))
                {
                    foreach (var ci in mList.OrderByDescending(x => x.SizeBytes).ThenBy(x => x.SourceName, StringComparer.OrdinalIgnoreCase))
                    {
                        if (_classifyNodesByItemId.TryGetValue(ci.Id, out var node))
                        {
                            node.SetFlatFilterMode(0);
                            result.Add(node);
                            AppendExpandedDescendants(result, node, 0);
                        }
                    }
                }
                if (includeKeepHeader && _classifyExistingBucketNodes.TryGetValue(gk, out var bList))
                {
                    foreach (var bNode in bList.OrderByDescending(x => x.Size).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        if (settledMovableNodeSet.Contains(bNode)) continue;
                        bNode.SetFlatFilterMode(0);
                        result.Add(bNode);
                        AppendExpandedDescendants(result, bNode, 0);
                    }
                }
            }
        }

        // 2) 下半区：其余所有原位目录严格沿用初始列表顺序（完全顺滑下让，零额外洗牌）
        var remainingOriginals = new List<OrganizeNode>(_classifyOriginalOrder.Count);
        var remainingSet = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);
        foreach (var orig in _classifyOriginalOrder)
        {
            if (!settledMovableNodeSet.Contains(orig) && !settledBucketNodeSet.Contains(orig) && remainingSet.Add(orig))
                remainingOriginals.Add(orig);
        }

        // 补充未被任何现有父行覆盖的独立保留项（例如离线预览或新增顶层保守保护目录）
        if (includeKeepHeader)
        {
            foreach (var item in _classifyAllItems)
            {
                if (!item.IsKeepInPlace && !item.IsBlocked) continue;
                if (!_classifyNodesByItemId.TryGetValue(item.Id, out var kNode)) continue;
                if (settledMovableNodeSet.Contains(kNode) || settledBucketNodeSet.Contains(kNode) || remainingSet.Contains(kNode)) continue;
                if (_classifyParentAnchorByItemId.ContainsKey(item.Id)) continue;
                if (remainingSet.Add(kNode))
                    remainingOriginals.Add(kNode);
            }
        }

        if (remainingOriginals.Count > 0)
        {
            bool keepCollapsed = includeKeepHeader && _classifyCollapsedGroups.Contains(ClassifyCategoryRule.KeepId);
            if (includeKeepHeader)
            {
                activeGroupKeys.Add(ClassifyCategoryRule.KeepId);
                var keepHdr = EnsureOrCreateGroupHeaderNode(ClassifyCategoryRule.KeepId, cats);
                keepHdr.SetClassifyGroupCollapsed(keepCollapsed);
                result.Add(keepHdr);
            }
            if (!keepCollapsed)
            {
                foreach (var orig in remainingOriginals)
                {
                    orig.SetFlatFilterMode(0);
                    result.Add(orig);
                    AppendExpandedDescendants(result, orig, 0);
                }
            }
        }

        // 清理已空掉的旧组头缓存
        foreach (var staleKey in _classifyGroupHeaders.Keys.Where(k => !activeGroupKeys.Contains(k)).ToList())
            _classifyGroupHeaders.Remove(staleKey);

        return result;
    }

    /// <summary>
    /// 极简原位差量同步 ObservableCollection：绝不 Clear()，已在正确位置上的行保持零触碰、不销毁容器。
    /// </summary>
    private static void ApplyMinimalCollectionDiff(ObservableCollection<OrganizeNode> target, IReadOnlyList<OrganizeNode> desired)
    {
        var desiredSet = new HashSet<OrganizeNode>(desired, ReferenceEqualityComparer.Instance);
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(target[i]))
                target.RemoveAt(i);
        }

        for (int i = 0; i < desired.Count; i++)
        {
            var wanted = desired[i];
            if (i < target.Count && ReferenceEquals(target[i], wanted))
                continue;

            int existingIdx = -1;
            for (int j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], wanted))
                {
                    existingIdx = j;
                    break;
                }
            }

            if (existingIdx >= 0)
            {
                target.RemoveAt(existingIdx);
                target.Insert(i, wanted);
            }
            else
            {
                target.Insert(i, wanted);
            }
        }
    }

    private static Brush GetCachedOpaqueSlideBrush(string accentHex, bool isDark)
    {
        string cacheKey = (isDark ? "D:" : "L:") + (accentHex ?? "");
        return _opaqueSlideBrushCache.GetOrAdd(cacheKey, _ =>
        {
            Color bg = isDark ? Color.FromRgb(0x16, 0x19, 0x20) : Color.FromRgb(0xFF, 0xFF, 0xFF);
            Color accent = Color.FromRgb(0xFF, 0x5A, 0x1F);
            try
            {
                if (ColorConverter.ConvertFromString(accentHex) is Color c) accent = c;
            }
            catch { }

            double alpha = isDark ? 0.16 : 0.08;
            byte r = (byte)(bg.R * (1 - alpha) + accent.R * alpha);
            byte g = (byte)(bg.G * (1 - alpha) + accent.G * alpha);
            byte b = (byte)(bg.B * (1 - alpha) + accent.B * alpha);
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        });
    }

    private OrganizeNode EnsureOrCreateGroupHeaderNode(string groupKey, IReadOnlyList<ClassifyCategoryRule> cats)
    {
        if (_classifyGroupHeaders.TryGetValue(groupKey, out var existing))
            return existing;

        bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;
        bool isKeep = string.Equals(groupKey, ClassifyCategoryRule.KeepId, StringComparison.OrdinalIgnoreCase);

        int idx = cats.ToList().FindIndex(c => string.Equals(c.Id, groupKey, StringComparison.OrdinalIgnoreCase));
        var rule = idx >= 0 ? cats[idx] : null;
        var theme = isKeep ? ClassifyCategoryPalette.KeepTheme : ClassifyCategoryPalette.For(groupKey, Math.Max(0, idx));

        string groupName = isKeep ? "系统核心与原位保护" : (rule?.Name ?? groupKey);
        string folderName = isKeep ? "" : (rule?.FolderName ?? groupKey);
        if (!isKeep && _classifyCachedResolvedTargets.TryGetValue(groupKey, out var actualBucket) && !string.IsNullOrWhiteSpace(actualBucket))
            folderName = actualBucket;

        var hdr = OrganizeNode.CreateClassifyGroupHeader(
            groupKey, groupName, folderName, theme.AccentHex, theme.TintHex(isDark), theme.PillWashHex(isDark));
        _classifyGroupHeaders[groupKey] = hdr;
        return hdr;
    }

    private void RefreshAllSortHeaderNodes()
    {
        if (!_classifySortActive) return;
        bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;
        var cats = _classifyCachedCats.Count > 0 ? _classifyCachedCats : App.Settings.EffectiveClassifyCategories();

        // 从逻辑集合统计各组目录数与总容量，确保分组折叠后标题行的数量与容量依然精确且绝不被误删
        var countByKey = new Dictionary<string, (int Count, int Checked, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        var settledMovableNodes = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);
        var settledBucketNodes = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);

        foreach (var ci in _classifyAllItems)
        {
            if (!_classifySettledIds.Contains(ci.Id)) continue;
            if (ci.IsKeepInPlace || ci.IsBlocked) continue;
            if (!_classifyNodesByItemId.TryGetValue(ci.Id, out var node)) continue;
            settledMovableNodes.Add(node);
            string key = EffectiveGroupKey(ci);
            countByKey.TryGetValue(key, out var cur);
            countByKey[key] = (cur.Count + 1, cur.Checked + (ci.IsChecked && ci.CanCheck ? 1 : 0), cur.Bytes + Math.Max(0, ci.SizeBytes));
        }

        if (_classifyKeepHeaderVisible)
        {
            foreach (var kv in _classifyExistingBucketNodes)
            {
                foreach (var bNode in kv.Value)
                {
                    if (settledMovableNodes.Contains(bNode) || !settledBucketNodes.Add(bNode)) continue;
                    countByKey.TryGetValue(kv.Key, out var cur);
                    countByKey[kv.Key] = (cur.Count + 1, cur.Checked, cur.Bytes + Math.Max(0, bNode.Size));
                }
            }

            var countedKeep = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);
            foreach (var orig in _classifyOriginalOrder)
            {
                if (settledMovableNodes.Contains(orig) || settledBucketNodes.Contains(orig) || !countedKeep.Add(orig)) continue;
                countByKey.TryGetValue(ClassifyCategoryRule.KeepId, out var cur);
                countByKey[ClassifyCategoryRule.KeepId] = (cur.Count + 1, cur.Checked, cur.Bytes + Math.Max(0, orig.Size));
            }
            foreach (var ci in _classifyAllItems)
            {
                if (!ci.IsKeepInPlace && !ci.IsBlocked) continue;
                if (!_classifyNodesByItemId.TryGetValue(ci.Id, out var kNode)) continue;
                if (settledMovableNodes.Contains(kNode) || settledBucketNodes.Contains(kNode) || _classifyParentAnchorByItemId.ContainsKey(ci.Id)) continue;
                if (!countedKeep.Add(kNode)) continue;
                countByKey.TryGetValue(ClassifyCategoryRule.KeepId, out var cur);
                countByKey[ClassifyCategoryRule.KeepId] = (cur.Count + 1, cur.Checked, cur.Bytes + Math.Max(0, ci.SizeBytes));
            }
        }

        foreach (var kv in _classifyGroupHeaders.ToList())
        {
            string key = kv.Key;
            var hdr = kv.Value;
            bool isKeep = string.Equals(key, ClassifyCategoryRule.KeepId, StringComparison.OrdinalIgnoreCase);
            countByKey.TryGetValue(key, out var stats);

            if (stats.Count == 0 && _organizeRows.Contains(hdr))
            {
                _organizeRows.Remove(hdr);
                _classifyGroupHeaders.Remove(key);
                continue;
            }

            int idx = cats.FindIndex(c => string.Equals(c.Id, key, StringComparison.OrdinalIgnoreCase));
            var rule = idx >= 0 ? cats[idx] : null;
            var theme = isKeep ? ClassifyCategoryPalette.KeepTheme : ClassifyCategoryPalette.For(key, Math.Max(0, idx));

            string groupName = isKeep ? "系统核心与原位保护" : (rule?.Name ?? key);
            string targetDir = isKeep
                ? ""
                : (_classifyCachedResolvedTargets.TryGetValue(key, out var fn) && !string.IsNullOrWhiteSpace(fn) ? fn : (rule?.FolderName ?? key));

            hdr.SetClassifyGroupCollapsed(_classifyCollapsedGroups.Contains(key));
            hdr.UpdateClassifyGroupHeader(
                groupName, targetDir, stats.Count, stats.Checked, stats.Bytes,
                theme.AccentHex, theme.TintHex(isDark), theme.PillWashHex(isDark), isKeep);
        }
    }

    private void OrganizeGrid_PreviewLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_classifySortActive || OrganizeGrid == null) return;
        DependencyObject? cur = e.OriginalSource as DependencyObject;
        while (cur != null && cur is not DataGridRow)
            cur = VisualTreeHelper.GetParent(cur);

        if (cur is DataGridRow { Item: OrganizeNode { IsClassifyGroupHeader: true } hdr }
            && !string.IsNullOrWhiteSpace(hdr.ClassifyGroupId))
        {
            ToggleClassifyGroupCollapsed(hdr.ClassifyGroupId);
            e.Handled = true;
        }
    }

    private void ToggleClassifyGroupCollapsed(string groupId)
    {
        if (!_classifySortActive || OrganizeGrid == null || string.IsNullOrWhiteSpace(groupId)) return;

        bool nowCollapsed = !_classifyCollapsedGroups.Contains(groupId);
        if (nowCollapsed) _classifyCollapsedGroups.Add(groupId);
        else _classifyCollapsedGroups.Remove(groupId);

        if (!CanAnimate)
        {
            ApplyMinimalCollectionDiff(_organizeRows, BuildInPlaceProgressiveOrder(_classifyKeepHeaderVisible));
            RefreshAllSortHeaderNodes();
            SaveActiveDriveToCache();
            return;
        }

        var liveTopY = SnapshotVisibleRowVisualTops(clearTransformsAfterSnapshot: true);
        double gridH = Math.Max(360, OrganizeGrid.ActualHeight > 0 ? OrganizeGrid.ActualHeight : 560);
        var desired = BuildInPlaceProgressiveOrder(_classifyKeepHeaderVisible);

        _classifyAnimInFlight = true;
        try
        {
            ApplyMinimalCollectionDiff(_organizeRows, desired);
            RefreshAllSortHeaderNodes();
            OrganizeGrid.UpdateLayout();

            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            for (int i = 0; i < _organizeRows.Count; i++)
            {
                var rowNode = _organizeRows[i];
                if (OrganizeGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row || !row.IsVisible)
                    continue;

                if (liveTopY.TryGetValue(rowNode, out double prevY))
                {
                    double afterY = MeasureRowLayoutTop(row);
                    double dy = Math.Clamp(prevY - afterY, -gridH, gridH);
                    if (Math.Abs(dy) <= 0.8) continue;

                    int epoch = NextMotionEpoch(row);
                    var (tr, _) = EnsureCompositeTransform(row);
                    tr.Y = dy;
                    var slide = new DoubleAnimation(dy, 0.0, TimeSpan.FromMilliseconds(240)) { EasingFunction = easeOut };
                    slide.Completed += (_, _) => { if (IsMotionEpoch(row, epoch)) ResetRowMotionTransform(row); };
                    tr.BeginAnimation(TranslateTransform.YProperty, slide);
                }
                else if (!nowCollapsed)
                {
                    // 新展开露出的子行柔和淡入
                    int epoch = NextMotionEpoch(row);
                    var (tr, _) = EnsureCompositeTransform(row);
                    tr.Y = -8.0;
                    row.Opacity = 0.0;
                    var aY = new DoubleAnimation(-8.0, 0.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeOut };
                    var aO = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeOut };
                    aO.Completed += (_, _) => { if (IsMotionEpoch(row, epoch)) ResetRowMotionTransform(row); };
                    tr.BeginAnimation(TranslateTransform.YProperty, aY);
                    row.BeginAnimation(OpacityProperty, aO);
                }
            }
        }
        finally
        {
            _classifyAnimInFlight = false;
        }

        SaveActiveDriveToCache();
    }

    private void SyncClassifyGroupedRowsInPlace()
    {
        if (!_classifySortActive) return;
        ApplyMinimalCollectionDiff(_organizeRows, BuildInPlaceProgressiveOrder(_classifyKeepHeaderVisible));
        RefreshAllSortHeaderNodes();
    }

    private bool TryRevealFolderInGroupedView(FileEntry folder, out OrganizeNode? targetNode)
    {
        targetNode = null;
        if (!_classifySortActive || folder == null) return false;

        string targetPath = CleanListSnapshot.NormPath(folder.FullPath);
        if (targetPath.Length == 0) return false;

        bool ExpandNodePathToTarget(OrganizeNode rootRow, string groupKey, out OrganizeNode? landed)
        {
            landed = null;
            _classifyCollapsedGroups.Remove(groupKey);
            var cur = rootRow;
            cur.SetFlatFilterMode(0);

            for (int guard = 0; guard < 32; guard++)
            {
                if (OrganizePathIsSame(cur.FullPath, targetPath))
                {
                    ExpandRevealedTarget(cur);
                    SyncClassifyGroupedRowsInPlace();
                    landed = _organizeRows.Contains(cur) ? cur : null;
                    return landed != null;
                }

                if (!cur.ChildrenLoaded)
                    Materialize(cur, FolderOrganize.MaxMaterializePerNode, null, autoExpand: true);
                else
                    cur.Expand();
                EnsureDirectFilesMaterialized(cur);

                OrganizeNode? next = null;
                foreach (var c in cur.Children)
                {
                    if (!OrganizePathIsUnder(targetPath, c.FullPath)) continue;
                    next = c;
                    break;
                }
                if (next == null)
                {
                    SyncClassifyGroupedRowsInPlace();
                    landed = _organizeRows.Contains(cur) ? cur : null;
                    return landed != null;
                }
                cur = next;
            }
            return false;
        }

        // 1) 在彩色可移动组中按路径深度匹配（优先命中具体子项，若在库容器/父目录内部则自动在分组内逐层展开）
        ClassifyItem? bestItem = null;
        OrganizeNode? bestItemNode = null;
        int bestLen = -1;
        foreach (var ci in _classifyAllItems)
        {
            if (!_classifySettledIds.Contains(ci.Id)) continue;
            if (ci.IsKeepInPlace || ci.IsBlocked) continue;
            if (!_classifyNodesByItemId.TryGetValue(ci.Id, out var node)) continue;
            string itemPath = CleanListSnapshot.NormPath(ci.SourcePath);
            if (!OrganizePathIsSame(targetPath, itemPath) && !OrganizePathIsUnder(targetPath, itemPath)) continue;
            if (itemPath.Length > bestLen)
            {
                bestLen = itemPath.Length;
                bestItem = ci;
                bestItemNode = node;
            }
        }
        if (bestItem != null && bestItemNode != null)
            return ExpandNodePathToTarget(bestItemNode, EffectiveGroupKey(bestItem), out targetNode);

        // 2) 在盘上已存在的分类归档目录（如 D:\文档、D:\视频）中匹配或向下展开
        OrganizeNode? bestBucketNode = null;
        string? bestBucketKey = null;
        int bestBucketLen = -1;
        foreach (var kv in _classifyExistingBucketNodes)
        {
            foreach (var bNode in kv.Value)
            {
                string bPath = CleanListSnapshot.NormPath(bNode.FullPath);
                if (!OrganizePathIsSame(targetPath, bPath) && !OrganizePathIsUnder(targetPath, bPath)) continue;
                if (bPath.Length > bestBucketLen)
                {
                    bestBucketLen = bPath.Length;
                    bestBucketNode = bNode;
                    bestBucketKey = kv.Key;
                }
            }
        }
        if (bestBucketNode != null && bestBucketKey != null)
            return ExpandNodePathToTarget(bestBucketNode, bestBucketKey, out targetNode);

        // 3) 其次在下方原位保护清单里匹配或向下展开（若原位保护组折叠了也自动展开）
        OrganizeNode? bestOrig = null;
        int bestOrigLen = -1;
        foreach (var orig in _classifyOriginalOrder)
        {
            string oPath = CleanListSnapshot.NormPath(orig.FullPath);
            if (!OrganizePathIsSame(targetPath, oPath) && !OrganizePathIsUnder(targetPath, oPath)) continue;
            if (oPath.Length > bestOrigLen)
            {
                bestOrigLen = oPath.Length;
                bestOrig = orig;
            }
        }
        if (bestOrig != null)
            return ExpandNodePathToTarget(bestOrig, ClassifyCategoryRule.KeepId, out targetNode);

        return false;
    }

    // ==================== 退出 / 切换模式 / 精简底条与左侧入口同步 ====================

    private void ExitClassifySortMode(bool clearState)
    {
        _classifySortActive = false;
        _classifyAnimInFlight = false;
        _classifyKeepHeaderVisible = false;
        _sortFlyZIndex = 100;

        foreach (var hdr in _classifyGroupHeaders.Values)
            _organizeRows.Remove(hdr);
        _classifyGroupHeaders.Clear();
        _classifyNodesByItemId.Clear();
        _classifyParentAnchorByItemId.Clear();
        _classifySettledIds.Clear();
        _classifyOriginalOrder.Clear();
        _classifyExtraKeepNodes.Clear();
        _classifyExistingBucketNodes.Clear();
        _activeFlightEndTick.Clear();

        foreach (var n in _organizeAll)
        {
            n.ClearClassifyVisualState();
            n.SetFlatFilterMode(null);
        }

        if (!clearState)
        {
            RefreshOrganizeClassifyShortcutBar(isSortingNow: false);
            return;
        }

        _classifyHasAiPlan = false;
        _classifyCollapsedGroups.Clear();
        _classifyBannerMsg = "";
        ReplaceClassifyItems(new());
        SetClassifyBanner("");
        RefreshOrganizeClassifyShortcutBar(isSortingNow: false);
    }

    private void ResetClassifyStageOnScanChange()
    {
        CancelQuietly(_classifyCts);
        ExitClassifySortMode(clearState: true);
    }

    private (bool HasPlan, bool SortActive, List<ClassifyItem> Items, List<ClassifyCategoryRule> Categories, HashSet<string> CollapsedGroups) CaptureClassifyStateForCache()
    {
        bool hasPlan = _classifyHasAiPlan && _classifyBuiltForScan == _scanGeneration && _classifyAllItems.Count > 0;
        if (!hasPlan)
            return (false, false, new List<ClassifyItem>(), new List<ClassifyCategoryRule>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        return (
            true,
            _classifySortActive,
            new List<ClassifyItem>(_classifyAllItems),
            new List<ClassifyCategoryRule>(_classifyCachedCats),
            new HashSet<string>(_classifyCollapsedGroups, StringComparer.OrdinalIgnoreCase));
    }

    private void ApplyInstantColorGroupLayout()
    {
        RefreshClassifyCategoryMemo();
        PrepareInPlaceSortStage(_classifyAllItems);
        foreach (var item in _classifyAllItems)
        {
            _classifySettledIds.Add(item.Id);
            if (_classifyNodesByItemId.TryGetValue(item.Id, out var node))
                ApplyClassifyItemStyleToNode(node, item);
        }
        foreach (var kv in _classifyExistingBucketNodes)
        {
            foreach (var bNode in kv.Value)
                StyleExistingBucketNode(bNode, kv.Key);
        }
        foreach (var extra in _classifyExtraKeepNodes)
            StyleExtraKeepNode(extra);
        _classifyKeepHeaderVisible = true;
        var desired = BuildInPlaceProgressiveOrder(includeKeepHeader: true);
        ApplyMinimalCollectionDiff(_organizeRows, desired);
        RefreshAllSortHeaderNodes();
    }

    private void RestoreClassifyStateFromCache(
        bool hasPlan,
        bool sortActive,
        List<ClassifyItem> items,
        List<ClassifyCategoryRule> categories,
        HashSet<string>? collapsedGroups = null)
    {
        EnsureClassifyBindingsInitialized();
        CancelQuietly(_classifyCts);
        ExitClassifySortMode(clearState: false);
        _classifyCollapsedGroups.Clear();
        if (collapsedGroups != null)
        {
            foreach (var g in collapsedGroups)
                _classifyCollapsedGroups.Add(g);
        }

        if (!hasPlan || items == null || items.Count == 0)
        {
            ExitClassifySortMode(clearState: true);
            return;
        }

        _classifyCachedCats = categories != null && categories.Count > 0
            ? new List<ClassifyCategoryRule>(categories)
            : App.Settings.EffectiveClassifyCategories();
        _classifyCachedResolvedTargets = _root != null
            ? FolderClassificationService.ResolveCategoryTargetNames(_root, _classifyCachedCats)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ReplaceClassifyItems(new List<ClassifyItem>(items));
        _classifyHasAiPlan = true;
        _classifyBuiltForScan = _scanGeneration;
        _classifyBuiltForDrive = CurrentClassifyDriveRoot();

        if (sortActive)
            ApplyInstantColorGroupLayout();

        RefreshOrganizeClassifyShortcutBar(isSortingNow: false);
    }

    private void RefreshOrganizeClassifyShortcutBar(bool isSortingNow = false)
    {
        // O(1) 内存缓存读，零磁盘 I/O
        var latestUndo = _reorganizeService.LoadLatestUndoBatch();
        bool hasUndo = latestUndo != null && latestUndo.Entries.Count > 0;
        bool hasPlan = _classifyHasAiPlan && _classifyBuiltForScan == _scanGeneration && _classifyAllItems.Count > 0;

        int readyCount = 0;
        int movableTotal = 0;
        int junctionCount = 0;
        int crossDriveCount = 0;
        for (int i = 0; i < _classifyAllItems.Count; i++)
        {
            var x = _classifyAllItems[i];
            if (x.Executed) continue;
            if (!x.IsKeepInPlace) movableTotal++;
            if (x.IsChecked && x.CanCheck)
            {
                readyCount++;
                if (x.UseJunction) junctionCount++;
                if (x.IsCrossDrive) crossDriveCount++;
            }
        }

        string actionText = readyCount > 0
            ? $"整理归类 ({readyCount})"
            : (hasPlan ? "整理清单与规则" : (hasUndo ? $"撤销整理 ({latestUndo!.Entries.Count})" : "整理归类"));

        if (ClassifyActionBtn != null && ClassifyActionBtnText != null)
        {
            if (hasPlan || hasUndo)
            {
                ClassifyActionBtn.Visibility = Visibility.Visible;
                ClassifyActionBtn.IsEnabled = !_classifyBusy && !isSortingNow;
                ClassifyActionBtnText.Text = actionText;
            }
            else
            {
                ClassifyActionBtn.Visibility = Visibility.Collapsed;
            }
        }

        if (ClassifyReportActionBtn != null && ClassifyReportActionBtnText != null)
        {
            ClassifyReportActionBtn.IsEnabled = !_classifyBusy && !isSortingNow && (hasPlan || hasUndo);
            ClassifyReportActionBtnText.Text = actionText;
        }

        if (ClassifyUndoBtn != null)
        {
            ClassifyUndoBtn.Visibility = hasUndo ? Visibility.Visible : Visibility.Collapsed;
            ClassifyUndoBtn.IsEnabled = !_classifyBusy;
            if (hasUndo)
            {
                bool undoHasSpecial = latestUndo!.Entries.Any(e => e.CrossDrive || e.CreatedJunction);
                ClassifyUndoBtn.Content = undoHasSpecial ? "撤销上次迁移（含软链）" : "撤销上次整理";
            }
        }

        if (ClassifyModalExecuteBtn != null)
        {
            ClassifyModalExecuteBtn.IsEnabled = !_classifyBusy && readyCount > 0;
            ClassifyModalExecuteBtn.Content = readyCount <= 0
                ? "先在清单里勾选项"
                : (crossDriveCount > 0 && junctionCount > 0
                    ? $"确认跨盘软链搬家 ({readyCount})"
                    : (crossDriveCount > 0
                        ? $"确认跨盘搬家 ({readyCount})"
                        : (junctionCount > 0
                            ? $"确认整理并建软链 ({readyCount})"
                            : $"确认同盘整理 ({readyCount})")));
        }

        if (OrganizeClassifyBar == null) return;
        if (!_classifySortActive && !hasPlan && !hasUndo)
        {
            OrganizeClassifyBar.Visibility = Visibility.Collapsed;
            return;
        }

        OrganizeClassifyBar.Visibility = Visibility.Visible;
        if (ClassifySortModeChipBtn != null)
            ClassifySortModeChipBtn.Visibility = Visibility.Collapsed;
        if (OrganizeQuickUndoBtn != null)
        {
            OrganizeQuickUndoBtn.Visibility = hasUndo ? Visibility.Visible : Visibility.Collapsed;
            if (hasUndo) OrganizeQuickUndoBtn.Content = $"撤销 ({latestUndo!.Entries.Count})";
        }
        if (OrganizeClassifyBarText != null)
        {
            if (isSortingNow)
                OrganizeClassifyBarText.Text = $"正在识别目录并流畅聚拢（已识别 {movableTotal} 个可整理项）…";
            else if (_classifySortActive)
                OrganizeClassifyBarText.Text = readyCount > 0
                    ? $"已按颜色归拢 {movableTotal} 个目录 · 尚未移动文件，点左侧「整理归类」微调、跨盘或启用软链迁移"
                    : "已按颜色分组排列 · 注册表软件/游戏已就绪隐藏软链，点左侧「整理归类」按需勾选迁移";
            else if (hasPlan)
                OrganizeClassifyBarText.Text = $"已就绪 {movableTotal} 个整理建议，点左侧「整理归类」执行";
            else
                OrganizeClassifyBarText.Text = $"上次整理了 {latestUndo!.Entries.Count} 个文件夹，支持随时原路撤销";
        }
    }

    private async void OnClassifyItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClassifyItem.IsChecked)
            or nameof(ClassifyItem.CategoryId)
            or nameof(ClassifyItem.UseJunction)
            or nameof(ClassifyItem.TargetDriveRoot))
        {
            if (_classifyTargetDriveSyncing || _classifyStreamingInProgress) return;

            if (e.PropertyName is nameof(ClassifyItem.CategoryId) or nameof(ClassifyItem.UseJunction) or nameof(ClassifyItem.TargetDriveRoot))
                await FolderReorganizeService.PreflightAsync(_classifyAllItems);

            if (sender is ClassifyItem ci && _classifyNodesByItemId.TryGetValue(ci.Id, out var node))
            {
                ApplyClassifyItemStyleToNode(node, ci);
                if (_classifySortActive && e.PropertyName is nameof(ClassifyItem.CategoryId) or nameof(ClassifyItem.UseJunction))
                    await ApplyBatchSortWaveAsync(new[] { ci }, animate: CanAnimate, CancellationToken.None);
                else
                    RefreshAllSortHeaderNodes();
            }

            RefreshOrganizeClassifyShortcutBar();
            SaveActiveDriveToCache();
        }
    }

    private void SetClassifyBanner(string message, bool isError = false)
    {
        _classifyBannerMsg = message ?? "";
        _classifyBannerError = isError;
        if (ClassifyStatusBanner == null || ClassifyBannerText == null) return;

        if (_classifyBusy)
        {
            ClassifyStatusBanner.Visibility = Visibility.Visible;
            ClassifyStatusBanner.Background = ThemeService.Brush("BrandAccentSoft");
            ClassifyBannerText.Text = _classifyBannerMsg;
            if (ClassifyProgressBar != null) ClassifyProgressBar.Visibility = Visibility.Visible;
            if (ClassifyStatusActionBtn != null) ClassifyStatusActionBtn.Content = "取消迁移";
            return;
        }

        if (ClassifyStatusActionBtn != null) ClassifyStatusActionBtn.Content = "关闭";

        if (string.IsNullOrWhiteSpace(_classifyBannerMsg))
        {
            ClassifyStatusBanner.Visibility = Visibility.Collapsed;
            if (ClassifyProgressBar != null) ClassifyProgressBar.Visibility = Visibility.Collapsed;
            return;
        }

        ClassifyStatusBanner.Visibility = Visibility.Visible;
        ClassifyStatusBanner.Background = _classifyBannerError
            ? ThemeService.Brush("ToneRedBg")
            : ThemeService.Brush("BrandAccentSoft");
        ClassifyBannerText.Text = _classifyBannerMsg;
        if (ClassifyProgressBar != null) ClassifyProgressBar.Visibility = Visibility.Collapsed;
    }

    // ==================== 极简交互 + 居中整理确认弹层（同盘 + 跨盘 + 隐藏软链兜底） ====================

    private void ClassifySortModeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_classifyHasAiPlan || _classifyAllItems.Count == 0) return;

        if (_classifySortActive)
        {
            ExitClassifySortMode(clearState: false);
            RefreshOrganizeRows();
            SaveActiveDriveToCache();
            return;
        }

        // 仅首次点 AI 分析 / 重分析时播放慢赏排序动画；日常在颜色分组与大小排序之间秒切，不再重复播飞行动画
        ApplyInstantColorGroupLayout();
        RefreshOrganizeClassifyShortcutBar(isSortingNow: false);
        SaveActiveDriveToCache();
    }

    private async void ClassifyOpenModal_Click(object sender, RoutedEventArgs e)
    {
        EnsureClassifyBindingsInitialized();
        PopulateClassifyTargetDriveOptions();
        RefreshClassifyModalItems();
        RefreshOrganizeClassifyShortcutBar();
        if (ClassifyRuleEditorCard != null) ClassifyRuleEditorCard.Visibility = Visibility.Collapsed;

        OpenOverlay($"整理归类与跨盘软链搬家（当前 {CurrentClassifyDriveRoot()}）");
        if (ClassifyModalBody != null) ClassifyModalBody.Visibility = Visibility.Visible;
        DialogCard.Width = 820;
        DialogCard.MaxHeight = 720;
        DialogClose.Visibility = Visibility.Collapsed;
        ConfirmButtons.Visibility = Visibility.Collapsed;

        await FolderReorganizeService.PreflightAsync(_classifyAllItems);
        RefreshOrganizeClassifyShortcutBar();
    }

    private void PopulateClassifyTargetDriveOptions()
    {
        if (ClassifyTargetDriveBox == null) return;
        string curRoot = CurrentClassifyDriveRoot();
        string normCur = curRoot.TrimEnd('\\');

        long curFree = 0;
        try
        {
            if (!string.IsNullOrWhiteSpace(curRoot))
            {
                var curDi = new DriveInfo(curRoot);
                if (curDi.IsReady) curFree = curDi.AvailableFreeSpace;
            }
        }
        catch { }

        var options = new List<ClassifyDriveOption>
        {
            new("", string.IsNullOrEmpty(normCur) ? "同盘整理" : $"同盘整理 ({normCur})", true, curFree),
        };

        try
        {
            foreach (var di in DriveInfo.GetDrives())
            {
                if (!di.IsReady) continue;
                if (di.DriveType != DriveType.Fixed && di.DriveType != DriveType.Removable) continue;
                string nameNorm = di.Name.TrimEnd('\\');
                if (string.Equals(nameNorm, normCur, StringComparison.OrdinalIgnoreCase)) continue;
                options.Add(new ClassifyDriveOption(
                    di.Name,
                    $"跨盘迁至 {nameNorm}  (余 {FileEntry.FormatSize(di.AvailableFreeSpace)})",
                    false,
                    di.AvailableFreeSpace));
            }
        }
        catch { }

        // 若用户之前已为清单选定了目标跨盘，保持选中状态；否则默认回同盘整理
        string activeTarget = _classifyAllItems
            .Where(x => !x.Executed && !string.IsNullOrWhiteSpace(x.TargetDriveRoot))
            .Select(x => x.TargetDriveRoot)
            .FirstOrDefault() ?? "";

        if (!options.Any(o => string.Equals(o.DriveRoot, activeTarget, StringComparison.OrdinalIgnoreCase)))
            activeTarget = "";

        _classifyTargetDriveSyncing = true;
        try
        {
            ClassifyTargetDriveBox.ItemsSource = options;
            ClassifyTargetDriveBox.SelectedValue = activeTarget;
            bool isCross = !string.IsNullOrWhiteSpace(activeTarget);
            if (ClassifyCrossDriveJunctionChk != null)
                ClassifyCrossDriveJunctionChk.Visibility = isCross ? Visibility.Visible : Visibility.Collapsed;
            if (ClassifyJunctionHintText != null)
                ClassifyJunctionHintText.Visibility = isCross ? Visibility.Collapsed : Visibility.Visible;
        }
        finally
        {
            _classifyTargetDriveSyncing = false;
        }
    }

    private async void ClassifyTargetDrive_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_classifyTargetDriveSyncing || _classifyBusy) return;
        if (ClassifyTargetDriveBox?.SelectedItem is not ClassifyDriveOption opt) return;

        bool isCross = !opt.IsSameDrive;
        if (ClassifyCrossDriveJunctionChk != null)
            ClassifyCrossDriveJunctionChk.Visibility = isCross ? Visibility.Visible : Visibility.Collapsed;
        if (ClassifyJunctionHintText != null)
            ClassifyJunctionHintText.Visibility = isCross ? Visibility.Collapsed : Visibility.Visible;

        bool crossJunctionDefault = ClassifyCrossDriveJunctionChk?.IsChecked ?? true;

        _classifyTargetDriveSyncing = true;
        try
        {
            foreach (var item in _classifyAllItems)
            {
                if (item.Executed) continue;
                item.TargetDriveRoot = opt.IsSameDrive ? "" : opt.DriveRoot;

                // 非保守红线项：跨盘时跟随「原位留隐藏软链」开关；切回同盘时自动恢复普通原子搬移
                // 已被用户手动解锁软链的注册表软件（BlockedConservative）保持其 UseJunction 勾选不被意外冲掉
                if (item.Tier != ClassifySafetyTier.BlockedConservative)
                {
                    item.UseJunction = isCross && crossJunctionDefault && !item.IsKeepInPlace;
                }
            }
        }
        finally
        {
            _classifyTargetDriveSyncing = false;
        }

        await FolderReorganizeService.PreflightAsync(_classifyAllItems);
        foreach (var item in _classifyAllItems)
        {
            if (_classifyNodesByItemId.TryGetValue(item.Id, out var node))
                ApplyClassifyItemStyleToNode(node, item);
        }
        RefreshAllSortHeaderNodes();
        RefreshOrganizeClassifyShortcutBar();
        SaveActiveDriveToCache();
    }

    private async void ClassifyCrossDriveJunctionChk_Click(object sender, RoutedEventArgs e)
    {
        if (_classifyBusy) return;
        if (ClassifyTargetDriveBox?.SelectedItem is not ClassifyDriveOption opt || opt.IsSameDrive) return;
        bool wantJunction = ClassifyCrossDriveJunctionChk?.IsChecked ?? true;

        _classifyTargetDriveSyncing = true;
        try
        {
            foreach (var item in _classifyAllItems)
            {
                if (item.Executed || item.Tier == ClassifySafetyTier.BlockedConservative) continue;
                if (!item.IsKeepInPlace)
                    item.UseJunction = wantJunction;
            }
        }
        finally
        {
            _classifyTargetDriveSyncing = false;
        }

        await FolderReorganizeService.PreflightAsync(_classifyAllItems);
        RefreshOrganizeClassifyShortcutBar();
        SaveActiveDriveToCache();
    }

    private async void ClassifyToggleJunction_Click(object sender, RoutedEventArgs e)
    {
        if (_classifyBusy) return;
        if ((sender as FrameworkElement)?.Tag is not ClassifyItem item) return;
        if (item.Executed || !item.SupportsJunctionMigration) return;

        bool enable = !item.UseJunction;
        if (enable && !JunctionNative.IsNtfsVolume(item.DriveRoot))
        {
            SetClassifyBanner($"原磁盘 {item.DriveRoot.TrimEnd('\\')} 不是 NTFS 文件系统，无法创建隐藏软链。", isError: true);
            return;
        }

        _classifyTargetDriveSyncing = true;
        try
        {
            if (enable)
            {
                item.UseJunction = true;
                if (item.IsKeepInPlace)
                {
                    var cats = _classifyCachedCats.Count > 0 ? _classifyCachedCats : App.Settings.EffectiveClassifyCategories();
                    string suggested = FolderClassificationService.SuggestJunctionCategoryId(item, cats);
                    if (!string.Equals(suggested, ClassifyCategoryRule.KeepId, StringComparison.OrdinalIgnoreCase))
                        item.CategoryId = suggested;
                }
            }
            else
            {
                item.UseJunction = false;
                if (item.Tier == ClassifySafetyTier.BlockedConservative)
                {
                    item.CategoryId = ClassifyCategoryRule.KeepId;
                    item.IsChecked = false;
                }
            }
        }
        finally
        {
            _classifyTargetDriveSyncing = false;
        }

        await FolderReorganizeService.PreflightAsync(_classifyAllItems);
        if (enable && item.CanCheck)
            item.IsChecked = true;

        if (_classifyNodesByItemId.TryGetValue(item.Id, out var node))
        {
            ApplyClassifyItemStyleToNode(node, item);
            if (_classifySortActive)
                await ApplyBatchSortWaveAsync(new[] { item }, animate: CanAnimate, CancellationToken.None);
            else
                RefreshAllSortHeaderNodes();
        }

        RefreshClassifyModalItems();
        RefreshOrganizeClassifyShortcutBar();
        SaveActiveDriveToCache();
    }

    private void ClassifyStatusClose_Click(object sender, RoutedEventArgs e)
    {
        if (_classifyBusy)
        {
            try { _classifyExecCts?.Cancel(); } catch { }
            if (ClassifyBannerText != null)
                ClassifyBannerText.Text = "正在安全取消剩余迁移并清理临时文件…";
            return;
        }
        SetClassifyBanner("");
    }

    private void ClassifyCustomToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ClassifyRuleEditorCard == null) return;
        bool nextVisible = ClassifyRuleEditorCard.Visibility != Visibility.Visible;
        if (nextVisible) LoadClassifyRuleDrafts();
        ClassifyRuleEditorCard.Visibility = nextVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClassifyRuleAdd_Click(object sender, RoutedEventArgs e)
    {
        _classifyRuleDrafts.Add(new ClassifyRuleDraft
        {
            Id = "custom-" + Guid.NewGuid().ToString("N")[..6],
            Enabled = true,
            Name = "AI模型/素材",
            FolderName = "AIModels",
            Description = "safetensors / gguf / LoRA / 模型权重",
        });
    }

    private void ClassifyRuleRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ClassifyRuleDraft draft) return;
        if (_classifyRuleDrafts.Count <= 1)
        {
            MessageBox.Show(this, "至少保留一条分类规则。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _classifyRuleDrafts.Remove(draft);
    }

    private void ClassifyRuleReset_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.ClassifyCategories = ClassifyCategoryRule.DefaultPresets();
        App.Settings.Save();
        LoadClassifyRuleDrafts();
        SetClassifyBanner("已恢复默认分类规则，点「保存并重排」可立即按新规则归类。");
    }

    private void ClassifyRuleBrowse_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ClassifyRuleDraft draft) return;
        string targetDrive = (ClassifyTargetDriveBox?.SelectedItem is ClassifyDriveOption opt && !opt.IsSameDrive)
            ? opt.DriveRoot
            : CurrentClassifyDriveRoot();
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"为「{draft.Name}」选择落地文件夹（{targetDrive.TrimEnd('\\')}）",
            InitialDirectory = Directory.Exists(targetDrive) ? targetDrive : Environment.GetFolderPath(Environment.SpecialFolder.MyComputer),
        };
        if (dlg.ShowDialog(this) != true) return;
        string picked = dlg.FolderName.TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(picked)) return;

        if (!string.IsNullOrEmpty(targetDrive))
        {
            string normDrive = targetDrive.TrimEnd('\\') + "\\";
            if (!picked.StartsWith(normDrive, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, $"请选择当前目标磁盘（{targetDrive.TrimEnd('\\')}）内的文件夹；如需跨盘请先在顶部切换「目标磁盘」。",
                    "目录盘符不匹配", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string rel = picked[normDrive.Length..].Trim('\\');
            if (rel.Length > 0) draft.FolderName = rel;
        }
        else
        {
            draft.FolderName = Path.GetFileName(picked);
        }
    }

    private async void ClassifyRuleSave_Click(object sender, RoutedEventArgs e)
    {
        var saved = _classifyRuleDrafts
            .Select(d => d.ToRule())
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && !string.IsNullOrWhiteSpace(r.FolderName))
            .ToList();
        if (saved.Count == 0)
        {
            MessageBox.Show(this, "至少保留一条有效分类规则。", "规则不能为空", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        App.Settings.ClassifyCategories = saved;
        App.Settings.Save();
        if (ClassifyRuleEditorCard != null) ClassifyRuleEditorCard.Visibility = Visibility.Collapsed;

        if (_root != null)
        {
            var model = AiConfigured() ? CreateActiveDecisionModel() : null;
            if (model != null)
                await RunUnifiedClassifyStreamingAsync(model, _root, CancellationToken.None);
            else
                await RebuildLocalCandidatesAndSortInPlaceAsync();
            RefreshClassifyModalItems();
        }
    }

    private IChatModel? CreateActiveDecisionModel()
    {
        var provider = App.Settings.DecisionProvider();
        var modelId = App.Settings.DecisionModelId();
        if (provider == null || string.IsNullOrWhiteSpace(modelId)) return null;
        return ChatModelFactory.Create(provider.BaseUrl, modelId, provider.ApiKey);
    }

    private async Task RebuildLocalCandidatesAndSortInPlaceAsync()
    {
        if (_root == null) return;
        RefreshClassifyCategoryMemo();
        var cats = _classifyCachedCats;
        bool includeDl = ClassifyIncludeDownloadsChk?.IsChecked ?? App.Settings.ClassifyIncludeDownloadsChildren;
        var context = FolderClassificationService.BuildContext(
            _folderPurpose.Evidence, _apps, App.Settings.ClassifyIgnoredPaths);
        if (_apps.Count == 0)
        {
            _classifyFastInstalled ??= await Task.Run(FolderClassificationService.ReadInstalledLocationsFast);
            foreach (var kv in _classifyFastInstalled)
                context.InstalledLocations.TryAdd(kv.Key, kv.Value);
        }

        var built = await Task.Run(() =>
            FolderClassificationService.BuildCandidates(_root, cats, context, includeDl, _classifyPinnedDirs));
        EnableAutoJunctionForEligibleCandidates(built, cats);
        await FolderReorganizeService.PreflightAsync(built);

        _classifyStreamingInProgress = true;
        try
        {
            ReplaceClassifyItems(built);
            _classifyHasAiPlan = true;
            _classifyBuiltForScan = _scanGeneration;
            _classifyBuiltForDrive = CurrentClassifyDriveRoot();
            PrepareInPlaceSortStage(built);
            var movables = built
                .Where(x => !x.IsKeepInPlace && !x.IsBlocked)
                .ToList();
            if (movables.Count > 0)
                await RelayGlideMovableItemsAsync(movables, animate: CanAnimate, relayMs: 130, glideMs: 500, CancellationToken.None);
            if (CanAnimate)
                await WaitForActiveFlightsAsync(CancellationToken.None);
            await RevealKeepSectionHeaderSmoothAsync(animate: CanAnimate, CancellationToken.None);
            RefreshOrganizeClassifyShortcutBar();
        }
        finally
        {
            _classifyStreamingInProgress = false;
        }
    }

    private async void ClassifyOptionsChanged_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.ClassifyIncludeDownloadsChildren = ClassifyIncludeDownloadsChk?.IsChecked ?? true;
        App.Settings.ClassifyUpdateShortcuts = ClassifyUpdateShortcutsChk?.IsChecked ?? true;
        App.Settings.Save();
        if (_classifySortActive)
        {
            await RebuildLocalCandidatesAndSortInPlaceAsync();
            RefreshClassifyModalItems();
        }
    }

    private async void ClassifyExecute_Click(object sender, RoutedEventArgs e)
    {
        if (_classifyBusy) return;
        await FolderReorganizeService.PreflightAsync(_classifyAllItems);

        var targets = _classifyAllItems
            .Where(x => x.IsChecked && x.CanCheck && !x.Executed && !x.IsKeepInPlace)
            .ToList();
        if (targets.Count == 0)
        {
            SetClassifyBanner("请先在清单里勾选至少一个可整理目录。");
            return;
        }

        int junctionCount = targets.Count(x => x.UseJunction);
        int crossDriveCount = targets.Count(x => x.IsCrossDrive);
        long totalBytes = targets.Sum(x => Math.Max(0, x.SizeBytes));

        static string ModeTag(ClassifyItem x) => (x.IsCrossDrive, x.UseJunction) switch
        {
            (true, true) => "跨盘 + 隐藏软链",
            (true, false) => "跨盘搬家",
            (false, true) => "同盘 + 隐藏软链",
            _ => "同盘原子归位",
        };

        var previewLines = targets.Take(8)
            .Select(x => $"• [{ModeTag(x)}] {x.SourceName} ({x.SizeText})  →  {x.DestinationPath}")
            .ToList();
        string extra = targets.Count > 8 ? $"\n…其余 {targets.Count - 8} 个文件夹也将按设定迁移" : "";

        var notes = new List<string>();
        if (junctionCount > 0)
        {
            notes.Add($"• 其中 {junctionCount} 个目录启用了「NTFS 隐藏软链」：迁出后会在原路径自动创建隐藏软链，原有注册表、游戏启动器与快捷方式零失效；迁移前会自动检查运行中进程，请先退出相关软件。");
        }
        if (crossDriveCount > 0)
        {
            string dstRoot = targets.First(x => x.IsCrossDrive).EffectiveTargetDriveRoot;
            string spaceHint = "";
            try
            {
                var di = new DriveInfo(dstRoot);
                if (di.IsReady)
                    spaceHint = $"（目标盘 {dstRoot.TrimEnd('\\')} 当前可用 {FileEntry.FormatSize(di.AvailableFreeSpace)}）";
            }
            catch { }
            notes.Add($"• 其中 {crossDriveCount} 个目录为跨盘搬家（共约 {FileEntry.FormatSize(totalBytes)}）{spaceHint}：将先复制到目标盘临时区，严格核对文件数与总字节数无误后再删除源目录释放空间，可随时点击「取消迁移」。");
        }
        int greenNoJunction = targets.Count(x => x.Tier == ClassifySafetyTier.ReviewGreenApp && !x.UseJunction);
        if (greenNoJunction > 0)
        {
            notes.Add($"• 其中含 {greenNoJunction} 个绿软/可执行目录，移动后将尽量自动修复桌面上关联的快捷方式。");
        }

        string title = crossDriveCount > 0
            ? (junctionCount > 0 ? "确认跨盘软链搬家" : "确认跨盘搬家")
            : (junctionCount > 0 ? "确认整理并建隐藏软链" : "确认同盘整理");
        string lead = crossDriveCount > 0
            ? $"确认按以下方案迁移 {targets.Count} 个文件夹（共 {FileEntry.FormatSize(totalBytes)}）？\n全程记录事务日志，支持随时一键原路撤销。"
            : $"确认在当前磁盘（{CurrentClassifyDriveRoot()}）内整理以下 {targets.Count} 个文件夹？\n同盘原子移动毫秒级完成，且支持随时一键撤销。";

        string confirmText = lead + "\n\n" + string.Join("\n", previewLines) + extra
            + (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : "");

        if (MessageBox.Show(this, confirmText, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes)
        {
            return;
        }

        bool updateShortcuts = ClassifyUpdateShortcutsChk?.IsChecked ?? App.Settings.ClassifyUpdateShortcuts;
        string driveRoot = CurrentClassifyDriveRoot();

        CancelQuietly(_classifyExecCts);
        _classifyExecCts = new CancellationTokenSource();
        var ct = _classifyExecCts.Token;

        _classifyBusy = true;
        RefreshOrganizeClassifyShortcutBar();
        SetClassifyBanner(crossDriveCount > 0
            ? $"正在跨盘迁移 {targets.Count} 个文件夹（实时校验中）…"
            : $"正在整理 {targets.Count} 个文件夹…");

        var progress = new Progress<ReorganizeProgress>(p =>
        {
            if (_classifyBusy)
                SetClassifyBanner($"[{p.Completed}/{p.Total}] {p.CurrentName}");
        });

        try
        {
            var result = await _reorganizeService.ExecuteAsync(
                _classifyAllItems, updateShortcuts, driveRoot, progress: progress, ct: ct);

            foreach (var item in _classifyAllItems)
            {
                if (_classifyNodesByItemId.TryGetValue(item.Id, out var node))
                    ApplyClassifyItemStyleToNode(node, item);
            }
            RefreshAllSortHeaderNodes();
            RefreshClassifyModalItems();

            if (result.MovedCount > 0)
            {
                var parts = new List<string>
                {
                    $"已成功迁移 {result.MovedCount} 个文件夹（{FileEntry.FormatSize(result.MovedBytes)}）"
                };
                if (result.CrossDriveCount > 0) parts.Add($"跨盘释放 {result.CrossDriveCount} 项");
                if (result.JunctionsCreated > 0) parts.Add($"原位创建 {result.JunctionsCreated} 个隐藏软链");
                if (result.ShortcutsUpdated > 0) parts.Add($"更新 {result.ShortcutsUpdated} 个快捷方式");
                if (result.Errors.Count > 0) parts.Add($"部分跳过：{result.Errors[0].Message}");
                string summary = string.Join("，", parts);

                _classifyBusy = false;
                SetClassifyBanner(summary, isError: result.Errors.Count > 0);
                RefreshOrganizeClassifyShortcutBar();
                SetOrganizeNote(summary);
                HideOverlay();
                _ = RunScanCoreAsync(silentRefresh: true);
            }
            else
            {
                _classifyBusy = false;
                SetClassifyBanner("未能移动任何目录：" + (result.Errors.FirstOrDefault()?.Message ?? "已被程序占用或存在冲突"), isError: true);
            }
        }
        catch (OperationCanceledException)
        {
            _classifyBusy = false;
            RefreshClassifyModalItems();
            SetClassifyBanner("已安全取消剩余迁移并清理临时文件；已完成的项目仍可随时一键撤销。");
        }
        catch (Exception ex)
        {
            _classifyBusy = false;
            AppLog.Record("Classify", ex, "execute reorganize");
            SetClassifyBanner("执行整理时出现异常：" + ex.Message, isError: true);
        }
        finally
        {
            _classifyBusy = false;
            CancelQuietly(_classifyExecCts);
            _classifyExecCts = null;
            RefreshOrganizeClassifyShortcutBar();
            SaveActiveDriveToCache();
        }
    }

    private async void ClassifyUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_classifyBusy) return;
        var batch = _reorganizeService.LoadLatestUndoBatch();
        if (batch == null || batch.Entries.Count == 0)
        {
            SetClassifyBanner("暂无可撤销的整理记录。");
            RefreshOrganizeClassifyShortcutBar();
            return;
        }

        int junctionCnt = batch.Entries.Count(x => x.CreatedJunction);
        int crossCnt = batch.Entries.Count(x => x.CrossDrive);
        string detail = "";
        if (crossCnt > 0 || junctionCnt > 0)
        {
            var dp = new List<string>();
            if (junctionCnt > 0) dp.Add($"移除 {junctionCnt} 个隐藏软链");
            if (crossCnt > 0) dp.Add($"把 {crossCnt} 个跨盘目录校验复制回原盘");
            detail = "\n本次撤销将：" + string.Join("，并", dp) + "（执行前会检测程序占用，请先退出相关软件）。";
        }

        if (MessageBox.Show(
                this,
                $"确定把上次迁移的 {batch.Entries.Count} 个文件夹原路退回原来的位置吗？{detail}",
                "撤销上次整理",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        CancelQuietly(_classifyExecCts);
        _classifyExecCts = new CancellationTokenSource();
        var ct = _classifyExecCts.Token;

        _classifyBusy = true;
        RefreshOrganizeClassifyShortcutBar();
        SetClassifyBanner($"正在还原 {batch.Entries.Count} 个文件夹到原路径…");

        var progress = new Progress<ReorganizeProgress>(p =>
        {
            if (_classifyBusy)
                SetClassifyBanner($"[{p.Completed}/{p.Total}] {p.CurrentName}");
        });

        try
        {
            var undoRes = await _reorganizeService.UndoLatestAsync(progress: progress, ct: ct);
            if (undoRes.RestoredCount > 0)
            {
                ExitClassifySortMode(clearState: true);
                var parts = new List<string> { $"已原路退回 {undoRes.RestoredCount} 个文件夹" };
                if (undoRes.JunctionsRemoved > 0) parts.Add($"移除 {undoRes.JunctionsRemoved} 个隐藏软链");
                if (undoRes.ShortcutsRestored > 0) parts.Add($"还原 {undoRes.ShortcutsRestored} 个快捷方式");
                if (undoRes.Errors.Count > 0) parts.Add($"部分失败：{undoRes.Errors[0].Message}");
                string msg = string.Join("，", parts);

                _classifyBusy = false;
                SetClassifyBanner(msg, isError: undoRes.Errors.Count > 0);
                SetOrganizeNote(msg);
                RefreshOrganizeClassifyShortcutBar();
                if (OverlayRoot.Visibility == Visibility.Visible) HideOverlay();
                _ = RunScanCoreAsync(silentRefresh: true);
            }
            else
            {
                _classifyBusy = false;
                SetClassifyBanner(undoRes.Errors.FirstOrDefault()?.Message ?? "未能还原任何文件夹，请确认未被程序占用。", isError: true);
            }
        }
        catch (OperationCanceledException)
        {
            _classifyBusy = false;
            SetClassifyBanner("已安全取消剩余撤销操作；尚未退回的记录已保留在撤销清单中。");
        }
        catch (Exception ex)
        {
            _classifyBusy = false;
            AppLog.Record("Classify", ex, "undo reorganize");
            SetClassifyBanner("撤销过程中遇到错误：" + ex.Message, isError: true);
        }
        finally
        {
            _classifyBusy = false;
            CancelQuietly(_classifyExecCts);
            _classifyExecCts = null;
            RefreshOrganizeClassifyShortcutBar();
            SaveActiveDriveToCache();
        }
    }

    /// <summary>目录右键把深层文件夹也纳入同表分类序列。</summary>
    private async void OrganizeSendToClassify_Click(object sender, RoutedEventArgs e)
    {
        var node = _organizeMenuNode ?? OrganizeGrid?.SelectedItem as OrganizeNode;
        if (node == null || node.IsClassifyGroupHeader || !node.Dir.IsDirectory) return;

        string driveRoot = CurrentClassifyDriveRoot();
        string? nodeDrive = Path.GetPathRoot(node.FullPath);
        if (!string.IsNullOrEmpty(driveRoot) && !string.Equals(
                driveRoot.TrimEnd('\\'), (nodeDrive ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            SetOrganizeNote("请先切换到该目录所在盘符后再纳入整理归类。");
            return;
        }

        if (!_classifyPinnedDirs.Any(d => string.Equals(d.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase)))
            _classifyPinnedDirs.Add(node.Dir);

        if (_root == null) return;
        var model = AiConfigured() ? CreateActiveDecisionModel() : null;
        if (model != null)
        {
            SetRadialAiRotating(true);
            try
            {
                await RunUnifiedClassifyStreamingAsync(model, _root, CancellationToken.None);
            }
            finally
            {
                SetRadialAiRotating(false);
            }
        }
        else
        {
            await RebuildLocalCandidatesAndSortInPlaceAsync();
        }
    }
}
