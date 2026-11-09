namespace AiDiskCleaner.Services.Agent;

public sealed record AgentReportProgress(string Phase, int Completed, int Total, string? Message = null);

public sealed class AgentReportOptions
{
    /// <summary>每批发多少张证据卡（04 §3：25 张/批，约 6–8k token）。</summary>
    public int CardsPerBatch { get; init; } = 25;

    /// <summary>最多几批（L1 默认 4 批 = 100 张卡）。</summary>
    public int MaxBatches { get; init; } = 4;

    public int MaxCandidates { get; init; } = 100;

    /// <summary>输入 token 预算；超过后停止后续批次，用已有结果出报告。</summary>
    public int MaxInputTokens { get; init; } = 50_000;

    public int MaxOutputTokens { get; init; } = 4096;

    public double Temperature { get; init; } = 0.2;

    public string PromptVersion { get; init; } = PromptBuilder.Version;

    public CandidateOptions Candidates { get; init; } = new();

    /// <summary>测试注入固定时钟；生产为 null（取当前时间）。</summary>
    public DateTimeOffset? Now { get; init; }
}

public sealed class AgentReportResult
{
    public AgentReport? Report { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public ChatErrorKind ErrorKind { get; set; }
    public int Batches { get; set; }
    public int Calls { get; set; }
    public ChatUsage Usage { get; set; }
    public List<string> Warnings { get; } = new();
    public List<string> RawOutputs { get; } = new();
}

/// <summary>
/// L1「单发报告」编排：候选筛选 → 证据卡 → 分批调用模型 → 本地校验 → 报告卡。
///
/// 这里没有任何写操作：不删文件、不勾选、不碰 UI 状态。失败时返回
/// <see cref="AgentReportResult.Ok"/> = false 与可读错误，由调用方决定怎么提示。
/// </summary>
public sealed class AgentReportService
{
    readonly IChatModel _model;
    readonly AgentReportOptions _options;

    public AgentReportService(IChatModel model, AgentReportOptions? options = null)
    {
        _model = model;
        _options = options ?? new AgentReportOptions();
    }

