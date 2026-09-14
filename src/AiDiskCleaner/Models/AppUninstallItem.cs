using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using UninstallTools;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models;

public sealed class AppUninstallItem : INotifyPropertyChanged
{
    private bool _selected;
    private string _status = "";
    private long _sizeBytes;
    private long _actualSizeBytes;
    private bool _hasMeasuredSize;
    private string _purposeSummary = "";
    private string _purposeDetail = "";
    private AppInfoConfidence _purposeConfidence = AppInfoConfidence.Unknown;
    private AppSourceKind _purposeKind = AppSourceKind.Unknown;
    private AppRunningState _runningState = AppRunningState.Unknown;
    private AppRecommendationDecision _recommendation = AppRecommendationDecision.Neutral;
    private string _recommendationReason = "";
    private string _recommendationWarning = "";
    private double _recommendationConfidence;
    private bool _aiSuggested;
    private string _footprintNote = "";

    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; OnPropertyChanged(); }
    }

    public string AppId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>安装记录里软件自己写的估计值（EstimatedSize）。改了要同步排序键。</summary>
    public long SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (_sizeBytes == value) return;
            _sizeBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeText));
            OnPropertyChanged(nameof(ActualSizeText));
            OnPropertyChanged(nameof(FootprintSource));
            OnPropertyChanged(nameof(SizeConfidenceRank));
            OnPropertyChanged(nameof(EffectiveFootprintBytes));
            OnPropertyChanged(nameof(HasKnownFootprint));
            OnPropertyChanged(nameof(FootprintHint));
        }
    }

    public long ActualSizeBytes
    {
        get => _actualSizeBytes;
        set
        {
            if (_actualSizeBytes == value) return;
            _actualSizeBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActualSizeText));
            OnPropertyChanged(nameof(SizeConfidenceRank));
            OnPropertyChanged(nameof(EffectiveFootprintBytes));
            OnPropertyChanged(nameof(HasKnownFootprint));
            OnPropertyChanged(nameof(FootprintHint));
        }
    }
    public bool HasMeasuredSize
    {
        get => _hasMeasuredSize;
        set
        {
            if (_hasMeasuredSize == value) return;
            _hasMeasuredSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActualSizeText));
            OnPropertyChanged(nameof(FootprintSource));
            OnPropertyChanged(nameof(SizeConfidenceRank));
            OnPropertyChanged(nameof(EffectiveFootprintBytes));
            OnPropertyChanged(nameof(HasKnownFootprint));
            OnPropertyChanged(nameof(FootprintHint));
        }
    }

    /// <summary>
    /// 占用为什么没测到（共享目录 / 系统目录 / 不在扫描范围内 / 目录不存在 …）。
    /// 只用于把话说清楚，绝不在这里编造数字。
    /// </summary>
    public string FootprintNote
    {
        get => _footprintNote;
        set
        {
            if (_footprintNote == value) return;
            _footprintNote = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FootprintHint));
        }
    }

    /// <summary>这一格的数字从哪来：实测 / 安装记录 / 未知。三者显示文案完全不同。</summary>
    public AppFootprintSource FootprintSource
    {
        get
        {
            if (HasMeasuredSize) return AppFootprintSource.Measured;
            return SizeBytes > 0 ? AppFootprintSource.InstallRecord : AppFootprintSource.Unknown;
        }
    }

    /// <summary>
    /// 「可信占用排序」用的分级：0 = 扫描实测，1 = 安装记录估计，2 = 未知。
    /// 实测优先，估的排后面，没有的排最后 —— 不让假数字混在真数字里。
    /// </summary>
    public int SizeConfidenceRank => (int)FootprintSource;

    /// <summary>
    /// 占用未知时用的排序哨兵：降序排序时，没有数字的条目永远排在最后。
    /// 用 <see cref="long.MinValue"/> 而不是 0 —— 0 是一个真实数字，会让「未知」混进小体积里。
    /// </summary>
    public const long UnknownFootprintSortValue = long.MinValue;

    /// <summary>
    /// 数值排序键（不是新的占用口径）：
    /// 扫描实测 → 用实测值；否则退回安装记录里的估计值；两样都没有 → <see cref="UnknownFootprintSortValue"/>。
    ///
    /// 数字从哪来仍然由 <see cref="FootprintSource"/> / <see cref="ActualSizeText"/> 如实区分，
    /// 这个属性只负责「按数字大小排」。
    /// </summary>
    public long EffectiveFootprintBytes => HasMeasuredSize
        ? ActualSizeBytes
        : _sizeBytes > 0 ? _sizeBytes : UnknownFootprintSortValue;

    /// <summary>有没有可排序的数字（实测或安装记录）。未知 = false。</summary>
    public bool HasKnownFootprint => HasMeasuredSize || _sizeBytes > 0;

    public DateTime InstallDate { get; set; }
    public string InstallLocation { get; set; } = "";
    public bool CanUninstall { get; set; }
    public bool IsProtected { get; set; }
    public bool SystemComponent { get; set; }
    /// <summary>
    /// Windows 自带组件 / 设备驱动软件（由卸载程序路径或安装路径**结构判定**，不靠名字猜）。
    /// 这类条目不属于用户装的软件，不能进泛化卸载建议，也不进「选择建议项」。
    /// </summary>
    public bool InboxComponent { get; set; }
    public bool HasStartup { get; set; }
    public AppRunningState RunningState
    {
        get => _runningState;
        set
        {
            if (_runningState == value) return;
            _runningState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsRunningKnown));
            OnPropertyChanged(nameof(RecommendationHint));
        }
    }
    public bool IsRunning => RunningState == AppRunningState.Running;
    public bool IsRunningKnown => RunningState != AppRunningState.Unknown;
    /// <summary>0 普通软件，1 Steam，2 Windows 功能，3 受保护。决定分组和默认折叠。</summary>
    public int GroupKey { get; set; }
    /// <summary>0 建议卸载，1 可以考虑，2 建议保留。只影响展示，不会自动执行动作。</summary>
    public int RecommendationGroupKey => (int)Recommendation;
    public byte[]? IconBytes { get; set; }
    public ImageSource? Icon { get; set; }
    public ApplicationUninstallerEntry? Entry { get; set; }

    public string SizeText => SizeBytes <= 0 ? "—" : FileEntry.FormatSize(SizeBytes);

    /// <summary>
    /// 占用那一格。**三种来源的文案必须一眼分得开**：
    /// 扫描实测 = 直接写数；安装记录 = 「约 X（安装记录）」；两样都没有 = 「未知」。
    /// 「0 G（估算）」这种既没有信息量、又让人以为软件只有 0 的写法已经取消。
    /// </summary>
    public string ActualSizeText
    {
        get
        {
            if (HasMeasuredSize)
                return ActualSizeBytes > 0
                    ? FormatFootprint(ActualSizeBytes)
                    : Loc.AppSizeMeasuredEmpty;
            if (SizeBytes > 0) return Loc.AppSizeFromRecord(FormatFootprint(SizeBytes));
            return Loc.AppSizeUnknown;
        }
    }

    /// <summary>悬停：占用来源 + 为什么没测到 + 拆分。数字口径写清楚，不承诺等于卸载可释放量。</summary>
    public string FootprintHint
    {
        get
        {
            var bits = new List<string>();
            bits.Add(Loc.AppSizeColumnTip);
            if (!HasMeasuredSize && FootprintNote.Length > 0)
                bits.Add(Loc.AppFootprintNotMeasured(FootprintNote));
            if (HasFootprintBreakdown) bits.Add(FootprintText);
            bits.Add(Loc.AppFootprintNotEqualFree);
            return string.Join(Environment.NewLine, bits);
        }
    }

    /// <summary>
    /// 体积格式化：**绝不把小值四舍五入成 0**。
    /// 1 字节就是「&lt; 1 KB」，400 MB 就是「400 MB」，不会变成「0 G」。
    /// </summary>
    public static string FormatFootprint(long bytes)
    {
        if (bytes <= 0) return "0 KB";
        if (bytes < 1024) return "< 1 KB";
        if (bytes < 1024L * 1024)
            return Math.Max(1, (long)Math.Round(bytes / 1024.0)) + " KB";
        if (bytes < 1024L * 1024 * 1024)
        {
            double mb = bytes / (1024.0 * 1024);
            return (mb >= 100 ? Math.Round(mb).ToString("0") : mb.ToString("0.0")) + " MB";
        }
        double g = bytes / (1024.0 * 1024 * 1024);
        return (g >= 100 ? Math.Round(g).ToString("0") : g.ToString("0.0")) + " G";
    }

    public string InstallDateText => InstallDate == DateTime.MinValue ? "—" : InstallDate.ToString("yyyy-MM-dd");

    // ---- 占用拆分：安装目录 / 用户数据 / 缓存 / 估计可释放 ----
    private long _installDirBytes;
    private long _userDataBytes;
    private long _cacheBytes;

    public long InstallDirBytes
    {
        get => _installDirBytes;
        set { if (_installDirBytes == value) return; _installDirBytes = value; NotifyFootprint(); }
    }

    public long UserDataBytes
    {
        get => _userDataBytes;
        set { if (_userDataBytes == value) return; _userDataBytes = value; NotifyFootprint(); }
    }

    /// <summary>缓存：删了会自动重建，也不影响软件能不能用。</summary>
    public long CacheBytes
    {
        get => _cacheBytes;
        set { if (_cacheBytes == value) return; _cacheBytes = value; NotifyFootprint(); }
    }

    /// <summary>估计可释放：不卸载软件、不动用户数据就能拿回来的那部分（= 缓存）。</summary>
    public long ReclaimableBytes => _cacheBytes;

    public bool HasFootprintBreakdown => _installDirBytes > 0 || _userDataBytes > 0 || _cacheBytes > 0;

    /// <summary>悬停用的明细，逐项写清「这块是什么、删了会怎样」。</summary>
    public string FootprintText => !HasFootprintBreakdown
        ? ""
        : string.Join(Environment.NewLine, new[]
        {
            Loc.FootprintInstall(FileEntry.FormatSize(_installDirBytes)),
            Loc.FootprintUserData(FileEntry.FormatSize(_userDataBytes)),
            Loc.FootprintCache(FileEntry.FormatSize(_cacheBytes)),
            Loc.FootprintReclaimable(FileEntry.FormatSize(ReclaimableBytes)),
        });

    private void NotifyFootprint()
    {
        OnPropertyChanged(nameof(InstallDirBytes));
        OnPropertyChanged(nameof(UserDataBytes));
        OnPropertyChanged(nameof(CacheBytes));
        OnPropertyChanged(nameof(ReclaimableBytes));
        OnPropertyChanged(nameof(HasFootprintBreakdown));
        OnPropertyChanged(nameof(FootprintText));
    }

    public AppRecommendationDecision Recommendation
    {
        get => _recommendation;
        set
        {
            if (_recommendation == value) return;
            _recommendation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecommendationGroupKey));
            OnPropertyChanged(nameof(RecommendationText));
            OnPropertyChanged(nameof(RecommendationHint));
        }
    }

    public double RecommendationConfidence
    {
        get => _recommendationConfidence;
        set { if (Math.Abs(_recommendationConfidence - value) < 0.001) return; _recommendationConfidence = value; OnPropertyChanged(); }
    }

    public string RecommendationReason
    {
        get => _recommendationReason;
        set
        {
            if (_recommendationReason == value) return;
            _recommendationReason = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecommendationText));
            OnPropertyChanged(nameof(RecommendationHint));
        }
    }

    public string RecommendationWarning
    {
        get => _recommendationWarning;
        set
        {
            if (_recommendationWarning == value) return;
            _recommendationWarning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecommendationText));
            OnPropertyChanged(nameof(RecommendationHint));
        }
    }

    public bool AiSuggested
    {
        get => _aiSuggested;
        set
        {
            if (_aiSuggested == value) return;
            _aiSuggested = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecommendationText));
        }
    }

    public string RecommendationText
    {
        get
        {
            string label = Loc.AppRecommendationLabel(Recommendation);
            string reason = string.IsNullOrWhiteSpace(RecommendationReason) ? "" : " · " + RecommendationReason;
            string warning = string.IsNullOrWhiteSpace(RecommendationWarning) ? "" : " · " + RecommendationWarning;
            string ai = AiSuggested ? " · " + Loc.AiMark : "";
            return label + reason + warning + ai;
        }
    }

    /// <summary>
    /// 建议列的悬停：建议 + （中性档时）为什么没有建议 + 占用来源说明。
    /// 套话只留在悬停里，而且只在确实有内容可讲的时候才出现。
    /// </summary>
    public string RecommendationHint
    {
        get
        {
            var bits = new List<string> { RecommendationText };
            if (Recommendation == AppRecommendationDecision.Neutral) bits.Add(Loc.AppNeutralHint);
            bits.Add(Loc.RunningStateText(RunningState));
            return string.Join(Environment.NewLine, bits);
        }
    }

    // ---- 事实用途（本地元数据 / 结构证据；**不涉及该不该卸载**）----

    /// <summary>
    /// 按本机证据能确认的条目性质。由本地的 <c>AppFactualInfoService</c> 只按结构判定，
    /// 绝不拿软件名字做子串猜测。
    /// </summary>
    public AppSourceKind PurposeKind
    {
        get => _purposeKind;
        set { if (_purposeKind == value) return; _purposeKind = value; OnPropertyChanged(); }
    }

    /// <summary>本地依据强度（结构化证据 / 只有注册表记录 / 未知）。**不是**卸载建议。</summary>
    public AppInfoConfidence PurposeConfidence
    {
        get => _purposeConfidence;
        set
        {
            if (_purposeConfidence == value) return;
            _purposeConfidence = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPurposeInfo));
        }
    }

    /// <summary>一句话事实摘要（服务填入；没有可靠依据时填如实的「未知」文案）。</summary>
    public string PurposeSummary
    {
        get => _purposeSummary;
        set
        {
            if (_purposeSummary == value) return;
            _purposeSummary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PurposeText));
        }
    }

    /// <summary>逐条可核对的事实依据 + 明确声明「不构成卸载建议」。</summary>
    public string PurposeDetail
    {
        get => _purposeDetail;
        set
        {
            if (_purposeDetail == value) return;
            _purposeDetail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PurposeHint));
        }
    }

    /// <summary>有没有拿到可靠本地依据（只有名字 = false，界面如实显示未知）。</summary>
    public bool HasPurposeInfo => _purposeConfidence != AppInfoConfidence.Unknown;

    /// <summary>列表「用途」列显示的文本。未知时由服务写成本机记录不足，不编造。</summary>
    public string PurposeText => _purposeSummary;

    /// <summary>「用途」列悬停：逐条事实依据 + 声明这不是卸载建议。</summary>
    public string PurposeHint => _purposeDetail;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 事实信息的本地依据强度。**不是**卸载建议：
/// <see cref="LocalEvidence"/> = 结构化 / 系统证据；<see cref="RecordOnly"/> = 只有注册表记录；
/// <see cref="Unknown"/> = 只有名字，什么都不编。
/// </summary>
public enum AppInfoConfidence
{
    Unknown = 0,
    RecordOnly = 1,
    LocalEvidence = 2,
}

/// <summary>按本地证据能确认的条目性质。判定只看结构化字段，不看名字子串。</summary>
public enum AppSourceKind
{
    Unknown = 0,
    /// <summary>卸载清单里有可用卸载程序的普通软件。</summary>
    InstalledApplication,
    /// <summary>卸载清单里没有可用卸载程序。</summary>
    NoUninstaller,
    /// <summary>Steam 清单里的条目。</summary>
    SteamItem,
    /// <summary>Windows 可选功能（BCU 的 WindowsFeature）。</summary>
    WindowsFeature,
    /// <summary>卸载程序或安装位置位于 Windows 系统目录内（结构化判定）。</summary>
    WindowsInboxComponent,
    /// <summary>卸载清单标记为受保护 / 系统组件。</summary>
    ProtectedSystemEntry,
}
