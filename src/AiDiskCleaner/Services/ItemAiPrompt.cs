using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>AI 对**单个项目**（一个文件或一个清理位置）给出的建议档位。</summary>
public enum ItemAiSuggestion
{
    /// <summary>信息不足 —— 模型自己说不清，或返回的东西不合法。</summary>
    Unknown = 0,
    CanConsider,
    NeedsConfirm,
    SuggestKeep,
}

/// <summary>
/// 一次逐项分析请求的**输入范围**。刻意只带必要元数据：
/// 脱敏路径、大小、修改时间、类型、命中的本地规则。
/// **不读文件内容、不上传内容。**
/// </summary>
public sealed record ItemAiRequest(
    string ScopeKey,
    bool IsFolder,
    string Path,
    string Label,
    long Size,
    DateTime Modified,
    string Kind,
    string LocalReason,
    /// <summary>文件夹的有上限目录摘要（只取已知扫描数据里最大的若干直接子项，不做遍历）。</summary>
    IReadOnlyList<string> FolderSummary,
    /// <summary>摘要覆盖了多少个子项里的前几个 —— 必须如实告诉模型和用户。</summary>
    int FolderSummaryShown,
    /// <summary>该文件夹已知的直接子项总数。</summary>
    int FolderChildTotal);

/// <summary>一次逐项分析的结果。字段就是界面上要展示的五项。</summary>
public sealed record ItemAiResult(
    ItemAiSuggestion Suggestion,
    string Purpose,
    string Impact,
    string Basis,
    string Missing,
    string Raw,
    bool FromCache,
    double QueueMs,
    double SendMs,
    double ParseMs,
    string Channel,
    long CacheVersion)
{
    public static ItemAiResult Empty(long version) =>
        new(ItemAiSuggestion.Unknown, "", "", "", "", "", false, 0, 0, 0, "", version);

    /// <summary>模型有回内容，但一项可用信息都没有。</summary>
    public bool Barren => string.IsNullOrWhiteSpace(Purpose)
                          && string.IsNullOrWhiteSpace(Impact)
                          && string.IsNullOrWhiteSpace(Basis);
}

/// <summary>缓存的键：项目身份 + 元数据版本 + 提示词版本 + 脱敏配置。任一项变了就过期。</summary>
public sealed record ItemAiCacheKey(string ScopeKey, long Version);

/// <summary>
/// 逐项 AI 分析的纯逻辑：缓存、键计算、提示词、解析、建议档位收敛。
/// 抽出来是为了**能在离线测试里覆盖**（缓存失效、范围上限、空响应、非法建议）。
/// 网络与并发编排在 <see cref="ItemAiService"/> 里。
/// </summary>
public static class ItemAiPrompt
{
    /// <summary>提示词版本：改了提示词/格式就 +1，旧缓存自动过期。</summary>
    public const int Version = 4;

    /// <summary>文件夹摘要最多列几项（有上限，不遍历整个目录）。</summary>
    public const int MaxFolderSummary = 12;

    /// <summary>每个字段的回答长度上限（先给短结论，不让模型写长篇）。</summary>
    public const int MaxFieldLength = 120;

    /// <summary>模型原始回复的解析上限。</summary>
    public const int MaxRawChars = 4000;

    public static readonly ItemAiSuggestion[] Allowed =
    {
        ItemAiSuggestion.CanConsider, ItemAiSuggestion.NeedsConfirm, ItemAiSuggestion.SuggestKeep,
    };

    /// <summary>
    /// 元数据版本：只有这些字段决定缓存是否有效。
    /// 路径本身在 key 里，所以这里放大小/时间/类型/规则/配置。
    /// </summary>
    public static long VersionOf(ItemAiRequest r, string configSignature)
    {
        long h = 17;
        void Mix(string s)
        {
            unchecked
            {
                foreach (char c in s ?? "") h = h * 31 + c;
            }
        }
        Mix(r.Path.ToLowerInvariant());
        Mix(r.Size.ToString());
        Mix(r.Modified.Ticks.ToString());
        Mix(r.Kind);
        Mix(r.LocalReason);
        Mix(configSignature);
        Mix(Version.ToString());
        Mix(r.IsFolder ? $"folder:{r.FolderChildTotal}:{r.FolderSummaryShown}" : "file");
        foreach (var s in r.FolderSummary) Mix(s);
        return h;
    }

