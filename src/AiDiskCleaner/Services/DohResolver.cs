using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace AiDiskCleaner.Services;

/// <summary>
/// DNS over HTTPS 解析。
///
/// <b>存在的理由（真机故障）：</b>部分网络的系统 DNS 会对判定端点做污染 ——
/// <c>openrouter.ai</c> 被解析到伪造 IP，连过去拿到的证书不受信任
/// （<c>SEC_E_UNTRUSTED_ROOT</c>），连接要么当场失败、要么挂到超时。
/// 用户看到的是「连接超时」，于是反复改密钥和模型，而真正的毛病在 DNS。
///
/// <b>为什么这是安全的：</b>DoH 只用来决定「连哪个 IP」，TLS 证书仍然按<b>原始主机名</b>
/// 校验。所以即使 DoH 应答本身被篡改，攻击者也无法完成中间人 —— 证书会校验失败。
/// 这里不关掉任何证书检查。
///
/// 只在系统解析不可信时兜底：先用 DoH，拿不到就退回系统 DNS（企业内网域名因此照常工作）。
/// </summary>
public static class DohResolver
{
    /// <summary>
    /// 按顺序尝试的 DoH 端点。全部写成 IP 字面量：这些请求本身不能依赖系统 DNS，
    /// 否则又会被同一个污染喂回伪造结果。
    /// </summary>
    static readonly string[] Endpoints =
    {
        "https://223.5.5.5/resolve?name={0}&type=A",     // AliDNS
        "https://1.1.1.1/dns-query?name={0}&type=A",      // Cloudflare
        "https://8.8.8.8/resolve?name={0}&type=A",        // Google
    };

    /// <summary>解析结果缓存（带过期），避免每条连接都问一次 DoH。</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry> Cache = new();

    sealed record CacheEntry(IPAddress[] Addresses, DateTime ExpiresUtc);

    static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>单个 DoH 端点的超时。整体预算靠端点数量兜住，避免拖慢正常请求。</summary>
    static readonly TimeSpan PerEndpointTimeout = TimeSpan.FromSeconds(5);

    /// <summary>DoH 专用客户端：连的是 IP 字面量，所以不需要自定义解析（否则会递归）。</summary>
    static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AutomaticDecompression = DecompressionMethods.All,
    })
    { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>
    /// 解析主机名的 IPv4 地址。全部端点失败时返回空列表（由调用方决定回退策略）。
    /// </summary>
    public static async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) return Array.Empty<IPAddress>();

        if (Cache.TryGetValue(host, out var hit) && hit.ExpiresUtc > DateTime.UtcNow)
            return hit.Addresses;

        foreach (var template in Endpoints)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string url = string.Format(template, Uri.EscapeDataString(host));
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(PerEndpointTimeout);

                using var res = await Http.GetAsync(url, timeout.Token);
                if (!res.IsSuccessStatusCode) continue;

                string body = await res.Content.ReadAsStringAsync(timeout.Token);
                var ips = ParseAddresses(body);
                if (ips.Count == 0) continue;

                Cache[host] = new CacheEntry(ips.ToArray(), DateTime.UtcNow + CacheTtl);
                AppLog.Info("Doh", $"resolved {host} via {new Uri(url).Host} -> {string.Join(",", ips)}");
                return ips;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单个端点不通就换下一个；全部不通时退回系统 DNS。
                AppLog.Warn("Doh", $"endpoint failed ({new Uri(template.Replace("{0}", host)).Host}): {AppError.From(ex).Kind}");
            }
        }

        return Array.Empty<IPAddress>();
    }

    /// <summary>
    /// 解析 DoH JSON 应答。AliDNS / Cloudflare / Google 用同一个形状：
    /// <c>{"Status":0,"Answer":[{"type":1,"data":"104.18.3.115"}]}</c>。
    /// 纯函数，离线可测。
    /// </summary>
    public static List<IPAddress> ParseAddresses(string? json)
    {
        var result = new List<IPAddress>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Status != 0 表示解析失败（NXDOMAIN / SERVFAIL），不能当成有效答案。
            if (root.TryGetProperty("Status", out var status)
                && status.ValueKind == JsonValueKind.Number
                && status.GetInt32() != 0)
            {
                return result;
            }

            if (!root.TryGetProperty("Answer", out var answer) || answer.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in answer.EnumerateArray())
            {
                // 只要 A 记录（type=1）；AAAA 不取，避免在只有 v4 出口的网络里连不上。
                if (!item.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.Number
                    || type.GetInt32() != 1) continue;

                if (!item.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String) continue;

                string? text = data.GetString();
                if (IPAddress.TryParse(text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                    result.Add(ip);
            }
        }
        catch (JsonException)
        {
            // 应答不是 JSON（门户劫持页等）：当作解析失败，交给下一个端点。
        }

        return result;
    }

    /// <summary>该地址是不是本机回环 / 局域网地址（代理端点、内网服务不该走 DoH）。</summary>
    public static bool IsLocalOrPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;

        byte[] b = ip.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254);
    }
}
