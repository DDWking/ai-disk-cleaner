using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace AiDiskCleaner.Services;

/// <summary>出站通道当前状态，给界面状态灯用。</summary>
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
/// 全进程**唯一**的判定出站闸门：限流 → 超时 → 重试 → 状态记录。
///
/// v3.0 起只剩判定一条通道（聊天通道连同 Node 侧车一起删掉了），所以「选路」没有了，
/// 但另外三件事必须继续留在这里：出站计数（它把「后台悄悄发请求」变成可测的事实）、
/// 限流、瞬时故障重试。调用方（界面、判定执行器）不需要知道下面发生了什么。
/// </summary>
public static class AiGateway
{
    /// <summary>两次请求之间的最小间隔，避免把中转打挂。</summary>
    public static TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>瞬时故障最多重试几次。</summary>
    public const int MaxAttempts = 3;

    private static readonly object Gate = new();
    private static DateTime _lastStart = DateTime.MinValue;

    /// <summary>
    /// 判定通道的发送实现。组合根（<see cref="AiGateways.Register"/>）在生产接线、
    /// 离线测试塞假的 —— 检查因此可以不联网跑完。
    /// </summary>
    public static Func<AiDecisionRequest, CancellationToken, Task<AiDecisionReply>>? DecisionsSender { get; set; }

    public static AiGatewayStatus LastStatus { get; private set; } = AiGatewayStatus.Idle;

    /// <summary>
    /// 出站请求计数（**每一次尝试**都在这里 +1，含失败与重试）。
    ///
    /// 存在的意义是把「后台悄悄发请求」变成可测的事实：回归测试用它断言
    /// 「扫描 / 切页 / 展开 / 筛选 = 0 次请求」，这比翻日志可靠。
    /// 计数点放在重试循环**里面**：否则「重试了 3 次」只会记成 1 次，
    /// 而这条计数的用途正是衡量真的往外发了多少次。
    /// </summary>
    private static int _sentCount;

    /// <summary>本进程内已经发出去的判定请求次数（含失败与重试前的那一次）。</summary>
    public static int SentCount => Volatile.Read(ref _sentCount);

    /// <summary>测试用：清零计数。</summary>
    public static void ResetSentCountForTest() => Volatile.Write(ref _sentCount, 0);

    /// <summary>
    /// 发一次结构化判定。
    ///
    /// 策略与 v3.0 之前一致（同一个出站计数、同一把限流闸、同一套瞬时故障重试与状态记录），
    /// 只是没有聊天时代那层 sidecar 降级 —— 判定只有内置 HTTP 一条路。
    /// </summary>
    public static async Task<AiDecisionReply> DecideAsync(AiDecisionRequest request, CancellationToken ct)
    {
        // 未接线时明确报错，而且**不算一次出站**（什么都没发出去）
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
            // 计数点在这里（不是函数入口）：一次「尝试」才算一次出站，
            // 重试了 3 次就该记 3 次 —— 这条计数存在的意义就是衡量真的往外发了多少。
            Interlocked.Increment(ref _sentCount);
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
}
