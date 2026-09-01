using System.IO;
using System.Text.Json;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

public sealed class ScanSnapshot
{
    public string Drive { get; set; } = "";
    public DateTime ScannedAt { get; set; }
    public long RootSize { get; set; }
    public Dictionary<string, long> Folders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> LargeFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static string FilePath(string drive)
    {
        string letter = new string((drive ?? "C").Where(char.IsLetterOrDigit).ToArray());
        if (letter.Length == 0) letter = "disk";
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DashaoHuo", "last-scan-" + letter + ".json");
    }

    public static ScanSnapshot? Load(string drive)
    {
        try
        {
            string path = FilePath(drive);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ScanSnapshot>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    public static ScanSnapshot Capture(FileEntry root)
    {
        var snap = new ScanSnapshot
        {
            Drive = root.FullPath,
            ScannedAt = DateTime.Now,
            RootSize = root.Size,
        };
        foreach (var c in root.Children)
        {
            if (c.IsFilesGroup) continue;
            snap.Folders[c.Name] = c.Size;
        }
        var stack = new Stack<FileEntry>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var c in n.Children)
            {
                if (c.IsDirectory) { stack.Push(c); continue; }
                if (c.IsFilesGroup) { stack.Push(c); continue; }
                if (c.Size >= 50L * 1024 * 1024 && !string.IsNullOrEmpty(c.FullPath))
                    snap.LargeFiles[c.FullPath] = c.Size;
            }
        }
        return snap;
    }

    public void Save()
    {
        try
        {
            string path = FilePath(Drive);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch { }
    }
}
