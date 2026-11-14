# UI / behaviour regression check (v3.1).
#
# Guards the interface behaviour so a later refactor cannot quietly roll it back:
#   - the cleanable panel is the default right-hand tab
#   - the file-browser sidebar is gone; the cleanup range stays an in-memory filter
#   - folder tidy-up is local-only: it never reaches the model and never auto-classifies
#   - AI is an "analysis + report" assistant: no Jev decision channel, no auto delete
#   - the AI report lives on the folder page and only LOCATES/highlights a folder;
#     checking a folder is always a user click, deletion still goes through preflight +
#     confirmation + Recycle Bin
#   - 3.0: the uninstall page is FACT-ONLY (name / install date / purpose / footprint / actions)
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/UiRegressionCheck/Run.ps1

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
function Read-Text([string]$rel) { Get-Content -LiteralPath (Join-Path $repo $rel) -Raw -Encoding UTF8 }

$cs = Read-Text 'src\AiDiskCleaner\MainWindow.xaml.cs'
$xaml = Read-Text 'src\AiDiskCleaner\MainWindow.xaml'
$agent = Read-Text 'src\AiDiskCleaner\MainWindow.AgentReport.cs'
$orgsrc = Read-Text 'src\AiDiskCleaner\MainWindow.Organize.cs'
$persist = Read-Text 'src\AiDiskCleaner\MainWindow.RecognitionPersistence.cs'
$nodes = Read-Text 'src\AiDiskCleaner\Models\CleanGroupNodes.cs'
$orgnode = Read-Text 'src\AiDiskCleaner\Models\OrganizeNode.cs'
$item = Read-Text 'src\AiDiskCleaner\Models\CleanItem.cs'
$loc = Read-Text 'src\AiDiskCleaner\Services\Loc.cs'
$rules = Read-Text 'src\AiDiskCleaner\Services\CleanRules\CleanRules.cs'
$dup = Read-Text 'src\AiDiskCleaner\Services\CleanRules\DuplicateScanService.cs'
$sig = Read-Text 'src\AiDiskCleaner\Services\AppSignatures.cs'
$forg = Read-Text 'src\AiDiskCleaner\Services\FolderOrganize.cs'
$csvc = Read-Text 'src\AiDiskCleaner\Services\FolderPurposeService.cs'
$newfp = Read-Text 'src\AiDiskCleaner\Services\FolderPurpose.cs'
$factual = Read-Text 'src\AiDiskCleaner\Services\AppFactualInfoService.cs'
$appitem = Read-Text 'src\AiDiskCleaner\Models\AppUninstallItem.cs'
$recsvc = Read-Text 'src\AiDiskCleaner\Services\AppRecommendationService.cs'
$app = Read-Text 'src\AiDiskCleaner\App.xaml.cs'
$redactor = Read-Text 'src\AiDiskCleaner\Services\PathRedactor.cs'
$workflow = Read-Text 'src\AiDiskCleaner\Services\CleanupWorkflowService.cs'
$card = Read-Text 'src\AiDiskCleaner\Services\Agent\EvidenceCard.cs'
$agentsvc = Read-Text 'src\AiDiskCleaner\Services\Agent\AgentReportService.cs'
$prompt = Read-Text 'src\AiDiskCleaner\Services\Agent\PromptBuilder.cs'
$parser = Read-Text 'src\AiDiskCleaner\Services\Agent\ReportParser.cs'
$assembler = Read-Text 'src\AiDiskCleaner\Services\Agent\ReportAssembler.cs'
$toolhost = Read-Text 'src\AiDiskCleaner\Services\Agent\ToolHost.cs'
$chat = Read-Text 'src\AiDiskCleaner\Services\Agent\Chat\ChatModelFactory.cs'
$aiconfig = Read-Text 'src\AiDiskCleaner\Services\AiConfigurationService.cs'
$settings = Read-Text 'src\AiDiskCleaner\Services\AppSettings.cs'
$catalog = Read-Text 'src\AiDiskCleaner\Services\Agent\Chat\ChatModelCatalog.cs'

