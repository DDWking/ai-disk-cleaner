namespace AiDiskCleaner.Models;

public enum EntryKind { File, Directory }

/// <summary>磁盘上的一个条目（文件或目录）。目录的 Size 是其下所有文件的聚合大小。</summary>
public class FileEntry
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long Size { get; set; }
    /// <summary>磁盘占用（Allocated）。稀疏/压缩文件会小于 Size。</summary>
    public long Allocated { get; set; }
    public DateTime Modified { get; set; }
    public string Category { get; set; } = "其他";
    public EntryKind Kind { get; set; }
    public FileEntry? Parent { get; set; }

    private List<FileEntry>? _children;

    private static readonly FileEntry[] NoChildren = Array.Empty<FileEntry>();

    /// <summary>
    /// 子节点列表（**写入用**）。第一次写入时才分配 ——
    /// 磁盘上绝大多数条目是文件，每个都挂一个空 List 在百万级规模下是白扔几十 MB。
    /// 只读遍历请用 <see cref="ChildList"/>，别用这个属性，否则会给叶子节点凭空造一个 List。
    /// </summary>
    public List<FileEntry> Children => _children ??= new();

    /// <summary>只读遍历用：没有子节点时返回共享空数组，不产生任何分配。</summary>
    public IReadOnlyList<FileEntry> ChildList => _children ?? (IReadOnlyList<FileEntry>)NoChildren;

    public int ChildCount => _children?.Count ?? 0;
    public bool HasChildren => _children is { Count: > 0 };

    /// <summary>这个条目自己占多少托管内存（估算，用于内存诊断）。</summary>
    public long EstimatedBytes => 120
        + (Name.Length * 2L)
        + (FullPath?.Length * 2L ?? 0)
        + (Category.Length * 2L)
        + (_children is null ? 0 : 32 + _children.Count * 8L);

    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public bool IsHidden { get; set; }
    public bool IsSystem { get; set; }
    /// <summary>
    /// 重解析点：junction / symlink / 云盘占位文件（OneDrive）。
    /// 目录递归时会跳过，删除预检会单独判定。
    /// </summary>
    public bool IsReparsePoint { get; set; }
    /// <summary>根目录下「散文件」合成组，点开才列出文件。</summary>
    public bool IsFilesGroup { get; set; }

    public bool IsDirectory => Kind == EntryKind.Directory;
    public bool IsDimmed => IsHidden || IsSystem || Name.StartsWith('$') || IsFilesGroup;

    public string SizeText => FormatSize(Size);
    public string AllocatedText => FormatSize(Allocated);

    /// <summary>当前列表里相对最大项的占用条宽度（像素）。</summary>
    public double SizeBarWidth { get; set; }

    public double PercentValue
    {
        get
        {
            long parentSize = Parent?.Size ?? Size;
            if (parentSize <= 0) return 0;
            return 100.0 * Size / parentSize;
        }
    }

    public string PercentText => PercentValue <= 0 && Size == 0 ? "0.0 %" : PercentValue.ToString("0.0") + " %";

    /// <summary>0–1，给占比条当比例。</summary>
    public double PercentShare => Math.Clamp(PercentValue / 100.0, 0, 1);

    public string ItemText => IsDirectory ? FileCount.ToString("N0") : "";

    public string ModifiedText => Modified == DateTime.MinValue ? "" : Modified.ToString("yyyy-MM-dd HH:mm");
    public string KindText => IsDirectory ? "文件夹" : Category;

    public int AgeDays => (int)(DateTime.Now - Modified).TotalDays;

    public string AgeText
    {
        get
        {
            int days = AgeDays;
            if (days < 1) return "今天";
            if (days < 30) return days + " 天";
            if (days < 365) return Math.Round(days / 30.0) + " 个月";
            return (days / 365.0).ToString("0.0") + " 年";
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024 * 1024)
            return Math.Max(0, (int)Math.Round(bytes / 1024.0)) + " KB";
        if (bytes < 1024L * 1024 * 1024)
        {
            double mb = bytes / (1024.0 * 1024);
            return (mb >= 100 ? Math.Round(mb).ToString() : mb.ToString("0.0")) + " MB";
        }
        double g = bytes / (1024.0 * 1024 * 1024);
        return (g >= 100 ? Math.Round(g).ToString() : g.ToString("0.0")) + " G";
    }
}
