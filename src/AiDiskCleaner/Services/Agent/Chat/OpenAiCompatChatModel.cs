using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AiDiskCleaner.Services.Agent;

/// <summary>
/// OpenAI 兼容的聊天客户端（DeepSeek 官方 / 中转 / Ollama 都走这一种形状）。
///
/// 只做一件事：把一组消息发出去，拿回完整文本。默认要求 JSON 输出（报告需要可解析的
/// 结构化结果），并用流式读取避免部分中转的缓冲超时；两种协商失败都会自动降级
/// （去掉 response_format / 改非流式）后重试一次，仍失败才抛错。
///
/// 密钥只出现在请求头里，Describe() 与异常信息都不包含它。
/// </summary>
public sealed class OpenAiCompatChatModel : IChatModel
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly HttpClient _http;
    readonly string _endpoint;
    readonly string _model;
    readonly string _apiKey;

    public OpenAiCompatChatModel(HttpClient http, string endpoint, string model, string apiKey)
    {
        _http = http;
        _endpoint = endpoint;
        _model = model;
        _apiKey = apiKey ?? "";
    }

    public string ModelId => _model;

    public string Describe() => $"{_endpoint} · {_model}";

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        bool json = request.JsonMode;
        bool stream = true;

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await SendAsync(request, json, stream, ct);
            }
            catch (ChatModelException ex) when (ex.Kind == ChatErrorKind.Protocol && attempt < 2)
            {
                if (json && Mentions(ex.Message, "response_format"))
                {
                    json = false;
                    continue;
                }

                if (stream && Mentions(ex.Message, "stream"))
                {
                    stream = false;
                    continue;
                }

                throw;
            }
        }
    }

    static bool Mentions(string text, string token)
        => text.Contains(token, StringComparison.OrdinalIgnoreCase);

    async Task<ChatResponse> SendAsync(ChatRequest request, bool jsonMode, bool stream, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["messages"] = request.Messages
                .Select(m => new Dictionary<string, object?> { ["role"] = m.RoleName, ["content"] = m.Content })
                .ToArray(),
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = stream,
        };
        if (stream) payload["stream_options"] = new Dictionary<string, object?> { ["include_usage"] = true };
        if (jsonMode) payload["response_format"] = new Dictionary<string, object?> { ["type"] = "json_object" };

        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChatModelException("模型请求超时", ChatErrorKind.Timeout, retryable: true);
        }
        catch (HttpRequestException ex)
        {
            throw new ChatModelException("网络错误：" + ex.Message, ChatErrorKind.Network, retryable: true, ex);
        }

        using (res)
        {
            if (!res.IsSuccessStatusCode)
            {
                string body = await res.Content.ReadAsStringAsync(ct);
                throw MapError(res.StatusCode, body);
            }

            string mediaType = res.Content.Headers.ContentType?.MediaType ?? "";
            if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
                return await ParseBodyAsync(res, ct);
            return await ReadSseAsync(res, ct);
        }
    }

    static ChatModelException MapError(HttpStatusCode status, string body)
    {
        string detail = ExtractError(body);
        string message = (int)status switch
        {
            401 or 403 => "密钥无效或没有权限（HTTP " + (int)status + "）" + Suffix(detail),
            404 => "接口地址或模型不存在（HTTP 404）" + Suffix(detail),
            429 => "请求太频繁（HTTP 429）" + Suffix(detail),
            >= 500 => "服务端错误（HTTP " + (int)status + "）" + Suffix(detail),
            _ => "接口拒绝请求（HTTP " + (int)status + "）" + Suffix(detail),
        };

        var kind = (int)status switch
        {
            401 or 403 => ChatErrorKind.Auth,
            404 => ChatErrorKind.Protocol,
            >= 500 => ChatErrorKind.Protocol,
            _ => ChatErrorKind.Protocol,
        };
        bool retryable = (int)status is 429 or >= 500;
        return new ChatModelException(message, kind, retryable);
    }

    static string Suffix(string detail) => string.IsNullOrWhiteSpace(detail) ? "" : "：" + detail;

    static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m))
                    return m.GetString() ?? "";
                if (err.ValueKind == JsonValueKind.String) return err.GetString() ?? "";
            }

            if (root.TryGetProperty("message", out var pm)) return pm.GetString() ?? "";
        }
        catch (JsonException)
        {
            // 不是 JSON 就原样截一段，便于用户判断是不是打到了错误的路由。
        }

        return body.Length <= 160 ? body.Trim() : body[..160].Trim();
    }

    static async Task<ChatResponse> ParseBodyAsync(HttpResponseMessage res, CancellationToken ct)
    {
        string body = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out _)) throw new ChatModelException(ExtractError(body), ChatErrorKind.Protocol);

        var response = new ChatResponse { Model = Str(root, "model") };
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                response.Content = content.GetString() ?? "";
            response.FinishReason = Str(first, "finish_reason");
        }

        response.Usage = ParseUsage(root);
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new ChatModelException("模型返回了空内容", ChatErrorKind.Protocol, retryable: true);
        return response;
    }

    static async Task<ChatResponse> ReadSseAsync(HttpResponseMessage res, CancellationToken ct)
    {
        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        var builder = new StringBuilder();
        var response = new ChatResponse { Model = "", Streamed = true };

        while (true)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            line = line.Trim();
            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            string data = line[5..].Trim();
            if (data == "[DONE]") break;
            AppendChunk(data, response, builder);
        }

        response.Content = builder.ToString();
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new ChatModelException("模型返回了空内容", ChatErrorKind.Protocol, retryable: true);
        return response;
    }

    static void AppendChunk(string data, ChatResponse response, StringBuilder builder)
    {
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out _))
            throw new ChatModelException(ExtractError(data), ChatErrorKind.Protocol);

        string model = Str(root, "model");
        if (!string.IsNullOrWhiteSpace(model)) response.Model = model;

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            string finish = Str(first, "finish_reason");
            if (!string.IsNullOrWhiteSpace(finish)) response.FinishReason = finish;
            if (first.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                builder.Append(content.GetString());
        }

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            response.Usage = ParseUsage(root);
    }

    static ChatUsage ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return default;

        int input = (int)(Num(usage, "prompt_tokens") ?? Num(usage, "input_tokens") ?? 0);
        int output = (int)(Num(usage, "completion_tokens") ?? Num(usage, "output_tokens") ?? 0);
        return new ChatUsage(input, output);
    }

    static double? Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
}
