using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AiDiskCleaner.Services;

public enum AiProtocol { Completions, Responses, Anthropic }

public sealed class AiToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "{}";
}

public sealed class AiMsg
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public List<AiToolCall>? Calls { get; set; }
    public string? CallId { get; set; }
    public string? ToolName { get; set; }
}

public sealed class AiReply
{
    public string Text { get; set; } = "";
    public List<AiToolCall> Calls { get; set; } = new();
    public bool HasTools => Calls.Count > 0;
}

public static class AiClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static AiProtocol ParseProtocol(string? s) => s switch
    {
        "responses" or "openai-responses" => AiProtocol.Responses,
        "anthropic" or "anthropic-messages" => AiProtocol.Anthropic,
        _ => AiProtocol.Completions,
    };

    public static string ProtocolId(AiProtocol p) => p switch
    {
        AiProtocol.Responses => "responses",
        AiProtocol.Anthropic => "anthropic",
        _ => "completions",
    };

    public static Task<string> ChatAsync(string system, string user, CancellationToken ct)
        => ChatAsync(system, new[] { new AiMsg { Role = "user", Text = user } }, ct);

    public static async Task<string> ChatAsync(string system, IReadOnlyList<AiMsg> turns, CancellationToken ct)
    {
        var reply = await TurnAsync(system, turns, tools: null, ct);
        return reply.Text;
    }

    public static Task<AiReply> TurnAsync(string system, IReadOnlyList<AiMsg> turns, IReadOnlyList<object>? tools, CancellationToken ct)
        => TurnAsync(App.Settings.CurrentProvider(), App.Settings.AiModel, system, turns, tools, ct);

    public static async Task<AiReply> StreamAsync(AiProviderCfg? p, string? modelId, string system, IReadOnlyList<AiMsg> turns, Action<string> onDelta, CancellationToken ct)
    {
        try
        {
            return await StreamCompletions(p, modelId, system, turns, onDelta, ct);
        }
        catch
        {
            var reply = await TurnAsync(p, modelId, system, turns, null, ct);
            if (!string.IsNullOrEmpty(reply.Text)) onDelta(reply.Text);
            return reply;
        }
    }

    public static async Task<AiReply> TurnAsync(AiProviderCfg? p, string? modelId, string system, IReadOnlyList<AiMsg> turns, IReadOnlyList<object>? tools, CancellationToken ct)
    {
        string baseUrl = (p?.BaseUrl ?? "").Trim().TrimEnd('/');
        string model = (modelId ?? "").Trim();
        string key = p?.ApiKey ?? "";
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiNeedConfig);
        var proto = ParseProtocol(p?.Protocol);
        try
        {
            return proto switch
            {
                AiProtocol.Anthropic => await Anthropic(baseUrl, model, key, system, turns, tools, ct),
                AiProtocol.Responses => await Responses(baseUrl, model, key, system, turns, tools, ct),
                _ => await Completions(baseUrl, model, key, system, turns, tools, ct),
            };
        }
        catch (Exception ex) when (tools != null && LooksLikeNoTools(ex))
        {
            return proto switch
            {
                AiProtocol.Anthropic => await Anthropic(baseUrl, model, key, system, turns, null, ct),
                AiProtocol.Responses => await Responses(baseUrl, model, key, system, turns, null, ct),
                _ => await Completions(baseUrl, model, key, system, turns, null, ct),
            };
        }
    }

    public static Task<string> TestAsync(CancellationToken ct)
        => ChatAsync(Loc.AiSystem, Loc.IsEn ? "Reply with one short word: ok" : "只回复一个字：好", ct);

    public static async Task<List<string>> ListModelsAsync(CancellationToken ct)
    {
        var p = App.Settings.CurrentProvider();
        string baseUrl = (p?.BaseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException(Loc.AiNeedUrl);
        var proto = ParseProtocol(p?.Protocol);
        string key = p?.ApiKey ?? "";
        using var req = new HttpRequestMessage(HttpMethod.Get, Join(baseUrl, "models"));
        Auth(req, proto, key);
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        return ParseModelIds(body);
    }

    static async Task<AiReply> StreamCompletions(AiProviderCfg? p, string? modelId, string system, IReadOnlyList<AiMsg> turns, Action<string> onDelta, CancellationToken ct)
    {
        string baseUrl = (p?.BaseUrl ?? "").Trim().TrimEnd('/');
        string model = (modelId ?? "").Trim();
        string key = p?.ApiKey ?? "";
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiNeedConfig);
        var messages = new List<object> { new { role = "system", content = system } };
        foreach (var t in turns)
            messages.Add(new { role = t.Role, content = t.Text });
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = 0.2,
            ["messages"] = messages,
            ["stream"] = true,
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, Join(baseUrl, "chat/completions"));
        Auth(req, ParseProtocol(p?.Protocol), key);
        req.Content = JsonBody(payload);
        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            string err = await res.Content.ReadAsStringAsync(ct);
            throw Fail(err, res.StatusCode.ToString());
        }
        var reply = new AiReply();
        var sb = new StringBuilder();
        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            string data = line[5..].Trim();
            if (data == "[DONE]") break;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                string delta = "";
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var c0 = choices[0];
                    if (c0.TryGetProperty("delta", out var d))
                        delta = Str(d, "content");
                    else if (c0.TryGetProperty("message", out var m))
                        delta = Str(m, "content");
                }
                if (string.IsNullOrEmpty(delta) && root.TryGetProperty("delta", out var d2))
                    delta = Str(d2, "text");
                if (string.IsNullOrEmpty(delta)) continue;
                sb.Append(delta);
                onDelta(delta);
            }
            catch { }
        }
        reply.Text = sb.ToString();
        if (string.IsNullOrWhiteSpace(reply.Text)) throw new InvalidOperationException("empty");
        return reply;
    }

    static bool LooksLikeNoTools(Exception ex)
    {
        string m = ex.Message.ToLowerInvariant();
        return m.Contains("tool") || m.Contains("function") || m.Contains("unknown")
               || m.Contains("unrecognized") || m.Contains("extra input") || m.Contains("unsupported");
    }

    static async Task<AiReply> Completions(string baseUrl, string model, string key, string system, IReadOnlyList<AiMsg> turns, IReadOnlyList<object>? tools, CancellationToken ct)
    {
        var messages = new List<object> { new { role = "system", content = system } };
        foreach (var t in turns)
        {
            if (t.Role == "tool")
                messages.Add(new { role = "tool", tool_call_id = t.CallId ?? "", content = t.Text });
            else if (t.Role == "assistant" && t.Calls is { Count: > 0 })
            {
                messages.Add(new
                {
                    role = "assistant",
                    content = string.IsNullOrEmpty(t.Text) ? null : t.Text,
                    tool_calls = t.Calls.Select(c => new
                    {
                        id = c.Id,
                        type = "function",
                        function = new { name = c.Name, arguments = c.Arguments },
                    }).ToArray(),
                });
            }
            else
                messages.Add(new { role = t.Role, content = t.Text });
        }
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = 0.2,
            ["messages"] = messages,
        };
        if (tools != null)
        {
            payload["tools"] = tools;
            payload["tool_choice"] = "auto";
        }
        using var req = new HttpRequestMessage(HttpMethod.Post, Join(baseUrl, "chat/completions"));
        Auth(req, AiProtocol.Completions, key);
        req.Content = JsonBody(payload);
        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        using var doc = JsonDocument.Parse(body);
        var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        var reply = new AiReply { Text = Str(msg, "content") };
        if (msg.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in calls.EnumerateArray())
            {
                var fn = c.GetProperty("function");
                reply.Calls.Add(new AiToolCall
                {
                    Id = Str(c, "id"),
                    Name = Str(fn, "name"),
                    Arguments = Str(fn, "arguments") is { Length: > 0 } a ? a : "{}",
                });
            }
        }
        if (string.IsNullOrWhiteSpace(reply.Text) && !reply.HasTools) throw Fail(body, "empty");
        return reply;
    }

    static async Task<AiReply> Responses(string baseUrl, string model, string key, string system, IReadOnlyList<AiMsg> turns, IReadOnlyList<object>? tools, CancellationToken ct)
    {
        var input = new List<object>();
        foreach (var t in turns)
        {
            if (t.Role == "tool")
                input.Add(new { type = "function_call_output", call_id = t.CallId ?? "", output = t.Text });
            else if (t.Role == "assistant" && t.Calls is { Count: > 0 })
            {
                if (!string.IsNullOrEmpty(t.Text))
                    input.Add(new { role = "assistant", content = t.Text });
                foreach (var c in t.Calls)
                    input.Add(new { type = "function_call", call_id = c.Id, name = c.Name, arguments = c.Arguments });
            }
            else
                input.Add(new { role = t.Role, content = t.Text });
        }
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["instructions"] = system,
            ["input"] = input,
            ["temperature"] = 0.2,
        };
        if (tools != null) payload["tools"] = tools;
        using var req = new HttpRequestMessage(HttpMethod.Post, Join(baseUrl, "responses"));
        Auth(req, AiProtocol.Responses, key);
        req.Content = JsonBody(payload);
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        using var doc = JsonDocument.Parse(body);
        var reply = new AiReply();
        var root = doc.RootElement;
        if (root.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
            reply.Text = ot.GetString() ?? "";
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            var texts = new List<string>();
            foreach (var item in output.EnumerateArray())
            {
                string type = Str(item, "type");
                if (type is "function_call" or "tool_call")
                {
                    reply.Calls.Add(new AiToolCall
                    {
                        Id = FirstStr(item, "call_id", "id"),
                        Name = Str(item, "name"),
                        Arguments = Str(item, "arguments") is { Length: > 0 } a ? a : "{}",
                    });
                }
                if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        if (Str(part, "type") is "output_text" or "text")
                            texts.Add(FirstStr(part, "text", "output_text"));
                    }
                }
            }
            if (string.IsNullOrEmpty(reply.Text) && texts.Count > 0)
                reply.Text = string.Join("", texts);
        }
        if (string.IsNullOrWhiteSpace(reply.Text) && !reply.HasTools) throw Fail(body, "empty");
        return reply;
    }

    static async Task<AiReply> Anthropic(string baseUrl, string model, string key, string system, IReadOnlyList<AiMsg> turns, IReadOnlyList<object>? tools, CancellationToken ct)
    {
        var messages = new List<object>();
        int i = 0;
        while (i < turns.Count)
        {
            var t = turns[i];
            if (t.Role == "tool")
            {
                var results = new List<object>();
                while (i < turns.Count && turns[i].Role == "tool")
                {
                    results.Add(new { type = "tool_result", tool_use_id = turns[i].CallId ?? "", content = turns[i].Text });
                    i++;
                }
                messages.Add(new { role = "user", content = results });
                continue;
            }
            if (t.Role == "assistant" && t.Calls is { Count: > 0 })
            {
                var parts = new List<object>();
                if (!string.IsNullOrEmpty(t.Text))
                    parts.Add(new { type = "text", text = t.Text });
                foreach (var c in t.Calls)
                {
                    object input = new Dictionary<string, object>();
                    try { input = JsonSerializer.Deserialize<Dictionary<string, object>>(c.Arguments) ?? input; }
                    catch { }
                    parts.Add(new { type = "tool_use", id = c.Id, name = c.Name, input });
                }
                messages.Add(new { role = "assistant", content = parts });
            }
            else
                messages.Add(new { role = t.Role == "assistant" ? "assistant" : "user", content = t.Text });
            i++;
        }
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = 2048,
            ["system"] = system,
            ["messages"] = messages,
        };
        if (tools != null)
        {
            payload["tools"] = tools;
            payload["tool_choice"] = new { type = "auto" };
        }
        using var req = new HttpRequestMessage(HttpMethod.Post, Join(baseUrl, "messages"));
        Auth(req, AiProtocol.Anthropic, key);
        req.Content = JsonBody(payload);
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        using var doc = JsonDocument.Parse(body);
        var reply = new AiReply();
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var texts = new List<string>();
            foreach (var part in content.EnumerateArray())
            {
                string type = Str(part, "type");
                if (type == "text") texts.Add(Str(part, "text"));
                if (type == "tool_use")
                {
                    reply.Calls.Add(new AiToolCall
                    {
                        Id = Str(part, "id"),
                        Name = Str(part, "name"),
                        Arguments = part.TryGetProperty("input", out var input)
                            ? input.GetRawText()
                            : "{}",
                    });
                }
            }
            reply.Text = string.Join("", texts);
        }
        if (string.IsNullOrWhiteSpace(reply.Text) && !reply.HasTools) throw Fail(body, "empty");
        return reply;
    }

    static void Auth(HttpRequestMessage req, AiProtocol proto, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (proto == AiProtocol.Anthropic)
        {
            req.Headers.TryAddWithoutValidation("x-api-key", key);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
        else
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
    }

    static string Join(string baseUrl, string endpoint)
    {
        string root = baseUrl.Trim().TrimEnd('/');
        bool hasV1 = root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                     || root.Contains("/v1/", StringComparison.OrdinalIgnoreCase)
                     || root.Contains("/v1beta", StringComparison.OrdinalIgnoreCase);
        if (!hasV1) root += "/v1";
        return root + "/" + endpoint.TrimStart('/');
    }

    static List<string> ParseModelIds(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var ids = new List<string>();
        JsonElement list = default;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            list = data;
        else if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            list = models;
        else if (root.ValueKind == JsonValueKind.Array)
            list = root;
        if (list.ValueKind != JsonValueKind.Array) return ids;
        foreach (var item in list.EnumerateArray())
        {
            string? id = null;
            if (item.ValueKind == JsonValueKind.String) id = item.GetString();
            else if (item.TryGetProperty("id", out var p)) id = p.GetString();
            else if (item.TryGetProperty("name", out var n)) id = n.GetString();
            if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
        }
        ids.Sort(StringComparer.OrdinalIgnoreCase);
        return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    static string FirstStr(JsonElement e, params string[] names)
    {
        foreach (var n in names)
        {
            string v = Str(e, n);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return "";
    }

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
