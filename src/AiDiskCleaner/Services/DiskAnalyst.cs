using System.Text.Json;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

public interface IAnalystHost
{
    FileEntry? Root { get; }
    CleanReport? Report { get; }
    void OnChecksChanged(bool showLarge);
    void OnSuggest(string path, string note);
}

public static class DiskAnalyst
{
    public const int MaxRounds = 6;
    public const int TokenBudget = 12000;
    static readonly HashSet<string> Listed = new(StringComparer.OrdinalIgnoreCase);

    public static string SystemPrompt() => Loc.AiAnalystSystem;

    public static string Opening(FileEntry root, CleanReport report, long used, long total)
    {
        var lines = new List<string>
        {
            Loc.AiScanHeader,
            $"volume: {root.FullPath}  used {FileEntry.FormatSize(used)} / {FileEntry.FormatSize(total)}",
            $"tree: {root.FileCount:N0} files, {root.FolderCount:N0} folders, {FileEntry.FormatSize(root.Size)}",
            "",
            "largest folders:",
        };
        foreach (var d in RootFolders(root).Take(8))
            lines.Add($"  {Line(d)}");
        lines.Add("");
        lines.Add("largest files:");
        foreach (var f in report.LargeFiles.Take(10))
            lines.Add($"  {ItemLine(f)}");
        lines.Add("");
        lines.Add($"cleanable: {report.Cleanable.Count:N0} items, {FileEntry.FormatSize(report.CleanableBytes)}");
        foreach (var g in report.Cleanable.GroupBy(x => string.IsNullOrEmpty(x.Group) ? "-" : x.Group)
                     .OrderByDescending(x => x.Sum(i => i.Size)))
            lines.Add($"  {g.Key}: {g.Count():N0}, {FileEntry.FormatSize(g.Sum(i => i.Size))}");
        lines.Add($"old: {report.OldFiles.Count:N0}, {Sum(report.OldFiles)}");
        lines.Add($"duplicates: {report.Duplicates.Count:N0} in {report.DupGroupCount:N0} groups, {Sum(report.Duplicates)}");
        if (!string.IsNullOrWhiteSpace(report.CompareNote))
            lines.Add("compare: " + report.CompareNote);
        var known = AppSignatures.HitsIn(RootFolders(root).Concat(root.Children)).Take(6).ToList();
        if (known.Count > 0)
        {
            lines.Add("");
            lines.Add("known apps (do not invent; copy these labels):");
            foreach (var (sig, sample, size) in known)
            {
                string risk = sig.Risk switch
                {
                    SigRisk.Safe => "safe cache",
                    SigRisk.Cautious => "confirm",
                    SigRisk.Keep => "keep / migrate",
                    SigRisk.Bloat => "bloatware, suggest uninstall",
                    _ => "",
                };
                string extra = string.IsNullOrEmpty(sig.Note) ? "" : "  " + sig.Note;
                if (!string.IsNullOrEmpty(sig.Migrate)) extra += "  migrate:" + sig.Migrate;
                lines.Add($"  {FileEntry.FormatSize(size)}  {sig.Name}  [{risk}]{extra}  {sample}");
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    public static string FolderAsk(FileEntry dir)
    {
        var lines = new List<string>
        {
            Loc.IsEn
                ? "Explain this folder only. What is it? Which children look deletable? Do not invent files."
                : "只解释这个文件夹：它是什么、哪些子项可能能删。不要编造。",
            Line(dir),
            "children (top 20):",
        };
        foreach (var c in dir.Children
                     .Where(x => !x.IsFilesGroup && !string.IsNullOrEmpty(x.FullPath))
                     .OrderByDescending(x => x.Size)
                     .Take(20))
            lines.Add("  " + Line(c));
        Listed.Add(Norm(dir.FullPath));
        return string.Join(Environment.NewLine, lines);
    }

    public static void ResetSession() => Listed.Clear();

    public static int EstimateTokens(IEnumerable<AiMsg> turns)
        => Math.Max(1, turns.Sum(t => (t.Text?.Length ?? 0) + 8) / 4);

    public static IReadOnlyList<object> Tools(AiProtocol proto)
    {
        var list = new (string Name, string Desc, object Schema)[]
        {
            ("list_folder",
                "List the largest direct children of a folder from the scan tree. Max 40. Use to explain what a large folder is.",
                Props(("path", "Folder path, e.g. C:\\\\Users"))),
            ("search_clean",
                "Search the cleanable and largest-file lists by name, path, reason, or group. Returns up to 30 items.",
                Props(("query", "Text to search"))),
            ("set_checked",
                "Check or uncheck items on the clean list. Cannot delete. Only safe cleanable items (temp/cache, dumps, recycle) and large files outside Windows/Program Files/system. Pass full paths.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["paths"] = new Dictionary<string, object>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object> { ["type"] = "string" },
                            ["description"] = "Full paths",
                        },
                        ["checked"] = new Dictionary<string, object>
                        {
                            ["type"] = "boolean",
                            ["description"] = "true to check, false to uncheck",
                        },
                    },
                    ["required"] = new[] { "paths", "checked" },
                }),
            ("suggest",
                "Mark files the user might delete. Checks them on the right clean list. Does not delete. Call this for every recommended FILE path, not Windows/Program Files/Users as a whole. note = specific reason in the user's language.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["items"] = new Dictionary<string, object>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["path"] = new Dictionary<string, object> { ["type"] = "string" },
                                    ["note"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Short reason, one line" },
                                },
                                ["required"] = new[] { "path", "note" },
                            },
                        },
                    },
                    ["required"] = new[] { "items" },
                }),
        };
        if (proto == AiProtocol.Anthropic)
            return list.Select(t => (object)new { name = t.Name, description = t.Desc, input_schema = t.Schema }).ToList();
        if (proto == AiProtocol.Responses)
            return list.Select(t => (object)new { type = "function", name = t.Name, description = t.Desc, parameters = t.Schema }).ToList();
        return list.Select(t => (object)new
        {
            type = "function",
            function = new { name = t.Name, description = t.Desc, parameters = t.Schema },
        }).ToList();
    }

    public static string Run(string name, string argsJson, IAnalystHost host)
    {
        JsonElement args = default;
        try { args = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson).RootElement; }
        catch { return "bad json"; }
        return name switch
        {
            "list_folder" => ListFolder(Str(args, "path"), host),
            "search_clean" => Search(Str(args, "query"), host),
            "set_checked" => SetChecked(args, host),
            "suggest" => Suggest(args, host),
            _ => "unknown tool",
        };
    }

    public static bool CanAiCheck(CleanItem x)
    {
        if (!x.CanDelete) return false;
        if (x.Group == Loc.GroupTemp || x.Group == Loc.GroupDump || x.Group == Loc.GroupRecycle)
            return true;
        if (x.Group == Loc.GroupLarge || x.Group == Loc.GroupInstaller)
            return !IsSystemPath(x.FullPath);
        return false;
    }

    static string ListFolder(string path, IAnalystHost host)
    {
        var root = host.Root;
        if (root == null) return "no scan";
        var dir = Find(root, path);
        if (dir == null) return "folder not in scan tree: " + path;
        string key = Norm(dir.FullPath);
        if (!Listed.Add(key))
            return "already listed this folder; use the previous result";
        var kids = dir.Children
            .Where(c => !c.IsFilesGroup && !string.IsNullOrEmpty(c.FullPath))
            .OrderByDescending(c => c.Size)
            .Take(40)
            .Select(Line)
            .ToList();
        if (kids.Count == 0) return "empty";
        string? known = KnownPaths.Describe(dir.FullPath);
        string head = known == null ? "" : known + Environment.NewLine;
        return head + string.Join(Environment.NewLine, kids);
    }

    static string Search(string query, IAnalystHost host)
    {
        var report = host.Report;
        if (report == null) return "no scan";
        string q = (query ?? "").Trim();
        if (q.Length == 0) return "empty query";
        var hits = AllItems(report)
            .Where(x => Hit(x, q))
            .Take(30)
            .Select(ItemLine)
            .ToList();
        return hits.Count == 0 ? "no matches" : string.Join(Environment.NewLine, hits);
    }

    static string SetChecked(JsonElement args, IAnalystHost host)
    {
        var report = host.Report;
        if (report == null) return "no scan";
        bool on = args.TryGetProperty("checked", out var c) && c.ValueKind is JsonValueKind.True;
        var paths = new List<string>();
        if (args.TryGetProperty("paths", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in arr.EnumerateArray())
                if (p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
                    paths.Add(p.GetString()!);
        }
        if (paths.Count == 0) return "no paths";
        int ok = 0, blocked = 0, missing = 0;
        bool large = false;
        var items = AllItems(report).ToList();
        foreach (var path in paths.Take(80))
        {
            var item = items.FirstOrDefault(x => PathEq(x.FullPath, path))
                       ?? items.FirstOrDefault(x => x.Name.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (item == null) { missing++; continue; }
            if (!CanAiCheck(item)) { blocked++; continue; }
            item.Selected = on;
            ok++;
            if (on) host.OnSuggest(item.FullPath, item.Reason);
            if (item.Group == Loc.GroupLarge || item.Group == Loc.GroupInstaller) large = true;
        }
        if (ok > 0) host.OnChecksChanged(large);
        return $"checked={on} ok={ok} blocked={blocked} missing={missing}";
    }

    static string Suggest(JsonElement args, IAnalystHost host)
    {
        if (!args.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return "no items";
        int n = 0;
        foreach (var it in arr.EnumerateArray().Take(40))
        {
            string path = Str(it, "path");
            string note = Str(it, "note");
            if (string.IsNullOrWhiteSpace(path)) continue;
            host.OnSuggest(path, string.IsNullOrWhiteSpace(note) ? "AI" : note.Trim());
            n++;
        }
        return $"marked {n}";
    }

    static IEnumerable<CleanItem> AllItems(CleanReport r)
        => r.Cleanable.Concat(r.LargeFiles);

    static bool Hit(CleanItem x, string q)
        => (x.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
           || (x.FullPath ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
           || (x.Reason ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
           || (x.Group ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);

    static IEnumerable<FileEntry> RootFolders(FileEntry root)
        => root.Children
            .Where(c => c.IsDirectory && !c.IsFilesGroup && !string.IsNullOrEmpty(c.FullPath))
            .OrderByDescending(c => c.Size);

    static FileEntry? Find(FileEntry root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return root;
        if (PathEq(root.FullPath, path)) return root;
        var stack = new Stack<FileEntry>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var c in n.Children)
            {
                if (PathEq(c.FullPath, path)) return c;
                if (c.IsDirectory) stack.Push(c);
            }
        }
        return null;
    }

    static bool PathEq(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    }

    static string Norm(string p) => p.Replace('/', '\\').TrimEnd('\\');

    static bool IsSystemPath(string? path)
    {
        string p = Norm(path ?? "").ToLowerInvariant();
        if (p.Contains(@"\$recycle.bin")) return false;
        return p.Contains(@"\windows\") || p.EndsWith(@"\windows")
               || p.Contains(@"\program files")
               || p.Contains(@"\system volume information")
               || p.Contains(@"\$");
    }

    static string Line(FileEntry e) => KnownPaths.Fingerprint(e);

    static string ItemLine(CleanItem x)
    {
        string extra = x.Entry != null ? KnownPaths.Fingerprint(x.Entry) : x.FullPath;
        return $"{x.SizeText}  [{x.Group}]  {(x.CanDelete ? "" : "protected ")}{(CanAiCheck(x) ? "checkable " : "")}{x.Reason}  {extra}";
    }

    static object Props(params (string Name, string Desc)[] fields)
    {
        var props = new Dictionary<string, object>();
        foreach (var f in fields)
            props[f.Name] = new Dictionary<string, object> { ["type"] = "string", ["description"] = f.Desc };
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = fields.Select(f => f.Name).ToArray(),
        };
    }

    static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    static string Sum(List<CleanItem> items)
        => FileEntry.FormatSize(items.Sum(x => x.Size));
}
