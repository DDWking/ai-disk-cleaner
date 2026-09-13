using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AiDiskCleaner.Services;

/// <summary>一条经过白名单筛选、脱敏后的文本片段。</summary>
public sealed record SourceSnippet(string FileName, string Text, bool Truncated)
{
    /// <summary>回报给用户的来源标注（只有文件名，没有路径）。</summary>
    public string Label => Truncated ? FileName + "…" : FileName;
}

/// <summary>一次片段收集的结果，以及**为什么没读**（如实说明，不假装看过）。</summary>
public sealed record SnippetSet(IReadOnlyList<SourceSnippet> Snippets, int SkippedCount, int RedactionCount)
{
    public static SnippetSet Empty { get; } = new(Array.Empty<SourceSnippet>(), 0, 0);
    public bool Any => Snippets.Count > 0;
}

/// <summary>
/// 允许送给模型的**少量辅助材料**：README / 项目配置 / 清单文件。
///
/// 边界（这是安全要求，不是优化）：
/// <list type="bullet">
/// <item><b>白名单</b>：只按文件名认，认不出一律不读 —— 普通文件正文永远不读；</item>
/// <item><b>体积上限</b>：单文件与合计字符数都有硬上限，超了截断并如实标注；</item>
/// <item><b>只读直接子级</b>：不递归进子目录，不列完整文件清单；</item>
/// <item><b>强制脱敏</b>：密钥 / Token / 密码 / 账号 / 连接串 / 私钥块在读出来之后、
/// 送出之前一律替换成占位符；</item>
/// <item>文件不存在、没权限、是链接 —— 一律跳过并计数，**不报错、不提权**。</item>
/// </list>
///
/// 纯逻辑 + 只读文件系统访问，可以完全离线测试（用临时目录构造真实文件）。
/// </summary>
public static class SourceSnippetReader
{
    /// <summary>单个片段最多取多少字符。</summary>
    public const int PerFileChars = 600;

    /// <summary>一次最多读几个白名单文件。</summary>
    public const int MaxFiles = 3;

    /// <summary>合计最多多少字符（所有片段加起来）。</summary>
    public const int TotalChars = 1200;

    /// <summary>超过这个大小的文件直接不读（清单文件可能是打包产物）。</summary>
    public const long MaxFileBytes = 64 * 1024;

    /// <summary>脱敏占位符。</summary>
    public const string Mask = "[已脱敏]";

    // ---------------- 白名单 ----------------

