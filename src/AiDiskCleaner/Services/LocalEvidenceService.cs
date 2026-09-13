using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 需要真实系统路径的位置（下载目录等会**重定向**，不能靠拼用户名）。
/// </summary>
public enum SystemPathId
{
    UserProfile,
    Desktop,
    Downloads,
    Pictures,
    Music,
    Videos,
    Public,
    PublicDesktop,
    PublicDocuments,
    PublicPictures,
    PublicMusic,
    PublicVideos,
}

/// <summary>
/// 系统路径解析器（可注入）。生产实现走真实 KnownFolder；测试可以注入假实现，
/// 在**不碰注册表、不碰磁盘**的前提下验证重定向 / 同名异盘等行为。
/// </summary>
public interface ISystemPathResolver
{
    string? Resolve(SystemPathId id);
}

/// <summary>
/// 生产用解析器：KnownFolder 优先（支持重定向），解析不到才退回 <see cref="Environment.SpecialFolder"/>。
/// 只在建立快照时调用一次，识别循环里不会再调。
/// </summary>
public sealed class WindowsSystemPathResolver : ISystemPathResolver
{
    public static readonly WindowsSystemPathResolver Instance = new();

    public string? Resolve(SystemPathId id) => id switch
    {
        SystemPathId.UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        SystemPathId.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        // 下载目录没有 SpecialFolder 枚举：必须走真实 KnownFolder，否则重定向到别的盘会认错。
        SystemPathId.Downloads => KnownFolderPaths.TryGet(KnownFolderPaths.DownloadsId),
        SystemPathId.Pictures => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        SystemPathId.Music => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        SystemPathId.Videos => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        SystemPathId.Public => KnownFolderPaths.TryGet(KnownFolderPaths.PublicId),
        SystemPathId.PublicDesktop => KnownFolderPaths.TryGet(KnownFolderPaths.PublicDesktopId)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        SystemPathId.PublicDocuments => KnownFolderPaths.TryGet(KnownFolderPaths.PublicDocumentsId)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
        SystemPathId.PublicPictures => KnownFolderPaths.TryGet(KnownFolderPaths.PublicPicturesId)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures),
        SystemPathId.PublicMusic => KnownFolderPaths.TryGet(KnownFolderPaths.PublicMusicId)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonMusic),
        SystemPathId.PublicVideos => KnownFolderPaths.TryGet(KnownFolderPaths.PublicVideosId)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonVideos),
        _ => null,
    };
}

/// <summary>
/// 系统语义快照：**一次解析、之后只查内存**。
/// 判定只按真实全路径相等（大小写不敏感），不按目录名猜 ——
/// 其它盘的同名目录（`D:\Windows`）不会被误判。
/// </summary>
public sealed class SystemPathSnapshot
{
    public static readonly SystemPathSnapshot Empty = new(Array.Empty<FolderPurposeRules.SystemPathRole>());

    SystemPathSnapshot(IReadOnlyList<FolderPurposeRules.SystemPathRole> roles) => Roles = roles;

    /// <summary>长路径优先（越具体越先匹配）。</summary>
    public IReadOnlyList<FolderPurposeRules.SystemPathRole> Roles { get; }

    public int Count => Roles.Count;

