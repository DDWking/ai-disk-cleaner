namespace AiDiskCleaner.Models;

public enum EntryKind { File, Directory }

/// <summary>磁盘上的一个条目（文件或目录）。目录的 Size 是其下所有文件的聚合大小。</summary>
public class FileEntry
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long Size { get; set; }
    public DateTime Modified { get; set; }
    public string Category { get; set; } = "其他";
    public EntryKind Kind { get; set; }
    public FileEntry? Parent { get; set; }
    public List<FileEntry> Children { get; set; } = new();
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public bool IsHidden { get; set; }
    public bool IsSystem { get; set; }

    public bool IsDirectory => Kind == EntryKind.Directory;
    public bool IsDimmed => IsHidden || IsSystem || Name.StartsWith('$');

    public string SizeText => FormatSize(Size);

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
