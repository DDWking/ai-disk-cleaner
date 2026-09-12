using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services.CleanRules;

/// <summary>重复检测的进度。阶段名 + 已处理文件数 + 已读字节 + 已找到的组数。</summary>
public sealed record DuplicateProgress(
    string Phase,
    int FilesProcessed,
    int TotalCandidates,
    long BytesRead,
    int GroupsFound,
    int Percent,
    double ElapsedMs);

/// <summary>一次重复检测的结果。带「有没有跑完」的明确标记。</summary>
public sealed class DuplicateScanResult
{
    public List<DuplicateGroup> Groups { get; init; } = new();
    public DuplicateStats Stats { get; init; } = new();
    /// <summary>是否完整跑完（没被取消、没撞预算、没被条数上限截断）。</summary>
    public bool Complete { get; init; }
    /// <summary>撞到了完整哈希的字节/条数预算，提前收手。</summary>
    public bool BudgetExhausted { get; init; }
    /// <summary>被条数上限截断（结果仍然有效，只是没穷尽）。</summary>
    public bool Truncated { get; init; }

    public string Note => Complete
        ? ""
        : BudgetExhausted ? "budget-exhausted"
        : Truncated ? "truncated"
        : "incomplete";
}

/// <summary>重复文件检测的统计，用于界面和日志解释「到底算了多少」。</summary>
public sealed class DuplicateStats
{
    public int Candidates { get; set; }
    public int SizeGroups { get; set; }
    public int HeadHashes { get; set; }
    public int TailHashes { get; set; }
    public int FullHashes { get; set; }
    /// <summary>完整哈希读过的字节。会超过 int，必须是 long。</summary>
    public long BytesRead { get; set; }
    public int SkippedCloud { get; set; }
    public int SkippedNetwork { get; set; }
    public int SkippedInUse { get; set; }
    public int SkippedHardLink { get; set; }
    public int SkippedErrors { get; set; }
    /// <summary>读到一半文件就变了/变短了，哈希不作数 —— 不算已验证。</summary>
    public int SkippedChanged { get; set; }
    public long ElapsedMs { get; set; }
    public long BudgetBytes { get; set; }

    public override string ToString()
        => $"cand={Candidates} sizeGroups={SizeGroups} head={HeadHashes} tail={TailHashes} full={FullHashes} "
         + $"bytes={BytesRead}/{BudgetBytes} cloud={SkippedCloud} net={SkippedNetwork} busy={SkippedInUse} "
         + $"hardlink={SkippedHardLink} changed={SkippedChanged} err={SkippedErrors} ms={ElapsedMs}";
}

public sealed class DuplicateGroup
{
    public required long Size { get; init; }
    public required List<FileEntry> Members { get; init; }
}

/// <summary>
/// 分阶段重复文件检测：按大小分组 → 首段哈希 → 尾段哈希 → 完整哈希。
/// 越往后越贵，只有活过上一阶段的候选才会进入下一阶段。
///
/// 边界情况：
/// - **硬链接**：同一个物理文件的两个路径不是重复文件，删一个不释放空间，合并成一个代表；
/// - **正在使用**：打不开就跳过并计数，不报错；
/// - **云占位文件**：读到内容会把文件从云端拉下来，一律不碰；
/// - **网络盘**：按内容哈希要过 SMB，慢且不稳，整盘跳过；
/// - **读到一半变了**：长度对不上就丢进 SkippedChanged，**不算已验证重复**。
///
/// 取消与预算：
/// - 分组循环、身份探测、每个文件、**每个读取块**都检查取消；
/// - 预算是**按块扣减**的，单个超大文件不会一口气冲过预算；
/// - 撞预算会明确返回 <see cref="DuplicateScanResult.BudgetExhausted"/>，
///   绝不假装「全部检测完了」。
/// </summary>
public static class DuplicateDetector
{
    /// <summary>参与重复检测的最小大小。太小的文件哈希开销大于收益。</summary>
    public const long MinSize = 8L * 1024 * 1024;

    /// <summary>首段/尾段读多少字节。</summary>
    public const int SampleBytes = 256 * 1024;

