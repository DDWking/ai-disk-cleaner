# UI / behaviour regression check (ASCII only on purpose).
#
# Guards the interface behaviour so a later refactor cannot quietly roll it back:
#   - the cleanable panel is the default right-hand tab
#   - the file-browser sidebar is gone (2.11); there is no tree to hide
#   - the clean pane still fills the right column (Grid, not the old DockPanel bug)
#   - categories expand locations in place (no LocationPage swap)
#   - AI can only write notes; it never touches risk / selection / deletability
#   - no long-running operation is left with CancellationToken.None
#   - 2.12: single 全选 toggles, size-sorted organize roots, AI chip, factual uninstall columns
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
$forg = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\FolderOrganize.cs') -Raw -Encoding UTF8
$csvc = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\FolderPurposeService.cs') -Raw -Encoding UTF8
$orgnode = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Models\OrganizeNode.cs') -Raw -Encoding UTF8
$factual = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AppFactualInfoService.cs') -Raw -Encoding UTF8
$appitem = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Models\AppUninstallItem.cs') -Raw -Encoding UTF8
# v2.7.0: filtered README/config snippets and the automatic two-level pass
$srcsnip = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\SourceSnippet.cs') -Raw -Encoding UTF8
$newfp = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\FolderPurpose.cs') -Raw -Encoding UTF8
# v2.8.1: startup hardening after the 2.8.0 "double-click does nothing" failure
$app = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\App.xaml.cs') -Raw -Encoding UTF8
$firstcrash = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\CrashFirstRecord.cs') -Raw -Encoding UTF8
$startupcheck = Join-Path $repo 'tools\StartupCheck\Program.cs'

$fail = 0
function Assert-True([string]$name, [bool]$ok) {
    if ($ok) { Write-Output "PASS $name" }
    else { Write-Output "FAIL $name"; $script:fail++ }
}

# --- 1. directory sidebar is gone (2.11) -------------------------------------
# 2.11 removed the file-browser sidebar. These asserts lock that in so a later
# refactor cannot quietly bring back the tree, the toggle, or a search box that
# used to live inside the sidebar.
Assert-True 'no directory-tree visibility flag remains' ($cs -notmatch '_treeVisible')
Assert-True 'ApplySidebarLayout is gone' ($cs -notmatch 'ApplySidebarLayout')
Assert-True 'left panel / splitter / tree toggle / overlay scrim are gone from XAML' `
    ($xaml -notmatch 'x:Name="LeftPanel"' -and
     $xaml -notmatch 'x:Name="TreeToggleBtn"' -and
     $xaml -notmatch 'x:Name="SidebarScrim"' -and
     $xaml -notmatch 'x:Name="SplitterCol"')
Assert-True 'sidebar overlay / docked-mode helpers are gone from code' `
    ($cs -notmatch 'SidebarDockMinWindow' -and
     $cs -notmatch 'SidebarMinWidth' -and
     $cs -notmatch 'SidebarScrim_Click')
Assert-True 'file search box is gone (no browse tree to search)' `
    ($xaml -notmatch 'x:Name="SearchBox"' -and $xaml -notmatch 'x:Name="DirGrid"')
Assert-True 'file search is gone from the top toolbar' `
    ($xaml -notmatch '(?s)x:Name="StopButton".*?x:Name="SearchBox".*?x:Name="VolumeText"')
Assert-True 'main content column has NO MaxWidth (that caused the black block)' `
    ($xaml -notmatch '(?s)x:Name="RightCol"[^>]*MaxWidth')
Assert-True 'there is no tree action that sets the cleanup range' `
    ($cs -notmatch 'private void ScopeToFolder_Click' -and
     $cs -notmatch 'private void ShowDirectory\(')
Assert-True 'cleanup range is a leftover in-memory filter, not a browse directory' `
    ($cs -match 'private FileEntry\? _cleanScopeRoot;')
Assert-True 'applied range still has a visible chip with a clear action' `
    ($xaml -match 'x:Name="CleanScopeBar"' -and $xaml -match 'x:Name="ClearScopeBtn"' -and
     $cs -match 'private void ClearScope_Click')
Assert-True 'range chip warns about selected items outside the range' `
    ($cs -match 'Loc\.ScopeChip\(path, outside\)')
Assert-True 'clearing the range never clears or widens the selection' `
    ($cs -match '(?s)private void ApplyCleanScope\(\)[\s\S]{0,700}RebuildLayersAsync')

# --- 2. folder tidy-up is the default workspace ------------------------------
# The user could not see the purpose feature when it only lived in the hidden
# sidebar tree. The clean center is the default home and the main work area;
# folder tidy-up is a secondary entry (local recognition only, no auto AI).
$org = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\MainWindow.Organize.cs') -Raw -Encoding UTF8
$orgPane = [regex]::Match($xaml, '(?s)x:Name="OrganizePane"(.*?)x:Name="CleanPane"').Groups[1].Value
Assert-True 'window opens on the clean tab (tidy-up is a secondary entry)' `
    ($cs -match 'RightTab _rightTab = RightTab\.Clean;' -and
     $cs -match 'ShowRightTab\(RightTab\.Clean\);')
Assert-True 'a scan never yanks the user off the page they chose' `
    (([regex]::Match($cs, '(?s)private async void RunScan\(\).*?\n    \}').Value) -notmatch 'ShowRightTab\(')
