using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.CleanRules;

/// <summary>
/// 用途分类（首页第一层）。
///
/// **必须由规则声明，不能在展示层按路径名猜** —— 分类是「这条规则凭什么认为它可清理」的一部分，
/// 和风险等级一样属于证据。所以 Purpose 跟着 <see cref="ICleanRule"/> / <see cref="CleanRuleHit"/>
/// 一起产出，而不是渲染时用字符串匹配。
/// </summary>
public enum CleanPurpose
{
    Temp,
    BrowserCache,
    AppCache,
    DevCache,
    AppLog,
    Recycle,
    Dump,
    Installer,
    Duplicate,
    Large,
    Old,
    EmptyFolder,
    BrokenShortcut,
    LongPath,
    Delta,
    Other,
}

/// <summary>用途分类的展示信息。风险不在这里 —— 风险一律沿用候选自己的判定。</summary>
public static class CleanPurposes
{
    public static string Name(CleanPurpose p) => p switch
    {
        CleanPurpose.Temp => Loc.PurposeTemp,
        CleanPurpose.BrowserCache => Loc.PurposeBrowserCache,
        CleanPurpose.AppCache => Loc.PurposeAppCache,
        CleanPurpose.DevCache => Loc.PurposeDevCache,
        CleanPurpose.AppLog => Loc.PurposeAppLog,
        CleanPurpose.Recycle => Loc.PurposeRecycle,
        CleanPurpose.Dump => Loc.PurposeDump,
        CleanPurpose.Installer => Loc.PurposeInstaller,
        CleanPurpose.Duplicate => Loc.PurposeDuplicate,
        CleanPurpose.Large => Loc.PurposeLarge,
        CleanPurpose.Old => Loc.PurposeOld,
        CleanPurpose.EmptyFolder => Loc.PurposeEmpty,
        CleanPurpose.BrokenShortcut => Loc.PurposeShortcut,
        CleanPurpose.LongPath => Loc.PurposeLongPath,
        CleanPurpose.Delta => Loc.PurposeDelta,
        _ => Loc.PurposeOther,
    };

    /// <summary>一句话说清「清掉这些会怎样」。首页要用它回答「有什么影响」。</summary>
    public static string Impact(CleanPurpose p) => p switch
    {
        CleanPurpose.Temp => Loc.ImpactTemp,
        CleanPurpose.BrowserCache => Loc.ImpactBrowserCache,
        CleanPurpose.AppCache => Loc.ImpactAppCache,
        CleanPurpose.DevCache => Loc.ImpactDevCache,
        CleanPurpose.AppLog => Loc.ImpactAppLog,
        CleanPurpose.Recycle => Loc.ImpactRecycle,
        CleanPurpose.Dump => Loc.ImpactDump,
        CleanPurpose.Installer => Loc.ImpactInstaller,
        CleanPurpose.Duplicate => Loc.ImpactDuplicate,
        CleanPurpose.Large => Loc.ImpactLarge,
        CleanPurpose.Old => Loc.ImpactOld,
        CleanPurpose.EmptyFolder => Loc.ImpactEmpty,
        CleanPurpose.BrokenShortcut => Loc.ImpactShortcut,
        CleanPurpose.LongPath => Loc.ImpactLongPath,
        CleanPurpose.Delta => Loc.ImpactDelta,
        _ => "",
    };

    /// <summary>
    /// 首页排序：先按「用户最可能想清」的顺序，再按占用降序。
    /// 大文件/旧文件这种「必须用户自己判断」的排在后面，避免首页第一眼就是它们。
    /// </summary>
    public static int Rank(CleanPurpose p) => p switch
    {
        CleanPurpose.Temp => 0,
        CleanPurpose.BrowserCache => 1,
        CleanPurpose.AppCache => 2,
        CleanPurpose.DevCache => 3,
        CleanPurpose.AppLog => 4,
        CleanPurpose.Dump => 5,
        CleanPurpose.Recycle => 6,
        CleanPurpose.Installer => 7,
        CleanPurpose.Duplicate => 8,
        CleanPurpose.EmptyFolder => 9,
        CleanPurpose.BrokenShortcut => 10,
        CleanPurpose.LongPath => 11,
        CleanPurpose.Old => 12,
        CleanPurpose.Large => 13,
        CleanPurpose.Delta => 14,
        _ => 15,
    };

    /// <summary>从签名分类（AppSignatures 的 Category key）细化用途。签名是证据，优先采纳。</summary>
    public static CleanPurpose FromSignatureCategory(string? categoryKey, CleanPurpose fallback) => categoryKey switch
    {
        "browser" => CleanPurpose.BrowserCache,
        "dev" => CleanPurpose.DevCache,
        "im" or "media" or "cloud" or "ide" or "ai" or "office" or "game" or "ime" or "vm"
            or "security" or "bloat" => CleanPurpose.AppCache,
        _ => fallback,
    };
}
