using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;

namespace AiDiskCleaner.Services;

/// <summary>
/// 全进程共享的出站 HTTP 客户端。
///
/// 两处用到它：判定通道（<see cref="AiClient"/>）和 agent 的聊天通道
/// （<see cref="Agent.OpenAiCompatChatModel"/>）。TLS 版本、DoH 兜底、
/// 连接超时只在这里定义一次，避免两条通道的行为悄悄分叉。
///
/// DoH 只负责「域名解析成哪个 IP」；TLS 仍按**原始主机名**校验，
/// 所以 DNS 被污染时这条路径既能把请求送到正确服务器，也不降低安全性。
/// </summary>
internal static class AiHttp
{
    static HttpClient? _client;
    static readonly object Gate = new();

    internal static HttpClient Client
    {
        get
        {
            if (_client != null) return _client;
            lock (Gate)
            {
                return _client ??= Create();
            }
        }
    }

    internal static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            },
            ConnectCallback = ConnectAsync,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
    }

    /// <summary>
    /// 建立 TCP 连接。默认用 DoH 解析（正常网络也返回正确结果），
    /// DoH 不可用时退回系统 DNS —— 企业内网域名因此不会被这条路径打断。
    /// </summary>
    static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;
        string host = endpoint.Host;

        // 字面量地址（含代理端点）不需要解析。
        if (IPAddress.TryParse(host, out var literal))
            return await OpenSocketAsync(new IPEndPoint(literal, endpoint.Port), ct);

        var candidates = await DohResolver.ResolveAsync(host, ct);
        if (candidates.Count == 0)
        {
            var system = await Dns.GetHostAddressesAsync(host, ct);
            candidates = system.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToList();
            if (candidates.Count == 0) candidates = system.ToList();
        }

        Exception? last = null;
        foreach (var ip in candidates)
        {
            try
            {
                return await OpenSocketAsync(new IPEndPoint(ip, endpoint.Port), ct);
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                // 某个地址不通就试下一个：被污染的答案往往是「能连但证书不对」或干脆黑洞。
                last = ex;
            }
        }

        throw last ?? new SocketException((int)SocketError.HostUnreachable);
    }

    static async ValueTask<Stream> OpenSocketAsync(IPEndPoint endpoint, CancellationToken ct)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        try
        {
            await socket.ConnectAsync(endpoint, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
