namespace AiDiskCleaner.Services;

/// <summary>
/// 只为编译 CleanItem / CleanPurpose 而存在的最小桩。
/// 真正的 Loc 在界面工程里，依赖 App.Settings，测试进程里不需要。
/// 这里只给「用途分类名」，是 CleanPurpose 的展示层依赖；不影响解析器检查本身。
/// </summary>
public static class Loc
{
    /// <summary>不可删候选在行内列表里的标记（CleanItem.KeptBadge 用）。</summary>
    public static string KeptItemBadge => "保留";
    public static string RiskSafe => "可安全删除";
    public static string RiskConfirm => "需确认";
    public static string RiskKeep => "别删";

    public static string PurposeTemp => "Temporary files";
    public static string PurposeBrowserCache => "Browser cache";
    public static string PurposeAppCache => "App cache";
    public static string PurposeDevCache => "Dev tool cache";
    public static string PurposeAppLog => "App logs";
    public static string PurposeRecycle => "Recycle Bin";
    public static string PurposeDump => "Crash dumps";
    public static string PurposeInstaller => "Installers";
    public static string PurposeDuplicate => "Duplicate files";
    public static string PurposeLarge => "Large files";
    public static string PurposeOld => "Old files";
    public static string PurposeEmpty => "Empty folders";
    public static string PurposeShortcut => "Broken shortcuts";
    public static string PurposeLongPath => "Very long paths";
    public static string PurposeDelta => "Changed since last scan";
    public static string PurposeOther => "Other";

