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
    public List<FileEntry> Children { get; set; } = new();

    public bool IsDirectory => Kind == EntryKind.Directory;

    public string SizeText => FormatSize(Size);

    /// <summary>当前列表里相对最大文件的占用条宽度（像素），仅用于界面展示。</summary>
    public double SizeBarWidth { get; set; }

    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");
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
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return v >= 100 ? Math.Round(v).ToString() + " " + units[i] : v.ToString("0.0") + " " + units[i];
    }
}
