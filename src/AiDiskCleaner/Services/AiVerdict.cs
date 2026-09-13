using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 用户能直接理解的三种归类。**这是界面上的建议分类，不是删除许可**；
/// 是否真有清理资格仍由本地规则决定（见 <see cref="AiVerdict.IsCleanable"/>）。
/// </summary>
public enum AiBucket
{
    /// <summary>本组每一项都符合本地清理资格、且无需额外确认。</summary>
    Cleanable,
    /// <summary>受保护 / 本地判定为保留的内容。</summary>
    Keep,
    /// <summary>需要确认：本地标记为需确认，或本地信息不足以给出结论。</summary>
    Review,
}

/// <summary>一个用户可读的分组：名称、数量、空间、一句说明、能不能快捷选。</summary>
public sealed record AiBucketResult(
    AiBucket Kind,
    string Name,
    IReadOnlyList<CleanItem> Items,
    long Bytes,
    string Reason,
    bool CanSelect)
{
    public int Count => Items.Count;
    public string StatText => Loc.AiBucketStat(Count, FileEntry.FormatSize(Bytes));
}

/// <summary>
/// AI 结果界面的**结论模型**：一句结论 + 一句说明 + 下一步。
///
/// 纯函数、不碰 UI、不调模型。设计目标是让用户第一眼就知道
/// 「选多少、能腾多少、下一步点什么」，而不是自己去读技术细节。
/// </summary>
public sealed record AiVerdictResult(
    string Headline,
    string Note,
    IReadOnlyList<AiBucketResult> Buckets,
    IReadOnlyList<CleanItem> SelectableItems,
    IReadOnlyList<string> Why,
    bool Mixed)
{
    /// <summary>可直接清理的项数（顶部结论与「选择这些文件」用的是同一个数）。</summary>
    public int CleanableCount => Buckets.Where(b => b.Kind == AiBucket.Cleanable).Sum(b => b.Count);

    /// <summary>可直接清理的空间（顶部结论与按钮用的是同一个数）。</summary>
    public long CleanableBytes => Buckets.Where(b => b.Kind == AiBucket.Cleanable).Sum(b => b.Bytes);

    public bool CanSelect => SelectableItems.Count > 0;

    public string SelectText => Loc.AiSelectTheseFiles;

    /// <summary>只有内容确实混在一起时才显示分组卡片。</summary>
    public bool ShowBuckets => Mixed;

    public static AiVerdictResult Empty => new("", "", Array.Empty<AiBucketResult>(),
        Array.Empty<CleanItem>(), Array.Empty<string>(), false);
}

public static class AiVerdict
{
    /// <summary>
    /// 单个条目是否**符合本地清理资格且无需额外确认**。
    /// 与 <see cref="CandidateGroups"/> 同一口径：只收窄，不看 AI，不看是否已选。
    /// </summary>
    public static bool IsCleanable(CleanItem x)
        => x.CanDelete && x.Risk != CleanRisk.Keep && x.Risk != CleanRisk.Confirm;

    /// <summary>受保护 / 保留：本地明确不让删的。</summary>
    static bool IsKeep(CleanItem x)
        => !x.CanDelete || x.Risk == CleanRisk.Keep;

    /// <summary>
    /// 根据位置里的条目（和可选的模型短结论）算出界面要显示的结论。
    /// </summary>
    public static AiVerdictResult Build(
        IReadOnlyList<CleanItem> items,
        string localIdentity,
        ItemAiResult? ai)
    {
        if (items.Count == 0) return AiVerdictResult.Empty;

        var cleanable = items.Where(IsCleanable).ToList();
        var keep = items.Where(x => IsKeep(x) && !IsCleanable(x)).ToList();
        var review = items.Where(x => !IsCleanable(x) && !IsKeep(x)).ToList();

        var buckets = new List<AiBucketResult>();
        if (cleanable.Count > 0)
            buckets.Add(new AiBucketResult(AiBucket.Cleanable, Loc.AiBucketCleanable, cleanable,
                Sum(cleanable), Loc.AiReasonCleanable, true));
        if (review.Count > 0)
            buckets.Add(new AiBucketResult(AiBucket.Review, Loc.AiBucketReview, review,
                Sum(review), Loc.AiReasonReview, false));
        if (keep.Count > 0)
            buckets.Add(new AiBucketResult(AiBucket.Keep, Loc.AiBucketKeep, keep,
                Sum(keep), Loc.AiReasonKeep, false));

        // 只有一种归类时不摆分组卡片 —— 别为了好看强行拆成三张卡
        bool mixed = buckets.Count > 1;

        int total = items.Count;
        long totalBytes = Sum(items);
        string purpose = ai != null && !string.IsNullOrWhiteSpace(ai.Purpose) ? ai.Purpose.Trim() : "";
        string headline;
        if (cleanable.Count > 0)
            headline = Loc.AiHeadlineClean(cleanable.Count, FileEntry.FormatSize(Sum(cleanable)));
        else if (purpose.Length > 0 && review.Count > 0)
            headline = Loc.AiHeadlineIdentifiedReview(purpose, review.Count, FileEntry.FormatSize(Sum(review)));
        else if (purpose.Length > 0)
            headline = Loc.AiHeadlineIdentified(purpose);
        else if (review.Count > 0)
            headline = Loc.AiHeadlineReview(review.Count, FileEntry.FormatSize(Sum(review)));
        else
            headline = Loc.AiHeadlineNothing;

        return new AiVerdictResult(headline, BuildNote(items, localIdentity, ai, cleanable),
            buckets, cleanable, BuildWhy(items, localIdentity, ai, cleanable.Count, review.Count, keep.Count), mixed);
    }