$fail = 0
function Assert-True([string]$name, [bool]$ok) {
    if ($ok) { Write-Output "PASS $name" }
    else { Write-Output "FAIL $name"; $script:fail++ }
}

# --- 1. directory sidebar is gone -------------------------------------------
Assert-True 'no directory-tree visibility flag remains' ($cs -notmatch '_treeVisible')
Assert-True 'ApplySidebarLayout is gone' ($cs -notmatch 'ApplySidebarLayout')
Assert-True 'left panel / splitter / tree toggle / overlay scrim are gone from XAML' `
    ($xaml -notmatch 'x:Name="LeftPanel"' -and
     $xaml -notmatch 'x:Name="TreeToggleBtn"' -and
     $xaml -notmatch 'x:Name="SidebarScrim"' -and
     $xaml -notmatch 'x:Name="SplitterCol"')
Assert-True 'file search box is gone (no browse tree to search)' `
    ($xaml -notmatch 'x:Name="SearchBox"' -and $xaml -notmatch 'x:Name="DirGrid"')
Assert-True 'main content column has NO MaxWidth (that caused the black block)' `
    ($xaml -notmatch '(?s)x:Name="RightCol"[^>]*MaxWidth')
Assert-True 'there is no tree action that sets the cleanup range' `
    ($cs -notmatch 'private void ScopeToFolder_Click' -and $cs -notmatch 'private void ShowDirectory\(')
Assert-True 'cleanup range is a leftover in-memory filter, not a browse directory' `
    ($cs -match 'private FileEntry\? _cleanScopeRoot;')
Assert-True 'applied range still has a visible chip with a clear action' `
    ($xaml -match 'x:Name="CleanScopeBar"' -and $xaml -match 'x:Name="ClearScopeBtn"' -and
     $cs -match 'private void ClearScope_Click')
Assert-True 'range chip warns about selected items outside the range' `
    ($cs -match 'Loc\.ScopeChip\(path, outside\)')
Assert-True 'clearing the range never clears or widens the selection' `
    ($cs -match '(?s)private void ApplyCleanScope\(\)[\s\S]{0,700}RebuildLayersAsync')

# --- 2. default tab and navigation ------------------------------------------
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
    ($xaml -match '(?s)x:Name="OrganizePane"[^>]*Visibility="Collapsed"' -and
     $xaml -match 'x:Name="OrganizeGrid"' -and
     $cs -match 'OrganizePane\.Visibility = tab == RightTab\.Organize')
Assert-True 'clean pane is visible by default (no Collapsed on CleanPane itself)' `
    ($xaml -notmatch '(?s)x:Name="CleanPane"[^>]*Visibility="Collapsed"')

# --- 3. tidy-up workspace structure -----------------------------------------
Assert-True 'tidy-up columns are name / what-it-is / size (the actions column is gone)' `
    ($xaml -match '(?s)x:Name="ColOrgName".*?x:Name="ColOrgPurpose".*?x:Name="ColOrgSize"' -and
     $xaml -notmatch 'ColOrgAction')
Assert-True 'real path sits on a second line in the row' `
    ($xaml -match '(?s)x:Name="ColOrgName".*?Binding RelativePath')
Assert-True 'tidy-up does not copy the clean candidate space or clean buttons' `
    ($orgPane.Length -gt 0 -and $orgPane -notmatch 'CheckAndCleanBtn' -and
     $orgPane -notmatch 'CleanSelectionSummary' -and $orgPane -notmatch 'CleanActionBar')
