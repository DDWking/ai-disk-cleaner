using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.CleanRules;

/// <summary>
/// 「哪些候选可以被批量勾选」的**唯一判据**。
///
/// 这是本轮新增的、也是唯一新增的一处判定，它**不是**第三套规则引擎：
/// 它只读取规则自己已经产出的字段（<see cref="CleanItem.Risk"/>、
/// <see cref="CleanItem.CanDelete"/>、<see cref="CleanItem.Evidence"/>、
/// <see cref="CleanItem.Purpose"/>），不重新判断文件是什么、也不看路径里有没有
/// "cache" 这种字符串，更不看 AI 说了什么。
///
/// 允许被批选的只有一种条目：**正式规则明确说它安全、证据达到签名/已验证级、
/// 而且清理资格检查放行**。其余（大文件 / 旧文件 / 下载里的安装包 / 重复文件 /
/// 空目录 / 长路径 / 未知用途）永远只能由用户逐个自己挑。
/// </summary>
public static class CleanRuleEligibility
{
    /// <summary>
    /// 可以作为「规则明确的清理项」被批选的用途。
    ///
    /// 只含缓存/临时/转储这类**规则本身就说清了它是什么**的用途：
    /// 它们删掉的后果是确定的（会自动重建，或者本来就是一次性的）。
    /// 明确排除：
    /// <list type="bullet">
    /// <item>Large / Old —— 「大」和「旧」不是可删理由，必须用户自己判断；</item>
    /// <item>Installer —— 下载目录里的安装包，可能是用户还要用的东西；</item>
    /// <item>Duplicate —— 删哪一个必须用户自己定；</item>
    /// <item>EmptyFolder / LongPath / Delta / BrokenShortcut / Other —— 处置方式不是「勾了就删」。</item>
    /// </list>
    /// </summary>
    public static bool IsBatchEligiblePurpose(CleanPurpose purpose) => purpose switch
    {
        CleanPurpose.Temp => true,
        CleanPurpose.BrowserCache => true,
        CleanPurpose.AppCache => true,
        CleanPurpose.DevCache => true,
        CleanPurpose.AppLog => true,
        CleanPurpose.Dump => true,
        _ => false,
    };

    /// <summary>这条候选够不够格被「选择规则明确的清理项」带走。</summary>
    public static bool IsRuleClear(CleanItem item)
        => item.CanDelete
           && item.Risk == CleanRisk.Safe
           && item.Evidence >= EvidenceLevel.Signature
           && IsBatchEligiblePurpose(item.Purpose);

    /// <summary>
    /// 应用批量选择前的**最后一次**核对：重新走一遍清理资格与保护路径判定。
    /// 界面上的 <see cref="CleanItem.CanDelete"/> 是扫描时算的，这里再验一次
    /// （保护规则、界面口径、执行口径始终只有一处定义）。
    /// </summary>
    public static bool IsStillDeletable(CleanItem item)
        => item.CanDelete
           && item.Entry != null
           && CleanRuleHelpers.CanOffer(item.Entry)
           && !ProtectedPaths.IsCleanupBlocked(item.Entry);
}
