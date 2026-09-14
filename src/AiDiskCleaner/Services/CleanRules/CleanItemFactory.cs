using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.CleanRules;

/// <summary>
/// 把规则命中变成界面条目。**应用签名统一在这里生效** ——
/// 能认出用途就用它的分类、风险和大白话说明；认不出来才回落到规则给的兜底值。
///
/// 单独抽出来是因为两条路都要用它：清理规则流水线（<see cref="ICleanRuleSink"/>）
/// 和独立跑的重复检测（<c>DuplicateScanService</c>）。两处必须完全一致，
/// 否则同一个文件会因为走哪条路而显示不同的颜色和分组。
/// </summary>
public static class CleanItemFactory
{
    /// <param name="riskIsAuthoritative">
    /// 规则的风险说了算（大 / 旧 / 长路径 / 变化）时为 true：
    /// 路径签名只能补充分类与说明，**不得把风险降级成 Safe**。
    /// </param>
    public static CleanItem Create(CleanRuleHit hit, bool riskIsAuthoritative = false)
    {
        var e = hit.Entry;
        var cls = AppSignatures.Classify(e.FullPath);
        string category = cls?.Key ?? "";
        string groupName = cls?.Name ?? hit.Group ?? "";
        // 风险归属：默认让签名细化；来自「大/旧/长路径/变化」这类规则时以规则为准，
        // 免得一个 8GB 的普通文件仅因路径挨着缓存目录就被放进「建议清理」。
        var finalRisk = riskIsAuthoritative ? hit.Risk : (cls?.Risk ?? hit.Risk);
        string plain = cls?.Plain ?? "";
        // 说明取舍：签名认出来了就用它那句大白话（比「大文件 · 看不出用途」具体）。
        // 但两种情况必须用规则自己的说明，不能被签名文案顶掉：
        //  - 风险由规则说了算（否则会出现「要你确认」配「删了会自动重建」，自相矛盾）；
        //  - 说明本身必需（重复项要写清和谁重复、检测有没有跑完）。
        bool keepRuleReason = riskIsAuthoritative || hit.ReasonIsEssential;
        string finalReason = keepRuleReason || string.IsNullOrEmpty(plain) ? hit.Reason : plain;

        bool canOffer = CleanRuleHelpers.CanOffer(e);
        bool canDelete = hit.CanDelete && canOffer;

        var item = new CleanItem
        {
            Name = e.Name,
            FullPath = e.FullPath,
            Size = e.Size,
            Reason = finalReason,
            Tech = hit.Tech ?? AppSignatures.Describe(e.FullPath) ?? "",
            Group = groupName,
            Category = category,
            Purpose = riskIsAuthoritative ? hit.Purpose : RefinePurpose(hit.Purpose, category),
            Handling = hit.Handling,
            Risk = finalRisk,
            CanDelete = canDelete,
            // 规则的证据强度原样带上：「批量选择规则明确的项」只能靠它，不靠路径字符串猜
            Evidence = hit.Evidence,
            Selected = hit.Selected && canDelete && finalRisk != CleanRisk.Keep,
            Entry = e,
            IsDirectory = e.IsDirectory,
        };
        if (!string.IsNullOrEmpty(hit.DisplayName)) item.Name = hit.DisplayName;
        return item;
    }

    /// <summary>
    /// 用签名分类细化用途。**只在「缓存类」用途内部细化** ——
    /// 大文件/旧文件/重复文件这些必须留在原来的用途里：
    /// 一个 8GB 的视频即使躺在某个浏览器缓存目录下，也仍然是「大文件·需确认」，
    /// 不能因为签名命中就被归进「浏览器缓存」而看起来像默认可清理。
    /// 风险等级完全不在这里动。
    /// </summary>
    private static CleanPurpose RefinePurpose(CleanPurpose rulePurpose, string? signatureCategory)
    {
        bool refinable = rulePurpose is CleanPurpose.Temp or CleanPurpose.AppCache
            or CleanPurpose.DevCache or CleanPurpose.BrowserCache
            or CleanPurpose.AppLog or CleanPurpose.Other;
        if (!refinable) return rulePurpose;
        return CleanPurposes.FromSignatureCategory(signatureCategory, rulePurpose);
    }
}
