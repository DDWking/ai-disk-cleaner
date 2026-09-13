using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace System.Windows.Media
{
    public class ImageSource
    {
    }
}

namespace AiDiskCleaner
{
    /// <summary>WPF 里的 App 在离线检查里只用来放设置。</summary>
    public static class App
    {
        public static AppSettings Settings { get; } = new();
    }
}

namespace AiDiskCleaner.Services
{
    /// <summary>
    /// 界面文案桩。真实的 AppSignatures 会被链进本检查工程（不走桩），
    /// 所以这里也补上它用到的分类名。
    /// </summary>
    public static class Loc
    {
        // AppSignatures 的分类名
        public static string CatSystem => "System";
        public static string CatBrowser => "Browser";
        public static string CatDev => "Dev";
        public static string CatChat => "Chat";
        public static string CatGame => "Game";
        public static string CatMedia => "Media";
        public static string CatCloud => "Cloud";
        public static string CatVm => "VM";
        public static string CatIde => "IDE";
        public static string CatAiTool => "AI";
        public static string CatOffice => "Office";
        public static string CatSecurity => "Security";
        public static string CatBloat => "Bloat";
        public static string CatIme => "IME";
        public static string NoteCache => "cache";
        // 规则文案
        public static string ReasonLarge => "Large file · purpose unclear";
        public static string GroupLarge => "Large";
        public static string GroupTemp => "Temp / cache";
        public static string ReasonOld(string age) => "Untouched for " + age;
        public static string GroupOld => "Old";
        public static string ReasonLong(int n) => $"Path is {n} chars";
        public static string GroupLong => "Long path";
        public static string ReasonEmpty => "empty folder";
        public static string GroupEmpty => "Empty";
        public static string ReasonDump => "crash dump";
        public static string GroupDump => "Crash dumps";
        public static string ReasonTempDir => "temp dir";
        public static string ReasonTempExt => "temp file";
        public static string ReasonWinUpdate => "windows update cache";
        public static string ReasonInstaller => "installer";
        public static string ReasonRecycle => "recycle bin";
        public static string ReasonRecycleNamed(string name) => "recycle: " + name;
        public static string GroupRecycle => "Recycle Bin";
        public static string ReasonBroken(string target) => "broken shortcut -> " + target;
        public static string GroupShortcut => "Shortcut";
        public static string ReasonGrew(string size) => "grew " + size;
        public static string ReasonShrunk(string size) => "shrank " + size;
        public static string ReasonGone => "gone";
        public static string GroupCompare => "Delta";
        public static string CompareFirst => "first scan";
        public static string CompareSince(DateTime t, string size) => $"{size} since {t:d}";
        public static string CleanWalk => "walk";
        public static string CleanRules => "rules";
        public static string CleanShortcuts => "shortcuts";
        public static string CleanDups => "dups";
        public static string CleanCompare => "compare";
        public static string CleanScan => "scan";
        public static string Analyzing => "analyzing";
        public static string AgeText(int days) => days + "d";

        public static string AiEdit => "edit";
        public static string AiDelProvider => "delete";
        public static string AiCustomTag => "custom";
        // 逐项 AI 文案（离线测试只断言状态语义）
        public static string PurposeUnclear => "purpose unclear";
        // 本地候选项分组文案
        public static string GroupSummaryLine(int i, int c, string s, string src) => $"{i} items, {c} eligible, {s}, {src}";
        public static string GroupAiTag => "[AI] ";
        public static string GroupProtectedOnly => "protected";
        public static string GroupFilterHint(string w) => "filter to " + w;
        public static string GroupViewNeedsLocation => "no location";
        public static string GroupFilterApplied(string w, int n) => $"filtered {w} ({n})";
        public static string AiNeedConfigLocalStillWorks => "no AI, local groups still work";

