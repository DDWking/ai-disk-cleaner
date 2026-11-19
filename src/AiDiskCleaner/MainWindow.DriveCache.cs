using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.Agent;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner;

public sealed class DriveCardItem
{
    public string DriveName { get; init; } = "";
    public string DriveLetter { get; init; } = "";
    public string Title { get; init; } = "";
    public string FreeText { get; init; } = "";
    public string UsageSummary { get; init; } = "";
    public string StatusText { get; init; } = "";
    public double UsagePercent { get; init; }
    public bool IsActive { get; init; }
    public bool IsHighUsage { get; init; }
    public bool IsCached { get; init; }
    public bool IsPreloading { get; init; }
    public string RescanTip { get; init; } = "";
}

/// <summary>
/// 启动多盘自动扫描（C:\ 与 D:\）、卸载清单预加载，以及跨盘切换秒级缓存。
/// </summary>
public partial class MainWindow
{
    private sealed class DriveScanCacheEntry
    {
        public string Drive { get; init; } = "";
        public FileEntry Root { get; set; } = null!;
        public ScanQuality? Quality { get; set; }
        public bool UsedFallback { get; set; }
        public string FallbackReason { get; set; } = "";
        public double ScanElapsedSeconds { get; set; }
        public DateTimeOffset ScannedAt { get; set; }

        public List<FileEntry> AllFiles { get; set; } = new();
        public CleanReport? CleanReport { get; set; }
        public CleanLayeredResult? Layered { get; set; }
        public int DupIncomplete { get; set; }

        public ScanSession? AgentSession { get; set; }
        public AgentReport? AgentReport { get; set; }
        public Dictionary<string, (FolderColorTone Tone, string Badge, string Summary, string Brief)> AiAnnotations { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        public List<OrganizeNode>? OrganizeAll { get; set; }
        public List<OrganizeNode>? OrganizeRoots { get; set; }
        public Dictionary<FileEntry, OrganizeNode>? OrganizeByDir { get; set; }
        public IReadOnlyList<string> OrganizeEntryPoints { get; set; } = Array.Empty<string>();
        public int OrganizeUnlistedTotal { get; set; }
        public HashSet<OrganizeNode>? DirectFilesMaterialized { get; set; }
    }

    private readonly Dictionary<string, DriveScanCacheEntry> _driveCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<DriveScanCacheEntry?>> _bgScanTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<DriveCardItem> _driveCards = new();
    private CancellationTokenSource? _preloadCts;
    private string _activeDriveKey = "";
    private bool _startupQueueStarted;
    private FileEntry? _collectingFilesForRoot;
    private string _bgPreloadMessage = "";
    private bool _bgPreloadBusy;

    private static string NormalizeDriveKey(string? drive)
    {
        string text = (drive ?? "").Trim();
        if (text.Length == 0) return "";
        if (text.Length == 2 && text[1] == ':') return text + "\\";
        if (!text.EndsWith('\\')) text += "\\";
        return text.ToUpperInvariant();
    }

    private void InvalidateDriveCache(string? drive)
    {
        string key = NormalizeDriveKey(drive);
        if (key.Length == 0) return;
        _driveCache.Remove(key);
    }

    private void SaveActiveDriveToCache()
    {
        if (_root == null) return;
        string key = NormalizeDriveKey(_activeDriveKey.Length > 0 ? _activeDriveKey : Path.GetPathRoot(_root.FullPath));
        if (key.Length == 0) return;
        _activeDriveKey = key;

        if (!_driveCache.TryGetValue(key, out var entry))
        {
            entry = new DriveScanCacheEntry { Drive = key };
            _driveCache[key] = entry;
        }

        entry.Root = _root;
        entry.Quality = _scanQuality;
        entry.UsedFallback = _scanUsedFallback;
        entry.FallbackReason = _scanFallbackReason;
        entry.ScanElapsedSeconds = _scanElapsed;
        entry.ScannedAt = DateTimeOffset.Now;
        entry.AllFiles = _allFiles;
        entry.CleanReport = _report;
        entry.Layered = _layered;
        entry.DupIncomplete = _dupIncomplete;
        entry.AgentSession = _agentSession;
        entry.AgentReport = _lastAgentReport;
        entry.AiAnnotations = new Dictionary<string, (FolderColorTone, string, string, string)>(_aiReportAnnotations, StringComparer.OrdinalIgnoreCase);
        SaveOrganizeSnapshotInto(entry);
        RefreshDriveCards();
    }

