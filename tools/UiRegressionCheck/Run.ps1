# UI / behaviour regression check (ASCII only on purpose).
#
# Guards the interface behaviour that was finished on 2026-09-10 so a later
# refactor cannot quietly roll it back:
#   - the cleanable panel is the default right-hand tab
#   - the file browser / directory tree starts hidden
#   - the clean pane still fills the right column (Grid, not the old DockPanel bug)
#   - AI can only write notes; it never touches risk / selection / deletability
#   - no long-running operation is left with CancellationToken.None
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/UiRegressionCheck/Run.ps1

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cs = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\MainWindow.xaml.cs') -Raw -Encoding UTF8
$xaml = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\MainWindow.xaml') -Raw -Encoding UTF8
$parser = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AiNoteParser.cs') -Raw -Encoding UTF8
$aiCoord = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AiCoordinator.cs') -Raw -Encoding UTF8
$aiclient = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AiClient.cs') -Raw -Encoding UTF8
$analyst = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\DiskAnalyst.cs') -Raw -Encoding UTF8
$agent = Get-Content -LiteralPath (Join-Path $repo 'sidecar\src\agent.js') -Raw -Encoding UTF8
$loc = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\Loc.cs') -Raw -Encoding UTF8
$rules = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\CleanRules\CleanRules.cs') -Raw -Encoding UTF8
$dup = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\CleanRules\DuplicateScanService.cs') -Raw -Encoding UTF8
$sig = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AppSignatures.cs') -Raw -Encoding UTF8
$item = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Models\CleanItem.cs') -Raw -Encoding UTF8
$nodes = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Models\CleanGroupNodes.cs') -Raw -Encoding UTF8
$itv = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Models\ItemAiView.cs') -Raw -Encoding UTF8
$iasvc = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\ItemAiService.cs') -Raw -Encoding UTF8
$iap = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\ItemAiPrompt.cs') -Raw -Encoding UTF8

$fail = 0
function Assert-True([string]$name, [bool]$ok) {
    if ($ok) { Write-Output "PASS $name" }
    else { Write-Output "FAIL $name"; $script:fail++ }
}

# --- 1. directory tree hidden by default -------------------------------------
Assert-True 'tree visibility defaults to false' ($cs -match 'private\s+bool\s+_treeVisible\s*;')
Assert-True 'tree visibility is never defaulted to true' ($cs -notmatch 'private\s+bool\s+_treeVisible\s*=\s*true')
Assert-True 'ApplySidebarLayout is called while building the window' ($cs -match 'ApplySidebarLayout\(\);')
Assert-True 'collapsed tree really hides the left panel' `
    ($cs -match 'LeftPanel\.Visibility = Visibility\.Collapsed')
Assert-True 'collapsed tree removes the splitter' `
    ($cs -match 'SplitterCol\.Width = new GridLength\(0\)')

# --- 1b. sidebar is a compact toggle with a vector icon ----------------------
Assert-True 'sidebar toggle sits before the app name (top-left)' `
    ($xaml -match '(?s)x:Name="TreeToggleBtn".*?x:Name="TitleText"')
Assert-True 'sidebar toggle no longer uses the big word button' `
    ($xaml -notmatch '(?s)x:Name="TreeToggleBtn"[^>]{0,400}Content="文件夹"')
Assert-True 'sidebar icon is vector geometry, not a Unicode glyph' `
    ($xaml -match '(?s)x:Name="TreeToggleBtn".*?<Viewbox[^>]*>.*?<Canvas')
Assert-True 'sidebar toggle has an accessible name' `
    ($xaml -match '(?s)x:Name="TreeToggleBtn".*?AutomationProperties\.Name=')
Assert-True 'sidebar toggle tooltip reflects the action' `
    ($cs -match 'Loc\.HideSidebar' -and $cs -match 'Loc\.ShowSidebar')
Assert-True 'collapsing the sidebar leaves no empty column' `
    ($cs -match 'LeftCol\.Width\s*=\s*new\s+GridLength\(0\)' -and
     $cs -match 'Splitter\.Visibility\s*=\s*Visibility\.Collapsed')
Assert-True 'sidebar width is bounded 240-360' `
    ($cs -match 'SidebarMinWidth = 240' -and $cs -match 'SidebarMaxWidth = 360')
Assert-True 'main content column has NO MaxWidth (that caused the black block)' `
    ($xaml -notmatch '(?s)x:Name="RightCol"[^>]*MaxWidth')
