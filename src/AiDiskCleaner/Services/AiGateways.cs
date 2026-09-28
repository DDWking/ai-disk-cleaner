namespace AiDiskCleaner.Services;

/// <summary>
/// 组合根：把真实判定通道接上统一入口。
///
/// <see cref="AiGateway"/> 本身不认识 HTTP，所以这一句是**唯一的接线点**。
/// v3.0 之前这里还要接 sidecar / 内置 HTTP 两条聊天通道，聊天删掉之后只剩判定一条 ——
/// 但接线本身必须留着：判定通道走的是设置读取（刻意不碰 <see cref="AiClient"/> 的静态构造），
/// 只靠静态构造会在「从没碰过 AiClient」的进程里漏接线，用户一点识别就报
/// "decisions channel is not registered"（真机日志里连报 4 次）。
/// </summary>
public static class AiGateways
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered) return;
            AiGateway.DecisionsSender ??= static (req, ct) => AiClient.SendDecisionsAsync(req, ct);
            _registered = true;
        }
    }
}
