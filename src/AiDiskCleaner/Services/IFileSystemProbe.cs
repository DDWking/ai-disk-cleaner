using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>一次文件探测的原始事实。全部字段都是「读到的」，不含判断。</summary>
public sealed class FileProbeInfo
{
    public bool Exists { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsReparsePoint { get; init; }
    public bool IsSystem { get; init; }
    public bool IsHidden { get; init; }
    public long Size { get; init; }
    /// <summary>磁盘占用（GetCompressedFileSize）。拿不到时为 -1。</summary>
    public long AllocatedSize { get; init; } = -1;
    public DateTime Modified { get; init; }
    public FileIdentity Identity { get; init; }
    /// <summary>独占打开失败 → 认为正在被占用。</summary>
    public bool InUse { get; init; }
    /// <summary>读取时遇到权限不足。</summary>
    public bool AccessDenied { get; init; }
    /// <summary>读取失败的原因（技术细节）。</summary>
    public string? Error { get; init; }

    public static FileProbeInfo Missing(string? error = null)
        => new() { Exists = false, Error = error };
}

/// <summary>
/// 文件系统探测抽象。抽出来是为了让删除预检能在没有真实磁盘的情况下被完整测试。
/// </summary>
public interface IFileSystemProbe
{
    /// <summary>探测一个路径的当前状态。</summary>
    FileProbeInfo Probe(string path);

    /// <summary>
    /// 只取文件身份（卷序列号 + file id），比 <see cref="Probe"/> 便宜得多：
    /// 不做独占打开、不查占用、不读磁盘占用。硬链接合并只需要这个。
    /// </summary>
    FileIdentity? TryGetIdentity(string path);

    /// <summary>把一批路径移入回收站。抛异常表示整批失败。</summary>
    void SendToRecycle(IReadOnlyList<string> paths);
}