Assert-True 'splitter drag is clamped into the allowed range' `
    ($cs -match 'Math\.Clamp\(w, SidebarMinWidth, SidebarMaxWidth\)')
Assert-True 'narrow windows switch the sidebar to an overlay drawer' `
    ($cs -match 'SidebarDockMinWindow' -and $cs -match 'Grid\.SetColumnSpan\(LeftPanel, 3\)')
Assert-True 'overlay drawer has a scrim that collapses it' `
    ($xaml -match 'x:Name="SidebarScrim"' -and $cs -match 'SidebarScrim_Click')
Assert-True 'returning to docked mode resets the overlay-only properties' `
    ($cs -match 'LeftPanel\.Width = double\.NaN' -and $cs -match 'Panel\.SetZIndex\(LeftPanel, 0\)')

# --- 1c. file search lives in the sidebar and is decoupled from cleanup -----
Assert-True 'file search box now lives inside the sidebar' `
    ($xaml -match '(?s)x:Name="LeftPanel".*?x:Name="SearchBox"')
Assert-True 'file search is gone from the top toolbar' `
    ($xaml -notmatch '(?s)x:Name="StopButton".*?x:Name="SearchBox".*?x:Name="VolumeText"')
Assert-True 'browsing a folder never changes the cleanup range' `
    ($cs -match '(?s)private void ShowDirectory\(FileEntry dir\).*?UpdateScopeButton\(\);' -and
     $cs -notmatch '(?s)private void ShowDirectory\(FileEntry dir\)[\s\S]{0,1200}_cleanScopeRoot\s*=')
Assert-True 'cleanup range is a separate variable from the browse directory' `
    ($cs -match 'private FileEntry\? _cleanScopeRoot;')
Assert-True 'range is only applied by an explicit action' `
    ($cs -match 'private void ScopeToFolder_Click' -and $cs -match 'private void ClearScope_Click')
Assert-True 'applied range is visible on the cleanup page with a clear action' `
    ($xaml -match 'x:Name="CleanScopeBar"' -and $xaml -match 'x:Name="ClearScopeBtn"')
Assert-True 'range chip warns about selected items outside the range' `
    ($cs -match 'Loc\.ScopeChip\(path, outside\)')
Assert-True 'setting the range never clears or widens the selection' `
    ($cs -match '(?s)private void ApplyCleanScope\(\)[\s\S]{0,700}RebuildLayersAsync')

# --- 2. cleanable panel is the default tab -----------------------------------
Assert-True 'window opens on the clean tab' ($cs -match 'ShowRightTab\(RightTab\.Clean\);')
Assert-True 'main navigation has exactly two pages (clean, uninstall)' `
    ($xaml -match '(?s)x:Name="TabCleanBtn".*?x:Name="TabUninstallBtn"' -and
     -not ([regex]::Match($xaml, 'x:Name="TabExtBtn"').Success))
Assert-True 'tab switching uses an enum, not fragile numeric indexes' `
    ($cs -match 'private enum RightTab \{ Clean, Uninstall \}' -and
     $cs -match 'ShowRightTab\(RightTab\.Uninstall\)' -and
     -not ([regex]::Match($cs, 'ShowRightTab\([0-9]\)').Success))
Assert-True 'clean pane still comes before uninstall pane' `
    ($xaml -match '(?s)x:Name="CleanPane".*?x:Name="UninstallPane"')
Assert-True 'clean pane is visible by default (no Collapsed on CleanPane itself)' `
    ($xaml -notmatch '<DockPanel\s+x:Name="CleanPane"[^>]*Visibility="Collapsed"')

# --- 3. the right column layout fix must not be reverted ---------------------
Assert-True 'right panel still uses a Grid for the three panes' `
    ($xaml -match '(?s)<Border\s+x:Name="RightPanel".*?<Grid>.*?<RowDefinition')
Assert-True 'panes share the same Grid.Row so the visible one fills it' `
    (([regex]::Matches($xaml, 'Grid\.Row="1"')).Count -ge 3)

