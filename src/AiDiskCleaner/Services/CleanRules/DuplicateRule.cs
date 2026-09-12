using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.CleanRules;

/// <summary>
/// 重复文件规则：把 <see cref="DuplicateScanService"/> 的结果投进报告。
///
/// 注意：默认路径下重复检测**不跑在这里** —— 它太慢，应该作为独立可取消阶段在清理列表之后跑
/// （见 <c>CleanAnalyzer.Analyze(includeDuplicates: false)</c> + <c>DuplicateScanService</c>）。
/// 保留这条规则是为了「一次性全跑」的调用方（含离线检查）。
/// </summary>
public sealed class DuplicateFileRule : ICleanRule
{
    private const int MaxHits = 200;

    public string Name => "duplicates";
    public string Category => "duplicate";
    public CleanPurpose Purpose => CleanPurpose.Duplicate;
    public CleanRisk DefaultRisk => CleanRisk.Confirm;
    public EvidenceLevel Evidence => EvidenceLevel.Verified;
    public bool CanDelete => true;
    public bool SupportsCancellation => true;

    /// <summary>测试注入点：探文件身份，用于硬链接合并。</summary>
    public Func<string, FileIdentity?>? IdentityProbe { get; set; }

    public DuplicateStats? LastStats { get; private set; }

    public void Evaluate(CleanRuleContext ctx, ICleanRuleSink sink)
    {
        ctx.Progress?.Report(new ScanProgress(ctx.Files.Count, Loc.CleanDups, 70));

        var service = new DuplicateScanService(IdentityProbe);
        var outcome = service.Run(ctx.Files, ctx.Ct, null);
        LastStats = outcome.Result.Stats;

        foreach (var item in outcome.Items)
        {
            if (sink.Count(CleanRuleTarget.Duplicates) >= MaxHits) return;
            if (item.Entry == null) continue;
            sink.Add(new CleanRuleHit
            {
                Entry = item.Entry,
                Target = CleanRuleTarget.Duplicates,
                Reason = item.Reason,
                Group = item.Group,
                Purpose = CleanPurpose.Duplicate,
                Handling = item.Handling,
                Risk = item.Risk,
                CanDelete = item.CanDelete,
                Selected = item.Selected,
                Evidence = EvidenceLevel.Verified,
                ReasonIsEssential = true,
                Tech = item.Tech,
            });
        }
    }
}
