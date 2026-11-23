using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 轻量持久化目录结构缓存（前 5 层主要目录 + 大文件摘要）：
/// 让软件冷启动时在 &lt;100ms 内先秒开完整首页；深度或数量截断的节点会标记 Partial，
/// 在后台 MFT/USN 校准完成前若用户点击展开，也会立即按需从磁盘补全当前目录，不出现“没头了”。
/// </summary>
public static class OrganizeTreeCache
{
    private const int MaxDepth = 5;
    private const int MaxNodes = 8000;
    private const int MaxFilesPerDir = 12;
    private const long MinCachedFileBytes = 1024L * 1024;

    private sealed class CachedDriveTree
    {
        public string Drive { get; set; } = "";
        public DateTime ScannedAtUtc { get; set; }
        public long RootSize { get; set; }
        public long RootAllocated { get; set; }
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
        public CachedDirNode? Root { get; set; }
    }

    private sealed class CachedDirNode
    {
        [JsonPropertyName("n")] public string Name { get; set; } = "";
        [JsonPropertyName("s")] public long Size { get; set; }
        [JsonPropertyName("a")] public long Allocated { get; set; }
        [JsonPropertyName("fc")] public int FileCount { get; set; }
        [JsonPropertyName("dc")] public int FolderCount { get; set; }
        [JsonPropertyName("h")] public bool IsHidden { get; set; }
        [JsonPropertyName("sy")] public bool IsSystem { get; set; }
        [JsonPropertyName("rp")] public bool IsReparsePoint { get; set; }
        [JsonPropertyName("f")] public bool IsFile { get; set; }
        [JsonPropertyName("pt")] public bool Partial { get; set; }
        [JsonPropertyName("m")] public long ModifiedTicks { get; set; }
        [JsonPropertyName("c")] public List<CachedDirNode>? Children { get; set; }
    }

    private static string CachePath(string drive)
    {
        string letter = new string((drive ?? "C").Where(char.IsLetterOrDigit).ToArray());
        if (letter.Length == 0) letter = "disk";
        return Path.Combine(AppPaths.ConfigDirectory, "tree-cache-" + letter + ".json");
    }

    public static FileEntry? TryLoad(string drive, out DateTime scannedAtUtc)
    {
        scannedAtUtc = default;
        try
        {
            string path = CachePath(drive);
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<CachedDriveTree>(json);
            if (dto?.Root == null || string.IsNullOrWhiteSpace(dto.Drive))
                return null;

            scannedAtUtc = dto.ScannedAtUtc;
            string rootPath = dto.Drive.EndsWith('\\') ? dto.Drive : dto.Drive + "\\";
            var root = new FileEntry
            {
                Name = rootPath,
                FullPath = rootPath,
                Kind = EntryKind.Directory,
                Size = dto.RootSize,
                Allocated = dto.RootAllocated,
                FileCount = dto.FileCount,
                FolderCount = dto.FolderCount,
                IsCachePartial = dto.Root.Partial,
            };

            if (dto.Root.Children != null)
            {
                foreach (var childDto in dto.Root.Children)
                {
                    var child = MaterializeDir(childDto, root, rootPath, depth: 1);
                    root.Children.Add(child);
                }
            }
            return root;
        }
        catch (Exception ex)
        {
            AppLog.Record("Scan", ex, "load tree cache " + LogRedactor.ScrubPath(CachePath(drive)));
            return null;
        }
    }

    private static FileEntry MaterializeDir(CachedDirNode dto, FileEntry parent, string parentPath, int depth)
    {
        string full = Path.Combine(parentPath, dto.Name);
        if (dto.IsFile)
        {
            return new FileEntry
            {
                Name = dto.Name,
                FullPath = full,
                Kind = EntryKind.File,
                Category = FileClassifier.Classify(dto.Name),
                Parent = parent,
                Size = dto.Size,
                Allocated = dto.Allocated > 0 ? dto.Allocated : dto.Size,
                IsHidden = dto.IsHidden,
                IsSystem = dto.IsSystem,
                IsReparsePoint = dto.IsReparsePoint,
                Modified = dto.ModifiedTicks > 0 ? new DateTime(dto.ModifiedTicks, DateTimeKind.Local) : DateTime.MinValue,
            };
        }

        int childDirMaterialized = 0;
        int childFileMaterialized = 0;
        if (dto.Children != null)
        {
            foreach (var c in dto.Children)
            {
                if (c.IsFile) childFileMaterialized++;
                else childDirMaterialized++;
            }
        }

        // 只要缓存里子目录或文件少于原目录真实数量，就标成 Partial 以便点击时按需从磁盘补全（包含 1~2 层小文件目录）。
        bool inferredPartial = depth >= 1
            && (dto.FolderCount > childDirMaterialized
                || (dto.FileCount > childFileMaterialized && dto.Size > 0));

        var dir = new FileEntry
        {
            Name = dto.Name,
            FullPath = full,
            Kind = EntryKind.Directory,
            Parent = parent,
            Size = dto.Size,
            Allocated = dto.Allocated,
            FileCount = dto.FileCount,
            FolderCount = dto.FolderCount,
            IsHidden = dto.IsHidden,
            IsSystem = dto.IsSystem,
            IsReparsePoint = dto.IsReparsePoint,
            IsCachePartial = dto.Partial || inferredPartial,
            Modified = dto.ModifiedTicks > 0 ? new DateTime(dto.ModifiedTicks, DateTimeKind.Local) : DateTime.MinValue,
        };

        if (dto.Children != null)
        {
            foreach (var c in dto.Children)
                dir.Children.Add(MaterializeDir(c, dir, full, depth + 1));
        }
        return dir;
    }

