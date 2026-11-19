using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// 轻量持久化目录结构缓存（仅前 3 层主要目录，通常 &lt; 250 KB）：
/// 让软件冷启动时在 &lt;100ms 内先秒开完整首页，随后后台静默跑 MFT/USN 校准，消除卡顿与等待感。
/// </summary>
public static class OrganizeTreeCache
{
    private const int MaxDepth = 3;
    private const int MaxNodes = 2500;

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
            };

            if (dto.Root.Children != null)
            {
                foreach (var childDto in dto.Root.Children)
                {
                    var child = MaterializeDir(childDto, root, rootPath);
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

    private static FileEntry MaterializeDir(CachedDirNode dto, FileEntry parent, string parentPath)
    {
        string full = Path.Combine(parentPath, dto.Name);
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
            Modified = dto.ModifiedTicks > 0 ? new DateTime(dto.ModifiedTicks, DateTimeKind.Local) : DateTime.MinValue,
        };

        if (dto.Children != null)
        {
            foreach (var c in dto.Children)
                dir.Children.Add(MaterializeDir(c, dir, full));
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

        if (depth >= MaxDepth || remaining <= 0 || !dir.HasChildren)
            return node;

        var subDirs = new List<FileEntry>();
        foreach (var child in dir.ChildList)
        {
            if (child.IsDirectory && !child.IsFilesGroup)
                subDirs.Add(child);
        }
        if (subDirs.Count == 0) return node;

        subDirs.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        int take = Math.Min(subDirs.Count, Math.Min(depth == 0 ? 120 : 60, remaining));
        remaining -= take;

        node.Children = new List<CachedDirNode>(take);
        for (int i = 0; i < take; i++)
            node.Children.Add(CaptureDir(subDirs[i], depth + 1, ref remaining, ct));

        return node;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
