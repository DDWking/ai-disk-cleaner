using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models;

/// <summary>三档风险，决定行高亮颜色。</summary>
public enum CleanRisk
{
    Safe,     // 绿：缓存、转储、回收站，放心删
    Confirm,  // 黄：大文件、重复、依赖目录，删前看一眼
    Keep,     // 红：系统文件、虚拟机、SDK，别删
}

public sealed class CleanItem : INotifyPropertyChanged
{
    private bool _selected;

    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
    }

    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long Size { get; set; }
    public string Reason { get; set; } = "";
    public string Group { get; set; } = "";
    /// <summary>用途分类（system / browser / dev / im / game …），来自 AppSignatures。</summary>
    public string Category { get; set; } = "";
    /// <summary>风险档位，决定行高亮颜色。默认 Safe，未识别的按条目类型另判。</summary>
    public CleanRisk Risk { get; set; } = CleanRisk.Safe;
    public bool CanDelete { get; set; } = true;
    public bool AiSuggested { get; set; }
    public FileEntry? Entry { get; set; }
    public bool IsDirectory { get; set; }

    public string SizeText => FileEntry.FormatSize(Size);

    public string RiskText => Risk switch
    {
        CleanRisk.Safe => Loc.RiskSafe,
        CleanRisk.Confirm => Loc.RiskConfirm,
        _ => Loc.RiskKeep,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
