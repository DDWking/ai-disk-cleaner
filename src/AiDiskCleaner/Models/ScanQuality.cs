namespace AiDiskCleaner.Models;

/// <summary>扫描来源。界面必须明确告诉用户这次结果是怎么来的。</summary>
public enum ScanSource
{
    None,
    /// <summary>MFT 高速扫描。</summary>
    Mft,
    /// <summary>兼容递归遍历。</summary>
    Recursive,
}

/// <summary>
/// 扫描质量报告。核心目的：不让用户把「跳了一大堆目录」的扫描当成完整扫描。
/// 每个计数都可以单独显示，界面据此决定要不要打「不完整」的标记。
/// </summary>
public sealed class ScanQuality
{
    public ScanSource Source { get; set; } = ScanSource.None;
    /// <summary>是否完整读完（没有跳过、没有权限失败、没有中途停止）。</summary>
    public bool Complete { get; set; }
    public bool Canceled { get; set; }

    public int FilesRead { get; set; }
    public int DirsRead { get; set; }
    /// <summary>因权限或异常整目录跳过的数量。</summary>
    public int SkippedDirs { get; set; }
    public int PermissionErrors { get; set; }
    public int PathErrors { get; set; }
    public int ReadErrors { get; set; }
    /// <summary>遇到的重解析点（junction / symlink / 云占位）数量。</summary>
    public int ReparsePoints { get; set; }
    /// <summary>MFT 里挂着但找不到父目录的孤儿记录。</summary>
    public int OrphanRecords { get; set; }
    /// <summary>MFT 里解析不出来的记录数。</summary>
    public int UnparsedRecords { get; set; }
    /// <summary>解析出多条 $FILE_NAME（硬链接）的数量。</summary>
    public int HardLinks { get; set; }
    public long DurationMs { get; set; }
    /// <summary>补充说明（失败原因、降级原因）。</summary>
    public string Note { get; set; } = "";

    public int TotalErrors => PermissionErrors + PathErrors + ReadErrors;

    /// <summary>有跳过就说明结果不完整。</summary>
    public bool HasSkips => SkippedDirs > 0 || TotalErrors > 0;

    public double DurationSeconds => DurationMs / 1000.0;

    /// <summary>把另一个报告的错误计数并进来（递归子目录回传）。</summary>
    public void Merge(ScanQuality other)
    {
        if (other == null) return;
        FilesRead += other.FilesRead;
        DirsRead += other.DirsRead;
        SkippedDirs += other.SkippedDirs;
        PermissionErrors += other.PermissionErrors;
        PathErrors += other.PathErrors;
        ReadErrors += other.ReadErrors;
        ReparsePoints += other.ReparsePoints;
        OrphanRecords += other.OrphanRecords;
        UnparsedRecords += other.UnparsedRecords;
        HardLinks += other.HardLinks;
    }

    public ScanQuality Clone() => (ScanQuality)MemberwiseClone();
}