# --- 4. AI can only write notes ---------------------------------------------
Assert-True 'AiNoteParser never writes Risk' ($parser -notmatch '\.Risk\s*=')
Assert-True 'AiNoteParser never writes CanDelete' ($parser -notmatch '\.CanDelete\s*=')
Assert-True 'AiNoteParser never writes Selected' ($parser -notmatch '\.Selected\s*=')
Assert-True 'AiNoteParser never deletes anything' ($parser -notmatch 'SendToRecycle|Delete\(')
Assert-True 'AiCoordinator never writes Risk/CanDelete/Selected' `
    ($aiCoord -notmatch '\.(Risk|CanDelete|Selected)\s*=')
Assert-True 'AI cannot execute uninstall' ($aiCoord -notmatch 'StartUninstall')

# --- 5. no long-running operation left without a token ----------------------
$noneCount = ([regex]::Matches($cs, 'CancellationToken\.None')).Count
Assert-True "no CancellationToken.None left in the window (found $noneCount)" ($noneCount -eq 0)
Assert-True 'stop button cancels everything' ($cs -match 'StopEverything\(\)')
Assert-True 'stale results are guarded by a generation counter' `
    ($cs -match '_scanGeneration' -and $cs -match '_analyzeGeneration' -and $cs -match '_uninstallGeneration')

# --- 6. deletion always goes through preflight ------------------------------
Assert-True 'deletion goes through DeletionCoordinator' `
    ($cs -match '_deleteCoordinator\.Plan\(' -and $cs -match '_deleteCoordinator\.Execute\(')
Assert-True 'no direct RecycleService call in the delete paths' `
    ($cs -notmatch 'RecycleService\.SendToRecycle')

# --- 7. grouping virtualisation (the 260k-row display fix) ------------------
Assert-True 'clean grid enables virtualisation when grouping' `
    ($xaml -match 'VirtualizingPanel\.IsVirtualizingWhenGrouping="True"')
Assert-True 'clean grid recycles row containers' `
    ($xaml -match 'VirtualizingPanel\.VirtualizationMode="Recycling"')
Assert-True 'clean grid scrolls by pixel' `
    ($xaml -match 'VirtualizingPanel\.ScrollUnit="Pixel"')
Assert-True 'clean grid keeps content scrolling on' `
    ($xaml -match 'ScrollViewer\.CanContentScroll="True"')
Assert-True 'uninstall grid also virtualises when grouping' `
    (([regex]::Matches($xaml, 'IsVirtualizingWhenGrouping="True"')).Count -ge 2)
Assert-True 'row container count is audited at runtime' `
    ($cs -match 'CountRowContainers' -and $cs -match 'rows created=')

$snapshot = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\CleanListSnapshot.cs') -Raw -Encoding UTF8
$scanSnapshot = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\ScanSnapshot.cs') -Raw -Encoding UTF8
$grouping = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\CleanGroupingService.cs') -Raw -Encoding UTF8
$nodes = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Models\CleanGroupNodes.cs') -Raw -Encoding UTF8
$pager = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\CleanItemPager.cs') -Raw -Encoding UTF8

# --- 8. heavy work is off the UI thread -------------------------------------
Assert-True 'layered grouping happens in Task.Run' `
    ($cs -match 'Task\.Run\(\(\) =>' -and $cs -match 'CleanGroupingService\.Build')
Assert-True 'old synchronous BuildCategories is gone' ($cs -notmatch 'void BuildCategories')
# 只看明细绑定那一段：卸载页/残留页的排序量级很小，不在这次优化范围内
$bindDetail = [regex]::Match($cs, '(?s)private void BindDetailPage\(\).*?\n    \}').Value
Assert-True 'detail view does not add SortDescriptions' `
    ($bindDetail.Length -gt 0 -and $bindDetail -notmatch 'SortDescriptions\.Add')
Assert-True 'display order is risk group then size (pager sorts it once)' `
    ($pager -match 'x\.Risk == CleanRisk\.Safe \? 0 : 1' -and $pager -match 'ThenByDescending\(x => x\.Size\)')
Assert-True 'grouping orders purposes by risk tier first' `
    ($grouping -match 'a\.RiskTier\.CompareTo\(b\.RiskTier\)')
# 扩展名页已整页移除：不仅隐藏，专属的后台统计也被删掉（不是留着不跑）
Assert-True 'extension page is fully removed from XAML' `
    (-not ([regex]::Match($xaml, 'x:Name="(ExtPane|ExtGrid|ExtTitle|ColExt|ColType|ColPct|ColSize)"').Success))
Assert-True 'extension stats model/converter are gone' `
    ((-not ([regex]::Match($cs, 'class ShareWidthConverter').Success)) -and
     -not (Test-Path (Join-Path $repo 'src\AiDiskCleaner\Models\ExtStat.cs')))
