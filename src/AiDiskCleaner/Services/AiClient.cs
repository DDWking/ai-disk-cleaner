using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AiDiskCleaner.Services;

public enum AiKind { OpenAI, DeepSeek, Anthropic, Gemini, Ollama, Custom }

public static class AiClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    public static (string Url, string Model, AiKind Kind) Preset(AiKind kind) => kind switch
    {
        AiKind.DeepSeek => ("https://api.deepseek.com/v1", "deepseek-chat", kind),
        AiKind.Anthropic => ("https://api.anthropic.com", "claude-3-5-haiku-latest", kind),
        AiKind.Gemini => ("https://generativelanguage.googleapis.com/v1beta", "gemini-2.0-flash", kind),
        AiKind.Ollama => ("http://127.0.0.1:11434", "llama3.2", kind),
        AiKind.Custom => ("", "", kind),
        _ => ("https://api.openai.com/v1", "gpt-4o-mini", AiKind.OpenAI),
    };

    public static async Task<string> ChatAsync(string system, string user, CancellationToken ct)
    {
        var s = App.Settings;
        var kind = ParseKind(s.AiProvider);
        string baseUrl = (s.AiBaseUrl ?? "").Trim().TrimEnd('/');
        string model = (s.AiModel ?? "").Trim();
        string key = s.AiApiKey ?? "";
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiNeedConfig);
        if (kind is not (AiKind.Ollama or AiKind.Custom) && string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(Loc.AiNeedKey);

        return kind switch
        {
            AiKind.Anthropic => await Anthropic(baseUrl, model, key, system, user, ct),
            AiKind.Gemini => await Gemini(baseUrl, model, key, system, user, ct),
            AiKind.Ollama => await Ollama(baseUrl, model, system, user, ct),
            _ => await OpenAi(baseUrl, model, key, system, user, ct),
        };
    }

    public static Task<string> TestAsync(CancellationToken ct)
        => ChatAsync(Loc.AiSystem, Loc.IsEn ? "Reply with one short word: ok" : "只回复一个字：好", ct);

    static AiKind ParseKind(string? name)
        => Enum.TryParse<AiKind>(name, true, out var k) ? k : AiKind.OpenAI;

    static async Task<string> OpenAi(string baseUrl, string model, string key, string system, string user, CancellationToken ct)
    {
        string url = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? baseUrl + "/chat/completions"
            : baseUrl + "/v1/chat/completions";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(key))
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        req.Content = JsonBody(new
        {
            model,
            temperature = 0.2,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
        });
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var msg = choices[0].GetProperty("message");
            if (msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                return c.GetString() ?? "";
        }
        throw Fail(body, "empty");
    }

    static async Task<string> Anthropic(string baseUrl, string model, string key, string system, string user, CancellationToken ct)
    {
        string url = baseUrl.Contains("/v1", StringComparison.OrdinalIgnoreCase)
            ? baseUrl.TrimEnd('/') + "/messages"
            : baseUrl + "/v1/messages";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("x-api-key", key);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        req.Content = JsonBody(new
        {
            model,
            max_tokens = 800,
            system,
            messages = new[] { new { role = "user", content = user } },
        });
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
        {
            var part = content[0];
            if (part.TryGetProperty("text", out var t)) return t.GetString() ?? "";
        }
        throw Fail(body, "empty");
    }

    static async Task<string> Gemini(string baseUrl, string model, string key, string system, string user, CancellationToken ct)
    {
        string root = baseUrl.Contains("/v1", StringComparison.OrdinalIgnoreCase) ? baseUrl : baseUrl + "/v1beta";
        string url = $"{root}/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(key)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = JsonBody(new
        {
            systemInstruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = user } } } },
            generationConfig = new { temperature = 0.2, maxOutputTokens = 800 },
        });
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("candidates", out var cand) && cand.GetArrayLength() > 0)
        {
            var parts = cand[0].GetProperty("content").GetProperty("parts");
            if (parts.GetArrayLength() > 0 && parts[0].TryGetProperty("text", out var t))
                return t.GetString() ?? "";
        }
        throw Fail(body, "empty");
    }

    static async Task<string> Ollama(string baseUrl, string model, string system, string user, CancellationToken ct)
    {
        // native /api/chat; if user pasted an OpenAI-compat /v1 URL, reuse OpenAI path
        if (baseUrl.Contains("/v1", StringComparison.OrdinalIgnoreCase))
            return await OpenAi(baseUrl, model, "", system, user, ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/chat");
        req.Content = JsonBody(new
        {
            model,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
        });
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c))
            return c.GetString() ?? "";
        throw Fail(body, "empty");
    }

    static StringContent JsonBody(object obj)
        => new(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    static InvalidOperationException Fail(string body, string code)
    {
        string msg = code;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m))
                    msg = m.GetString() ?? code;
                else if (err.ValueKind == JsonValueKind.String)
                    msg = err.GetString() ?? code;
            }
            else if (root.TryGetProperty("message", out var m2))
                msg = m2.GetString() ?? code;
        }
        catch { if (!string.IsNullOrWhiteSpace(body) && body.Length < 240) msg = body.Trim(); }
        return new InvalidOperationException(msg);
    }
}
