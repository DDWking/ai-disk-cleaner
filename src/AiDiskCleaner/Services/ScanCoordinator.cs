using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>一次扫描的结果：树 + 质量报告 + 有没有降级。</summary>
public sealed record ScanOutcome(
    FileEntry Root,
    ScanQuality? Quality,
    bool UsedFallback,
    string FallbackReason,
    Exception? PrimaryError)
{
    public bool IsFallback => UsedFallback;
}

/// <summary>
/// 扫描协调：跑主扫描（MFT），失败就降级到递归扫描，并把失败原因一路带下去。
/// 从 MainWindow 里抽出来是为了让「MFT 挂了会怎样」能单独测，
/// 而不是只能在窗口代码里靠人肉回归。
///
/// 不碰 UI：进度通过 <see cref="IProgress{T}"/> 往外报，取消通过 CancellationToken。
/// </summary>
public sealed class ScanCoordinator
{
    private readonly IScanService _primary;
    private readonly IScanService _fallback;

    /// <summary>
    /// 依赖从构造函数进来，模块内部不自己 new 扫描器 ——
    /// 这样离线测试可以整条换掉（也顺便让本文件不认识 MftScanService）。
    /// </summary>
    public ScanCoordinator(IScanService primary, IScanService fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    /// <summary>上一次是否降级过（界面状态用）。</summary>
    public bool LastUsedFallback { get; private set; }
    public string LastFallbackReason { get; private set; } = "";

    /// <summary>
    /// 扫一个盘。取消原样抛出 <see cref="OperationCanceledException"/>，不当故障。
    /// <paramref name="onFallback"/> 在**开始降级扫描之前**回调，
    /// 让界面能立刻把「MFT 不可用，正在用兼容扫描」显示出来，而不是等扫完才知道。
    /// </summary>
    public async Task<ScanOutcome> RunAsync(
        string drive,
        IProgress<ScanProgress>? progress,
        CancellationToken ct,
        Action<string>? onFallback = null)
    {
        try
        {
            using var op = AppLog.Begin("Scan");
            op.Stage("mft", drive);
            var root = await Task.Run(() => _primary.Scan(drive, progress, ct), ct);
            var quality = _primary.LastQuality;
            LastUsedFallback = false;
            LastFallbackReason = "";
            op.Done("mft", root.FileCount);
            return new ScanOutcome(root, quality, false, "", null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string reason = Shorten(AppError.RootMessage(ex));
            AppLog.Record("Scan", ex, "mft failed, falling back to recursive");
            LastUsedFallback = true;
            LastFallbackReason = reason;
            onFallback?.Invoke(reason);

            using var op = AppLog.Begin("Scan");
            op.Stage("recursive", drive);
            var root = await Task.Run(() => _fallback.Scan(drive, progress, ct), ct);
            var quality = _fallback.LastQuality;
            op.Done("recursive", root.FileCount);
            return new ScanOutcome(root, quality, true, reason, ex);
        }
    }

    /// <summary>不降级的单次扫描（卸载后复扫用）。</summary>
    public Task<ScanOutcome> RunAgainAsync(
        string drive,
        IProgress<ScanProgress>? progress,
        CancellationToken ct,
        Action<string>? onFallback = null)
        => RunAsync(drive, progress, ct, onFallback);

    public static string Shorten(string? message)
    {
        message = string.IsNullOrWhiteSpace(message) ? "未知原因" : message.Trim();
        return message.Length <= 180 ? message : message[..177] + "…";
    }
}