Assert-True 'top navigation is clean | tidy-up | uninstall, in that order' `
    ($xaml -match '(?s)x:Name="TabCleanBtn".*?x:Name="TabOrganizeBtn".*?x:Name="TabUninstallBtn"')
Assert-True 'tab switching uses an enum, not fragile numeric indexes' `
    ($cs -match 'private enum RightTab \{ Organize, Clean, Uninstall \}' -and
     $cs -match 'ShowRightTab\(RightTab\.Uninstall\)' -and
     -not ([regex]::Match($cs, 'ShowRightTab\([0-9]\)').Success))
Assert-True 'clean pane still comes before uninstall pane' `
    ($xaml -match '(?s)x:Name="CleanPane".*?x:Name="UninstallPane"')
Assert-True 'tidy-up pane is the real workspace, not a collapsed placeholder' `
    ($xaml -match '(?s)<DockPanel\s+x:Name="OrganizePane"[^>]*Visibility="Collapsed"' -and
     $xaml -match 'x:Name="OrganizeGrid"' -and
     $cs -match 'OrganizePane\.Visibility = tab == RightTab\.Organize')
Assert-True 'clean pane is visible by default (no Collapsed on CleanPane itself)' `
    ($xaml -notmatch '<DockPanel\s+x:Name="CleanPane"[^>]*Visibility="Collapsed"')

# --- 2b. tidy-up workspace requirements --------------------------------------
Assert-True 'tidy-up columns are name / what-it-is / size / actions' `
    ($xaml -match '(?s)x:Name="ColOrgName".*?x:Name="ColOrgPurpose".*?x:Name="ColOrgSize".*?x:Name="ColOrgAction"')
Assert-True 'real path sits on a second line in the row' `
    ($xaml -match '(?s)x:Name="ColOrgName".*?Binding RelativePath')
Assert-True 'tidy-up does not copy the clean candidate space or clean buttons' `
    ($orgPane.Length -gt 0 -and $orgPane -notmatch 'CheckAndCleanBtn' -and
     $orgPane -notmatch 'CleanSelectionSummary' -and $orgPane -notmatch 'CleanActionBar' -and
     $org -notmatch 'CheckAndCleanBtn' -and $org -notmatch 'CleanSelectionSummary')
Assert-True 'no batch / scope / progress / cancel AI entry remains in the tidy-up pane' `
    ($xaml -notmatch 'x:Name="OrganizeIdentifyAllBtn"' -and
     $xaml -notmatch 'x:Name="OrganizeScopeText"' -and
     $xaml -notmatch 'x:Name="OrganizeProgressBar"' -and $xaml -notmatch 'x:Name="OrganizeStopBtn"' -and
     $xaml -notmatch 'x:Name="OrganizeWorkBar"' -and $xaml -notmatch 'x:Name="OrganizeIdentifyCurrentBtn"' -and
     $org -notmatch 'OrganizeIdentifyAll|OrganizeIdentifyCurrent|OrgCtxIdentifyCurrent' -and
     $org -notmatch 'AutoIdentifyTargets|StartOrganizeAutoIdentify|RunOrganizeIdentifyAsync')
Assert-True 'the tidy-up workspace never sends a request by itself (local recognition runs)' `
    ($org -match 'private void LocalRecognize\(OrganizeNode node\)' -and
     $org -match 'FolderPurposeRules\.RecognizeLocally' -and
     $org -notmatch 'RecognizeWithAsync')
Assert-True 'single-item AI is the only model entry (one object per click)' `
    ($orgnode -match 'public ItemAiView Ai' -and
     $cs -match 'case OrganizeNode node:' -and
     $cs -match 'private static List<string> BuildFolderSummary\(FileEntry dir, out int total\)' -and
     $cs -match 'ItemAiPrompt\.MaxFolderSummary')
Assert-True 'item AI never writes Risk / CanDelete / Selected' `
    ($iasvc -notmatch '\.Risk\s*=' -and $iasvc -notmatch '\.CanDelete\s*=' -and $iasvc -notmatch '\.Selected\s*=')
