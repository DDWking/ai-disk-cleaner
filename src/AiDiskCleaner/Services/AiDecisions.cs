namespace AiDiskCleaner.Services;

/// <summary>
/// 决策请求里的一个问题。三种原语，没有第四种：
/// <list type="bullet">
/// <item><c>choice</c> —— 从 <see cref="Criteria"/> 里选一个（最多 255 项）；</item>
/// <item><c>noul</c> —— 「是不是」，返回 0~1 的概率；</item>
/// <item><c>score</c> —— 在 <see cref="Scale"/> 上打分（2~10 档）。</item>
/// </list>
/// 一次请求里三种可以混用，而且要**一次全问完**：state 只预填一次，
/// 多问一题的边际成本几乎只有 token、没有时间（串行追问会贵十倍）。
/// </summary>
public sealed class AiDecisionQuestion
{
    public string Type { get; set; } = "choice";
    public string Instructions { get; set; } = "";

    /// <summary><c>choice</c> 用：选项键 → 说明。</summary>
    public Dictionary<string, string>? Criteria { get; set; }

    /// <summary><c>score</c> 用：从低到高的档位描述。</summary>
    public List<string>? Scale { get; set; }
}

/// <summary>
/// 一次结构化判定请求。
///
/// 注意它**不是** <see cref="AiRequest"/> 的变体：那个是 chat（system + turns），
/// 这个是「一份 state + 若干道题」。硬凑成一个类型只会让两边都别扭。
/// </summary>
public sealed class AiDecisionRequest
{
    public AiProviderCfg? Provider { get; set; }
    public string? Model { get; set; }

    /// <summary>要评估的内容。这里的实现按「一行一个路径」拼纯文本。</summary>
    public string State { get; set; } = "";

    public Dictionary<string, AiDecisionQuestion> Questions { get; set; } = new();

    /// <summary>整个请求的墙钟上限（含重试）。</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>一道题的答案。</summary>
public sealed class AiDecisionAnswer
{
    public string Type { get; set; } = "";

    /// <summary><c>choice</c> 选中的键。</summary>
    public string? Choice { get; set; }

    /// <summary><c>noul</c> 的概率。<b>它本身就是置信度</b>，不再单独给 confidence。</summary>
    public double? Noul { get; set; }

    /// <summary><c>score</c> 的分值（可落在档位之间）。</summary>
    public double? Score { get; set; }

    public double Confidence { get; set; }

    /// <summary>完整概率分布。分布很平通常说明 criteria 写错了，不是模型困惑。</summary>
    public Dictionary<string, double>? Probabilities { get; set; }
}

/// <summary>一次判定的结果，外加用量（用来算钱和盯预算）。</summary>
public sealed class AiDecisionReply
{
    public Dictionary<string, AiDecisionAnswer> Answers { get; set; } = new();

    /// <summary>服务端回报的模型 id。实测会带日期后缀（<c>jev-1.13-20260917</c>），可用于日志。</summary>
    public string Model { get; set; } = "";

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public double Cost { get; set; }
}
