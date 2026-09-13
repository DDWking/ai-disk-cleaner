using System.IO;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>目录在识别上的性质。**不是**层级硬限制，而是「要不要继续往下看」的依据。</summary>
public enum FolderKind
{
    /// <summary>还没判断出来。</summary>
    Unknown,
    /// <summary>收纳目录：装着多个独立对象，应该继续识别直接子目录。</summary>
    Container,
    /// <summary>具体对象：已经认出是某个应用/游戏/项目/资料集，默认停止对子目录逐一分类。</summary>
    Concrete,
    /// <summary>混合：既有具体对象，也有看不出来的东西。</summary>
    Mixed,
}

/// <summary>结论从哪来。**来源必须如实展示**，不能把本地判断说成 AI，更不能说成用户确认。</summary>
public enum PurposeSource { None, Local, Ai, User }

/// <summary>界面要表达的状态。没有有效结果时**不允许**显示成「已识别」。</summary>
public enum PurposeState { Unrecognized, Queued, Running, Recognized, NeedsConfirm, Failed }
/// <summary>
/// 目录的**稳定标识**：完整路径 + 扫描代次。
/// 结果一律按它关联，不依赖显示名称，也不接受 AI 返回的路径。
/// </summary>
public readonly record struct FolderId(string Path, int ScanGeneration)
{
    public override string ToString() => $"{ScanGeneration}\u0001{CleanListSnapshot.NormPath(Path)}";
}

/// <summary>
/// 有上限的目录摘要。**用途分类与容量统计分开** ——
/// 停止分类不代表停止统计内部空间（<see cref="Size"/> 始终是整个子树的）。
/// </summary>
public sealed record FolderSummary(
    FolderId Id,
    string Name,
    string RelativePath,
    int Depth,
    long Size,
    int FolderCount,
    int FileCount,
    int DirectFolderCount,
    IReadOnlyList<string> TypeMix,
    IReadOnlyList<string> SampleNames,
    IReadOnlyList<string> SampleDirs,
    string ProductHint,
    DateTime Modified,
    bool Truncated)
{
    /// <summary>摘要里实际取了多少个代表样本（如实写出来，不声称看了全部）。</summary>
    public int SampleCount => SampleNames.Count;

    /// <summary>
    /// 结构指纹：类型分布 + 代表文件名 + 直接子目录名。
    /// 参与缓存键，所以「目录结构变了」这一类变化也会让旧结论失效。
    /// 只由本地摘要算出来，不含任何用户数据。
    /// </summary>
    public string KindSignature =>
        string.Join(",", TypeMix) + "|" + string.Join(",", SampleNames) + "|" + string.Join(",", SampleDirs);
}

/// <summary>一次识别结论。</summary>
public sealed record FolderPurposeResult(
    FolderId Id,
    string PurposeName,
    string Category,
    string Basis,
    PurposeSource Source,
    bool NeedsConfirm,
    FolderKind Kind,
    string Model = "")
{
    public static FolderPurposeResult None(FolderId id, FolderKind kind = FolderKind.Unknown)
        => new(id, "", "", "", PurposeSource.None, false, kind);

    /// <summary>识别失败（网络不通 / 超时 / 供应商报错）：**和「还没识别」不是一回事**。</summary>
    public static FolderPurposeResult Failure(FolderId id, FolderKind kind = FolderKind.Unknown)
        => new(id, "", "", "", PurposeSource.None, false, kind) { Failed = true };

    /// <summary>这次识别**失败**了（不是「没结论」）。默认 false。</summary>
    public bool Failed { get; init; }

    /// <summary>有结论才算「已识别」；本地/模型都没给出可用内容时不能当成功。</summary>
    public bool HasConclusion => PurposeName.Length > 0;

    /// <summary>
    /// 状态映射（四种结论 + 流程态，互不冒充）：
    /// <list type="bullet">
    /// <item>有结论且无需确认 ⇒ <see cref="PurposeState.Recognized"/>（本地识别 / 用户确认）；</item>
    /// <item>有结论但需要确认（模型推测）⇒ <see cref="PurposeState.NeedsConfirm"/>；</item>
    /// <item>请求失败 / 超时 / 网络不通 ⇒ <see cref="PurposeState.Failed"/>；</item>
    /// <item>其余没有结论 ⇒ <see cref="PurposeState.Unrecognized"/>。</item>
    /// </list>
    /// </summary>
    public PurposeState State => Failed
        ? PurposeState.Failed
        : Source switch
        {
            PurposeSource.None => PurposeState.Unrecognized,
            _ => NeedsConfirm ? PurposeState.NeedsConfirm : PurposeState.Recognized,
        };

    public string SourceText => Source switch
    {
        PurposeSource.Local => Loc.PurposeFromLocal,
        PurposeSource.Ai => Loc.PurposeFromAi,
        PurposeSource.User => Loc.PurposeFromUser,
        _ => "",
    };
}