    public static void Save(FileEntry root, CancellationToken ct = default)
    {
        if (root == null || string.IsNullOrWhiteSpace(root.FullPath)) return;
        string path = CachePath(root.FullPath);
        string tmp = path + ".tmp";
        try
        {
            ct.ThrowIfCancellationRequested();
            int remaining = MaxNodes;
            var dto = new CachedDriveTree
            {
                Drive = root.FullPath,
                ScannedAtUtc = DateTime.UtcNow,
                RootSize = root.Size,
                RootAllocated = root.Allocated,
                FileCount = root.FileCount,
                FolderCount = root.FolderCount,
                Root = CaptureDir(root, depth: 0, ref remaining, ct),
            };

            string dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            });
            ct.ThrowIfCancellationRequested();
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tmp);
        }
        catch (Exception ex)
        {
            TryDelete(tmp);
            AppLog.Record("Scan", ex, "save tree cache " + LogRedactor.ScrubPath(path));
        }
    }

    private static CachedDirNode CaptureDir(FileEntry dir, int depth, ref int remaining, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var node = new CachedDirNode
        {
            Name = dir.Name,
            Size = dir.Size,
            Allocated = dir.Allocated,
            FileCount = dir.FileCount,
            FolderCount = dir.FolderCount,
            IsHidden = dir.IsHidden,
            IsSystem = dir.IsSystem,
            IsReparsePoint = dir.IsReparsePoint,
            ModifiedTicks = dir.Modified == DateTime.MinValue ? 0 : dir.Modified.Ticks,
        };

        if (!dir.HasChildren) return node;

        var subDirs = new List<FileEntry>();
        var largeFiles = new List<FileEntry>();
        foreach (var child in dir.ChildList)
        {
            if (child.IsFilesGroup) continue;
            if (child.IsDirectory)
                subDirs.Add(child);
            else if (child.Size >= MinCachedFileBytes)
                largeFiles.Add(child);
        }

        if (depth >= MaxDepth || remaining <= 0)
        {
            node.Partial = subDirs.Count > 0 || largeFiles.Count > 0 || dir.FolderCount > 0 || dir.FileCount > 0;
            return node;
        }

        if (subDirs.Count == 0 && largeFiles.Count == 0)
        {
            node.Partial = dir.FolderCount > 0 || (dir.FileCount > 0 && dir.Size > 0);
            return node;
        }

        subDirs.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        int takeDirs = Math.Min(subDirs.Count, Math.Min(depth == 0 ? 120 : 60, remaining));
        remaining -= takeDirs;

        largeFiles.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        int takeFiles = remaining > 0 ? Math.Min(largeFiles.Count, Math.Min(MaxFilesPerDir, remaining)) : 0;
        remaining -= takeFiles;

        node.Partial = takeDirs < subDirs.Count || takeFiles < largeFiles.Count || remaining <= 0;

        int totalChildren = takeDirs + takeFiles;
        if (totalChildren == 0) return node;

        node.Children = new List<CachedDirNode>(totalChildren);
        for (int i = 0; i < takeDirs; i++)
            node.Children.Add(CaptureDir(subDirs[i], depth + 1, ref remaining, ct));
        for (int i = 0; i < takeFiles; i++)
        {
            var f = largeFiles[i];
            node.Children.Add(new CachedDirNode
            {
                Name = f.Name,
                Size = f.Size,
                Allocated = f.Allocated,
                IsHidden = f.IsHidden,
                IsSystem = f.IsSystem,
                IsReparsePoint = f.IsReparsePoint,
                IsFile = true,
                ModifiedTicks = f.Modified == DateTime.MinValue ? 0 : f.Modified.Ticks,
            });
        }

        return node;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
