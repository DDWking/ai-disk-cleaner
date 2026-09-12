using System.IO;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 路线 C：用 DirectoryInfo 递归遍历真实磁盘。
/// 真实数据、分钟级；是 MFT 秒扫（路线 A）接入前的过渡实现。
///
/// 关键点：错误**不再静默吞掉**。跳过的目录、权限失败、读取失败、重解析点
/// 全部计入 <see cref="ScanQuality"/>，界面据此显示扫描完整度。
/// </summary>
public sealed class RecursiveScanService : IScanService
{
    /// <summary>最近一次扫描的质量报告。</summary>
    public ScanQuality? LastQuality { get; private set; }

    public FileEntry Scan(string rootPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var quality = new ScanQuality { Source = ScanSource.Recursive };
        var root = new FileEntry { Name = rootPath, FullPath = rootPath, Kind = EntryKind.Directory };
        int count = 0;
        ScanDirectory(new DirectoryInfo(rootPath), root, progress, ct, ref count, quality, depth: 0);

        sw.Stop();
        quality.DurationMs = sw.ElapsedMilliseconds;
        quality.FilesRead = count;
        quality.Complete = !quality.HasSkips && !ct.IsCancellationRequested;
        quality.Canceled = ct.IsCancellationRequested;
        if (quality.Canceled) quality.Note = "用户停止";
        LastQuality = quality;
        return root;
    }

    private static void ScanDirectory(DirectoryInfo dir, FileEntry parent,
        IProgress<ScanProgress>? progress, CancellationToken ct, ref int count,
        ScanQuality quality, int depth)
    {
        quality.DirsRead++;

        IEnumerator<FileSystemInfo>? en;
        try
        {
            en = dir.EnumerateFileSystemInfos().GetEnumerator();
        }
        catch (UnauthorizedAccessException ex)
        {
            quality.SkippedDirs++;
            quality.PermissionErrors++;
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Scan", "", "recursive/skip",
                LogRedactor.ScrubPath(dir.FullName) + " | " + ex.GetType().Name));
            return;
        }
        catch (DirectoryNotFoundException ex)
        {
            quality.SkippedDirs++;
            quality.PathErrors++;
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Scan", "", "recursive/path",
                LogRedactor.ScrubPath(dir.FullName) + " | " + ex.GetType().Name));
            return;
        }
        catch (PathTooLongException ex)
        {
            quality.SkippedDirs++;
            quality.PathErrors++;
            AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Scan", "", "recursive/toolong",
                LogRedactor.ScrubPath(dir.FullName) + " | " + ex.GetType().Name));
            return;
        }
        catch (Exception ex)
        {
            quality.SkippedDirs++;
            quality.ReadErrors++;
            AppLog.Record("Scan", ex, "recursive/enumerate " + LogRedactor.ScrubPath(dir.FullName));
            return;
        }

        using (en)
        {
            while (true)
            {
                FileSystemInfo e;
                try { if (!en.MoveNext()) break; e = en.Current; }
                catch (Exception ex)
                {
                    // 遍历中途出错：这个目录剩下的读不到了，记下来继续别的目录
                    quality.SkippedDirs++;
                    if (ex is UnauthorizedAccessException) quality.PermissionErrors++;
                    else if (ex is PathTooLongException or DirectoryNotFoundException) quality.PathErrors++;
                    else quality.ReadErrors++;
                    AppLog.Record("Scan", ex, "recursive/movenext " + LogRedactor.ScrubPath(dir.FullName));
                    break;
                }

                ct.ThrowIfCancellationRequested();

                if (e is DirectoryInfo d)
                {
                    FileAttributes attrs;
                    try { attrs = d.Attributes; }
                    catch (Exception ex)
                    {
                        quality.SkippedDirs++;
                        quality.ReadErrors++;
                        AppLog.Record("Scan", ex, "recursive/attributes " + LogRedactor.ScrubPath(d.FullName));
                        continue;
                    }

                    // 跳过重解析点（junction/symlink），避免死循环。只统计，不当成错误。
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                    {
                        quality.ReparsePoints++;
                        continue;
                    }

                    var child = new FileEntry
                    {
                        Name = d.Name,
                        FullPath = d.FullName,
                        Kind = EntryKind.Directory,
                        IsHidden = (attrs & FileAttributes.Hidden) != 0,
                        IsSystem = (attrs & FileAttributes.System) != 0,
                    };
                    parent.Children.Add(child);
                    ScanDirectory(d, child, progress, ct, ref count, quality, depth + 1);
                    parent.Size += child.Size;
                    parent.Allocated += child.Allocated;
                    parent.FolderCount += 1 + child.FolderCount;
                    parent.FileCount += child.FileCount;
                }
                else if (e is FileInfo f)
                {
                    FileAttributes attrs;
                    try { attrs = f.Attributes; }
                    catch (Exception ex)
                    {
                        quality.ReadErrors++;
                        AppLog.Record("Scan", ex, "recursive/file-attributes " + LogRedactor.ScrubPath(f.FullName));
                        continue;
                    }
                    if ((attrs & FileAttributes.ReparsePoint) != 0) quality.ReparsePoints++;

                    long size = 0;
                    DateTime modified = DateTime.MinValue;
                    try { size = f.Length; modified = f.LastWriteTime; }
                    catch (UnauthorizedAccessException)
                    {
                        quality.ReadErrors++;
                        quality.PermissionErrors++;
                    }
                    catch (Exception)
                    {
                        quality.ReadErrors++;
                    }

                    parent.Children.Add(new FileEntry
                    {
                        Name = f.Name,
                        FullPath = f.FullName,
                        Size = size,
                        Allocated = size,
                        Modified = modified,
                        Category = FileClassifier.Classify(f.Name),
                        Kind = EntryKind.File,
                        IsHidden = (attrs & FileAttributes.Hidden) != 0,
                        IsSystem = (attrs & FileAttributes.System) != 0,
                    });
                    parent.Size += size;
                    parent.Allocated += size;
                    parent.FileCount++;
                    count++;
                }

                if (count % 1000 == 0)
                    progress?.Report(new ScanProgress(count, parent.FullPath, -1));
            }
        }
    }
}
