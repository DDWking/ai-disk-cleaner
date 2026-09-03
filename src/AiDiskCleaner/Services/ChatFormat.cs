using System.Text.RegularExpressions;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

public enum ChatPartKind { Text, Path, Break, Heading }

public sealed class ChatPart
{
    public ChatPartKind Kind { get; init; }
    public string Text { get; init; } = "";
    public string? Path { get; init; }
    public string? Note { get; init; }
}

public static class ChatFormat
{
    static readonly Regex GotoLine = new(
        @"^\s*(?:GOTO|跳转)[\s|:：]+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex WinPath = new(
        @"[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*",
        RegexOptions.Compiled);

    static readonly Regex Bullet = new(@"^\s*(?:[-*•]|\d+[.)、])\s+", RegexOptions.Compiled);

    public static List<ChatPart> Parse(string src)
    {
        var parts = new List<ChatPart>();
        if (string.IsNullOrWhiteSpace(src)) return parts;
        var lines = src.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string? section = null;
        bool pendingBreak = false;
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            string? head = HeadingOf(line);
            if (head != null)
            {
                section = head;
                AddBreak(parts, ref pendingBreak);
                parts.Add(new ChatPart { Kind = ChatPartKind.Heading, Text = head });
                pendingBreak = true;
                continue;
            }
            var item = ItemFrom(line);
            if (item != null)
            {
                AddBreak(parts, ref pendingBreak);
                parts.Add(item);
                pendingBreak = true;
                continue;
            }
            if (section == Loc.SecQuestion || section == Loc.SecSummary || section == null)
            {
                AddBreak(parts, ref pendingBreak);
                parts.Add(new ChatPart { Kind = ChatPartKind.Text, Text = StripMd(line) });
                pendingBreak = true;
            }
        }
        return parts;
    }

    static void AddBreak(List<ChatPart> parts, ref bool pending)
    {
        if (!pending || parts.Count == 0) return;
        parts.Add(new ChatPart { Kind = ChatPartKind.Break });
        pending = false;
    }

    static ChatPart? ItemFrom(string line)
    {
        string work = Bullet.Replace(line, "");
        var g = GotoLine.Match(work);
        if (g.Success) work = g.Groups[1].Value.Trim();
        var m = WinPath.Match(work);
        if (!m.Success || m.Length < 4) return null;
        string path = m.Value.Trim().TrimEnd('.', ',', ';', '，', '。', '、', '`', '"', '\'');
        string rest = (work[..m.Index] + work[(m.Index + m.Length)..]).Trim();
        rest = rest.Trim(' ', '-', '—', '–', '|', '·', ':', '：', '\t');
        string size = TakeSize(ref rest);
        string note = StripMd(rest);
        string label = string.IsNullOrEmpty(size) ? ShortName(path) : ShortName(path) + "  " + size;
        return new ChatPart { Kind = ChatPartKind.Path, Text = label, Path = path, Note = string.IsNullOrEmpty(note) ? null : note };
    }

    static string TakeSize(ref string rest)
    {
        var m = Regex.Match(rest, @"(\d+(?:\.\d+)?\s?(?:KB|MB|GB|G|M|K|TB))", RegexOptions.IgnoreCase);
        if (!m.Success) return "";
        rest = (rest[..m.Index] + rest[(m.Index + m.Length)..]).Trim(' ', '-', '—', '|', '·');
        return m.Groups[1].Value.Replace(" ", "");
    }

    static string? HeadingOf(string line)
    {
        if (WinPath.IsMatch(line)) return null;
        string s = StripMd(line).Trim().TrimEnd(':', '：');
        if (s.Length > 18) return null;
        string u = s.ToUpperInvariant();
        if (u is "SUMMARY" or "OVERVIEW" or "总览" or "概览") return Loc.SecSummary;
        if (u is "FOLDERS" or "LARGE FOLDERS" or "大文件夹" or "大根目录") return Loc.SecFolders;
        if (u is "DELETABLE" or "SAFE TO DELETE" or "可删" or "可能能删" or "建议删除") return Loc.SecDeletable;
        if (u is "KEEP" or "DO NOT DELETE" or "保留" or "别动") return Loc.SecKeep;
        if (u is "QUESTION" or "ASK" or "问你一句" or "问题") return Loc.SecQuestion;
        return null;
    }

    static string StripMd(string s)
        => s.Replace("**", "").Replace("`", "").Trim().TrimStart('#', ' ');

    static string ShortName(string path)
    {
        string p = path.TrimEnd('\\');
        int i = p.LastIndexOf('\\');
        return i < 0 || i == p.Length - 1 ? p : p[(i + 1)..];
    }
}
