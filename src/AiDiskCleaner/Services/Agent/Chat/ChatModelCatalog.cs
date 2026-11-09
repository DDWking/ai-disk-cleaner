using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AiDiskCleaner.Services.Agent;

/// <summary>
/// 从 OpenAI 兼容上游拉「可用模型」列表，只用于设置页把模型名填对。
/// 纯只读：GET {base}/models，不重试、不落盘、不写日志；失败一律返回错误文本，
/// 由界面用黄字提示并保留手动输入 —— 不阻断用户。
/// </summary>
public sealed record ChatModelList(bool Ok, IReadOnlyList<string> Models, string? Error)
{
    public static ChatModelList Fail(string error) => new(false, Array.Empty<string>(), error);
}

public static class ChatModelCatalog
{
    private const int TimeoutSeconds = 15;

    /// <summary>
    /// 把用户填的聊天地址换算成模型列表地址：
    /// https://api.deepseek.com/v1 → https://api.deepseek.com/v1/models
    /// https://x/v1/chat/completions → https://x/v1/models
    /// https://x/v1/models → 原样（不重复拼）。
    /// </summary>
    public static string ModelsEndpoint(string? baseUrl)
    {
        string url = (baseUrl ?? "").Trim().TrimEnd('/');
        const string chatSuffix = "/chat/completions";
        if (url.EndsWith(chatSuffix, StringComparison.OrdinalIgnoreCase))
            url = url[..^chatSuffix.Length].TrimEnd('/');
        if (url.EndsWith("/models", StringComparison.OrdinalIgnoreCase)) return url;
        return url + "/models";
    }

    /// <summary>
    /// 解析两种常见形状：{ "data": [ { "id": "..." }, ... ] } 与裸数组 [ "..." ]。
    /// 去重（忽略大小写）后按字母排序，保证同一个上游每次展示顺序一致。
    /// </summary>
    public static ChatModelList Parse(string? body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body ?? "");
        }
        catch (JsonException)
        {
            return ChatModelList.Fail("返回内容不是 JSON");
        }

        using (doc)
        {
            var models = new List<string>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                Collect(doc.RootElement, models);
            else if (doc.RootElement.ValueKind == JsonValueKind.Object
                     && doc.RootElement.TryGetProperty("data", out var data))
                Collect(data, models);
            else
                return ChatModelList.Fail("返回内容不是模型列表");

            if (models.Count == 0) return ChatModelList.Fail("上游没有返回任何模型");

            var sorted = models
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new ChatModelList(true, sorted, null);
        }
    }

    /// <summary>请求上游模型列表；任何失败都变成 Ok=false + 可读原因，不抛给界面。</summary>
    public static async Task<ChatModelList> FetchAsync(
        string baseUrl, string? apiKey, HttpClient? http = null, CancellationToken ct = default)
    {
        string endpoint = ModelsEndpoint(baseUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            using var response = await (http ?? AiHttp.Client)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return ChatModelList.Fail($"HTTP {(int)response.StatusCode}: {ErrorText(body)}");

            return Parse(body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ChatModelList.Fail($"请求超时（{TimeoutSeconds} 秒）");
        }
        catch (HttpRequestException ex)
        {
            return ChatModelList.Fail(ex.Message);
        }
    }

    private static void Collect(JsonElement array, List<string> models)
    {
        if (array.ValueKind != JsonValueKind.Array) return;
        foreach (var item in array.EnumerateArray())
        {
            string? id = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : item.ValueKind == JsonValueKind.Object
                  && item.TryGetProperty("id", out var prop)
                  && prop.ValueKind == JsonValueKind.String
                    ? prop.GetString()
                    : null;
            if (!string.IsNullOrWhiteSpace(id)) models.Add(id.Trim());
        }
    }

    private static string ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String) return Clip(error.GetString());
                    if (error.ValueKind == JsonValueKind.Object
                        && error.TryGetProperty("message", out var message)
                        && message.ValueKind == JsonValueKind.String)
                        return Clip(message.GetString());
                }
                if (doc.RootElement.TryGetProperty("message", out var top)
                    && top.ValueKind == JsonValueKind.String)
                    return Clip(top.GetString());
            }
        }
        catch (JsonException)
        {
            // 非 JSON 错误页：直接用截断后的原文。
        }
        return Clip(body);
    }

    private static string Clip(string? text)
    {
        string t = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (t.Length == 0) return "没有错误详情";
        return t.Length <= 160 ? t : t[..160] + "…";
    }
}
