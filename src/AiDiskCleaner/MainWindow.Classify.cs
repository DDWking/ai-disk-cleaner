using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.Agent;
using Path = System.IO.Path;

namespace AiDiskCleaner;

/// <summary>
/// 「软件内虚拟分类视图」· 60fps 原位逐个接力排序引擎 + 本地持久化缓存：
/// 100% 纯只读可视化分组（绝不移动、软链或改动磁盘上任何真实文件与注册表）：
/// 1) 点击 AI 分析后保持右侧原有容量顺序不动，左侧亮起四段式呼吸流光弧；
/// 2) 每判出一个目录：先原位轻亮分类色条并触发左环同色共振脉冲，再以 1200ms 弹性缓动逐个接力飞升至顶部对应色组；
/// 3) 自动持久化到本地缓存（按盘符独立存储），下次打开软件或切盘直接恢复上次分好的颜色分组与折叠状态，省 AI 额度。
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
    private readonly FolderClassificationService _classifyService = new();

    private readonly Dictionary<string, ClassifyItem> _classifyItemsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrganizeNode> _classifyNodesByItemId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrganizeNode> _classifyParentAnchorByItemId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrganizeNode> _classifyGroupHeaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _classifyCollapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _classifySettledIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OrganizeNode> _classifyOriginalOrder = new();
    private readonly List<OrganizeNode> _classifyExtraKeepNodes = new();
    private readonly Dictionary<OrganizeNode, long> _activeFlightEndTick = new(ReferenceEqualityComparer.Instance);

    private List<ClassifyCategoryRule> _classifyCachedCats = new();

    private CancellationTokenSource? _classifyCts;
    private bool _classifySortActive;
    private bool _classifyAnimInFlight;
    private bool _classifyHasAiPlan;
    private bool _classifyKeepHeaderVisible;
    private int _sortFlyZIndex = 100;
    private int _classifyBuiltForScan = -1;
    private string _classifyBuiltForDrive = "";
    private string _classifyAiWarning = "";
    private DateTime? _classifyRestoredFromDiskAtUtc;
    private Dictionary<string, string>? _classifyFastInstalled;

    private void RefreshClassifyCategoryMemo()
    {
        _classifyCachedCats = App.Settings.EffectiveClassifyCategories();
    }

    private void OnScanCompletedRefreshClassify()
    {
        if (_root == null)
        {
            ResetClassifyStageOnScanChange();
            return;
        }
        string drive = CurrentClassifyDriveRoot();
        _ = TryRestorePersistedVirtualClassifyAsync(_root, _scanGeneration, drive);
    }

    private void OnAppsLoadedRefreshClassify()
    {
        // 软件内虚拟分类不涉及物理搬移，不需要在卸载清单加载后重锁目录。
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
        foreach (var kNode in _classifyExtraKeepNodes)
            StyleExtraKeepNode(kNode);
        RefreshAllSortHeaderNodes();
    }

    private static void StyleExtraKeepNode(OrganizeNode kNode)
    {
        bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;
        var keepTheme = ClassifyCategoryPalette.KeepTheme;
        string reason = kNode.IsSystemEntry
            ? "系统入口 · 原位展示"
            : (!string.IsNullOrWhiteSpace(kNode.PurposeText) ? kNode.PurposeText : "系统或根级目录");
        kNode.ApplyClassifyVisualState(
            "", ClassifyCategoryRule.KeepId,
            keepTheme.AccentHex, keepTheme.RowWashHex(isDark), keepTheme.PillWashHex(isDark),
            "系统核心", reason, "", false, false);
    }

    private string CurrentClassifyDriveRoot()
    {
        if (_root != null && !string.IsNullOrWhiteSpace(_root.FullPath))
            return Path.GetPathRoot(_root.FullPath) ?? _root.FullPath;
        return DriveBox?.SelectedItem?.ToString() ?? "";
    }

    // ==================== 本地持久化虚拟分类自动恢复（重启/切盘零 AI 消耗） ====================

    private async Task<bool> TryRestorePersistedVirtualClassifyAsync(FileEntry root, int expectedScanGen, string driveKey)
    {
        if (root == null || string.IsNullOrWhiteSpace(driveKey)) return false;
        if (_agentBusy || _classifySortActive || _classifyHasAiPlan) return false;

        var snap = await Task.Run(() => VirtualClassifyCache.TryLoad(driveKey));
        if (snap == null || snap.Entries.Count == 0) return false;

        // 切盘或开启新扫描/AI 分析后放弃旧回调
        if (expectedScanGen != _scanGeneration || _agentBusy || _classifySortActive || _classifyHasAiPlan)
            return false;
        if (!string.Equals(NormalizeDriveKey(CurrentClassifyDriveRoot()), NormalizeDriveKey(driveKey), StringComparison.OrdinalIgnoreCase))
            return false;

        var cats = snap.Categories is { Count: > 0 }
            ? snap.Categories
            : App.Settings.EffectiveClassifyCategories();
        bool includeDl = App.Settings.ClassifyIncludeDownloadsChildren;

        var context = FolderClassificationService.BuildContext(
            _folderPurpose.Evidence, _apps, App.Settings.ClassifyIgnoredPaths);
        if (_apps.Count == 0)
        {
            _classifyFastInstalled ??= await Task.Run(FolderClassificationService.ReadInstalledLocationsFast);
            foreach (var kv in _classifyFastInstalled)
                context.InstalledLocations.TryAdd(kv.Key, kv.Value);
        }

        var built = await Task.Run(() =>
            FolderClassificationService.BuildCandidates(root, cats, context, includeDl));

        if (expectedScanGen != _scanGeneration || _agentBusy || _classifySortActive)
            return false;

        var resolvedTargets = FolderClassificationService.ResolveCategoryTargetNames(root, cats);
        var choices = FolderClassificationService.BuildChoices(cats, resolvedTargets);

        int matched = 0;
        foreach (var item in built)
        {
            string norm = CleanListSnapshot.NormPath(item.SourcePath);
            if (!snap.Entries.TryGetValue(norm, out var saved)) continue;
            matched++;
            if (!item.IsBlocked)
                FolderClassificationService.ApplyChoice(item, saved.CategoryId, choices);
            if (!string.IsNullOrWhiteSpace(saved.AiReason))
                item.AiReason = saved.AiReason;
            item.FromAi = saved.FromAi;
        }

        if (matched == 0) return false;

        _classifyCachedCats = cats;
        _classifyAiWarning = "";
        _classifyRestoredFromDiskAtUtc = snap.SavedAtUtc;
        _classifyCollapsedGroups.Clear();
        foreach (var g in snap.CollapsedGroups)
            _classifyCollapsedGroups.Add(g);

        ReplaceClassifyItems(built);
        _classifyHasAiPlan = true;
        _classifyBuiltForScan = _scanGeneration;
        _classifyBuiltForDrive = CurrentClassifyDriveRoot();

        ApplyInstantColorGroupLayout();
        RefreshOrganizeClassifyShortcutBar(isSortingNow: false);
        SaveActiveDriveToCache();
        return true;
    }

    // ==================== 流式 AI 虚拟分类与 60fps 原位逐个接力排序引擎 ====================

    private async Task<UnifiedClassifyOutcome> RunUnifiedClassifyStreamingAsync(
        IChatModel model, FileEntry root, CancellationToken ct)
    {
        CancelStartupChoreographyAndFlush();
        CancelQuietly(_classifyCts);
        var sortCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _classifyCts = sortCts;
        _classifyAiWarning = "";
        _classifyRestoredFromDiskAtUtc = null;

        try
        {
            RefreshClassifyCategoryMemo();
            var cats = _classifyCachedCats;
            bool includeDl = App.Settings.ClassifyIncludeDownloadsChildren;
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
                FolderClassificationService.BuildCandidates(root, cats, context, includeDl), sortCts.Token);

            bool hasContent = built.Any(x => !x.IsBlocked);

            ReplaceClassifyItems(built);
            _classifyHasAiPlan = true;
            _classifyBuiltForScan = _scanGeneration;
            _classifyBuiltForDrive = CurrentClassifyDriveRoot();

            if (built.Count == 0)
                return new UnifiedClassifyOutcome(true, false, 0, "", built, cats);

            // 保持原大小排序列表纹丝不动，让每个彩色目录拥有真实可见的飞升轨迹
            _classifyCollapsedGroups.Clear();
            PrepareInPlaceSortStage(built);
            RefreshOrganizeClassifyShortcutBar(isSortingNow: true);

            // Wave 0：本地高确定性命中的彩色目录立刻逐个接力起飞
            var wave0Movables = built
                .Where(x => !x.IsKeepInPlace && !x.IsBlocked)
                .OrderByDescending(x => x.SizeBytes)
                .ToList();
            if (wave0Movables.Count > 0)
            {
                await RelayGlideMovableItemsAsync(
                    wave0Movables, animate: CanAnimate, relayMs: 360, glideMs: 1200, sortCts.Token);
            }

            // Waves 1..N：AI 每批 8 个目录流式返回后，彩色目录逐个接力飞升（下一批请求并行在飞）
            var aiRes = await _classifyService.ClassifyWithAiAsync(
                model,
                built,
                cats,
                fullPaths,
                progress: new Progress<ClassifyProgress>(p =>
                {
                    if (OrganizeClassifyBarText != null && p.Phase == "calling")
                    {
                        OrganizeClassifyBarText.Foreground = ThemeService.Brush("TextDim");
                        OrganizeClassifyBarText.Text =
                            $"正在深度解读目录并逐个按色归类（第 {Math.Min(p.Completed + 1, p.Total)}/{p.Total} 批）…";
                    }
                }),
                ct: sortCts.Token,
                onBatchReady: async slice =>
                {
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

            // 补齐任何尚未起飞的已归类目录，确保 100% 飞入对应色组
            var remainingMovables = built
                .Where(x => !x.IsKeepInPlace && !x.IsBlocked && !_classifySettledIds.Contains(x.Id))
                .ToList();
            if (remainingMovables.Count > 0)
            {
                await RelayGlideMovableItemsAsync(
                    remainingMovables, animate: CanAnimate, relayMs: 260, glideMs: 1100, sortCts.Token);
            }

            await WaitForActiveFlightsAsync(sortCts.Token);
            await RevealKeepSectionHeaderSmoothAsync(animate: CanAnimate, sortCts.Token);

            string driveRoot = CurrentClassifyDriveRoot();
            if (!aiRes.Ok && !string.IsNullOrWhiteSpace(aiRes.Error))
            {
                bool isBalance = aiRes.Error.Contains("HTTP 402", StringComparison.OrdinalIgnoreCase)
                    || aiRes.Error.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase)
                    || aiRes.Error.Contains("余额", StringComparison.Ordinal)
                    || aiRes.Error.Contains("额度不足", StringComparison.Ordinal);
                _classifyAiWarning = isBalance
                    ? "⚠ AI 账户余额不足，右侧当前仅展示本地规则初步分类（不改动磁盘文件；点左侧设置充值或换模型）"
                    : "⚠ AI 连接异常，右侧当前仅展示本地规则初步分类（详见左侧提示面板）";
            }
            else
            {
                _classifyAiWarning = "";
            }

            // 持久化到本地：AI 成功时无条件刷新；AI 失败时仅在本地尚无旧 AI 缓存时才保存离线初筛结果
            var itemsSnap = new List<ClassifyItem>(built);
            var catsSnap = new List<ClassifyCategoryRule>(cats);
            var collapsedSnap = new List<string>(_classifyCollapsedGroups);
            bool aiOk = aiRes.Ok;
            _ = Task.Run(() =>
            {
                if (aiOk || VirtualClassifyCache.TryLoad(driveRoot)?.HasAiResults != true)
                    VirtualClassifyCache.Save(driveRoot, itemsSnap, catsSnap, collapsedSnap, hasAiResults: aiOk);
            });

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
    }

    /// <summary>供离线回归测试或回放直接展开同表彩色虚拟分类状态。</summary>
    private async Task PresentClassifySortPreviewAsync(
        UnifiedClassifyOutcome outcome, bool animateSteps, CancellationToken ct = default)
    {
        RefreshClassifyCategoryMemo();
        ReplaceClassifyItems(outcome.Items);
        _classifyHasAiPlan = true;
        _classifyBuiltForScan = _scanGeneration;
        _classifyBuiltForDrive = CurrentClassifyDriveRoot();

        PrepareInPlaceSortStage(outcome.Items);
        bool anim = animateSteps && CanAnimate;
        var movables = outcome.Items
            .Where(x => !x.IsKeepInPlace && !x.IsBlocked)
            .ToList();
        if (movables.Count > 0)
            await RelayGlideMovableItemsAsync(movables, animate: anim, relayMs: 115, glideMs: 480, ct);
        if (anim)
            await WaitForActiveFlightsAsync(ct);
        await RevealKeepSectionHeaderSmoothAsync(animate: anim, ct);
        RefreshOrganizeClassifyShortcutBar(isSortingNow: false);
    }

    private void ReplaceClassifyItems(List<ClassifyItem> nextItems)
    {
        _classifyAllItems.Clear();
        _classifyItemsById.Clear();

        foreach (var item in nextItems)
        {
            _classifyAllItems.Add(item);
            _classifyItemsById[item.Id] = item;
        }
    }

    private void PrepareInPlaceSortStage(IReadOnlyList<ClassifyItem> candidates)
    {
        if (OrganizeGrid == null) return;

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
        _activeFlightEndTick.Clear();

        ShowOrganizeState(OrganizeStateKind.None);
        foreach (var n in _organizeAll) n.ClearClassifyVisualState();

        var usedNodes = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);
        long maxRowSize = 0;

        foreach (var row in _organizeRows)
        {
            if (row.IsClassifyGroupHeader) continue;
            if (!usedNodes.Add(row)) continue;
            _classifyOriginalOrder.Add(row);
            if (row.Size > maxRowSize) maxRowSize = row.Size;

            var hit = candidates.FirstOrDefault(c => c.SourceEntry == row.Dir);
            if (hit != null)
                _classifyNodesByItemId[hit.Id] = row;
            else if (row.IsRoot)
                _classifyExtraKeepNodes.Add(row);
        }

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
        if (item.IsBlocked)
        {
            var t = ClassifyCategoryPalette.KeepTheme;
            node.ApplyClassifyVisualState(
                item.Id, ClassifyCategoryRule.KeepId,
                t.AccentHex, t.RowWashHex(isDark), t.PillWashHex(isDark),
                "系统核心", item.SafetyNote, "", false, false);
        }
        else
        {
            string probeAccent = "#94A3B8";
            string probeBg = isDark ? "#0A94A3B8" : "#0694A3B8";
            string probePill = isDark ? "#1E94A3B8" : "#1494A3B8";
            node.ApplyClassifyVisualState(
                item.Id, "__pending__",
                probeAccent, probeBg, probePill,
                "分析中", item.ContentDigestDisplay, "", false, false);
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
            ? "系统核心"
            : (item.IsKeepInPlace ? "未归类" : item.CategoryName);

        string reason = !string.IsNullOrWhiteSpace(item.AiReason)
            ? (item.FromAi ? $"AI：{item.AiReason}" : item.AiReason)
            : (item.IsBlocked ? item.SafetyNote : item.ContentDigest);

        node.ApplyClassifyVisualState(
            item.Id,
            EffectiveGroupKey(item),
            theme.AccentHex,
            theme.RowWashHex(isDark),
            theme.PillWashHex(isDark),
            catLabel,
            reason,
            "",
            false,
            false);
    }

    private static string EffectiveGroupKey(ClassifyItem item)
        => item.IsKeepInPlace ? ClassifyCategoryRule.KeepId : item.CategoryId;

    // ==================== 60fps 原位逐个接力排序动画引擎 ====================

    private async Task RelayGlideMovableItemsAsync(
        IReadOnlyList<ClassifyItem> movableItems,
        bool animate,
        int relayMs,
        int glideMs,
        CancellationToken ct)
    {
        if (OrganizeGrid == null || !_classifySortActive || movableItems.Count == 0) return;

        var groupBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in _classifyAllItems)
        {
            if (it.IsKeepInPlace || it.IsBlocked) continue;
            string gk = EffectiveGroupKey(it);
            groupBytes.TryGetValue(gk, out long cur);
            groupBytes[gk] = cur + Math.Max(0, it.SizeBytes);
        }

        var ordered = movableItems
            .Where(x => !x.IsKeepInPlace && !x.IsBlocked && !_classifySettledIds.Contains(x.Id))
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
        PulseRadialAiRing(node.ClassifyAccentHex);

        _classifySettledIds.Add(item.Id);

        var liveTopY = SnapshotVisibleRowVisualTops(clearTransformsAfterSnapshot: true);
        double gridH = Math.Max(360, OrganizeGrid.ActualHeight > 0 ? OrganizeGrid.ActualHeight : 560);

        double originY;
        if (liveTopY.TryGetValue(node, out double selfY))
            originY = selfY;
        else if (_classifyParentAnchorByItemId.TryGetValue(item.Id, out var anchorNode)
                 && liveTopY.TryGetValue(anchorNode, out double ancY))
            originY = ancY + 8.0;
        else
            originY = Math.Min(gridH - 48.0, Math.Max(260.0, gridH * 0.74));

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

    private Dictionary<OrganizeNode, double> SnapshotVisibleRowVisualTops(bool clearTransformsAfterSnapshot)
    {
        var map = new Dictionary<OrganizeNode, double>(ReferenceEqualityComparer.Instance);
        if (OrganizeGrid == null) return map;

        for (int i = 0; i < _organizeRows.Count; i++)
        {
            if (OrganizeGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row
                || !row.IsVisible || row.ActualHeight <= 0)
                continue;

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

        var movableGroups = settledMovable
            .GroupBy(EffectiveGroupKey, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(x => Math.Max(0, x.SizeBytes)))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var grp in movableGroups)
        {
            activeGroupKeys.Add(grp.Key);
            var hdr = EnsureOrCreateGroupHeaderNode(grp.Key, cats);
            bool collapsed = _classifyCollapsedGroups.Contains(grp.Key);
            hdr.SetClassifyGroupCollapsed(collapsed);
            result.Add(hdr);
            if (!collapsed)
            {
                foreach (var ci in grp.OrderByDescending(x => x.SizeBytes).ThenBy(x => x.SourceName, StringComparer.OrdinalIgnoreCase))
                {
                    if (_classifyNodesByItemId.TryGetValue(ci.Id, out var node))
                    {
                        node.SetFlatFilterMode(0);
                        result.Add(node);
                        AppendExpandedDescendants(result, node, 0);
                    }
                }
            }
        }

        var remainingOriginals = new List<OrganizeNode>(_classifyOriginalOrder.Count);
        var remainingSet = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);
        foreach (var orig in _classifyOriginalOrder)
        {
            if (!settledMovableNodeSet.Contains(orig) && remainingSet.Add(orig))
                remainingOriginals.Add(orig);
        }

        if (includeKeepHeader)
        {
            foreach (var item in _classifyAllItems)
            {
                if (!item.IsKeepInPlace && !item.IsBlocked) continue;
                if (!_classifyNodesByItemId.TryGetValue(item.Id, out var kNode)) continue;
                if (settledMovableNodeSet.Contains(kNode) || remainingSet.Contains(kNode)) continue;
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

        foreach (var staleKey in _classifyGroupHeaders.Keys.Where(k => !activeGroupKeys.Contains(k)).ToList())
            _classifyGroupHeaders.Remove(staleKey);

        return result;
    }

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

        string groupName = isKeep ? "系统核心与原位目录" : (rule?.Name ?? groupKey);
        var hdr = OrganizeNode.CreateClassifyGroupHeader(
            groupKey, groupName, "", theme.AccentHex, theme.TintHex(isDark), theme.PillWashHex(isDark));
        _classifyGroupHeaders[groupKey] = hdr;
        return hdr;
    }

    private void RefreshAllSortHeaderNodes()
    {
        if (!_classifySortActive) return;
        bool isDark = ThemeService.CurrentTheme == AppTheme.Dark;
        var cats = _classifyCachedCats.Count > 0 ? _classifyCachedCats : App.Settings.EffectiveClassifyCategories();

        var countByKey = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        var settledMovableNodes = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);

        foreach (var ci in _classifyAllItems)
        {
            if (!_classifySettledIds.Contains(ci.Id)) continue;
            if (ci.IsKeepInPlace || ci.IsBlocked) continue;
            if (!_classifyNodesByItemId.TryGetValue(ci.Id, out var node)) continue;
            settledMovableNodes.Add(node);
            string key = EffectiveGroupKey(ci);
            countByKey.TryGetValue(key, out var cur);
            countByKey[key] = (cur.Count + 1, cur.Bytes + Math.Max(0, ci.SizeBytes));
        }

        if (_classifyKeepHeaderVisible)
        {
            var countedKeep = new HashSet<OrganizeNode>(ReferenceEqualityComparer.Instance);
            foreach (var orig in _classifyOriginalOrder)
            {
                if (settledMovableNodes.Contains(orig) || !countedKeep.Add(orig)) continue;
                countByKey.TryGetValue(ClassifyCategoryRule.KeepId, out var cur);
                countByKey[ClassifyCategoryRule.KeepId] = (cur.Count + 1, cur.Bytes + Math.Max(0, orig.Size));
            }
            foreach (var ci in _classifyAllItems)
            {
                if (!ci.IsKeepInPlace && !ci.IsBlocked) continue;
                if (!_classifyNodesByItemId.TryGetValue(ci.Id, out var kNode)) continue;
                if (settledMovableNodes.Contains(kNode) || _classifyParentAnchorByItemId.ContainsKey(ci.Id)) continue;
                if (!countedKeep.Add(kNode)) continue;
                countByKey.TryGetValue(ClassifyCategoryRule.KeepId, out var cur);
                countByKey[ClassifyCategoryRule.KeepId] = (cur.Count + 1, cur.Bytes + Math.Max(0, ci.SizeBytes));
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

            string groupName = isKeep ? "系统核心与原位目录" : (rule?.Name ?? key);
            hdr.SetClassifyGroupCollapsed(_classifyCollapsedGroups.Contains(key));
            hdr.UpdateClassifyGroupHeader(
                groupName, "", stats.Count, 0, stats.Bytes,
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
            SyncClassifyGroupedRowsInPlace();
            SaveActiveDriveToCache();
            PersistCollapsedGroupsAsync();
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
        PersistCollapsedGroupsAsync();
    }

    private void PersistCollapsedGroupsAsync()
    {
        string drive = CurrentClassifyDriveRoot();
        if (string.IsNullOrWhiteSpace(drive)) return;
        var collapsed = new List<string>(_classifyCollapsedGroups);
        _ = Task.Run(() => VirtualClassifyCache.UpdateCollapsedGroups(drive, collapsed));
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

    // ==================== 退出 / 缓存切换 / 极简顶栏提示 ====================

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
        _classifyAiWarning = "";
        _classifyRestoredFromDiskAtUtc = null;
        ReplaceClassifyItems(new());
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
        bool hasPlan = _classifyHasAiPlan && _classifyBuiltForScan == _scanGeneration && _classifyAllItems.Count > 0;
        int groupedCount = 0;
        for (int i = 0; i < _classifyAllItems.Count; i++)
        {
            if (!_classifyAllItems[i].IsKeepInPlace) groupedCount++;
        }

        if (OrganizeClassifyBar == null) return;
        if (!_classifySortActive && !hasPlan)
        {
            OrganizeClassifyBar.Visibility = Visibility.Collapsed;
            return;
        }

        OrganizeClassifyBar.Visibility = Visibility.Visible;
        if (OrganizeClassifyBarText != null)
        {
            if (isSortingNow)
            {
                OrganizeClassifyBarText.Foreground = ThemeService.Brush("TextDim");
                OrganizeClassifyBarText.Text = $"正在识别目录并按色聚拢（已归类 {groupedCount} 个目录）…";
            }
            else if (_classifyAiWarning.Length > 0)
            {
                OrganizeClassifyBarText.Foreground = ThemeService.Brush("BrandAccent");
                OrganizeClassifyBarText.Text = _classifyAiWarning;
            }
            else if (_classifyRestoredFromDiskAtUtc is { } savedUtc)
            {
                OrganizeClassifyBarText.Foreground = ThemeService.Brush("TextDim");
                OrganizeClassifyBarText.Text =
                    $"已恢复软件内彩色分类视图（共 {groupedCount} 个目录 · 上次保存 {savedUtc.ToLocalTime():MM-dd HH:mm}）· 不改动磁盘原路径";
            }
            else
            {
                OrganizeClassifyBarText.Foreground = ThemeService.Brush("TextDim");
                OrganizeClassifyBarText.Text =
                    $"已按颜色归类 {groupedCount} 个目录 · 仅在软件内分组展示，不改动磁盘原文件";
            }
        }
    }
}
