using System.IO;
using System.Windows;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;
using AiDiskCleaner.Services.Agent;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner;

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
    private CancellationTokenSource? _preloadCts;
    private string _activeDriveKey = "";
    private bool _startupUninstallPreloaded;
    private bool _startupSecondaryDrivesStarted;

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

        string cachedTip = $"已从缓存切换至 {entry.Drive}（点「扫描」可重新扫描）";
        SetStatus(cachedTip);
        SetOrganizeNote(cachedTip);

        if (entry.CleanReport == null)
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

        if (_bgScanTasks.TryGetValue(targetDrive, out var bgTask))
        {
            _activeDriveKey = targetDrive;
            InvalidateFollowUpStages();
            _report = null;
            CancelAgentReport();
            ClearCleanLayers();
            ScanProgressPanel.Visibility = Visibility.Visible;
            ScanProgressBar.IsIndeterminate = true;
            ScanProgressText.Text = $"正在完成 {targetDrive} 预扫描…";
            SetStatus($"正在切换并等待 {targetDrive} 预扫描完成…");
            ShowOrganizeState(OrganizeStateKind.Scanning);

            var entry = await bgTask;
            string nowSelected = NormalizeDriveKey(DriveBox.SelectedItem?.ToString());
            if (!string.Equals(nowSelected, targetDrive, StringComparison.OrdinalIgnoreCase))
                return;

            ScanProgressPanel.Visibility = Visibility.Collapsed;
            ScanProgressBar.IsIndeterminate = false;
            if (entry != null && _driveCache.TryGetValue(targetDrive, out var loaded))
            {
                RestoreDriveFromCache(loaded);
                return;
            }
        }

        _activeDriveKey = targetDrive;
        RunScan();
    }

    /// <summary>打开软件时即在后台预加载卸载清单，不等用户切到「卸载」页才扫描。</summary>
    private void TriggerStartupPreloadUninstall()
    {
        if (_startupUninstallPreloaded) return;
        _startupUninstallPreloaded = true;
        _uninstallTabVisited = true;
        if (_apps.Count == 0 && !_listingApps)
            _ = LoadApps(silent: true);
    }

    /// <summary>首盘（默认 C:\）扫描结束后，自动在后台预扫描并缓存 D:\（或未扫的 C:\）。</summary>
    private void TriggerStartupSecondaryDrivePreload()
    {
        if (_startupSecondaryDrivesStarted) return;
        _startupSecondaryDrivesStarted = true;

        if (DriveBox?.ItemsSource is not IEnumerable<string> available) return;
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

            var task = PreloadDriveInBackgroundAsync(key);
            _bgScanTasks[key] = task;
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
            // 使用独立的 ScanCoordinator / CleanupWorkflowService，避免与前台扫描竞争 LastQuality。
            var bgWorkflow = new CleanupWorkflowService(new MftScanService(), new RecursiveScanService());
            var outcome = await bgWorkflow.ScanAsync(driveKey, progress: null, ct);
            ct.ThrowIfCancellationRequested();

            var files = await Task.Run(() => CollectFiles(outcome.Root), ct);
            ct.ThrowIfCancellationRequested();
            sw.Stop();

            // 如果用户已经把前台切过来并在重新手动扫同一块盘，不再覆写前台扫描结果。
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