    public static string SuggestionName(ItemAiSuggestion s) => s switch
    {
        ItemAiSuggestion.CanConsider => Loc.AiSuggestCanConsider,
        ItemAiSuggestion.NeedsConfirm => Loc.AiSuggestNeedsConfirm,
        ItemAiSuggestion.SuggestKeep => Loc.AiSuggestKeep,
        _ => Loc.AiSuggestUnknown,
    };

    /// <summary>
    /// 拼给模型的输入。只含元数据与摘要，不含文件内容。
    /// 有本地分组时，把**分组摘要**一并给它 —— 让模型针对具体子组说话，
    /// 而不是对整个目录下一个笼统结论。
    /// </summary>
    public static string BuildUser(ItemAiRequest r, bool sendFullPaths)
    {
        var sb = new System.Text.StringBuilder();
        string path = sendFullPaths ? r.Path : PathRedactor.Redact(r.Path);
        sb.AppendLine(r.IsFolder ? Loc.AiItemFolderHeader : Loc.AiItemFileHeader);
        sb.AppendLine("path: " + path);
        sb.AppendLine("size: " + FileEntry.FormatSize(r.Size));
        sb.AppendLine("modified: " + (r.Modified == default ? "unknown" : r.Modified.ToString("yyyy-MM-dd")));
        sb.AppendLine("type: " + r.Kind);
        sb.AppendLine("local-rule: " + r.LocalReason);

        if (r.IsFolder)
        {
            // 摘要范围必须写清楚：只看过这些，不是整个目录。
            sb.AppendLine(Loc.AiItemSummaryScope(r.FolderSummaryShown, r.FolderChildTotal));
            foreach (var line in r.FolderSummary) sb.AppendLine("  " + line);
        }

        return sb.ToString();
    }



    /// <summary>
    /// 解析模型回复。**宽松**：字段缺失就留空，建议不合法就降级成「信息不足」。
    /// 绝不因为模型没说就替它编一个可删结论。
    /// </summary>
    public static ItemAiResult Parse(string? text, long version, double queueMs, double sendMs,
        double parseMs, string channel)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ItemAiResult.Empty(version) with { QueueMs = queueMs, SendMs = sendMs, ParseMs = parseMs, Channel = channel };

        string raw = text.Length > MaxRawChars ? text[..MaxRawChars] : text;

        string suggest = "", purpose = "", impact = "", basis = "", missing = "";
        foreach (var rawLine in raw.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim().TrimStart('-', '*', '•', ' ');
            if (line.Length == 0) continue;
            int colon = line.IndexOf(':');
            int full = line.IndexOf('：');
            if (colon < 0 || (full >= 0 && full < colon)) colon = full;
            if (colon <= 0) continue;
            string key = line[..colon].Trim().ToLowerInvariant();
            string val = Clip(line[(colon + 1)..].Trim());
            switch (key)
            {
                case "suggest" or "建议": suggest = val; break;
                case "purpose" or "用途": purpose = val; break;
                case "impact" or "影响" or "删除影响": impact = val; break;
                case "basis" or "依据" or "判断依据": basis = val; break;
                case "missing" or "缺少" or "缺少的信息": missing = val; break;
            }
        }

        var parsed = MapSuggestion(suggest);
        return new ItemAiResult(parsed, purpose, impact, basis, missing, raw, false,
            queueMs, sendMs, parseMs, channel, version);
    }

    /// <summary>把模型给的建议词收敛到允许的四档；认不出来就是「信息不足」。</summary>
    public static ItemAiSuggestion MapSuggestion(string? s)
    {
        string t = (s ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return ItemAiSuggestion.Unknown;
        if (t.Contains("保留") || t.Contains("keep") || t.Contains("不要动")) return ItemAiSuggestion.SuggestKeep;
        if (t.Contains("确认") || t.Contains("confirm") || t.Contains("check")) return ItemAiSuggestion.NeedsConfirm;
        if (t.Contains("考虑") || t.Contains("can consider") || t.Contains("candidate")) return ItemAiSuggestion.CanConsider;
        if (t.Contains("不足") || t.Contains("unknown") || t.Contains("insufficient")) return ItemAiSuggestion.Unknown;
        // 「安全」「可以删」这类词不在允许档位里 —— 不认，降级为信息不足
        return ItemAiSuggestion.Unknown;
    }

    static string Clip(string s) => s.Length <= MaxFieldLength ? s : s[..(MaxFieldLength - 1)] + "…";
}
