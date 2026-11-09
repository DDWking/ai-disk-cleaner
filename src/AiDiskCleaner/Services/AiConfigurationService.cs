using AiDiskCleaner.Services.Agent;

namespace AiDiskCleaner.Services;

public enum AiSetupPreset
{
    DeepSeek,
    OpenAi,
    Ollama,
    CustomCompatible,
}

public sealed record AiSetupPresetInfo(
    AiSetupPreset Id,
    string Name,
    string Description,
    string BaseUrl,
    string Model,
    string Protocol,
    bool RequiresApiKey);

public enum AiDiagnosticKind
{
    Success,
    MissingConfiguration,
    Network,
    Authentication,
    Model,
    Protocol,
    /// <summary>证书链不受信任 / 域名解析被污染：连接被中间设备拦截。</summary>
    TlsInterception,
    Timeout,
    Unknown,
}

public sealed record AiDiagnosticResult(
    AiDiagnosticKind Kind,
    string Summary,
    string NextStep,
    string? Model,
    long ElapsedMs,
    double Confidence)
{
    public bool IsSuccess => Kind == AiDiagnosticKind.Success;
}

/// <summary>
/// AI 接入向导的纯业务部分：模板、连通性测试和用户可读诊断。
/// 不修改设置，也不决定是否启用 AI；窗口层只负责展示和保存。
///
/// v3.1 起接入对象从 Jev 判定服务改成普通 OpenAI 兼容聊天接口
/// （DeepSeek / OpenAI / Ollama / 任何兼容中转）。
/// </summary>
public static class AiConfigurationService
{
    public const string ChatProtocol = "chat";

    public static IReadOnlyList<AiSetupPresetInfo> Presets { get; } = new[]
    {
        new AiSetupPresetInfo(
            AiSetupPreset.DeepSeek,
            "DeepSeek（推荐）",
            "官方接口，地址填到 /v1 即可。适合中文清理报告。",
            "https://api.deepseek.com/v1",
            "deepseek-chat",
            ChatProtocol,
            true),
        new AiSetupPresetInfo(
            AiSetupPreset.OpenAi,
            "OpenAI 官方",
            "官方接口，地址到 /v1。模型可按账户可用性改成 gpt-4o-mini 等。",
            "https://api.openai.com/v1",
            "gpt-4o-mini",
            ChatProtocol,
            true),
        new AiSetupPresetInfo(
            AiSetupPreset.Ollama,
            "Ollama（本地）",
            "本机模型，不需要密钥。也要先把 Ollama 跑起来。",
            "http://localhost:11434/v1",
            "qwen2.5:7b",
            ChatProtocol,
            false),
        new AiSetupPresetInfo(
            AiSetupPreset.CustomCompatible,
            "自定义 OpenAI 兼容接口",
            "任何兼容 /chat/completions 的中转、公司网关或私有服务。",
            "",
            "",
            ChatProtocol,
            false),
    };

    public static AiSetupPresetInfo Preset(AiSetupPreset preset)
        => Presets.First(x => x.Id == preset);

    /// <summary>发一次最小聊天请求，只验证地址、密钥和模型能不能用。</summary>
    public static ChatRequest BuildProbe(string? baseUrl, string? model, bool english)
    {
        return new ChatRequest
        {
            Model = (model ?? "").Trim(),
            Messages = new[]
            {
                new ChatMessage(ChatRole.System, english
                    ? "You are a connectivity probe. Reply with exactly: OK"
                    : "你是连通性测试。只回复两个字：可以"),
                new ChatMessage(ChatRole.User, english ? "ping" : "在吗"),
            },
            Temperature = 0,
            // 推理模型（DeepSeek V4 Flash 一类）会先花 token 思考。24 个 token
            // 会被思考全部吃光，正文是空的 —— 探针会误报成「协议不对」。
            // 给足余量，并且下面允许「只有 reasoning_content」也算连通。
            MaxTokens = 64,
            JsonMode = false,
        };
    }

    public static void ValidateProbe(ChatResponse reply, bool english)
    {
        // 有正文最好；只有思考也算连通（接口 / 密钥 / 模型名都已被接受）。
        if (!string.IsNullOrWhiteSpace(reply.Content)) return;
        if (!string.IsNullOrWhiteSpace(reply.Reasoning)) return;
        throw new ChatModelException(
            english ? "The service replied without any content." : "服务已响应，但没有返回任何内容。",
            ChatErrorKind.Protocol,
            retryable: true);
    }

