using System.IO;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 删除前置校验。做四件事，全部产出一条条可追查的记录：
/// 1) 父子/重复路径去重，避免同一个东西被删两次；
/// 2) 保护路径与重解析点判定；
/// 3) 存在性、路径身份（file id）、大小、修改时间比对 —— 确认「还是扫描时那个文件」；
/// 4) 占用与权限探测。
///
/// 依赖 <see cref="IFileSystemProbe"/>，可以喂假探针做离线测试。
/// </summary>
public sealed class DeletionPreflight
{
    /// <summary>修改时间容差（FAT 时间戳精度 2 秒）。</summary>
    public static readonly TimeSpan ModifiedTolerance = TimeSpan.FromSeconds(2);

    private readonly IFileSystemProbe _probe;

    public DeletionPreflight(IFileSystemProbe? probe = null)
        => _probe = probe ?? Win32FileSystemProbe.Instance;

    /// <summary>用于比较的规范路径：全路径 + 去尾部分隔符 + 大写。</summary>
    public static string NormPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string p = path.Trim();
        try { p = Path.GetFullPath(p); }
        catch { /* 非法路径就按原样比较 */ }
        p = p.Replace('/', '\\').TrimEnd('\\');
        return p.ToUpperInvariant();
    }

    /// <summary>
    /// 生成删除计划。用户确认与否通过 <paramref name="sensitiveConfirmed"/> 传入。
    /// 计划里保留**所有**项，包括被拒的，界面据此逐条汇报。
    /// </summary>
    public DeletionPlan CreatePlan(
        IReadOnlyList<DeletionTarget> targets,
        bool sensitiveConfirmed = false,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new DeletionPreflightResult { SensitiveConfirmed = sensitiveConfirmed };

        // ---- 1) 先按路径规范化 + 父子去重 ----
        var ordered = new List<(DeletionTarget Target, string Norm)>();
        foreach (var t in targets)
        {
            ct.ThrowIfCancellationRequested();
            ordered.Add((t, NormPath(t.Path ?? "")));
        }

        // 短的在前：父目录一定比子项短，先处理父就能把子项判成冗余。
        var byLength = ordered
            .Select((x, i) => (x.Target, x.Norm, Index: i))
            .OrderBy(x => x.Norm.Length)
            .ToList();

        var acceptedDirs = new List<string>();
        var acceptedExact = new HashSet<string>(StringComparer.Ordinal);
        var redundant = new Dictionary<int, string>(); // Index → 原因

        foreach (var x in byLength)
        {
            if (x.Norm.Length == 0)
            {
                redundant[x.Index] = ""; // 空路径单独处理
                continue;
            }
            if (!acceptedExact.Add(x.Norm))
            {
                redundant[x.Index] = "重复的路径";
                continue;
            }
            bool coveredByParent = false;
            foreach (var dir in acceptedDirs)
            {
                if (x.Norm.Length > dir.Length && x.Norm.StartsWith(dir, StringComparison.Ordinal)
                    && x.Norm[dir.Length] == '\\')
                {
                    coveredByParent = true;
                    break;
                }
            }
            if (coveredByParent)
            {
                redundant[x.Index] = "父目录已选中";
                continue;
            }
            // 目录（或未知类型，保守当作目录）才可能覆盖子项
            if (x.Target.IsDirectory || x.Target.Source == null)
                acceptedDirs.Add(x.Norm);
        }

        // ---- 2) 逐项探测与判定 ----
        for (int i = 0; i < ordered.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (target, norm) = ordered[i];
            var item = new DeletionPreflightItem { Target = target };

            if (norm.Length == 0)
            {
                item.Outcome = DeletionOutcome.Failed;
                item.Reason = "路径为空，跳过";
                item.Tech = "empty path";
                result.Items.Add(item);
                continue;
            }

            if (redundant.TryGetValue(i, out var why))
            {
                item.Outcome = DeletionOutcome.RedundantChild;
                item.Guard = DeletionGuardLevel.Allowed;
                item.Reason = why;
                item.Tech = "dedup: " + why;
                result.Items.Add(item);
                continue;
            }

            Judge(item);
            result.Items.Add(item);
        }

        sw.Stop();
        result.Elapsed = sw.Elapsed;
        return new DeletionPlan
        {
            Preflight = result,
            UserConfirmed = sensitiveConfirmed,
            AllowSensitive = sensitiveConfirmed,
        };
    }

    /// <summary>对一个已去重的目标做完整判定，结果写回 <paramref name="item"/>。</summary>
    private void Judge(DeletionPreflightItem item)
    {
        var t = item.Target;
        var probe = _probe.Probe(t.Path);

        if (!probe.Exists)
        {
            item.Outcome = DeletionOutcome.NotFound;
            item.Reason = "文件已经不在了";
            item.Tech = probe.Error ?? "not found";
            return;
        }

        if (probe.AccessDenied)
        {
            item.Outcome = DeletionOutcome.AccessDenied;
            item.Reason = "没有权限读取";
            item.Tech = "access denied: " + (probe.Error ?? "");
            return;
        }

        item.ObservedIdentity = probe.Identity.IsKnown ? probe.Identity : null;
        item.ObservedSize = probe.Size;
        item.ObservedModified = probe.Modified;

        bool isDir = probe.IsDirectory || t.IsDirectory;

        // ---- 保护路径 ----
        var guard = ProtectedPaths.Classify(t.Path, probe.IsReparsePoint, probe.IsSystem, isDir);
        item.Guard = guard.Guard switch
        {
            PathGuard.Blocked => DeletionGuardLevel.Blocked,
            PathGuard.NeedsConfirm => DeletionGuardLevel.NeedsConfirm,
            _ => DeletionGuardLevel.Allowed,
        };
        if (guard.Guard == PathGuard.Blocked)
        {
            item.Outcome = DeletionOutcome.Protected;
            item.Reason = guard.Reason;
            item.Tech = guard.Tech;
            return;
        }
        if (guard.Guard == PathGuard.NeedsConfirm)
        {
            item.Reason = guard.Reason;
            item.Tech = guard.Tech;
        }

        // ---- 占用 ----
        if (probe.InUse)
        {
            item.Outcome = DeletionOutcome.InUse;
            item.Reason = "文件正在被程序使用，先关掉它";
            item.Tech = "exclusive open failed: sharing violation";
            return;
        }

        // ---- 还是同一个文件吗 ----
        if (t.ExpectedIdentity is { } expected && expected.IsKnown
            && probe.Identity.IsKnown && !expected.Equals(probe.Identity))
        {
            item.Outcome = DeletionOutcome.PathChanged;
            item.Reason = "这个位置已经换成别的文件了，没有删";
            item.Tech = $"file id {expected.FileIdHigh:X8}{expected.FileIdLow:X8} → {probe.Identity.FileIdHigh:X8}{probe.Identity.FileIdLow:X8}";
            return;
        }

        if (!isDir && t.ExpectedSize >= 0 && probe.Size != t.ExpectedSize)
        {
            if (t.SnapshotIsExact)
            {
                item.Outcome = DeletionOutcome.Modified;
                item.Reason = "文件大小变了，扫描之后被改过";
                item.Tech = $"size {t.ExpectedSize} → {probe.Size}";
                return;
            }
            Soften(item, "大小和扫描时不一样", $"size {t.ExpectedSize} → {probe.Size}");
        }

        if (t.ExpectedModified != default && probe.Modified != default)
        {
            var delta = (probe.Modified - t.ExpectedModified).Duration();
            if (delta > ModifiedTolerance)
            {
                if (t.SnapshotIsExact)
                {
                    item.Outcome = DeletionOutcome.Modified;
                    item.Reason = "文件被改过，扫描之后有更新";
                    item.Tech = $"mtime {t.ExpectedModified:O} → {probe.Modified:O}";
                    return;
                }
                Soften(item, "修改时间变了（MFT 时间戳仅供参考）",
                    $"mtime {t.ExpectedModified:O} → {probe.Modified:O}");
            }
        }

        item.Outcome = DeletionOutcome.Ready;
        if (string.IsNullOrEmpty(item.Reason))
            item.Reason = "可以删除";
    }

    /// <summary>
    /// 把「证据不够硬」的差异降级成需要用户再确认，而不是硬拦。
    /// 不覆盖更严重的结论，也不覆盖已有的敏感位置说明。
    /// </summary>
    private static void Soften(DeletionPreflightItem item, string reason, string tech)
    {
        if (item.Outcome != DeletionOutcome.Ready) return;
        if (item.Guard == DeletionGuardLevel.Blocked) return;
        item.Guard = DeletionGuardLevel.NeedsConfirm;
        item.Reason = string.IsNullOrEmpty(item.Reason) ? reason : item.Reason + "；" + reason;
        item.Tech = item.Tech.Length > 0 ? item.Tech + "; " + tech : tech;
    }
}