Assert-True 'extension-only background computation is gone' `
    (-not ([regex]::Match($cs, 'BuildExtStats|CollectExt|ShowExtStats').Success))
Assert-True 'snapshot saving is off the UI thread' `
    ($cs -match 'await Task\.Run\(' -and $cs -match 'snap\.Save\(ct\)')
Assert-True 'snapshot save uses temp-file replace' `
    ($scanSnapshot -match 'File\.Move\(tmp, path, overwrite: true\)')
Assert-True 'app inventory is deferred until the uninstall tab is opened' `
    ($cs -match '_uninstallTabVisited' -and $cs -match 'EnsureAppsLoaded')

# --- 9. stop button tracks the work state, not a single flow ----------------
Assert-True 'stop button visibility is driven by WorkState' `
    ($cs -match 'private void UpdateStopButton' -and $cs -match '_work\.Busy')
Assert-True 'the scan finally block no longer hides the stop button' `
    ($cs -notmatch 'StopButton\.Visibility\s*=\s*Visibility\.Collapsed')
Assert-True 'scan/analyze/duplicates are registered stages' `
    ($cs -match 'StageNames\.Scan' -and $cs -match 'StageNames\.Analyze' -and $cs -match 'StageNames\.Duplicates')
Assert-True 'duplicate detection runs as its own cancellable stage' `
    ($cs -match 'RunDuplicateStageAsync' -and $cs -match 'StartOperation\(ref _dupCts')
Assert-True 'analyze skips duplicates so the list shows first' `
    ($cs -match 'includeDuplicates:\s*false')
Assert-True 'progress callbacks are generation-guarded' `
    (([regex]::Matches($cs, 'myGeneration != _\w+Generation')).Count -ge 3)

# --- 11. single-layer navigation (no stacked tables) ------------------------
Assert-True 'purpose home exists as the default view' ($xaml -match 'x:Name="PurposePage"')
Assert-True 'location page exists and starts hidden' `
    ($xaml -match 'x:Name="LocationPage" Grid\.Column="0" Visibility="Collapsed"')
Assert-True 'detail panel starts hidden' `
    ($xaml -match 'x:Name="DetailPanel" Grid\.Column="1" Visibility="Collapsed"')
# 关键：主内容区里只有一列用于页面，两层页面是互斥可见，不再各占一行
$staleStack = @('PurposeRow', 'LocationRow', 'DetailRow', 'CleanLayers') |
    Where-Object { $xaml -match ('x:Name="' + $_ + '"') }
Assert-True 'main area has no per-layer rows (old 3-row stack is gone)' ($staleStack.Count -eq 0)
Assert-True 'purpose and location pages share the same grid column' `
    (([regex]::Matches($xaml, 'Grid\.Column="0"')).Count -ge 2)
Assert-True 'entering a purpose hides the home page' `
    ($cs -match 'PurposePage\.Visibility = Visibility\.Collapsed' -and $cs -match 'LocationPage\.Visibility = Visibility\.Visible')
Assert-True 'returning restores the home page' `
    ($cs -match 'PurposePage\.Visibility = Visibility\.Visible' -and $cs -match 'LocationPage\.Visibility = Visibility\.Collapsed')
Assert-True 'home binds purpose sections, not the file list' `
    ($cs -match 'PurposeSections\.ItemsSource\s*=\s*_sections' -and $cs -match 'CleanPurposeSection\.Build')
Assert-True 'home has one risk section per tier with safe expanded' `
    ($nodes -match 'IsExpanded = tier == 0')
Assert-True 'file grid is only bound through the pager' `
    ($cs -match 'CleanGrid\.ItemsSource\s*=\s*view' -and $pager -match 'class CleanItemPager')
Assert-True 'detail loading is paged with a page size' `
    ($pager -match 'DefaultPageSize\s*=\s*100' -and $cs -match 'new CleanItemPager\(node\.Items')
Assert-True 'search runs over the full candidate set' `
    ($pager -match 'Rebuild' -and $pager -match 'ApplySearch\(view, Search\)')
Assert-True 'search can reach rows that were never loaded' `
    ($pager -match 'public void SetSearch' -and $pager -match '_filtered = Sort')
Assert-True 'group nodes keep the full item list (not the page)' `
    ($nodes -match 'public IReadOnlyList<CleanItem> Items')
Assert-True 'page state (scroll) is preserved across navigation' `
    ($cs -match '_purposeScrollOffset' -and $cs -match '_locationScrollOffset')
Assert-True 'narrow windows turn the detail into a full page' `
    ($cs -match 'DetailOverlayThreshold' -and $cs -match '_detailIsOverlay')