    private void SaveOrganizeSnapshotInto(DriveScanCacheEntry entry)
    {
        if (_organizeBuiltForScan != _scanGeneration || _organizeRoots.Count == 0) return;
        entry.OrganizeAll = new List<OrganizeNode>(_organizeAll);
        entry.OrganizeRoots = new List<OrganizeNode>(_organizeRoots);
        entry.OrganizeByDir = new Dictionary<FileEntry, OrganizeNode>(_organizeByDir);
        entry.OrganizeEntryPoints = _organizeEntryPoints;
        entry.OrganizeUnlistedTotal = _organizeUnlistedTotal;
        entry.DirectFilesMaterialized = new HashSet<OrganizeNode>(_directFilesMaterialized);
    }

    private void UpdateDriveCacheAnalysis(FileEntry root)
    {
        foreach (var entry in _driveCache.Values)
        {
            if (!ReferenceEquals(entry.Root, root)) continue;
            entry.AllFiles = _allFiles;
            entry.CleanReport = _report;
            entry.Layered = _layered;
            entry.DupIncomplete = _dupIncomplete;
            break;
        }
    }

    private void RestoreDriveFromCache(DriveScanCacheEntry entry)
    {
        InvalidateFollowUpStages();
        if (_agentBusy)
            CancelQuietly(_agentCts);

        _scanning = false;
        _activeDriveKey = entry.Drive;
        _scanGeneration++;
        _layerGeneration++;

        _root = entry.Root;
        _scanQuality = entry.Quality;
        _scanUsedFallback = entry.UsedFallback;
        _scanFallbackReason = entry.FallbackReason;
        _scanElapsed = entry.ScanElapsedSeconds;
        _allFiles = entry.AllFiles;
        _report = entry.CleanReport;
        _layered = entry.Layered ?? CleanLayeredResult.Empty;
        _dupIncomplete = entry.DupIncomplete;
        _cleanScopeRoot = null;

        ScanProgressPanel.Visibility = Visibility.Collapsed;
        ScanProgressBar.IsIndeterminate = false;
        HideCleanProgress();

        if (entry.OrganizeRoots is { Count: > 0 } && entry.OrganizeAll != null && entry.OrganizeByDir != null)
        {
            _organizeGeneration++;
            _organizeBuiltForScan = _scanGeneration;
            _organizeRows.Clear();
            _organizeAll.Clear();
            _organizeAll.AddRange(entry.OrganizeAll);
            _organizeByDir.Clear();
            foreach (var kv in entry.OrganizeByDir)
                _organizeByDir[kv.Key] = kv.Value;
            _organizeRoots.Clear();
            _organizeRoots.AddRange(entry.OrganizeRoots);
            _organizeEntryPoints = entry.OrganizeEntryPoints;
            _organizeUnlistedTotal = entry.OrganizeUnlistedTotal;
            _directFilesMaterialized.Clear();
            if (entry.DirectFilesMaterialized != null)
                foreach (var n in entry.DirectFilesMaterialized) _directFilesMaterialized.Add(n);
            _filterExpandedNodes.Clear();
            _organizeColorFilter = null;
            _organizePendingOnly = false;
            _lastJumpNode = null;
            _organizeNote = "";
            ShowOrganizeState(OrganizeStateKind.None);
            RefreshOrganizeRows();
        }
        else
        {
            RebuildOrganize();
            SaveOrganizeSnapshotInto(entry);
        }

        RestoreCachedAgentReport(entry);
        UpdateVolumeInfo();
        ShowScanQuality();
        RefreshCleanUi();
        UpdateAiReportButton();
        UpdateStopButton();
        RefreshDriveCards();

        string cachedTip = $"{entry.Drive} 已从缓存秒切";
        SetStatus(cachedTip);
        SetOrganizeNote("");
        AnimateSlideFadeIn(OrganizeGridHost, offsetY: 8, durationMs: 190, fromOpacity: 0.55);
        AnimateSpringPop(RadialRingCanvas, fromScale: 0.94, offsetY: 0, durationMs: 240);

        if (entry.CleanReport == null && CleanPane != null && CleanPane.Visibility == Visibility.Visible)
            _ = RunCleanPipelineAsync(entry.Root);
    }

