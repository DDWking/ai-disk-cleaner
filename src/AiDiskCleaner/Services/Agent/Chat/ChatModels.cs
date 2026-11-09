namespace AiDiskCleaner.Services.Agent;

/// <summary>聊天消息角色。这里的通道只用于「一次性结构化输出」，不做多轮闲谈。</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
}

public sealed record ChatMessage(ChatRole Role, string Content)
{
    public string RoleName => Role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        _ => "assistant",
    };
}

/// <summary>一次补全请求。默认要求 JSON 输出、流式返回。</summary>
public sealed class ChatRequest
{
    public required string Model { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public double Temperature { get; init; } = 0.2;
    public int MaxTokens { get; init; } = 2048;
    public bool JsonMode { get; init; } = true;
}

public readonly record struct ChatUsage(int InputTokens, int OutputTokens)
{
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>一次补全的最终结果（流式增量在内部拼好，调用方只拿完整文本）。</summary>
public sealed class ChatResponse
{
    public string Content { get; set; } = "";
    public string Model { get; set; } = "";
    public string FinishReason { get; set; } = "";
    public ChatUsage Usage { get; set; }
    public bool Streamed { get; set; }
}

/// <summary>聊天模型的最小接口。离线检查用脚本化实现替换真实网络。</summary>
public interface IChatModel
{
    /// <summary>模型 id（写进请求体，也用于报告 provenance）。</summary>
    string ModelId { get; }

    /// <summary>端点 + 模型的可读描述（不含密钥），用于日志与报告 provenance。</summary>
    string Describe();

    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct);
}

public enum ChatErrorKind
{
    Unknown,
    Auth,
    Network,
    Protocol,
    Timeout,
}

public sealed class ChatModelException : Exception
{
    public ChatModelException(string message, ChatErrorKind kind = ChatErrorKind.Unknown, bool retryable = false, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        Retryable = retryable;
    }

    public ChatErrorKind Kind { get; }
    public bool Retryable { get; }
}