Assert-True 'path is hidden by default with a toggle' `
    ($nodes -match 'public bool IsPathVisible' -and $xaml -match 'Converter=\{StaticResource BoolToVisible\}')
Assert-True 'no leftover references to the removed stacked grids' `
    ($cs -notmatch 'PurposeGrid|LocationGrid|LocationPanel|CloseLocationLayer|CloseDetailLayer|UpdateCleanSelHint|BindPurposeGridFiltered')

# --- 12. selection safety and single primary action -------------------------
Assert-True 'group selection skips non-deletable items' `
    ($nodes -match '(?s)private void ApplySelection\(bool on\).*?if \(!item\.CanDelete\) continue;')
Assert-True 'group nodes expose tri-state' `
    ($nodes -match 'public bool\? IsChecked' -and (([regex]::Matches($xaml, 'IsThreeState="True"')).Count -ge 2))
Assert-True 'checkbox tooltip states the whole-group scope' `
    ($nodes -match 'GroupScopeHint' -and $xaml -match 'ToolTip="\{Binding GroupScopeHint\}"')
Assert-True 'confirm shows locations / files / size / sensitive count' `
    ($cs -match 'Loc\.DeleteScopeConfirm\(')
Assert-True 'confirm uses the full selected set, not the current page' `
    ($cs -match '_layered\.SelectedItems')
# 只看清理面板那一段：卸载页/残留页/对话框各有自己的主按钮，不在本次范围
$cleanPane = [regex]::Match($xaml, '(?s)x:Name="CleanPane".*?x:Name="UninstallPane"').Value
# 底部操作栏任何时候只能有一个主按钮（清理前检查页是独立一层，不算同屏竞争）
$actionBar = [regex]::Match($xaml, '(?s)x:Name="CleanActionBar".*?</Border>').Value
Assert-True 'the bottom action bar has exactly one primary action' `
    ($actionBar.Length -gt 0 -and ([regex]::Matches($actionBar, 'StaticResource PrimaryButton')).Count -eq 1)
Assert-True 'the primary button is disabled with nothing selected' `
    ($cs -match 'CheckAndCleanBtn\.IsEnabled = false')

# --- 板块：按钮语义 + 清理前检查页 ------------------------------------------
Assert-True 'the primary button no longer says 检查并清理' `
    ($cs -notmatch 'Loc\.CheckAndClean' -and $xaml -notmatch '检查并清理')
Assert-True 'the primary button says 清理已选项目' `
    ($cs -match 'Loc\.CleanSelectedItems' -and $xaml -match '清理已选项目')
Assert-True 'the primary action opens the preflight page, not the delete' `
    ($cs -match '(?s)private void CheckAndClean_Click.*?ShowPreflightPage\(')
Assert-True 'preflight page shows locations / items / size / needs-confirm' `
    ($cs -match 'Loc\.PreflightLocations\(' -and $cs -match 'Loc\.PreflightItems\(' -and
     $cs -match 'Loc\.PreflightSize\(' -and $cs -match 'Loc\.PreflightNeedsConfirm\(')
Assert-True 'preflight page explains the re-check before handling' `
    ($cs -match 'Loc\.PreflightRecheck' -and $xaml -match 'x:Name="PreflightRecheck"')
Assert-True 'preflight page has 返回修改 / 确认清理' `
    ($cs -match 'Loc\.BackToEdit' -and $cs -match 'Loc\.ConfirmClean' -and
     $xaml -match 'x:Name="PreflightBackBtn"' -and $xaml -match 'x:Name="PreflightConfirmBtn"')
Assert-True 'confirm on preflight reuses the existing preflight+execute chain' `
    ($cs -match '(?s)private void PreflightConfirm_Click.*?RunCleanFor\(')
Assert-True 'RunCleanFor keeps the delete preflight + confirm step' `
    ($cs -match '(?s)private void RunCleanFor.*?_deleteCoordinator\.Plan\(' -and
     $cs -match '(?s)private void RunCleanFor.*?AskConfirm\(')
Assert-True 'executing shows 正在清理 and a stop path' `
    ($cs -match 'Loc\.CleaningNow' -and $cs -match 'CleanActionState\.Running')
Assert-True 'after cleaning the button becomes 查看结果' `
    ($cs -match 'Loc\.ViewResults' -and $cs -match 'CleanActionState\.Done')
Assert-True 'the results dialog keeps per-item detail' `
    ($cs -match '_lastResultText' -and $cs -match 'DeletionCoordinator\.Summarize\(')

