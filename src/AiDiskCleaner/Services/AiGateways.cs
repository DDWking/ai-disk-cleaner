using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>走 Pi sidecar（pi-ai 负责中转协议 / 推理内容 / 工具循环）。</summary>
public sealed class SidecarAiGateway : IAiGateway
{
    public string Name => "sidecar";

    public async Task<AiReply> SendAsync(AiRequest request, Action<string>? onDelta, CancellationToken ct)
    {
        if (request.Provider == null || string.IsNullOrWhiteSpace(request.Model))
            throw new InvalidOperationException(Loc.AiNeedConfig);

        // 冷启动放线程池：进程启动 + 等 READY 可能几十秒，UI 线程不能停在这。
        bool up = SidecarClient.IsRunning || await Task.Run(SidecarClient.EnsureStarted, ct);
        if (!up) throw new InvalidOperationException("sidecar unavailable");

        string text = await SidecarClient.ChatAsync(
            request.Provider, request.Model!, request.System, request.Turns,
            onDelta ?? (_ => { }), request.MaxTurns, ct);
        return new AiReply { Text = text };
    }
}

/// <summary>走内置 HTTP（OpenAI.NET + 手写 Responses / Anthropic）。</summary>
public sealed class DirectHttpAiGateway : IAiGateway
{
    public string Name => "http";

    public Task<AiReply> SendAsync(AiRequest request, Action<string>? onDelta, CancellationToken ct)
        => AiClient.SendDirectAsync(request, onDelta, ct);
}

/// <summary>生产环境的通道提供者：sidecar 是否启用看用户设置。</summary>
public sealed class DefaultAiGatewayProvider : IAiGatewayProvider
{
    private readonly SidecarAiGateway _sidecar = new();
    private readonly DirectHttpAiGateway _direct = new();

    public bool SidecarEnabled => App.Settings.AiUseSidecar;
    public IAiGateway Sidecar => _sidecar;
    public IAiGateway Direct => _direct;
}

/// <summary>
/// 组合根：把真实通道接上统一入口。
/// <see cref="AiGateway"/> 本身不认识 sidecar / HTTP，所以这一句是唯一的接线点。
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
            AiGateway.ProviderFactory ??= static () => new DefaultAiGatewayProvider();
            // 判定通道只有内置 HTTP 一条路：sidecar 是 chat 协议的适配器，没有 decisions 原语。
            AiGateway.DecisionsSender ??= static (req, ct) => AiClient.SendDecisionsAsync(req, ct);
            _registered = true;
        }
    }
}
