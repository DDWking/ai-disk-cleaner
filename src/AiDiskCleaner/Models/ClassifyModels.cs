using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AiDiskCleaner.Models;

/// <summary>同盘文件夹整理的安全分级。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClassifySafetyTier
{
    /// <summary>纯内容/资源/文档/散落非系统下载目录，同盘移动安全，默认自动勾选。</summary>
    Safe,

    /// <summary>未命中系统卸载清单的可执行/绿游/绿软目录，默认不自动勾选，需用户确认后随快捷方式更新一起移动。</summary>
    ReviewGreenApp,

    /// <summary>系统红线、活跃 KnownFolder、已安装程序或启动器游戏库等，保守锁死禁止一键移动。</summary>
    BlockedConservative,
}

/// <summary>用户自定义的目录归类大类（同盘落地目标规则，可直接传给 AI Prompt）。</summary>
public sealed class ClassifyCategoryRule
{
    public const string KeepId = "__keep__";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    public bool Enabled { get; set; } = true;

    public ClassifyCategoryRule Clone() => new()
    {
        Id = Id,
        Name = Name,
        FolderName = FolderName,
        Description = Description,
        Aliases = new List<string>(Aliases),
        Enabled = Enabled,
    };

    public static List<ClassifyCategoryRule> DefaultPresets() => new()
    {
        new()
        {
            Id = "games",
            Name = "游戏娱乐",
            FolderName = "Games",
            Description = "单机游戏本体、Steam/WeGame/Epic 等游戏平台库、免安装游戏包、游戏启动器、整合包与 Mod 存档",
            Aliases = new() { "Games", "Game", "游戏", "单机游戏", "My Games" },
            Enabled = true,
        },
        new()
        {
            Id = "media",
            Name = "影音动漫",
            FolderName = "Media",
            Description = "电影、番剧动画、录屏录像、无损音乐、图片摄影或壁纸素材合集",
            Aliases = new() { "Media", "Videos", "Video", "Movies", "Anime", "影音", "视频", "动漫", "影视" },
            Enabled = true,
        },
        new()
        {
            Id = "installers",
            Name = "下载与安装包",
            FolderName = "Installers",
            Description = "浏览器或下载器堆积的零散下载目录、压缩包、ISO 镜像、驱动包、离线安装包",
            Aliases = new() { "Installers", "Downloads_Archive", "Archives", "Packages", "安装包", "压缩包", "下载归档" },
            Enabled = true,
        },
        new()
        {
            Id = "dev",
            Name = "开发与工具",
            FolderName = "Dev",
            Description = "开发工具与 IDE（Unity/VS/JetBrains）、虚拟机镜像（VMware/VirtualBox）、Git 代码仓库、Python .venv 环境与 SDK 工具链",
            Aliases = new() { "Dev", "Projects", "Code", "Tools", "开发", "项目", "代码", "工具" },
            Enabled = true,
        },
        new()
        {
            Id = "docs",
            Name = "文档与资料",
            FolderName = "Docs",
            Description = "PDF 电子书、Office 文档、学习课件、设计稿或归档个人备份资料",
            Aliases = new() { "Docs", "Documents_Archive", "Backup", "Archive", "文档", "资料", "备份" },
            Enabled = true,
        },
    };
}

/// <summary>列表下拉菜单的分类选项。</summary>
public sealed record ClassifyCategoryChoice(string Id, string DisplayName, string TargetFolderName)
{
    public string OptionLabel => string.IsNullOrWhiteSpace(TargetFolderName)
        ? DisplayName
        : $"{DisplayName}  →  {TargetFolderName}";

    public override string ToString() => OptionLabel;
}