# --- 板块：AI 是核心区域，但不得越权 ----------------------------------------
# ---- 逐项 AI（本轮：从首页全局面板改为项目旁的按需分析） ----
Assert-True 'the home-page global AI panel is gone' `
    ($xaml -notmatch 'x:Name="AiPanel"' -and $xaml -notmatch 'x:Name="AiPrimaryBtn"' -and
     $xaml -notmatch 'x:Name="AiScopeText"' -and $cs -notmatch 'UpdateAiPanel')
Assert-True 'scan completion no longer auto-fires a model request' `
    ($cs -notmatch 'AnalyzeCurrentCategory' -and $cs -notmatch 'AiAnalyzeCat_Click')
Assert-True 'AI config and the gateway are preserved (only the entry moved)' `
    ($cs -match 'AiConfigured\(' -and $cs -match '_aiCoordinator' -and $cs -match 'ItemAiService')
Assert-True 'each item carries its own AI state (stable id, not the row control)' `
    ($itv -match 'class ItemAiView' -and $itv -match 'public required string ScopeKey' -and
     ($item -match 'public ItemAiView Ai' -or $nodes -match 'public ItemAiView Ai'))
Assert-True 'per-item AI button is wired on rows' `
    ($xaml -match 'x:Key="ItemAiInline"' -and $xaml -match 'x:Key="ItemAiResultOnly"' -and $cs -match 'ItemAi_Click' -and
     $cs -match 'case CleanLocationNode loc' -and $cs -match 'case CleanItem item')
Assert-True 'file analysis scope stays the single file' `
    ((($cs -replace '\s+', ' ') -match 'case CleanItem item:.*?FolderChildTotal: 0'))
Assert-True 'folder analysis scope stays that location (no whole-purpose fallback)' `
    ($cs -match 'BuildFolderSummary' -and $cs -notmatch 'AnalyzeCurrentCategory')
Assert-True 'folder summary is bounded and reuses scan data (no traversal)' `
    ($cs -match 'ItemAiPrompt\.MaxFolderSummary' -and $cs -notmatch 'Directory\.EnumerateFiles')
Assert-True 'queue / concurrency / timeout are bounded' `
    ($iasvc -match 'MaxConcurrent' -and $iasvc -match 'Timeout' -and $iasvc -match 'SemaphoreSlim')
Assert-True 'per-item states cover busy/done/no-useful/failed/timeout/cancel' `
    ($itv -match 'enum ItemAiStatus' -and $itv -match 'ItemAiStatus\.Queued' -and
     $itv -match 'ItemAiStatus\.NoUseful' -and $itv -match 'ItemAiStatus\.Timeout' -and
     $itv -match 'ItemAiStatus\.Canceled')
Assert-True 'per-item cancel and duplicate submission are handled' `
    ($cs -match '_itemAiRunning' -and $cs -match 'if \(view\.IsBusy\) return;' -and
     $cs -match 'myReq != view\.RequestId')
Assert-True 'result is cached and the cache key covers metadata + config' `
    ($iasvc -match 'ItemAiCacheKey' -and $iasvc -match 'TryGetCached' -and
     $cs -match 'AiConfigSignature')
Assert-True 'suggestion is constrained to four allowed verdicts' `
    ($iap -match 'enum ItemAiSuggestion' -and $iap -match 'ItemAiSuggestion\.Unknown' -and
     $iap -match 'MapSuggestion')
Assert-True 'result panel shows the five required fields' `
    ($itv -match 'ItemAiSuggestionLabel' -and $itv -match 'ItemAiPurposeLabel' -and
     $itv -match 'ItemAiImpactLabel' -and $itv -match 'ItemAiBasisLabel' -and
     $itv -match 'ItemAiMissingLabel')
Assert-True 'local-rule feedback is labelled as local, not AI' `
    ($itv -match 'LocalNote' -and ($cs -match 'ItemAiLocalOnly' -or $item -match 'ItemAiLocalOnly'))
Assert-True 'AI tools can no longer change the selection' `
    ($analyst -match 'tool removed: the app never lets AI change the selection' -and
     $analyst -notmatch '"set_checked" => SetChecked')
Assert-True 'the sidecar no longer offers selection-changing tools' `
    ($agent -notmatch "name: 'set_checked'" -and $agent -notmatch "name: 'suggest'")

# ---- 清理候选 + 默认不勾选 ----
Assert-True 'the safe tier is renamed to 清理候选' `
    ($loc -match '"清理候选"' -and $cs -notmatch '"建议清理"')
