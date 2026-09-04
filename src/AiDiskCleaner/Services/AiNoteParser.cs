using System.Text.RegularExpressions;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 把 AI 回的「GOTO 路径&lt;TAB&gt;说明」写回条目的 <see cref="CleanItem.AiNote"/>。
///
/// 只写说明，绝不写 Risk / Selected —— 风险档位全部由规则（AppSignatures / CleanAnalyzer）判定，
/// 这样同一份扫描结果两次分析颜色不会漂移，AI 也不可能背着用户勾上什么东西。
/// </summary>
public static class AiNoteParser
{
    /// <summary>说明最长多少字，超了截断（表格一列放不下长篇大论）。</summary>
    public const int MaxNoteLength = 160;

    /// <summary>返回真正写进去的条数。</summary>
    public static int Apply(IEnumerable<CleanItem> batch, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var byPath = new Dictionary<string, CleanItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in batch)
            if (!string.IsNullOrEmpty(x.FullPath))
                byPath[x.FullPath] = x;

        int applied = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim().TrimStart('-', '*', '•', ' ').Trim().Trim('`');
            if (line.Length == 0) continue;

            // 主格式是 "GOTO 路径<TAB>说明"。但不少模型会省掉 GOTO 直接写 "路径<TAB>说明"，
            // 或者把整行包进反引号里。省掉 GOTO 也认——反正路径必须命中清单，编不出来。
            string body = line.StartsWith("GOTO", StringComparison.OrdinalIgnoreCase)
                ? line[4..].Trim()
                : (line.Contains('\t') ? line : "");
            if (body.Length == 0) continue;

            var parts = body.Split('\t')
                .Select(CleanCell).Where(s => s.Length > 0).ToList();
            if (parts.Count < 2) continue;

            if (!byPath.TryGetValue(parts[0], out var item)) continue;

            // 模型不听话时常见两种噪音：把风险词塞进第二列，或者照抄输入里的文件大小。
            // 跳过它们，取后面真正像说明的部分；一路都是噪音就丢掉这条。
            int noteAt = 1;
            while (noteAt < parts.Count && LooksLikeNoise(parts[noteAt]))
                noteAt++;
            if (noteAt >= parts.Count) continue;

            string note = string.Join(" ", parts.Skip(noteAt)).Trim();
            if (note.Length > MaxNoteLength) note = note[..(MaxNoteLength - 1)] + "…";
            if (note.Length == 0) continue;

            item.AiNote = note;
            applied++;
        }
        return applied;
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
