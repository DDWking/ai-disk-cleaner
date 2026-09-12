using AiDiskCleaner.Models;
using AiDiskCleaner.Services.CleanRules;

namespace AiDiskCleaner.Services;

/// <summary>
/// 把完整候选集分层成「用途 → 清理位置」。
///
/// 三条铁律：
/// 1. **完整候选一行不丢**。位置/用途只是索引，候选总数和空间始终按全量算。
/// 2. **锚点只收窄、不放大**。位置锚点要么是签名/已知缓存目录，要么就是条目自己的父目录；
///    绝不上卷到 AppData、用户主目录、Program Files、盘符根这种宽泛位置。
/// 3. **这是纯展示分组**。它不产生任何新的删除目标，也不改变任何风险判定 ——
///    勾选只是去改已有候选的 <c>Selected</c>。
///
/// 纯计算、不碰 WPF，可以在后台线程跑（<see cref="Build"/> 可取消）。
/// </summary>
public static class CleanGroupingService
{
    /// <summary>
    /// 明确的缓存/临时目录（相对路径片段）。命中就把它当作该位置的锚点，
    /// 从而把其下成千上万个候选子目录汇总成**一个**清理位置。
    /// 只收已知的，不做通用推断 —— 免得把 AppData 整棵树变成一个位置。
    /// </summary>
    private static readonly string[] AnchorDirBits =
    {
        @"\temp", @"\tmp", @"\cache", @"\caches", @"\logs", @"\log",
        @"\crashdumps", @"\minidump", @"\wer",
        @"\softwaredistribution\download",
        @"\inetcache", @"\code cache", @"\gpucache", @"\dawncache", @"\shadercache",
        @"\npm-cache", @"\pip\cache", @"\yarn\cache", @"\nuget\v3-cache",
        @"\package cache", @"\downloading", @"\installer\cache",
        @"\$recycle.bin",
    };

    /// <summary>
    /// 宽泛位置：**任何情况下都不作为锚点**。
    /// 落到这些位置说明锚定失败，此时退回条目自己的父目录（更窄、更具体）。
    /// </summary>
    private static readonly string[] ForbiddenAnchors =
    {
        @"\users", @"\appdata", @"\appdata\local", @"\appdata\roaming", @"\appdata\locallow",
        @"\program files", @"\program files (x86)", @"\programdata", @"\windows",
        @"\documents", @"\desktop", @"\downloads", @"\pictures", @"\videos", @"\music",
        @"\onedrive", @"\system volume information",
    };

    /// <summary>单个用途下最多给界面建多少个位置行；超出部分计入 <c>TruncatedLocations</c>，总数不受影响。</summary>
    public const int MaxLocationsPerPurpose = 4000;

    public static CleanLayeredResult Build(IReadOnlyList<CleanItem> candidates, CancellationToken ct = default)
    {
        // ---- 1) 去重：同一路径重复命中只留一条，空间不重复计 ----
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<CleanItem>(candidates.Count);
        int dupHits = 0;
        foreach (var item in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (item.Risk == CleanRisk.Keep) continue;   // 「别删」不进清理列表
            string key = CleanListSnapshot.NormPath(item.FullPath);
            if (key.Length == 0) continue;
            if (!seen.Add(key)) { dupHits++; continue; }
            all.Add(item);
        }

        // ---- 2) 按 (用途, 风险档) 分桶 ----
        var buckets = new Dictionary<(CleanPurpose, int), List<CleanItem>>();
        var order = new List<(CleanPurpose, int)>();
        foreach (var item in all)
        {
            ct.ThrowIfCancellationRequested();
            var key = (item.Purpose, item.Risk == CleanRisk.Safe ? 0 : 1);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = new List<CleanItem>();
                buckets[key] = list;
                order.Add(key);
            }
            list.Add(item);
        }