    /// <summary>完整的文件名（小写比较）。</summary>
    private static readonly HashSet<string> ExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json", "cargo.toml", "go.mod", "pyproject.toml", "requirements.txt",
        "composer.json", "gemfile", "pubspec.yaml", "cmakelists.txt", "makefile",
        "manifest.json", "info.plist", "app.config", "appsettings.json", "assemblyinfo.cs",
        "platformio.ini", "build.gradle", "pom.xml", "setup.py", "setup.cfg", "environment.yml",
    };

    /// <summary>前缀匹配（README.md / README.txt / …）。</summary>
    private static readonly string[] Prefixes = { "readme", "license", "licence", "changelog", "notice" };

    /// <summary>扩展名（小写，含点）。</summary>
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sln", ".csproj", ".vbproj", ".vcxproj", ".fsproj", ".props", ".targets", ".manifest",
    };

    /// <summary>
    /// 这个文件名是不是「允许读的辅助材料」。**认不出就返回 false**（默认不读）。
    /// </summary>
    public static bool IsAllowed(string? fileName)
    {
        string n = (fileName ?? "").Trim();
        if (n.Length == 0) return false;
        if (n.Length > 80) return false;                       // 畸长文件名不是正常清单
        if (ExactNames.Contains(n)) return true;
        foreach (var p in Prefixes)
            if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        if (Extensions.Contains(Path.GetExtension(n))) return true;
        return false;
    }

    /// <summary>白名单里的名字（供界面说明「可能读哪些」用，不含路径）。</summary>
    public static IReadOnlyList<string> AllowedExamples { get; } =
        new[] { "README.md", "package.json", "app.csproj", "app.sln", "go.mod", "Cargo.toml" };

    // ---------------- 脱敏 ----------------

    /// <summary>
    /// 把密钥 / Token / 密码 / 账号 / 连接串替换成占位符。
    /// 宁可多遮一点，也不能漏出去 —— 遮错了只是少一点上下文。
    /// </summary>
    public static string Redact(string? text, out int hits)
    {
        if (string.IsNullOrEmpty(text)) { hits = 0; return ""; }
        string s = text;
        int total = 0;

        s = Replace(s, SecretKeyValue, ref total);      // "apiKey": "xxxx"
        s = Replace(s, SecretAssignment, ref total);    // PASSWORD=xxxx
        s = Replace(s, WellKnownToken, ref total);      // sk- / ghp_ / AKIA / eyJ…
        s = Replace(s, BearerToken, ref total);         // Authorization: Bearer xxxx
        s = Replace(s, UrlCredentials, ref total);      // https://user:pass@host
        s = Replace(s, WindowsPath, ref total);         // C:\Users\<name>\…
        s = Replace(s, LongBase64, ref total);          // 很长的疑似编码串
        hits = total;
        return s;
    }

    /// <summary>
    /// 只是问「干不干净」，不改内容。
    ///
    /// 判据：脱敏结果去掉自己写入的占位符之后，必须与原文**完全一致** ——
    /// 说明这条文本里没有可被识别出来的密钥/账号/路径。
    /// 先消掉占位符是为了让**已经脱敏过的文本**也能自检通过（幂等）。
    /// </summary>
    public static bool LooksClean(string? text)
    {
        string src = text ?? "";
        if (src.Length == 0) return true;
        string mine = src.Replace(Mask, "", StringComparison.Ordinal);
        string red = Redact(src, out _).Replace(Mask, "", StringComparison.Ordinal);
        return string.Equals(mine, red, StringComparison.Ordinal);
    }

    static string Replace(string input, Regex re, ref int hits)
    {
        if (!re.IsMatch(input)) return input;
        // 计数先在委托外面累计（lambda 里不能碰 ref 参数）
        int local = 0;
        string outp = re.Replace(input, m =>
        {
            local++;
            // 保留键名 / 前缀，只把值换掉：模型仍然知道「这里有个配置项」
            return m.Groups["keep"].Success && m.Groups["keep"].Value.Length > 0
                ? m.Groups["keep"].Value + Mask
                : Mask;
        });
        hits += local;
        return outp;
    }

    const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    /// <summary>JSON / YAML 风格的「敏感键: 值」。</summary>
    static readonly Regex SecretKeyValue = new(
        """(?<keep>["']?(?<k>api[_-]?key|apikey|secret|client[_-]?secret|access[_-]?token|refresh[_-]?token|auth[_-]?token|token|password|passwd|pwd|credential|private[_-]?key|connection[_-]?string|conn[_-]?string|account[_-]?key|sas[_-]?token|session[_-]?id|cookie)["']?\s*[:=]\s*["']?)[^"',;\r\n}]{2,}""",
        Opt);

    /// <summary>环境变量 / ini 风格：KEY=值。</summary>
    static readonly Regex SecretAssignment = new(
        @"(?<keep>\b[A-Za-z0-9_]*(?:KEY|TOKEN|SECRET|PASSWORD|PASSWD|PWD|CREDENTIAL|ACCOUNT|SIGNATURE|SALT|CERT)[A-Za-z0-9_]*\s*=\s*)[^\s""';,\r\n]{2,}",
        Opt);

    /// <summary>已知的密钥前缀（OpenAI / GitHub / AWS / JWT / Slack / Google…）。</summary>
    static readonly Regex WellKnownToken = new(
        @"\b(?:sk-[A-Za-z0-9_\-]{8,}|sk-proj-[A-Za-z0-9_\-]{8,}|gh[pousr]_[A-Za-z0-9]{16,}|github_pat_[A-Za-z0-9_]{16,}|AKIA[0-9A-Z]{12,}|ASIA[0-9A-Z]{12,}|xox[baprs]-[A-Za-z0-9\-]{8,}|AIza[0-9A-Za-z_\-]{20,}|ya29\.[A-Za-z0-9_\-]{10,}|eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{4,})\b",
        Opt);

    static readonly Regex BearerToken = new(
        @"(?<keep>\b(?:authorization|bearer|basic)\b\s*[:=]?\s*(?:bearer\s+|basic\s+)?)[A-Za-z0-9._\-+/=]{8,}",
        Opt);

    /// <summary>URL 里的 user:password@host。</summary>
    static readonly Regex UrlCredentials = new(
        @"(?<keep>\b[a-z][a-z0-9+.\-]*://[^/\s:@]{1,64}:)[^@/\s]{2,}(?=@)",
        Opt);

    /// <summary>路径里的用户名段（保守：只处理常见的 Users 段，不猜别的）。</summary>
    static readonly Regex WindowsPath = new(
        @"(?<keep>[A-Za-z]:\\Users\\)[^\\\r\n""']{1,64}",
        Opt);

    /// <summary>很长的 base64 / hex：不像人写的说明文字，更像密钥材料。</summary>
    static readonly Regex LongBase64 = new(
        @"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{40,}={0,2}(?![A-Za-z0-9+/])",
        Opt);
}