    /// <summary>解析一次系统路径，建立只读快照。解析不到的位置直接跳过，不影响其它项。</summary>
    public static SystemPathSnapshot Capture(ISystemPathResolver? resolver = null)
    {
        resolver ??= WindowsSystemPathResolver.Instance;
        var roles = new List<FolderPurposeRules.SystemPathRole>();

        void Add(SystemPathId id, Func<string> name, Func<string> basis)
        {
            string? p;
            try { p = resolver.Resolve(id); }
            catch { return; }
            if (string.IsNullOrWhiteSpace(p)) return;
            string norm = CleanListSnapshot.NormPath(p);
            if (norm.Length == 0) return;
            roles.Add(new FolderPurposeRules.SystemPathRole(norm, name(), Loc.PurposeUserFiles, basis()));
        }

        Add(SystemPathId.Desktop, () => LocalRecognitionText.Desktop,
            () => LocalRecognitionText.BasisResolvedFolder(LocalRecognitionText.Desktop));
        Add(SystemPathId.Pictures, () => LocalRecognitionText.Pictures,
            () => LocalRecognitionText.BasisResolvedFolder(LocalRecognitionText.Pictures));
        Add(SystemPathId.Music, () => LocalRecognitionText.Music,
            () => LocalRecognitionText.BasisResolvedFolder(LocalRecognitionText.Music));
        Add(SystemPathId.Videos, () => LocalRecognitionText.Videos,
            () => LocalRecognitionText.BasisResolvedFolder(LocalRecognitionText.Videos));
        Add(SystemPathId.Public, () => LocalRecognitionText.PublicRoot,
            () => LocalRecognitionText.BasisResolvedFolder(LocalRecognitionText.PublicRoot));
        Add(SystemPathId.PublicDesktop, () => LocalRecognitionText.PublicDesktop,
            () => LocalRecognitionText.BasisResolvedFolder(LocalRecognitionText.PublicDesktop));
        Add(SystemPathId.PublicDocuments, () => LocalRecognitionText.PublicDocuments,
            () => LocalRecognitionText.BasisResolvedFolder(LocalRecognitionText.PublicDocuments));
        Add(SystemPathId.PublicPictures, () => LocalRecognitionText.PublicPictures,
            () => LocalRecognitionText.BasisResolvedFolder(LocalRecognitionText.PublicPictures));
        Add(SystemPathId.PublicMusic, () => LocalRecognitionText.PublicMusic,
            () => LocalRecognitionText.BasisResolvedFolder(LocalRecognitionText.PublicMusic));
        Add(SystemPathId.PublicVideos, () => LocalRecognitionText.PublicVideos,
            () => LocalRecognitionText.BasisResolvedFolder(LocalRecognitionText.PublicVideos));

        // 下载目录单独一条：文本沿用既有 Loc，但依据写清「可能已重定向」。
        try
        {
            string? dl = resolver.Resolve(SystemPathId.Downloads);
            if (!string.IsNullOrWhiteSpace(dl))
            {
                string norm = CleanListSnapshot.NormPath(dl);
                if (norm.Length > 0)
                    roles.Add(new FolderPurposeRules.SystemPathRole(norm, Loc.SysDownloads,
                        Loc.PurposeUserFiles, LocalRecognitionText.BasisRedirectedDownloads));
            }
        }
        catch { /* 解析不到就跳过 */ }

        return new SystemPathSnapshot(roles
            .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderByDescending(r => r.Path.Length)
            .ToArray());
    }

    /// <summary>只按真实全路径相等判定。</summary>
    public FolderPurposeRules.SystemPathRole? Match(string? path)
    {
        string p = CleanListSnapshot.NormPath(path);
        if (p.Length == 0) return null;
        foreach (var r in Roles)
            if (p.Equals(r.Path, StringComparison.OrdinalIgnoreCase)) return r;
        return null;
    }
}

/// <summary>
/// 已安装清单里的一条**真实安装位置**（只做输入，不改用户数据）。
/// </summary>
public readonly record struct InstalledAppLocation(string ProductName, string InstallLocation);

/// <summary>安装位置匹配的判定结果。</summary>
public enum InstalledLocationVerdict
{
    /// <summary>没有任何已安装位置匹配。</summary>
    None,
    /// <summary>查询路径**就是**某个软件唯一的安装位置（事实）。</summary>
    UniqueExact,
    /// <summary>查询路径位于某个软件唯一的安装目录**内部**（结构推测）。</summary>
    UniqueInside,
    /// <summary>多个软件共用这个位置，或该位置本身就装着别的软件 —— 不归属单个应用。</summary>
    Ambiguous,
    /// <summary>这个父目录里装着已安装软件，但它本身不是任何一个软件的安装位置。</summary>
    ContainerOfApps,
}