        public static string SelectAdded(int c, string s) => $"added {c} {s}";
        public static string SelectNothingNew => "nothing new";
        // AI 结论界面文案
        public static string AiAnalyzing => "analysing";
        public static string AiBucketCleanable => "Could be considered";
        public static string AiBucketKeep => "Better kept";
        public static string AiBucketReview => "Needs confirmation";
        public static string AiReasonCleanable => "the app rebuilds these";
        public static string AiReasonReview => "open the files first";
        public static string AiReasonKeep => "protected";
        public static string AiHeadlineClean(int c, string s) => $"Suggest cleaning {c} item(s) - about {s} can be freed";
        public static string AiHeadlineReview(int c, string s) => $"{c} item(s) need your confirmation ({s})";
        public static string AiHeadlineIdentified(string p) => $"Identified as {p}";
        public static string AiHeadlineIdentifiedReview(string p, int c, string s)
            => $"Identified as {p} — {c} item(s) ({s}) still need your confirmation";
        public static string AiHeadlineNothing => "Nothing here is worth cleaning right now";
        public static string AiBucketStat(int c, string s) => $"{c} item(s) · {s}";
        public static string AiSelectTheseFiles => "Select these files";
        public static string AiViewFiles => "View files";
        public static string AiWhyToggle => "Why this suggestion?";
        public static string AiEverythingSelected => "all already selected";
        public static string AiNoteBelongs(string w) => $"These are {w}";
        public static string PurposeFromLocal => "local rules";
        public static string PurposeFromAi => "AI guess";
        public static string PurposeFromUser => "you confirmed";
        public static string PurposeBasisUser => "you set this";
        public static string PurposeUnknownCategory => "uncategorised";
        public static string PurposeNoExt => "(no ext)";
        public static string PurposeGameLibrary => "game library";
        public static string PurposeCatGame => "games";
        public static string PurposeCatDev => "dev";
        public static string PurposeCatSystem => "system";
        public static string PurposeDevProject => "dev project";
        public static string PurposeSystemArea => "system location";
        public static string PurposeBasisSignature(string w) => $"signature: {w}";
        public static string PurposeBasisPath(string w) => $"name matches {w}";
        public static string PurposeBasisPlatform => "platform game folder";
        public static string PurposeBasisDevMark(string m) => $"project marker {m}";
        public static string PurposeBasisSystemEntry => "system entry point";
        public static string PurposeNeedsConfirm => "needs confirmation";
        public static string PurposeUnrecognized => "not recognised";
        public static string PurposeKnownInternal => "inside an app";
        public static string PurposeBasisKnownLeaf(string n) => $"known leaf: {n}";
        public static string PurposeQueued => "queued";
        public static string PurposeRunning => "identifying";
        public static string PurposeFailed => "identification failed";
        public static string PurposeCancelled => "stopped";
        public static string PurposeCorrected(string w) => $"purpose set to {w}";
        public static readonly string[] PurposeCorrections = { "game", "dev", "system" };
        public static string PurposeIdentify => "Identify";
        public static string PurposeDeepen => "Look deeper";
        public static string PurposeCorrect => "Correct";
        public static string PurposeAiHeader => "identify one folder";
        public static string PurposeAiSnippetHeader => "filtered snippets";
        public static string PurposeAiSystem => "system";
        public static string OrganizeWaitNoResult => "waiting, no result yet";
        public static string OrganizeRetryHint => "failed, you can retry";
        public static string OrganizeExpand => "Show sub-folders";
        public static string OrganizeCollapse => "Collapse";
        public static string OrganizeDeepen => "Identify inside";
        public static string OrganizeIdentifyOne => "Identify";
        public static string OrganizePlatformTag => "games live inside";
        public static string OrganizeEntryPointTag => "system entry";
        public static string OrganizeDetailWhat => "What it is: ";
        public static string OrganizeDetailWhy => "Why: ";
        public static string OrganizeDetailKind(int folders, int files, string types)
            => $"{folders} sub-folders, {files} files";
        public static string OrganizeDetailSamples(string names) => $"largest files: {names}";
        public static string OrganizeDetailSource(string source) => $"Judged by: {source}";
        public static string OrganizeDetailNoEvidence => "not enough evidence";
        public static string OrganizeDetailTip => "click for detail";
        public static string OrganizeDetailHide => "click to hide";
        public static string OrganizeRunRunning => "identifying";
        public static string OrganizeRunDone => "identification finished";
        public static string OrganizeRunIncomplete => "identification not finished";
        public static string OrganizeRunCanceled => "identification stopped";
        public static string OrganizeRunCounts(int named, int ai, int unknown, int failed)
            => $"named {named} (AI {ai}) unknown {unknown} failed {failed}";
        public static string OrganizeRunCovered(int covered, int planned) => $"{covered}/{planned} have a result";
        public static string OrganizeRunRequests(int used, int budget) => $"AI requests {used}/{budget}";
        public static string OrganizeRunNotCovered(int n) => $"{n} not reached";
        public static string OrganizeIdentifyThisLevel => "Identify folders on this level";
        public static string OrganizeThisLevelOnly => "only direct sub-folders of this folder";
        public static string OrganizeWorkBarScope(int children, int budget) => $"direct sub-folders {children}, budget {budget}";
        public static string PurposeUserFiles => "user files";
        public static string SysWindows => "Windows operating system files";
        public static string SysProgramFiles => "program installation folder";
        public static string SysProgramFilesX86 => "32-bit program installation folder";
        public static string SysProgramData => "shared program data";
        public static string SysUserProfile => "user files and app data";
        public static string SysLocalAppData => "local app data";
        public static string SysUsersContainer => "user folders";
        public static string SysAppData => "user app data";
        public static string SysBasisUsersContainer => "folder where Windows keeps every user's home";
        public static string SysBasisAppData => "per-user app data folder";
        public static string SysRoamingAppData => "roaming app data";
        public static string SysDocuments => "documents";
        public static string SysDownloads => "downloaded files";
        public static string SysBasisWindows => "Windows folder resolved by the system";
        public static string SysBasisProgramFiles => "program folder resolved by the system";
        public static string SysBasisProgramData => "shared data folder resolved by the system";
        public static string SysBasisUserProfile => "user folder resolved by the system";
        public static string SysBasisLocalAppData => "local app data folder resolved by the system";
        public static string SysBasisRoamingAppData => "roaming app data folder resolved by the system";
        public static string SysBasisDocuments => "documents folder resolved by the system";
        public static string SysBasisDownloads => "downloads folder under the user folder";
        public static string OrganizeLevelAuto(int level) => $"level {level} auto";
        public static string OrganizeLevelDeep(int level) => $"level {level} on demand";
        public static string OrganizeWorkNone => "pick a folder first";
        public static string OrganizeWorkScope(int children, int budget) => $"this level only: {children} children, {budget} requests";
        public static string OrganizeWorkPathHidden => "(path on hover)";
        public static string OrganizeDeepOnly => "level 3 and deeper are never automatic";
        public static string OrganizeSendNote => "name, structure, type mix, a few samples, filtered snippets";
        public static string OrganizeAutoStart(int count, int budget) => $"auto {count} / {budget}";
        public static string OrganizeAutoDone(int done, int pending, int failed, int used, int budget)
            => $"done {done} pending {pending} failed {failed} ai {used}/{budget}";
        public static string OrganizeCounts(int resolved, int pending, int failed)
            => $"named {resolved} to confirm {pending} failed {failed}";
        public static string OrganizeRetryPending => "Retry the rest";
        public static string OrganizeRetryPendingTip => "retry pending or failed";
        public static string OrganizeWorkTitleFixed(string name, int level) => $"{name} level {level}";
        public static string OrganizeUnlistedNote(int shown, int total, int unlisted)
            => $"listed {shown}/{total}, {unlisted} not listed (not identified)";
        public static string OrganizeUnlistedTotal(int unlisted) => $"{unlisted} not listed";
        public static string OrganizePartFailed(int failed) => $"{failed} failed";
        public static string OrganizeBudgetLeft(int pending) => $"{pending} over budget";
        public static string OrganizeNoModelHonest(int pending) => $"{pending} need a model";
        public static string OrganizeIdentifyCurrent => "Identify this folder";
        public static string OrganizeIdentifyCurrentTip => "only this level";
        public static string OrganizeChildBudgetNote(int shown, int total) => $"{shown}/{total} shown";
        public static string OrganizeBudgetUsed(int used) => $"budget {used} used";
        public static string OtherFilesTitle => "Other files";
        public static string GroupStatLine(int c, string s) => $"{c} items · {s}";
        public static string GroupSelectedLine(int c, string s) => $"selected {c} · {s}";
        public static string AiResultExpired => "scan changed, analyse again";         public static string AiNoResultRetry => "no usable result, try again";         public static string AiNoteByLocalRule => "matched the local cleanup rules";         public static string AiWhyNoLockCheck => "File locks were not checked";
        public static string AiNoteUnknown => "we could not confirm what they are for";
        public static string AiWhyLocation(string w) => $"Located in {w}";
        public static string AiWhyModified(string a) => $"Last changed {a}";
        public static string AiWhyMatched(string t) => $"Matched rule: {t}";
        public static string AiWhyNoContentRead => "File contents were not read or uploaded";
        public static string AiWhySomeUncertain(int n) => $"{n} item(s) still cannot be confirmed";
        public static string AiWhyProtected(int n) => $"{n} item(s) are protected";
        public static string AiWhyNothingSafe => "No item here passed the local safety rules";
        public static string AiWhyNoModel => "This is the local summary - no model advice was used";
        public static string AiAgeToday => "today";
        public static string AiAgeDays(int d) => $"{d} day(s) ago";
        public static string AiAgeMonths(int m) => $"{m} month(s) ago";
        public static string AiAgeYears(int y) => $"{y} year(s) ago";
        public static string AiFilteredToCount(int n) => $"only these {n}";
        public static string DetailShowingAll => "all";
        public static string SelectBlockedByRule => "review first";
        public static string AiGroupsUserHeader(int n) => $"local groups ({n}). Never say the whole folder is safe.";
        public static string GroupBySubdir => "by folder";
        public static string GroupByApp => "by app";
        public static string GroupByRule => "by rule";
        public static string GroupByKind => "by file type";
        public static string GroupUnspecified => "(no rule label)";
        public static string GroupOthers(int hidden) => $"Others ({hidden})";
        public static string GroupFolders => "folders";
        public static string GroupFiles => "files";
        public static string GroupNoExtension => "(no extension)";
        public static string GroupRiskMix(int c, int n) => $"{c} eligible, {n} need confirmation";
        public static string GroupProtectedCount(int n) => $"{n} protected item(s)";
        public static string SelectGroupCandidates(int c, string s) => $"Select this group ({c} · {s})";
        public static string GroupAllSelected => "all already selected";
        public static string GroupNeedsReview => "mixed - review first";
        public static string GroupViewFiles => "View its files";
        public static string GroupLocalOnlyTag => "[local rules] ";
        public static string GroupsHead(int g, int c, string s) => $"{g} groups · {c} eligible · {s}";
        public static string GroupsTruncated(int h) => $"{h} more merged";
        public static string GroupScopeSampled(int a, int b) => $"covered {a} of {b}";
        public static string ItemAiAnalyze => "AI";
        public static string ItemAiQueued => "queued";
        public static string ItemAiRunning => "running";
        public static string ItemAiViewResult => "view result";
        public static string ItemAiRetry => "retry";
        public static string ItemAiFailed => "failed";
        public static string ItemAiTimeout => "timeout";
        public static string ItemAiCanceled => "canceled";
        public static string ItemAiNoUseful => "no useful result";
        public static string ItemAiStopHint => "stop";
        public static string ItemAiTip => "tip";
        public static string ItemAiSuggestionLabel => "Suggestion: ";
        public static string ItemAiPurposeLabel => "What it is: ";
        public static string ItemAiImpactLabel => "If removed: ";
        public static string ItemAiBasisLabel => "Based on: ";
        public static string ItemAiMissingLabel => "Missing: ";
        public static string ItemAiFromCache => "cached";
        public static string ItemAiLocalOnly => "[local rules] ";
        public static string AiSuggestCanConsider => "could be considered";
        public static string AiSuggestNeedsConfirm => "needs your confirmation";
        public static string AiSuggestKeep => "better kept";
        public static string AiSuggestUnknown => "not enough information";
        public static string AiItemFileHeader => "file";
        public static string AiItemFolderHeader => "folder";
        public static string AiItemSummaryScope(int shown, int total) => $"summary {shown} of {total}";
        public static string AiItemSystem => "system";
        public static string AiNeedConfig => "config missing";
        // DiskAnalyst 用到的提示词（离线测试只做工具面断言，内容不重要）
        public static string AiAnalystSystem => "analyst";
        public static string AiScanHeader => "scan:";
        public static string AiFolderAskUser(string path, string listing) => "ask:";
        public static string AiTimeout => "timeout";
        public static string Folder => "folder";
        public static string FilesCol => "file";
        public static string AiCatListHeader => "List:";
        public static string AiCatSystem => "system";
        public static string NothingSelected => "nothing selected";
        public static string CatAll => "All";
        public static string CatOther => "Other";
        public static string GroupDup => "Duplicate";
        public static string ReasonDupKeep => "keep";
        public static string ReasonDupExtra(string keep) => "dup of " + keep;
        public static string DupIncompleteBudget => "incomplete-budget";
        public static string DupIncompletePartial => "incomplete-partial";

