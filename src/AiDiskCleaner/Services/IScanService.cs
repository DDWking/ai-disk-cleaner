using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>扫描进度快照。</summary>
public record ScanProgress(int FileCount, string CurrentDirectory);

/// <summary>磁盘扫描服务的抽象接口。路线 C 用递归遍历，路线 A 用 MFT 直接读取，两者都实现此接口。</summary>
public interface IScanService
{
    FileEntry Scan(string rootPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default);
}