    public static AiDiagnosticResult Diagnose(Exception ex, long elapsedMs, bool english)
    {
        // 证书失败必须排在 Network / Timeout 之前。被污染的 DNS 会让连接
        // 「连得上但证书不对」，或者干脆挂到超时 —— 这两种表现都不能报成普通网络问题，
        // 否则用户会一直去换密钥和模型，而真正的毛病在 DNS。
        if (IsCertificateFailure(ex))
        {
            return Result(AiDiagnosticKind.TlsInterception,
                english ? "The connection was intercepted (untrusted certificate)." : "连接被拦截，证书不受信任。",
                english
                    ? "Plain DNS is being tampered with. Turn on your proxy or encrypted DNS (DoH); switching to another plain DNS server does not help."
                    : "明文 DNS 正在被投毒。请开启代理，或启用系统加密 DNS（DoH）。换一台普通 DNS 服务器没有用。",
                elapsedMs);
        }

        if (ex is ChatModelException { Kind: var chatKind })
        {
            return chatKind switch
            {
                ChatErrorKind.Auth => Result(AiDiagnosticKind.Authentication,
                    english ? "The API key was rejected." : "接口拒绝了这把密钥。",
                    english ? "Check the key, account quota, and service permissions." : "检查密钥、账户额度和服务权限。",
                    elapsedMs),
                ChatErrorKind.Network => Result(AiDiagnosticKind.Network,
                    english ? "Could not reach the service." : "无法连接服务。",
                    english ? "Check the address or network." : "检查接口地址或网络。",
                    elapsedMs),
                ChatErrorKind.Timeout => Result(AiDiagnosticKind.Timeout,
                    english ? "The connectivity test timed out." : "连接测试超时。",
                    english ? "Check the service load and address. If it always times out, plain DNS may be tampered with — turn on your proxy or encrypted DNS (DoH)." : "检查服务负载和接口地址。如果一直超时，可能是明文 DNS 被投毒：请开启代理，或启用系统加密 DNS（DoH）。",
                    elapsedMs),
                ChatErrorKind.Protocol when LooksLikeModelName(ex.Message) => Result(AiDiagnosticKind.Model,
                    english ? "The service does not recognize this model name." : "服务不认识这个模型名。",
                    english
                        ? "Fetch the model list and pick one. For DeepSeek official, deepseek-chat is the chat model; deepseek-flash is a reasoning model."
                        : "点「获取模型」从上游拉列表再选。DeepSeek 官方：deepseek-chat 是对话模型，deepseek-flash 是推理模型。",
                    elapsedMs),
                ChatErrorKind.Protocol when LooksLikeEmptyReply(ex.Message) => Result(AiDiagnosticKind.Model,
                    english ? "The model is reachable, but returned no text." : "模型已连通，但没有返回正文。",
                    english
                        ? "A reasoning model may have spent the whole probe on thinking. Switch to a chat model such as deepseek-chat, or just test again."
                        : "推理模型可能把探测额度用在思考上了。换成 deepseek-chat 这类对话模型再测，或直接重试。",
                    elapsedMs),
                ChatErrorKind.Protocol => Result(AiDiagnosticKind.Protocol,
                    english ? "The service replied, but not like an OpenAI-compatible chat API." : "服务已响应，但不像 OpenAI 兼容聊天接口。",
                    english ? "Use a /chat/completions endpoint (usually ends with /v1)." : "请填写兼容 /chat/completions 的地址（通常以 /v1 结尾）。",
                    elapsedMs),
                _ => Unknown(ex, elapsedMs, english),
            };
        }

        return AppError.Classify(ex) switch
        {
            AppErrorKind.Auth => Result(AiDiagnosticKind.Authentication, english ? "The API key was rejected." : "接口拒绝了这把密钥。", english ? "Check the key, account quota, and service permissions." : "检查密钥、账户额度和服务权限。", elapsedMs),
            AppErrorKind.Network => Result(AiDiagnosticKind.Network, english ? "Could not reach the service." : "无法连接服务。", english ? "Check the address or network." : "检查接口地址或网络。", elapsedMs),
            AppErrorKind.Parse => Result(AiDiagnosticKind.Protocol, english ? "The service replied in an unexpected format." : "服务返回的格式不对。", english ? "Use a /chat/completions endpoint (usually ends with /v1)." : "请填写兼容 /chat/completions 的地址（通常以 /v1 结尾）。", elapsedMs),
            _ when ex is TimeoutException or TaskCanceledException => Result(AiDiagnosticKind.Timeout, english ? "The connectivity test timed out." : "连接测试超时。", english ? "Check the service load and address. If it always times out, plain DNS may be tampered with — turn on your proxy or encrypted DNS (DoH)." : "检查服务负载和接口地址。如果一直超时，可能是明文 DNS 被投毒：请开启代理，或启用系统加密 DNS（DoH）。", elapsedMs),
            _ => Unknown(ex, elapsedMs, english),
        };
    }

    private static bool LooksLikeModelName(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        return message.Contains("模型不存在", StringComparison.OrdinalIgnoreCase)
            || message.Contains("模型名", StringComparison.OrdinalIgnoreCase)
            || message.Contains("model not exist", StringComparison.OrdinalIgnoreCase)
            || message.Contains("model_not_found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid model", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unknown model", StringComparison.OrdinalIgnoreCase)
            || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeEmptyReply(string? message)
        => !string.IsNullOrWhiteSpace(message)
           && (message.Contains("空内容", StringComparison.Ordinal)
               || message.Contains("没有返回", StringComparison.Ordinal)
               || message.Contains("empty content", StringComparison.OrdinalIgnoreCase));

    private static AiDiagnosticResult Unknown(Exception ex, long elapsedMs, bool english)
        => Result(AiDiagnosticKind.Unknown,
            english ? "The connectivity test failed." : "连接测试失败。",
            english ? "Open technical details or verify the endpoint configuration." : "查看技术详情，或重新检查接口配置。",
            elapsedMs);

    /// <summary>异常链里有没有 TLS 证书校验失败（<c>SEC_E_UNTRUSTED_ROOT</c> 一类）。</summary>
    public static bool IsCertificateFailure(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is System.Security.Authentication.AuthenticationException) return true;
        }
        return false;
    }

    public static AiDiagnosticResult Missing(bool english)
        => Result(AiDiagnosticKind.MissingConfiguration,
            english ? "Enter an endpoint and model first." : "请先填写接口地址和模型。",
            english ? "Choose a template or enter an OpenAI-compatible endpoint." : "选择服务模板，或填写兼容 OpenAI 的接口地址。", 0);

    private static AiDiagnosticResult Result(AiDiagnosticKind kind, string summary, string next, long elapsed)
        => new(kind, summary, next, null, elapsed, 0);
}
