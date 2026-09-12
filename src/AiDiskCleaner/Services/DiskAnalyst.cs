using System.Text.Json;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

/// <summary>
/// AI 能看到的宿主信息。**只有只读入口** ——
/// 以前这里还有 OnChecksChanged / OnSuggest，让模型能直接改勾选；
/// 那违反「AI 只解释和建议，选择由用户做」，已删除。
/// </summary>
public interface IAnalystHost
{
    FileEntry? Root { get; }
    CleanReport? Report { get; }
}

public static class DiskAnalyst
{
    public const int MaxRounds = 2;
    public const int TokenBudget = 8000;
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
        lines.Add("");
        lines.Add("cleanable paths (copy these):");
        foreach (var x in report.Cleanable.OrderByDescending(i => i.Size).Take(24))
            lines.Add($"  {x.FullPath}\t{x.SizeText}\t{x.Reason}");
        lines.Add($"old: {report.OldFiles.Count:N0}, {Sum(report.OldFiles)}");
        lines.Add($"duplicates: {report.Duplicates.Count:N0} in {report.DupGroupCount:N0} groups, {Sum(report.Duplicates)}");
        if (!string.IsNullOrWhiteSpace(report.CompareNote))
            lines.Add("compare: " + report.CompareNote);
        var known = AppSignatures.HitsIn(RootFolders(root).Concat(root.ChildList)).Take(6).ToList();
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
                string extra = string.IsNullOrEmpty(sig.Plain) ? "" : "  " + sig.Plain;
                if (!string.IsNullOrEmpty(sig.Migrate)) extra += "  migrate:" + sig.Migrate;
                lines.Add($"  {FileEntry.FormatSize(size)}  {sig.Name}  [{risk}]{extra}  {sample}");
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 右键「问 AI 这是什么」的提问内容：只给路径指纹，让模型解释这个文件夹。
    /// 不再问「哪些子项能删」——风险由规则判定，AI 只负责说明。
    /// </summary>
    public static string FolderAsk(FileEntry dir)
    {
        var kids = new List<string>();
        foreach (var c in dir.ChildList
                     .Where(x => !x.IsFilesGroup && !string.IsNullOrEmpty(x.FullPath))
                     .OrderByDescending(x => x.Size)
                     .Take(20))
            kids.Add("  " + Line(c));
        Listed.Add(Norm(dir.FullPath));
        return Loc.AiFolderAskUser(Line(dir), string.Join(Environment.NewLine, kids));
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
            // 只保留**只读**工具。以前这里还有 set_checked / suggest，它们能让模型
            // 直接勾选清理项 —— 那是用户才能做的决定，已经删除。
            ("report_finding",
                "Report what you found about one item. Text only; this never changes selection or risk.",
                Props(("path", "The item path"), ("note", "One-line finding in the user's language"))),
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
            // set_checked / suggest 已移除：它们会让模型直接改勾选。
            // AI 只解释和建议，选择永远由用户自己做（见 PROGRESS 第十三/十四阶段）。
            "set_checked" or "suggest" => "tool removed: the app never lets AI change the selection",
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
        var kids = dir.ChildList
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



    static IEnumerable<CleanItem> AllItems(CleanReport r)
        => r.Cleanable.Concat(r.LargeFiles);

    static bool Hit(CleanItem x, string q)
        => (x.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
           || (x.FullPath ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
           || (x.Reason ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
           || (x.Group ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);

    static IEnumerable<FileEntry> RootFolders(FileEntry root)
        => root.ChildList
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
            foreach (var c in n.ChildList)
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