    static long Sum(IEnumerable<CleanItem> items) => items.Sum(x => Math.Max(0, x.Size));

    /// <summary>
    /// 一句说明：优先用「本地认出来的身份」+ 模型给的删除影响；
    /// 都没有就如实说无法确认，不编造。
    /// </summary>
    static string BuildNote(IReadOnlyList<CleanItem> items, string identity, ItemAiResult? ai,
        IReadOnlyList<CleanItem> cleanable)
    {
        var bits = new List<string>();

        string what = "";
        if (ai != null && !string.IsNullOrWhiteSpace(ai.Purpose)) what = ai.Purpose.Trim();
        else if (!string.IsNullOrWhiteSpace(identity)) what = identity.Trim();
        else
        {
            var sig = AppSignatures.FriendlyName(items[0].FullPath);
            if (!string.IsNullOrWhiteSpace(sig)) what = sig!;
            else if (!string.IsNullOrWhiteSpace(items[0].Reason)) what = items[0].Reason.Trim();
        }
        if (what.Length > 0) bits.Add(Loc.AiNoteBelongs(what));

        if (ai != null && !string.IsNullOrWhiteSpace(ai.Impact))
            bits.Add(Clip(ai.Impact.Trim()));
        else if (cleanable.Count > 0)
            // 只有本地规则这一条依据时就说规则本身，不替用户下「没被占用」的结论
            bits.Add(Loc.AiNoteByLocalRule);

        if (bits.Count == 0) bits.Add(Loc.AiNoteUnknown);
        return string.Join("，", bits) + "。";
    }

    /// <summary>「为什么这样建议？」里的短依据。全部来自本地事实，不吹不编。</summary>
    static IReadOnlyList<string> BuildWhy(IReadOnlyList<CleanItem> items, string identity,
        ItemAiResult? ai, int cleanable, int review, int keep)
    {
        var why = new List<string>();

        string where = !string.IsNullOrWhiteSpace(identity)
            ? identity.Trim()
            : (items[0].Reason ?? "").Trim();
        if (where.Length > 0) why.Add(Loc.AiWhyLocation(Clip(where, 60)));

        var modified = items[0].Entry?.Modified ?? default;
        if (modified != default)
            why.Add(Loc.AiWhyModified(AgeText(modified)));

        if (!string.IsNullOrWhiteSpace(items[0].Tech)) why.Add(Loc.AiWhyMatched(Clip(items[0].Tech!, 60)));

        why.Add(Loc.AiWhyNoContentRead);
        why.Add(Loc.AiWhyNoLockCheck);

        if (review > 0) why.Add(Loc.AiWhySomeUncertain(review));
        if (keep > 0) why.Add(Loc.AiWhyProtected(keep));
        if (cleanable == 0) why.Add(Loc.AiWhyNothingSafe);
        if (ai == null) why.Add(Loc.AiWhyNoModel);

        return why;
    }

    static string AgeText(DateTime t)
    {
        var days = (DateTime.Now - t).TotalDays;
        if (days < 1) return Loc.AiAgeToday;
        if (days < 30) return Loc.AiAgeDays((int)days);
        if (days < 365) return Loc.AiAgeMonths((int)(days / 30));
        return Loc.AiAgeYears((int)(days / 365));
    }

    static string Clip(string s, int max = 80) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
