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
/// 逐项 AI 结果的**来源树**。清理树与整理树复用同一条请求管线，
/// 但「能不能据此勾选清理项 / 定位清理位置」只属于清理树。
/// </summary>
public enum ItemAiSource
{
    /// <summary>清理树：CleanItem / CleanLocationNode。可以有清理动作能力。</summary>
    Clean = 0,
    /// <summary>整理树：OrganizeNode。只做展示，绝不携带清理动作能力。</summary>
    Organize = 1,
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

    private ItemAiSource _source = ItemAiSource.Clean;

    /// <summary>
    /// 这一项属于清理树还是整理树。默认清理（保持既有调用方兼容）。
    /// 一旦标成整理，<see cref="CanSelect"/> / <see cref="CanViewFiles"/> 永远为 false ——
    /// 模型结果再有用，也不会把清理动作能力带进整理页。
    /// </summary>
    public ItemAiSource Source
    {
        get => _source;
        set
        {
            if (_source == value) return;
            _source = value;
            Raise();
            Raise(nameof(IsCleanSource));
            Raise(nameof(IsolationKey));
            Raise(nameof(CanSelect));
            Raise(nameof(HasModelNote));
            Raise(nameof(ShowResultPanel));
            Raise(nameof(ShowIdentifyError));
            Raise(nameof(ShowAiInline));
            Raise(nameof(CanViewFiles));
        }
    }

    /// <summary>只有清理树的结果才允许影响清理选择。</summary>
    public bool IsCleanSource => _source == ItemAiSource.Clean;

    /// <summary>
    /// 请求登记键：来源 + 稳定标识。
    /// 清理页的一个文件与整理页的一个文件夹**可能同路径**；只用 ScopeKey 做键，
    /// 两个请求会互相顶掉取消源，其中一个结束时还会删掉另一个的登记。
    /// </summary>
    public string IsolationKey => IsCleanSource ? ScopeKey : "organize\u0001" + ScopeKey;

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
            Raise(nameof(TipText));
            Raise(nameof(HasModelNote));
            Raise(nameof(ShowResultPanel));
            Raise(nameof(ShowIdentifyError));
            Raise(nameof(ShowAiInline));
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
            Raise(nameof(HasModelNote));
            Raise(nameof(ShowResultPanel));
            Raise(nameof(ShowAiInline));
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
            Raise(nameof(ShowResultPanel));
            Raise(nameof(ShowAiInline));
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
    public string TipText => _status switch
    {
        // 跑着时按钮变成旋转指示器、但仍然可点：提示必须说清「点一下＝停这一项」
        ItemAiStatus.Queued or ItemAiStatus.Running => Services.Loc.ItemAiStopHint,
        ItemAiStatus.Idle => Services.Loc.ItemAiTip,
        _ => StatusText.Length > 0 ? StatusText : Services.Loc.ItemAiTip,
    };

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
            Raise(nameof(ShowResultPanel));
            Raise(nameof(ShowAiInline));
            Raise(nameof(ShowBuckets));
            Raise(nameof(Buckets));
            Raise(nameof(Why));
        }
    }

    public bool HasVerdict => _verdict != null && !IsStale;
    public string Headline => IsStale ? Services.Loc.AiResultExpired : _verdict?.Headline ?? "";
    public string Note => IsStale ? "" : _verdict?.Note ?? "";

    /// <summary>
    /// 「选择这些文件」是否可用 —— 只可能是**清理树**里「可考虑清理」的那部分，且结果不能过期。
    /// 整理树的结果哪怕模型给了正面结论，也永远不提供勾选能力。
    /// </summary>
    public bool CanSelect => IsCleanSource && _verdict?.CanSelect == true && !IsStale;

    /// <summary>
    /// 模型是否说了用途或删除影响。这两句是模型独有的；项数/空间以文件表为准。
    /// </summary>
    public bool HasModelNote => HasResult && !IsStale
        && (!string.IsNullOrWhiteSpace(_result?.Purpose)
            || !string.IsNullOrWhiteSpace(_result?.Impact));

    /// <summary>
    /// 清理行下方的瘦提示：只展示模型用途/删除影响。
    /// 不再用本地项数当「AI 结论」，也不再和「选择这些文件」绑在一起。
    /// 整理树只展示信息，不出这块清理提示。
    /// </summary>
    public bool ShowResultPanel => IsCleanSource && HasModelNote && IsExpanded;

    /// <summary>识别失败/超时/取消/无可用结论时的一行实话，不冒充建议卡。</summary>
    public bool ShowIdentifyError => IsCleanSource
        && _status is ItemAiStatus.Failed or ItemAiStatus.Timeout
            or ItemAiStatus.Canceled or ItemAiStatus.NoUseful;

    /// <summary>位置行上要不要留出识别结果/错误那一两行。</summary>
    public bool ShowAiInline => ShowResultPanel || ShowIdentifyError;

    /// <summary>
    /// 「查看文件」是否可用。定位不到所属位置时置 false ——
    /// **不留一个必然失败的按钮**（§四）。整理树没有「清理明细」可定位，一律 false。
    /// </summary>
    private bool _canViewFiles = true;
    public bool CanViewFiles
    {
        get => IsCleanSource && _canViewFiles && !IsStale;
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
            Raise(nameof(HasModelNote));
            Raise(nameof(ShowResultPanel));
            Raise(nameof(ShowIdentifyError));
            Raise(nameof(ShowAiInline));
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