        // ---- 分层归类（首页 / 位置 / 明细）----
        public static bool IsEn => false;
        public static string EstUnknown => "size unknown";
        public static string NoSoftwareName => "folder (app not identified)";
        public static string UnknownLocation => "(unrecognised)";
        public static string LocationFiles(int n) => $"{n:N0} files";
        public static string DetailShowing(int shown, int total) => $"Showing {shown:N0} / {total:N0} files";
        public static string LocationsNotShown(int n) => $"+{n} locations not listed";
        public static string LayerSafe => "auto clean";
        public static string LayerConfirm => "review first";
        public static string PurposeHeader => "pick a purpose";
        public static string LocationHeaderFormat(string purpose, int locations) => $"{purpose} · {locations} locations";
        public static string DetailHeaderFormat(string name, int files) => $"{name} · {files} files";
        public static string DeselectAll => "clear";
        public static string OverlapNotice(int n) => $"{n} overlapping";
        public static string DupHitsNotice(int n) => $"{n} dup hits";
        public static string SelectAllInGroup => "select group";
        public static string Collapse => "collapse";
        public static string LoadMore => "load more";
        public static string SearchInScope => "search";
        public static string GroupSelectionTitle => "scope";
        public static string SelectCurrentPage => "this page";
        public static string SelectAllInSearch => "all matches";
        public static string ShowPathAction => "show path";
        public static string HidePathAction => "hide path";
        public static string NothingSelectable => "nothing selectable";
        public static string SelectedRatio(int selected, int total) => $"{selected} / {total} selected";
        public static string SelectWholeGroup(int n) => $"selects whole group ({n})";
        public static string SectionHeader(string title, int kinds, int locs, string size)
            => $"{title} · {kinds} kinds · {locs} locations · {size}";
        /// <summary>分区右侧统计：空间按「约」估算。</summary>
        public static string SectionStats(int kinds, int locs, string size)
            => $"{kinds} kinds · {locs} locations · about {size}";
        /// <summary>分区副标题与图标：风险不单靠颜色表达。</summary>
        public static string SectionSubtitleSafe => "rules are clear; handle first";
        public static string SectionSubtitleConfirm => "may still be valuable; decide after looking";
        public static string SectionIconSafe => "✓";
        public static string SectionIconConfirm => "!";
        /// <summary>折叠的分区里仍有已选项时的提示。</summary>
        public static string CollapsedSelected(int selected, int locs)
            => $"({selected} selected in {locs} locations)";
        public static string LocationPageHead(string purpose, int locs, string size)
            => $"{locs} locations · about {size}";
        public static string NothingSelectedYet => "nothing selected yet";
        public static string SelectionSummary(int locs, int items, string size)
            => $"{locs} locations, {items} items, about {size}";
        public static string GlobalSelectionNote => "selection is global";
        public static string ClearSelection => "clear";
        public static string CheckAndClean => "check and clean";
        public static string ViewSelected => "view selected";
        public static string ViewSelectedTitle => "selected";
        public static string LargestSelected => "largest:";
        public static string AllLoaded => "all loaded";
        public static string DetailScopeCount(int n, string size) => $"{n} items · {size}";
        public static string NoScanYet => "no scan yet";
        public static string CleanStateFailedBody => "nothing changed";
        public static string CleanStatePartialBody => "counts are a lower bound";
        public static string SelectCurrentPageCount(int n) => $"page ({n})";
        public static string SelectAllInSearchCount(int n) => $"search ({n})";
        public static string SelectWholeGroupCount(int n) => $"group ({n})";
        public static string DeselectAllCount(int n) => $"clear ({n})";
        public static string SearchScopeHint(string scope) => $"search covers all {scope}";
        public static string KeepCandidate => "keep";
        public static string ExtraCandidate => "duplicate";
        public static string DuplicateKeepPath => "shortest path, kept";
        public static string DuplicateExtraPath(int copies, int folders) => $"{copies} copies / {folders} folders";
        public static string PurposeDuplicate => "Duplicate files";

