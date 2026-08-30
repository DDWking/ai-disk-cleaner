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
                        Category = FileClassifier.Classify(f.Name),
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

}
