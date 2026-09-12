using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models;

/// <summary>三档风险，决定风险列颜色。</summary>
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
    /// <summary>
    /// 用途分类（首页第一层：临时文件 / 浏览器缓存 / 应用缓存 / 回收站 / 重复文件 …）。
    /// 由规则声明、签名细化，**不在展示层按路径名猜**。
    /// </summary>
    public Services.CleanRules.CleanPurpose Purpose { get; set; } = Services.CleanRules.CleanPurpose.Other;
    /// <summary>同一位置里处理方式不同的候选要拆开（重复文件的保留项 vs 多余项）。</summary>
    public string Handling { get; set; } = "";
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

    /// <summary>表格「说明」列：优先 AI，否则规则原因。两者都该是大白话。</summary>
    public string NoteText => !string.IsNullOrWhiteSpace(_aiNote) ? _aiNote : Reason;

    /// <summary>
    /// 「类型 · 说明」：窄窗口下类型列**整列隐藏**时，让说明列承载类型，
    /// 这样次要信息不会整块消失，也不会被压成残缺文字（§6）。
    /// </summary>
    public string NoteWithGroup
    {
        get
        {
            string g = (Group ?? "").Trim();
            string n = NoteText ?? "";
            if (g.Length == 0) return n;
            return n.Length == 0 ? g : g + " · " + n;
        }
    }

    public bool HasAiNote => !string.IsNullOrWhiteSpace(_aiNote);

    /// <summary>技术细节（命中了哪条签名等）。术语都留在悬停里，不进表格。</summary>
    public string Tech { get; set; } = "";

    ItemAiView? _ai;
    /// <summary>
    /// 这一个文件的 AI 状态（按需分析用）。挂在条目自身上、用完整路径做稳定标识，
    /// 因此分页/虚拟化回收不会让结果串到别的行。
    /// 惰性创建以保证绑定永远拿得到非 null 的对象。
    /// </summary>
    public ItemAiView Ai
    {
        get
        {
            if (_ai == null)
            {
                _ai = new ItemAiView
                {
                    ScopeKey = FullPath,
                    // 本地规则给出的是**本地判断**，必须标注，不能冒充 AI 结论
                    LocalNote = Services.Loc.ItemAiLocalOnly + Reason,
                };
                OnPropertyChanged();
            }
            return _ai;
        }
    }

    /// <summary>悬停提示：完整路径 + 技术细节。</summary>
    public string HintText => string.IsNullOrEmpty(Tech) ? FullPath : FullPath + "\n" + Tech;

    public FileEntry? Entry { get; set; }
    public bool IsDirectory { get; set; }

    public string SizeText => FileEntry.FormatSize(Size);

    /// <summary>表格分组用：0 = 建议清理（安全），1 = 需要你确认。「别删」不进列表。</summary>
    public int RiskGroupKey => Risk == CleanRisk.Safe ? 0 : 1;

    /// <summary>
    /// 详情列表的**展示分组键**：同一位置、同一风险分区内，
    /// **说明来源、实际说明、处理条件**都一致才归为一组。
    ///
    /// 用完整说明文本参与比较（不是截断后的文字），所以「看起来一样」不会误合并；
    /// 风险、可删性、处理方式也都进键，避免把不同删除影响的项目混在一起。
    /// **纯展示用**，不参与分组统计以外的任何判断。
    /// </summary>
    public string DetailGroupKey =>
        $"{RiskGroupKey}\u0001{Risk}\u0001{CanDelete}\u0001{Handling}\u0001{Group}\u0001{NoteText}";

    /// <summary>分组标题：简短、可理解的用途名（不出现「未标注」这类技术标签）。</summary>
    public string DetailGroupTitle
    {
        get
        {
            string p = Services.CleanRules.CleanPurposes.Name(Purpose);
            if (!string.IsNullOrWhiteSpace(p)) return p;
            if (!string.IsNullOrWhiteSpace(Group)) return Group.Trim();
            return Loc.OtherFilesTitle;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>用途分类的一行，带占比可视化所需的数据。</summary>
public sealed class CatRow : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public List<CleanItem> Items { get; set; } = new();
    public int Count => Items.Count;
    public long Bytes => Items.Sum(x => x.Size);
    public string SizeText => FileEntry.FormatSize(Bytes);
    /// <summary>下拉里显示：名称 · 条数 · 大小。</summary>
    public string MenuText => $"{Name}  ·  {Count:N0}  ·  {SizeText}";
    /// <summary>占全部可清理空间的百分比（0~100）。</summary>
    public double Percent { get; set; }
    public string PercentText => Percent >= 10 ? $"{Percent:0}%" : $"{Percent:0.#}%";

    bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { if (_isCurrent == value) return; _isCurrent = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
