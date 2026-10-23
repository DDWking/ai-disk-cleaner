namespace AiDiskCleaner.Services;

public enum AiSetupPreset
{
    OpenRouterJev,
    SystemOne,
    JevCompatibleGateway,
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
/// Jev 接入向导的纯业务部分：模板、能力请求和用户可读诊断。
/// 不修改设置，也不决定是否启用 AI；窗口层只负责展示和保存。
/// </summary>
public static class AiConfigurationService
{
    public static IReadOnlyList<AiSetupPresetInfo> Presets { get; } = new[]
    {
        new AiSetupPresetInfo(
            AiSetupPreset.OpenRouterJev,
            "OpenRouter · Jev（推荐）",
            "适合直接使用 Jev 判定服务。默认模型支持本软件的结构化问题格式。",
            "https://openrouter.ai/api/alpha",
            "typesafe/jev-1.13",
            "decisions",
            true),
        new AiSetupPresetInfo(
            AiSetupPreset.SystemOne,
            "TypeSafe System One API",
            "适合官方 System One API 或兼容 /v1/systemone 的第三方中转站。已有地址不会被覆盖。",
            "https://openrouter.ai/api",
            "jev-latest",
            "systemone",
            true),
        new AiSetupPresetInfo(
            AiSetupPreset.JevCompatibleGateway,
            "自定义 Jev Decisions API",
            "适合公司网关或本地 Jev 服务，接口必须提供 /decisions。",
            "",
            "",
            "decisions",
            false),
    };

    public static AiSetupPresetInfo Preset(AiSetupPreset preset)
        => Presets.First(x => x.Id == preset);

    public static AiDecisionRequest BuildProbe(AiProviderCfg? provider, string model, bool english)
    {
        return new AiDecisionRequest
        {
            Provider = provider,
            Model = model.Trim(),
            State = english
                ? "This is a Jev capability check. The text is only a harmless test payload."
                : "这是 Jev 能力检查，只包含无敏感信息的测试内容。",
            Questions =
            {
                ["ok"] = new AiDecisionQuestion
                {
                    Type = "noul",
                    Instructions = english
                        ? "Does this state explicitly say that this is a capability check?"
                        : "这段状态是否明确说明这是一次能力检查？",
                },
            },
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public static void ValidateProbe(AiDecisionReply reply, bool english)
    {
        if (reply.Answers is null || !reply.Answers.TryGetValue("ok", out var answer))
            throw new DecisionLimitException(english
                ? "The service returned no answer for the Jev probe."
                : "服务已响应，但没有返回 Jev 测试问题的答案。");
        if (answer.Noul is null || double.IsNaN(answer.Noul.Value)
            || answer.Noul.Value < 0 || answer.Noul.Value > 1)
            throw new DecisionLimitException(english
                ? "The service returned an invalid structured answer."
                : "服务返回的结构化答案不是合法的 0 到 1 概率。");
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

        var kind = AppError.Classify(ex) switch
        {
            AppErrorKind.Auth => AiDiagnosticKind.Authentication,
            AppErrorKind.Network => AiDiagnosticKind.Network,
            AppErrorKind.Parse => AiDiagnosticKind.Protocol,
            _ when ex is TimeoutException or TaskCanceledException => AiDiagnosticKind.Timeout,
            _ => AiDiagnosticKind.Unknown,
        };
        return kind switch
        {
            AiDiagnosticKind.Authentication => Result(kind, english ? "The API key was rejected." : "接口拒绝了这把密钥。", english ? "Check the key, account quota, and service permissions." : "检查密钥、账户额度和服务权限。", elapsedMs),
            AiDiagnosticKind.Network => Result(kind, english ? "Could not reach the service." : "无法连接服务。", english ? "Check the address or network." : "检查接口地址或网络。", elapsedMs),
            AiDiagnosticKind.Protocol => Result(kind, english ? "The service replied, but not in Jev format." : "服务已响应，但不是 Jev 结构化格式。", english ? "Use a Jev-compatible /decisions endpoint, not a normal chat endpoint." : "请使用兼容 /decisions 的 Jev 服务，不要填写普通聊天接口。", elapsedMs),
            AiDiagnosticKind.Timeout => Result(kind, english ? "The Jev capability check timed out." : "Jev 能力检查超时。", english ? "Check the service load and address. If it always times out, plain DNS may be tampered with — turn on your proxy or encrypted DNS (DoH)." : "检查服务负载和接口地址。如果一直超时，可能是明文 DNS 被投毒：请开启代理，或启用系统加密 DNS（DoH）。", elapsedMs),
            _ => Result(kind, english ? "The Jev capability check failed." : "Jev 能力检查失败。", english ? "Open technical details or verify the endpoint configuration." : "查看技术详情，或重新检查接口配置。", elapsedMs),
        };
    }

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
            english ? "Choose a template or enter a Jev-compatible endpoint." : "选择服务模板，或填写兼容 Jev 的接口地址。", 0);

    private static AiDiagnosticResult Result(AiDiagnosticKind kind, string summary, string next, long elapsed)
        => new(kind, summary, next, null, elapsed, 0);
}
