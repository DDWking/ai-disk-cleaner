using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>
/// 一次批量归类的结果，用来给用户一句实话（而不是「已完成」）。
///
/// <paramref name="Unsure"/> 和 <paramref name="Unknown"/> **必须分开**：
/// 前者是「模型给了答案但没把握」（放宽阈值就能救），后者是「模型说不出」（得改提示词）。
/// 两者在界面上都显示成「未识别」，混成一个数就没法判断问题出在哪。
/// </summary>
public sealed record AiPurposeBatchOutcome(
    int Asked,
    int Applied,
    int Unsure,
    int Unknown,
    int Calls,
    int InputTokens,
    double Cost);

/// <summary>
/// 「按文件夹删除」页的批量归类入口：把本地认不出用途的文件夹交给判定通道（Jev 这类）归类。
///
/// <b>这个类只是一层门面</b>：它负责「挑出该问的条目、解析判定通道配置、把结果翻译成界面
/// 用的那个 outcome」，真正的分批/重试/折半/取消/遥测在 <see cref="DecisionRunner"/>，
/// 选项表与回写在 <see cref="FolderPurposeDecisionSet"/>。
/// 这么分是为了让第二批判定域（重复文件「留哪一个」）能复用同一套规则，
/// 而不是把「一批撑爆要拆」再写一遍。
/// </summary>
public static class AiPurposeBatchService
{
    /// <summary>一次请求最多问多少条。真值在判定域里（现在是 1：一个目录一次请求），这里保留转发是为了让界面文案能引用它。</summary>
    public static int MaxPerRequest => FolderPurposeDecisionSet.MaxPerRequest;

    /// <summary>自适应步长的起点。真值在判定域里。</summary>
    public static int AdaptiveStart => FolderPurposeDecisionSet.AdaptiveStart;

    /// <summary>步长下限。真值在 <see cref="DecisionRunner"/> 里。</summary>
    public const int AdaptiveMin = DecisionRunner.AdaptiveMin;

    /// <param name="stillCurrent">
    /// 每写回一批之前问一次：扫描有没有换代、这一页还在不在。返回 false 就停止写入
    /// —— 旧结果绝不许贴到新扫描上。为 null 表示调用方自己保证。
    /// </param>
    public static async Task<AiPurposeBatchOutcome> RunAsync(
        IReadOnlyList<OrganizeNode> nodes,
        Func<bool>? stillCurrent,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        // 入选判据在节点自己身上（OrganizeNode.NeedsPurposeClassification），离线可测
        var targets = nodes.Where(n => n.NeedsPurposeClassification).ToList();
        if (targets.Count == 0)
            return new AiPurposeBatchOutcome(0, 0, 0, 0, 0, 0, 0);

        var provider = App.Settings.DecisionProvider();
        string model = App.Settings.DecisionModelId();
        if (provider == null || string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiPurposeBatchNotConfigured);

        var outcome = await DecisionRunner.RunAsync(
            new FolderPurposeDecisionSet(), targets, provider, model, stillCurrent, progress, ct);

        return new AiPurposeBatchOutcome(
            outcome.Asked, outcome.Applied, outcome.Unsure, outcome.Unknown,
            outcome.Calls, outcome.InputTokens, outcome.Cost);
    }
}
