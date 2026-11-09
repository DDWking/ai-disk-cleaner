namespace AiDiskCleaner.Services.Agent;

/// <summary>模型给出的结论分类（03 §6.2 的四个枚举）。</summary>
public enum ReportVerdict
{
    Recommend,
    Consider,
    Keep,
    Unknown,
}

/// <summary>报告里一条可点开的证据。ref 是模型提交的原始引用，label 是本地回填的可读文本。</summary>
public sealed class ReportEvidence
{
    public string Ref { get; set; } = "";
    public string Label { get; set; } = "";
    public string Target { get; set; } = "";
}

/// <summary>报告里的一条建议。所有数字都来自本地证据卡，模型只负责引用与解释。</summary>
public sealed class ReportItem
{
    public string FolderId { get; set; } = "";
    public string Title { get; set; } = "";
    public string RelPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public long ReclaimableBytes { get; set; }
    public ReportVerdict Verdict { get; set; }
    public double Confidence { get; set; }
    public string Why { get; set; } = "";
    public string Risk { get; set; } = "";
    public List<ReportEvidence> Evidence { get; set; } = new();

    /// <summary>UI 动作：select = 可加入清理；keep = 保持不动；ask = 需要用户判断。</summary>
    public string Action => Verdict switch
    {
        ReportVerdict.Recommend => "select",
        ReportVerdict.Keep => "keep",
        _ => "ask",
    };
}

public sealed class ReportGroup
{
    public string Group { get; set; } = "";
    public long TotalBytes { get; set; }
    public List<ReportItem> Items { get; set; } = new();
}

public sealed record ReportQuestion(string Text, string? SuggestedTool);

/// <summary>UI 直接消费的报告卡（03 §7）。</summary>
public sealed class AgentReport
{
    public string ReportId { get; set; } = "";
    public string ScanId { get; set; } = "";
    public string Drive { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public string Summary { get; set; } = "";
    public long TotalReclaimableBytes { get; set; }
    public List<ReportGroup> Groups { get; set; } = new();
    public List<ReportQuestion> Questions { get; set; } = new();
    public string? Degraded { get; set; }
    public string Model { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public int DroppedItems { get; set; }
    public List<string> Warnings { get; set; } = new();

    public IEnumerable<ReportItem> AllItems => Groups.SelectMany(g => g.Items);
}