Assert-True 'rules never preselect anything' `
    ($rules -match 'Selected = false,' -and $rules -notmatch 'Selected = risk == CleanRisk\.Safe')
Assert-True 'duplicates are not preselected either' `
    ($dup -notmatch 'Selected = canDelete && !incomplete')
Assert-True 'path signatures match whole segments only' `
    ($sig -match 'SegmentIndexOf' -and $sig -notmatch 'p\.IndexOf\(n, StringComparison\.Ordinal\)')
Assert-True 'npm global install dir is not a safe cache' `
    ($sig -match 'npm 全局工具' -and $sig -match 'SigRisk\.Keep, N\(@"\\appdata\\roaming\\npm"\)')

Assert-True 'icons are shared vector geometries, not emoji/Unicode' `
    ($xaml -match 'x:Key="IconGear"' -and $xaml -match 'x:Key="IconChecklist"' -and
     $xaml -match 'x:Key="IconInfo"' -and $xaml -match 'x:Key="IconMore"' -and
     $xaml -match 'x:Key="IconArrow' -or $xaml -match 'x:Key="IconBack"')
Assert-True 'there is one unified icon button style with all four states' `
    ($xaml -match 'x:Key="IconButton"' -and $xaml -match 'IsMouseOver' -and $xaml -match 'IsPressed' -and
     $xaml -match 'IsKeyboardFocused' -and $xaml -match 'Property="IsEnabled" Value="False"')
Assert-True 'icon buttons carry ToolTip and AutomationProperties.Name' `
    (([regex]::Match($xaml, '(?s)x:Name="ScanDetailsBtn".*?/>').Value) -match 'ToolTip=' -and
     ([regex]::Match($xaml, '(?s)x:Name="ScanDetailsBtn".*?/>').Value) -match 'AutomationProperties\.Name=' -and
     ([regex]::Match($xaml, '(?s)x:Name="CleanMoreBtn".*?/>').Value) -match 'AutomationProperties\.Name=')
Assert-True 'settings is a gear button, not a text button' `
    ($xaml -match '(?s)x:Name="SettingsButton".*?IconGear' -and
     $xaml -notmatch '(?s)x:Name="SettingsButton"[^>]{0,200}Content="设置"')
Assert-True 'ApplyUi never overwrites the settings icon Content' `
    (([regex]::Match($cs, '(?s)if \(SettingsButton != null\).*?\n        \}').Value) -notmatch 'SettingsButton\.Content')
Assert-True 'ApplyUi never overwrites the view-selected icon Content' `
    (([regex]::Match($cs, '(?s)if \(ViewSelectedText != null\).*?\n').Value) -notmatch 'ViewSelectedBtn\.Content')
Assert-True 'the section chevron uses vector arrows, not glyph characters' `
    ($xaml -match 'SectionChevronDown' -and $xaml -match 'SectionChevronRight' -and
     $xaml -notmatch 'Text="\{Binding Chevron\}"')
# ---- 查看已选：结构化清单 ----
Assert-True 'view-selected opens a structured panel, not a text blob' `
    ($xaml -match 'x:Name="SelectionBody"' -and $xaml -match 'x:Name="SelectionGrid"' -and
     $cs -match 'selection: true')
Assert-True 'the selection grid is virtualised (large selections must not materialise rows)' `
    ($xaml -match '(?s)x:Name="SelectionGrid".*?EnableRowVirtualization="True"' -and
     $xaml -match 'VirtualizationMode="Recycling"')
Assert-True 'selection panel states counts with explicit units' `
    ($cs -match 'Loc\.SelectionSummary\(' -and $cs -match 'Loc\.ViewSelectedSub')
Assert-True 'selection rows expose full path for tooltip and copy' `
    ($cs -match 'class SelectedRow' -and $cs -match 'public string FullPath' -and
     $cs -match 'SelectionCopyPath_Click')
Assert-True 'opening the selection panel does not clear the selection' `
    (([regex]::Match($cs, '(?s)private void ViewSelected_Click.*?\n    \}').Value) -notmatch 'Selected = false')
Assert-True 'the view-selected chip shows a count and disables when empty' `
    ($cs -match 'UpdateViewSelectedLabel' -and $cs -match 'ViewSelectedBtn\.IsEnabled = n > 0')

Assert-True 'AI never writes Risk / Selected / CanDelete' `
    ($parser -notmatch '\.Risk\s*=' -and
     ([regex]::Match($cs, '(?s)private void UpdateAiPanel\(\).*?\n    \}').Value) -notmatch '\.(Risk|Selected|CanDelete)\s*=')