/// <summary>一次安装位置匹配。</summary>
public readonly record struct InstalledLocationHit(
    InstalledLocationVerdict Verdict,
    string ProductName,
    string MatchedRoot,
    string QueryPath)
{
    /// <summary>能不能归到**一个**产品上。</summary>
    public bool IsProduct => Verdict is InstalledLocationVerdict.UniqueExact or InstalledLocationVerdict.UniqueInside;
}

/// <summary>
/// 已安装软件的**只读安装位置快照**：一次性建立，之后只查内存。
///
/// 规则是刻意的：
/// <list type="bullet">
/// <item>**只按规范化后的真实路径做整段边界匹配**，不比名字、不做子串相似；</item>
/// <item>**唯一**才认产品：多个软件共用同一位置 ⇒ 不归属任何一个；</item>
/// <item>装着多个 / 其它软件的**父目录** ⇒ 不归属单个应用；</item>
/// <item>缺失 / 是盘根 / 相对路径的安装位置一律跳过并计数，不拿名字去猜。</item>
/// </list>
/// </summary>
public sealed class InstalledLocationSnapshot
{
    public static readonly InstalledLocationSnapshot Empty = new(Array.Empty<Entry>(), 0, 0, 0);

    readonly record struct Entry(string ProductName, string Path);

    readonly Entry[] _entries;

    InstalledLocationSnapshot(Entry[] entries, int appCount, int usableCount, int skippedCount)
    {
        _entries = entries;
        AppCount = appCount;
        UsableLocationCount = usableCount;
        SkippedCount = skippedCount;
    }

    /// <summary>输入清单里的软件条数。</summary>
    public int AppCount { get; }

    /// <summary>可用的真实安装位置数（去重前的条目数）。</summary>
    public int UsableLocationCount { get; }

    /// <summary>因位置缺失/不可用而跳过的条数。</summary>
    public int SkippedCount { get; }

    public bool IsEmpty => _entries.Length == 0;

    /// <summary>一次枚举建立快照，之后不再碰输入集合（也不碰注册表 / 磁盘）。</summary>
    public static InstalledLocationSnapshot Build(IEnumerable<InstalledAppLocation>? apps)
    {
        if (apps == null) return Empty;
        var entries = new List<Entry>();
        int total = 0, skipped = 0;
        foreach (var app in apps)
        {
            total++;
            if (string.IsNullOrWhiteSpace(app.ProductName) || string.IsNullOrWhiteSpace(app.InstallLocation))
            {
                skipped++;
                continue;
            }
            string norm = CleanListSnapshot.NormPath(app.InstallLocation);
            if (!IsUsableLocation(norm))
            {
                skipped++;
                continue;
            }
            entries.Add(new Entry(app.ProductName.Trim(), norm));
        }
        return new InstalledLocationSnapshot(entries.ToArray(), total, entries.Count, skipped);
    }

    /// <summary>
    /// 盘根（`C:`）或太短的路径不能当安装位置；相对路径也不行。
    /// 这里**不**因为「位置很宽」就丢弃 —— 共享由匹配时的唯一性判定处理。
    /// </summary>
    static bool IsUsableLocation(string norm)
        => norm.Length >= 4 && System.IO.Path.IsPathRooted(norm);

    /// <summary>规范化后的整段边界匹配（`C:\A` 不会命中 `C:\AB`）。</summary>
    static bool IsUnder(string path, string root)
        => path.Length > root.Length
           && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
           && path[root.Length] == '\\';