    /// <summary>完整哈希的总读取预算，防止在大盘上跑太久。</summary>
    public const long DefaultBudgetBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>完整哈希的候选数量上限。</summary>
    public const int MaxFullHashes = 200;

    /// <summary>进度合并间隔：不要每读一块就回报一次。</summary>
    public const int ProgressIntervalMs = 300;

    /// <summary>读盘块大小。取消检查就发生在块与块之间。</summary>
    private const int ReadChunk = 1024 * 1024;

    /// <summary>卷类型查询缓存：同一个盘符只问一次。</summary>
    private static readonly ConcurrentDictionary<string, bool> NetworkVolumeCache = new(StringComparer.OrdinalIgnoreCase);

    public static DuplicateScanResult Find(
        IReadOnlyList<FileEntry> files,
        Func<string, FileIdentity?>? identityProbe,
        CancellationToken ct,
        IProgress<DuplicateProgress>? progress = null,
        long budgetBytes = DefaultBudgetBytes,
        int maxFullHashes = MaxFullHashes)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stats = new DuplicateStats { BudgetBytes = budgetBytes };
        var result = new List<DuplicateGroup>();
        bool budgetExhausted = false;
        bool truncated = false;
        var throttle = new ProgressThrottle(progress, ProgressIntervalMs);

        // ---- 阶段 0：按大小分组 ----
        var bySize = new Dictionary<long, List<FileEntry>>();
        int scanned = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            scanned++;
            if (stats.Candidates % 512 == 0)
                throttle.Report("group", scanned, files.Count, 0, stats, 0, sw);