Assert-True 'rows can grow when a detail block is open' `
    ($xaml -match 'RowHeight="NaN"' -and $xaml -match 'MinRowHeight="46"')
Assert-True 'the purpose cell opens an inline detail block (collapsed by default)' `
    ($xaml -match 'MouseLeftButtonUp="OrganizePurpose_Click"' -and
     $xaml -match 'Binding IsDetailOpen' -and
     $orgsrc -match 'private void OrganizePurpose_Click' -and
     $orgnode -match 'public void ToggleDetail' -and
     $orgnode -match 'private bool _detailOpen')
Assert-True 'the left arrow is the single expand/collapse entry' `
    ($xaml -match 'x:Name="ColOrgName"' -and
     [regex]::Match($xaml, '(?s)x:Name="ColOrgName".*?OrganizeExpand_Click').Success -and
     [regex]::Match($xaml, '(?s)x:Name="ColOrgName".*?Binding ExpandGlyphKey').Success)
Assert-True 'the actions column is gone; opening a folder lives in the context menu' `
    ($xaml -notmatch 'ColOrgAction' -and $xaml -match 'OrganizeOpen_Click' -and
     $xaml -match 'Header="在资源管理器中打开"')
Assert-True 'opening a folder goes through ShellReveal (never executes a program)' `
    ($orgsrc -match 'ShellReveal\.Reveal\(' -and $orgsrc -notmatch 'Process\.Start')
Assert-True 'the list is virtualised and has no nested scroller' `
    ($xaml -match '(?s)x:Name="OrganizeGrid"[^>]*VirtualizingPanel\.IsVirtualizing="True"' -and
     $xaml -match '(?s)x:Name="OrganizeGrid"[^>]*EnableRowVirtualization="True"' -and
     $xaml -notmatch '(?s)<ScrollViewer[^>]*>\s*<DataGrid\s+x:Name="OrganizeGrid"')

# --- 4. tidy-up is local-only: no model, no auto-classify -------------------
Assert-True 'local recognition is the only identification path' `
    ($orgsrc -match 'private void LocalRecognize\(OrganizeNode node\)' -and
     $orgsrc -match 'FolderPurposeRules\.RecognizeLocally' -and
     $orgsrc -notmatch 'RecognizeWithAsync')
Assert-True 'the classify button / forget button / result panel are gone' `
    ($xaml -notmatch 'OrganizeClassifyBtn' -and $xaml -notmatch 'OrganizeForgetAiBtn' -and
     $xaml -notmatch 'OrganizeResultPanel' -and $xaml -notmatch 'OrganizeBucket_Click' -and
     $orgsrc -notmatch 'OrganizeClassify' -and $orgsrc -notmatch 'AiPurposeBatchService')
Assert-True 'the organize page never sends a request by itself' `
    ($orgsrc -notmatch 'HttpClient' -and $orgsrc -notmatch 'PostAsync' -and
     $orgsrc -notmatch 'AiClient' -and $orgsrc -notmatch 'ChatModelFactory')
Assert-True 'batch purpose slots are gone from the row model' `
    ($orgnode -notmatch 'BatchPurpose' -and $orgnode -notmatch 'MarkBatchAsked' -and
     $orgnode -notmatch 'NeedsPurposeClassification' -and $orgnode -notmatch 'SetBatchPurpose')
Assert-True 'persistence no longer restores AI purposes' `
    ($persist -notmatch 'AiPurpose' -and $persist -notmatch 'RestoreAiPurposes')
Assert-True 'the duplicate keep-advice chip is gone' `
    ($nodes -notmatch 'DupAsk' -and $xaml -notmatch 'DupAsk' -and $cs -notmatch 'DupAskKeep')
Assert-True 'tidy-up never touches risk / deletability / selection' `
    ($orgsrc -notmatch '\.(Risk|CanDelete|Selected)\s*=' -and
     $orgsrc -notmatch 'SendToRecycle|DeletionExecutor|RecycleService')
Assert-True 'tidy-up never moves or renames real files' `
    ($orgsrc -notmatch 'File\.Move|Directory\.Move|\.MoveTo\(|File\.Copy|Directory\.CreateDirectory')