/// <summary>同盘文件夹整理规划中的一条建议行（UI 与预检/执行共用契约）。</summary>
public sealed class ClassifyItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isChecked;
    private string _categoryId = ClassifyCategoryRule.KeepId;
    private string _categoryName = "保留在原位";
    private string _targetFolderName = "";
    private string _aiReason = "";
    private bool _fromAi;
    private ClassifySafetyTier _tier;
    private string _safetyNote = "";
    private string _conflictNote = "";
    private bool _executed;
    private string _executionStatus = "";
    private bool _supportsJunctionMigration;
    private bool _useJunction;
    private string _targetDriveRoot = "";

    public required string Id { get; init; }
    public required FileEntry SourceEntry { get; init; }
    public required string DriveRoot { get; init; }
    public required string SourcePath { get; init; }
    public required string SourceName { get; init; }
    public required long SizeBytes { get; init; }
    public required string ContentDigest { get; init; }
    public bool IsGreenExecutable { get; init; }
    public string InstalledProductHint { get; set; } = "";
    public string FallbackCategoryId { get; set; } = ClassifyCategoryRule.KeepId;

    /// <summary>该目录虽命中注册表/路径依赖被保守锁定，但非系统核心/杀毒/反作弊红线，允许用户主动启用「NTFS 隐藏软链兜底」无损迁移。</summary>
    public bool SupportsJunctionMigration
    {
        get => _supportsJunctionMigration;
        set
        {
            if (_supportsJunctionMigration == value) return;
            _supportsJunctionMigration = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TierBadge));
            OnPropertyChanged(nameof(ShowJunctionOptInButton));
            OnPropertyChanged(nameof(DestinationPreview));
        }
    }

    /// <summary>是否对该目录启用 NTFS Junction 隐藏软链兜底（默认 false；用户主动点击开启后生效）。</summary>
    public bool UseJunction
    {
        get => _useJunction;
        set
        {
            bool effective = value && (Tier != ClassifySafetyTier.BlockedConservative || _supportsJunctionMigration);
            if (_useJunction == effective) return;
            _useJunction = effective;
            if (!effective && Tier == ClassifySafetyTier.BlockedConservative && _isChecked)
            {
                _isChecked = false;
                OnPropertyChanged(nameof(IsChecked));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(TierBadge));
            OnPropertyChanged(nameof(TierToneKey));
            OnPropertyChanged(nameof(IsBlocked));
            OnPropertyChanged(nameof(IsKeepInPlace));
            OnPropertyChanged(nameof(CanCheck));
            OnPropertyChanged(nameof(ShowAttentionBadge));
            OnPropertyChanged(nameof(ShowJunctionOptInButton));
            OnPropertyChanged(nameof(IsDropdownEnabled));
            OnPropertyChanged(nameof(JunctionToggleLabel));
            OnPropertyChanged(nameof(DestinationPath));
            OnPropertyChanged(nameof(DestinationPreview));
        }
    }

    /// <summary>目标落地盘符（空字符串表示同盘整理；设为其他盘如 D:\ 则走安全跨盘搬家）。</summary>
    public string TargetDriveRoot
    {
        get => _targetDriveRoot;
        set
        {
            string norm = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            if (norm.Length == 2 && norm[1] == ':') norm += "\\";
            if (string.Equals(_targetDriveRoot, norm, StringComparison.OrdinalIgnoreCase)) return;
            _targetDriveRoot = norm;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveTargetDriveRoot));
            OnPropertyChanged(nameof(IsCrossDrive));
            OnPropertyChanged(nameof(TierBadge));
            OnPropertyChanged(nameof(ShowAttentionBadge));
            OnPropertyChanged(nameof(DestinationPath));
            OnPropertyChanged(nameof(DestinationPreview));
        }
    }

    public string EffectiveTargetDriveRoot =>
        string.IsNullOrWhiteSpace(_targetDriveRoot) ? DriveRoot : _targetDriveRoot;

    public bool IsCrossDrive
    {
        get
        {
            string srcR = (Path.GetPathRoot(SourcePath) ?? DriveRoot).TrimEnd('\\');
            string dstR = EffectiveTargetDriveRoot.TrimEnd('\\');
            return srcR.Length > 0 && dstR.Length > 0
                && !string.Equals(srcR, dstR, StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool ShowJunctionOptInButton =>
        !_executed
        && _supportsJunctionMigration
        && (Tier == ClassifySafetyTier.BlockedConservative
            || Tier == ClassifySafetyTier.ReviewGreenApp
            || _useJunction);

    public string JunctionToggleLabel => _useJunction ? "🔗 已启用软链" : "🔗 启用软链搬家";

    public bool IsDropdownEnabled => (!IsBlocked || _useJunction) && !_executed;

    public IReadOnlyList<ClassifyCategoryChoice> Choices { get; set; } = Array.Empty<ClassifyCategoryChoice>();

    public string SizeText => FileEntry.FormatSize(SizeBytes);

    public string SourceParentHint
    {
        get
        {
            string? dir = Path.GetDirectoryName(SourcePath);
            return string.IsNullOrEmpty(dir) ? DriveRoot : dir;
        }
    }

    public ClassifySafetyTier Tier
    {
        get => _tier;
        set
        {
            if (_tier == value) return;
            _tier = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TierBadge));
            OnPropertyChanged(nameof(TierToneKey));
            OnPropertyChanged(nameof(CanCheck));
            OnPropertyChanged(nameof(IsBlocked));
            OnPropertyChanged(nameof(IsKeepInPlace));
            OnPropertyChanged(nameof(ShowAttentionBadge));
            OnPropertyChanged(nameof(ShowJunctionOptInButton));
            OnPropertyChanged(nameof(IsDropdownEnabled));
            OnPropertyChanged(nameof(DestinationPreview));
        }
    }

    public string TierBadge
    {
        get
        {
            if (_useJunction) return IsCrossDrive ? "🔗 跨盘软链搬家" : "🔗 软链迁移";
            return Tier switch
            {
                ClassifySafetyTier.Safe => IsCrossDrive ? "跨盘整理" : "可放心整理",
                ClassifySafetyTier.ReviewGreenApp => "绿软 · 请确认",
                _ => _supportsJunctionMigration ? "注册表保护 · 可软链" : "系统红线锁死",
            };
        }
    }

    public string TierToneKey
    {
        get
        {
            if (_useJunction) return "Caution";
            return Tier switch
            {
                ClassifySafetyTier.Safe => "Safe",
                ClassifySafetyTier.ReviewGreenApp => "Caution",
                _ => "Blocked",
            };
        }
    }

    public string SafetyNote
    {
        get => _safetyNote;
        set { if (_safetyNote != value) { _safetyNote = value; OnPropertyChanged(); } }
    }

    public string ContentDigestDisplay => string.IsNullOrWhiteSpace(AiReason)
        ? ContentDigest
        : (FromAi ? $"AI：{AiReason}" : AiReason);

    public string AiReason
    {
        get => _aiReason;
        set
        {
            if (_aiReason == value) return;
            _aiReason = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContentDigestDisplay));
        }
    }

    public bool FromAi
    {
        get => _fromAi;
        set
        {
            if (_fromAi == value) return;
            _fromAi = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContentDigestDisplay));
        }
    }

    public string CategoryId
    {
        get => _categoryId;
        set
        {
            string next = string.IsNullOrWhiteSpace(value) ? ClassifyCategoryRule.KeepId : value;
            if (_categoryId == next) return;
            _categoryId = next;
            var picked = Choices.FirstOrDefault(c => string.Equals(c.Id, next, StringComparison.OrdinalIgnoreCase));
            if (picked != null)
            {
                _categoryName = picked.DisplayName;
                _targetFolderName = picked.TargetFolderName;
            }
            else if (next == ClassifyCategoryRule.KeepId)
            {
                _categoryName = "保留在原位";
                _targetFolderName = "";
            }
            if (IsKeepInPlace && _isChecked)
            {
                _isChecked = false;
                OnPropertyChanged(nameof(IsChecked));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(CategoryName));
            OnPropertyChanged(nameof(TargetFolderName));
            OnPropertyChanged(nameof(IsKeepInPlace));
            OnPropertyChanged(nameof(CanCheck));
            OnPropertyChanged(nameof(DestinationPath));
            OnPropertyChanged(nameof(DestinationPreview));
        }
    }

    public string CategoryName
    {
        get => _categoryName;
        set { if (_categoryName != value) { _categoryName = value; OnPropertyChanged(); } }
    }

    public string TargetFolderName
    {
        get => _targetFolderName;
        set
        {
            string clean = (value ?? "").Trim().Trim('\\', '/');
            if (_targetFolderName == clean) return;
            _targetFolderName = clean;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DestinationPath));
            OnPropertyChanged(nameof(DestinationPreview));
            OnPropertyChanged(nameof(CanCheck));
        }
    }

    public bool IsKeepInPlace =>
        (Tier == ClassifySafetyTier.BlockedConservative && !_useJunction)
        || string.Equals(_categoryId, ClassifyCategoryRule.KeepId, StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(_targetFolderName);

    public bool IsBlocked => Tier == ClassifySafetyTier.BlockedConservative && !_useJunction;

    public bool ShowAttentionBadge => Tier != ClassifySafetyTier.Safe || _useJunction || IsCrossDrive || HasConflict;

    public string DestinationPath
    {
        get
        {
            if (IsKeepInPlace) return "";
            string root = EffectiveTargetDriveRoot.EndsWith('\\') ? EffectiveTargetDriveRoot : EffectiveTargetDriveRoot + "\\";
            return Path.Combine(root, _targetFolderName, SourceName);
        }
    }

    public string DestinationPreview
    {
        get
        {
            if (_executed && !string.IsNullOrWhiteSpace(_executionStatus))
                return _executionStatus;
            if (Tier == ClassifySafetyTier.BlockedConservative && !_useJunction)
                return _supportsJunctionMigration
                    ? "保留在原位（点右侧「启用软链搬家」可通过隐藏软链无损迁移）"
                    : "保留在原位（系统红线，禁止移动）";
            if (IsKeepInPlace)
                return "保留在原位";
            if (!string.IsNullOrWhiteSpace(_conflictNote))
                return $"{DestinationPath}（{_conflictNote}）";
            if (_useJunction)
                return IsCrossDrive
                    ? $"{DestinationPath}  · 跨盘搬家 + 原位留隐藏软链"
                    : $"{DestinationPath}  · 同盘归位 + 原位留隐藏软链";
            if (IsCrossDrive)
                return $"{DestinationPath}  · 跨盘安全复制校验后释放原空间";
            return DestinationPath;
        }
    }

    public string ConflictNote
    {
        get => _conflictNote;
        set
        {
            if (_conflictNote == value) return;
            _conflictNote = value ?? "";
            if (_conflictNote.Length > 0 && _isChecked)
            {
                _isChecked = false;
                OnPropertyChanged(nameof(IsChecked));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConflict));
            OnPropertyChanged(nameof(CanCheck));
            OnPropertyChanged(nameof(DestinationPreview));
        }
    }

    public bool HasConflict => _conflictNote.Length > 0;

    public bool CanCheck =>
        !IsBlocked
        && !IsKeepInPlace
        && !HasConflict
        && !_executed;

    public bool IsChecked
    {
        get => _isChecked && CanCheck;
        set
        {
            bool effective = value && CanCheck;
            if (_isChecked == effective) return;
            _isChecked = effective;
            OnPropertyChanged();
        }
    }

    public bool Executed
    {
        get => _executed;
        set
        {
            if (_executed == value) return;
            _executed = value;
            if (value) _isChecked = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCheck));
            OnPropertyChanged(nameof(IsChecked));
            OnPropertyChanged(nameof(ShowJunctionOptInButton));
            OnPropertyChanged(nameof(IsDropdownEnabled));
            OnPropertyChanged(nameof(DestinationPreview));
        }
    }

    public string ExecutionStatus
    {
        get => _executionStatus;
        set
        {
            if (_executionStatus == value) return;
            _executionStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DestinationPreview));
        }
    }

    public void ApplyCategorySelection(string categoryId, string categoryName, string targetFolderName)
    {
        _categoryId = string.IsNullOrWhiteSpace(categoryId) ? ClassifyCategoryRule.KeepId : categoryId;
        _categoryName = categoryName;
        _targetFolderName = (targetFolderName ?? "").Trim().Trim('\\', '/');
        OnPropertyChanged(nameof(CategoryId));
        OnPropertyChanged(nameof(CategoryName));
        OnPropertyChanged(nameof(TargetFolderName));
        OnPropertyChanged(nameof(IsKeepInPlace));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(DestinationPath));
        OnPropertyChanged(nameof(DestinationPreview));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>单条移动快捷方式修改记录（供事务撤销恢复原 .lnk 目标）。</summary>