        public static string PurposeTemp => "Temporary files";
        public static string PurposeBrowserCache => "Browser cache";
        public static string PurposeAppCache => "App cache";
        public static string PurposeDevCache => "Dev tool cache";
        public static string PurposeAppLog => "App logs";
        public static string PurposeRecycle => "Recycle Bin";
        public static string PurposeDump => "Crash dumps";
        public static string PurposeInstaller => "Installers";
        public static string PurposeLarge => "Large files";
        public static string PurposeOld => "Old files";
        public static string PurposeEmpty => "Empty folders";
        public static string PurposeShortcut => "Broken shortcuts";
        public static string PurposeLongPath => "Very long paths";
        public static string PurposeDelta => "Changed";
        public static string PurposeOther => "Other";

        public static string ImpactTemp => "apps rebuild these";
        public static string ImpactBrowserCache => "pages reload";
        public static string ImpactAppCache => "apps rebuild these";
        public static string ImpactDevCache => "re-downloaded on next build";
        public static string ImpactAppLog => "past log records only";
        public static string ImpactRecycle => "gone for good";
        public static string ImpactDump => "crash records";
        public static string ImpactInstaller => "need re-downloading";
        public static string ImpactDuplicate => "one copy kept";
        public static string ImpactLarge => "large is not useless";
        public static string ImpactOld => "old is not unused";
        public static string ImpactEmpty => "nothing inside";
        public static string ImpactShortcut => "target is gone";
        public static string ImpactLongPath => "some programs cannot open these";
        public static string ImpactDelta => "information only";

