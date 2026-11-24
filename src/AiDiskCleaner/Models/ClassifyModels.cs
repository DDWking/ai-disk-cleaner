using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AiDiskCleaner.Models;

/// <summary>软件内虚拟分类视图的目录性质标记（纯只读展示，不执行任何文件搬移）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClassifySafetyTier
{
    /// <summary>普通内容/资料/媒体/下载类目录。</summary>
    Safe,

    /// <summary>含可执行文件、游戏或开发工具链的应用类目录。</summary>
    ReviewGreenApp,

    /// <summary>Windows 系统核心或根级容器目录，归入底部「系统核心与原位保护」分组。</summary>
    BlockedConservative,
}

/// <summary>软件内虚拟分类大类定义（供本地规则与 AI 内容归纳使用，仅作界面可视化分组，绝不移动磁盘文件）。</summary>
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
            Aliases = new() { "Media", "Videos", "Video", "Movies", "Anime", "影音", "视频", "动漫", "影视", "图片" },
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
            Description = "开发工具与 IDE（Unity/VS/JetBrains）、虚拟机镜像（VMware/VirtualBox）、Git 代码仓库、Python .venv 环境、常用软件与 SDK 工具链",
            Aliases = new() { "Dev", "Projects", "Code", "Tools", "开发", "项目", "代码", "工具" },
            Enabled = true,
        },
        new()
        {
            Id = "docs",
            Name = "文档与资料",
            FolderName = "Docs",
            Description = "PDF 电子书、Office 文档、学习课件、设计稿或归档个人备份资料",
            Aliases = new() { "Docs", "Documents_Archive", "Backup", "Archive", "文档", "资料", "备份", "我的文档" },
            Enabled = true,
        },
    };
}

/// <summary>虚拟分类选项。</summary>
public sealed record ClassifyCategoryChoice(string Id, string DisplayName, string TargetFolderName)
{
    public string OptionLabel => DisplayName;
    public override string ToString() => OptionLabel;
}

/// <summary>软件内虚拟分类视图中的单个目录条目（纯只读视图模型，绝不改动磁盘文件）。</summary>
public sealed class ClassifyItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _categoryId = ClassifyCategoryRule.KeepId;
    private string _categoryName = "系统核心";
    private string _aiReason = "";
    private bool _fromAi;
    private ClassifySafetyTier _tier;
    private string _safetyNote = "";

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
    public IReadOnlyList<ClassifyCategoryChoice> Choices { get; set; } = Array.Empty<ClassifyCategoryChoice>();

    public string SizeText => FileEntry.FormatSize(SizeBytes);

    public ClassifySafetyTier Tier
    {
        get => _tier;
        set
        {
            if (_tier == value) return;
            _tier = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBlocked));
            OnPropertyChanged(nameof(IsKeepInPlace));
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
            _categoryName = picked != null ? picked.DisplayName : "系统核心";
            OnPropertyChanged();
            OnPropertyChanged(nameof(CategoryName));
            OnPropertyChanged(nameof(IsKeepInPlace));
        }
    }

    public string CategoryName
    {
        get => _categoryName;
        set { if (_categoryName != value) { _categoryName = value; OnPropertyChanged(); } }
    }

    public bool IsBlocked => Tier == ClassifySafetyTier.BlockedConservative;

    public bool IsKeepInPlace =>
        IsBlocked || string.Equals(_categoryId, ClassifyCategoryRule.KeepId, StringComparison.OrdinalIgnoreCase);

    public void ApplyCategorySelection(string categoryId, string categoryName)
    {
        _categoryId = string.IsNullOrWhiteSpace(categoryId) ? ClassifyCategoryRule.KeepId : categoryId;
        _categoryName = string.IsNullOrWhiteSpace(categoryName) ? "系统核心" : categoryName;
        OnPropertyChanged(nameof(CategoryId));
        OnPropertyChanged(nameof(CategoryName));
        OnPropertyChanged(nameof(IsKeepInPlace));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
