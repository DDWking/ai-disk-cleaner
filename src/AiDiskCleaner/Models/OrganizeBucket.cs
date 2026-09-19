using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AiDiskCleaner.Models;

/// <summary>
/// 「按文件夹删除」页分类结果面板里的**一行**：一类一行。
///
/// 这是「合并」的落点 —— 一屏 38 行里如果有 12 行都被认成「软件缓存」，
/// 面板上就是一行「软件缓存 · 12 个 · 34 G」。勾这一行 = 勾上它下面所有节点，
/// 然后走**现成的**底部执行栏（预览 → 确认 → 回收站），不另起一套删除路径。
///
/// 两条不变量：
/// <list type="bullet">
/// <item>勾选永远是**用户动作** —— 面板的勾选框由用户点击，AI 不预勾、也不写 <see cref="OrganizeNode.IsChecked"/>；</item>
/// <item>「还没认出来」那一桶 <see cref="CanSelect"/> 为 false —— 它没有结论，
///   不该被当成一类来整片处理（那会变成「一键删掉所有看不出来的」，正是最危险的动作）。</item>
/// </list>
/// </summary>
public sealed class OrganizeBucket : INotifyPropertyChanged
{
    public required string Name { get; init; }

    public required IReadOnlyList<OrganizeNode> Members { get; init; }

    /// <summary>能不能整片勾选。只有「还没认出来」那一桶是 false。</summary>
    public bool CanSelect { get; init; } = true;

    public int Count => Members.Count;

    /// <summary>
    /// 这一类的容量。**必须去掉被别的成员包住的那些** ——
    /// 父子同时出现在一屏里是允许的，直接相加会把同一块空间算两遍
    /// （页头统计也因此只用扫描根的真实总量，不累加顶层对象）。
    /// </summary>
    public long Bytes
    {
        get
        {
            var paths = Members.Select(m => m.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            long sum = 0;
            foreach (var m in Members)
                if (!HasAncestorIn(m.FullPath, paths)) sum += m.Size;
            return sum;
        }
    }

    public string CountText => Count.ToString("N0");

    public string SizeText => FileEntry.FormatSize(Bytes);

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            Raise();
        }
    }

    private int _selected;
    /// <summary>已经被勾上的成员数。用户在行上单独改动之后要跟着回填。</summary>
    public string SelectedText => _selected == 0 || _selected == Count
        ? ""
        : $"已选 {_selected:N0} / {Count:N0}";

    /// <summary>
    /// 按成员的真实勾选状态回填自己。
    /// 用户在行内单独勾/取消之后必须调 —— 否则面板那一行会一直停在旧状态上，
    /// 显示成一个跟列表不符的勾。
    /// </summary>
    public void SyncFromMembers()
    {
        _selected = Members.Count(m => m.IsChecked);
        IsChecked = Count > 0 && _selected == Count;
        Raise(nameof(SelectedText));
    }

    /// <summary>
    /// 路径 <paramref name="path"/> 上面有没有人也在集合里（含自身则不算）。
    ///
    /// 公开出来给列表复用：一行如果被列表里某个祖先目录包含，勾了两个**不会算两遍**
    /// （<c>FolderSelectionService</c> 会去重），但列表看起来像重复，得在界面上说清楚。
    /// </summary>
    public static bool HasAncestorIn(string path, HashSet<string> paths)
    {
        int i = path.Length;
        while (i > 3)
        {
            i = path.LastIndexOf('\\', i - 1);
            if (i <= 2) break;
            if (paths.Contains(path[..i])) return true;
        }
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