        // KnownPaths 用到的那几条
        public static string ReasonOldInstaller => "old installer";
        public static string ReasonVmDisk => "vm disk";
        public static string GroupInstaller => "Installers";
        public static string DeletePreflightNote(int ready, int blocked, int missing, int changed, int inUse, int sensitive)
            => $"preflight ready={ready} blocked={blocked} missing={missing} changed={changed} inuse={inUse} sensitive={sensitive}";
        public static string DeleteResultHead(int recycled, string freed, int total)
            => $"head recycled={recycled} freed={freed} total={total}";
        public static string DeleteResultGroup(AiDiskCleaner.Models.DeletionOutcome outcome, int count, string names)
            => $"{outcome}({count}): {names}";
    }

    /// <summary>离线测试里不需要真的发请求，只挡住 AiCoordinator 的编译依赖。</summary>
    public static class AiClient
    {
        public static Func<AiRequest, Action<string>?, CancellationToken, Task<AiReply>>? Handler { get; set; }

        public static Task<AiReply> StreamAsync(
            AiProviderCfg? p, string? modelId, string system,
            IReadOnlyList<AiMsg> turns, Action<string> onDelta, CancellationToken ct, int maxTurns = 1)
            => Handler != null
                ? Handler(new AiRequest { Provider = p, Model = modelId, System = system, Turns = turns }, onDelta, ct)
                : Task.FromResult(new AiReply { Text = "" });
    }

    /// <summary>
    /// 假的 AI 通道，用来验证 <see cref="AiGateway"/> 的选路、重试、限流与 fallback，
    /// 不需要真的联网或起 sidecar。
    /// </summary>
    public sealed class FakeGateway : IAiGateway
    {
        private readonly Func<AiRequest, CancellationToken, Task<AiReply>> _handler;

        public FakeGateway(string name, Func<AiRequest, CancellationToken, Task<AiReply>> handler)
        {
            Name = name;
            _handler = handler;
        }

        public string Name { get; }
        public int Calls { get; private set; }

        public Task<AiReply> SendAsync(AiRequest request, Action<string>? onDelta, CancellationToken ct)
        {
            Calls++;
            return _handler(request, ct);
        }
    }

    /// <summary>测试用的通道提供者：两条通道都能换，sidecar 开关也能控。</summary>
    public sealed class FakeGatewayProvider : IAiGatewayProvider
    {
        public bool SidecarEnabled { get; set; }
        public IAiGateway Sidecar { get; set; } = new FakeGateway("sidecar", (_, _) => Task.FromResult(new AiReply()));
        public IAiGateway Direct { get; set; } = new FakeGateway("http", (_, _) => Task.FromResult(new AiReply()));
    }
}