Assert-True 'single-item AI keeps its cache / cancel / retry plumbing' `
    ($iasvc -match 'TryGetCached' -and $cs -match '_itemAiRunning' -and $cs -match 'ItemAiRetry_Click')
Assert-True 'outbound requests are countable at the single gateway choke point' `
    ((Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AiGateway.cs') -Raw -Encoding UTF8) -match 'public static int SentCount')
Assert-True 'unscanned / scanning / empty / no-model / failed states all exist' `
    ($org -match 'OrganizeStateKind\.NoScan' -and
     $org -match 'OrganizeStateKind\.Scanning' -and
     $org -match 'OrganizeStateKind\.Empty' -and
     $org -match 'OrganizeStateKind\.NoModel' -and $org -match 'OrganizeStateKind\.Failed')
# --- 2c. auto two-level identification, no per-row identify buttons ----------
Assert-True 'the row no longer carries a per-row identify button' `
    ($orgPane -notmatch 'OrganizeIdentifyOne_Click' -and $xaml -notmatch 'Binding ActionText' -and
     $xaml -notmatch 'x:Name="OrgCtxIdentify"')
Assert-True 'the right side has no second expand/collapse button (arrow is the only one)' `
    ($xaml -notmatch 'Binding ToggleText' -and $xaml -notmatch 'Binding HasToggleText' -and
     $xaml -notmatch 'GhostButton[^>]*OrganizeExpand_Click')
Assert-True 'the left arrow is the single expand/collapse entry' `
    ($xaml -match 'x:Name="ColOrgName"' -and
     [regex]::Match($xaml, '(?s)x:Name="ColOrgName".*?OrganizeExpand_Click').Success -and
     [regex]::Match($xaml, '(?s)x:Name="ColOrgName".*?Binding ExpandGlyphKey').Success)
Assert-True 'the action column only keeps the explorer folder icon with tooltip + automation name' `
    ([regex]::Match($xaml, '(?s)x:Name="ColOrgAction"(.*?)</DataGridTemplateColumn>').Groups[1].Value -match
        'IconFolderOpen' -and
     [regex]::Match($xaml, '(?s)x:Name="ColOrgAction"(.*?)</DataGridTemplateColumn>').Groups[1].Value -match
        'AutomationProperties\.Name="在资源管理器中打开"' -and
     [regex]::Match($xaml, '(?s)x:Name="ColOrgAction"(.*?)</DataGridTemplateColumn>').Groups[1].Value -match
        'ToolTip=')
Assert-True 'rows can grow when a detail block is open' `
    ($xaml -match 'RowHeight="NaN"' -and $xaml -match 'MinRowHeight="46"')
Assert-True 'the purpose cell opens an inline detail block (collapsed by default)' `
    ($xaml -match 'MouseLeftButtonUp="OrganizePurpose_Click"' -and
     $xaml -match 'Binding IsDetailOpen' -and
     $org -match 'private void OrganizePurpose_Click' -and
     $orgnode -match 'public void ToggleDetail' -and
     $orgnode -match 'private bool _detailOpen')
Assert-True 'the detail shows what / why / source from real evidence only' `
    ($orgnode -match 'Loc\.OrganizeDetailWhat' -and $orgnode -match 'Loc\.OrganizeDetailWhy' -and
     $orgnode -match 'Loc\.OrganizeDetailSource' -and $orgnode -match 'SetEvidence')
Assert-True 'the UI never uses the developer term "hit signature"' `
    ($loc -notmatch '命中签名' -and $loc -notmatch 'matched local signature')
Assert-True 'system paths get a real local conclusion without confirmation' `
    ($newfp -match 'public static SystemPathRole\? SystemRole' -and
     $newfp -match 'NeedsConfirm: false' -and $newfp -match 'SystemRoles\(\)' -and
     $newfp -match 'Environment\.SpecialFolder\.Windows')
Assert-True 'system semantics cover windows / program files / programdata / appdata / user / downloads' `
    ($loc -match 'SysWindows' -and $loc -match 'SysProgramFiles' -and $loc -match 'SysProgramData' -and
     $loc -match 'SysAppData' -and $loc -match 'SysUserProfile' -and $loc -match 'SysLocalAppData' -and
     $loc -match 'SysRoamingAppData' -and $loc -match 'SysDownloads')
Assert-True 'system semantics are matched by real path only (other drives are not misjudged)' `
    ($newfp -match 'p\.Equals\(r\.Path, StringComparison\.OrdinalIgnoreCase\)' -and
     $newfp -notmatch 'EndsWith\(.*Name')
Assert-True 'there is no unified batch retry entry any more' `
    ($xaml -notmatch 'x:Name="OrganizeIdentifyAllBtn"' -and
     $org -notmatch 'Loc\.OrganizeRetryPending' -and $org -notmatch 'OrganizeIdentifyAll_Click')
Assert-True 'no identification pass is started after a scan' `
    ($org -notmatch 'StartOrganizeAutoIdentify' -and $org -notmatch '_organizeAutoStarted' -and
     $org -notmatch '_organizeBusy')
Assert-True 'the tidy-up workspace has no automatic model scope at all' `
    ($org -notmatch 'AutoIdentifyTargets' -and $org -notmatch 'IsAutoLevel\)\s*$' -and
     $org -notmatch 'RunOrganizeIdentifyAsync')
Assert-True 'the deep this-level workspace bar is gone' `
    ($xaml -notmatch 'x:Name="OrganizeWorkBar"' -and $xaml -notmatch 'x:Name="OrganizeIdentifyCurrentBtn"' -and
     $xaml -notmatch 'x:Name="OrganizeWorkScope"' -and
     $org -notmatch 'OrganizeIdentifyCurrent_Click' -and $org -notmatch 'Loc\.OrganizeWorkBarScope\(')
Assert-True 'no this-level wording remains (the action no longer exists)' `
    ($org -notmatch 'Loc\.OrganizeIdentifyThisLevel' -and $org -notmatch 'Loc\.OrganizeThisLevelOnly' -and
     $org -notmatch 'OpenCurrentFolderChildren' -and $org -notmatch 'IsInCurrentFolderScope')
Assert-True 'there is no request-budget / plan machinery left' `
    ($org -notmatch 'FolderOrganize\.PlanFor\(' -and $org -notmatch 'RequestBudgetFor' -and
     $org -notmatch 'Loc\.OrganizeRunAttempts')
Assert-True 'no batch progress states remain (nothing to report progress for)' `
    ($org -notmatch 'Loc\.OrganizeRunRunning' -and $org -notmatch 'Loc\.OrganizeRunSuperseded' -and
     $org -notmatch '_organizeTaskId')
Assert-True 'the tidy-up page states counts per object, not a batch budget' `
    ($org -match 'private void UpdateOrganizeHeader\(\)' -and
     $org -match 'Loc\.OrganizeCountsLine\(local, ai, unknown, failed\)' -and
     $org -notmatch 'OrganizeCounts\.ToolTip')
Assert-True 'single-item AI is the only way to reach the model from this page' `
    ($orgnode -match 'public ItemAiView Ai' -and $cs -match 'case OrganizeNode node:' -and
     $org -notmatch 'RecognizeWithAsync')
Assert-True 'local recognition still runs (the page is not dead without a model)' `
    ($org -match 'private void LocalRecognize\(OrganizeNode node\)' -and
     $org -match 'FolderPurposeRules\.RecognizeLocally')
# --- 2f. the v2.8.2 behaviour fixes (expand / lifecycle / filter) ---------------
Assert-True 'first click on the arrow expands after lazy materialisation' `
    ($org -match 'Materialize\(node, FolderOrganize\.ChildBudget, null, autoExpand: true\)' -and
     $org -match 'autoExpand && kids\.Count > 0')
Assert-True 'background materialisation still never auto-expands' `
    ($org -match 'Materialize\(node, FolderOrganize\.ChildBudget, FolderOrganize\.AutoMaterializeBudget\(node\.Depth == 0\)\);' -and
     $orgnode -match 'bool autoExpand = false')
Assert-True 'a click that cannot expand says why (no silent no-op, no fake expand)' `
    ($org -match 'OrganizeExpandBudgetReached' -and $org -match 'OrganizeExpandAlreadyListed' -and
     $org -match 'OrganizeExpandNoChildren' -and $org -match 'OrganizeMaterializeResult\.BudgetReached')
Assert-True 'the organize page no longer carries a batch task generation' `
    ($org -notmatch 'taskGen != _organizeGeneration' -and
     $org -notmatch '_organizeTaskId' -and $org -notmatch 'bool Owns\(\)')
Assert-True 'the per-item result generation guard survives (no stale write into a new scan)' `
    ($org -match 'int _organizeGeneration' -and
     $cs -match '_aiDataGeneration\+\+' -and
     (Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Models\ItemAiView.cs') -Raw -Encoding UTF8) -match 'public bool IsStale')
Assert-True 'filter is a view: it never mutates IsExpanded' `
    ($org -notmatch 'foreach \(var n in _organizeAll\) if \(n\.ChildrenLoaded\) n\.Expand\(\)' -and
     $org -match '_organizeFilterSet' -and
     $org -match 'if \(!_organizeFilterSet\.Contains\(node\)\) return;')
Assert-True 'filter keeps ancestor context so no orphan rows appear' `
    ($org -match 'bool keep = n\.IsPending;' -and $org -match 'if \(Mark\(c\)\) keep = true;')
Assert-True 'there is no automatic target ordering left (no auto list to order)' `
    ($org -notmatch 'OrderByDescending\(x => x\.Size\)' -and $org -notmatch 'ThenByDescending\(x => x\.Size\)')
Assert-True 'nothing starts an identification pass on scan or page switch' `
    ($org -notmatch 'if \(_organizeBusy\) return;' -and $org -notmatch '_organizeAutoStarted' -and
     $org -notmatch 'OrganizeIdentifyRun')
Assert-True 'a single-item request never sends a full path by default' `
    ($csvc -match 'BuildOutboundInput' -and
     $srcsnip -match 'SourceSnippetCollector' -and
     $newfp -match 'IsSafeOutbound' -and
     $iasvc -match 'ItemAiPrompt\.BuildUser\(request, sendFullPaths\)')
Assert-True 'the outbound payload is length-capped' `
    ($csvc -match 'MaxInputChars = 1800' -and $srcsnip -match 'MaxFiles = 3' -and
     $srcsnip -match 'TotalChars = 1200' -and $srcsnip -match 'PerFileChars = 600')
Assert-True 'secrets are filtered before anything is sent' `
    ($srcsnip -match 'api\[_-\]\?key' -and $srcsnip -match 'Bearer' -and $srcsnip -match '已脱敏')
Assert-True 'only README / config / manifest files may be read' `
    ($srcsnip -match 'IsAllowed' -and $srcsnip -match 'readme' -and $srcsnip -match 'package\.json' -and
     $srcsnip -match 'TopDirectoryOnly')
Assert-True 'language never says "recently used"' `
    ($loc -notmatch '最近使用')
# --- 2e. startup hardening (the 2.8.0 "double-click does nothing" failure) -----
Assert-True 'row height uses a LEGAL auto value (never the string Auto)' `
    ($xaml -match 'RowHeight="NaN"' -and $xaml -notmatch 'RowHeight="Auto"')
Assert-True 'row height keeps a minimum so an expanded detail cannot be clipped' `
    ($xaml -match 'MinRowHeight="46"')
Assert-True 'crash handling has a fatal latch (a fatal start is handled once)' `
    ($app -match '_fatalLatched' -and $app -match 'CrashOutcome\.Suppressed')
Assert-True 'crash handling guards re-entry' `
    ($app -match 'Interlocked\.CompareExchange\(ref _crashGate, 1, 0\)')
Assert-True 'the async alert callback is itself exception-safe' `
    ($app -match 'try \{ w\.ShowCrash\(ex\.Message\); \}')
Assert-True 'a startup-fatal crash exits with a non-zero code (no windowless spinner)' `
    ($app -match 'FatalStartupExitCode' -and $app -match 'Environment\.Exit\(FatalStartupExitCode\)')
Assert-True 'half-initialised UI is detected before touching any control' `
    (($cs -match 'public bool IsUiReady') -and ($app -match 'w\.IsUiReady') -and
     ($app -match 'IsUiReady: true'))
Assert-True 'the first original exception is kept in its own bounded file' `
    ($firstcrash -match 'first-crash\.log' -and $firstcrash -match 'if \(_saved > 0\) return false' -and
     $firstcrash -match 'LogRedactor\.ScrubAll')
Assert-True 'the first-crash file only ever rotates itself' `
    ($firstcrash -match 'MaxFileBytes' -and $firstcrash -match 'first-crash\.log' -and
     $firstcrash -notmatch 'AppLog\.Write' -and $firstcrash -notmatch 'RotateIfNeeded')
Assert-True 'startup regression loads the REAL compiled BAML (not string matching)' `
    ((Test-Path $startupcheck) -and
     ((Get-Content -LiteralPath $startupcheck -Raw -Encoding UTF8) -match 'new MainWindow\(\)') -and
     ((Get-Content -LiteralPath $startupcheck -Raw -Encoding UTF8) -match 'DetailText'))
Assert-True 'startup regression asserts detail rows grow and stay inside the row' `
    ((Get-Content -LiteralPath $startupcheck -Raw -Encoding UTF8) -match '完全落在行内')
# --- 2d. calibration: entry points, coverage vs UI cap, honest status ----------
Assert-True 'user-profile ancestors never swallow AppData entry points' `
    ($forg -match 'included\.Add\(n\)' -and $forg -match 'IsAncestorOfEntryPoint' -and
     $forg -notmatch 'IsUnderOrEqual\(a, n\)')
Assert-True 'entry points accept an injected table (testable, no hardcoded user name)' `
    ($forg -match 'IReadOnlyList<string>\? entryPoints = null')
Assert-True 'entries children are still materialised in the background (entry does not stop it)' `
    ($forg -match 'IsEntryPoint\(dir\.FullPath, entryPoints\)\) return true' -and
     $forg -notmatch 'IsEntryPoint\(dir\.FullPath, entryPoints\)\) return false')
Assert-True 'entry points come from system resolution only (no hardcoded paths)' `
    ($newfp -match 'Environment\.GetFolderPath' -and
     $newfp -notmatch 'C:\\\\Users\\\\' -and $forg -notmatch 'C:\\\\Users\\\\')
Assert-True 'path to a deep entry point is still walked, and the cap still exists' `
    ($forg -match 'IsAncestorOfEntryPoint\(dir\.FullPath, entryPoints\)' -and
     $forg -match 'AutoChildLevel = 2')
Assert-True 'first screen shows only the roots (nothing auto-expands)' `
    ($orgnode -match 'bool autoExpand = false' -and $org -match 'Materialize\(node, FolderOrganize\.ChildBudget, FolderOrganize\.AutoMaterializeBudget\(node\.Depth == 0\)\)' -and
     -not ([regex]::Match($org, 'SetChildren\([^)]*autoExpand:\s*true').Success))
Assert-True 'recognition coverage is decoupled from the UI row cap' `
    ($forg -match 'MaterializeBudget = 400' -and $forg -match 'MaxMaterializePerNode' -and
     $forg -match 'ChildBudget = 24' -and $forg -match 'AutoMaterializeBudget')
Assert-True 'unlisted folders are reported as not identified' `
    ($org -match 'AutoMaterializeBudget\(node\.Depth == 0\)' -and
     $orgnode -match 'Loc\.OrganizeUnlistedNote' -and $org -match '_organizeUnlistedTotal' -and
     $orgnode -match 'UnlistedChildCount' -and $orgnode -match 'UnlistedNote')
Assert-True 'status mapping keeps recognised / ai / unknown / failed apart' `
    ($newfp -match 'public bool Failed' -and $newfp -match 'public static FolderPurposeResult Failure' -and
     $newfp -match 'Failed\s*\? PurposeState\.Failed')
Assert-True 'failure path really lands in Failed (not silently unknown)' `
    ($csvc -match 'FileNode|FolderPurposeResult\.Failure' -and
     $orgnode -match 'if \(!HasConclusion\) _override = r\.Failed \? PurposeState\.Failed : PurposeState\.Unrecognized' -or
     $orgnode -match 'r\.Failed \? PurposeState\.Failed')
Assert-True 'no model / request-budget wording is left (there is no batch pass)' `
    ($org -notmatch 'OrganizeNoModelHonest' -and $org -notmatch 'OrganizeBudgetLeft' -and
     $org -notmatch 'Loc\.OrganizeRunSummary' -and $org -match 'OrganizeUnlistedTotal')
Assert-True 'user correction never changes the folder kind (set stays expandable)' `
    ($orgnode -match 'if \(r\.Source != PurposeSource\.User && r\.Kind != FolderKind\.Unknown\) _kind = r\.Kind' -and
     $csvc -match 'Kind: FolderKind\.Unknown')
Assert-True 'collection detection is generic, not example-driven' `
    ($newfp -match 'IsKnownObject' -and $newfp -match 'StrongChildBytes' -and
     $newfp -match 'ContainerMinChildren = 4' -and
     $newfp -notmatch 'Gameklll|ESP32|steamapps\\common\\VolleyBall')
Assert-True 'deep levels are manual only (no automatic pass exists to enter)' `
    ($forg -match 'AutoChildLevel = 2' -and
     $org -notmatch 'IsAutoLevel\s*&&' -and $org -notmatch 'AutoIdentifyTargets')
Assert-True 'a cancelled or stale per-item request cannot overwrite a new scan' `
    ($org -match '_organizeGeneration\+\+' -and
     $cs -match '_itemAiRunning' -and $cs -match 'ItemAiStatus\.Canceled')
Assert-True 'the page counts per object, never per batch target' `
    ($org -match 'private void LocalRecognize\(OrganizeNode node\)' -and
     $org -notmatch 'done = Math\.Min\(targets\.Count, done \+ 1\)')
Assert-True 'there is no longer a current-folder batch scope' `
    ($org -notmatch 'private void SetOrganizeCurrent\(OrganizeNode\? node\)' -and
     $org -notmatch 'SetOrganizeCurrent\(node\);')
Assert-True 'single-item analysis and its retry go through the same path' `
    ($cs -match 'public void ItemAi_Click' -and $cs -match 'public void ItemAiRetry_Click' -and
     $cs -match '_ = RunItemAiAsync\(view, request\)' -and $org -notmatch 'RunOrganizeIdentifyAsync')
Assert-True 'tidy-up never touches risk / deletability / selection' `
    ($org -notmatch '\.(Risk|CanDelete|Selected)\s*=' -and
     $org -notmatch 'SendToRecycle|DeletionExecutor|RecycleService')
Assert-True 'tidy-up never moves or renames real files' `
    ($org -notmatch 'File\.Move|Directory\.Move|\.MoveTo\(|File\.Copy|Directory\.CreateDirectory')
Assert-True 'tidy-up never asks for elevation on its own' `
    ($org -notmatch 'runas|Verb\s*=|ProcessStartInfo.*Verb' -and
     $org -notmatch 'WindowsPrincipal|IsInRole')
Assert-True 'opening a folder goes through ShellReveal (never executes a program)' `
    ($org -match 'ShellReveal\.Reveal\(' -and $org -notmatch 'Process\.Start')
Assert-True 'queued / running states cannot show a success conclusion' `
    ($orgnode -match 'public PurposeState State => _override' -and
     $orgnode -match 'if \(State is PurposeState\.Queued\) return Loc\.PurposeQueued;' -and
     $orgnode -match 'if \(State is PurposeState\.Running\) return Loc\.PurposeRunning;')
Assert-True 'a stale per-item result can never overwrite a new scan' `
    ($cs -match '_aiDataGeneration\+\+' -and
     $cs -match 'InvalidateItemAiAfterScan' -and $org -notmatch 'myData != _aiDataGeneration')
Assert-True 'local recognition runs before (and without) any model call' `
    ($org -match 'private void LocalRecognize\(OrganizeNode node\)' -and
     $org -match 'FolderPurposeRules\.RecognizeLocally' -and
     $org -notmatch 'if \(!allowAi\)' -and $org -notmatch 'if \(t\.HasConclusion\) continue;')
Assert-True 'the model path is gated by config and only reachable per item' `
    ($cs -match 'bool AiConfigured\(\)' -and $cs -match 'case OrganizeNode node:' -and
     $org -notmatch 'RecognizeWithAsync')
Assert-True 'user corrections are persisted and win over later results' `
    ($csvc -match 'SetUserCorrection\(' -and
     $org -match 'TryGetUserCorrection\(' -and
     $csvc -match 'UserKey\(id\.Path\)' -and $csvc -match 'public void LoadCorrections\(\)')
Assert-True 'two levels by default, containers keep going, concrete objects stop' `
    ($forg -match 'public const int AutoLevels = 2' -and
     $forg -match 'ShouldAutoMaterializeChildren' -and $forg -match 'CanAutoMaterialize')
Assert-True 'deep system entry points are not cut off by a fixed depth from the drive' `
    ($forg -match 'ShouldAutoFollowEntryPath' -and $forg -match 'MaxEntryDepth')
Assert-True 'recognised platforms keep their inner game entries' `
    ($forg -match 'KeepsObjectEntries' -and $forg -match 'IsObjectContainer')
Assert-True 'exploration, requests and rows all have hard budgets' `
    ($forg -match 'ChildBudget = 24' -and $forg -match 'UnknownProbeBudget' -and
     $forg -match 'MaxRows = 4000' -and $csvc -match 'MaxAiRequests')
Assert-True 'reparse points / links are skipped, not followed' `
    ($forg -match 'c\.IsReparsePoint \|\| c\.IsFilesGroup' -and $forg -match 'skipped\+\+')
Assert-True 'the list is virtualised and has no nested scroller' `
    ($xaml -match '(?s)x:Name="OrganizeGrid"[^>]*VirtualizingPanel\.IsVirtualizing="True"' -and
     $xaml -match '(?s)x:Name="OrganizeGrid"[^>]*EnableRowVirtualization="True"' -and
     $xaml -notmatch '(?s)<ScrollViewer[^>]*>\s*<DataGrid\s+x:Name="OrganizeGrid"')

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
Assert-True 'uninstall grid virtualises rows (flat list, no grouping)' `
    ($xaml -match '(?s)x:Name="UninstallGrid".{0,800}EnableRowVirtualization="True"' -and
     $xaml -match '(?s)x:Name="UninstallGrid".{0,800}VirtualizingPanel\.IsVirtualizing="True"')
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

# --- 11. single-layer navigation: in-place expand (2.11) --------------------
Assert-True 'purpose home exists as the default view' ($xaml -match 'x:Name="PurposePage"')
Assert-True 'the old location page is gone (categories expand in place)' `
    ($xaml -notmatch 'x:Name="LocationPage"')
Assert-True 'detail panel starts hidden' `
    ($xaml -match 'x:Name="DetailPanel" Grid\.Column="1" Visibility="Collapsed"')
# 关键：主内容区里只有一列用于页面，不再各占一行
$staleStack = @('PurposeRow', 'LocationRow', 'DetailRow', 'CleanLayers') |
    Where-Object { $xaml -match ('x:Name="' + $_ + '"') }
Assert-True 'main area has no per-layer rows (old 3-row stack is gone)' ($staleStack.Count -eq 0)
Assert-True 'purpose home and preflight share the same grid column' `
    ($xaml -match '(?s)x:Name="PurposePage" Grid\.Column="0"' -and
     $xaml -match '(?s)x:Name="PreflightPage" Grid\.Column="0"')
Assert-True 'clicking a category expands locations inline (does not hide the home page)' `
    ($cs -match 'private void PurposeExpand_Click' -and
     $cs -match 'private void TogglePurposeInline' -and
     $cs -notmatch 'LocationPage\.Visibility' -and
     $nodes -match 'VisibleLocations')
Assert-True 'in-place expand keeps the same location node instances (selection is not rebuilt)' `
    ($nodes -match 'IsExpanded \? Locations : Array\.Empty<CleanLocationNode>')
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
Assert-True 'no misleading AI wording anywhere' `
    ($cs -notmatch 'AI 自动清理' -and $cs -notmatch 'AI 安全清理' -and
     $cs -notmatch 'AI 决定清理' -and $cs -notmatch 'AI 已替你选择')

# --- 板块：AI 结论界面（本轮核心：一眼看懂该选哪些） -------------------------
$verdict = Get-Content -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AiVerdict.cs') -Raw -Encoding UTF8
Assert-True 'the default view is one headline + next step (note stays in the model, not the card)' `
    ($verdict -match 'AiHeadlineClean' -and $verdict -match 'BuildNote' -and
     $xaml -match 'Ai\.Headline')
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
$aiCard = [regex]::Match($xaml, '(?s)x:Key="ItemAiResultOnly".*?</DataTemplate>').Value
Assert-True 'the select action uses the plain wording' `
    ($loc -match '选择这些文件' -and $aiCard -match '选择这些文件')
Assert-True 'technical wording is gone from the panel' `
    ($xaml -notmatch '本地拆分' -and $xaml -notmatch '符合清理资格' -and
     $xaml -notmatch '按组' -and $xaml -notmatch '本地规则')
Assert-True 'AI result card is headline + select; no view-files / why / retry chrome' `
    ($aiCard.Length -gt 0 -and
     $aiCard -match 'ShowResultPanel' -and
     $aiCard -match 'Ai\.Headline' -and
     $aiCard -notmatch '查看文件' -and
     $aiCard -notmatch '为什么这样建议' -and
     $aiCard -notmatch '重新分析' -and
     $aiCard -notmatch '<Expander' -and
     $aiCard -notmatch 'ScrollViewer')
Assert-True 'result panel only appears when there is something selectable and the row is expanded' `
    ($itv -match 'ShowResultPanel => CanSelect && IsExpanded')
Assert-True 'selecting goes through the existing refresh chain' `
    ($cs -match 'AiSelectBucket_Click' -and $cs -match 'RefreshAfterSelectionChange\(\)')
Assert-True 'viewing files uses exact item filtering' `
    ($cs -match 'AiViewBucket_Click' -and $cs -match 'SetItemFilter' -and
     $pager -match 'public void SetItemFilter')
Assert-True 'local verdict is available even without AI configured' `
    ($cs -match 'AiNeedConfigLocalStillWorks' -and $cs -match 'AiVerdict\.Build\(items')

# --- 2.12 UX contract -------------------------------------------------------
Assert-True 'clean action bar has a single 全选 toggle, not a separate clear or rule-select button' `
    ($xaml -match 'x:Name="CleanSelectAllBtn"' -and
     $xaml -notmatch 'x:Name="ClearSelectionBtn"' -and
     $xaml -notmatch 'x:Name="RuleSelectBtn"' -and
     $cs -match 'private void CleanSelectAll_Click' -and
     $cs -match 'CleanSelectAllBtn\.Content = Loc\.SelectAll')
Assert-True 'clean 全选 never retitles itself to 取消全选' `
    ($cs -notmatch 'CleanSelectAllBtn\.Content = Loc\.ClearSelection' -and
     $cs -match 'allSelected \? Loc\.SelectAllClearTip : Loc\.SelectAllActionTip')
Assert-True 'organize page has a single 全选 toggle on listed rows' `
    ($xaml -match 'x:Name="OrganizeSelectAllBtn"' -and
     $org -match 'OrganizeSelectAllToggle_Click' -and
     $org -match 'OrganizeSelectAllBtn\.Content = Loc\.SelectAll')
Assert-True 'organize pending-filter chip and duplicate title are gone' `
    ($xaml -notmatch 'x:Name="OrganizeFilterPendingBtn"' -and
     $xaml -notmatch 'x:Name="OrganizeTitle"')
Assert-True 'organize grid cannot be resorted from the header (tree order is data-layer size sort)' `
    ($xaml -match '(?s)x:Name="OrganizeGrid"[^>]*CanUserSortColumns="False"')
Assert-True 'organize roots mix entry points with other top-level dirs and sort by size' `
    ($forg -match '入口与普通子目录混在一起按容量降序' -and
     $forg -match 'int bySize = b\.Size\.CompareTo\(a\.Size\)')
Assert-True 'AI chip sits between volume text and settings' `
    ($xaml -match '(?s)x:Name="VolumeText".*?x:Name="AiChip".*?x:Name="SettingsButton"')
Assert-True 'AI chip is collapsed when unconfigured and spins while busy' `
    ($cs -match 'AiChip\.Visibility = configured \? Visibility\.Visible : Visibility\.Collapsed' -and
     $xaml -match 'x:Name="AiChipDot"' -and $xaml -match 'Fill="#3ECF6A"' -and
     $xaml -match 'x:Name="AiChipSpin"')
Assert-True 'uninstall keeps name / install date / purpose / size / actions' `
    ($xaml -match 'x:Name="ColAppName"' -and $xaml -match 'x:Name="ColAppInstallDate"' -and
     $xaml -match 'x:Name="ColAppPurpose"' -and $xaml -match 'x:Name="ColAppSize"' -and
     $xaml -match 'x:Name="ColAppAction"')
Assert-True 'uninstall publisher / version / status columns stay in the tree but are collapsed' `
    ($xaml -match '(?s)x:Name="ColAppPub".{0,220}Visibility="Collapsed"' -and
     $xaml -match '(?s)x:Name="ColAppVersion".{0,220}Visibility="Collapsed"' -and
     $xaml -match '(?s)x:Name="ColAppStatus".{0,220}Visibility="Collapsed"')
$colSize = [regex]::Match($xaml, '(?s)x:Name="ColAppSize".*?</DataGridTextColumn>').Value
Assert-True 'uninstall size cell is number-only; source lives in the tooltip' `
    ($appitem -match 'AppSizeSourceMeasured' -and $appitem -match 'AppSizeSourceRecord' -and
     $colSize -match 'FootprintHint' -and $colSize -notmatch 'MaxWidth')
Assert-True 'uninstall purpose is type + publisher, no version in the cell' `
    ($factual -match 'DuplicatesLabel' -and
     $factual -match 'parts\.Count == 1 && location\.Length > 0' -and
     $factual -notmatch 'parts\.Add\(version\)')
Assert-True 'inline file list has no 完整列表 button; extra files are counted only' `
    ($xaml -notmatch 'FullListButtonText' -and
     $xaml -notmatch '完整列表（可搜索）' -and
     $loc -match '另有')
Assert-True 'clean location subtitle no longer appends 用途待确认' `
    ($nodes -match 'ItemAiPurposeText' -and $nodes -notmatch 'PurposeUnclear')
Assert-True 'organize purpose column replaces 未识别 after item AI' `
    ($orgnode -match 'HasItemAiPurpose' -and
     $orgnode -match 'NeedsConfirm => _needsConfirm && !HasItemAiPurpose' -and
     $cs -match 'RefreshOrganizeAfterItemAi')

# 发布产物：取 dist 下最新的 DashaoHuo-*-win-x64。
# 这是**打包门禁**，不是行为检查：dist 是 gitignore 的生成物，开发机 / CI 没有它很正常。
# 所以「dist 缺失」明确标 SKIP（exit 0），绝不因 Test-Path 对 null 路径抛错而误报成行为失败；
# 只有「dist 存在但缺包/版本不对」才算真失败 —— 那是发布时该抓的。
$distDir = Join-Path $repo 'dist'
if (Test-Path -LiteralPath $distDir) {
    # 注意：必须按**版本号**取最新，不能按名字排序。
    # 字符串比较下 'DashaoHuo-2.9.2' > 'DashaoHuo-2.10.0'（'9' > '1'），
    # 于是 2.10.0 一发布这个门禁反而去检查旧包 —— 之前没暴露只是因为没跨过 2.9 -> 2.10。
    $publish = Get-ChildItem -LiteralPath $distDir -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'DashaoHuo-*-win-x64' } |
        ForEach-Object {
            $m = [regex]::Match($_.Name, '^DashaoHuo-(\d+(?:\.\d+)*)-')
            [pscustomobject]@{
                Dir = $_
                Ver = if ($m.Success) { [version]$m.Groups[1].Value } else { [version]'0.0.0' }
            }
        } |
        Sort-Object Ver, @{ Expression = { $_.Dir.Name } } -Descending |
        Select-Object -First 1 -ExpandProperty Dir |
        Select-Object -ExpandProperty FullName

    if ($publish -and (Test-Path -LiteralPath $publish)) {
        $rc = Get-Content -LiteralPath (Join-Path $publish 'AiDiskCleaner.runtimeconfig.json') -Raw
        Assert-True 'publish is self-contained' ($rc -match 'includedFrameworks')
        $exeVersion = (Get-Item (Join-Path $publish 'AiDiskCleaner.exe')).VersionInfo.FileVersion
        Assert-True "published exe carries the new version (got $exeVersion)" ($exeVersion -like '2.12.1*')
    }
    else {
        Assert-True 'publish package exists (dist/ present but no DashaoHuo-*-win-x64 package)' $false
    }
}
else {
    Write-Output 'SKIP publish package check (dist/ not present — packaging is validated at release time)'
}

Write-Output ''
if ($fail -eq 0) { Write-Output 'PASS: UI/behaviour regression checks all green.'; exit 0 }
Write-Output "FAIL: $fail check(s) failed."
exit 1
