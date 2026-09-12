using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 一次「让 AI 解释这些是什么」的结果。
///
/// **请求成功 ≠ 结果有用**：<see cref="Sent"/> 是提交条数，<see cref="Applied"/> 才是有几条
/// 真正拿到了可用说明。两者必须分开呈现，绝不能把「发送了 60 条」说成「分析了 60 条」。
/// </summary>
public sealed record AiExplainResult(
    int Sent,
    int Applied,
    string Text,
    AiNoteParser.AiParseStats Parse,
    double BuildMs,
    double SendMs,
    double ParseMs,
    string Channel,
    int Attempts)
{
    public bool AnyApplied => Applied > 0;

    /// <summary>提交了但没拿到可用说明的条数。</summary>
    public int WithoutNote => Math.Max(0, Sent - Applied);

    /// <summary>只覆盖了部分候选（送出数 < 可分析总数）。</summary>
    public bool PartialCoverage { get; init; }

    /// <summary>被单批上限截掉的条数。</summary>
    public int SkippedByLimit { get; init; }

    /// <summary>模型有回内容，但一条可用的说明都没解析出来。</summary>
    public bool Unusable => Applied == 0 && !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// AI 协调：组装批次 → 脱敏 → 发请求 → 强校验解析 → 写回 AiNote。
///
/// 三条铁律在这里落地：
/// 1) 默认只发脱敏路径，完整路径要用户明确允许；
/// 2) AI 只写说明，**不碰** Risk / Selected / CanDelete；
/// 3) 解析失败就保留本地规则结果，不抛给界面。
///
/// 通道细节（sidecar / HTTP / fallback）由 <see cref="AiGateway"/> 负责，这里不管。
/// </summary>
public sealed class AiCoordinator
{
    /// <summary>单次最多送多少条。</summary>
    public const int MaxBatch = AiNoteParser.MaxItems;

    /// <summary>
    /// 解释一批条目。返回实际写进去几条。
    /// onDelta 收流式文本（可为空），流式内容同样受长度上限约束。
    /// </summary>
    public async Task<AiExplainResult> ExplainAsync(
        IReadOnlyList<CleanItem> items,
        AiProviderCfg? provider,
        string? model,
        bool sendFullPaths,
        Action<string>? onDelta,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return new AiExplainResult(0, 0, "", default, 0, 0, 0, "", 0);

        var swBuild = System.Diagnostics.Stopwatch.StartNew();
        var batch = items.OrderByDescending(x => x.Size).Take(MaxBatch).ToList();

        // 默认发脱敏路径；用户明确打开才发真实路径
        Func<string, string> pathKey = sendFullPaths
            ? static p => p
            : PathRedactor.Redact;

        var lines = batch.Select(x =>
            $"- {pathKey(x.FullPath)} ({(x.IsDirectory ? Loc.Folder : Loc.FilesCol)}, {x.SizeText})");
        string user = Loc.AiCatListHeader + Environment.NewLine + string.Join(Environment.NewLine, lines);

        var turns = new List<AiMsg> { new() { Role = "user", Text = user } };
        swBuild.Stop();

        var buf = new System.Text.StringBuilder();
        void Push(string delta)
        {
            // 流式也要封顶：模型跑飞时不至于把内存吃光
            if (buf.Length < AiNoteParser.MaxResponseChars) buf.Append(delta);
            onDelta?.Invoke(delta);
        }

        var swSend = System.Diagnostics.Stopwatch.StartNew();
        var reply = await AiClient.StreamAsync(provider, model, Loc.AiCatSystem, turns, Push, ct);
        swSend.Stop();
        // 通道/尝试次数取自网关刚记录的状态，不额外改 AiReply 的形状
        var ch = AiGateway.LastStatus;
        if (ct.IsCancellationRequested)
            return new AiExplainResult(batch.Count, 0, "", default,
                swBuild.Elapsed.TotalMilliseconds, swSend.Elapsed.TotalMilliseconds, 0,
                ch.Channel, ch.Attempts);

        string text = string.IsNullOrWhiteSpace(reply.Text) ? buf.ToString() : reply.Text;

        var swParse = System.Diagnostics.Stopwatch.StartNew();
        // 发出去的是脱敏路径，模型回的自然也是脱敏路径 —— 用同一套规则映射回条目
        var stats = AiNoteParser.ApplyWithStats(batch, text, sendFullPaths ? null : PathRedactor.Redact);
        swParse.Stop();

        return new AiExplainResult(
            batch.Count, stats.Applied, text, stats,
            swBuild.Elapsed.TotalMilliseconds, swSend.Elapsed.TotalMilliseconds, swParse.Elapsed.TotalMilliseconds,
            ch.Channel, ch.Attempts)
        {
            PartialCoverage = items.Count > batch.Count,
            SkippedByLimit = Math.Max(0, items.Count - batch.Count),
        };
    }

    /// <summary>
    /// 拼写给模型的那段清单（单独抽出来是为了能在测试里断言「有没有泄漏真实路径」）。
    /// </summary>
    public static string BuildPrompt(IReadOnlyList<CleanItem> batch, bool sendFullPaths)
    {
        var lines = batch.Select(x =>
            $"- {(sendFullPaths ? x.FullPath : PathRedactor.Redact(x.FullPath))} " +
            $"({(x.IsDirectory ? Loc.Folder : Loc.FilesCol)}, {x.SizeText})");
        return Loc.AiCatListHeader + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}
