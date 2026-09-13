using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 目录用途识别的编排：本地摘要 → 本地规则 → （必要时）AI → 缓存。
///
/// 预算是**硬限制**，不是建议值：整次任务的请求数、并发、探索深度都有上限，
/// 而且 AI 默认关闭——只有本地说不清、且调用方明确允许时才请求。
/// </summary>
public sealed class FolderPurposeService
{
    /// <summary>整次识别最多发多少次 AI 请求（用完后一律标「待确认」，不再发）。</summary>
    public const int MaxAiRequests = 60;

    /// <summary>同时最多几个 AI 请求。</summary>
    public const int MaxConcurrent = 2;

    /// <summary>单次 AI 超时。</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>两次请求之间的最小间隔（自动批量时别把供应商打爆，也别让界面被回复淹没）。</summary>
    public static readonly TimeSpan MinRequestGap = TimeSpan.FromMilliseconds(250);

    /// <summary>探索深度上限：收纳目录往下看，但不会无限递归。</summary>
    public const int MaxDepth = 4;

    /// <summary>给模型的输入字符上限（含少量脱敏片段）。</summary>
    public const int MaxInputChars = 1800;

    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);
    private readonly Dictionary<string, FolderPurposeResult> _cache = new(StringComparer.Ordinal);
    /// <summary>用户纠正**单独存放**：重扫只清缓存，绝不清用户确认过的结论。</summary>
    private readonly Dictionary<string, FolderPurposeResult> _userCorrections = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private int _aiUsed;
    private DateTime _lastRequestAt = DateTime.MinValue;

    public int AiRequestsUsed { get { lock (_lock) return _aiUsed; } }
    public int CacheCount { get { lock (_lock) return _cache.Count; } }

    /// <summary>换了扫描代次就清缓存：旧扫描的结果不能当成新扫描的结论。</summary>
    public void ResetForScan()
    {
        lock (_lock) { _cache.Clear(); _aiUsed = 0; }
    }

    /// <summary>
    /// 缓存键：稳定目录标识（含扫描代次）+ 摘要指纹。
    /// 摘要指纹把**大小 / 文件数 / 直接子目录数 / 最后修改时间**都算进去，
    /// 所以只有目录内容真的变了缓存才失效；光「展开」不会重复请求。
    /// </summary>
    static string CacheKey(FolderId id, FolderSummary sum, string configSignature)
        => $"{id}\u0001{sum.Size}\u0001{sum.FileCount}\u0001{sum.DirectFolderCount}"
           + $"\u0001{sum.Modified.Ticks}\u0001{sum.KindSignature}\u0001{configSignature}";

    public FolderPurposeResult? TryGetCached(FolderId id, FolderSummary sum, string configSignature)
    {
        lock (_lock)
            return _cache.TryGetValue(CacheKey(id, sum, configSignature), out var v) ? v : null;
    }

    /// <summary>
    /// 用户手动纠正：**永久优先**，不被之后的本地/AI 结果覆盖，也不因重扫（甚至重启）失效。
    ///
    /// 键是**规范化路径**而不是「路径+代次」：用户说的是「这个文件夹是什么」，
    /// 换一代扫描它还是同一个文件夹。
    /// </summary>
    public void SetUserCorrection(FolderId id, string purposeName, string category)
    {
        // Kind 用 Unknown：**用户纠正只改用途，不改目录性质**（集合结构不动）
        var r = new FolderPurposeResult(id, purposeName, category, Loc.PurposeBasisUser,
            PurposeSource.User, NeedsConfirm: false, Kind: FolderKind.Unknown);
        lock (_lock) _userCorrections[UserKey(id.Path)] = r;
    }

    public FolderPurposeResult? TryGetUserCorrection(FolderId id)
    {
        lock (_lock) return _userCorrections.TryGetValue(UserKey(id.Path), out var v) ? v : null;
    }

    static string UserKey(string? path) => "user\u0001" + CleanListSnapshot.NormPath(path).ToLowerInvariant();

    // ---------------- 用户纠正落盘（跨重启仍然有效） ----------------

    /// <summary>
    /// 从磁盘读回用户纠正。**只有路径与用途名**，没有任何机密；
    /// 文件缺失或损坏就当没有（绝不因此打断启动）。目录由 AppPaths 统一决定，
    /// 测试可以把它指到临时目录，不碰真实用户配置。
    /// </summary>
    public void LoadCorrections()
    {
        try
        {
            string file = AppPaths.FolderPurposeFile;
            if (!System.IO.File.Exists(file)) return;
            var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                System.IO.File.ReadAllText(file));
            if (raw == null) return;
            lock (_lock)
            {
                foreach (var kv in raw)
                {
                    if (kv.Value is not { Length: >= 1 } v) continue;
                    string path = CleanListSnapshot.NormPath(v[0]);
                    if (path.Length == 0) continue;
                    string name = v.Length > 1 ? v[1] : "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string cat = v.Length > 2 && v[2].Length > 0 ? v[2] : name;
                    var id = new FolderId(path, 0);
                    _userCorrections[UserKey(path)] = new FolderPurposeResult(
                        id, name, cat, Loc.PurposeBasisUser, PurposeSource.User, false, FolderKind.Unknown);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Record("Purpose", ex, "load corrections");
        }
    }

    /// <summary>把用户纠正写盘。写不了只记日志，不影响本次会话的结果。</summary>
    public void SaveCorrections()
    {
        try
        {
            Dictionary<string, string[]> map = new(StringComparer.Ordinal);
            lock (_lock)
            {
                foreach (var r in _userCorrections.Values)
                    map[r.Id.Path] = new[] { r.Id.Path, r.PurposeName, r.Category };
            }
            AppLog.EnsureDirectory();
            System.IO.File.WriteAllText(AppPaths.FolderPurposeFile,
                System.Text.Json.JsonSerializer.Serialize(map,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Record("Purpose", ex, "save corrections");
        }
    }

    /// <summary>已记住的用户纠正条数（界面/测试用）。</summary>
    public int UserCorrectionCount { get { lock (_lock) return _userCorrections.Count; } }

    /// <summary>识别一个目录。先本地；本地说不清且有预算、且允许时才问 AI。</summary>
    public async Task<FolderPurposeResult> RecognizeAsync(
        FileEntry dir,
        FolderId id,
        int depth,
        string relativePath,
        bool allowAi,
        AiProviderCfg? provider,
        string? model,
        bool sendFullPath,
        string configSignature,
        CancellationToken ct)
    {
        // 用户纠正最高优先级
        if (TryGetUserCorrection(id) is { } user) return user;

        var sum = FolderPurposeRules.Summarize(dir, id, depth, relativePath);

        if (TryGetCached(id, sum, configSignature) is { } hit) return hit;

        // 1) 本地规则（纯内存，不碰磁盘、不发请求）
        var local = FolderPurposeRules.RecognizeLocally(dir, sum);
        if (local.HasConclusion)
        {
            Store(sum, configSignature, local);
            return local;
        }

        // 2) 本地说不清：没有预算或未授权就直接标「待确认」，不假装成功
        if (!allowAi || provider == null)
            return FolderPurposeResult.None(id, local.Kind) with { NeedsConfirm = true };

        if (AiRequestsUsed >= MaxAiRequests)
            return FolderPurposeResult.None(id, local.Kind) with { NeedsConfirm = true };

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();

            // 自动批量时两次请求之间留一点间隔：供应商不会被瞬间打爆，界面也不会被回复淹没
            var gap = MinRequestGap - (DateTime.UtcNow - _lastRequestAt);
            if (gap > TimeSpan.Zero)
                await Task.Delay(gap, ct).ConfigureAwait(false);

            // 送出去之前的最后一道闸：只带脱敏后的白名单片段，绝不带完整文件清单。
            // 自检不过就**如实标失败**（不是「没结论」）—— 不能悄悄跳过、也不能算成功。
            string input = BuildOutboundInput(dir, sum, sendFullPath);
            if (input.Length == 0)
            {
                AppLog.Info("Purpose", $"op=folder-purpose blocked-by-safety name={sum.Name}");
                return FolderPurposeResult.Failure(id, local.Kind);
            }

            lock (_lock) _aiUsed++;
            _lastRequestAt = DateTime.UtcNow;

            using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
            to.CancelAfter(Timeout);

            var turns = new List<AiMsg> { new() { Role = "user", Text = input } };
            var reply = await AiClient.StreamAsync(provider, model, Loc.PurposeAiSystem,
                turns, _ => { }, to.Token, maxTurns: 1).ConfigureAwait(false);

            // 模型回复里若回显了密钥类内容，也不能进缓存、不能进界面
            int scrubHits = 0;
            string safeReply = SourceSnippetReader.Redact(reply.Text, out scrubHits);
            var ai = FolderPurposeRules.ParseAi(safeReply, id, local.Kind);
            if (ai.HasConclusion) ai = ai with { Model = model ?? "" };
            // 只有拿到结论才缓存：**失败 / 超时 / 空回复一律不缓存**，
            // 否则「重试」会直接命中失败结果，永远修不回来。
            if (ai.HasConclusion) Store(sum, configSignature, ai);
            AppLog.Info("Purpose", $"op=folder-purpose name={sum.Name} local=none "
                + $"ai={(ai.HasConclusion ? ai.PurposeName : "none")} depth={depth} "
                + $"requests={AiRequestsUsed} scrubbed={scrubHits}");
            return ai;
        }
        catch (OperationCanceledException)
        {
            // 用户取消 ⇒ 回到「没有结论」（不是失败）；**超时**是失败，两者必须分开报。
            bool timedOut = !ct.IsCancellationRequested;
            var none = FolderPurposeResult.None(id, local.Kind) with { NeedsConfirm = true };
            return timedOut ? FolderPurposeResult.Failure(id, local.Kind) : none;
        }
        catch (Exception ex)
        {
            // 网络不通 / 供应商报错：如实标「失败」，绝不显示成功结论，也不缓存
            AppLog.Record("Purpose", ex, "folder-purpose");
            return FolderPurposeResult.Failure(id, local.Kind);
        }
        finally
        {
            _gate.Release();
        }
    }

    void Store(FolderSummary sum, string configSignature, FolderPurposeResult r)
    {
        lock (_lock) _cache[CacheKey(sum.Id, sum, configSignature)] = r;
    }

    /// <summary>
    /// 组织一次出站输入：受控摘要 + 经白名单与脱敏筛选的少量 README / 项目配置 / 清单片段。
    /// 片段读取**只发生在真的要问模型的时候**（本地规则完全不需要碰磁盘）。
    /// 自检不过就返回空串 —— 宁可不识别，也不把可疑内容发出去。
    /// </summary>
    public static string BuildOutboundInput(FileEntry dir, FolderSummary sum, bool sendFullPath,
        Func<string, string?>? snippetReader = null)
    {
        SnippetSet snippets = SnippetSet.Empty;
        try { snippets = SourceSnippetCollector.Collect(dir.FullPath, snippetReader); }
        catch (Exception ex) { AppLog.Record("Purpose", ex, "collect snippets"); }

        string payload = FolderPurposeRules.BuildAiInput(sum, sendFullPath, MaxInputChars, snippets);
        return FolderPurposeRules.IsSafeOutbound(payload) ? payload : "";
    }

    /// <summary>
    /// 这个目录该不该继续往里识别。
    ///
    /// - 具体对象：**默认停**（不把应用内部逐个贴标签）；
    /// - 收纳/混合：继续，但要用户主动展开；
    /// - 平台容器：**保留往里找游戏的入口**（认出平台不等于忽略内部游戏）；
    /// - 未知：只有还有本地线索时才继续，否则标待确认。
    /// </summary>
    public static bool ShouldDescend(FileEntry dir, FolderKind kind, int depth)
    {
        if (!FolderPurposeRules.CanDescend(dir)) return false;
        if (depth >= MaxDepth) return false;
        if (FolderPurposeRules.IsObjectContainer(dir.FullPath)) return true;
        return kind is FolderKind.Container or FolderKind.Mixed;
    }
}
