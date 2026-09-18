namespace AiDiskCleaner.Services.CleanRules;

/// <summary>
/// Jev 判出来的大类。
///
/// 刻意**比 <see cref="CleanPurpose"/> 更宽**：Jev 面对的是「本地规则认不出来的那批」，
/// 那批里什么都有——可能是缓存，也可能是用户自己的照片、系统文件、模型权重。
/// 只给它「可清理」的类别，等于逼它把一张全家福塞进「应用缓存」。
/// </summary>
public enum AiPurposeKind
{
    Temporary,
    BrowserCache,
    AppCache,
    DevCache,
    AppLog,
    Dump,
    Installer,
    /// <summary>模型 / 游戏素材这类大件：删了要重新下载，不是「会自动重建」。</summary>
    Model,
    /// <summary>用户自己的数据，或 Windows 自己的文件。<b>只用于提示，绝不用于阻挡删除。</b></summary>
    Keep,
    /// <summary>认不出来。什么都不做。</summary>
    Unknown,
}

/// <summary>
/// 用途判定的选项表、回写映射与置信度阈值。
///
/// 三条设计约束（都有实测依据，别随手改）：
///
/// <list type="number">
/// <item><b>只留一个「别删」出口。</b>把「用户数据 / 系统文件 / 开发项目」拆成三类时，
/// 边界立刻模糊：实测同一批路径里，用户视频被判成「项目」、Windows 更新缓存的置信度
/// 从中等的 0.43 变成「系统 0.84」（更容易误导）。合并成一个出口后，
/// 视频 / 程序本体 / 游戏存档全部正确落进 keep。</item>
///
/// <item><b>选项集越小越准。</b>实测每项 token：6 类 293、9 类 232、10 类 304、
/// 13 类 337——类别越多不光更贵，准确率还下降。</item>
///
/// <item><b>置信度阈值按「判错的代价」分档。</b>说「这是缓存」说错了会误导用户去删；
/// 说「别删」说错了只是少清一个东西。所以前者阈值高、后者低。</item>
/// </list>
///
/// <b>安全不变量：这一整套结果永远不改 Risk / CanDelete / Selected。</b>
/// 回写出去的用途一律压在 <see cref="Models.EvidenceLevel.Heuristic"/>，
/// 而批量勾选要求 <see cref="Models.EvidenceLevel.Signature"/>——
/// 也就是说 **AI 推测在结构上就够不到「界面替你打勾」那道闸**，不靠提示词约束。
/// </summary>
public static class AiPurposeCriteria
{
    /// <summary>
    /// 选项键的**规范列表**（顺序即展示顺序）。
    ///
    /// 键是稳定契约：它进请求体、也是以后做结果缓存时的缓存键 —— <b>不要改</b>。
    /// 文案（双语）在 <c>Loc.AiPurposeOptions</c> 里；键放这里是为了让离线回归
    /// 不用碰 Loc 就能断言「选项表是闭合的、正好这 10 个」。
    /// </summary>
    public static readonly string[] Keys =
    {
        "temp", "browsercache", "appcache", "devcache", "applog",
        "dump", "installer", "model", "keep", "unknown",
    };

    /// <summary>Jev 返回的键 → 大类。认不出的键一律当 <see cref="AiPurposeKind.Unknown"/>。</summary>
    public static AiPurposeKind Parse(string? key) => (key ?? "").Trim().ToLowerInvariant() switch
    {
        "temp" => AiPurposeKind.Temporary,
        "browsercache" => AiPurposeKind.BrowserCache,
        "appcache" => AiPurposeKind.AppCache,
        "devcache" => AiPurposeKind.DevCache,
        "applog" => AiPurposeKind.AppLog,
        "dump" => AiPurposeKind.Dump,
        "installer" => AiPurposeKind.Installer,
        "model" => AiPurposeKind.Model,
        "keep" => AiPurposeKind.Keep,
        _ => AiPurposeKind.Unknown,
    };

    /// <summary>
    /// 这一类能不能落到 <see cref="CleanPurpose"/> 上。
    /// <see cref="AiPurposeKind.Keep"/> / <see cref="AiPurposeKind.Unknown"/> 落不下去——
    /// 它们要么是「不该动」，要么是「没结论」，都不该被写成某个清理用途。
    /// </summary>
    public static bool TryToPurpose(AiPurposeKind kind, out CleanPurpose purpose)
    {
        switch (kind)
        {
            case AiPurposeKind.Temporary: purpose = CleanPurpose.Temp; return true;
            case AiPurposeKind.BrowserCache: purpose = CleanPurpose.BrowserCache; return true;
            case AiPurposeKind.AppCache: purpose = CleanPurpose.AppCache; return true;
            case AiPurposeKind.DevCache: purpose = CleanPurpose.DevCache; return true;
            case AiPurposeKind.AppLog: purpose = CleanPurpose.AppLog; return true;
            case AiPurposeKind.Dump: purpose = CleanPurpose.Dump; return true;
            case AiPurposeKind.Installer: purpose = CleanPurpose.Installer; return true;
            default: purpose = CleanPurpose.Other; return false;
        }
    }

    /// <summary>
    /// 采纳这条判定所需的最低置信度。
    ///
    /// 实测参考（16 条真实路径）：判对且 0.89~1.00 的集中在「认识」；0.63~0.77 的是
    /// 「认得但语焉不详」（用户视频、程序本体、IE 缓存）；0.22~0.46 的全部是模糊项。
    /// 所以清理类卡 0.75 —— **宁可少认几条，也不要给用户一个似是而非的「这是缓存」**。
    /// keep 卡 0.55：方向保守，说错了只是少清一个东西。
    /// </summary>
    public static double ThresholdFor(AiPurposeKind kind) => kind switch
    {
        AiPurposeKind.Keep => 0.55,
        AiPurposeKind.Temporary or AiPurposeKind.BrowserCache or AiPurposeKind.AppCache
            or AiPurposeKind.DevCache or AiPurposeKind.AppLog or AiPurposeKind.Dump
            or AiPurposeKind.Installer => 0.75,
        // Model 归到清理类那一档：它同样会告诉用户「这东西可以删」。
        AiPurposeKind.Model => 0.75,
        _ => 1.0, // Unknown 本来就没结论
    };

    /// <summary>这条判定够不够格被采纳。</summary>
    public static bool IsAccepted(AiPurposeKind kind, double confidence)
        => kind != AiPurposeKind.Unknown && confidence >= ThresholdFor(kind);

    /// <summary>能不能显示成「AI 推测的用途」（rest 只做展示，不回写用途）。</summary>
    public static bool IsInformational(AiPurposeKind kind)
        => kind is AiPurposeKind.Keep or AiPurposeKind.Model;
}
