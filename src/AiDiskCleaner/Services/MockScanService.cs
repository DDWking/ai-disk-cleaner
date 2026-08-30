using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>生成模拟磁盘数据（真实扫盘接入前的占位实现）。</summary>
public static class MockScanService
{
    private static readonly Random Rnd = new();
    private const long KB = 1024, MB = 1024 * KB, GB = 1024 * MB;

    private static readonly string[] FilePrefixes =
    {
        "setup", "install", "update", "cache", "data", "temp", "backup", "report",
        "image", "video", "audio", "notes", "config", "log", "dump", "archive",
        "doc", "sheet", "slides"
    };

    private static readonly Dictionary<string, string[]> ExtByCategory = new()
    {
        ["系统"] = new[] { "dll", "sys", "exe", "mui" },
        ["日志"] = new[] { "log", "etl", "txt" },
        ["临时"] = new[] { "tmp", "temp", "cache", "bak" },
        ["媒体"] = new[] { "jpg", "png", "mp4", "mp3", "mov" },
        ["文档"] = new[] { "docx", "xlsx", "pdf", "pptx" },
        ["代码"] = new[] { "py", "js", "ts", "json", "cs", "cpp", "h" },
        ["压缩包"] = new[] { "zip", "rar", "7z", "msi" },
        ["数据"] = new[] { "db", "sqlite", "dat", "bin" },
    };

    private sealed class DirSpec
    {
        public string Name;
        public double WeightGB;
        public int Files;
        public bool IsFile;
        public List<DirSpec> Children = new();
        public DirSpec(string name, double w, int files = 0, bool isFile = false)
            => (Name, WeightGB, Files, IsFile) = (name, w, files, isFile);
    }

    private static readonly List<DirSpec> RootSpec = BuildSpec();

    private static List<DirSpec> BuildSpec()
    {
        var win = new DirSpec("Windows", 24, 6);
        win.Children.Add(new DirSpec("System32", 12, 20));
        win.Children.Add(new DirSpec("WinSxS", 9, 14));
        win.Children.Add(new DirSpec("Logs", 1.2, 12));
        win.Children.Add(new DirSpec("Temp", 2.5, 10));

        var pf = new DirSpec("Program Files", 15, 4);
        pf.Children.Add(new DirSpec("Google", 2.2, 8));
        pf.Children.Add(new DirSpec("Python311", 1.8, 24));
        pf.Children.Add(new DirSpec("Microsoft", 3.1, 9));

        var users = new DirSpec("Users", 30, 3);
        users.Children.Add(new DirSpec("Public", 2.5, 6));
        var u32098 = new DirSpec("32098", 26, 4);
        u32098.Children.Add(new DirSpec("Downloads", 12, 15));
        u32098.Children.Add(new DirSpec("Documents", 6, 11));
        u32098.Children.Add(new DirSpec("Desktop", 3.5, 9));
        u32098.Children.Add(new DirSpec("AppData", 8, 18));
        users.Children.Add(u32098);

        return new List<DirSpec>
        {
            win,
            pf,
            new DirSpec("Program Files (x86)", 8, 5),
            users,
            new DirSpec("ProgramData", 6, 8),
            new DirSpec("pagefile.sys", 8, isFile: true),
            new DirSpec("hiberfil.sys", 5.5, isFile: true),
        };
    }

    public static FileEntry Scan(string rootName)
    {
        var tree = new FileEntry { Name = rootName, FullPath = rootName, Kind = EntryKind.Directory };
        foreach (var spec in RootSpec)
        {
            if (spec.IsFile)
            {
                AddFile(tree, rootName, spec.Name, (long)(spec.WeightGB * GB * Jitter(0.8, 1.25)), "系统");
            }
            else
            {
                var node = Walk(spec, rootName);
                tree.Children.Add(node);
                tree.Size += node.Size;
            }
        }
        return tree;
    }

    private static FileEntry Walk(DirSpec spec, string parentPath)
    {
        var node = new FileEntry
        {
            Name = spec.Name,
            FullPath = parentPath + "\\" + spec.Name,
            Kind = EntryKind.Directory,
        };
        for (int i = 0; i < spec.Files; i++)
        {
            var cat = Pick(ExtByCategory.Keys.ToArray());
            AddFile(node, node.FullPath, RandomName(Pick(ExtByCategory[cat])), RandomSize(cat), cat);
        }
        foreach (var child in spec.Children)
        {
            var c = Walk(child, node.FullPath);
            node.Children.Add(c);
            node.Size += c.Size;
        }
        return node;
    }

    private static void AddFile(FileEntry parent, string dirPath, string name, long size, string category)
    {
        parent.Children.Add(new FileEntry
        {
            Name = name,
            FullPath = dirPath + "\\" + name,
            Size = size,
            Modified = RandomModified(),
            Category = category,
            Kind = EntryKind.File,
        });
        parent.Size += size;
    }

    private static string Pick(string[] arr) => arr[Rnd.Next(arr.Length)];
    private static double Jitter(double min, double max) => min + Rnd.NextDouble() * (max - min);
    private static string RandomName(string ext) => Pick(FilePrefixes) + "_" + Rnd.Next(9999) + "." + ext;

    private static long RandomSize(string category)
    {
        double baseMB = category switch
        {
            "媒体" => 400, "压缩包" => 250, "系统" => 80, "数据" => 120,
            "文档" => 2, "代码" => 0.05, "日志" => 0.2, "临时" => 0.5,
            _ => 1,
        };
        return (long)(baseMB * MB * Math.Pow(10, Rnd.NextDouble() * 2.5) * Jitter(0.8, 1.25));
    }

    private static DateTime RandomModified()
        => DateTime.Now.AddDays(-Math.Pow(Rnd.NextDouble(), 1.6) * 730);
}
