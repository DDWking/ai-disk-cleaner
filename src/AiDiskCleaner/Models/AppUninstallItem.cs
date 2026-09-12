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
    private long _actualSizeBytes;
    private bool _hasMeasuredSize;
    private AppRunningState _runningState = AppRunningState.Unknown;
    private AppRecommendationDecision _recommendation = AppRecommendationDecision.Consider;
    private string _recommendationReason = "";
    private string _recommendationWarning = "";
    private double _recommendationConfidence;
    private bool _aiSuggested;

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
    public long SizeBytes { get; set; }
    public long ActualSizeBytes
    {
        get => _actualSizeBytes;
        set
        {
            if (_actualSizeBytes == value) return;
            _actualSizeBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActualSizeText));
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
        }
    }
    public DateTime InstallDate { get; set; }
    public string InstallLocation { get; set; } = "";
    public bool CanUninstall { get; set; }
    public bool IsProtected { get; set; }
    public bool SystemComponent { get; set; }
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
    public string ActualSizeText => ActualSizeBytes <= 0
        ? SizeText
        : FileEntry.FormatSize(ActualSizeBytes) + (HasMeasuredSize ? "" : Loc.AppSizeEstimated);
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
