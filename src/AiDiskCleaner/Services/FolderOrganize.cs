using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>一次「列出直接子目录」的结果。预算与跳过都如实带出来，不假装列全了。</summary>
public sealed record OrganizeChildSet(
    IReadOnlyList<FileEntry> Dirs,
    bool Truncated,
    int Total,
    int Skipped);

/// <summary>
/// 一个对象处在第几级「认识层级」，以及它该不该被自动铺开 / 自动问模型。
///
/// <para><b>层级从这里数起</b>：</para>
/// <list type="bullet">
/// <item>普通盘：盘符下的直接子目录 = 第 1 级（例：<c>D:\Gameklll</c>），
/// 它们的直接子目录 = 第 2 级（例：<c>D:\Gameklll\volleyball</c>、<c>D:\Gameklll\steam</c>）。</item>
/// <item>系统盘入口（Program Files / ProgramData / AppData\Local · Roaming / 用户目录）：
/// <b>入口本身就是第 1 级</b>，它的直接应用目录也是第 1 级 —— 入口只是导航/容器，
/// 不是「盘符下面的一层」。这样才不会出现「C 盘只看到 Users、Program Files 就停了」。</item>
/// <item>入口的祖先（<c>Users</c> / <c>Users\bob</c> / <c>AppData</c>）沿用同一个层级往下走到入口，
/// 层级**不叠加**，所以深层入口不会被距盘符的层数截掉，也不会把层级越推越深。</item>
/// </list>
///
/// 层级只限制**展示对象与 AI 调用**：为了判断一个第 2 级对象，
/// 本地可以有限查看它内部的结构与代表性文件；**三级及更深一律不自动调用 AI**，
/// 由用户在「当前文件夹」工作区里显式点「识别当前文件夹」。
/// </summary>
public readonly record struct OrganizeLevelPolicy(int Level, long LevelBytes, int LevelFiles)
{
    /// <summary>第 1 级：自动铺开，自动本地识别，自动补 AI。</summary>
    public const int AutoLevel = 1;
    /// <summary>第 2 级：自动铺开并识别，但**不再自动往下展开/调用**。</summary>
    public const int AutoChildLevel = 2;

    public bool IsAutoLevel => Level >= AutoLevel && Level <= AutoChildLevel;
    /// <summary>三级及更深：不自动调用 AI。</summary>
    public bool IsManualOnly => Level > AutoChildLevel;
    /// <summary>还能按预算自动往下铺一层（只在第 1 级）。</summary>
    public bool CanAutoMaterialize => Level < AutoChildLevel;
    /// <summary>属于「扫描后自动批量识别」的范围（一、二级）。</summary>
    public bool AutoIdentify => IsAutoLevel;

    public static OrganizeLevelPolicy None => new(int.MaxValue, 0, 0);
}