    public InstalledLocationHit Match(string? path)
    {
        string p = CleanListSnapshot.NormPath(path);
        if (p.Length == 0 || _entries.Length == 0) return new(InstalledLocationVerdict.None, "", "", p);

        List<Entry>? exact = null;
        List<Entry>? inside = null;
        List<Entry>? children = null;
        foreach (var e in _entries)
        {
            if (e.Path.Equals(p, StringComparison.OrdinalIgnoreCase)) (exact ??= new()).Add(e);
            else if (IsUnder(p, e.Path)) (inside ??= new()).Add(e);
            else if (IsUnder(e.Path, p)) (children ??= new()).Add(e);
        }

        if (exact is { Count: > 0 })
        {
            var products = DistinctProducts(exact);
            // 这个位置本身还装着别的软件的安装位置 ⇒ 是共享父目录，不归属单个应用
            if (products.Count == 1 && !ContainsOtherProduct(children, products))
                return new(InstalledLocationVerdict.UniqueExact, products[0], exact[0].Path, p);
            return new(InstalledLocationVerdict.Ambiguous, "", exact[0].Path, p);
        }

        if (inside is { Count: > 0 })
        {
            // 取最具体（路径最长）的那层安装根
            int max = 0;
            foreach (var e in inside) if (e.Path.Length > max) max = e.Path.Length;
            var best = new List<Entry>();
            foreach (var e in inside) if (e.Path.Length == max) best.Add(e);
            var products = DistinctProducts(best);
            if (products.Count == 1 && !ContainsOtherProduct(children, products))
                return new(InstalledLocationVerdict.UniqueInside, products[0], best[0].Path, p);
            return new(InstalledLocationVerdict.Ambiguous, "", best[0].Path, p);
        }

        if (children is { Count: > 0 })
            return new(InstalledLocationVerdict.ContainerOfApps, "", "", p);

        return new(InstalledLocationVerdict.None, "", "", p);
    }

    static List<string> DistinctProducts(List<Entry> entries)
    {
        var list = new List<string>();
        foreach (var e in entries)
            if (!list.Contains(e.ProductName, StringComparer.OrdinalIgnoreCase)) list.Add(e.ProductName);
        return list;
    }

    static bool ContainsOtherProduct(List<Entry>? children, List<string> products)
    {
        if (children == null) return false;
        foreach (var c in children)
            if (!products.Contains(c.ProductName, StringComparer.OrdinalIgnoreCase)) return true;
        return false;
    }
}

/// <summary>
/// 本地证据服务：把「系统语义快照」和「已安装位置快照」合成一个可注入的
/// <see cref="ILocalPurposeEvidence"/>。**所有查询都只在内存里做**，
/// 建立快照时各枚举一次，识别循环里不会重复扫注册表 / 磁盘。
/// </summary>
public sealed class LocalEvidenceService : ILocalPurposeEvidence
{
    readonly SystemPathSnapshot _system;
    readonly InstalledLocationSnapshot _installed;
    readonly IReadOnlyList<FolderPurposeRules.SystemPathRole> _roles;

    public LocalEvidenceService(
        InstalledLocationSnapshot? installed = null,
        SystemPathSnapshot? system = null)
    {
        _installed = installed ?? InstalledLocationSnapshot.Empty;
        _system = system ?? SystemPathSnapshot.Capture();
        _roles = Merge(_system.Roles, FolderPurposeRules.BuiltInSystemRoles());
    }

    /// <summary>只用扩展系统语义，没有已安装清单（启动早期就能装）。</summary>
    public static LocalEvidenceService CreateSystemOnly() => new(InstalledLocationSnapshot.Empty, SystemPathSnapshot.Capture());

    /// <summary>用已安装清单建立一次快照（调用方只枚举一次）。</summary>
    public static LocalEvidenceService Create(IEnumerable<InstalledAppLocation>? installedApps)
        => new(InstalledLocationSnapshot.Build(installedApps), SystemPathSnapshot.Capture());

    /// <summary>换一份安装位置快照，系统语义沿用（识别重启/重新清点软件时用）。</summary>
    public LocalEvidenceService WithInstalled(InstalledLocationSnapshot snapshot)
        => new(snapshot, _system);

    public InstalledLocationSnapshot Installed => _installed;
    public SystemPathSnapshot System => _system;