public sealed class ShortcutUpdateRecord
{
    public string LnkPath { get; set; } = "";
    public string OldTargetPath { get; set; } = "";
    public string OldWorkingDirectory { get; set; } = "";
    public string OldIconPath { get; set; } = "";
    public int OldIconIndex { get; set; }
}

/// <summary>单条文件夹移动的撤销记录（支持普通同盘移动、隐藏 NTFS Junction 软链搬家及跨盘搬家事务回滚）。</summary>
public sealed class ReorganizeUndoEntry
{
    public string OriginalPath { get; set; } = "";
    public string MovedPath { get; set; } = "";
    public string TargetCategoryRoot { get; set; } = "";
    public bool CreatedCategoryRoot { get; set; }
    public bool CreatedJunction { get; set; }
    public bool JunctionHidden { get; set; }
    public bool CrossDrive { get; set; }
    public long MovedBytes { get; set; }
    public List<ShortcutUpdateRecord> UpdatedShortcuts { get; set; } = new();
}

/// <summary>整批「一键整理」的持久化撤销清单。</summary>
public sealed class ReorganizeUndoBatch
{
    public string BatchId { get; set; } = "";
    public DateTime ExecutedAtUtc { get; set; }
    public string DriveRoot { get; set; } = "";
    public List<ReorganizeUndoEntry> Entries { get; set; } = new();
}

