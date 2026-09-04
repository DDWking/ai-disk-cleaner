using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AiDiskCleaner.Services;

/// <summary>
/// 接收 sidecar 转发回来的工具调用，在 C# 侧执行。
/// 勾选清理项、删到回收站都要动 C# 状态和 Windows Shell，所以工具必须留在这边，
/// sidecar 只负责「模型想调什么」。
/// </summary>
public sealed class SidecarToolHost : IDisposable
{
    readonly HttpListener _listener = new();
    readonly IAnalystHost _host;
    readonly CancellationTokenSource _cts = new();

    public int Port { get; }

    public SidecarToolHost(IAnalystHost host)
    {
        _host = host;
        Port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _ = Task.Run(ListenLoop);
    }

    static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    async Task ListenLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    void Handle(HttpListenerContext ctx)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = reader.ReadToEnd();

            string name = "";
            string argsJson = "{}";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("name", out var n)) name = n.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("args", out var a))
                    argsJson = a.ValueKind == JsonValueKind.String
                        ? (a.GetString() ?? "{}")
                        : a.GetRawText();
            }
            catch { }

            // 工具要改 UI 状态（勾选项等），回到 UI 线程跑，和原来的工具循环一致
            string result = "";
            var app = System.Windows.Application.Current;
            if (app != null)
                app.Dispatcher.Invoke(() => result = DiskAnalyst.Run(name, argsJson, _host));
            else
                result = DiskAnalyst.Run(name, argsJson, _host);

            Respond(ctx, 200, JsonSerializer.Serialize(new { result }));
        }
        catch (Exception ex)
        {
            try { Respond(ctx, 500, JsonSerializer.Serialize(new { error = ex.Message })); }
            catch { }
        }
    }

    static void Respond(HttpListenerContext ctx, int code, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }
}
