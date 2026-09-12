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
        catch (Exception ex)
        {
            // 上一次扫描快照坏了只是少了「和上次对比」这一条规则，不该打断扫描。
            AppLog.Record("Snapshot", ex, "load " + LogRedactor.ScrubPath(FilePath(drive)));
            return null;
        }
    }

    public static ScanSnapshot Capture(FileEntry root)
    {
        var snap = new ScanSnapshot
        {
            Drive = root.FullPath,
            ScannedAt = DateTime.Now,
            RootSize = root.Size,
        };
        foreach (var c in root.ChildList)
        {
            if (c.IsFilesGroup) continue;
            snap.Folders[c.Name] = c.Size;
        }
        var stack = new Stack<FileEntry>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var c in n.ChildList)
            {
                if (c.IsDirectory) { stack.Push(c); continue; }
                if (c.IsFilesGroup) { stack.Push(c); continue; }
                if (c.Size >= 50L * 1024 * 1024 && !string.IsNullOrEmpty(c.FullPath))
                    snap.LargeFiles[c.FullPath] = c.Size;
            }
        }
        return snap;
    }

    /// <summary>
    /// 存盘。**原子替换**：先写 .tmp 再 Move 覆盖，
    /// 中断（取消 / 崩 / 断电）只会留下临时文件，不会留下半截损坏的 json 让下次读取报错。
    /// </summary>
    public void Save(CancellationToken ct = default)
    {
        string path = FilePath(Drive);
        string tmp = path + ".tmp";
        try
        {
            ct.ThrowIfCancellationRequested();
            string dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = false });
            ct.ThrowIfCancellationRequested();
            File.WriteAllText(tmp, json);

            // File.Move(overwrite) 在同一卷上是原子的
            File.Move(tmp, path, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tmp);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tmp);
            // 快照存不下来不影响本次结果，只影响下次的「和上次对比」。
            AppLog.Record("Snapshot", ex, "save " + LogRedactor.ScrubPath(path));
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* 清临时文件失败不值得再记一笔 */ }
    }
}
