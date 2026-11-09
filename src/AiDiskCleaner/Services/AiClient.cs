using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AiDiskCleaner.Services;

/// <summary>
/// 判定通道的 HTTP 实现。
///
/// v3.0 把聊天那一整套（completions / responses / anthropic / 流式 / 工具循环 /
/// Node 侧车 / OpenAI SDK）删掉了，只留这一条：三个原语、请求/响应、没有流。
/// agent 的聊天通道（<see cref="Agent.OpenAiCompatChatModel"/>）复用
/// <see cref="AiHttp"/> 的同一个客户端，两条通道的 TLS/DoH 行为保持一致。
///
/// URL 约定和聊天不一样，**不要用「补 /v1」那套**：判定端点是
/// <c>{BaseUrl}/decisions</c>。BaseUrl 已经以 <c>/decisions</c> 结尾时原样使用
/// —— 这样用户可以直接粘完整地址（例如 OpenRouter 的
/// <c>https://openrouter.ai/api/alpha/decisions</c>，补成 <c>/api/v1/alpha/decisions</c> 就是 404）。
/// </summary>
public static class AiClient
{
    static AiClient()
    {
        // 组合根接线：把判定通道接到统一入口上。
        // 界面启动时 App.OnStartup 也会显式调一次 —— 静态构造只在「有东西碰到 AiClient」
        // 时才跑，而设置页读配置刻意不碰 AiClient，只靠这里会漏接线。
        AiGateways.Register();
    }

    static readonly HttpClient Http = AiHttp.Client;

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// 发一次结构化判定（不做选路、不重试 —— 那些在 <see cref="AiGateway.DecideAsync"/>）。
    /// </summary>
    internal static async Task<AiDecisionReply> SendDecisionsAsync(AiDecisionRequest request, CancellationToken ct)
    {
        var p = request.Provider;
        string baseUrl = (p?.BaseUrl ?? "").Trim().TrimEnd('/');
        string model = (request.Model ?? "").Trim();
        string key = p?.ApiKey ?? "";
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiNeedConfig);

        bool systemOne = string.Equals(p?.Protocol, "systemone", StringComparison.OrdinalIgnoreCase);
        string suffix = systemOne ? "/v1/systemone" : "/decisions";
        string url = baseUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : baseUrl + suffix;

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["state"] = request.State,
            ["questions"] = request.Questions.ToDictionary(kv => kv.Key, kv => (object?)BuildQuestion(kv.Value)),
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonBody(payload) };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);

        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        return ParseDecisions(body);
    }

    static Dictionary<string, object?> BuildQuestion(AiDecisionQuestion q)
    {
        var d = new Dictionary<string, object?>
        {
            ["type"] = string.IsNullOrWhiteSpace(q.Type) ? "choice" : q.Type,
            ["instructions"] = q.Instructions,
        };
        // 同一个字段名承载两种形状：choice 是对象（键→说明），score 是数组（档位描述）
        if (q.Criteria is { Count: > 0 }) d["criteria"] = q.Criteria;
        else if (q.Scale is { Count: > 0 }) d["criteria"] = q.Scale;
        return d;
    }

    static AiDecisionReply ParseDecisions(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // 有的中转会把错误塞在 200 的 body 里
        if (root.TryGetProperty("error", out _)) throw Fail(body, "decisions");

        var reply = new AiDecisionReply { Model = Str(root, "model") };

        if (root.TryGetProperty("answers", out var answers) && answers.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in answers.EnumerateObject())
            {
                var v = prop.Value;
                if (v.ValueKind != JsonValueKind.Object) continue;
                var a = new AiDecisionAnswer
                {
                    Type = Str(v, "type"),
                    Choice = Str(v, "choice"),
                    Noul = Num(v, "noul"),
                    Score = Num(v, "score"),
                };
                a.Confidence = Num(v, "confidence") ?? a.Noul ?? 0;
                if (v.TryGetProperty("probabilities", out var pr) && pr.ValueKind == JsonValueKind.Object)
                {
                    var map = new Dictionary<string, double>();
                    foreach (var e in pr.EnumerateObject())
                        if (e.Value.ValueKind == JsonValueKind.Number) map[e.Name] = e.Value.GetDouble();
                    a.Probabilities = map;
                }
                if (string.IsNullOrEmpty(a.Choice)) a.Choice = null;
                reply.Answers[prop.Name] = a;
            }
        }

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            reply.InputTokens = (int)(Num(usage, "input_tokens") ?? 0);
            reply.OutputTokens = (int)(Num(usage, "output_tokens") ?? 0);
            reply.Cost = Num(usage, "cost") ?? 0;
        }
        return reply;
    }

    static double? Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    static StringContent JsonBody(object obj)
        => new(JsonSerializer.Serialize(obj, Json), Encoding.UTF8, "application/json");

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
