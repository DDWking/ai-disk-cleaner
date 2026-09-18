using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>一次 AI 请求，和具体通道（sidecar / 内置 HTTP）无关。</summary>
public sealed class AiRequest
{
    public AiProviderCfg? Provider { get; set; }
    public string? Model { get; set; }
    public string System { get; set; } = "";
    public IReadOnlyList<AiMsg> Turns { get; set; } = Array.Empty<AiMsg>();
    public IReadOnlyList<object>? Tools { get; set; }
    /// <summary>工具循环最多几轮（sidecar 用）。</summary>
    public int MaxTurns { get; set; } = 4;
    /// <summary>整个请求的墙钟上限（含重试）。</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(150);
}

/// <summary>
/// AI 通道抽象。主界面只跟这一层打交道，不关心走 sidecar 还是内置 HTTP、
/// 也不关心有没有 fallback。想换实现（比如换成托管服务）只要再写一个实现。
/// </summary>
public interface IAiGateway
{
    /// <summary>通道名，用于日志和界面状态。</summary>
    string Name { get; }
    Task<AiReply> SendAsync(AiRequest request, Action<string>? onDelta, CancellationToken ct);
}

/// <summary>
/// 通道提供者。统一入口只认这个接口，不直接 new 具体实现 ——
/// 这样离线测试可以整条换掉，也顺便挡住了「主界面自己去判断走哪条路」。
/// </summary>
public interface IAiGatewayProvider
{
    /// <summary>用户是否开启了 sidecar。</summary>
    bool SidecarEnabled { get; }
    /// <summary>Pi sidecar 通道。</summary>
    IAiGateway Sidecar { get; }
    /// <summary>内置 HTTP 通道。</summary>
    IAiGateway Direct { get; }
}

/// <summary>AI 通道当前状态，给界面状态灯用。</summary>
public sealed record AiGatewayStatus(
    string Channel,
    bool Ok,
    int Attempts,
    double ElapsedMs,
    string Message)
{
    public static readonly AiGatewayStatus Idle = new("", true, 0, 0, "");
}

/// <summary>
/// 统一入口：选路 → 超时 → 重试 → 限流 → fallback → 状态记录。
/// 调用方（含主界面）不需要知道下面发生了什么。
/// </summary>
public static class AiGateway
{
    /// <summary>两次请求之间的最小间隔，避免把中转打挂。</summary>
    public static TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>瞬时故障最多重试几次。</summary>
    public const int MaxAttempts = 3;

    private static readonly object Gate = new();
    private static DateTime _lastStart = DateTime.MinValue;
    private static IAiGatewayProvider? _provider;

    /// <summary>
    /// 组合根注入：第一次需要通道时才调用，返回真实实现。
    /// 这样本文件不需要引用 SidecarClient / AiClient，能被离线测试完整覆盖。
    /// </summary>
    public static Func<IAiGatewayProvider>? ProviderFactory { get; set; }

    /// <summary>测试可以直接塞一个假提供者；生产走 <see cref="ProviderFactory"/>。</summary>
    public static IAiGatewayProvider? Provider
    {
        get => _provider;
        set => _provider = value;
    }

    /// <summary>
    /// 结构化判定通道（<see cref="AiDecisionRequest"/>）。
    ///
    /// 单独一条而不是塞进 <see cref="IAiGatewayProvider"/>，有两个原因：
    /// sidecar 是 chat 协议的适配器、根本不支持判定；而加接口成员会强迫所有离线测试桩跟着改。
    /// 做成和 <see cref="ProviderFactory"/> 一样的注入点，生产在组合根接线、测试塞假的就行。
    /// </summary>
    public static Func<AiDecisionRequest, CancellationToken, Task<AiDecisionReply>>? DecisionsSender { get; set; }

    public static AiGatewayStatus LastStatus { get; private set; } = AiGatewayStatus.Idle;

    private static IAiGatewayProvider Resolve()
    {
        if (_provider != null) return _provider;
        var factory = ProviderFactory
                      ?? throw new InvalidOperationException("AI gateway provider is not registered");
        _provider = factory();
        return _provider;
    }

    /// <summary>
    /// 出站请求计数（**所有**走网关的请求都在这里 +1，sidecar 与内置 HTTP 都算）。
    ///
    /// 存在的意义是把「后台悄悄发请求」变成可测的事实：回归测试用它断言
    /// 「扫描 / 切页 / 展开 / 筛选 = 0 次请求」，这比翻日志可靠。
    /// </summary>
    private static int _sentCount;

    /// <summary>本进程内已经发出去的模型请求次数（含失败与重试前的那一次）。</summary>
    public static int SentCount => Volatile.Read(ref _sentCount);

    /// <summary>测试用：清零计数。</summary>
    public static void ResetSentCountForTest() => Volatile.Write(ref _sentCount, 0);

