using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AiDiskCleaner.Models;

/// <summary>逐项 AI 的状态。UI 只读这个。</summary>
public enum ItemAiStatus
{
    Idle,
    Queued,
    Running,
    Done,
    NoUseful,
    Failed,
    Canceled,
    Timeout,
}

/// <summary>
/// 挂在**单个项目**（一个文件 / 一个清理位置）上的 AI 视图状态。
///
/// 关键点：状态挂在项目自己的稳定标识上，而不是挂在虚拟化出来的行控件或「当前选中行」上 ——
/// 滚动回收、切页、重扫都不会让结果串到别的项目。
/// </summary>
public sealed class ItemAiView : INotifyPropertyChanged
{
    private ItemAiStatus _status = ItemAiStatus.Idle;
    private Services.ItemAiResult? _result;
    private bool _expanded;
    private string _error = "";

    /// <summary>稳定标识（位置键或文件完整路径）。结果靠它归属，不靠行控件。</summary>
    public required string ScopeKey { get; init; }

    /// <summary>在飞请求的版本号：晚回来的旧请求不许覆盖新状态。</summary>
    public int RequestId { get; set; }

    public ItemAiStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            Raise();
            Raise(nameof(StatusText));
            Raise(nameof(IsBusy));
            Raise(nameof(CanAnalyze));
            Raise(nameof(ButtonText));
            Raise(nameof(HasResult));
            Raise(nameof(IsExpanded));
            Raise(nameof(ProgressText));
            Raise(nameof(IsAnalyzing));
        }
    }

    public Services.ItemAiResult? Result
    {
        get => _result;
        set
        {
            _result = value;
            Raise();
            Raise(nameof(HasResult));
            Raise(nameof(SuggestionText));
            Raise(nameof(PurposeText));
            Raise(nameof(ImpactText));
            Raise(nameof(BasisText));
            Raise(nameof(MissingText));
            Raise(nameof(DetailText));
        }
    }

    /// <summary>结果是否在行下方展开显示。默认收起，点「查看结果」才展开。</summary>
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            Raise();
        }
    }

    public string Error
    {
        get => _error;
        set { _error = value ?? ""; Raise(); Raise(nameof(StatusText)); }
    }

    public bool IsBusy => _status is ItemAiStatus.Queued or ItemAiStatus.Running;

    public bool CanAnalyze => !IsBusy;

    public bool HasResult => _result != null
        && _status is ItemAiStatus.Done or ItemAiStatus.NoUseful;

    public string ButtonText => _status switch
    {
        ItemAiStatus.Queued => Services.Loc.ItemAiQueued,
        ItemAiStatus.Running => Services.Loc.ItemAiRunning,
        ItemAiStatus.Done => Services.Loc.ItemAiViewResult,
        ItemAiStatus.NoUseful => Services.Loc.ItemAiViewResult,
        ItemAiStatus.Failed => Services.Loc.ItemAiRetry,
        ItemAiStatus.Timeout => Services.Loc.ItemAiRetry,
        ItemAiStatus.Canceled => Services.Loc.ItemAiRetry,
        _ => Services.Loc.ItemAiAnalyze,
    };

    /// <summary>一行状态摘要：排队/进行中/失败原因/建议档位。</summary>
    public string StatusText => _status switch
    {
        ItemAiStatus.Queued => Services.Loc.ItemAiQueued,
        ItemAiStatus.Running => Services.Loc.ItemAiRunning,
        ItemAiStatus.Failed => Error.Length > 0 ? Error : Services.Loc.ItemAiFailed,
        ItemAiStatus.Timeout => Services.Loc.ItemAiTimeout,
        ItemAiStatus.Canceled => Services.Loc.ItemAiCanceled,
        ItemAiStatus.NoUseful => Services.Loc.ItemAiNoUseful,
        ItemAiStatus.Done => SuggestionText,
        _ => "",
    };

    /// <summary>悬停提示：说清这一项现在点下去会发生什么（只影响这一项）。</summary>
    public string TipText => _status == ItemAiStatus.Idle
        ? Services.Loc.ItemAiTip
        : StatusText.Length > 0 ? StatusText : Services.Loc.ItemAiTip;

    public string SuggestionText => _result == null
        ? ""
        : Services.Loc.ItemAiSuggestionLabel + Services.ItemAiPrompt.SuggestionName(_result.Suggestion);

    public string PurposeText => _result == null || _result.Purpose.Length == 0
        ? "" : Services.Loc.ItemAiPurposeLabel + _result.Purpose;

    public string ImpactText => _result == null || _result.Impact.Length == 0
        ? "" : Services.Loc.ItemAiImpactLabel + _result.Impact;

    public string BasisText => _result == null || _result.Basis.Length == 0
        ? "" : Services.Loc.ItemAiBasisLabel + _result.Basis;

    public string MissingText => _result == null || _result.Missing.Length == 0
        ? "" : Services.Loc.ItemAiMissingLabel + _result.Missing;

    /// <summary>缓存/耗时等技术信息放在可展开的详情里，不占主行。</summary>
    public string DetailText
    {
        get
        {
            if (_result == null) return "";
            var bits = new List<string>();
            if (_result.FromCache) bits.Add(Services.Loc.ItemAiFromCache);
            bits.Add($"queue {_result.QueueMs:0}ms · send {_result.SendMs:0}ms · parse {_result.ParseMs:0}ms");
            if (!string.IsNullOrEmpty(_result.Channel)) bits.Add("channel " + _result.Channel);
            return string.Join(" · ", bits);
        }
    }

    /// <summary>本地规则给出的即时说明（明确标为本地判断，不是 AI 结论）。</summary>
    public string LocalNote { get; set; } = "";

    /// <summary>
    /// 面板里的一行提示（例如「AI 不可用，但本地分组仍可用」）。
    /// 与 AI 结果、本地规则说明都分开，避免三者混为一谈。
    /// </summary>
    private string _notice = "";
    public string Notice
    {
        get => _notice;
        set
        {
            if (_notice == value) return;
            _notice = value ?? "";
            Raise();
            Raise(nameof(HasNotice));
        }
    }
    public bool HasNotice => _notice.Length > 0;

    /// <summary>
    /// 结论模型：一句结论 + 一句说明 + 下一步按钮（必要时才附分组）。
    /// 这是界面的**默认视图** —— 让用户第一眼就知道该选哪些，而不是读技术细节。
    /// </summary>
    private Services.AiVerdictResult? _verdict;
    public Services.AiVerdictResult? Verdict
    {
        get => _verdict;
        set
        {
            _verdict = value;
            Raise();
            Raise(nameof(HasVerdict));
            Raise(nameof(Headline));
            Raise(nameof(Note));
            Raise(nameof(CanSelect));
            Raise(nameof(ShowBuckets));
            Raise(nameof(Buckets));
            Raise(nameof(Why));
        }
    }

    public bool HasVerdict => _verdict != null && !IsStale;
    public string Headline => IsStale ? Services.Loc.AiResultExpired : _verdict?.Headline ?? "";
    public string Note => IsStale ? "" : _verdict?.Note ?? "";

    /// <summary>「选择这些文件」是否可用 —— 只可能是「可考虑清理」的那部分，且结果不能过期。</summary>
    public bool CanSelect => _verdict?.CanSelect == true && !IsStale;

    /// <summary>
    /// 「查看文件」是否可用。定位不到所属位置时置 false ——
    /// **不留一个必然失败的按钮**（§四）。
    /// </summary>
    private bool _canViewFiles = true;
    public bool CanViewFiles
    {
        get => _canViewFiles && !IsStale;
        set
        {
            if (_canViewFiles == value) return;
            _canViewFiles = value;
            Raise();
            Raise(nameof(CanViewFiles));
        }
    }

    /// <summary>
    /// 结果是否已因**重新扫描**而过期。过期后不显示旧结论、也不给任何操作。
    /// </summary>
    private bool _stale;
    public bool IsStale
    {
        get => _stale;
        set
        {
            if (_stale == value) return;
            _stale = value;
            Raise();
            Raise(nameof(IsStale));
            Raise(nameof(CanSelect));
            Raise(nameof(CanViewFiles));
            Raise(nameof(HasVerdict));
            Raise(nameof(Headline));
            Raise(nameof(Note));
        }
    }

    /// <summary>结果对应的扫描代次。与当前代次不一致就是过期。</summary>
    public int ScanGeneration { get; set; }

    /// <summary>只有内容确实混合时才显示分组卡片。</summary>
    public bool ShowBuckets => _verdict?.ShowBuckets == true;
    public IReadOnlyList<Services.AiBucketResult> Buckets =>
        _verdict?.Buckets ?? (IReadOnlyList<Services.AiBucketResult>)Array.Empty<Services.AiBucketResult>();

    /// <summary>「为什么这样建议？」里的短依据。</summary>
    public IReadOnlyList<string> Why =>
        _verdict?.Why ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>分析期间显示的一行简单状态（不出现计时/阶段等技术信息）。</summary>
    public string ProgressText => _status == ItemAiStatus.Running || _status == ItemAiStatus.Queued
        ? Services.Loc.AiAnalyzing
        : "";
    public bool IsAnalyzing => ProgressText.Length > 0;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
