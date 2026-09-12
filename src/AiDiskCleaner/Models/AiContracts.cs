namespace AiDiskCleaner.Models;

/// <summary>中转协议。决定往哪个 endpoint 发、payload 长什么样。</summary>
public enum AiProtocol { Completions, Responses, Anthropic }

/// <summary>模型要求调用的一次工具。</summary>
public sealed class AiToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "{}";
}

/// <summary>一轮对话消息。Role 是 "system" / "user" / "assistant" / "tool"。</summary>
public sealed class AiMsg
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public List<AiToolCall>? Calls { get; set; }
    public string? CallId { get; set; }
    public string? ToolName { get; set; }
}

/// <summary>模型的一次回复：文本 + 可能的工具调用。</summary>
public sealed class AiReply
{
    public string Text { get; set; } = "";
    public List<AiToolCall> Calls { get; set; } = new();
    public bool HasTools => Calls.Count > 0;
}
