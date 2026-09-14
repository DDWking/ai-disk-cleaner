using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.CleanRules;

// 证据强度 EvidenceLevel 定义在 AiDiskCleaner.Models（见 Models/CleanEvidence.cs）：
// 它是候选自己的属性（CleanItem.Evidence），要一路走到界面，不属于某一条规则。

/// <summary>规则把条目投到报告的哪个列表。</summary>
public enum CleanRuleTarget
{
    Cleanable,
    Large,
    Old,
    EmptyFolders,
    BrokenShortcuts,
    LongPaths,
    Duplicates,
    Compare,
}

/// <summary>规则产出的一条候选。</summary>
public sealed class CleanRuleHit
{
    public required FileEntry Entry { get; init; }
    public required CleanRuleTarget Target { get; init; }
    public required string Reason { get; init; }
    /// <summary>分类名（会被应用签名的分类覆盖）。</summary>
    public string? Group { get; init; }
    /// <summary>
    /// 用途分类（首页第一层）。由规则声明 —— 这是「凭什么认为它可清理」的一部分证据，
    /// 不是渲染时按路径名猜出来的。
    /// </summary>
    public CleanPurpose Purpose { get; init; } = CleanPurpose.Other;
    public CleanRisk Risk { get; init; } = CleanRisk.Confirm;
    public bool CanDelete { get; init; } = true;
    public bool Selected { get; init; }
    public EvidenceLevel Evidence { get; init; } = EvidenceLevel.Heuristic;
    /// <summary>回收站里的文件显示用户认得的原名，删除仍用真实路径。</summary>
    public string? DisplayName { get; init; }
    /// <summary>技术说明（悬停/日志）。</summary>
    public string? Tech { get; init; }

    /// <summary>
    /// 这条命中的「说明」是否不可被路径签名的文案替换。
    ///
    /// 设为 true 用于说明本身承载了必需信息的情况 —— 例如重复项必须写清
    /// 「和哪个文件一模一样」以及「本轮检测有没有跑完」。签名那句大白话再准，
    /// 也不能把这些信息顶掉。
    /// </summary>
    public bool ReasonIsEssential { get; init; }
    /// <summary>
    /// 同一位置里「处理方式」不同的候选要拆开显示。
    /// 重复文件的「保留项」和「多余项」就是典型：不能混在一行里。
    /// </summary>
    public string Handling { get; init; } = "";
}

/// <summary>规则运行需要的东西。规则不许自己去扫盘，只用这里已经准备好的数据。</summary>
public sealed class CleanRuleContext
{
    public required FileEntry Root { get; init; }
    public required IReadOnlyList<FileEntry> Files { get; init; }
    public required IReadOnlyList<FileEntry> Dirs { get; init; }
    public ScanSnapshot? Previous { get; init; }
    public CancellationToken Ct { get; init; }
    public IProgress<ScanProgress>? Progress { get; init; }
}

/// <summary>规则把结果投进来。sink 负责按 (目标列表, 路径) 去重。</summary>
public interface ICleanRuleSink
{
    /// <summary>投一条。返回 false 表示被去重或超过了该列表的容量上限。</summary>
    bool Add(CleanRuleHit hit);

    /// <summary>某个列表已经收了多少条（规则自己控制预算用）。</summary>
    int Count(CleanRuleTarget target);

    /// <summary>重复文件分组计数。</summary>
    void CountDuplicateGroup();
}

/// <summary>
/// 一条清理规则。每条规则独立、可单独测试；抛异常不会影响别的规则。
/// </summary>
public interface ICleanRule
{
    /// <summary>规则名称（日志、诊断、测试用）。</summary>
    string Name { get; }
    /// <summary>规则归属的分类（用户能看到的粗分类）。</summary>
    string Category { get; }
    /// <summary>这条规则默认产出的用途分类（首页第一层）。</summary>
    CleanPurpose Purpose { get; }
    /// <summary>规则默认给的风险档位。</summary>
    CleanRisk DefaultRisk { get; }
    /// <summary>规则的证据等级。</summary>
    EvidenceLevel Evidence { get; }
    /// <summary>这条规则产出的条目是否允许删除。</summary>
    bool CanDelete { get; }
    /// <summary>是否支持取消（长跑规则必须支持）。</summary>
    bool SupportsCancellation { get; }

    /// <summary>
    /// 这条规则的风险是否「说了算」。
    ///
    /// true 表示路径签名**不得把它降级成 Safe**。用在「大 / 旧 / 路径特殊」这类规则上：
    /// 仅仅因为文件大、老、或者路径恰好落在某个缓存目录附近，不足以把它放进「建议清理」。
    /// 小目录缓存类规则（临时/缓存）保持 false，签名认出来就按签名走 —— 那才是它们的主要证据。
    /// </summary>
    bool RiskIsAuthoritative => false;

    /// <summary>产出候选。实现里不要吞掉 OperationCanceledException。</summary>
    void Evaluate(CleanRuleContext ctx, ICleanRuleSink sink);
}

/// <summary>单条规则的运行统计：耗时、产出条数、是否出错。界面/日志据此说明「哪条规则慢/挂了」。</summary>
public sealed record CleanRuleTiming(string Rule, string Category, TimeSpan Elapsed, int Hits, string? Error)
{
    public double ElapsedMs => Elapsed.TotalMilliseconds;
}

/// <summary>规则共用的判定。抽出来是为了让「什么算可清理」只有一处定义。</summary>
public static class CleanRuleHelpers
{
    public static readonly HashSet<string> TempExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".temp", ".cache", ".bak", ".old", ".log", ".etl", ".dmp", ".chk", ".gid",
    };

    public static readonly HashSet<string> InstallExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".msi", ".iso", ".msu",
    };

    private static readonly string[] SafeDirBits =
    {
        @"\temp\", @"\tmp\", @"\cache\", @"\caches\", @"\logs\",
        @"\crashdumps\", @"\minidump\", @"\wer\", @"\downloads\",
        @"\$recycle.bin\", @"\windows\temp\", @"\windows\softwaredistribution\download\",
        @"\appdata\local\temp\", @"\appdata\local\microsoft\windows\inetcache\",
        @"\appdata\local\microsoft\windows\explorer\",
        @"\appdata\local\crashdumps\",
    };

    private static readonly string[] UnsafeBits =
    {
        @"\windows\system32\", @"\windows\syswow64\", @"\windows\winsxs\",
        @"\windows\servicing\", @"\$mft", @"\program files\", @"\program files (x86)\",
    };

    public static string Lower(string? path) => (path ?? "").ToLowerInvariant().Replace('/', '\\');

    public static bool LooksLikeTempDir(string lowerPath) => SafeDirBits.Any(lowerPath.Contains);

    public static bool LooksUnsafe(string lowerPath) => UnsafeBits.Any(lowerPath.Contains);

    /// <summary>够不够格进列表：不是散文件合成组、有完整路径、不是 NTFS 元数据。</summary>
    public static bool CanList(FileEntry e)
        => !e.IsFilesGroup && !string.IsNullOrEmpty(e.FullPath) && !e.Name.StartsWith('$');

    /// <summary>
    /// 够不够格作为「可删候选」。**用统一的清理资格判据** ——
    /// 与删除时的保护规则同一口径，避免出现「界面能勾、执行被拦」，
    /// 或「名字写着别删、实际却能删」。
    /// </summary>
    public static bool CanOffer(FileEntry e)
    {
        if (!CanList(e)) return false;
        if (ProtectedPaths.IsCleanupBlocked(e)) return false;
        return !LooksUnsafe(Lower(e.FullPath));
    }
}
