using System.Text.RegularExpressions;

namespace AiDiskCleaner.Services;

/// <summary>
/// 脱敏。两件事必须同时成立：
/// 1) 任何写进日志/异常/诊断包的文字都不能带 API Key；
/// 2) 发给外部 AI 的路径默认不带真实用户名与主机名。
///
/// 纯字符串变换，不碰网络也不碰磁盘，方便离线测试。
/// </summary>
public static class LogRedactor
{
    public const string Mask = "<redacted>";

    /// <summary>键名像密钥的字段：apiKey / api_key / Authorization / token / password / secret。</summary>
    static readonly Regex SecretField = new(
        @"(""?\b(?:api[_-]?key|apikey|authorization|auth|token|password|passwd|secret|credential|access[_-]?key|client[_-]?secret)\b""?\s*[:=]\s*""?)([^""\s,;}\)\r\n]{3,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>常见密钥字面量：OpenAI / Anthropic / Google / GitHub / HuggingFace。</summary>
    static readonly Regex SecretLiteral = new(
        @"\b(?:sk-(?:ant-|proj-)?[A-Za-z0-9_\-]{12,}|AIza[A-Za-z0-9_\-]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|hf_[A-Za-z0-9]{20,}|xox[baprs]-[A-Za-z0-9\-]{10,})\b",
        RegexOptions.Compiled);

    static readonly Regex BearerToken = new(
        @"(Bearer\s+)[A-Za-z0-9\-._~+/=]{8,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>查询串里的 key=...（URL 里的密钥）。</summary>
    static readonly Regex QuerySecret = new(
        @"([?&](?:key|api_key|apikey|token|access_token)=)[^&\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>把一段文本里的密钥全部替换掉。null / 空串原样返回。</summary>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        try
        {
            string s = SecretLiteral.Replace(text, Mask);
            s = BearerToken.Replace(s, "$1" + Mask);
            s = SecretField.Replace(s, "$1" + Mask);
            s = QuerySecret.Replace(s, "$1" + Mask);
            return s;
        }
        catch
        {
            // 脱敏本身绝不能让主流程崩，兜底成「什么都不说」
            return Mask;
        }
    }

    /// <summary>是不是看起来像密钥（用于「有没有泄漏」的自检）。已脱敏的占位符不算。</summary>
    public static bool LooksSecret(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        // 先把自己写进去的占位符去掉，否则 "apiKey": "<redacted>" 会被误判成泄漏
        string s = text.Replace(Mask, " ");
        return SecretLiteral.IsMatch(s) || BearerToken.IsMatch(s) || SecretField.IsMatch(s);
    }

    static readonly string[] PathPrefixes =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    };

    /// <summary>
    /// 路径脱敏：把当前用户目录换成 &lt;UserProfile&gt; 之类的占位符，并抹掉主机名。
    /// 规则统一放在 <see cref="PathRedactor"/>，日志和「发给 AI」用的是同一套，
    /// 免得两处口径不一致漏掉一边。
    /// </summary>
    public static string ScrubPath(string? path)
        => PathRedactor.Redact(path);

    /// <summary>ScrubPath 后再 Scrub，用于日志一行到底。</summary>
    public static string ScrubAll(string? text) => Scrub(ScrubPath(text));
}
