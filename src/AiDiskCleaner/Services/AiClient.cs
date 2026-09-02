using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AiDiskCleaner.Services;

public enum AiProtocol { Completions, Responses, Anthropic }

public static class AiClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

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
        => ChatAsync(system, new[] { ("user", user) }, ct);

    public static async Task<string> ChatAsync(string system, IReadOnlyList<(string Role, string Text)> turns, CancellationToken ct)
    {
        var s = App.Settings;
        string baseUrl = (s.AiBaseUrl ?? "").Trim().TrimEnd('/');
        string model = (s.AiModel ?? "").Trim();
        string key = s.AiApiKey ?? "";
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiNeedConfig);
        var proto = ParseProtocol(s.AiProtocol);
        return proto switch
        {
            AiProtocol.Anthropic => await Anthropic(baseUrl, model, key, system, turns, ct),
            AiProtocol.Responses => await Responses(baseUrl, model, key, system, turns, ct),
            _ => await Completions(baseUrl, model, key, system, turns, ct),
        };
    }

    public static Task<string> TestAsync(CancellationToken ct)
        => ChatAsync(Loc.AiSystem, Loc.IsEn ? "Reply with one short word: ok" : "只回复一个字：好", ct);

    public static async Task<List<string>> ListModelsAsync(CancellationToken ct)
    {
        var s = App.Settings;
        string baseUrl = (s.AiBaseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException(Loc.AiNeedUrl);
        var proto = ParseProtocol(s.AiProtocol);
        string key = s.AiApiKey ?? "";
        using var req = new HttpRequestMessage(HttpMethod.Get, Join(baseUrl, "models"));
        Auth(req, proto, key);
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        return ParseModelIds(body);
    }

    static async Task<string> Completions(string baseUrl, string model, string key, string system, IReadOnlyList<(string Role, string Text)> turns, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Join(baseUrl, "chat/completions"));
        Auth(req, AiProtocol.Completions, key);
        var messages = new List<object> { new { role = "system", content = system } };
        foreach (var t in turns)
            messages.Add(new { role = t.Role, content = t.Text });
        req.Content = JsonBody(new
        {
            model,
            temperature = 0.2,
            messages,
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

    static async Task<string> Responses(string baseUrl, string model, string key, string system, IReadOnlyList<(string Role, string Text)> turns, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Join(baseUrl, "responses"));
        Auth(req, AiProtocol.Responses, key);
        var input = turns.Select(t => new { role = t.Role, content = t.Text }).ToArray();
        req.Content = JsonBody(new
        {
            model,
            instructions = system,
            input,
            temperature = 0.2,
        });
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw Fail(body, res.StatusCode.ToString());
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
            return ot.GetString() ?? "";
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            var texts = new List<string>();
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        texts.Add(t.GetString() ?? "");
                    else if (part.TryGetProperty("output_text", out var t2) && t2.ValueKind == JsonValueKind.String)
                        texts.Add(t2.GetString() ?? "");
                }
            }
            if (texts.Count > 0) return string.Join("", texts);
        }
        throw Fail(body, "empty");
    }

    static async Task<string> Anthropic(string baseUrl, string model, string key, string system, IReadOnlyList<(string Role, string Text)> turns, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Join(baseUrl, "messages"));
        Auth(req, AiProtocol.Anthropic, key);
        var messages = turns.Select(t => new { role = t.Role, content = t.Text }).ToArray();
        req.Content = JsonBody(new
        {
            model,
            max_tokens = 800,
            system,
            messages,
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