/// <summary>
/// **本地**目录识别规则。纯函数、不碰磁盘、不调模型，可以完全离线测试。
///
/// 明确的原则：
/// <list type="bullet">
/// <item>认出具体对象就停 —— 不因为「还能往下看」就把一个应用的内部目录逐个贴标签；</item>
/// <item>收纳目录继续往下 —— 但由界面在用户展开时按需触发，不自动铺开整棵树；</item>
/// <item>**平台不吞内部游戏**：Steam 游戏库这类容器要保留往里探索的入口；</item>
/// <item>判不出来就说判不出来，允许未知与混合，不硬给结论。</item>
/// </list>
/// </summary>
public static class FolderPurposeRules
{
    /// <summary>摘要里最多列几种类型 / 几个代表文件名。</summary>
    public const int MaxTypeMix = 3;
    public const int MaxSamples = 8;

    /// <summary>一个目录至少有这么多「像独立对象」的直接子目录，才算收纳目录。</summary>
    public const int ContainerMinChildren = 4;

    /// <summary>子目录小到这个尺寸以下、又没有独立对象证据时，不构成「集合」。</summary>
    public const long TrivialChildBytes = 1024 * 1024;

    /// <summary>「像独立对象」的子目录至少要占到这么大，小集合才不至于被漏判。</summary>
    public const int StrongChildBytes = 8 * 1024 * 1024;