/// <summary>精致低饱和分类配色板（同步支持浅白与黑曜暗黑主题）。</summary>
public readonly record struct ClassifyCategoryTheme(string AccentHex, string LightTintHex, string DarkTintHex)
{
    public string TintHex(bool isDark) => isDark ? DarkTintHex : LightTintHex;

    public string RowWashHex(bool isDark) => WithAlpha(AccentHex, isDark ? "14" : "09");

    public string PillWashHex(bool isDark) => WithAlpha(AccentHex, isDark ? "28" : "16");

    private static string WithAlpha(string rgbHex, string alphaHex)
    {
        string raw = (rgbHex ?? "").Trim().TrimStart('#');
        if (raw.Length == 6) return "#" + alphaHex + raw;
        if (raw.Length == 8) return "#" + alphaHex + raw[2..];
        return rgbHex ?? "#FF5A1F";
    }
}

public static class ClassifyCategoryPalette
{
    public static readonly ClassifyCategoryTheme KeepTheme = new("#64748B", "#F1F5F9", "#1C212A");

    private static readonly ClassifyCategoryTheme[] Ring =
    {
        new("#FF5A1F", "#FFF3ED", "#2A1811"), // games: 珊瑚琥珀橙
        new("#6366F1", "#EEF2FF", "#1C1F38"), // media: 靛蓝紫
        new("#0EA47A", "#ECFBF6", "#102821"), // installers: 翡翠青绿
        new("#0284C7", "#ECFAFF", "#102431"), // dev: 科技青蓝
        new("#D97706", "#FFFBEB", "#282010"), // docs: 暖陶土金
        new("#DB2777", "#FDF2F8", "#2B1421"), // extra-1: 玫红
        new("#7C3AED", "#F5F3FF", "#221836"), // extra-2: 紫罗兰
    };