        // ---- 3) 每个桶再按「位置」分 ----
        var purposes = new List<CleanPurposeNode>(order.Count);
        long totalBytes = 0;
        int selectableTotal = 0;
        int overlapTotal = 0;
        // 位置计数用**去重后的稳定键**：同一个文件夹在「建议清理 / 需要你确认」两行里各出现一次，
        // 但它只是一处位置。风险拆组只是展示分组，不能把位置数翻倍。
        var distinctLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in order)
        {
            ct.ThrowIfCancellationRequested();
            var items = buckets[key];
            var locations = BuildLocations(items, key.Item1, key.Item2, ct, out int hidden);
            foreach (var loc in locations) distinctLocations.Add(loc.Key);
            if (hidden > 0)
            {
                // 未建行的位置也要计入去重集合（用规则键，不是显示名）
                foreach (var item in items)
                    distinctLocations.Add(LocationKey(item, key.Item1));
            }

            // 注意：**总数按完整候选算，不按已建的位置行算**。
            // 位置行数有上限（MaxLocationsPerPurpose），截断只影响显示，
            // 不能让首页的「几处位置 / 预计空间」跟着少算。
            long bytes = 0;
            int selectable = 0, overlap = 0;
            foreach (var loc in locations)
            {
                overlap += loc.OverlapCount;
            }
            selectable = CountSelectable(items);
            bytes = SumTopLevelBytes(items);

            totalBytes += bytes;
            selectableTotal += selectable;
            overlapTotal += overlap;

            var node = new CleanPurposeNode
            {
                Purpose = key.Item1,
                RiskTier = key.Item2,
                PurposeName = CleanPurposes.Name(key.Item1),
                Impact = CleanPurposes.Impact(key.Item1),
                Items = items,
                Locations = locations,
                SelectableCount = selectable,
                Bytes = bytes,
                SizeIsEstimate = false,
                OverlapCount = overlap,
                LocationsCounted = true,
                LocationsNotShown = hidden,
            };
            node.SyncFromItems();
            purposes.Add(node);
        }

        // ---- 4) 首页排序：风险档 → 用途优先级 → 空间降序 ----
        purposes.Sort(static (a, b) =>
        {
            int byRisk = a.RiskTier.CompareTo(b.RiskTier);
            if (byRisk != 0) return byRisk;
            int byRank = CleanPurposes.Rank(a.Purpose).CompareTo(CleanPurposes.Rank(b.Purpose));
            if (byRank != 0) return byRank;
            return b.Bytes.CompareTo(a.Bytes);
        });

        return new CleanLayeredResult
        {
            Purposes = purposes,
            AllItems = all,
            TotalLocations = distinctLocations.Count,
            TotalBytes = totalBytes,
            SelectableTotal = selectableTotal,
            OverlapCount = overlapTotal,
            DuplicateHitsRemoved = dupHits,
        };
    }

    /// <summary>可删候选数（不含保留项 / 受保护 / 规则本就不允许删的）。</summary>
    private static int CountSelectable(List<CleanItem> items)
    {
        int n = 0;
        foreach (var item in items)
            if (item.CanDelete) n++;
        return n;
    }

    /// <summary>
    /// 该用途的预计可清理空间：只算可删的、且**不被同用途内目录候选覆盖**的条目，
    /// 避免父目录和子文件重复计。按完整候选算，与位置行是否被截断无关。
    /// </summary>
    private static long SumTopLevelBytes(List<CleanItem> items)
    {
        var covered = FindCoveredItems(items, out _);
        long sum = 0;
        foreach (var item in items)
        {
            if (!item.CanDelete) continue;
            if (covered.Contains(item)) continue;
            sum += Math.Max(0, item.Size);
        }
        return sum;
    }

    /// <summary>把一批同用途同风险的候选聚成位置。</summary>
    private static List<CleanLocationNode> BuildLocations(
        List<CleanItem> items, CleanPurpose purpose, int riskTier, CancellationToken ct, out int hidden)
    {
        var map = new Dictionary<string, List<CleanItem>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            string key = LocationKey(item, purpose);
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<CleanItem>();
                map[key] = list;
                order.Add(key);
            }
            list.Add(item);
        }

        // 按可删空间降序决定「哪些位置值得建行」——先建大的，被截掉的是最小的那些。
        // 截断只影响显示行数，候选与空间总数始终按全量算。
        order.Sort((a, b) => LocationWeight(map[b]).CompareTo(LocationWeight(map[a])));

        var nodes = new List<CleanLocationNode>(Math.Min(order.Count, MaxLocationsPerPurpose));
        int built = 0;
        foreach (var key in order)
        {
            if (built >= MaxLocationsPerPurpose) break;
            built++;
            nodes.Add(BuildLocation(key, map[key], purpose, riskTier, ct));
        }
        hidden = Math.Max(0, order.Count - built);

        // 位置排序：可删空间降序 → 文件数降序 → 路径，保证稳定（不依赖字典顺序）
        nodes.Sort(static (a, b) =>
        {
            int byBytes = b.Bytes.CompareTo(a.Bytes);
            if (byBytes != 0) return byBytes;
            int byCount = b.FileCount.CompareTo(a.FileCount);
            return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
        });
        return nodes;
    }

    /// <summary>位置的「分量」：可删空间合计，用于决定哪些位置优先建行。</summary>
    private static long LocationWeight(List<CleanItem> items)
    {
        long sum = 0;
        foreach (var item in items)
            if (item.CanDelete) sum += Math.Max(0, item.Size);
        return sum;
    }

    /// <summary>
    /// 把分层结果限制到某个目录前缀下（「只看此文件夹的清理项」）。
    ///
    /// 纯函数：只重建索引与统计，**不改任何候选的风险、CanDelete 或 Selected** ——
    /// 范围只决定「显示什么」，全局选择始终保留（范围外的已选会被界面显式提示）。
    /// 空前缀或空前缀结果直接返回原对象，避免无意义的复制。
    /// </summary>
    public static CleanLayeredResult Scope(CleanLayeredResult full, string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return full;

        var purposes = new List<CleanPurposeNode>();
        var kept = new List<CleanItem>();
        var distinctLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        int selectableTotal = 0, overlapTotal = 0;

        foreach (var p in full.Purposes)
        {
            var items = p.Items.Where(x => CleanListSnapshot.UnderPrefix(x.FullPath, prefix)).ToList();
            if (items.Count == 0) continue;
            kept.AddRange(items);

            var locations = BuildLocations(items, p.Purpose, p.RiskTier, CancellationToken.None, out int hidden);
            foreach (var loc in locations) distinctLocations.Add(loc.Key);
            if (hidden > 0)
                foreach (var item in items) distinctLocations.Add(LocationKey(item, p.Purpose));

            long bytes = SumTopLevelBytes(items);
            int selectable = CountSelectable(items);
            int overlap = locations.Sum(l => l.OverlapCount);
            totalBytes += bytes;
            selectableTotal += selectable;
            overlapTotal += overlap;

            var node = new CleanPurposeNode
            {
                Purpose = p.Purpose,
                RiskTier = p.RiskTier,
                PurposeName = p.PurposeName,
                Impact = p.Impact,
                Items = items,
                Locations = locations,
                SelectableCount = selectable,
                Bytes = bytes,
                OverlapCount = overlap,
                LocationsCounted = true,
                LocationsNotShown = hidden,
            };
            node.SyncFromItems();
            purposes.Add(node);
        }

        return new CleanLayeredResult
        {
            Purposes = purposes,
            AllItems = kept,
            TotalLocations = distinctLocations.Count,
            TotalBytes = totalBytes,
            SelectableTotal = selectableTotal,
            OverlapCount = overlapTotal,
            DuplicateHitsRemoved = 0,
        };
    }

    /// <summary>构建一个位置节点（清理位置页的一行）。</summary>
    private static CleanLocationNode BuildLocation(
        string key, List<CleanItem> items, CleanPurpose purpose, int riskTier, CancellationToken ct)
    {
        var sample = items[0];

        // 空间统计：目录候选覆盖它下面的文件候选时不能重复计
        long bytes = 0;
        int overlap = 0;
        var covered = FindCoveredItems(items, out var dirCandidates);

        foreach (var item in items)
        {
            if (!item.CanDelete) continue;              // 保留项不计入「预计可清理」
            if (covered.Contains(item)) { overlap++; continue; }
            bytes += Math.Max(0, item.Size);
        }

        int selectable = items.Count(x => x.CanDelete);

        string displayName;
        string path;
        bool unidentified;
        if (purpose == CleanPurpose.Duplicate)
        {
            // 重复组是逻辑位置：显示组内成员数和「保留/多余」，不假装是一个文件夹
            bool keep = sample.Handling.StartsWith("dup-keep", StringComparison.Ordinal);
            displayName = Loc.PurposeDuplicate + " · " + (keep ? Loc.KeepCandidate : Loc.ExtraCandidate);
            int folders = items
                .Select(x => { int i = (x.FullPath ?? "").LastIndexOf('\\'); return i > 0 ? x.FullPath![..i] : x.FullPath ?? ""; })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            path = keep ? Loc.DuplicateKeepPath : Loc.DuplicateExtraPath(items.Count, folders);
            unidentified = false;
        }
        else
        {
            // 显示一律用**真实大小写**：稳定键把锚点转成了大写（用于去重），
            // 拿键当名字会显示成 `D:\PROJ1\BUILD\CACHE`，那是键不是路径。
            string real = RealCaseAnchor(sample.FullPath, key);
            var (name, isUnidentified) = DescribeLocation(real, sample);
            displayName = name;
            path = real.Length > 0 ? real : DisplayPathOf(key, sample);
            unidentified = isUnidentified;        }

        var node = new CleanLocationNode
        {
            Key = key,
            DisplayName = displayName,
            Path = path,
            IsUnidentified = unidentified,
            Purpose = purpose,
            RiskTier = riskTier,
            Risk = riskTier == 0 ? CleanRisk.Safe : CleanRisk.Confirm,
            Handling = sample.Handling,
            Reason = sample.Reason,
            // 技术标签（签名 + 风险词）只进悬停，不进标题
            Tech = sample.Tech ?? "",
            Impact = CleanPurposes.Impact(purpose),
            Items = items,
            SelectableCount = selectable,
            Bytes = bytes,
            SizeIsEstimate = false,
            OverlapCount = overlap,
        };
        // 目录候选覆盖了子项时，空间口径是「顶层合计」，如实标注
        if (overlap > 0) node.SizeIsEstimate = true;
        node.SyncFromItems();
        return node;
    }

    /// <summary>
    /// 找出被同组目录候选覆盖的条目（父子同时被选中时不能重复计空间）。
    /// 复用的是删除预检那套口径：父目录覆盖 → 子项按「已包含」处理。
    /// </summary>
    private static HashSet<CleanItem> FindCoveredItems(List<CleanItem> items, out List<string> dirs)
    {
        dirs = items.Where(x => x.CanDelete && x.IsDirectory)
            .Select(x => CleanListSnapshot.NormPath(x.FullPath))
            .Where(p => p.Length > 0)
            .ToList();
        dirs.Sort(static (a, b) => a.Length.CompareTo(b.Length));

        var covered = new HashSet<CleanItem>();
        if (dirs.Count == 0) return covered;
        foreach (var item in items)
        {
            if (item.IsDirectory) continue;
            string p = CleanListSnapshot.NormPath(item.FullPath);
            foreach (var dir in dirs)
            {
                if (p.Length > dir.Length && p.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
                    && p[dir.Length] == '\\')
                {
                    covered.Add(item);
                    break;
                }
            }
        }
        return covered;
    }

    /// <summary>
    /// 位置稳定键。**不包含显示名** —— 显示名会随语言/文案变化，键不能跟着漂。
    /// 目录锚点用归一化后的路径；重复组用规则给的组标识。
    /// </summary>
    public static string LocationKey(CleanItem item, CleanPurpose purpose)
    {
        if (purpose == CleanPurpose.Duplicate)
        {
            // Handling 里已经带了「保留/多余 + 重复组标识」，正好是稳定键
            string handling = string.IsNullOrEmpty(item.Handling) ? "dup" : item.Handling;
            return "dup|" + handling;
        }

        string anchor = FindAnchor(item.FullPath);
        return "dir|" + anchor.ToUpperInvariant();
    }

    /// <summary>
    /// 找位置锚点。顺序：签名根 → 已知缓存目录 → 条目自己的父目录。
    /// **只会越找越窄，绝不会上卷到宽泛位置。**
    /// </summary>
    public static string FindAnchor(string? fullPath)
    {
        string norm = CleanListSnapshot.NormPath(fullPath);
        if (norm.Length == 0) return "";
        string lower = norm.ToLowerInvariant();

        // 1) 签名根：认得出来的软件/用途，用它的根目录当一个位置
        var hit = AppSignatures.Match(lower);
        if (hit is { } h && h.Needle.Length > 0)
        {
            int at = lower.IndexOf(h.Needle, StringComparison.Ordinal);
            if (at > 0)
            {
                string root = norm[..Math.Min(norm.Length, at + h.Needle.Length)];
                if (!IsForbidden(root)) return root;
            }
        }

        // 2) 已知缓存目录：命中就把整棵子树汇总成一个位置
        foreach (var bit in AnchorDirBits)
        {
            int at = lower.IndexOf(bit, StringComparison.Ordinal);
            if (at <= 0) continue;
            string root = norm[..Math.Min(norm.Length, at + bit.Length)];
            if (!IsForbidden(root)) return root;
        }

        // 3) 兜底：条目自己的父目录（最窄，最具体）
        string parent = ParentOf(norm);
        return parent.Length > 0 ? parent : norm;
    }

    /// <summary>锚点是否落在宽泛位置上（这些位置不能当位置行）。</summary>
    private static bool IsForbidden(string anchor)
    {
        string lower = anchor.ToLowerInvariant();
        // 盘符根：C:\ 只有两个字符（或三个含斜杠）
        string trimmed = lower.TrimEnd('\\');
        if (trimmed.Length <= 2 && trimmed.EndsWith(':')) return true;
        foreach (var bad in ForbiddenAnchors)
        {
            if (lower.EndsWith(bad, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string ParentOf(string norm)
    {
        int at = norm.LastIndexOf('\\');
        if (at <= 0) return "";
        // 不要再退到盘符根
        if (at <= 2) return "";
        return norm[..at];
    }

    /// <summary>
    /// 位置显示名。
    ///
    /// **只表达身份，不表达风险**（§四）：
    /// <list type="number">
    /// <item>优先签名给出的具体软件/用途名（如「Chrome 缓存」「npm」）；</item>
    /// <item>否则用**真实文件夹名** —— 不再拿 KnownPaths 那种笼统标签当标题，
    ///       否则 `C:\Windows\Logs\CBS` 会被叫成「Windows 系统（别删）」，
    ///       而 `C:\Users\me\Documents\x` 会被叫成「用户目录」，两个不同位置看起来一模一样；</item>
    /// <item>同名文件夹靠「父路径」区分（见 <see cref="ParentHint"/>），完整路径在悬停里。</item>
    /// </list>
    /// 第二项同时保证：认不出就照实说文件夹名，不虚构应用归属。
    /// </summary>
    private static (string Name, bool Unidentified) DescribeLocation(string realPath, CleanItem sample)
    {
        // 1) 签名认得出来就用具体的软件/用途名（「Chrome 缓存」「npm」）
        string sig = AppSignatures.FriendlyName(sample.FullPath) ?? "";
        if (!string.IsNullOrWhiteSpace(sig)) return (sig, false);

        // 2) 否则用**真实文件夹名**。绝不用 KnownPaths 那种「Windows 系统」「用户目录」
        //    的笼统标签当标题 —— 那会让两个不同位置看起来一模一样，也没告诉用户是哪个文件夹。
        string path = CleanListSnapshot.NormPath(realPath);
        if (path.Length == 0) path = CleanListSnapshot.NormPath(sample.FullPath);
        string leaf = LeafOf(path);
        return (leaf.Length > 0 ? leaf : Loc.UnknownLocation, true);
    }

    /// <summary>
    /// 从真实路径里取回「锚点」那一段的**原始大小写**。
    /// 稳定键把锚点转成了大写，显示时要用真实路径的大小写。
    /// 匹配不上就退回真实路径的目录部分，总之不要用大写键当名字。
    /// </summary>
    internal static string RealCaseAnchor(string? realPath, string key)
    {
        string real = CleanListSnapshot.NormPath(realPath);
        if (real.Length == 0) return "";
        if (!key.StartsWith("dir|", StringComparison.Ordinal)) return real;
        string anchor = key[4..];
        if (anchor.Length == 0) return real;

        // 锚点是真实路径的前缀（忽略大小写）⇒ 取真实路径对应的那一段
        if (real.Length >= anchor.Length
            && real.AsSpan(0, anchor.Length).Equals(anchor, StringComparison.OrdinalIgnoreCase))
            return real[..anchor.Length];

        // 锚点就是整条路径（文件型位置）⇒ 直接用真实路径
        if (real.Equals(anchor, StringComparison.OrdinalIgnoreCase)) return real;

        // 其它情况（重复组等）：退回真实路径的父目录，至少大小写是对的
        int cut = real.LastIndexOf('\\');
        return cut > 0 ? real[..cut] : real;
    }

    /// <summary>第二行的「精简父路径」：去掉盘符与过长的部分，只留能区分同名的几段。</summary>
    internal static string ParentHint(string path)
    {
        var parts = ProtectedPaths.Segments(path);
        if (parts.Length <= 1) return "";
        // 文件的话去掉文件名那一段，只留父目录
        var dirs = parts.Length >= 2 && !path.EndsWith('\\')
                   && System.IO.Path.HasExtension(parts[^1])
            ? parts[..^1]
            : parts;
        if (dirs.Length <= 1) return "";
        // 最多显示末尾 3 段，前面用 … 省略，避免长路径把行撑破
        const int keep = 3;
        if (dirs.Length - 1 <= keep)
            return string.Join("\\", dirs);
        return "…\\" + string.Join("\\", dirs[(dirs.Length - keep)..]);
    }

    private static string DisplayPathOf(string key, CleanItem sample)
    {
        if (key.StartsWith("dir|", StringComparison.Ordinal))
        {
            string anchor = key[4..];
            if (anchor.Length > 0) return anchor;
        }
        return sample.FullPath ?? "";
    }

    private static string LeafOf(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        int at = path.LastIndexOf('\\');
        return at >= 0 && at + 1 < path.Length ? path[(at + 1)..] : path;
    }
}
