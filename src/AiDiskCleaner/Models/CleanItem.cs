using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models;

/// <summary>三档风险，决定行高亮颜色。</summary>
public enum CleanRisk
{
    Safe,     // 绿：缓存、转储、回收站，可安全删除
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

    string _reason = "";
    public string Reason
    {
        get => _reason;
        set
        {
            if (_reason == value) return;
            _reason = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NoteText));
        }
    }
    public string Group { get; set; } = "";
    /// <summary>用途分类（system / browser / dev / im / game …），来自 AppSignatures。</summary>
    public string Category { get; set; } = "";
    /// <summary>风险档位，决定行高亮颜色。</summary>
    public CleanRisk Risk { get; set; } = CleanRisk.Confirm;
    public bool CanDelete { get; set; } = true;
    public bool AiSuggested { get; set; }

    string _aiNote = "";
    /// <summary>
    /// AI 给的一句话说明。风险档位一律由规则判定，AI 不参与。
    /// 表格只显示一列说明：有 AI 文字就用它，没有就回退到规则原因。
    /// </summary>
    public string AiNote
    {
        get => _aiNote;
        set
        {
            if (_aiNote == value) return;
            _aiNote = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NoteText));
            OnPropertyChanged(nameof(HasAiNote));
        }
    }

    /// <summary>表格「说明」列：优先 AI，否则规则原因。</summary>
    public string NoteText => !string.IsNullOrWhiteSpace(_aiNote) ? _aiNote : Reason;

    public bool HasAiNote => !string.IsNullOrWhiteSpace(_aiNote);

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

/// <summary>用途分类的一行，带占比可视化所需的数据。</summary>
public sealed class CatRow
{
    public string Name { get; set; } = "";
    public List<CleanItem> Items { get; set; } = new();
    public int Count => Items.Count;
    public long Bytes => Items.Sum(x => x.Size);
    public string SizeText => FileEntry.FormatSize(Bytes);
    /// <summary>占全部可清理空间的百分比（0~100）。</summary>
    public double Percent { get; set; }
    /// <summary>占比条填充部分的宽度（0~100）。</summary>
    public double BarWidth => Math.Clamp(Percent, 0, 100);
    /// <summary>占比条剩余部分的宽度。</summary>
    public double BarRest => 100 - BarWidth;
    public string PercentText => Percent >= 10 ? $"{Percent:0}%" : $"{Percent:0.#}%";
}