    /// <summary>
    /// 系统盘入口：通过系统路径解析，**不硬编码 C 盘或用户名**。
    /// 入口本身不是可清理结论。
    /// </summary>
    public static IReadOnlyList<string> EntryPoints()
    {
        var list = new List<string>();
        void Add(Environment.SpecialFolder f)
        {
            try
            {
                string p = Environment.GetFolderPath(f);
                if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) list.Add(p);
            }
            catch { /* 解析不到就跳过，不影响其它入口 */ }
        }
        Add(Environment.SpecialFolder.ProgramFiles);
        Add(Environment.SpecialFolder.ProgramFilesX86);
        Add(Environment.SpecialFolder.CommonApplicationData);
        Add(Environment.SpecialFolder.LocalApplicationData);
        Add(Environment.SpecialFolder.ApplicationData);
        Add(Environment.SpecialFolder.UserProfile);
        return list;
    }

    /// <summary>
    /// 这个目录是否「装着独立对象」的平台容器 —— 认出平台后**仍要能往里找游戏**。
    /// </summary>
    public static bool IsObjectContainer(string? path)
    {
        string p = (path ?? "").Replace('/', '\\').ToLowerInvariant();
        return p.Contains(@"\steamapps\common")
            || p.Contains(@"\steamapps\downloading")
            || p.EndsWith(@"\steamapps")
            || p.Contains(@"\steamlibrary")
            || p.Contains(@"\epic games\")
            || p.Contains(@"\gog galaxy\games")
            || p.Contains(@"\battle.net\")
            || p.Contains(@"\xboxgames")
            || p.Contains(@"\ubisoft game launcher\games")
            || p.Contains(@"\origin games")
            || p.Contains(@"\riot games")
            || p.Contains(@"\we imposed") == false && p.Contains(@"\wegameapps");
    }

    /// <summary>不跟随重解析点 / 目录链接，避免越出扫描范围或成环。</summary>
    /// <summary>
    /// 一眼就是「某个对象的内部」的目录名。认出这些就**不再往里逐个分类** ——
    /// 否则会一头钻进 node_modules / .git 这种几万个子目录的地方。
    /// 名字判定只用目录名，不读内容。
    /// </summary>
    static readonly HashSet<string> KnownLeafDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "__pycache__", ".venv", "venv",
        ".vs", ".vscode", ".idea", "obj", "bin", "target", "dist", "build",
        "packages", "vendor", "site-packages", ".gradle", ".nuget", ".cargo",
        "winSxS".ToLowerInvariant(), "assembly", "driverstore", "winsxs",
    };

    /// <summary>是不是「已知对象的内部目录」。</summary>
    public static bool IsKnownLeafDir(string? name)
        => !string.IsNullOrWhiteSpace(name) && KnownLeafDirs.Contains(name.Trim());

    public static bool CanDescend(FileEntry? e)
        => e is { IsDirectory: true } && !e.IsReparsePoint && !e.IsFilesGroup;
    /// <summary>
    /// 生成有上限的摘要。**只读扫描树，不访问磁盘**。
    /// </summary>
    public static FolderSummary Summarize(FileEntry dir, FolderId id, int depth, string relativePath)
    {
        var childDirs = dir.ChildList.Where(c => c.IsDirectory).ToList();
        var childFiles = dir.ChildList.Where(c => !c.IsDirectory).ToList();

        // 类型分布：按扩展名聚合直接子文件 + 子目录里的小样本，取前几名
        var mix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in childFiles)
        {
            string ext = System.IO.Path.GetExtension(f.Name ?? "").ToLowerInvariant();
            if (ext.Length == 0) ext = Loc.PurposeNoExt;
            mix[ext] = mix.TryGetValue(ext, out int n) ? n + 1 : 1;
        }
        var typeMix = mix.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(MaxTypeMix).Select(kv => $"{kv.Key}×{kv.Value}").ToList();

        // 代表性文件名：取最大的几个（不读文件内容，只用名字）
        var samples = childFiles.OrderByDescending(f => f.Size).Take(MaxSamples)
            .Select(f => f.Name ?? "").Where(s => s.Length > 0).ToList();

        // 直接子目录名：**只看名字、不读内容**，用来判断工程结构（.git / src / node_modules …）
        var sampleDirs = childDirs.OrderByDescending(d => d.Size).Take(MaxSamples)
            .Select(d => d.Name ?? "").Where(s => s.Length > 0).ToList();

        // 程序产品信息：完全走本地签名，不猜
        string product = AppSignatures.FriendlyName(dir.FullPath) ?? "";

        return new FolderSummary(
            id, dir.Name ?? "", relativePath, depth,
            dir.Size, dir.FolderCount, dir.FileCount, childDirs.Count,
            typeMix, samples, sampleDirs, product, dir.Modified,
            Truncated: childFiles.Count > MaxSamples || mix.Count > MaxTypeMix
                       || childDirs.Count > MaxSamples);
    }

    /// <summary>
    /// 判断目录性质：收纳 / 具体对象 / 未知 / 混合。
    ///
    /// 这里**刻意不绑定任何具体例子**（不认「某个盘名/某个开发板项目名」）：
    /// 只看通用证据 —— 目录名命中的已知签名、子目录的规模、
    /// 以及子目录里有多少个像独立对象。这样小集合（2–3 个真实子目录）
    /// 也不会因为「数量不到 4」被漏判成「未知」。
    /// </summary>
    public static FolderKind ClassifyKind(FileEntry dir)
    {
        // 0) 已知对象内部（.git / node_modules / winsxs …）：就是具体对象，不再下钻
        if (IsKnownLeafDir(dir.Name)) return FolderKind.Concrete;

        // 1) 这个目录**自己**命中已知签名才算「具体对象」。
        //    注意：只是「里面装着已知程序」不等于自己是程序 —— 那是集合，应该让人往下看。
        bool knownSelf = IsKnownObject(dir.FullPath);

        var kids = dir.ChildList.Where(c => c.IsDirectory)
            .Where(c => !c.IsReparsePoint && !c.IsFilesGroup).ToList();
        if (kids.Count == 0)
            return knownSelf ? FolderKind.Concrete : FolderKind.Unknown;

        // 2) 统计「像独立对象」的直接子目录
        int strong = 0;          // 有内容、像独立对象的子目录
        foreach (var k in kids)
            if (k.Size >= TrivialChildBytes || IsKnownObject(k.FullPath)) strong++;

        // 3) 平台容器：认出平台后**仍然要能往里找游戏**
        if (IsObjectContainer(dir.FullPath)) return FolderKind.Container;

        // 4) 收纳目录：多个独立子目录。
        //    小集合放宽到「2 个有内容的子目录」也能算集合，避免只认 4 个以上。
        if (kids.Count >= ContainerMinChildren && strong >= 3)
            return knownSelf ? FolderKind.Mixed : FolderKind.Container;

        long strongBytes = 0;
        foreach (var k in kids) if (k.Size >= TrivialChildBytes) strongBytes += k.Size;
        bool substantial = dir.Size > 0 && strongBytes * 2 >= dir.Size;   // 大头都在子目录里
        if (kids.Count >= 2 && strong >= 2 && (strongBytes >= StrongChildBytes || substantial))
            return knownSelf ? FolderKind.Mixed : FolderKind.Container;

        if (knownSelf) return FolderKind.Concrete;
        if (strong > 0) return FolderKind.Mixed;   // 有独立子对象，但也夹着说不清的小目录
        return FolderKind.Unknown;
    }

    /// <summary>这个目录**自己**是不是「已知的软件 / 缓存对象」（只看路径签名，不读内容）。</summary>
    public static bool IsKnownObject(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && (AppSignatures.Match(path) != null
               || !string.IsNullOrWhiteSpace(AppSignatures.FriendlyName(path)));

    /// <summary>
    /// 本地识别：能用可靠规则说清楚就直接给结论，说不清就返回 None（由上层决定要不要问 AI）。
    /// </summary>
    /// <param name="entryPoints">
    /// 系统识别入口（由 <see cref="EntryPoints"/> 解析）。传 null 就现解析一次；
    /// 批量识别时由调用方传进来，避免逐行去碰磁盘。
    /// </param>
    public static FolderPurposeResult RecognizeLocally(
        FileEntry dir, FolderSummary sum, IReadOnlyList<string>? entryPoints = null)
    {
        var id = sum.Id;

        // a) 本地签名认出的软件 / 缓存
        string friendly = AppSignatures.FriendlyName(dir.FullPath) ?? "";
        if (friendly.Length > 0)
        {
            var sig = AppSignatures.Classify(dir.FullPath);
            string cat = sig != null ? AppSignatures.CategoryName(sig.Value.Key) : Loc.PurposeUnknownCategory;
            string basis = sig != null ? Loc.PurposeBasisSignature(friendly) : Loc.PurposeBasisPath(friendly);
            return new FolderPurposeResult(id, friendly, cat, basis,
                PurposeSource.Local, NeedsConfirm: false, Kind: FolderKind.Concrete);
        }

        // b) 已知对象的内部（.git / node_modules / WinSxS …）：说清是程序内部目录，且**不再下钻**
        if (IsKnownLeafDir(dir.Name))
            return new FolderPurposeResult(id, Loc.PurposeKnownInternal, Loc.PurposeUnknownCategory,
                Loc.PurposeBasisKnownLeaf(dir.Name), PurposeSource.Local, false, FolderKind.Concrete);

        // c) 明确的平台 / 游戏库容器：说清它是什么，但**保留往里找游戏的入口**
        if (IsObjectContainer(dir.FullPath))
            return new FolderPurposeResult(id, Loc.PurposeGameLibrary, Loc.PurposeCatGame,
                Loc.PurposeBasisPlatform, PurposeSource.Local, false, FolderKind.Container);

        // d) 开发项目：有工程文件 / 版本库标记（只看结构，不读内容）
        string devMark = DevProjectMark(sum);
        if (devMark.Length > 0)
            return new FolderPurposeResult(id, Loc.PurposeDevProject, Loc.PurposeCatDev,
                Loc.PurposeBasisDevMark(devMark), PurposeSource.Local, false, FolderKind.Concrete);

        // e) 系统入口本身：说明它是系统位置，但**不当作可清理结论**
        if (IsSystemEntry(dir.FullPath, entryPoints))
            return new FolderPurposeResult(id, Loc.PurposeSystemArea, Loc.PurposeCatSystem,
                Loc.PurposeBasisSystemEntry, PurposeSource.Local, true, FolderKind.Container);

        return FolderPurposeResult.None(id, ClassifyKind(dir));
    }

    /// <summary>系统目录入口（用于说明，不作为清理结论）。</summary>
    public static bool IsSystemEntry(string? path) => IsSystemEntry(path, null);

    /// <summary>系统目录入口（可传入预先解析好的入口表，避免重复碰磁盘）。</summary>
    public static bool IsSystemEntry(string? path, IReadOnlyList<string>? entryPoints)
    {
        string p = (path ?? "").Replace('/', '\\').ToLowerInvariant().TrimEnd('\\');
        if (p.Length == 0) return false;
        foreach (var e in entryPoints ?? EntryPoints())
        {
            string q = CleanListSnapshot.NormPath(e).ToLowerInvariant();
            if (p == q) return true;
        }
        return false;
    }

    /// <summary>
    /// 只看目录名/文件名判断是否开发项目标志（**不读文件内容**）。
    /// 结构标志既看文件（.sln / package.json），也看目录（.git / node_modules）。
    /// </summary>
    static string DevProjectMark(FolderSummary sum)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in sum.SampleNames) files.Add(n);
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in sum.SampleDirs) dirs.Add(n);

        if (dirs.Contains(".git") || files.Contains(".git")) return ".git";
        if (dirs.Contains(".svn") || dirs.Contains(".hg")) return ".git";
        if (files.Any(n => n.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))) return ".sln";
        if (files.Any(n => n.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))) return ".csproj";
        if (dirs.Contains("src") && files.Contains("package.json")) return "package.json";
        if (files.Contains("package.json")) return "package.json";
        if (files.Contains("Cargo.toml")) return "Cargo.toml";
        if (files.Contains("go.mod")) return "go.mod";
        if (files.Contains("pyproject.toml")) return "pyproject.toml";
        if (files.Contains("platformio.ini") || files.Contains("CMakeLists.txt")
            || files.Contains("Makefile") || files.Contains("pom.xml")
            || files.Contains("build.gradle")) return "platformio.ini";
        return "";
    }

    /// <summary>
    /// 给模型看的**有上限**摘要。不列整盘清单，不含完整私人路径（交由调用方脱敏）。
    /// </summary>
    public static string BuildAiInput(FolderSummary sum, bool sendFullPath, int maxChars)
        => BuildAiInput(sum, sendFullPath, maxChars, null);

    /// <summary>
    /// 给模型看的**有上限**摘要，外加**经白名单与脱敏筛选**的 README / 项目配置 / 清单片段。
    ///
    /// 默认口径（安全要求，不是可选项）：
    /// <list type="bullet">
    /// <item>只发目录名 + 受控结构摘要 + 文件类型分布 + 少量代表文件名 + 程序元数据；</item>
    /// <item>**不发完整路径**（<paramref name="sendFullPath"/> 为假时走脱敏），
    /// **不发完整文件清单**；</item>
    /// <item>片段只来自 <see cref="SourceSnippetReader"/> 的白名单，且已脱敏；</item>
    /// <item>「最近修改」只作为时间事实出现，**不会**被写成「最近使用」。</item>
    /// </list>
    /// </summary>
    public static string BuildAiInput(
        FolderSummary sum, bool sendFullPath, int maxChars, SnippetSet? snippets)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Loc.PurposeAiHeader);
        sb.AppendLine("name: " + sum.Name);
        sb.AppendLine("path: " + (sendFullPath ? sum.RelativePath : PathRedactor.Redact(sum.RelativePath)));
        sb.AppendLine("size: " + FileEntry.FormatSize(sum.Size));
        sb.AppendLine("subfolders: " + sum.DirectFolderCount);
        sb.AppendLine("files: " + sum.FileCount);
        if (sum.TypeMix.Count > 0) sb.AppendLine("types: " + string.Join(", ", sum.TypeMix));
        if (sum.SampleNames.Count > 0)
            sb.AppendLine("samples(" + sum.SampleNames.Count + " of " + sum.FileCount + "): "
                + string.Join(", ", sum.SampleNames));
        if (sum.SampleDirs.Count > 0)
            sb.AppendLine("subfolder names: " + string.Join(", ", sum.SampleDirs));
        if (sum.ProductHint.Length > 0) sb.AppendLine("product: " + sum.ProductHint);
        // 时间事实：只写「最后修改」，不推断「最近使用」——我们确实没有使用记录。
        if (sum.Modified != default) sb.AppendLine("last modified: " + sum.Modified.ToString("yyyy-MM-dd"));

        string body = sb.ToString();
        if (snippets is { Any: true })
        {
            string extra = Loc.PurposeAiSnippetHeader + "\n" + SourceSnippetCollector.FormatForAi(snippets);
            string combined = body + extra;
            // 片段是辅助材料：宁可截断片段，也不能把结构化摘要挤掉
            if (combined.Length <= maxChars) return combined;
            return combined[..maxChars];
        }
        return body.Length <= maxChars ? body : body[..maxChars];
    }

    /// <summary>
    /// 送出去之前自检：还有没有漏掉的密钥 / 完整用户路径。
    /// 返回 false 时调用方应当**不发**（宁可不识别也不外泄）。
    /// </summary>
    public static bool IsSafeOutbound(string? payload)
    {
        if (string.IsNullOrEmpty(payload)) return true;
        if (!PathRedactor.IsRedacted(payload)) return false;
        // 我们自己的占位符不算泄漏；其余情况必须与脱敏结果完全一致
        return SourceSnippetReader.LooksClean(payload);
    }

    /// <summary>
    /// 解析模型回复：只接受约定的四行，认不出就是没有结论（不编造）。
    /// </summary>
    public static FolderPurposeResult ParseAi(string? text, FolderId id, FolderKind kind)
    {
        if (string.IsNullOrWhiteSpace(text)) return FolderPurposeResult.None(id, kind);
        string raw = text.Length > 2000 ? text[..2000] : text;
        string name = "", cat = "", basis = "";
        foreach (var line in raw.Replace("\r\n", "\n").Split('\n'))
        {
            string l = line.Trim().TrimStart('-', '*', ' ');
            int c = l.IndexOf(':');
            int f = l.IndexOf('：');
            if (c < 0 || (f >= 0 && f < c)) c = f;
            if (c <= 0) continue;
            string k = l[..c].Trim().ToLowerInvariant();
            string v = Clip(l[(c + 1)..].Trim());
            switch (k)
            {
                case "purpose" or "用途": name = v; break;
                case "category" or "类别": cat = v; break;
                case "basis" or "依据": basis = v; break;
            }
        }
        if (name.Length == 0) return FolderPurposeResult.None(id, kind);
        // 提示词明确允许模型写「未知」：那**不是**结论，不能被当成识别成功。
        if (IsNoAnswer(name)) return FolderPurposeResult.None(id, kind);
        return new FolderPurposeResult(id, name,
            cat.Length > 0 ? cat : Loc.PurposeUnknownCategory,
            basis, PurposeSource.Ai, NeedsConfirm: true, kind, Model: "");
    }

    /// <summary>模型说「不知道」的各种写法 —— 都不算结论。</summary>
    public static bool IsNoAnswer(string? name)
    {
        string s = (name ?? "").Trim().Trim('.', '。', '!', '！').ToLowerInvariant();
        return s is "unknown" or "unclear" or "n/a" or "na" or "none" or "undetermined"
            or "未知" or "不确定" or "不清楚" or "无法判断" or "不知道" or "无法确定";
    }

    static string Clip(string s) => s.Length <= 40 ? s : s[..39] + "…";
}