    private void RestoreCachedAgentReport(DriveScanCacheEntry entry)
    {
        _agentSession = entry.AgentSession;
        _lastAgentReport = entry.AgentReport;
        _lastAgentError = "";
        _agentProgressText = "";
        _aiReportAnnotations.Clear();
        foreach (var node in _organizeAll) node.ClearAiAnnotation();

        foreach (var kv in entry.AiAnnotations)
            _aiReportAnnotations[kv.Key] = kv.Value;

        if (_lastAgentReport != null)
        {
            ApplyReportToOrganizeTree(_lastAgentReport);
            ShowDockedAgentReport(_lastAgentReport);
        }
        else
        {
            if (OrganizeReportHeaderTitle != null)
                OrganizeReportHeaderTitle.Text = Loc.AiReportTitle;
            if (OrganizeReportExpandBtn != null)
                OrganizeReportExpandBtn.Visibility = Visibility.Collapsed;
            RefreshOrganizeRows();
            RefreshDockedReportPlaceholder();
            UpdateAiReportDockToggle();
        }
    }

    private async Task HandleDriveSelectionChangedAsync()
    {
        UpdateVolumeInfo();
        if (!IsUiReady) return;

        string targetDrive = NormalizeDriveKey(DriveBox.SelectedItem?.ToString());
        if (targetDrive.Length == 0 || string.Equals(targetDrive, _activeDriveKey, StringComparison.OrdinalIgnoreCase))
            return;

        if (_root != null)
            SaveActiveDriveToCache();

        if (_scanning)
            CancelQuietly(_scanCts);

        if (_driveCache.TryGetValue(targetDrive, out var cached))
        {
            RestoreDriveFromCache(cached);
            return;
        }

        // 若内存尚未缓存（比如用户刚开软件就立刻切到 D:），先瞬时读出上次目录快照，再后台无感校准
        var diskRoot = await Task.Run(() => OrganizeTreeCache.TryLoad(targetDrive, out _));
        string selectedAfterLoad = NormalizeDriveKey(DriveBox.SelectedItem?.ToString());
        if (diskRoot != null && string.Equals(selectedAfterLoad, targetDrive, StringComparison.OrdinalIgnoreCase))
        {
            _activeDriveKey = targetDrive;
            InvalidateFollowUpStages();
            _report = null;
            CancelAgentReport();
            ClearCleanLayers();
            _root = diskRoot;
            _allFiles = new();
            _scanQuality = null;
            _scanGeneration++;
            _layerGeneration++;
            UpdateVolumeInfo();
            await RebuildOrganizeAsync(diskRoot, _scanGeneration, preserveChecks: false, _preloadCts?.Token ?? default);
            SaveActiveDriveToCache();
            UpdateAiReportButton();
            UpdateStopButton();
            RefreshDriveCards();
            SetStatus($"{targetDrive} 秒开快照，后台静默校准中");
            _ = RunScanCoreAsync(silentRefresh: true);
            return;
        }

        if (_bgScanTasks.TryGetValue(targetDrive, out var bgTask))
        {
            _activeDriveKey = targetDrive;
            InvalidateFollowUpStages();
            _report = null;
            CancelAgentReport();
            ClearCleanLayers();
            SetTopScanBeam(true);
            SetStatus($"正在完成 {targetDrive} 扫描…");
            ShowOrganizeState(OrganizeStateKind.Scanning);

            var entry = await bgTask;
            string nowSelected = NormalizeDriveKey(DriveBox.SelectedItem?.ToString());
            if (!string.Equals(nowSelected, targetDrive, StringComparison.OrdinalIgnoreCase))
                return;

            SetTopScanBeam(false);
            if (entry != null && _driveCache.TryGetValue(targetDrive, out var loaded))
            {
                RestoreDriveFromCache(loaded);
                return;
            }
        }

        _activeDriveKey = targetDrive;
        RunScan();
    }