    /// <summary>扩展项在前（同路径优先用扩展文本），内置项补在其后。</summary>
    public IReadOnlyList<FolderPurposeRules.SystemPathRole> SystemRoles() => _roles;

    public FolderPurposeResult? RecognizeInstalled(FileEntry dir, FolderSummary sum, FolderId id)
    {
        var hit = _installed.Match(dir.FullPath);
        return hit.Verdict switch
        {
            // 就是安装位置本身：事实，不需要确认。
            InstalledLocationVerdict.UniqueExact => new FolderPurposeResult(id, hit.ProductName,
                LocalRecognitionText.InstalledCategory,
                LocalRecognitionText.BasisInstalledExact(hit.MatchedRoot),
                PurposeSource.Local, NeedsConfirm: false, Kind: FolderKind.Concrete),
            // 位于安装目录内部：**结构推测**，需要确认（用途不等于可删）。
            InstalledLocationVerdict.UniqueInside => new FolderPurposeResult(id, hit.ProductName,
                LocalRecognitionText.InstalledCategory,
                LocalRecognitionText.BasisInstalledInside(hit.ProductName, hit.MatchedRoot),
                PurposeSource.Local, NeedsConfirm: true, Kind: FolderKind.Concrete),
            // 共享位置 / 厂商父目录：**不归属单个应用**，交回上层按结构判定。
            _ => null,
        };
    }

    static IReadOnlyList<FolderPurposeRules.SystemPathRole> Merge(
        IReadOnlyList<FolderPurposeRules.SystemPathRole> primary,
        IReadOnlyList<FolderPurposeRules.SystemPathRole> fallback)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<FolderPurposeRules.SystemPathRole>(primary.Count + fallback.Count);
        void Push(IReadOnlyList<FolderPurposeRules.SystemPathRole> src)
        {
            foreach (var r in src)
                if (r.Path.Length > 0 && seen.Add(r.Path)) list.Add(r);
        }
        Push(primary);
        Push(fallback);
        return list.OrderByDescending(r => r.Path.Length).ToArray();
    }
}

/// <summary>
/// 本次新增的本地文案（中/英）。等集成时统一并进 <c>Loc</c>；
/// 在那之前集中放在这里，避免和共享的 Loc 文件打架。
/// </summary>
internal static class LocalRecognitionText
{
    static bool En => Loc.IsEn;

    public static string Desktop => En ? "desktop" : "桌面";
    public static string PublicDesktop => En ? "public desktop" : "公共桌面";
    public static string Pictures => En ? "pictures" : "图片";
    public static string PublicPictures => En ? "public pictures" : "公共图片";
    public static string Music => En ? "music" : "音乐";
    public static string PublicMusic => En ? "public music" : "公共音乐";
    public static string Videos => En ? "videos" : "视频";
    public static string PublicVideos => En ? "public videos" : "公共视频";
    public static string PublicDocuments => En ? "public documents" : "公共文档";
    public static string PublicRoot => En ? "public folders" : "公共文件夹";

    public static string BasisResolvedFolder(string what) => En
        ? $"Windows itself resolves this as the {what} folder"
        : $"这是系统自己解析出来的{what}目录";

    public static string BasisRedirectedDownloads => En
        ? "this is the download folder resolved by Windows itself (it may be redirected to another drive)"
        : "这是系统自己解析出来的下载目录（可能已重定向到其它盘）";

    public static string InstalledCategory => En ? "installed software" : "已安装软件";

    public static string BasisInstalledExact(string path) => En
        ? $"exactly matches an install location from the installed list: {path}"
        : $"与已安装清单里的安装位置完全一致：{path}";

    public static string BasisInstalledInside(string product, string root) => En
        ? $"inside the install folder of \"{product}\" (a guess: this sub-folder is not the app itself) — {root}"
        : $"位于已安装软件「{product}」的安装目录内（推测：这个子目录本身不等于那个软件）—— {root}";
}