Assert-True 'tidy-up never asks for elevation on its own' `
    ($orgsrc -notmatch '\brunas\b|Verb\s*=|ProcessStartInfo.*Verb' -and
     $orgsrc -notmatch 'WindowsPrincipal|IsInRole')

# --- 5. AI config is an OpenAI-compatible chat channel ----------------------
Assert-True 'the settings model is a chat protocol, not a decision protocol' `
    ($aiconfig -match 'ChatProtocol = "chat"' -and $settings -match 'ChatProtocol')
Assert-True 'presets cover DeepSeek / OpenAI-compatible / Ollama' `
    ($aiconfig -match 'DeepSeek' -and $aiconfig -match 'Ollama' -and
     $aiconfig -match 'https://api\.deepseek\.com/v1')
Assert-True 'the test button validates a real chat round-trip' `
    ($aiconfig -match 'BuildProbe' -and $aiconfig -match 'ValidateProbe' -and
     $cs -match 'ChatModelFactory\.Create')
Assert-True 'chat completions endpoint is derived from the base url' `
    ($chat -match '/chat/completions')
Assert-True 'settings expose preset / url / model / key fields' `
    ($xaml -match 'x:Name="AiPresetBox"' -and $xaml -match 'x:Name="AiUrlBox"' -and
     $xaml -match 'x:Name="AiDecisionModelBox"' -and $xaml -match 'x:Name="AiTestBtn"')
Assert-True 'the Jev decision channel is gone from source and composition root' `
    (-not (Test-Path -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AiGateway.cs')) -and
     -not (Test-Path -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AiClient.cs')) -and
     -not (Test-Path -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\AiPurposeBatchService.cs')) -and
     -not (Test-Path -LiteralPath (Join-Path $repo 'src\AiDiskCleaner\Services\Decisions')) -and
     $cs -notmatch 'AiGateway|DecisionRunner' -and $app -notmatch 'AiGateways')
Assert-True 'historical protocol values normalize to chat' `
    ($settings -match 'NormalizeProtocols' -and $settings -match 'ChatProtocol')
Assert-True 'the settings page shows which provider AI analysis uses, with a one-click switch' `
    ($xaml -match 'x:Name="AiActiveTitle"' -and $xaml -match 'x:Name="AiActiveName"' -and
     $xaml -match 'x:Name="AiActiveModel"' -and $xaml -match 'x:Name="AiActiveStatus"' -and
     $xaml -match 'x:Name="AiActiveDot"' -and
     $xaml -match 'GroupName="AiActiveProv"' -and $xaml -match 'Click="AiUseProv_Click"' -and
     $cs -match 'AiUseProv_Click' -and $cs -match 'SetActiveProvider' -and
     $cs -match 'UpdateAiActiveStrip')
Assert-True 'the provider dot reflects health instead of a hard-coded green' `
    ($xaml -match 'Binding="\{Binding StatusKind\}"' -and
     $xaml -match 'ToolTip="\{Binding StatusTip\}"' -and
     $settings -match 'StatusKind' -and $settings -match 'StatusTip')
Assert-True 'the model travels with each provider (no shared global model write)' `
    ($cs -match 'p\.Models\.RemoveAll' -and $cs -match 'p\.Models\.Insert' -and
     $cs -notmatch 'AiDecisionModel\s*=')
Assert-True 'switching provider cancels an in-flight report from the old provider' `
    ($cs -match 'private void AiUseProv_Click' -and $cs -match 'CancelAgentReport\(\)')
