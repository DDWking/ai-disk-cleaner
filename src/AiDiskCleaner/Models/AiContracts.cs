namespace AiDiskCleaner.Models;

/// <summary>
/// 中转协议。决定往哪个 endpoint 发、payload 长什么样。
///
/// <see cref="Decisions"/> 是结构化判定通道（TypeSafe Jev 这类），
/// 请求体是 <c>state</c> + <c>questions</c>、响应是类型化答案，**和 chat 完全不兼容**，
/// 所以它既不能走 <c>/chat/completions</c>，也不能套用 <c>/v1</c> 的 URL 约定。
/// </summary>
public enum AiProtocol { Completions, Responses, Anthropic, Decisions }

/// <summary>协议 id 的判定。放在 Models 里是为了让<b>不带副作用</b>的地方
/// （比如读设置）也能问一句「这条是不是判定协议」，而不必去碰 <c>AiClient</c> 的静态构造。</summary>
public static class AiProtocols
{
    /// <summary>是不是结构化判定协议（TypeSafe Jev / System One 这类）。</summary>
    public static bool IsDecision(string? id) => id is "decisions" or "systemone";
}

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