/// <summary>当前「文件夹工作区」的识别计划 —— 界面上的范围、数量、请求预算都从这里来，不另写一套口径。</summary>
public sealed record OrganizeWorkPlan(
    string Path,
    string Name,
    int Level,
    int DirectChildTotal,
    int QueuedCount,
    int RequestBudget,
    bool ManualOnly)
{
    public static OrganizeWorkPlan Empty { get; } = new("", "", 0, 0, 0, 0, false);
    public bool HasTarget => Path.Length > 0;
}

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

    /// <summary>一个收纳目录默认列多少个直接子对象（**只是显示上限**，不是识别上限）。</summary>
    public const int ChildBudget = 24;

    /// <summary>
    /// 一个目录最多材料化多少个对象行。**和识别覆盖解耦**：
    /// 显示仍然只列 <see cref="ChildBudget"/> 条，但为了让「一、二级全量本地识别」
    /// 不被行数上限卡住，自动铺开时会一路算到这个上限。
    /// </summary>
    public const int MaterializeBudget = 400;

    /// <summary>单个目录最多材料化多少行；防止一个几十万子目录的地方把界面拖死。</summary>
    public const int MaxMaterializePerNode = 200;

    /// <summary>
    /// 自动识别时每个目录最多铺多少行。行数到顶不代表「只识别这几条」——
    /// 没铺出来的部分会如实标成「未识别 + 未列出」，不假装识别过。
    /// </summary>
    public static int AutoMaterializeBudget(bool isRoot) => isRoot ? MaterializeBudget : MaxMaterializePerNode;

    /// <summary>默认铺几层：顶层对象 + 它们的直接子对象。</summary>
    public const int AutoLevels = 2;

    /// <summary>未知目录「有限探索」时最多看几个直接子目录。</summary>
    public const int UnknownProbeBudget = 8;

    /// <summary>顺着系统入口往下最多走几层（不按距盘符固定层数截断）。</summary>
    public const int MaxEntryDepth = 8;

    /// <summary>整页最多铺多少行；到顶就停自动铺开（用户仍可手动展开）。</summary>
    public const int MaxRows = 4000;

    // ---------------- 层级策略（起点：盘符 / 系统入口） ----------------

    /// <summary>
    /// 顶层对象（盘符直接子目录、系统入口）的层级与容量。
    /// </summary>
    public static OrganizeLevelPolicy ForRoot(FileEntry dir)
        => new(OrganizeLevelPolicy.AutoLevel, dir.Size, dir.FileCount);

    /// <summary>
    /// 从父对象推出子对象的层级。
    ///
    /// <paramref name="parentIsEntryPoint"/> 为真时子对象就是第 1 级，
    /// <paramref name="childIsEntryPoint"/> 为真时子对象自己是第 1 级：
    /// **系统入口只是容器，层级不叠加** —— 这正是「C 盘不会只看到 Users / Program Files 就停」
    /// 的原因，入口的祖先（Users / AppData …）继续沿用同一层级往下走到入口。
    /// </summary>
    public static OrganizeLevelPolicy ForChild(
        OrganizeLevelPolicy parent, FileEntry child, bool childIsEntryPoint, bool parentIsEntryPoint)
    {
        int level = parentIsEntryPoint || childIsEntryPoint
            ? OrganizeLevelPolicy.AutoLevel
            : parent.Level + 1;
        return new OrganizeLevelPolicy(level, child.Size, child.FileCount);
    }

    /// <summary>
    /// 自动铺开的总判定：**最多两级** + 收纳/混合才继续。
    ///
    /// 例外：如果这个目录是**某个系统入口的祖先**（例如 <c>C:\Users</c>、
    /// <c>C:\Users\&lt;你&gt;\AppData</c>），就继续往下铺到入口本身 ——
    /// 否则深层入口（AppData\Local · Roaming）永远露不出来。
    /// 这条例外**只走路径**，到入口就停，不会把入口内部也自动铺开。
    /// </summary>
    public static bool ShouldAutoMaterializeChildren(
        OrganizeLevelPolicy policy, FileEntry? dir, FolderKind kind,
        IReadOnlyList<string>? entryPoints = null)
    {
        if (!FolderPurposeRules.CanDescend(dir)) return false;
        // 入口自己不再自动往下：它已经是识别起点，内部由用户展开
        if (entryPoints != null && dir != null && IsEntryPoint(dir.FullPath, entryPoints)) return false;
        if (policy.CanAutoMaterialize)
            return kind is FolderKind.Container or FolderKind.Mixed;
        // 层级已经超过两级：只有「通往系统入口的路上」才继续
        return dir != null && entryPoints != null
               && IsAncestorOfEntryPoint(dir.FullPath, entryPoints);
    }

    /// <summary>
    /// 这个对象要不要进「扫描后自动批量识别」的队列：一、二级里**还没有结论**的都算。
    /// 具体对象在本地就能认出，通常不会落到这里。
    /// </summary>
    public static bool ShouldAutoIdentify(OrganizeLevelPolicy policy, bool hasConclusion, bool canDescend)
        => policy.AutoIdentify && !hasConclusion && canDescend;

    /// <summary>
    /// 在当前文件夹工作区里能处理的对象：**只有直接子目录**，
    /// 不会顺着子目录再往下自动展开（点一次只分析当前这一层）。
    /// </summary>
    public static bool IsInCurrentFolderScope(string? candidate, string? currentFolder)
    {
        string c = Norm(candidate), p = Norm(currentFolder);
        if (c.Length == 0 || p.Length == 0) return false;
        if (!c.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        string rest = c[(p.Length + 1)..];
        return rest.Length > 0 && rest.IndexOf('\\') < 0;
    }

    /// <summary>
    /// 当前文件夹的单次请求预算：直接子目录数就是候选人，再被总预算卡住。
    /// 界面用同一个函数算「这次最多发几次」，避免界面与执行两套口径。
    /// </summary>
    public static int RequestBudgetFor(int directChildTotal)
    {
        int n = directChildTotal <= 0 ? 0 : directChildTotal;
        return Math.Min(n, MaxAiRequests);
    }

    /// <summary>给界面用的识别计划（范围 / 数量 / 请求预算一起算，不重复实现）。</summary>
    public static OrganizeWorkPlan PlanFor(string? name, string? path, int level, int directChildTotal)
        => new(path ?? "", name ?? "", level, Math.Max(0, directChildTotal),
            Math.Max(0, directChildTotal), RequestBudgetFor(directChildTotal),
            level > OrganizeLevelPolicy.AutoChildLevel);

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

    /// <summary>p 严格位于 a 之下（不包含相等）。</summary>
    public static bool IsStrictlyUnder(string? p, string? a)
    {
        string x = Norm(p), y = Norm(a);
        if (x.Length == 0 || y.Length == 0) return false;
        return x.StartsWith(y + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// p 是某个系统入口的祖先（入口在它下面）。
    /// 这类目录**只是通往入口的路**，不能当成「已经收过」而把入口挡掉 ——
    /// 那正是「C 盘只看到 Users 就停」的原因。
    /// </summary>
    public static bool IsAncestorOfEntryPoint(string? p, IReadOnlyList<string> entryPoints)
    {
        string x = Norm(p);
        if (x.Length == 0) return false;
        foreach (var e in entryPoints)
            if (IsStrictlyUnder(e, x)) return true;
        return false;
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
    /// 顶层对象：**系统解析出来的识别入口**（Program Files / Program Files (x86) /
    /// ProgramData / AppData\Local · Roaming …）加上盘符下其它直接子目录。
    ///
    /// 两条去重规则（这是「C 盘不会只看到 Users 就停」的关键）：
    /// <list type="number">
    /// <item>入口之间：被更外层入口覆盖的内层入口丢掉（避免同一个目录出现两次）；</item>
    /// <item>普通盘符子目录：**只有「已经是一份入口」时才被清掉**；
    /// 如果某个普通目录只是「某个入口的祖先」（例如 <c>C:\Users</c> 之于
    /// <c>C:\Users\&lt;你&gt;\AppData\Local</c>），它**不覆盖**入口 ——
    /// 否则用户目录会把真正有用的识别起点吞掉。</item>
    /// </list>
    ///
    /// 入口不一定贴着盘符（<c>…\AppData\Local</c> 就在好几层下面）——
    /// 这里**不按距盘符的固定层数截断**，只要解析得到就用；解析不到就跳过，**不自动提权**。
    /// </summary>
    public static OrganizeRootSet BuildRoots(
        FileEntry? root, int budget = MaxRoots, IReadOnlyList<string>? entryPoints = null)
    {
        if (root == null) return new OrganizeRootSet(Array.Empty<FileEntry>(), false, Array.Empty<string>(), 0);

        var entries = entryPoints ?? FolderPurposeRules.EntryPoints();
        var roots = new List<FileEntry>();
        var included = new List<string>();
        int skipped = 0;

        // 1) 解析入口节点
        var resolved = new List<FileEntry>();
        foreach (var ep in entries)
        {
            if (!IsUnderOrEqual(ep, root.FullPath)) continue;
            var e = Find(root, ep);
            if (e == null) { skipped++; continue; }      // 没扫到 / 解析不到：跳过，不提权
            resolved.Add(e);
        }

        // 2) 入口的祖先不作为顶层对象：它们只是路径，不是识别起点。
        //    这样 C:\Users / C:\Users\<你> / AppData 都不会占名额，也不会把入口挡掉。
        var ancestorPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in resolved)
        {
            var cur = e.Parent;
            int guard = 0;
            while (cur != null && guard++ < MaxEntryDepth + 8)
            {
                string cn = Norm(cur.FullPath);
                if (cn.Length == 0 || IsUnderOrEqual(cn, root.FullPath) == false) break;
                if (cn.Equals(Norm(root.FullPath), StringComparison.OrdinalIgnoreCase)) break;
                ancestorPaths.Add(cn);
                cur = cur.Parent;
            }
        }

        // 3) 入口之间按路径长度从短到长处理 ⇒ 先收外层，内层被外层覆盖时丢掉
        resolved.Sort(static (a, b) => Norm(a.FullPath).Length.CompareTo(Norm(b.FullPath).Length));
        foreach (var e in resolved)
        {
            string n = Norm(e.FullPath);
            if (included.Any(a => IsUnderOrEqual(n, a))) continue;   // 已被更外层入口覆盖
            included.Add(n);
            roots.Add(e);
        }

        // 4) 盘符下的其它直接子目录：**不当任何入口的祖先，也不用祖先身份去覆盖入口**
        var top = new List<FileEntry>();
        foreach (var c in root.ChildList)
        {
            if (!c.IsDirectory) continue;
            if (c.IsReparsePoint || c.IsFilesGroup) { skipped++; continue; }   // 链接不跟随，避免成环
            string n = Norm(c.FullPath);
            if (ancestorPaths.Contains(n)) continue;                  // 入口的祖先：不是识别起点
            if (included.Any(a => IsUnderOrEqual(n, a))) continue;    // 已经被入口本身收过
            top.Add(c);
        }
        top.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        foreach (var c in top)
        {
            string n = Norm(c.FullPath);
            // 只有「它自己就是一份入口」才去重；仅仅是祖先关系的不再互相吞掉
            if (included.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
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
