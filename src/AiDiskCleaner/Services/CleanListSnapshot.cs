using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 清理列表的数据快照：去重、分类、统计、预排序的一次性产物。
///
/// 为什么要它：以前这些活全在界面线程上干，而且 **同一批数据排了两次序**
/// （先给每个分类排，再拼「全部」又排一次）。26 万条量级下这就是肉眼可见的卡顿。
/// 现在整个构建过程是纯函数式的，可以丢到后台线程跑，只把成品交回界面。
///
/// 注意：快照里引用的是**同一批 CleanItem 实例**，所以勾选状态和 AI 写回的说明
/// 仍然共享，不会因为重建列表而丢失。
/// </summary>
public sealed class CleanListSnapshot
{
    /// <summary>去重后、按大小降序的完整候选（「全部」分类直接用这一份，不再复制再排序）。</summary>
    public required IReadOnlyList<CleanItem> All { get; init; }

    /// <summary>分类列表。第一个是「全部」（只有多于一个分类时才加）。</summary>
    public required IReadOnlyList<CatRow> Categories { get; init; }

    public int Count => All.Count;
    public long Bytes { get; init; }

    public static readonly CleanListSnapshot Empty = new()
    {
        All = Array.Empty<CleanItem>(),
        Categories = Array.Empty<CatRow>(),
        Bytes = 0,
    };

    /// <summary>
    /// 构建快照。**纯计算、不碰 UI**，可以在后台线程调用。
    /// 传进来的 token 只影响本方法，不会去取消别的阶段。
    /// </summary>
    public static CleanListSnapshot Build(
        IEnumerable<CleanItem> candidates,
        string catAll,
        string catOther,
        CancellationToken ct = default)
    {
        // 1) 去重 + 过滤「别删」/ 空路径，得到唯一一份完整候选
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<CleanItem>();
        foreach (var x in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (x.Risk == CleanRisk.Keep) continue;
            if (string.IsNullOrEmpty(x.FullPath)) continue;
            if (!seen.Add(NormPath(x.FullPath))) continue;
            all.Add(x);
        }

        // 2) 只排这一次序，按 (风险分组, 大小降序)。
        //    界面那个 ListCollectionView 只加分组、不加排序，所以源顺序就是最终显示顺序 ——
        //    这样 26 万条不会在 UI 线程上被再排一遍。
        //    后面分组建「全部」都复用这个顺序，也不再重复 OrderByDescending。
        all.Sort(static (a, b) =>
        {
            int byGroup = a.RiskGroupKey.CompareTo(b.RiskGroupKey);
            return byGroup != 0 ? byGroup : b.Size.CompareTo(a.Size);
        });

        long total = 0;
        foreach (var x in all) total += x.Size;

        // 3) 按分类切分。all 已经有序，顺序取用即可，组内天然按大小降序。
        var byName = new Dictionary<string, List<CleanItem>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var x in all)
        {
            ct.ThrowIfCancellationRequested();
            string name = string.IsNullOrWhiteSpace(x.Group) ? catOther : x.Group;
            if (!byName.TryGetValue(name, out var list))
            {
                list = new List<CleanItem>();
                byName[name] = list;
                order.Add(name);
            }
            list.Add(x);
        }

        // 4) 分类按占用降序
        var cats = new List<CatRow>(order.Count + 1);
        foreach (var name in order)
        {
            var items = byName[name];
            long bytes = 0;
            foreach (var x in items) bytes += x.Size;
            cats.Add(new CatRow
            {
                Name = name,
                Items = items,
                Percent = total > 0 ? bytes * 100.0 / total : 0,
            });
        }
        cats.Sort(static (a, b) => b.Bytes.CompareTo(a.Bytes));

        // 5) 多于一个分类才加「全部」，并直接复用 all（零拷贝、零再排序）
        if (cats.Count > 1)
        {
            cats.Insert(0, new CatRow
            {
                Name = catAll,
                Items = all,
                Percent = 100,
            });
        }

        return new CleanListSnapshot
        {
            All = all,
            Categories = cats,
            Bytes = total,
        };
    }

    /// <summary>
    /// 按目录前缀过滤某个分类的条目。带缓存 —— 分类切换、搜索、勾选统计都复用，
    /// 不反复全量重算。
    /// </summary>
    public static List<CleanItem> FilterByFolder(IReadOnlyList<CleanItem> source, string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return source as List<CleanItem> ?? source.ToList();
        var result = new List<CleanItem>(Math.Min(source.Count, 4096));
        for (int i = 0; i < source.Count; i++)
        {
            if (UnderPrefix(source[i].FullPath, prefix)) result.Add(source[i]);
        }
        return result;
    }

    public static string NormPath(string? p)
        => (p ?? "").Replace('/', '\\').Trim().TrimEnd('\\');

    public static bool UnderPrefix(string? path, string prefix)
    {
        string p = NormPath(path);
        if (p.Length == 0) return false;
        return p.Equals(prefix.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string FolderPrefix(string folderPath)
    {
        string p = NormPath(folderPath);
        return p + "\\";
    }
}