    private void ShowBgPreloadBanner(string message, bool busy)
    {
        _bgPreloadMessage = message ?? "";
        _bgPreloadBusy = busy;
        if (BgPreloadBanner == null || BgPreloadText == null) return;
        string bgKey = busy ? "BrandAccentSoft" : "CardSurface";
        string fgKey = busy ? "BrandAccent" : "TextDim";
        string borderKey = busy ? "BrandAccentSoft" : "Border";
        BgPreloadBanner.Background = ThemeService.Brush(bgKey);
        BgPreloadBanner.BorderBrush = ThemeService.Brush(borderKey);
        BgPreloadText.Foreground = ThemeService.Brush(fgKey);
        BgPreloadText.Text = _bgPreloadMessage;
        if (BgPreloadProgress != null)
        {
            BgPreloadProgress.Foreground = ThemeService.Brush("BrandAccent");
            BgPreloadProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }
        bool wasHidden = BgPreloadBanner.Visibility != Visibility.Visible;
        BgPreloadBanner.Visibility = Visibility.Visible;
        if (wasHidden)
            AnimateSlideFadeIn(BgPreloadBanner, offsetY: -6, durationMs: 200, fromOpacity: 0.0);
        RefreshDriveCards();
    }

    private void HideBgPreloadBanner()
    {
        _bgPreloadMessage = "";
        if (BgPreloadBanner != null)
            BgPreloadBanner.Visibility = Visibility.Collapsed;
        RefreshDriveCards();
    }

    private void RefreshBgPreloadTheme()
    {
        if (BgPreloadBanner == null || BgPreloadBanner.Visibility != Visibility.Visible || _bgPreloadMessage.Length == 0)
            return;
        string bgKey = _bgPreloadBusy ? "BrandAccentSoft" : "CardSurface";
        string fgKey = _bgPreloadBusy ? "BrandAccent" : "TextDim";
        string borderKey = _bgPreloadBusy ? "BrandAccentSoft" : "Border";
        BgPreloadBanner.Background = ThemeService.Brush(bgKey);
        BgPreloadBanner.BorderBrush = ThemeService.Brush(borderKey);
        if (BgPreloadText != null) BgPreloadText.Foreground = ThemeService.Brush(fgKey);
        if (BgPreloadProgress != null) BgPreloadProgress.Foreground = ThemeService.Brush("BrandAccent");
    }

    private void InitDriveCards()
    {
        if (DriveCardsList != null)
            DriveCardsList.ItemsSource = _driveCards;
        RefreshDriveCards();
    }