    public async Task<AgentReportResult> AnalyzeAsync(
        ScanSession session,
        IProgress<AgentReportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new AgentReportResult();
        var index = session.Index;

        progress?.Report(new AgentReportProgress("selecting", 0, 1));
        var candidates = CandidateSelector.Select(session, index, _options.Candidates);
        if (candidates.Items.Count == 0)
        {
            result.Ok = true;
            result.Report = ReportAssembler.Build(
                session,
                Array.Empty<ReportItem>(),
                "",
                Array.Empty<ReportQuestion>(),
                _model.Describe(),
                _options.PromptVersion,
                _options.Now ?? DateTimeOffset.Now,
                0,
                Array.Empty<string>(),
                null);
            progress?.Report(new AgentReportProgress("done", 1, 1));
            return result;
        }

        progress?.Report(new AgentReportProgress("packing", 0, candidates.Items.Count));
        int cardLimit = Math.Min(_options.MaxBatches * _options.CardsPerBatch, _options.MaxCandidates);
        var cards = new List<EvidenceCard>();
        foreach (var candidate in candidates.Items)
        {
            if (cards.Count >= cardLimit) break;
            if (!index.TryGet(candidate.FolderId, out var directory)) continue;
            cards.Add(EvidenceCardBuilder.Build(session, index, directory));
        }

        if (candidates.Truncated || candidates.Items.Count > cardLimit)
            result.Warnings.Add($"候选目录较多，只分析前 {cards.Count} 个");

        var batches = new List<List<EvidenceCard>>();
        for (int i = 0; i < cards.Count; i += _options.CardsPerBatch)
            batches.Add(cards.GetRange(i, Math.Min(_options.CardsPerBatch, cards.Count - i)));

        if (batches.Count > _options.MaxBatches)
            batches = batches.Take(_options.MaxBatches).ToList();

        var allItems = new List<ReportItem>();
        var questions = new List<ReportQuestion>();
        string summary = "";
        string? degraded = null;
        int dropped = 0;
        int succeeded = 0;

        for (int b = 0; b < batches.Count; b++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new AgentReportProgress("calling", b, batches.Count, $"第 {b + 1}/{batches.Count} 批"));

            var messages = new[]
            {
                new ChatMessage(ChatRole.System, PromptBuilder.SystemPrompt),
                new ChatMessage(ChatRole.User, PromptBuilder.BuildUserPrompt(session, batches[b], b + 1, batches.Count)),
            };
            var request = new ChatRequest
            {
                Model = _model.ModelId,
                Messages = messages,
                Temperature = _options.Temperature,
                MaxTokens = _options.MaxOutputTokens,
                JsonMode = true,
            };

            ChatResponse response;
            try
            {
                response = await _model.CompleteAsync(request, ct);
            }
            catch (ChatModelException ex)
            {
                result.Calls++;
                result.Warnings.Add($"第 {b + 1} 批调用失败：{ex.Message}");
                degraded ??= ex.Retryable ? "call_failed_retryable" : "call_failed";
                if (succeeded == 0 && b == batches.Count - 1)
                {
                    result.Ok = false;
                    result.Error = ex.Message;
                    result.ErrorKind = ex.Kind;
                    return result;
                }

                continue;
            }

            result.Calls++;
            result.Usage = new ChatUsage(
                result.Usage.InputTokens + response.Usage.InputTokens,
                result.Usage.OutputTokens + response.Usage.OutputTokens);

            // 推理模型（DeepSeek V4 Flash 一类）可能把 max_tokens 全用在思考上：
            // 这批只有 reasoning_content，没有可解析的正文。不能当成「解析失败」，
            // 否则用户只会看到一句含糊的 parse_failed。
            if (string.IsNullOrWhiteSpace(response.Content))
            {
                result.Warnings.Add($"第 {b + 1} 批只回了思考、没有正文；换对话模型（如 deepseek-chat）或调大输出额度。");
                degraded ??= "reasoning_only";
                if (succeeded == 0 && b == batches.Count - 1)
                {
                    result.Ok = false;
                    result.Error = "模型没有返回正文（思考占满了输出额度）。";
                    result.ErrorKind = ChatErrorKind.Protocol;
                    return result;
                }
                continue;
            }

            result.RawOutputs.Add(Truncate(response.Content, 8000));

            var parsed = ReportParser.Parse(response.Content, session, batches[b]);
            if (parsed.FatalError != null)
            {
                result.Warnings.Add($"第 {b + 1} 批无法使用：{parsed.FatalError}");
                degraded ??= "parse_failed";
                continue;
            }

            succeeded++;
            allItems.AddRange(parsed.Items);
            questions.AddRange(parsed.Questions);
            dropped += parsed.Dropped;
            result.Warnings.AddRange(parsed.Warnings);
            if (!string.IsNullOrWhiteSpace(parsed.Summary)) summary = parsed.Summary;
            degraded ??= parsed.Degraded;

            if (result.Usage.InputTokens > _options.MaxInputTokens)
            {
                degraded ??= "budget";
                result.Warnings.Add("输入 token 已超过预算，停止分析剩余批次");
                break;
            }
        }

        progress?.Report(new AgentReportProgress("done", batches.Count, batches.Count));

        if (succeeded == 0)
        {
            result.Ok = false;
            result.Error = result.Warnings.Count > 0 ? result.Warnings[^1] : "模型调用失败";
            return result;
        }

        result.Report = ReportAssembler.Build(
            session,
            allItems,
            summary,
            questions,
            _model.Describe(),
            _options.PromptVersion,
            _options.Now ?? DateTimeOffset.Now,
            dropped,
            result.Warnings,
            degraded);
        result.Batches = batches.Count;
        result.Ok = true;
        return result;
    }

    static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