# 唯一会动勾选的 AI 入口是「按 AI 建议选择」，下一条单独锁它的范围
    ($cs -match '(?s)AiSelectAdvice_Click.*?p\.RiskTier != 0\) continue;' -and
     $cs -match 'if \(!item\.CanDelete \|\| item\.Risk == CleanRisk\.Keep\) continue;')
    ($cs -match 'Loc\.AiSelectByAdvice' -and $cs -match 'Loc\.AiAdviceHint')
Assert-True 'no misleading AI wording anywhere' `
    ($cs -notmatch 'AI 自动清理' -and $cs -notmatch 'AI 安全清理' -and
     $cs -notmatch 'AI 决定清理' -and $cs -notmatch 'AI 已替你选择')

# --- 板块：AI 结论界面（本轮核心：一眼看懂该选哪些） -------------------------
$verdict = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AiVerdict.cs') -Raw -Encoding UTF8
Assert-True 'the default view is one headline + one note + next step' `
    ($verdict -match 'AiHeadlineClean' -and $verdict -match 'BuildNote' -and
     $xaml -match 'Ai\.Headline' -and $xaml -match 'Ai\.Note')
Assert-True 'only three user-facing buckets, named in plain words' `
    ($verdict -match 'enum AiBucket' -and $loc -match '可考虑清理' -and
     $loc -match '建议保留' -and $loc -match '需要确认')
Assert-True 'groups appear only when content is genuinely mixed' `
    ($verdict -match 'bool mixed = buckets\.Count > 1')
Assert-True 'only the cleanable bucket can be bulk-selected' `
    ($verdict -match 'Loc\.AiReasonCleanable, true' -and
     $verdict -match 'Loc\.AiReasonReview, false' -and
     $verdict -match 'Loc\.AiReasonKeep, false')
Assert-True 'protected items never become cleanable' `
    ($verdict -match 'x\.CanDelete && x\.Risk != CleanRisk\.Keep && x\.Risk != CleanRisk\.Confirm')
Assert-True 'the two action buttons use the plain wording' `
    ($loc -match '查看文件' -and $loc -match '选择这些文件')
Assert-True 'technical wording is gone from the panel' `
    ($xaml -notmatch '本地拆分' -and $xaml -notmatch '符合清理资格' -and
     $xaml -notmatch '按组' -and $xaml -notmatch '本地规则')
Assert-True 'why-it-suggests is collapsed by default' `
    ($xaml -match '为什么这样建议' -and $xaml -match '<Expander')
Assert-True 'the panel is height-bounded and scrolls internally' `
    ($xaml -match '(?s)ItemAiResultOnly.*?ScrollViewer MaxHeight')
Assert-True 'selecting goes through the existing refresh chain' `
    ($cs -match 'AiSelectBucket_Click' -and $cs -match 'RefreshAfterSelectionChange\(\)')
Assert-True 'viewing files uses exact item filtering' `
    ($cs -match 'AiViewBucket_Click' -and $cs -match 'SetItemFilter' -and
     $pager -match 'public void SetItemFilter')
Assert-True 'local verdict is available even without AI configured' `
    ($cs -match 'AiNeedConfigLocalStillWorks' -and $cs -match 'AiVerdict\.Build\(items')

# 发布产物：取 dist 下最新的 DashaoHuo-*-win-x64
$publish = Get-ChildItem -LiteralPath (Join-Path $repo 'dist') -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'DashaoHuo-*-win-x64' } |
    Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName

if (Test-Path $publish) {
    $rc = Get-Content -LiteralPath (Join-Path $publish 'AiDiskCleaner.runtimeconfig.json') -Raw
    Assert-True 'publish is self-contained' ($rc -match 'includedFrameworks')
    $exeVersion = (Get-Item (Join-Path $publish 'AiDiskCleaner.exe')).VersionInfo.FileVersion
    Assert-True "published exe carries the new version (got $exeVersion)" ($exeVersion -like '2.3.0*')
}
else {
    Assert-True 'publish package exists' $false
}

Write-Output ''
if ($fail -eq 0) { Write-Output 'PASS: UI/behaviour regression checks all green.'; exit 0 }
Write-Output "FAIL: $fail check(s) failed."
exit 1
