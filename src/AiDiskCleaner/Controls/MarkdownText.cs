using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AiDiskCleaner.Controls;

public sealed class MarkdownText : TextBlock
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(MarkdownText),
            new PropertyMetadata("", OnChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MarkdownText)d).Rebuild(e.NewValue as string ?? "");

    void Rebuild(string src)
    {
        Inlines.Clear();
        if (string.IsNullOrEmpty(src)) return;
        var lines = src.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) Inlines.Add(new LineBreak());
            AddLine(lines[i]);
        }
    }

    void AddLine(string line)
    {
        string s = line.TrimEnd();
        if (s.StartsWith("### ")) s = s[4..];
        else if (s.StartsWith("## ")) s = s[3..];
        else if (s.StartsWith("# ")) s = s[2..];
        if (s.StartsWith("- ")) s = "• " + s[2..];
        else if (s.StartsWith("* ") && !s.StartsWith("**")) s = "• " + s[2..];
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '`' && Find(s, i + 1, '`') is int tick)
            {
                Inlines.Add(Code(s[(i + 1)..tick]));
                i = tick + 1;
                continue;
            }
            if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*' && Find(s, i + 2, "**") is int star)
            {
                Inlines.Add(new Bold(new Run(s[(i + 2)..star])));
                i = star + 2;
                continue;
            }
            int next = NextMark(s, i);
            Inlines.Add(new Run(s[i..next]));
            i = next;
        }
    }

    static int NextMark(string s, int from)
    {
        for (int i = from + 1; i < s.Length; i++)
        {
            if (s[i] == '`') return i;
            if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '*') return i;
        }
        return s.Length;
    }

    static int? Find(string s, int from, char c)
    {
        int i = s.IndexOf(c, from);
        return i < 0 ? null : i;
    }

    static int? Find(string s, int from, string token)
    {
        int i = s.IndexOf(token, from, StringComparison.Ordinal);
        return i < 0 ? null : i;
    }

    Run Code(string text)
    {
        var run = new Run(text);
        run.Foreground = TryFindResource("AccentDim") as Brush ?? Foreground;
        return run;
    }
}