Assert-True 'the model field can fetch the upstream model list' `
    ($xaml -match 'x:Name="AiFetchModelsBtn"' -and $xaml -match 'x:Name="AiModelsPopup"' -and
     $xaml -match 'x:Name="AiModelsList"' -and $xaml -match 'x:Name="AiModelsHint"' -and
     $cs -match 'AiFetchModels_Click' -and $cs -match 'ChatModelCatalog\.FetchAsync' -and
     $catalog -match 'ModelsEndpoint')
Assert-True 'a failed model fetch keeps manual entry and only shows a hint' `
    ($cs -match 'SetModelsHint\(Loc\.AiModelsFailed' -and
     $loc -match 'AiModelsFailed' -and
     $cs -notmatch 'throw new InvalidOperationException\(Loc\.AiModelsFailed')
Assert-True 'long test results wrap inside the dialog' `
    ($xaml -notmatch '(?s)<StackPanel Orientation="Horizontal">\s*<Button x:Name="AiTestBtn"' -and
     $xaml -match 'x:Name="AiTestHint" Grid\.Column="1"')
Assert-True 'the address placeholder is a chat endpoint, not the old router path' `
    ($xaml -match 'Tag="https://api\.deepseek\.com/v1"')
Assert-True 'reasoning-only replies count as a working connection' `
    ($aiconfig -match 'reply\.Reasoning')
Assert-True 'an empty reply is diagnosed as a model issue, not a wrong endpoint' `
    ($aiconfig -match 'AiDiagnosticKind\.Model' -and
     $aiconfig -match 'LooksLikeEmptyReply')
Assert-True 'a failed connection is named as failed, not merely untested' `
    ($cs -match 'AiLampFailed' -and $loc -match 'AiLampFailed')
$iLamp = $xaml.IndexOf('x:Name="AiLampButton"', [StringComparison]::Ordinal)
$iGear = $xaml.IndexOf('x:Name="SettingsButton"', [StringComparison]::Ordinal)
Assert-True 'the AI lamp lives in the top bar, before the settings gear' `
    ($iLamp -gt 0 -and $iGear -gt 0 -and $iLamp -lt $iGear)
Assert-True 'the lamp is red when unconfigured, yellow when untested, green with the model name' `
    ($cs -match 'void UpdateAiLamp' -and
     $cs -match '#E07070' -and $cs -match '#E0B050' -and $cs -match '#3ECF6A' -and
     $cs -match 'AiLampText\.Text = p\.ModelLabel')
Assert-True 'clicking the lamp opens settings' `
    ($cs -match 'AiLamp_Click' -and $cs -match 'SettingsButton_Click\(sender, e\)')
Assert-True 'the lamp result survives a restart (health is persisted, key is not)' `
    ($settings -match 'public AiProviderHealth Health' -and
     $settings -notmatch '\[JsonIgnore\]\s*\r?\n\s*public AiProviderHealth Health' -and
     $settings -match 'JsonStringEnumConverter')

# --- 6. AI report is an interactive suggestion sheet (folder page only) ------
$iOrgPane = $xaml.IndexOf('x:Name="OrganizePane"', [StringComparison]::Ordinal)
$iReportBtn = $xaml.IndexOf('x:Name="AiReportBtn"', [StringComparison]::Ordinal)
$iCleanPane = $xaml.IndexOf('x:Name="CleanPane"', [StringComparison]::Ordinal)
Assert-True 'the folder-delete page (not the clean page) has the AI analysis button' `
    ($xaml -match 'x:Name="AiReportBtn"' -and $xaml -match 'Click="AiReport_Click"' -and
     $loc -match 'AiReportBtn' -and
     $iOrgPane -gt 0 -and $iReportBtn -gt $iOrgPane -and $iCleanPane -gt $iReportBtn)
Assert-True 'the report renders inside the dialog card' `
    ($xaml -match 'x:Name="ReportBody"' -and $xaml -match 'x:Name="ReportContent"' -and
     $agent -match 'ReportBody\.Visibility = Visibility\.Visible' -and
     $agent -match 'DialogCard\.Width = 940')
Assert-True 'the folder page also provides a right-side docked report split panel' `
    ($xaml -match 'x:Name="OrganizeReportPanel"' -and
     $xaml -match 'x:Name="OrganizeReportContent"' -and
     $xaml -match 'x:Name="AiReportToggleBtn"' -and
     $agent -match 'ShowDockedAgentReport' -and
     $agent -match 'SetDockedReportVisible')