    public static ClassifyCategoryTheme For(string? categoryId, int fallbackIndex = 0)
    {
        string id = (categoryId ?? "").Trim().ToLowerInvariant();
        if (string.Equals(id, ClassifyCategoryRule.KeepId, StringComparison.OrdinalIgnoreCase))
            return KeepTheme;
        return id switch
        {
            "games" => Ring[0],
            "media" => Ring[1],
            "installers" => Ring[2],
            "dev" => Ring[3],
            "docs" => Ring[4],
            _ => Ring[Math.Abs(fallbackIndex + (id.GetHashCode() & 0x7FFF)) % Ring.Length],
        };
    }
}

/// <summary>Bento 可视化整理网格中的单个彩色大类文件夹卡片。</summary>
public sealed class ClassifyBentoGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private long _totalBytes;
    private int _selectedCount;

    public required string CategoryId { get; init; }
    public required string Name { get; init; }
    public required string TargetFolderName { get; init; }
    public required string DestinationFolderPath { get; init; }
    public required string AccentHex { get; init; }
    public required string HeaderTintHex { get; init; }
    public required string SoftSurfaceHex { get; init; }
    public System.Collections.ObjectModel.ObservableCollection<ClassifyItem> Items { get; } = new();

    public string TotalSizeText => FileEntry.FormatSize(_totalBytes);
    public string CountText => Items.Count == 1 ? "1 个文件夹" : $"{Items.Count} 个文件夹";
    public string SelectedBadgeText => _selectedCount > 0 ? $"已勾 {_selectedCount} 项" : "未勾选";
    public long TotalBytes => _totalBytes;

    public void RefreshStats()
    {
        _totalBytes = 0;
        _selectedCount = 0;
        foreach (var item in Items)
        {
            _totalBytes += Math.Max(0, item.SizeBytes);
            if (item.IsChecked && item.CanCheck) _selectedCount++;
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalBytes)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalSizeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedBadgeText)));
    }
}
