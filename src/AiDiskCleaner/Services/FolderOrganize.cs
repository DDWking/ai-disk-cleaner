using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>一次「列出直接子目录」的结果。预算与跳过都如实带出来，不假装列全了。</summary>
public sealed record OrganizeChildSet(
    IReadOnlyList<FileEntry> Dirs,
    bool Truncated,
    int Total,
    int Skipped);

/// <summary>顶层对象集合。系统入口解析不到就跳过（不报错、也不提权）。</summary>
public sealed record OrganizeRootSet(
    IReadOnlyList<FileEntry> Roots,
    bool Truncated,
    IReadOnlyList<string> EntryPoints,
    int Skipped);

/// <summary>
/// 「文件夹整理」的对象模型：**决定先看哪些目录、每层看多少**。
///
/// 全是纯函数、只在扫描出来的内存树上走，不碰磁盘、不调模型，
/// 所以可以完全离线测试。这里**没有**任何删除/选择相关的东西：
/// 用途识别不改 Risk / CanDelete / Selected，也不移动文件。
/// </summary>
public static class FolderOrganize
{
    /// <summary>顶层对象上限（超出的按容量截断，并如实说明）。</summary>
    public const int MaxRoots = 40;

    /// <summary>一个收纳目录默认列多少个直接子对象。</summary>
    public const int ChildBudget = 24;

    /// <summary>默认铺几层：顶层对象 + 它们的直接子对象。</summary>
    public const int AutoLevels = 2;

    /// <summary>未知目录「有限探索」时最多看几个直接子目录。</summary>
    public const int UnknownProbeBudget = 8;

    /// <summary>顺着系统入口往下最多走几层（不按距盘符固定层数截断）。</summary>
    public const int MaxEntryDepth = 8;

    /// <summary>整页最多铺多少行；到顶就停自动铺开（用户仍可手动展开）。</summary>
    public const int MaxRows = 4000;

    /// <summary>给模型的请求总量沿用服务里的硬上限，这里只是别名，避免两处口径漂移。</summary>
    public static int MaxAiRequests => FolderPurposeService.MaxAiRequests;

    static string Norm(string? p) => CleanListSnapshot.NormPath(p);

