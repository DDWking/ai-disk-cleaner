using System.Text.RegularExpressions;
using AiDiskCleaner.Models;

namespace AiDiskCleaner.Services;

public enum ChatPartKind { Text, Path, Break }

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
        @"^\s*(?:GOTO|跳转)[\t ]+([A-Za-z]:\\[^\t|]+?)[\t|]+([^\t|]*?)[\t|]+(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    static readonly Regex WinPath = new(
        @"[A-Za-z]:\\(?:[^\\/:*?""<>|\s]+\\)*[^\\/:*?""<>|\s]*",
        RegexOptions.Compiled);

    public static List<ChatPart> Parse(string src)
    {
        var parts = new List<ChatPart>();
        if (string.IsNullOrEmpty(src)) return parts;
        var lines = src.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        bool any = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) parts.Add(new ChatPart { Kind = ChatPartKind.Break });
            string line = lines[i];
            string head = line.Trim();
            if (head is "SUMMARY" or "FOLDERS" or "DELETABLE" or "KEEP" or "QUESTION"
                or "总览" or "大文件夹" or "可删" or "保留")
            {
                parts.Add(new ChatPart { Kind = ChatPartKind.Text, Text = Title(head) });
                continue;
            }
            var g = GotoLine.Match(line);
            if (g.Success)
            {
                any = true;
                string path = g.Groups[1].Value.Trim().TrimEnd('.', ',', '，', '。');
                string size = g.Groups[2].Value.Trim();
                string note = g.Groups[3].Value.Trim();
                string label = string.IsNullOrEmpty(size) ? ShortName(path) : ShortName(path) + "  " + size;
                parts.Add(new ChatPart { Kind = ChatPartKind.Path, Text = label, Path = path, Note = note });
                if (!string.IsNullOrEmpty(note))
                    parts.Add(new ChatPart { Kind = ChatPartKind.Text, Text = "  " + note });
                continue;
            }
            SplitLine(line, parts, ref any);
        }
        if (!any && parts.TrueForAll(p => p.Kind != ChatPartKind.Path))
            return new List<ChatPart> { new() { Kind = ChatPartKind.Text, Text = src } };
        return parts;
    }

    static void SplitLine(string line, List<ChatPart> parts, ref bool any)
    {
        int i = 0;
        while (i < line.Length)
        {
            var m = WinPath.Match(line, i);
            if (!m.Success)
            {
                parts.Add(new ChatPart { Kind = ChatPartKind.Text, Text = line[i..] });
                return;
            }
            if (m.Index > i)
                parts.Add(new ChatPart { Kind = ChatPartKind.Text, Text = line[i..m.Index] });
            string path = m.Value.TrimEnd('.', ',', ';', '，', '。', '、');
            if (path.Length >= 4)
            {
                any = true;
                parts.Add(new ChatPart
                {
                    Kind = ChatPartKind.Path,
                    Text = ShortName(path),
                    Path = path,
                });
            }
            else
                parts.Add(new ChatPart { Kind = ChatPartKind.Text, Text = path });
            i = m.Index + m.Length;
        }
    }

    static string ShortName(string path)
    {
        string p = path.TrimEnd('\\');
        int i = p.LastIndexOf('\\');
        return i < 0 ? p : p[(i + 1)..];
    }

    static string Title(string head) => head.ToUpperInvariant() switch
    {
        "SUMMARY" or "总览" => Loc.IsEn ? "Overview" : "总览",
        "FOLDERS" or "大文件夹" => Loc.IsEn ? "Folders" : "大文件夹",
        "DELETABLE" or "可删" => Loc.IsEn ? "Likely deletable" : "可能能删",
        "KEEP" or "保留" => Loc.IsEn ? "Keep" : "别动",
        "QUESTION" => Loc.IsEn ? "Question" : "问你一句",
        _ => head,
    };
}
