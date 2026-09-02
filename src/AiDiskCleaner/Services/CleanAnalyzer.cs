using System.IO;
using System.Security.Cryptography;
using AiDiskCleaner.Models;
using AiDiskCleaner.Native;

namespace AiDiskCleaner.Services;

/// <summary>扫完后按规则出可清理项。不走大模型。</summary>
public static class CleanAnalyzer
{
    private static readonly HashSet<string> TempExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".temp", ".cache", ".bak", ".old", ".log", ".etl", ".dmp", ".chk", ".gid",
    };

    private static readonly HashSet<string> InstallExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".msi", ".iso", ".msu",
    };

    private static readonly string[] SafeDirBits =
    {
        @"\temp\", @"\tmp\", @"\cache\", @"\caches\", @"\logs\",
        @"\crashdumps\", @"\minidump\", @"\wer\", @"\downloads\",
        @"\$recycle.bin\", @"\windows\temp\", @"\windows\softwaredistribution\download\",
        @"\appdata\local\temp\", @"\appdata\local\microsoft\windows\inetcache\",
        @"\appdata\local\microsoft\windows\explorer\",
        @"\appdata\local\crashdumps\",
    };

    private static readonly string[] UnsafeBits =
    {
        @"\windows\system32\", @"\windows\syswow64\", @"\windows\winsxs\",
        @"\windows\servicing\", @"\$mft", @"\program files\", @"\program files (x86)\",
    };

    public static CleanReport Analyze(FileEntry root, ScanSnapshot? previous, CancellationToken ct, IProgress<ScanProgress>? progress = null)
    {
        var report = new CleanReport();
        var files = new List<FileEntry>(Math.Max(1024, root.FileCount));
        var dirs = new List<FileEntry>();
        progress?.Report(new ScanProgress(0, Loc.CleanWalk, 5));
        Walk(root, files, dirs, ct);

        progress?.Report(new ScanProgress(files.Count, Loc.CleanRules, 20));
        FillCleanable(report, files, dirs);
        FillLarge(report, files);
        FillOld(report, files);
        FillEmpty(report, dirs);
        FillLongPaths(report, files, dirs);
        progress?.Report(new ScanProgress(files.Count, Loc.CleanShortcuts, 45));
        FillBrokenShortcuts(report, files, ct);
        progress?.Report(new ScanProgress(files.Count, Loc.CleanDups, 70));
        FillDuplicates(report, files, ct);
        progress?.Report(new ScanProgress(files.Count, Loc.CleanCompare, 92));
        FillCompare(report, root, previous);

        report.CleanableBytes = report.Cleanable.Sum(x => x.Size);
        progress?.Report(new ScanProgress(files.Count, Loc.Analyzing, 100));
        return report;
    }

    private static void Walk(FileEntry node, List<FileEntry> files, List<FileEntry> dirs, CancellationToken ct)
    {
        var stack = new Stack<FileEntry>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var n = stack.Pop();
            foreach (var c in n.Children)
            {
                if (c.IsFilesGroup)
                {
                    stack.Push(c);
                    continue;
                }
                if (c.IsDirectory)
                {
                    dirs.Add(c);
                    stack.Push(c);
                }
                else files.Add(c);
            }
        }
    }

    private static void FillCleanable(CleanReport report, List<FileEntry> files, List<FileEntry> dirs)
    {
        foreach (var f in files)
        {
            if (!CanOffer(f)) continue;
            string path = (f.FullPath ?? "").ToLowerInvariant().Replace('/', '\\');
            string ext = Path.GetExtension(f.Name);
            string? reason = null;
            string group;

            if (path.Contains(@"\$recycle.bin\"))
            {
                reason = Loc.ReasonRecycle;
                group = Loc.GroupRecycle;
            }
            else if (LooksLikeTempDir(path) && (TempExt.Contains(ext) || f.Name.StartsWith('~') || f.Size == 0))
            {
                reason = Loc.ReasonTempDir;
                group = Loc.GroupTemp;
            }
            else if (TempExt.Contains(ext) && LooksLikeTempDir(path))
            {
                reason = Loc.ReasonTempExt;
                group = Loc.GroupTemp;
            }
            else if (ext.Equals(".dmp", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\minidump\") || path.Contains(@"\crashdumps\"))
            {
                reason = Loc.ReasonDump;
                group = Loc.GroupDump;
            }
            else if (path.Contains(@"\windows\softwaredistribution\download\") || path.Contains(@"\windows\temp\"))
            {
                reason = Loc.ReasonWinUpdate;
                group = Loc.GroupTemp;
            }
            else if (InstallExt.Contains(ext) && path.Contains(@"\downloads\") && f.Size >= 20L * 1024 * 1024)
            {
                reason = Loc.ReasonInstaller;
                group = Loc.GroupInstaller;
            }
            else if ((ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase) || ext.Equals(".temp", StringComparison.OrdinalIgnoreCase)
                      || ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                     && f.Size >= 8L * 1024 * 1024 && !LooksUnsafe(path))
            {
                reason = Loc.ReasonTempExt;
                group = Loc.GroupTemp;
            }
            else continue;

            report.Cleanable.Add(Item(f, reason, group, selected: group != Loc.GroupInstaller));
        }

        foreach (var d in dirs)
        {
            if (!CanOffer(d)) continue;
            string path = (d.FullPath ?? "").ToLowerInvariant().Replace('/', '\\');
            if (path.EndsWith(@"\windows\temp") || path.EndsWith(@"\appdata\local\temp"))
            {
                report.Cleanable.Add(Item(d, Loc.ReasonTempDir, Loc.GroupTemp, selected: false));
            }
        }

        report.Cleanable.Sort((a, b) => b.Size.CompareTo(a.Size));
        if (report.Cleanable.Count > 400)
            report.Cleanable.RemoveRange(400, report.Cleanable.Count - 400);
    }

    private static void FillLarge(CleanReport report, List<FileEntry> files)
    {
        foreach (var f in files.Where(CanList).OrderByDescending(x => x.Size).Take(80))
        {
            var hint = KnownPaths.LargeHint(f);
            string reason = hint?.Reason ?? Loc.ReasonLarge;
            string group = hint?.Group ?? Loc.GroupLarge;
            report.LargeFiles.Add(Item(f, reason, group, selected: false));
        }
    }

    private static void FillOld(CleanReport report, List<FileEntry> files)
    {
        var cutoff = DateTime.Now.AddYears(-1);
        foreach (var f in files
                     .Where(x => CanList(x) && x.Modified != DateTime.MinValue && x.Modified < cutoff && x.Size >= 8L * 1024 * 1024)
                     .OrderBy(x => x.Modified)
                     .Take(80))
            report.OldFiles.Add(Item(f, Loc.ReasonOld(f.AgeText), Loc.GroupOld, selected: false));
    }

    private static void FillEmpty(CleanReport report, List<FileEntry> dirs)
    {
        foreach (var d in dirs)
        {
            if (!CanOffer(d)) continue;
            if (d.FileCount != 0 || d.FolderCount != 0) continue;
            if (d.Children.Count > 0) continue;
            string path = (d.FullPath ?? "").ToLowerInvariant();
            if (LooksUnsafe(path)) continue;
            if (d.Name.StartsWith('.')) continue;
            report.EmptyFolders.Add(Item(d, Loc.ReasonEmpty, Loc.GroupEmpty, selected: false));
            if (report.EmptyFolders.Count >= 120) break;
        }
    }

    private static void FillLongPaths(CleanReport report, List<FileEntry> files, List<FileEntry> dirs)
    {
        foreach (var e in files.Concat(dirs))
        {
            if (string.IsNullOrEmpty(e.FullPath) || e.IsFilesGroup) continue;
            if (e.FullPath.Length < 240) continue;
            report.LongPaths.Add(Item(e, Loc.ReasonLong(e.FullPath.Length), Loc.GroupLong, selected: false, canDelete: CanOffer(e)));
            if (report.LongPaths.Count >= 80) break;
        }
    }

    private static void FillBrokenShortcuts(CleanReport report, List<FileEntry> files, CancellationToken ct)
    {
        int checkedN = 0;
        foreach (var f in files)
        {
            if (checkedN > 2500) break;
            if (!f.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(f.FullPath) || !File.Exists(f.FullPath)) continue;
            if (!CanOffer(f)) continue;
            checkedN++;
            ct.ThrowIfCancellationRequested();
            string? target = ShortcutNative.ResolveTarget(f.FullPath);
            if (string.IsNullOrEmpty(target)) continue;
            bool exists = File.Exists(target) || Directory.Exists(target);
            if (exists) continue;
            report.BrokenShortcuts.Add(Item(f, Loc.ReasonBroken(target), Loc.GroupShortcut, selected: false));
            if (report.BrokenShortcuts.Count >= 80) break;
        }
    }

    private static void FillDuplicates(CleanReport report, List<FileEntry> files, CancellationToken ct)
    {
        const long minSize = 8L * 1024 * 1024;
        var bySize = new Dictionary<long, List<FileEntry>>();
        foreach (var f in files)
        {
            if (!CanList(f) || f.Size < minSize) continue;
            if (string.IsNullOrEmpty(f.FullPath)) continue;
            if (!bySize.TryGetValue(f.Size, out var list))
            {
                list = new List<FileEntry>();
                bySize[f.Size] = list;
            }
            list.Add(f);
        }

        int hashed = 0;
        foreach (var kv in bySize.Where(x => x.Value.Count >= 2).OrderByDescending(x => x.Key))
        {
            ct.ThrowIfCancellationRequested();
            if (hashed > 80) break;
            var groups = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in kv.Value)
            {
                if (hashed > 80) break;
                if (!File.Exists(f.FullPath)) continue;
                string? hash = HashHead(f.FullPath, f.Size);
                hashed++;
                if (hash == null) continue;
                if (!groups.TryGetValue(hash, out var g))
                {
                    g = new List<FileEntry>();
                    groups[hash] = g;
                }
                g.Add(f);
            }
            foreach (var g in groups.Values)
            {
                if (g.Count < 2) continue;
                report.DupGroupCount++;
                var keep = g.OrderBy(x => x.FullPath.Length).First();
                foreach (var f in g)
                {
                    bool extra = !ReferenceEquals(f, keep);
                    report.Duplicates.Add(Item(
                        f,
                        extra ? Loc.ReasonDupExtra(keep.FullPath) : Loc.ReasonDupKeep,
                        Loc.GroupDup,
                        selected: extra && CanOffer(f),
                        canDelete: extra && CanOffer(f)));
                }
                if (report.Duplicates.Count >= 200) return;
            }
        }
    }

    private static string? HashHead(string path, long size)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int n = (int)Math.Min(size, 256 * 1024);
            byte[] buf = new byte[n];
            int read = fs.Read(buf, 0, n);
            if (read <= 0) return null;
            byte[] hash = SHA256.HashData(buf.AsSpan(0, read));
            return size + ":" + Convert.ToHexString(hash);
        }
        catch { return null; }
    }

    private static void FillCompare(CleanReport report, FileEntry root, ScanSnapshot? previous)
    {
        if (previous == null)
        {
            report.CompareNote = Loc.CompareFirst;
            return;
        }

        report.CompareNote = Loc.CompareSince(previous.ScannedAt, FileEntry.FormatSize(root.Size - previous.RootSize));
        var now = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in root.Children)
        {
            if (c.IsFilesGroup) continue;
            now[c.Name] = c;
        }

        foreach (var kv in now)
        {
            previous.Folders.TryGetValue(kv.Key, out long old);
            long delta = kv.Value.Size - old;
            if (Math.Abs(delta) < 8L * 1024 * 1024) continue;
            string reason = delta >= 0
                ? Loc.ReasonGrew(FileEntry.FormatSize(delta))
                : Loc.ReasonShrunk(FileEntry.FormatSize(-delta));
            report.Compare.Add(new CleanItem
            {
                Name = kv.Value.Name,
                FullPath = kv.Value.FullPath,
                Size = Math.Abs(delta),
                Reason = reason,
                Group = Loc.GroupCompare,
                CanDelete = false,
                Selected = false,
                Entry = kv.Value,
                IsDirectory = kv.Value.IsDirectory,
            });
        }

        foreach (var name in previous.Folders.Keys)
        {
            if (now.ContainsKey(name)) continue;
            long old = previous.Folders[name];
            if (old < 8L * 1024 * 1024) continue;
            report.Compare.Add(new CleanItem
            {
                Name = name,
                FullPath = name,
                Size = old,
                Reason = Loc.ReasonGone,
                Group = Loc.GroupCompare,
                CanDelete = false,
            });
        }

        report.Compare.Sort((a, b) => b.Size.CompareTo(a.Size));
    }

    private static CleanItem Item(FileEntry e, string reason, string group, bool selected, bool canDelete = true)
        => new()
        {
            Name = e.Name,
            FullPath = e.FullPath,
            Size = e.Size,
            Reason = reason,
            Group = group,
            CanDelete = canDelete && CanOffer(e),
            Selected = selected && canDelete && CanOffer(e),
            Entry = e,
            IsDirectory = e.IsDirectory,
        };

    private static bool CanList(FileEntry e)
        => !e.IsFilesGroup && !string.IsNullOrEmpty(e.FullPath) && !e.Name.StartsWith('$');

    private static bool CanOffer(FileEntry e)
    {
        if (!CanList(e)) return false;
        if (RecycleService.IsProtected(e)) return false;
        string path = (e.FullPath ?? "").ToLowerInvariant().Replace('/', '\\');
        if (LooksUnsafe(path)) return false;
        return true;
    }

    private static bool LooksLikeTempDir(string path)
        => SafeDirBits.Any(path.Contains);

    private static bool LooksUnsafe(string path)
        => UnsafeBits.Any(path.Contains);
}
