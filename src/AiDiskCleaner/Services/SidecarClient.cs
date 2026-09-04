using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AiDiskCleaner.Services;

/// <summary>
/// Pi sidecar 的 C# 侧客户端。
/// 优先走 sidecar（pi-ai 处理中转协议/推理内容/工具循环），
/// 起不来或报错就抛出去，由 AiClient 退回 OpenAI.NET 路径。
/// </summary>
public static class SidecarClient
{
    static readonly object Gate = new();
    static Process? _proc;
    static int _port;
    static SidecarToolHost? _toolHost;
    static bool _disabled;   // 彻底起不来就不再重试，避免每次分析都卡一遍

    public static bool IsRunning => _proc is { HasExited: false };

    public static void AttachToolHost(IAnalystHost host)
    {
        lock (Gate)
        {
            _toolHost?.Dispose();
            _toolHost = new SidecarToolHost(host);
        }
    }

    static int ToolPort
    {
        get
        {
            lock (Gate) { return _toolHost?.Port ?? 0; }
        }
    }

    static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>定位 sidecar：优先随包 exe，其次开发模式的 node 脚本。</summary>
    static (string File, string? ArgsPrefix)? Locate()
    {
        var baseDir = AppContext.BaseDirectory;

        // 1) 打包后的自包含 exe
        string[] exeNames = { "AiSidecar.exe", "sidecar.exe", "pi-sidecar.exe" };
        foreach (var name in exeNames)
        {
            string p = Path.Combine(baseDir, "sidecar", name);
            if (File.Exists(p)) return (p, null);
        }

        // 2) 开发模式：node + 源码入口
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            string js = Path.Combine(dir.FullName, "sidecar", "src", "index.js");
            if (File.Exists(js)) return (js, null);
        }

        return null;
    }

    static bool IsNodeScript(string path) => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

    public static bool EnsureStarted()
    {
        lock (Gate)
        {
            if (_disabled) return false;
            if (_proc is { HasExited: false }) return true;

            var loc = Locate();
            if (loc == null)
            {
                _disabled = true;
                return false;
            }

            try
            {
                _port = FreePort();
                var psi = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(loc.Value.File)!,
                };

                if (IsNodeScript(loc.Value.File))
                {
                    psi.FileName = "node";
                    psi.ArgumentList.Add(loc.Value.File);
                }
                else
                {
                    psi.FileName = loc.Value.File;
                }
                psi.ArgumentList.Add(_port.ToString());

                var proc = Process.Start(psi);
                if (proc == null) { _disabled = true; return false; }
                _proc = proc;

                // 等 READY（sidecar 起来后会打一行 "READY <port>"）
                var ready = proc.StandardOutput.ReadLineAsync();
                if (ready.Wait(TimeSpan.FromSeconds(20)))
                {
                    string? line = ready.Result;
                    if (!string.IsNullOrEmpty(line) && line.StartsWith("READY", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // 起不来就杀掉，标记禁用，交给降级路径
                try { proc.Kill(true); } catch { }
                _proc = null;
                _disabled = true;
                return false;
            }
            catch
            {
                _disabled = true;
                return false;
            }
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            try { _proc?.Kill(true); } catch { }
            _proc?.Dispose();
            _proc = null;
            _toolHost?.Dispose();
            _toolHost = null;
        }
    }

    /// <summary>发一次对话，onDelta 收文本块。工具调用由 toolHost 自动应答。</summary>
    public static async Task<string> ChatAsync(
        AiProviderCfg provider,
        string model,
        string system,
        IReadOnlyList<AiMsg> turns,
        Action<string> onDelta,
        int maxTurns,
        CancellationToken ct)
    {
        if (!EnsureStarted()) throw new InvalidOperationException("sidecar unavailable");

        var messages = new List<object>();
        foreach (var t in turns)
            if (!string.IsNullOrWhiteSpace(t.Text))
                messages.Add(new { role = t.Role, content = t.Text });

        var payload = new
        {
            provider = new
            {
                baseUrl = provider.BaseUrl,
                apiKey = provider.ApiKey,
                api = ProtocolToApi(provider.Protocol),
                model,
            },
            system,
            messages,
            callbackPort = ToolPort,
            maxTurns,
        };

        var body = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_port}/chat")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        using var res = await SharedHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"sidecar HTTP {(int)res.StatusCode}");

        var sb = new StringBuilder();
        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            string data = line[5..].Trim();
            if (string.IsNullOrEmpty(data)) continue;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                string type = root.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";
                if (type == "delta")
                {
                    string text = root.TryGetProperty("text", out var tx) ? (tx.GetString() ?? "") : "";
                    if (!string.IsNullOrEmpty(text))
                    {
                        sb.Append(text);
                        onDelta(text);
                    }
                }
                else if (type == "error")
                {
                    string msg = root.TryGetProperty("message", out var m) ? (m.GetString() ?? "") : "";
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? "sidecar error" : msg);
                }
                else if (type == "done")
                {
                    break;
                }
            }
            catch (JsonException) { /* 半包忽略 */ }
        }

        return sb.ToString();
    }

    static string ProtocolToApi(string? protocol)
        => (protocol ?? "").ToLowerInvariant() switch
        {
            "responses" or "openai-responses" => "openai-responses",
            "anthropic" or "anthropic-messages" => "anthropic-messages",
            _ => "openai-completions",
        };

    static HttpClient SharedHttp { get; } = new() { Timeout = TimeSpan.FromMinutes(5) };
}