    /// <summary>发一次请求。onDelta 可为空（整段模式）。</summary>
    public static async Task<AiReply> SendAsync(AiRequest request, Action<string>? onDelta, CancellationToken ct)
    {
        Interlocked.Increment(ref _sentCount);
        var provider = Resolve();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(request.Timeout);

        int attempts = 0;
        Exception? last = null;

        // 1) 优先 sidecar（如果用户开着）
        if (provider.SidecarEnabled)
        {
            try
            {
                await ThrottleAsync(timeoutCts.Token);
                attempts++;
                var reply = await provider.Sidecar.SendAsync(request, onDelta, timeoutCts.Token);
                if (!string.IsNullOrEmpty(reply.Text) || reply.HasTools)
                {
                    sw.Stop();
                    Status(new AiGatewayStatus(provider.Sidecar.Name, true, attempts, sw.Elapsed.TotalMilliseconds, "ok"));
                    return reply;
                }
                // 空回复也算失败，继续走内置通道
                AppLog.Warn("Ai", "sidecar returned an empty reply; falling back to built-in HTTP");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Warn("sidecar failed, falling back to built-in HTTP", ex);
            }
        }

        // 2) 内置 HTTP，带重试
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            attempts++;
            try
            {
                await ThrottleAsync(timeoutCts.Token);
                var reply = await provider.Direct.SendAsync(request, onDelta, timeoutCts.Token);
                sw.Stop();
                Status(new AiGatewayStatus(provider.Direct.Name, true, attempts, sw.Elapsed.TotalMilliseconds, "ok"));
                return reply;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // 超时（不是用户取消）：可以重试
                last = ex;
                if (attempt >= MaxAttempts) break;
                await DelayAsync(attempt, ct);
            }
            catch (Exception ex)
            {
                last = ex;
                if (!IsTransient(ex) || attempt >= MaxAttempts) break;
                AppLog.Warn("Ai", $"transient failure (attempt {attempt}/{MaxAttempts}): {AppError.From(ex).Kind}");
                await DelayAsync(attempt, ct);
            }
        }

        sw.Stop();
        var err = last ?? new InvalidOperationException("ai request failed");
        Status(new AiGatewayStatus(provider.Direct.Name, false, attempts, sw.Elapsed.TotalMilliseconds,
            AppError.From(err).UserMessage));
        throw err is OperationCanceledException
            ? new TimeoutException(Loc.AiTimeout, err)
            : err;
    }

    /// <summary>
    /// 发一次结构化判定。
    ///
    /// 策略和 <see cref="SendAsync"/> 刻意保持一致（同一个出站计数、同一把限流闸、
    /// 同一套瞬时故障重试与状态记录），差别只在没有 sidecar 降级那一层 ——
    /// 判定通道只有内置 HTTP 一条路。
    /// </summary>
    public static async Task<AiDecisionReply> DecideAsync(AiDecisionRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _sentCount);
        var send = DecisionsSender
                   ?? throw new InvalidOperationException("decisions channel is not registered");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(request.Timeout);

        int attempts = 0;
        Exception? last = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            attempts++;
            try
            {
                await ThrottleAsync(timeoutCts.Token);
                var reply = await send(request, timeoutCts.Token);
                sw.Stop();
                Status(new AiGatewayStatus("decisions", true, attempts, sw.Elapsed.TotalMilliseconds, "ok"));
                return reply;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                last = ex;
                if (attempt >= MaxAttempts) break;
                await DelayAsync(attempt, ct);
            }
            catch (Exception ex)
            {
                last = ex;
                if (!IsTransient(ex) || attempt >= MaxAttempts) break;
                AppLog.Warn("Ai", $"decisions transient failure (attempt {attempt}/{MaxAttempts}): {AppError.From(ex).Kind}");
                await DelayAsync(attempt, ct);
            }
        }

        sw.Stop();
        var err = last ?? new InvalidOperationException("decisions request failed");
        Status(new AiGatewayStatus("decisions", false, attempts, sw.Elapsed.TotalMilliseconds,
            AppError.From(err).UserMessage));
        throw err is OperationCanceledException
            ? new TimeoutException(Loc.AiTimeout, err)
            : err;
    }

    /// <summary>瞬时故障才值得重试：网络断/超时/429/5xx。401、403、400 重试没意义。</summary>
    public static bool IsTransient(Exception? ex)
    {
        if (ex == null) return false;
        if (ex is TimeoutException or SocketException) return true;

        if (ex is HttpRequestException http)
        {
            if (http.StatusCode is { } code)
            {
                int c = (int)code;
                return c == 408 || c == 429 || c >= 500;
            }
            return true; // 没有状态码 = 连接层失败
        }

        if (ex is TaskCanceledException) return true;

        string m = (ex.Message + " " + (ex.InnerException?.Message ?? "")).ToLowerInvariant();
        return m.Contains("timeout") || m.Contains("timed out")
            || m.Contains("429") || m.Contains("502") || m.Contains("503") || m.Contains("504");
    }

    private static async Task DelayAsync(int attempt, CancellationToken ct)
    {
        // 400ms / 1200ms 退避
        int ms = attempt == 1 ? 400 : 1200;
        await Task.Delay(ms, ct);
    }

    /// <summary>简单限流：两次请求启动之间至少 <see cref="MinInterval"/>。</summary>
    private static async Task ThrottleAsync(CancellationToken ct)
    {
        TimeSpan wait;
        lock (Gate)
        {
            var since = DateTime.UtcNow - _lastStart;
            wait = MinInterval - since;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            _lastStart = DateTime.UtcNow + wait;
        }
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
    }

    private static void Status(AiGatewayStatus status)
    {
        LastStatus = status;
        AppLog.Info("Ai", $"channel={status.Channel} ok={status.Ok} attempts={status.Attempts} ms={status.ElapsedMs:0}");
    }

    private static void Warn(string message, Exception ex)
    {
        // 只记异常类型与分类，绝不记消息里可能夹带的密钥
        AppLog.Warn("Ai", message + " | " + ex.GetType().Name + " " + AppError.From(ex).Kind);
    }
}