    private void RefreshDriveCards()
    {
        if (DriveCardsList == null || DriveBox?.ItemsSource is not IEnumerable<string> drives)
            return;

        string active = NormalizeDriveKey(_activeDriveKey.Length > 0 ? _activeDriveKey : DriveBox.SelectedItem?.ToString());
        _driveCards.Clear();
        foreach (var raw in drives)
        {
            string key = NormalizeDriveKey(raw);
            if (key.Length == 0) continue;
            string letter = key.TrimEnd('\\');
            bool isSystem = letter.Equals("C:", StringComparison.OrdinalIgnoreCase);

            string title = isSystem ? Loc.DriveSystemLabel : Loc.DriveDataLabel;
            string freeText = "";
            string usageSummary = key;
            double pct = 0;

            try
            {
                var info = new DriveInfo(key);
                if (info.IsReady)
                {
                    if (!isSystem && !string.IsNullOrWhiteSpace(info.VolumeLabel))
                        title = info.VolumeLabel.Trim();
                    long total = Math.Max(1, info.TotalSize);
                    long free = Math.Max(0, info.TotalFreeSpace);
                    long used = Math.Max(0, total - free);
                    pct = Math.Clamp(100.0 * used / total, 0, 100);
                    freeText = Loc.IsEn ? $"Free {FileEntry.FormatSize(free)}" : $"余 {FileEntry.FormatSize(free)}";
                    usageSummary = $"{FileEntry.FormatSize(used)} / {FileEntry.FormatSize(total)}";
                }
            }
            catch
            {
            }

            bool isActive = string.Equals(key, active, StringComparison.OrdinalIgnoreCase);
            bool isPreloading = _bgScanTasks.ContainsKey(key);
            bool isCached = _driveCache.ContainsKey(key) || (isActive && _root != null && !_scanning);
            string statusText = isPreloading
                ? Loc.DrivePreloadingTag
                : isCached
                    ? Loc.DriveCachedTag
                    : $"{pct:0.#}%";

            _driveCards.Add(new DriveCardItem
            {
                DriveName = raw,
                DriveLetter = letter,
                Title = title,
                FreeText = freeText,
                UsageSummary = usageSummary,
                StatusText = statusText,
                UsagePercent = pct,
                IsActive = isActive,
                IsHighUsage = pct >= 90.0,
                IsCached = isCached,
                IsPreloading = isPreloading,
                RescanTip = Loc.DriveRescanTip,
            });
        }
    }