Assert-True 'the report only locates the folder (never checks it, never deletes)' `
    ($agent -match 'RevealReportFolder' -and $agent -match 'ShowRightTab\(RightTab\.Organize\)' -and
     $agent -match 'RevealOrganizeFolder' -and
     $agent -notmatch 'AddReportItemToCleanup' -and $agent -notmatch 'JumpToReportFolder' -and
     $agent -notmatch '\.Selected = true' -and $agent -notmatch 'IsChecked' -and
     $orgsrc -match 'RevealOrganizeFolder' -and $orgsrc -match 'ScrollIntoView' -and
     $loc -match 'AiReportJump')
Assert-True 'the report is built from groups and evidence, not free text' `
    ($agent -match 'BuildReportItem' -and $agent -match 'MakeBadge' -and
     $loc -match 'AiReportEvidence' -and $loc -match 'AiReportRisk')
Assert-True 'the report never deletes or moves anything itself' `
    ($agent -notmatch 'SendToRecycle|File\.Delete|Directory\.Delete|DeletionExecutor|RecycleService')
Assert-True 'closing the report restores the normal dialog width' `
    ($cs -match 'DialogCard\.Width = 520' -and
     $agent -match 'UpdateAiReportButton' -and $cs -match 'UpdateAiReportButton')
Assert-True 'the AI button only enables after a scan, and not while busy' `
    ($agent -match 'AiReportBtn\.IsEnabled = _root != null' -and $agent -match '_agentBusy')
Assert-True 'starting a scan cancels and clears any running report' `
    ($cs -match '_report = null;' -and $cs -match 'CancelAgentReport\(\)')
Assert-True 'the report pipeline is budgeted (candidates / batches / tokens)' `
    ($agentsvc -match 'CardsPerBatch \{ get; init; \} = 25' -and
     $agentsvc -match 'MaxBatches \{ get; init; \} = 4' -and
     $agentsvc -match 'MaxCandidates \{ get; init; \} = 100' -and
     $agentsvc -match 'MaxInputTokens \{ get; init; \} = 50_000')
Assert-True 'the prompt is versioned and asks for JSON only' `
    ($prompt -match 'cleanup-report-v1' -and $prompt -match 'JSON')
Assert-True 'the parser caps items and evidence refs' `
    ($parser -match 'MaxItems = 40' -and $parser -match 'MaxEvidenceRefs = 6')
Assert-True 'the assembler floats recommendations and clamps the total' `
    ($assembler -match 'RecommendConfidenceFloor = 0\.55' -and $assembler -match 'total_clamped')
Assert-True 'the tool host exposes read-only tools only' `
    ($toolhost -match 'GetOverviewTool' -and $toolhost -match 'ListFoldersTool' -and
     $toolhost -match 'GetFolderCardTool' -and $toolhost -match 'QueryEntriesTool' -and
     $toolhost -notmatch 'PlanDeleteTool' -and $toolhost -notmatch 'DeleteTool' -and
     $toolhost -notmatch 'ShellTool' -and $toolhost -notmatch 'UninstallTool')
Assert-True 'evidence cards redact the user name by default' `
    ($card -match 'Redact\(' -and $card -match '%USER%' -and $card -match 'CardPrivacyMode')

# --- 7. clean page behaviour -------------------------------------------------
Assert-True 'clean action bar has a single 全选 toggle' `
    ($xaml -match 'x:Name="CleanSelectAllBtn"' -and
     $cs -match 'private void CleanSelectAll_Click' -and
     $cs -match 'allSelected \? Loc\.SelectAllClearTip : Loc\.SelectAllActionTip')
Assert-True 'selection summary still counts selected and total' `
    ($xaml -match 'x:Name="CleanSelectionSummary"' -and
     $cs -match 'Loc\.SelectionSummary\(' -and $cs -match 'Loc\.ViewSelectedSub')
