using System.Globalization;
using System.Text.Json;

namespace AiDiskCleaner.Services.Agent;

/// <summary>一批模型输出的本地校验结果。格式化失败与逐条丢弃是两回事，分开表达。</summary>
public sealed class ParsedBatch
{
    public List<ReportItem> Items { get; } = new();
    public string Summary { get; set; } = "";
    public List<ReportQuestion> Questions { get; } = new();
    public List<string> Warnings { get; } = new();
    public int Dropped { get; set; }
    public string? FatalError { get; set; }
    public string? Degraded { get; set; }
}

/// <summary>
/// 把模型返回的文本变成「可展示的候选建议」。
///
/// 这是整个 agent 的安全闸门：任何无法对回本地证据卡的内容都在这里被丢弃，
/// 绝不静默修正。03 §6.3 的流水线按顺序实现（合并同父建议一步留到 UI 定型后）。
/// </summary>
public static class ReportParser
{
    public const int MaxItems = 40;
    public const int MaxWhyChars = 60;
    public const int MaxRiskChars = 40;
    public const int MaxSummaryChars = 200;
    public const int MaxEvidenceRefs = 6;

    public static ParsedBatch Parse(string? raw, ScanSession session, IReadOnlyList<EvidenceCard> cards)
    {
        var batch = new ParsedBatch();
        string text = ExtractJson(raw);
        if (text.Length == 0)
        {
            batch.FatalError = "模型没有返回 JSON";
            return batch;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            batch.FatalError = "模型输出的 JSON 无法解析";
            return batch;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                batch.FatalError = "模型输出不是 JSON 对象";
                return batch;
            }

            if (!root.TryGetProperty("reportVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != 1)
            {
                batch.FatalError = "reportVersion 不是 1";
                return batch;
            }

            string scanId = Str(root, "scanId");
            if (!string.Equals(scanId, session.ScanId, StringComparison.Ordinal))
            {
                batch.FatalError = "scanId 与本次扫描不一致";
                return batch;
            }

            batch.Summary = Truncate(Str(root, "summary"), MaxSummaryChars);
            batch.Degraded = OptionalStr(root, "degraded");

            if (root.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
            {
                int taken = 0;
                foreach (var q in questions.EnumerateArray())
                {
                    if (taken >= 3) break;
                    if (q.ValueKind != JsonValueKind.Object) continue;
                    string text2 = Truncate(Str(q, "text"), 100);
                    if (text2.Length == 0) continue;
                    batch.Questions.Add(new ReportQuestion(text2, OptionalStr(q, "suggestedTool")));
                    taken++;
                }
            }

            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                batch.Warnings.Add("模型没有给出 items");
                return batch;
            }

            var byFolder = new Dictionary<string, EvidenceCard>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in cards)
                byFolder[card.FolderId] = card;

            int index = 0;
            foreach (var element in items.EnumerateArray())
            {
                if (index >= MaxItems)
                {
                    batch.Warnings.Add($"items 超过 {MaxItems} 条，已截断");
                    break;
                }

                index++;
                var item = ValidateItem(element, byFolder, batch);
                if (item != null) batch.Items.Add(item);
            }
        }

        return batch;
    }

