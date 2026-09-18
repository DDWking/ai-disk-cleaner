using System.ClientModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AiDiskCleaner.Models;
using OpenAI;
using OpenAI.Chat;

namespace AiDiskCleaner.Services;

public static class AiClient
{
    static AiClient()
    {
        AppContext.SetSwitch("OpenAI.DisableTelemetry", true);
        // 组合根接线：把 sidecar / 内置 HTTP 接到统一入口上
        AiGateways.Register();
    }

    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            },
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
    }
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static AiProtocol ParseProtocol(string? s) => s switch
    {
        "responses" or "openai-responses" => AiProtocol.Responses,
        "anthropic" or "anthropic-messages" => AiProtocol.Anthropic,
        "decisions" or "systemone" => AiProtocol.Decisions,
        _ => AiProtocol.Completions,
    };

    public static string ProtocolId(AiProtocol p) => p switch
    {
        AiProtocol.Responses => "responses",
        AiProtocol.Anthropic => "anthropic",
        AiProtocol.Decisions => "decisions",
        _ => "completions",
    };

    public static Task<string> ChatAsync(string system, string user, CancellationToken ct)
        => ChatAsync(system, new[] { new AiMsg { Role = "user", Text = user } }, ct);

    public static async Task<string> ChatAsync(string system, IReadOnlyList<AiMsg> turns, CancellationToken ct)
    {
        // 走统一网关：选路（sidecar → 内置 HTTP）、超时、重试、限流、fallback 都在那一层。
        var reply = await AiGateway.SendAsync(new AiRequest
        {
            Provider = App.Settings.CurrentProvider(),
            Model = App.Settings.AiModel,
            System = system,
            Turns = turns,
            MaxTurns = 1,
        }, null, ct);
        return reply.Text;
    }

    public static Task<AiReply> TurnAsync(string system, IReadOnlyList<AiMsg> turns, IReadOnlyList<object>? tools, CancellationToken ct)
        => TurnAsync(App.Settings.CurrentProvider(), App.Settings.AiModel, system, turns, tools, ct);

    /// <summary>
    /// 「给每一条写一句话」的批注任务。
    ///
    /// <paramref name="maxTurns"/> 默认 **1**：这纯粹是一次文本转换，不需要多轮工具调查。
    /// 以前这里硬编码 4，sidecar 的 agent 会拿着工具跑最多 4 轮，然后在超轮数后
    /// 额外发一次「兜底总结」请求 —— 实测一次批注要 111 秒，而且循环把预算花在
    /// 调查上，最终只产出部分条目（实测 60 条里只回来 15 条可用说明）。
    /// 单轮就能让模型直接把清单写完，既不浪费请求也不容易截断。
    /// </summary>
    public static Task<AiReply> StreamAsync(
        AiProviderCfg? p, string? modelId, string system, IReadOnlyList<AiMsg> turns,
        Action<string> onDelta, CancellationToken ct, int maxTurns = 1)
        => AiGateway.SendAsync(new AiRequest
        {
            Provider = p,
            Model = modelId,
            System = system,
            Turns = turns,
            MaxTurns = maxTurns,
        }, onDelta, ct);

    static bool LooksLikeHardFail(Exception ex)
    {
        string t = (ex.Message + " " + (ex.InnerException?.Message ?? "")).ToLowerInvariant();
        return t.Contains("520") || t.Contains("502") || t.Contains("503") || t.Contains("504")
            || t.Contains("401") || t.Contains("403");
    }

    /// <summary>
    /// 内置通道的一次完整发送（含 completions 的流式优先）。
    /// 只给 <see cref="DirectHttpAiGateway"/> 用 —— 外面请走 <see cref="AiGateway"/>，
    /// 否则会绕过重试 / 限流 / fallback。
    /// </summary>
    internal static async Task<AiReply> SendDirectAsync(AiRequest request, Action<string>? onDelta, CancellationToken ct)
    {
        var p = request.Provider;
        string? modelId = request.Model;
        var proto = ParseProtocol(p?.Protocol);

        if (proto == AiProtocol.Completions && onDelta != null)
        {
            try
            {
                return await StreamCompletions(p, modelId, request.System, request.Turns, onDelta, ct);
            }
            catch (Exception ex)
            {
                if (LooksLikeHardFail(ex)) throw new InvalidOperationException(Pretty(ex), ex);
            }
        }

        var reply = await SendDirectOnceAsync(p, modelId, request.System, request.Turns, request.Tools, ct);
        if (!string.IsNullOrEmpty(reply.Text) && onDelta != null) onDelta(reply.Text);
        return reply;
    }

    /// <summary>内置通道的协议分发（不做选路、不重试）。</summary>
    internal static async Task<AiReply> SendDirectOnceAsync(AiProviderCfg? p, string? modelId, string system, IReadOnlyList<AiMsg> turns, IReadOnlyList<object>? tools, CancellationToken ct)
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
        catch (Exception ex)
        {
            throw new InvalidOperationException(Pretty(ex), ex);
        }
    }

    public static Task<AiReply> TurnAsync(AiProviderCfg? p, string? modelId, string system, IReadOnlyList<AiMsg> turns, IReadOnlyList<object>? tools, CancellationToken ct)
        => AiGateway.SendAsync(new AiRequest
        {
            Provider = p,
            Model = modelId,
            System = system,
            Turns = turns,
            Tools = tools,
        }, null, ct);

    public static string Pretty(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        if (inner is ClientResultException cre)
        {
            if (cre.Status == 520) return Loc.AiHttp520;
            if (cre.Status > 0) return Loc.AiHttpFail(cre.Status, cre.Message);
            return cre.Message;
        }
        if (inner is HttpRequestException http)
        {
            if (http.StatusCode is { } code) return Loc.AiHttpFail((int)code, http.Message);
            if (inner.InnerException is SocketException sock)
                return Loc.AiHostFail(sock.Message);
            return Loc.AiNetFail(http.Message);
        }
        if (inner is SocketException s) return Loc.AiHostFail(s.Message);
        if (inner is TaskCanceledException or OperationCanceledException or TimeoutException)
            return Loc.AiTimeout;
        string m = inner.Message ?? ex.Message;
        if (m.Contains("No such host", StringComparison.OrdinalIgnoreCase)
            || m.Contains("不知道这样的主机", StringComparison.OrdinalIgnoreCase)
            || m.Contains("host not found", StringComparison.OrdinalIgnoreCase))
            return Loc.AiHostFail(m);
        if (m.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || m.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
            || m.Contains("没有正确答复", StringComparison.OrdinalIgnoreCase))
            return Loc.AiTimeout;
        if (m.Contains("520")) return Loc.AiHttp520;
        return m;
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

    /// <summary>
    /// 结构化判定通道（TypeSafe Jev 这类）。
    ///
    /// <b>URL 约定和 chat 不一样，不要用 <see cref="Join"/>。</b>
    /// <see cref="RootUrl"/> 会在缺失时补 <c>/v1</c>，可 OpenRouter 的判定端点恰恰是
    /// <c>https://openrouter.ai/api/alpha/decisions</c>——补成 <c>/api/v1/alpha/decisions</c> 就是 404。
    /// 所以这里直接把 <c>/decisions</c> 接到配置的 BaseUrl 后面；BaseUrl 已经写了
    /// <c>/decisions</c> 的话就原样用，方便用户直接粘贴完整地址。
    /// </summary>
    internal static async Task<AiDecisionReply> SendDecisionsAsync(AiDecisionRequest request, CancellationToken ct)
    {
        var p = request.Provider;
        string baseUrl = (p?.BaseUrl ?? "").Trim().TrimEnd('/');
        string model = (request.Model ?? "").Trim();
        string key = p?.ApiKey ?? "";
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiNeedConfig);

        string url = baseUrl.EndsWith("/decisions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : baseUrl + "/decisions";

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["state"] = request.State,
            ["questions"] = request.Questions.ToDictionary(kv => kv.Key, kv => (object?)BuildQuestion(kv.Value)),
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonBody(payload) };
        // 判定通道一律 Bearer（Auth 只对 Anthropic 特判）
        Auth(req, AiProtocol.Decisions, key);

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

    static async Task<AiReply> StreamCompletions(AiProviderCfg? p, string? modelId, string system, IReadOnlyList<AiMsg> turns, Action<string> onDelta, CancellationToken ct)
    {
        string baseUrl = (p?.BaseUrl ?? "").Trim().TrimEnd('/');
        string model = (modelId ?? "").Trim();
        string key = p?.ApiKey ?? "";
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(Loc.AiNeedConfig);
        var client = MakeChat(baseUrl, model, key);
        var messages = ToChatMessages(system, turns);
        var sb = new StringBuilder();
        using var first = CancellationTokenSource.CreateLinkedTokenSource(ct);
        first.CancelAfter(TimeSpan.FromSeconds(45));
        bool got = false;
        try
        {
            await foreach (var update in client.CompleteChatStreamingAsync(messages, new ChatCompletionOptions(), first.Token))
            {
                if (update.ContentUpdate.Count == 0) continue;
                foreach (var part in update.ContentUpdate)
                {
                    if (string.IsNullOrEmpty(part.Text)) continue;
                    if (!got)
                    {
                        got = true;
                        first.CancelAfter(Timeout.InfiniteTimeSpan);
                    }
                    sb.Append(part.Text);
                    onDelta(part.Text);
                }
            }
        }
        catch (OperationCanceledException) when (!got && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("empty");
        }
        var reply = new AiReply { Text = sb.ToString() };
        if (string.IsNullOrWhiteSpace(reply.Text)) throw new InvalidOperationException("empty");
        return reply;
    }

    static bool LooksLikeNoTools(Exception ex)
    {
        string m = ex.Message.ToLowerInvariant();
        return m.Contains("tool") || m.Contains("function") || m.Contains("unknown")
               || m.Contains("unrecognized") || m.Contains("extra input") || m.Contains("unsupported");
    }

    static ChatClient MakeChat(string baseUrl, string model, string key)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(RootUrl(baseUrl)),
            NetworkTimeout = TimeSpan.FromSeconds(90),
        };
        string cred = string.IsNullOrWhiteSpace(key) ? "none" : key;
        return new ChatClient(model, new ApiKeyCredential(cred), options);
    }

    static List<ChatMessage> ToChatMessages(string system, IReadOnlyList<AiMsg> turns)
    {
        var list = new List<ChatMessage> { new SystemChatMessage(system) };
        foreach (var t in turns)
        {
            if (t.Role == "tool")
                list.Add(new ToolChatMessage(t.CallId ?? "", t.Text ?? ""));
            else if (t.Role == "assistant" && t.Calls is { Count: > 0 })
            {
                var calls = t.Calls.Select(c => ChatToolCall.CreateFunctionToolCall(
                    string.IsNullOrEmpty(c.Id) ? Guid.NewGuid().ToString("N") : c.Id,
                    c.Name,
                    BinaryData.FromString(string.IsNullOrEmpty(c.Arguments) ? "{}" : c.Arguments))).ToList();
                var msg = new AssistantChatMessage(calls);
                if (!string.IsNullOrEmpty(t.Text))
                    msg.Content.Add(ChatMessageContentPart.CreateTextPart(t.Text));
                list.Add(msg);
            }
            else if (t.Role == "assistant")
                list.Add(new AssistantChatMessage(t.Text ?? ""));
            else
                list.Add(new UserChatMessage(t.Text ?? ""));
        }
        return list;
    }

    static IEnumerable<ChatTool> ToChatTools(IReadOnlyList<object> tools)
    {
        foreach (var t in tools)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(t, Json));
            var root = doc.RootElement;
            var fn = root.TryGetProperty("function", out var f) ? f : root;
            string name = Str(fn, "name");
            if (string.IsNullOrEmpty(name)) continue;
            string desc = Str(fn, "description");
            BinaryData? schema = fn.TryGetProperty("parameters", out var p)
                ? BinaryData.FromString(p.GetRawText())
                : null;
            yield return ChatTool.CreateFunctionTool(name, desc, schema);
        }
    }

    static async Task<AiReply> Completions(string baseUrl, string model, string key, string system, IReadOnlyList<AiMsg> turns, IReadOnlyList<object>? tools, CancellationToken ct)
    {
        var client = MakeChat(baseUrl, model, key);
        var messages = ToChatMessages(system, turns);
        var options = new ChatCompletionOptions();
        if (tools != null)
        {
            foreach (var tool in ToChatTools(tools))
                options.Tools.Add(tool);
        }
        ClientResult<ChatCompletion> result = await client.CompleteChatAsync(messages, options, ct);
        return FromCompletion(result);
    }

    static AiReply FromCompletion(ClientResult<ChatCompletion> result)
    {
        var completion = result.Value;
        var reply = new AiReply();
        if (completion.Content is { Count: > 0 })
        {
            var bits = new List<string>();
            foreach (var part in completion.Content)
                if (!string.IsNullOrEmpty(part.Text)) bits.Add(part.Text);
            reply.Text = string.Join("", bits);
        }
        if (completion.ToolCalls is { Count: > 0 })
        {
            foreach (var c in completion.ToolCalls)
            {
                reply.Calls.Add(new AiToolCall
                {
                    Id = c.Id ?? "",
                    Name = c.FunctionName ?? "",
                    Arguments = c.FunctionArguments is { } args && args.ToMemory().Length > 0
                        ? args.ToString()
                        : "{}",
                });
            }
        }
        if (string.IsNullOrWhiteSpace(reply.Text) && !reply.HasTools)
        {
            string raw = "";
            try { raw = result.GetRawResponse()?.Content?.ToString() ?? ""; }
            catch (Exception ex)
            {
                // SDK 内部对象取不到原始体：只是少了一条兜底解析路径，记一笔继续。
                AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Ai", "", "raw-response",
                    ex.GetType().Name + " " + LogRedactor.Scrub(AppError.RootMessage(ex))));
            }
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var msg = choices[0].GetProperty("message");
                        reply.Text = MessageText(msg);
                    }
                }
                catch (Exception ex)
                {
                    // 兜底解析失败是可预期的（有的中转返回的不是标准 JSON），
                    // 上层会用 ClipBody(raw) 报错，这里只留技术痕迹。
                    AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Ai", "", "raw-parse",
                        ex.GetType().Name));
                }
            }
            if (string.IsNullOrWhiteSpace(reply.Text))
                throw Fail(ClipBody(raw), "empty");
        }
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
                    catch (Exception ex)
                    {
                        // 模型给的 tool 参数不是合法 JSON：退回空对象，别把整轮对话炸掉。
                        AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Ai", "", "tool-args",
                            "tool=" + c.Name + " " + ex.GetType().Name));
                    }
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
        if (string.IsNullOrWhiteSpace(reply.Text) && !reply.HasTools) throw Fail(ClipBody(body), "empty");
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

    static string RootUrl(string baseUrl)
    {
        string root = baseUrl.Trim().TrimEnd('/');
        bool hasV1 = root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                     || root.Contains("/v1/", StringComparison.OrdinalIgnoreCase)
                     || root.Contains("/v1beta", StringComparison.OrdinalIgnoreCase);
        if (!hasV1) root += "/v1";
        return root;
    }

    static string Join(string baseUrl, string endpoint)
        => RootUrl(baseUrl) + "/" + endpoint.TrimStart('/');

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

    static string MessageText(JsonElement msg)
    {
        string text = ContentText(msg.TryGetProperty("content", out var c) ? c : default);
        if (!string.IsNullOrWhiteSpace(text)) return text;
        foreach (var name in new[] { "reasoning_content", "reasoning", "output_text", "text" })
        {
            text = ContentText(msg.TryGetProperty(name, out var p) ? p : default);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return "";
    }

    static string ContentText(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String) return e.GetString() ?? "";
        if (e.ValueKind == JsonValueKind.Array)
        {
            var bits = new List<string>();
            foreach (var part in e.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String) bits.Add(part.GetString() ?? "");
                else if (part.ValueKind == JsonValueKind.Object)
                {
                    string t = FirstStr(part, "text", "content", "output_text");
                    if (!string.IsNullOrEmpty(t)) bits.Add(t);
                }
            }
            return string.Join("", bits);
        }
        return "";
    }

    static string ClipBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "empty";
        string t = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return t.Length > 280 ? t[..277] + "…" : t;
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