Assert-True 'the AI report never changes the selection (locate only)' `
    ($agent -notmatch 'UnderPrefix' -and $agent -notmatch 'RefreshAfterSelectionChange' -and
     $agent -notmatch 'Selected = true')

# --- 8. no long-running operation left without a token ----------------------
Assert-True 'no CancellationToken.None left in the window' ($cs -notmatch 'CancellationToken\.None')
Assert-True 'stop button cancels everything' ($cs -match 'StopEverything\(\)')
Assert-True 'stale results are guarded by generation counters' `
    ($cs -match '_scanGeneration' -and $cs -match '_analyzeGeneration' -and
     $cs -match '_uninstallGeneration')

# --- 9. deletion always goes through preflight ------------------------------
$deletionBoundaryOk = ($cs -match '_cleanupWorkflow\.PlanDeletion\(' -and
    $workflow -match 'coordinator\.Plan\(' -and $workflow -match 'DeletionTarget')
Assert-True 'deletion goes through DeletionCoordinator' $deletionBoundaryOk
Assert-True 'no direct RecycleService call in the delete paths' `
    ($cs -notmatch 'RecycleService\.SendToRecycle')

# --- 10. privacy ------------------------------------------------------------
Assert-True 'outbound paths are redacted unless full paths are enabled' `
    ($redactor -match 'AiSendFullPaths \? \(path \?\? ""\) : Redact\(path\)')
Assert-True 'language never says "recently used"' ($loc -notmatch '最近使用')
Assert-True 'no misleading AI wording anywhere' `
    ($cs -notmatch 'AI 自动清理' -and $cs -notmatch 'AI 安全清理' -and
     $cs -notmatch 'AI 决定清理' -and $cs -notmatch 'AI 已替你选择')

# --- 11. uninstall page stays fact-only -------------------------------------
Assert-True 'uninstall keeps name / install date / purpose / size / actions' `
    ($xaml -match 'x:Name="ColAppName"' -and $xaml -match 'x:Name="ColAppInstallDate"' -and
     $xaml -match 'x:Name="ColAppPurpose"' -and $xaml -match 'x:Name="ColAppSize"' -and
     $xaml -match 'x:Name="ColAppAction"')
Assert-True 'uninstall page shows no advice (no advice column / field / entry / tier)' `
    ($xaml -notmatch 'AiSuggested' -and $xaml -notmatch 'Binding Recommendation' -and
     $appitem -notmatch 'Recommendation' -and
     $cs -notmatch 'ReviewSuggestions' -and
     $recsvc -notmatch 'ApplyAiResults' -and
     $loc -notmatch 'AppRecommendationLabel')
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

# 发布产物：取 dist 下最新的 DashaoHuo-*-win-x64。
# 这是**打包门禁**，不是行为检查：dist 是 gitignore 的生成物，开发机 / CI 没有它很正常。
# 所以「dist 缺失」明确标 SKIP（exit 0），绝不因 Test-Path 对 null 路径抛错而误报成行为失败；
# 只有「dist 存在但缺包/版本不对」才算真失败 —— 那是发布时该抓的。
$distDir = Join-Path $repo 'dist'
if (Test-Path -LiteralPath $distDir) {
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
        $csproj = Read-Text 'src\AiDiskCleaner\AiDiskCleaner.csproj'
        $wantVersion = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value.Trim()
        $exeVersion = (Get-Item (Join-Path $publish 'AiDiskCleaner.exe')).VersionInfo.FileVersion
        Assert-True "published exe carries the new version (want $wantVersion, got $exeVersion)" `
            ($wantVersion.Length -gt 0 -and $exeVersion -like "$wantVersion*")
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
