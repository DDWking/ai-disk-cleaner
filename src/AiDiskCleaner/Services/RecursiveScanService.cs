using System.IO;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 路线 C：用 DirectoryInfo 递归遍历真实磁盘。
/// 真实数据、分钟级；是 MFT 秒扫（路线 A）接入前的过渡实现。
/// </summary>
public sealed class RecursiveScanService : IScanService
{
    public FileEntry Scan(string rootPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var root = new FileEntry { Name = rootPath, FullPath = rootPath, Kind = EntryKind.Directory };
        int count = 0;
        ScanDirectory(new DirectoryInfo(rootPath), root, progress, ct, ref count);
        return root;
    }

    private static void ScanDirectory(DirectoryInfo dir, FileEntry parent,
        IProgress<ScanProgress>? progress, CancellationToken ct, ref int count)
    {
        IEnumerator<FileSystemInfo>? en;
        try { en = dir.EnumerateFileSystemInfos().GetEnumerator(); }
        catch (Exception) { return; } // 无权限或不可访问：跳过该目录

        using (en)
        {
            while (true)
            {
                FileSystemInfo e;
                try { if (!en.MoveNext()) break; e = en.Current; }
                catch (Exception) { break; } // 遍历中途出错：停止该目录

                ct.ThrowIfCancellationRequested();

                if (e is DirectoryInfo d)
                {
                    // 跳过重解析点（junction/symlink），避免死循环
                    try { if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue; }
                    catch (Exception) { continue; }

                    var child = new FileEntry { Name = d.Name, FullPath = d.FullName, Kind = EntryKind.Directory };
                    parent.Children.Add(child);
                    ScanDirectory(d, child, progress, ct, ref count);
                    parent.Size += child.Size;
                }
                else if (e is FileInfo f)
                {
                    long size = 0;
                    DateTime modified = DateTime.MinValue;
                    try { size = f.Length; modified = f.LastWriteTime; } catch (Exception) { }

                    parent.Children.Add(new FileEntry
                    {
                        Name = f.Name,
                        FullPath = f.FullName,
                        Size = size,
                        Modified = modified,
                        Category = Classify(f.Extension),
                        Kind = EntryKind.File,
                    });
                    parent.Size += size;
                    count++;
                }

                if (count % 1000 == 0)
                    progress?.Report(new ScanProgress(count, parent.FullPath));
            }
        }
    }

    private static string Classify(string extension)
    {
        string ext = extension.ToLowerInvariant();
        if (ext is ".dll" or ".sys" or ".exe" or ".mui") return "系统";
        if (ext is ".log" or ".etl" or ".txt") return "日志";
        if (ext is ".tmp" or ".temp" or ".cache" or ".bak" or ".old") return "临时";
        if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp"
            or ".mp4" or ".mp3" or ".mov" or ".wav" or ".mkv") return "媒体";
        if (ext is ".doc" or ".docx" or ".xls" or ".xlsx" or ".pdf" or ".ppt" or ".pptx") return "文档";
        if (ext is ".py" or ".js" or ".ts" or ".json" or ".cs" or ".cpp" or ".c" or ".h"
            or ".java" or ".go" or ".rs" or ".html" or ".css") return "代码";
        if (ext is ".zip" or ".rar" or ".7z" or ".msi" or ".tar" or ".gz" or ".iso") return "压缩包";
        if (ext is ".db" or ".sqlite" or ".dat" or ".bin") return "数据";
        return "其他";
    }
}
