using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 文件明细的按需加载器。
///
/// 设计要点（对应验收要求）：
/// - <see cref="Source"/> 是**完整候选集**，搜索在整个集合上做，不只是已加载的页；
/// - 默认先给 <see cref="PageSize"/> 条，<see cref="LoadMore"/> 追加；
/// - 勾选状态挂在 <see cref="CleanItem"/> 实例上，翻页/搜索/折叠都不会丢
///   （因为从来不复制条目，只是换一个「要显示哪几条」的视图）；
/// - 纯计算、不碰 WPF，能在后台或界面线程安全调用。
/// </summary>
public sealed class CleanItemPager
{
    /// <summary>默认每页条数。</summary>
    public const int DefaultPageSize = 100;

    private readonly List<CleanItem> _source;
    private List<CleanItem> _filtered;
    private int _shown;

    public CleanItemPager(IReadOnlyList<CleanItem> source, int pageSize = DefaultPageSize)
    {
        _source = source as List<CleanItem> ?? source.ToList();
        PageSize = Math.Max(1, pageSize);
        _filtered = Sort(_source);
        _shown = Math.Min(PageSize, _filtered.Count);
    }

    /// <summary>完整候选集（该位置/该用途下的全部，不是当前页）。</summary>
    public IReadOnlyList<CleanItem> Source => _source;
    /// <summary>当前搜索条件下的完整匹配集（不是当前页）。</summary>
    public IReadOnlyList<CleanItem> Matches => _filtered;
    public int PageSize { get; }
    /// <summary>当前已显示的条数。</summary>
    public int Shown => _shown;
    public int MatchCount => _filtered.Count;
    public int TotalCount => _source.Count;
    public bool HasMore => _shown < _filtered.Count;
    /// <summary>当前页（已加载的部分）。</summary>
    public IReadOnlyList<CleanItem> Visible => _filtered.Take(_shown).ToList();

    public string Search { get; private set; } = "";

    /// <summary>「当前显示 X / Y」——必须让用户知道还有多少没显示。</summary>
    public string StatusText => Loc.DetailShowing(_shown, _filtered.Count);

    /// <summary>搜索范围说明：搜索针对完整匹配集，不是已加载的页。</summary>
    public string ScopeText => Loc.SearchScopeHint(Loc.LocationFiles(_filtered.Count));

    /// <summary>追加一页。</summary>
    public bool LoadMore()
    {
        if (!HasMore) return false;
        _shown = Math.Min(_shown + PageSize, _filtered.Count);
        return true;
    }

    /// <summary>
    /// 设置搜索词。**在完整候选集上过滤**，因此能命中还没加载的页。
    /// 过滤只换视图，不改任何条目的勾选状态。
    /// </summary>
    public void SetSearch(string? text)
    {
        string q = (text ?? "").Trim();
        if (string.Equals(q, Search, StringComparison.Ordinal) && _itemFilter == null) return;
        Search = q;
        _itemFilter = null;
        Rebuild();
    }

    private HashSet<CleanItem>? _itemFilter;

    /// <summary>是否正被「查看某一组文件」精确过滤。</summary>
    public bool HasItemFilter => _itemFilter != null;

    /// <summary>
    /// 精确过滤到**指定的这一组条目**（按对象身份，不靠路径字符串匹配）。
    /// 用于「查看文件」——一组条目可能散在多个子目录里，用搜索词表达不了。
    /// 同样只换视图，不改勾选。
    /// </summary>
    public void SetItemFilter(IEnumerable<CleanItem>? items)
    {
        if (items == null)
        {
            if (_itemFilter == null) return;
            _itemFilter = null;
            Rebuild();
            return;
        }
        _itemFilter = new HashSet<CleanItem>(items);
        Search = "";
        Rebuild();
    }

    /// <summary>清除全部过滤，回到完整列表。</summary>
    public void ClearFilter()
    {
        if (_itemFilter == null && Search.Length == 0) return;
        _itemFilter = null;
        Search = "";
        Rebuild();
    }

    void Rebuild()
    {
        IEnumerable<CleanItem> view = _source;
        if (_itemFilter != null) view = view.Where(_itemFilter.Contains);
        if (Search.Length > 0) view = ApplySearch(view, Search);
        _filtered = Sort(view);
        _shown = Math.Min(PageSize, _filtered.Count);
    }

    /// <summary>排序：按 (风险档, 大小降序)，与首页保持同一口径。</summary>
    private static List<CleanItem> Sort(IEnumerable<CleanItem> items)
        => items
            .OrderBy(x => x.Risk == CleanRisk.Safe ? 0 : 1)
            .ThenByDescending(x => x.Size)
            .ToList();

    private static List<CleanItem> ApplySearch(IEnumerable<CleanItem> source, string q)
    {
        var hit = new List<CleanItem>();
        foreach (var item in source)
        {
            if ((item.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (item.FullPath ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (item.Reason ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                hit.Add(item);
        }
        return hit;
    }

    /// <summary>
    /// 当前搜索范围内的全部可删候选（不只是当前页）。
    /// 用于「选中搜索结果全部」——范围和文案必须一致。
    /// </summary>
    public IReadOnlyList<CleanItem> SelectableMatches
        => _filtered.Where(x => x.CanDelete).ToList();

    /// <summary>当前页里的可删候选。用于「只选中当前页」。</summary>
    public IReadOnlyList<CleanItem> SelectableOnPage
        => _filtered.Take(_shown).Where(x => x.CanDelete).ToList();

    /// <summary>全量可删候选。用于「选中整组」。</summary>
    public IReadOnlyList<CleanItem> SelectableAll
        => _source.Where(x => x.CanDelete).ToList();

    /// <summary>按范围设置勾选。永远只作用于 <c>CanDelete</c> 的条目。</summary>
    public void Select(IReadOnlyList<CleanItem> scope, bool on)
    {
        foreach (var item in scope)
        {
            if (!item.CanDelete) continue;
            item.Selected = on;
        }
    }
}