    private void DriveCard_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DriveCardItem card || DriveBox == null)
            return;
        if (!string.Equals(DriveBox.SelectedItem?.ToString(), card.DriveName, StringComparison.OrdinalIgnoreCase))
            DriveBox.SelectedItem = card.DriveName;
        if (_rightTab != RightTab.Clean && _rightTab != RightTab.Organize)
            ShowRightTab(RightTab.Clean);
    }

    private void DriveCardRescan_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button { Content: UIElement icon })
            AnimateSpinIconOnce(icon, 480);
        if ((sender as FrameworkElement)?.DataContext is not DriveCardItem card || DriveBox == null)
            return;
        if (_rightTab != RightTab.Clean && _rightTab != RightTab.Organize)
            ShowRightTab(RightTab.Clean);

        InvalidateDriveCache(card.DriveName);
        if (string.Equals(DriveBox.SelectedItem?.ToString(), card.DriveName, StringComparison.OrdinalIgnoreCase))
        {
            if (!_scanning) RunScan();
            return;
        }
        DriveBox.SelectedItem = card.DriveName;
    }

    private async Task ShowBgPreloadDoneBrieflyAsync(string message, CancellationToken ct)
    {
        try
        {
            ShowBgPreloadBanner(message, busy: false);
            await Task.Delay(4000, ct);
            if (!ct.IsCancellationRequested && BgPreloadText?.Text == message)
                HideBgPreloadBanner();
        }
        catch (OperationCanceledException)
        {
            HideBgPreloadBanner();
        }
    }

    /// <summary>
    /// 首盘（C:\）首帧展示稳定后，延后在低优先级后台串联完成预载：
    /// 1) 静默校准并持久化另一块常驻盘（如 D:\）的轻量目录树缓存；
    /// 2) 稍后按需补齐卸载清点所需的扁平清单，彻底消除刚开软件时的 Gen2 GC 与 UI 卡顿。
    /// </summary>
    private async void StartStartupPreloadQueueAsync()
    {
        if (_startupQueueStarted) return;
        _startupQueueStarted = true;

        if (_preloadCts == null || _preloadCts.IsCancellationRequested)
            _preloadCts = new CancellationTokenSource();
        var ct = _preloadCts.Token;

        var doneParts = new List<string>();
        if (_activeDriveKey.Length > 0) doneParts.Add(_activeDriveKey.TrimEnd('\\'));

        try
        {
            // 先让首屏动画、圆环与用户首批交互完全平稳过渡，再静默做后台任务
            await Task.Delay(1200, ct);

            // 1) 串联预扫另一块启动目标盘（C:\ / D:\）并写入轻量持久化目录缓存
            var pendingDrives = new List<string>();
            if (DriveBox?.ItemsSource is IEnumerable<string> available)
            {
                var targets = new[] { @"C:\", @"D:\" };
                foreach (var raw in available)
                {
                    string key = NormalizeDriveKey(raw);
                    if (!targets.Any(t => string.Equals(t, key, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (string.Equals(key, _activeDriveKey, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (_driveCache.ContainsKey(key) || _bgScanTasks.ContainsKey(key))
                        continue;
                    pendingDrives.Add(key);
                }
            }

            foreach (var key in pendingDrives)
            {
                ct.ThrowIfCancellationRequested();
                ShowBgPreloadBanner($"{key.TrimEnd('\\')} 同步中", busy: true);
                var task = PreloadDriveInBackgroundAsync(key);
                _bgScanTasks[key] = task;
                var entry = await task;
                if (entry != null) doneParts.Add(key.TrimEnd('\\'));
                await Task.Delay(350, ct);
            }

            // 2) 若尚未收集主盘扁平文件清单，仅在准备清点软件占用前以低优先级延迟收集
            var activeRoot = _root;
            if (activeRoot != null && _allFiles.Count == 0 && !ReferenceEquals(_collectingFilesForRoot, activeRoot))
                await EnsureActiveFilesCollectedAsync(activeRoot, ct);

            // 3) 预加载卸载清单，此时 C:\ 与 D:\ 文件已入缓存，体积可一次算齐
            if (_apps.Count == 0 && !_listingApps)
            {
                ct.ThrowIfCancellationRequested();
                ShowBgPreloadBanner("预载卸载列表", busy: true);
                _uninstallTabVisited = true;
                await LoadApps(silent: true);
            }
            if (_apps.Count > 0) doneParts.Add("应用");

            if (doneParts.Count > 0)
                _ = ShowBgPreloadDoneBrieflyAsync($"已就绪 · {string.Join(" / ", doneParts)}", ct);
            else
                HideBgPreloadBanner();
        }
        catch (OperationCanceledException)
        {
            HideBgPreloadBanner();
        }
        catch (Exception ex)
        {
            AppLog.Record("Scan", ex, "startup preload queue failed");
            HideBgPreloadBanner();
        }
    }

    private async Task EnsureActiveFilesCollectedAsync(FileEntry activeRoot, CancellationToken ct = default)
    {
        if (ReferenceEquals(_collectingFilesForRoot, activeRoot)) return;
        _collectingFilesForRoot = activeRoot;
        try
        {
            var collected = await Task.Run(() => CollectFilesLowPriority(activeRoot), ct);
            if (!ReferenceEquals(_root, activeRoot)) return;
            _allFiles = collected;
            SaveActiveDriveToCache();
            if (_apps.Count > 0)
                _ = RefreshAppUsageWithAllCachedDrivesAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Record("Scan", ex, "collect root files in background");
        }
        finally
        {
            if (ReferenceEquals(_collectingFilesForRoot, activeRoot))
                _collectingFilesForRoot = null;
        }
    }

    private static List<FileEntry> CollectFilesLowPriority(FileEntry root)
    {
        var prev = Thread.CurrentThread.Priority;
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            return CollectFiles(root);
        }
        finally
        {
            Thread.CurrentThread.Priority = prev;
        }
    }

    private async Task<DriveScanCacheEntry?> PreloadDriveInBackgroundAsync(string driveKey)
    {
        if (_preloadCts == null || _preloadCts.IsCancellationRequested)
            _preloadCts = new CancellationTokenSource();
        var ct = _preloadCts.Token;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var bgWorkflow = new CleanupWorkflowService(new MftScanService(), new RecursiveScanService());
            var outcome = await bgWorkflow.ScanAsync(driveKey, progress: null, ct);
            ct.ThrowIfCancellationRequested();

            // 后台轻量落盘目录树缓存，便于下次冷启动直接秒开 D:
            _ = Task.Run(() => OrganizeTreeCache.Save(outcome.Root, ct), ct);

            var files = await Task.Run(() => CollectFilesLowPriority(outcome.Root), ct);
            ct.ThrowIfCancellationRequested();
            sw.Stop();

            if (_scanning && string.Equals(_activeDriveKey, driveKey, StringComparison.OrdinalIgnoreCase))
                return null;

            var entry = new DriveScanCacheEntry
            {
                Drive = driveKey,
                Root = outcome.Root,
                Quality = outcome.Quality,
                UsedFallback = outcome.UsedFallback,
                FallbackReason = outcome.FallbackReason,
                ScanElapsedSeconds = sw.Elapsed.TotalSeconds,
                ScannedAt = DateTimeOffset.Now,
                AllFiles = files,
            };
            _driveCache[driveKey] = entry;
            RefreshDriveCards();
            AppLog.Info("Scan", $"background preload cached drive={driveKey} files={files.Count} ms={sw.ElapsedMilliseconds}");

            if (_apps.Count > 0)
                _ = RefreshAppUsageWithAllCachedDrivesAsync();

            return entry;
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("Scan", $"background preload canceled drive={driveKey}");
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Record("Scan", ex, $"background preload failed drive={driveKey}");
            return null;
        }
        finally
        {
            _bgScanTasks.Remove(driveKey);
        }
    }

    /// <summary>合并已缓存的全部磁盘文件清单，使「卸载」页能跨 C:\ 与 D:\ 同时核对软件实际占用。</summary>
    private List<FileEntry> GetAllCachedScanFiles()
    {
        if (_driveCache.Count == 0) return _allFiles;
        var seen = new HashSet<FileEntry>();
        var merged = new List<FileEntry>(_allFiles.Count + 4096);
        foreach (var f in _allFiles)
            if (seen.Add(f)) merged.Add(f);
        foreach (var entry in _driveCache.Values)
        {
            foreach (var f in entry.AllFiles)
                if (seen.Add(f)) merged.Add(f);
        }
        return merged;
    }

    private async Task RefreshAppUsageWithAllCachedDrivesAsync()
    {
        var apps = _apps;
        if (apps.Count == 0) return;
        var files = GetAllCachedScanFiles();
        try
        {
            AppRecommendationService.InvalidateUsageCache();
            var usage = await Task.Run(() => AppRecommendationService.CalculateUsage(apps, files));
            if (!ReferenceEquals(_apps, apps)) return;
            AppRecommendationService.ApplyUsage(usage);
            AppFactualInfoService.Apply(apps);
            if (!_showingJunk) BindAppList();
        }
        catch (Exception ex)
        {
            UiLog("多盘刷新软件占用失败: " + ex.Message);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        CancelQuietly(_scanCts);
        CancelQuietly(_analyzeCts);
        CancelQuietly(_dupCts);
        CancelQuietly(_snapshotCts);
        CancelQuietly(_uninstallCts);
        CancelQuietly(_aiConfigCts);
        CancelQuietly(_preloadCts);
        CancelQuietly(_agentCts);
        base.OnClosed(e);
    }
}