/// <summary>
/// 真正去读片段的入口。**只读直接子文件、只读白名单名字、读完必脱敏**。
/// 所有异常都被吞掉并计数：权限不足 / 文件消失 / 路径太长都不能打断识别。
/// </summary>
public static class SourceSnippetCollector
{
    /// <summary>
    /// 从一个目录里收集片段。传 <paramref name="reader"/> 可以在测试里换成假实现。
    /// </summary>
    public static SnippetSet Collect(
        string? directory,
        Func<string, string?>? reader = null,
        int maxFiles = SourceSnippetReader.MaxFiles,
        int perFile = SourceSnippetReader.PerFileChars,
        int totalChars = SourceSnippetReader.TotalChars)
    {
        if (string.IsNullOrWhiteSpace(directory)) return SnippetSet.Empty;
        var picked = new List<string>();
        int skipped = 0;
        try
        {
            var dir = new DirectoryInfo(directory);
            if (!dir.Exists) return SnippetSet.Empty;
            foreach (var fi in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    // 白名单之外的文件**一律不读**，并且如实计数（不假装目录里什么都没有）
                    if (!SourceSnippetReader.IsAllowed(fi.Name)) { skipped++; continue; }
                    if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint)) { skipped++; continue; }
                    if (fi.Length <= 0 || fi.Length > SourceSnippetReader.MaxFileBytes) { skipped++; continue; }
                    picked.Add(fi.FullName);
                }
                catch { skipped++; }
            }
        }
        catch
        {
            // 目录不可读：如实返回「什么都没读到」，不抛、不提权
            return new SnippetSet(Array.Empty<SourceSnippet>(), 1, 0);
        }

        // 名字短的更可能是 README / 清单，先读它们
        picked.Sort((a, b) => Path.GetFileName(a).Length.CompareTo(Path.GetFileName(b).Length));

        var outList = new List<SourceSnippet>();
        int redactions = 0;
        int used = 0;
        foreach (var path in picked)
        {
            if (outList.Count >= maxFiles || used >= totalChars) break;
            string? raw = null;
            try { raw = reader != null ? reader(path) : ReadCapped(path, perFile * 2); }
            catch { skipped++; }
            if (string.IsNullOrEmpty(raw)) { skipped++; continue; }

            string clean = Normalize(raw);
            string red = SourceSnippetReader.Redact(clean, out int hits);
            redactions += hits;

            int room = Math.Min(perFile, totalChars - used);
            if (room <= 0) break;
            bool cut = red.Length > room;
            string text = cut ? red[..room] : red;
            outList.Add(new SourceSnippet(Path.GetFileName(path), text, cut));
            used += text.Length;
        }
        return new SnippetSet(outList, skipped, redactions);
    }

    /// <summary>有上限地读一个文本文件（不读整个大文件）。</summary>
    static string ReadCapped(string path, int maxChars)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int cap = Math.Max(64, Math.Min(maxChars, (int)SourceSnippetReader.MaxFileBytes));
        var buf = new byte[cap];
        int n = fs.Read(buf, 0, buf.Length);
        if (n <= 0) return "";
        // BOM 去掉，避免片段开头带不可见字符
        int start = n >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF ? 3 : 0;
        string s = new UTF8Encoding(false).GetString(buf, start, n - start);
        // 非法字节解出来的替换字符会让片段变噪音，直接丢掉
        return s.Replace('\uFFFD', ' ');
    }

    /// <summary>压掉多余空白与空行：片段要短，且不能靠换行藏东西。</summary>
    static string Normalize(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        bool lastSpace = false;
        foreach (char c in raw)
        {
            if (c is '\r' or '\n' or '\t') { if (!lastSpace) { sb.Append(' '); lastSpace = true; } continue; }
            if (char.IsControl(c)) continue;
            sb.Append(c);
            lastSpace = c == ' ';
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// 把片段拼成给模型的文本行。**只有文件名与脱敏内容**，不含完整路径。
    /// </summary>
    public static string FormatForAi(SnippetSet set)
    {
        if (!set.Any) return "";
        var sb = new StringBuilder();
        foreach (var s in set.Snippets)
            sb.Append("snippet[").Append(s.FileName).Append("]: ").Append(s.Text).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }
}