    /// <summary>p 是否等于 a，或位于 a 之下。两层都按整段目录比较，不用裸子串。</summary>
    public static bool IsUnderOrEqual(string? p, string? a)
    {
        string x = Norm(p), y = Norm(a);
        if (x.Length == 0 || y.Length == 0) return false;
        if (x.Equals(y, StringComparison.OrdinalIgnoreCase)) return true;
        return x.StartsWith(y + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从扫描根沿着路径一段一段找下去。找不到返回 null（**不访问磁盘**）。
    /// </summary>
    public static FileEntry? Find(FileEntry? root, string? fullPath)
    {
        if (root == null) return null;
        string target = Norm(fullPath);
        string start = Norm(root.FullPath);
        if (target.Length == 0 || start.Length == 0) return null;
        if (target.Equals(start, StringComparison.OrdinalIgnoreCase)) return root;
        if (!target.StartsWith(start + "\\", StringComparison.OrdinalIgnoreCase)) return null;

        var cur = root;
        string rest = target[(start.Length + 1)..];
        foreach (var seg in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            FileEntry? next = null;
            foreach (var c in cur.ChildList)
            {
                if (!c.IsDirectory) continue;
                if (string.Equals(c.Name, seg, StringComparison.OrdinalIgnoreCase)) { next = c; break; }
            }
            if (next == null) return null;
            cur = next;
        }
        return cur;
    }

    /// <summary>
    /// 顶层对象：**系统解析出来的识别入口**（Program Files / ProgramData / 用户目录 …）
    /// 加上盘符下其它直接子目录；重叠的只留最外层，避免同一个目录出现两次。
    ///
    /// 入口不一定贴着盘符（`…\AppData\Local` 就在好几层下面）——
    /// 这里**不按距盘符的固定层数截断**，只要解析得到就用。
    /// </summary>
    public static OrganizeRootSet BuildRoots(FileEntry? root, int budget = MaxRoots)
    {
        if (root == null) return new OrganizeRootSet(Array.Empty<FileEntry>(), false, Array.Empty<string>(), 0);

        var entries = FolderPurposeRules.EntryPoints();
        var roots = new List<FileEntry>();
        var included = new List<string>();
        int skipped = 0;

        // 1) 系统入口：按路径长度从短到长处理 ⇒ 先收外层，内层自然被外层覆盖
        var resolved = new List<FileEntry>();
        foreach (var ep in entries)
        {
            if (!IsUnderOrEqual(ep, root.FullPath)) continue;
            var e = Find(root, ep);
            if (e == null) { skipped++; continue; }      // 没扫到 / 解析不到：跳过，不提权
            resolved.Add(e);
        }
        resolved.Sort(static (a, b) => Norm(a.FullPath).Length.CompareTo(Norm(b.FullPath).Length));
        foreach (var e in resolved)
        {
            string n = Norm(e.FullPath);
            if (included.Any(a => IsUnderOrEqual(n, a))) continue;   // 已被更外层覆盖
            included.Add(n);
            roots.Add(e);
        }

        // 2) 盘符下的其它直接子目录（与已收的对象互为祖先/后代的一律跳过 ⇒ 去重）
        var top = new List<FileEntry>();
        foreach (var c in root.ChildList)
        {
            if (!c.IsDirectory) continue;
            if (c.IsReparsePoint || c.IsFilesGroup) { skipped++; continue; }   // 链接不跟随，避免成环
            top.Add(c);
        }
        top.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        foreach (var c in top)
        {
            string n = Norm(c.FullPath);
            if (included.Any(a => IsUnderOrEqual(n, a) || IsUnderOrEqual(a, n))) continue;
            included.Add(n);
            roots.Add(c);
        }

        bool truncated = roots.Count > budget;
        if (truncated) roots = roots.Take(budget).ToList();
        return new OrganizeRootSet(roots, truncated, entries, skipped);
    }

    /// <summary>
    /// 列出直接子目录：重解析点/散文件组跳过并计数，按容量降序，超出预算就截断
    /// （截断了会如实说出来，不假装列全）。
    /// </summary>
    public static OrganizeChildSet DirectChildDirs(FileEntry? dir, int budget = ChildBudget)
    {
        if (dir == null) return new OrganizeChildSet(Array.Empty<FileEntry>(), false, 0, 0);
        var all = new List<FileEntry>();
        int skipped = 0;
        foreach (var c in dir.ChildList)
        {
            if (!c.IsDirectory) continue;
            if (c.IsReparsePoint || c.IsFilesGroup) { skipped++; continue; }   // 不跟随链接，防环
            all.Add(c);
        }
        int total = all.Count;
        all.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        bool truncated = all.Count > budget;
        if (truncated) all = all.Take(budget).ToList();
        return new OrganizeChildSet(all, truncated, total, skipped);
    }

    /// <summary>
    /// 默认铺开：**收纳目录继续直接子目录，具体对象停止内部逐项分类**，未知也不自动铺。
    /// </summary>
    public static bool ShouldAutoExpand(FileEntry? dir, FolderKind kind, int depth)
    {
        if (!FolderPurposeRules.CanDescend(dir)) return false;
        if (depth >= AutoLevels - 1) return false;
        return depth == 0 && kind is FolderKind.Container or FolderKind.Mixed;
    }

    /// <summary>
    /// 顺着「系统入口」的路径往下走：入口本身不再自动往下，入口之上的祖先继续。
    /// 这条规则让 `…\AppData\Local` 这类深层入口照样成为对象，而不是被层数截掉。
    /// </summary>
    public static bool ShouldAutoFollowEntryPath(string? path, int depth, IReadOnlyList<string> entryPoints)
    {
        if (depth >= MaxEntryDepth) return false;
        string p = Norm(path);
        if (p.Length == 0) return false;
        foreach (var e in entryPoints)
        {
            string q = Norm(e);
            if (q.Length == 0) continue;
            if (p.Equals(q, StringComparison.OrdinalIgnoreCase)) return false;
            if (IsUnderOrEqual(q, p)) return true;
        }
        return false;
    }

    /// <summary>这个目录自己就是系统解析出来的识别入口。</summary>
    public static bool IsEntryPoint(string? path, IReadOnlyList<string> entryPoints)
    {
        string p = Norm(path);
        if (p.Length == 0) return false;
        foreach (var e in entryPoints)
            if (p.Equals(Norm(e), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>自动铺开的总判定：默认两层 + 系统入口路径例外。</summary>
    public static bool ShouldAutoMaterialize(FileEntry? dir, FolderKind kind, int depth, IReadOnlyList<string> entryPoints)
        => ShouldAutoExpand(dir, kind, depth)
           || (FolderPurposeRules.CanDescend(dir) && ShouldAutoFollowEntryPath(dir!.FullPath, depth, entryPoints));

    /// <summary>
    /// 「未知目录」的有限探索判定。
    ///
    /// 共享的 <see cref="FolderPurposeService.ShouldDescend"/> 对未知目录是**停**——
    /// 那是给侧栏树用的保守口径（侧栏是用户自己点，不必替他把未知摊开）。
    /// 整理页在**用户显式要求识别**这一批对象时，才允许对未知目录多看一层，
    /// 而且只看直接子目录、受预算与深度限制，看完还是说不清就保持「待确认」。
    /// </summary>
    public static bool ShouldProbeUnknown(FileEntry? dir, FolderKind kind, int depth)
        => kind == FolderKind.Unknown
           && depth < FolderPurposeService.MaxDepth
           && FolderPurposeRules.CanDescend(dir);

    /// <summary>
    /// 这个对象是不是「平台游戏库」这类容器 —— 认出平台后**仍然允许往里找游戏**。
    /// 认不出平台的目录一律按普通未知处理，不去猜它里面有没有游戏。
    /// </summary>
    public static bool KeepsObjectEntries(FileEntry? dir)
        => dir is { IsDirectory: true } && FolderPurposeRules.IsObjectContainer(dir.FullPath);

    /// <summary>行数是否已经到顶（到顶就停自动铺开，避免海量目录把界面拖死）。</summary>
    public static bool RowBudgetReached(int rows) => rows >= MaxRows;
}