    static ReportItem? ValidateItem(JsonElement element, Dictionary<string, EvidenceCard> byFolder, ParsedBatch batch)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            batch.Dropped++;
            batch.Warnings.Add("丢弃一条：item 不是对象");
            return null;
        }

        string folderId = Str(element, "folderId");
        if (!byFolder.TryGetValue(folderId, out var card))
        {
            batch.Dropped++;
            batch.Warnings.Add("丢弃一条：folderId 不在本批证据卡里");
            return null;
        }

        if (!TryVerdict(Str(element, "verdict"), out var verdict))
        {
            batch.Dropped++;
            batch.Warnings.Add($"丢弃 {card.FolderId}：verdict 不在枚举内");
            return null;
        }

        var refs = new List<string>();
        if (element.TryGetProperty("evidenceRefs", out var evidence) && evidence.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in evidence.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.String) continue;
                string value = (r.GetString() ?? "").Trim();
                if (value.Length > 0) refs.Add(value);
            }
        }

        if (refs.Count == 0)
        {
            batch.Dropped++;
            batch.Warnings.Add($"丢弃 {card.FolderId}：没有 evidenceRefs");
            return null;
        }

        if (refs.Count > MaxEvidenceRefs)
        {
            batch.Warnings.Add($"{card.FolderId} 的 evidenceRefs 超过 {MaxEvidenceRefs} 个，已截断");
            refs = refs.Take(MaxEvidenceRefs).ToList();
        }

        var resolved = new List<ReportEvidence>();
        bool hasCardRefForSelf = false;
        foreach (var reference in refs)
        {
            var parsed = ResolveEvidence(reference, card, byFolder);
            if (parsed == null)
            {
                batch.Dropped++;
                batch.Warnings.Add($"丢弃 {card.FolderId}：证据引用无法对回本地事实（{reference}）");
                return null;
            }

            if (parsed.Target == "folder" && string.Equals(parsed.Ref, "card:" + card.FolderId, StringComparison.OrdinalIgnoreCase))
                hasCardRefForSelf = true;
            resolved.Add(parsed);
        }

        if (!hasCardRefForSelf)
        {
            batch.Dropped++;
            batch.Warnings.Add($"丢弃 {card.FolderId}：缺少指向自己的 card: 引用");
            return null;
        }

        long size = card.SizeBytes;
        long reclaimable = Long(element, "reclaimableBytes") ?? 0;
        if (reclaimable < 0) reclaimable = 0;
        if (reclaimable == 0 && (verdict == ReportVerdict.Recommend || verdict == ReportVerdict.Consider))
            reclaimable = size;
        if (reclaimable > size) reclaimable = size;

        double confidence = Num(element, "confidence") ?? 0;
        confidence = Math.Clamp(confidence, 0, 1);

        string title = BuildCardTitle(card);

        return new ReportItem
        {
            FolderId = card.FolderId,
            Title = title,
            RelPath = card.RelPath ?? "",
            SizeBytes = size,
            ReclaimableBytes = reclaimable,
            Verdict = verdict,
            Confidence = confidence,
            Why = Truncate(Str(element, "why"), MaxWhyChars),
            Risk = Truncate(Str(element, "risk"), MaxRiskChars),
            Evidence = resolved,
        };
    }

    /// <summary>把一条 evidenceRef 对回卡片里的既有事实；对不上就返回 null（整条丢弃）。</summary>
    static ReportEvidence? ResolveEvidence(string reference, EvidenceCard card, Dictionary<string, EvidenceCard> byFolder)
    {
        int colon = reference.IndexOf(':');
        if (colon <= 0 || colon == reference.Length - 1) return null;

        string kind = reference[..colon].Trim().ToLowerInvariant();
        string value = reference[(colon + 1)..].Trim();
        if (value.Length == 0) return null;

        switch (kind)
        {
            case "card":
                if (!byFolder.ContainsKey(value)) return null;
                return new ReportEvidence
                {
                    Ref = reference,
                    Label = "证据卡 · " + (byFolder[value].RelPath ?? value),
                    Target = "folder",
                };

            case "rule":
                if (!card.Rules.Any(rule => string.Equals(rule.Id, value, StringComparison.OrdinalIgnoreCase))) return null;
                return new ReportEvidence { Ref = reference, Label = "规则：" + value, Target = "rule" };

            case "app":
                bool appMatch = string.Equals(card.AppKey, value, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(card.ProductHint)
                        && card.ProductHint.Contains(value, StringComparison.OrdinalIgnoreCase));
                if (!appMatch) return null;
                return new ReportEvidence
                {
                    Ref = reference,
                    Label = "软件：" + (string.IsNullOrWhiteSpace(card.ProductHint) ? value : card.ProductHint),
                    Target = "app",
                };

            case "age":
                int? days = ParseAgeDays(value);
                if (days == null || Math.Abs(days.Value - card.AgeDays) > 1) return null;
                return new ReportEvidence { Ref = reference, Label = "年龄：" + card.AgeDays + " 天", Target = "age" };

            case "size":
                long? size = ParseSize(value);
                if (size == null) return null;
                long tolerance = Math.Max(1, card.SizeBytes / 100);
                if (Math.Abs(size.Value - card.SizeBytes) > tolerance) return null;
                return new ReportEvidence { Ref = reference, Label = "大小：" + FormatSize(card.SizeBytes), Target = "size" };

            case "lock":
                if (!string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)) return null;
                return new ReportEvidence { Ref = reference, Label = "占用：未检测", Target = "lock" };

            default:
                // dup: 需要 find_duplicates 的数据；P1 没有，直接拒绝。
                return null;
        }
    }

    static bool TryVerdict(string value, out ReportVerdict verdict)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "recommend": verdict = ReportVerdict.Recommend; return true;
            case "consider": verdict = ReportVerdict.Consider; return true;
            case "keep": verdict = ReportVerdict.Keep; return true;
            case "unknown": verdict = ReportVerdict.Unknown; return true;
            default: verdict = ReportVerdict.Unknown; return false;
        }
    }

    static int? ParseAgeDays(string value)
    {
        string text = value.Trim().TrimEnd('d', 'D', '天');
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int days) && days >= 0 ? days : null;
    }

    /// <summary>接受 3221225472、3.0GB、3 GB、512MB 这些写法。</summary>
    public static long? ParseSize(string value)
    {
        string text = value.Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        if (text.Length == 0) return null;

        string number = text;
        long multiplier = 1;
        if (text.EndsWith("TB", StringComparison.Ordinal)) { number = text[..^2]; multiplier = 1L << 40; }
        else if (text.EndsWith("GB", StringComparison.Ordinal)) { number = text[..^2]; multiplier = 1L << 30; }
        else if (text.EndsWith("MB", StringComparison.Ordinal)) { number = text[..^2]; multiplier = 1L << 20; }
        else if (text.EndsWith("KB", StringComparison.Ordinal)) { number = text[..^2]; multiplier = 1L << 10; }
        else if (text.EndsWith("B", StringComparison.Ordinal)) { number = text[..^1]; }

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)) return null;
        if (amount < 0 || double.IsNaN(amount) || double.IsInfinity(amount)) return null;
        double bytes = amount * multiplier;
        if (bytes > long.MaxValue) return null;
        return (long)bytes;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? bytes.ToString(CultureInfo.InvariantCulture) + " B"
            : value.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>去掉 markdown 围栏与前后闲话，只留第一个 { 到最后一个 }。</summary>
    internal static string ExtractJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return "";
        return raw.Substring(start, end - start + 1);
    }

    static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    static string BuildCardTitle(EvidenceCard card)
    {
        string folderName = LastSegment(card.RelPath);
        if (string.Equals(folderName, "%USER%", StringComparison.OrdinalIgnoreCase))
            folderName = "当前用户目录";

        string hint = (card.ProductHint ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(folderName) && !string.IsNullOrWhiteSpace(hint))
        {
            if (folderName.Contains(hint, StringComparison.OrdinalIgnoreCase))
                return folderName;
            if (hint.Contains(folderName, StringComparison.OrdinalIgnoreCase))
                return hint;
            return $"{folderName} · {hint}";
        }

        if (!string.IsNullOrWhiteSpace(folderName)) return folderName;
        if (!string.IsNullOrWhiteSpace(hint)) return hint;
        return card.FolderId;
    }

    static string LastSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string trimmed = path.TrimEnd('\\');
        int slash = trimmed.LastIndexOf('\\');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    static string Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    static string? OptionalStr(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static double? Num(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    static long? Long(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;
}
