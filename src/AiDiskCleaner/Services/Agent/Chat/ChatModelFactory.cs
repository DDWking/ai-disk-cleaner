using System.Net.Http;

namespace AiDiskCleaner.Services.Agent;

/// <summary>
/// 从「用户填的地址/模型/密钥」造一个聊天客户端。
///
/// 地址规则只有一条：以 <c>/chat/completions</c> 结尾就原样用，否则补上。
/// 因此下面三种写法都能工作：
///   https://api.deepseek.com                → …/chat/completions
///   https://api.deepseek.com/v1             → …/v1/chat/completions
///   https://api.openai.com/v1/chat/completions → 原样
///
/// 不传 <paramref name="http"/> 时复用进程内共享的 <see cref="AiHttp.Client"/>
/// （带 DoH 兜底与统一的 TLS 策略）。
/// </summary>
public static class ChatModelFactory
{
    public static string NormalizeEndpoint(string? baseUrl)
    {
        string url = (baseUrl ?? "").Trim().TrimEnd('/');
        if (url.Length == 0) return "";
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return url;
        return url + "/chat/completions";
    }

    public static IChatModel Create(string? baseUrl, string? model, string? apiKey, HttpClient? http = null)
    {
        string url = NormalizeEndpoint(baseUrl);
        string name = (model ?? "").Trim();
        if (url.Length == 0 || name.Length == 0) throw new InvalidOperationException(Loc.AiNeedConfig);
        return new OpenAiCompatChatModel(http ?? AiHttp.Client, url, name, apiKey ?? "");
    }
}
