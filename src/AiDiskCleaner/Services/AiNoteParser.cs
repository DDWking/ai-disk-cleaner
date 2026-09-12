using System.Text.RegularExpressions;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 把 AI 回的「GOTO 路径&lt;TAB&gt;说明」写回条目的 <see cref="CleanItem.AiNote"/>。
///
/// 只写说明，绝不写 Risk / Selected / CanDelete —— 风险档位全部由规则（AppSignatures / CleanAnalyzer）判定，
/// 这样同一份扫描结果两次分析颜色不会漂移，AI 也不可能背着用户勾上什么东西。
///
/// 另外三条硬约束：
/// 1) 路径必须命中清单，编不出来（启用路径脱敏时按脱敏后的形式匹配，仍然编不出来）；
/// 2) 响应体有长度上限，模型吐出一本书也只解析前面一段；
/// 3) 说明长度有上限，超了截断。
/// </summary>
public static class AiNoteParser
{
    /// <summary>说明最长多少字，超了截断（表格一列放不下长篇大论）。</summary>
    public const int MaxNoteLength = 160;

    /// <summary>单次最多处理多少条（和发起分析的条数上限对齐）。</summary>
    public const int MaxItems = 60;

    /// <summary>模型响应的最大字符数。防止中转返回畸形超长文本把解析拖死。</summary>
    public const int MaxResponseChars = 20000;

    /// <summary>最多认多少行，避免畸形输出里几万行垃圾。</summary>
    public const int MaxLines = 2000;

    /// <summary>
    /// 一次解析的计数明细。**必须能回答「为什么只应用了 15 条」** ——
    /// 是模型没吐够行、路径对不上、还是全被当成噪音跳过了。
    /// 以前只返回一个 applied 数字，出问题时无从判断。
    /// </summary>
    public readonly record struct AiParseStats(
        int Lines,
        int CandidateLines,
        int PathHits,
        int NoiseSkipped,
        int Applied)
    {
        /// <summary>响应里根本没几条能认的行 ⇒ 多半是模型没按格式回答。</summary>
        public bool LooksUnformatted => CandidateLines == 0 && Lines > 0;
    }

    /// <summary>
    /// 返回真正写进去的条数。
    /// <paramref name="pathKey"/> 用于把「发给模型的路径」映射回条目 ——
    /// 默认用条目自己的 FullPath；启用脱敏时传 <see cref="PathRedactor.Redact"/>。
    /// </summary>
    public static int Apply(IEnumerable<CleanItem> batch, string text, Func<string, string>? pathKey = null)
        => ApplyWithStats(batch, text, pathKey).Applied;

    /// <summary>同上，但把「为什么只应用了 N 条」的计数一起带出来。</summary>
    public static AiParseStats ApplyWithStats(
        IEnumerable<CleanItem> batch, string text, Func<string, string>? pathKey = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;

        pathKey ??= static p => p;

        var byPath = new Dictionary<string, CleanItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in batch)
        {
            if (string.IsNullOrEmpty(x.FullPath)) continue;
            string key = pathKey(x.FullPath);
            if (string.IsNullOrEmpty(key)) continue;
            byPath[key] = x; // 脱敏后撞名时后一条覆盖前一条，绝不误写到别人的行上
        }
        if (byPath.Count == 0) return default;

        // 超长响应只解析前面一段：模型跑飞时既浪费内存也容易写出乱七八糟的东西
        if (text.Length > MaxResponseChars) text = text[..MaxResponseChars];

        int applied = 0;
        int lines = 0, candidates = 0, hits = 0, noise = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (++lines > MaxLines) break;
            string line = raw.Trim().TrimStart('-', '*', '•', ' ').Trim().Trim('`');
            if (line.Length == 0) continue;

            // 主格式是 "GOTO 路径<TAB>说明"。但不少模型会省掉 GOTO 直接写 "路径<TAB>说明"，
            // 或者把整行包进反引号里。省掉 GOTO 也认——反正路径必须命中清单，编不出来。
            string body = line.StartsWith("GOTO", StringComparison.OrdinalIgnoreCase)
                ? line[4..].Trim()
                : (line.Contains('\t') ? line : "");
            if (body.Length == 0) continue;
            candidates++;

            var parts = body.Split('\t')
                .Select(CleanCell).Where(s => s.Length > 0).ToList();
            if (parts.Count < 2) continue;

            if (!byPath.TryGetValue(parts[0], out var item)) continue;
            hits++;

            // 模型不听话时常见两种噪音：把风险词塞进第二列，或者照抄输入里的文件大小。
            // 跳过它们，取后面真正像说明的部分；一路都是噪音就丢掉这条。
            int noteAt = 1;
            while (noteAt < parts.Count && LooksLikeNoise(parts[noteAt]))
            {
                noise++;
                noteAt++;
            }
            if (noteAt >= parts.Count) continue;

            string note = string.Join(" ", parts.Skip(noteAt)).Trim();
            if (note.Length > MaxNoteLength) note = note[..(MaxNoteLength - 1)] + "…";
            if (note.Length == 0) continue;

            item.AiNote = note;
            applied++;
        }
        return new AiParseStats(lines, candidates, hits, noise, applied);
    }

    static string CleanCell(string s) => s.Trim().Trim('`', ' ', '"', '\'');

    /// <summary>这一列是不是「不是说明」的噪音：风险词或纯大小。暴露出来是为了能被测到。</summary>
    public static bool LooksLikeNoise(string cell)
        => IsRiskWord(cell) || IsSizeish(cell);

    static readonly HashSet<string> RiskWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "safe", "confirm", "caution", "check", "keep", "danger", "no", "yes",
        "可安全删除", "需确认", "别删", "安全", "危险", "可删", "不建议",
    };

    static bool IsRiskWord(string s) => RiskWords.Contains(s.Trim());

    static readonly Regex SizePattern = new(
        @"^[\d.,]+\s*(TB|GB|MB|KB|B|字节|T|G|M|K)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>像 "2.1G" / "1,024 MB" 这种纯大小的列，不是说明。</summary>
    static bool IsSizeish(string s)
    {
        string t = s.Trim();
        return t.Length > 0 && t.Length <= 14 && SizePattern.IsMatch(t);
    }
}