    public static string ImpactTemp => "";
    public static string ImpactBrowserCache => "";
    public static string ImpactAppCache => "";
    public static string ImpactDevCache => "";
    public static string ImpactAppLog => "";
    public static string ImpactRecycle => "";
    public static string ImpactDump => "";
    public static string ImpactInstaller => "";
    public static string ImpactDuplicate => "";
    public static string ImpactLarge => "";
    public static string ImpactOld => "";
    public static string ImpactEmpty => "";
    public static string ImpactShortcut => "";
    public static string ImpactLongPath => "";
    public static string ImpactDelta => "";
    // 逐项 AI 用到的文案（本工程只关心 CleanItem 能编译）
    public static string ItemAiLocalOnly => "[local rules] ";
    public static string AiSuggestCanConsider => "could be considered";
    public static string AiSuggestNeedsConfirm => "needs your confirmation";
    public static string AiSuggestKeep => "better kept";
    public static string AiSuggestUnknown => "not enough information";
    public static string AiItemFileHeader => "file";
    public static string AiItemFolderHeader => "folder";
    public static string AiItemSummaryScope(int shown, int total) => $"summary {shown} of {total}";
    public static string ItemAiAnalyze => "AI";
    public static string ItemAiTip => "tip";
    public static string ItemAiQueued => "queued";
    public static string ItemAiRunning => "running";
    public static string ItemAiViewResult => "view result";
    public static string ItemAiRetry => "retry";
    public static string ItemAiFailed => "failed";
    public static string ItemAiTimeout => "timeout";
    public static string ItemAiCanceled => "canceled";
    public static string ItemAiNoUseful => "no useful result";
    public static string ItemAiSuggestionLabel => "Suggestion: ";
    public static string ItemAiPurposeLabel => "What it is: ";
    public static string ItemAiImpactLabel => "If removed: ";
    public static string ItemAiBasisLabel => "Based on: ";
    public static string ItemAiMissingLabel => "Missing: ";
    public static string ItemAiFromCache => "cached";
    public static string Folder => "folder";
    public static string GroupSummaryLine(int i, int c, string s, string src) => $"{i} items, {c} eligible, {s}, {src}";
    public static string GroupLocalOnlyTag => "[local rules] ";
    public static string GroupAiTag => "[AI] ";
    public static string GroupNeedsReview => "review first";
    public static string GroupProtectedOnly => "protected";
    public static string GroupViewFiles => "view files";
    public static string GroupBySubdir => "by folder";
    public static string GroupByApp => "by app";
    public static string GroupByRule => "by rule";
    public static string GroupByKind => "by kind";
    public static string GroupUnspecified => "(none)";
    public static string GroupOthers(int n) => $"Others ({n})";
    public static string GroupFolders => "folders";
    public static string GroupFiles => "files";
    public static string GroupNoExtension => "(no ext)";
    public static string GroupRiskMix(int a, int b) => $"{a}/{b}";
    public static string GroupProtectedCount(int n) => $"{n} protected";
    public static string SelectGroupCandidates(int c, string s) => $"select {c} {s}";
    public static string GroupAllSelected => "all selected";
    public static string GroupsHead(int g, int c, string s) => $"{g} groups, {c}, {s}";
    public static string GroupsTruncated(int h) => $"{h} merged";
    public static string GroupScopeSampled(int a, int b) => $"{a}/{b}";
    public static string AiGroupsUserHeader(int n) => $"groups({n})";
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
    public static string CatAll => "All";
    public static string CatOther => "Other";
    public static string NoteCache => "cache";
    public static string AiAgeDays(int d) => $"{d} day(s) ago";
    public static string AiAgeMonths(int m) => $"{m} month(s) ago";
    public static string AiAgeToday => "today";
    public static string AiAgeYears(int y) => $"{y} year(s) ago";
    public static string AiAnalyzing => "analysing";
    public static string AiBucketCleanable => "Can be cleaned";
    public static string AiBucketKeep => "Better kept";
    public static string AiBucketReview => "Needs confirmation";
    public static string AiBucketStat(int c, string s) => $"{c} item(s) · {s}";
    public static string AiEverythingSelected => "all already selected";
    public static string AiFilteredToCount(int n) => $"only these {n}";
    public static string AiHeadlineClean(int c, string s) => $"Suggest cleaning {c} item(s) - about {s} can be freed";
    public static string AiHeadlineNothing => "Nothing here is worth cleaning right now";
    public static string AiHeadlineReview(int c, string s) => $"{c} item(s) need your confirmation ({s})";
    public static string AiNoteBelongs(string w) => $"These are {w}";
    public static string OtherFilesTitle => "Other files";
    public static string GroupStatLine(int c, string s) => $"{c} items · {s}";
    public static string GroupSelectedLine(int c, string s) => $"selected {c} · {s}";
    public static string AiResultExpired => "scan changed, analyse again";     public static string AiNoResultRetry => "no usable result, try again";     public static string AiNoteByLocalRule => "matched the local cleanup rules";     public static string AiWhyNoLockCheck => "File locks were not checked";
    public static string AiNoteUnknown => "we could not confirm what they are for";
    public static string AiReasonCleanable => "the app rebuilds these";
    public static string AiReasonKeep => "protected";
    public static string AiReasonReview => "open the files first";
    public static string AiSelectTheseFiles => "Select these files";
    public static string AiViewFiles => "View files";
    public static string AiWhyLocation(string w) => $"Located in {w}";
    public static string AiWhyMatched(string t) => $"Matched rule: {t}";
    public static string AiWhyModified(string a) => $"Last changed {a}";
    public static string AiWhyNoContentRead => "File contents were not read or uploaded";
    public static string AiWhyNoModel => "This is the local summary - no model advice was used";
    public static string AiWhyNothingSafe => "No item here passed the local safety rules";
    public static string AiWhyProtected(int n) => $"{n} item(s) are protected";
    public static string AiWhySomeUncertain(int n) => $"{n} item(s) still cannot be confirmed";
    public static string AiWhyToggle => "Why this suggestion?";
    public static string GroupFilterHint(string w) => "filter " + w;
}

/// <summary>PathRedactor.Outbound 需要 App.Settings；测试里给个最简实现。</summary>
public static class App
{
    public static AppSettings Settings { get; } = new();
}

/// <summary>
/// 只为 PathRedactor.Outbound 编译通过的最小设置桩。
/// 刻意不链真实的 AppSettings：它会拖进 AppLog / SecretStore 一整串依赖。
/// </summary>
public sealed class AppSettings
{
    public bool AiSendFullPaths { get; set; }
}