            if (!CleanRuleHelpers.CanList(f) || f.Size < MinSize) continue;
            if (string.IsNullOrEmpty(f.FullPath)) continue;
            if (IsCloudPlaceholder(f)) { stats.SkippedCloud++; continue; }
            if (IsOnNetworkDrive(f.FullPath)) { stats.SkippedNetwork++; continue; }
            if (!bySize.TryGetValue(f.Size, out var list))
            {
                list = new List<FileEntry>();
                bySize[f.Size] = list;
            }
            list.Add(f);
            stats.Candidates++;
        }

        var work = bySize.Where(x => x.Value.Count >= 2).OrderByDescending(x => x.Key).ToList();
        int groupIndex = 0;

        foreach (var kv in work)
        {
            ct.ThrowIfCancellationRequested();
            stats.SizeGroups++;
            groupIndex++;
            throttle.Report("hash", groupIndex, work.Count, stats.BytesRead, stats, result.Count, sw);

            long size = kv.Key;
            // ---- 硬链接合并：同一物理文件只留一个代表 ----
            var candidates = CollapseHardLinks(kv.Value, identityProbe, stats, ct);

            // ---- 阶段 1：首段哈希 ----
            var afterHead = GroupByHash(candidates, size, HashPart.Head, ct, stats,
                budgetBytes, maxFullHashes, ref budgetExhausted, ref truncated, throttle, result.Count, sw);
            if (budgetExhausted) break;

            // ---- 阶段 2：尾段哈希 ----
            foreach (var headGroup in afterHead.Where(g => g.Count >= 2))
            {
                ct.ThrowIfCancellationRequested();
                var afterTail = GroupByHash(headGroup, size, HashPart.Tail, ct, stats,
                    budgetBytes, maxFullHashes, ref budgetExhausted, ref truncated, throttle, result.Count, sw);
                if (budgetExhausted) break;

                // ---- 阶段 3：完整哈希 ----
                foreach (var tailGroup in afterTail.Where(g => g.Count >= 2))
                {
                    ct.ThrowIfCancellationRequested();
                    var afterFull = GroupByHash(tailGroup, size, HashPart.Full, ct, stats,
                        budgetBytes, maxFullHashes, ref budgetExhausted, ref truncated, throttle, result.Count, sw);
                    if (budgetExhausted) break;

                    // 只有完整哈希对上的组才算「已验证重复」
                    foreach (var fullGroup in afterFull.Where(g => g.Count >= 2))
                        result.Add(new DuplicateGroup { Size = size, Members = FullGroupOrder(fullGroup) });
                }
                if (budgetExhausted) break;
            }
            if (budgetExhausted) break;
        }

        sw.Stop();
        stats.ElapsedMs = sw.ElapsedMilliseconds;

        // 收尾一定要报一次，否则界面永远停在最后一个节流点
        progress?.Report(new DuplicateProgress(
            budgetExhausted ? "budget-exhausted" : "done",
            Math.Max(groupIndex, stats.SizeGroups), Math.Max(work.Count, 1),
            stats.BytesRead, result.Count, 100, sw.Elapsed.TotalMilliseconds));

        return new DuplicateScanResult
        {
            Groups = result,
            Stats = stats,
            Complete = !budgetExhausted && !truncated,
            BudgetExhausted = budgetExhausted,
            Truncated = truncated,
        };
    }

    /// <summary>把进度按间隔合并，别让 UI 被每秒上千次回报压死。</summary>
    private sealed class ProgressThrottle
    {
        private readonly IProgress<DuplicateProgress>? _sink;
        private readonly int _intervalMs;
        private long _last;

        public ProgressThrottle(IProgress<DuplicateProgress>? sink, int intervalMs)
        {
            _sink = sink;
            _intervalMs = intervalMs;
        }

        public void Report(string phase, int processed, int total, long bytes,
            DuplicateStats stats, int groups, System.Diagnostics.Stopwatch sw)
        {
            if (_sink == null) return;
            long now = sw.ElapsedMilliseconds;
            if (now - _last < _intervalMs) return;
            _last = now;
            int pct = total > 0 ? (int)Math.Clamp(processed * 100L / total, 0, 99) : 0;
            _sink.Report(new DuplicateProgress(phase, processed, total, bytes, groups, pct, now));
        }
    }

    /// <summary>
    /// 硬链接：同一 (卷序列号, file id) 只保留一个代表。
    /// 没有探针信息时原样返回。
    /// </summary>
    private static List<FileEntry> CollapseHardLinks(
        List<FileEntry> input, Func<string, FileIdentity?>? identityProbe,
        DuplicateStats stats, CancellationToken ct)
    {
        if (identityProbe == null) return input;

        var seen = new Dictionary<FileIdentity, FileEntry>();
        var kept = new List<FileEntry>(input.Count);
        int n = 0;
        foreach (var f in input)
        {
            // 身份探测要问盘，成百上千次，中间必须能取消
            if ((++n & 0x3F) == 0) ct.ThrowIfCancellationRequested();

            FileIdentity? id = null;
            try { id = identityProbe(f.FullPath); }
            catch (OperationCanceledException) { throw; }
            catch { }

            if (id is { IsKnown: true })
            {
                if (seen.ContainsKey(id.Value))
                {
                    stats.SkippedHardLink++;
                    continue; // 同一物理文件，不是重复文件
                }
                seen[id.Value] = f;
            }
            kept.Add(f);
        }
        return kept;
    }

    private static List<FileEntry> FullGroupOrder(List<FileEntry> group)
        => group.OrderBy(x => x.FullPath.Length).ThenBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase).ToList();

    private enum HashPart { Head, Tail, Full }

    private static List<List<FileEntry>> GroupByHash(
        List<FileEntry> input, long size, HashPart part, CancellationToken ct,
        DuplicateStats stats, long budgetBytes, int maxFullHashes,
        ref bool budgetExhausted, ref bool truncated,
        ProgressThrottle throttle, int groupsSoFar, System.Diagnostics.Stopwatch sw)
    {
        var groups = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);
        if (budgetExhausted) return groups.Values.ToList();

        foreach (var f in input)
        {
            ct.ThrowIfCancellationRequested();

            if (part == HashPart.Full)
            {
                if (stats.FullHashes >= maxFullHashes) { truncated = true; return groups.Values.ToList(); }
                if (stats.BytesRead >= budgetBytes) { budgetExhausted = true; return groups.Values.ToList(); }
            }

            string? hash = TryHash(f.FullPath, size, part, ct, stats, budgetBytes, ref budgetExhausted, out bool inUse, out bool changed);
            if (budgetExhausted) return groups.Values.ToList();
            if (inUse) { stats.SkippedInUse++; continue; }
            if (changed) { stats.SkippedChanged++; continue; }
            if (hash == null) { stats.SkippedErrors++; continue; }

            switch (part)
            {
                case HashPart.Head: stats.HeadHashes++; break;
                case HashPart.Tail: stats.TailHashes++; break;
                default: stats.FullHashes++; break;
            }

            if (!groups.TryGetValue(hash, out var g))
            {
                g = new List<FileEntry>();
                groups[hash] = g;
            }
            g.Add(f);
        }

        throttle.Report(part switch
        {
            HashPart.Head => "head",
            HashPart.Tail => "tail",
            _ => "full",
        }, 0, 0, stats.BytesRead, stats, groupsSoFar, sw);

        return groups.Values.ToList();
    }

    /// <summary>
    /// 算一段哈希。
    /// - <paramref name="inUse"/>：被独占，跳过；
    /// - <paramref name="changed"/>：读到一半长度对不上，**不作数**；
    /// - 返回 null 且两者都为 false 表示读失败。
    /// 完整哈希按块读并**逐块扣预算 + 逐块检查取消**。
    /// </summary>
    private static string? TryHash(
        string path, long size, HashPart part, CancellationToken ct,
        DuplicateStats stats, long budgetBytes, ref bool budgetExhausted,
        out bool inUse, out bool changed)
    {
        inUse = false;
        changed = false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (part == HashPart.Full)
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buf = new byte[Math.Min(ReadChunk, SampleBytes * 4)];
                long remaining = size;
                while (remaining > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    int toRead = (int)Math.Min(buf.Length, remaining);
                    int read = fs.Read(buf, 0, toRead);
                    if (read <= 0)
                    {
                        // 文件被截短了：这份哈希不能当"已验证"
                        changed = true;
                        return null;
                    }

                    // 逐块扣预算：单个超大文件也不能一口气冲过预算
                    if (stats.BytesRead + read > budgetBytes)
                    {
                        budgetExhausted = true;
                        return null;
                    }
                    stats.BytesRead += read;

                    sha.AppendData(buf, 0, read);
                    remaining -= read;
                }

                // 长度也进哈希：不同长度但前缀相同的文件不能算同一个
                sha.AppendData(BitConverter.GetBytes(size));
                return Convert.ToHexString(sha.GetHashAndReset());
            }

            long want = Math.Min(SampleBytes, size);
            if (want <= 0) return null;
            var sample = new byte[want];
            if (part == HashPart.Tail && fs.CanSeek)
                fs.Seek(Math.Max(0, size - want), SeekOrigin.Begin);

            long got = 0;
            while (got < want)
            {
                ct.ThrowIfCancellationRequested();
                int read = fs.Read(sample, (int)got, (int)(want - got));
                if (read <= 0) break;
                got += read;
            }
            if (got < want)
            {
                changed = true;
                return null;
            }

            if (stats.BytesRead + got > budgetBytes)
            {
                budgetExhausted = true;
                return null;
            }
            stats.BytesRead += got;

            return Convert.ToHexString(SHA256.HashData(sample.AsSpan(0, (int)got)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            // 被别的进程独占：跳过，不当错误
            inUse = true;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            inUse = true;
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 云盘占位文件：内容是「按需下载」的，读它会把文件从云端拉下来。
    /// MFT 上它们带重解析点属性，直接用这个判断，绝不尝试打开。
    /// </summary>
    public static bool IsCloudPlaceholder(FileEntry e) => e.IsReparsePoint;

    /// <summary>
    /// 网络盘：按内容哈希要过 SMB，慢且不稳，整盘不做重复检测。
    /// 卷类型查询结果会缓存 —— 26 万条里逐条问盘是白扔的开销。
    /// </summary>
    public static bool IsOnNetworkDrive(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 2) return false;

        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;

        string root;
        try { root = Path.GetPathRoot(path) ?? ""; }
        catch { return false; }
        if (root.Length < 2) return false;

        return NetworkVolumeCache.GetOrAdd(root, static r =>
        {
            try { return new DriveInfo(r).DriveType == DriveType.Network; }
            catch { return false; }
        });
    }

    /// <summary>测试用：清掉卷类型缓存。</summary>
    public static void ResetVolumeCache() => NetworkVolumeCache.Clear();
}
