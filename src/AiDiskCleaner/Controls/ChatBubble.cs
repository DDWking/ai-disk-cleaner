using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Controls;

public sealed class ChatBubble : StackPanel
{
    public static readonly DependencyProperty LineProperty =
        DependencyProperty.Register(nameof(Line), typeof(ChatLine), typeof(ChatBubble),
            new PropertyMetadata(null, OnLine));

    public static readonly RoutedEvent PathClickEvent = MarkdownText.PathClickEvent;

    public event RoutedEventHandler PathClick
    {
        add => AddHandler(PathClickEvent, value);
        remove => RemoveHandler(PathClickEvent, value);
    }

    public ChatLine? Line
    {
        get => (ChatLine?)GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    static void OnLine(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChatBubble)d).Rebuild(e.NewValue as ChatLine);

    void Rebuild(ChatLine? line)
    {
        Children.Clear();
        if (line == null) return;
        bool log = line.Log;
        Children.Add(new TextBlock
        {
            Text = line.Who,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = Brush(log ? 0x6A : 0x8A),
        });
        var parts = line.Parts;
        if (parts.Count == 0)
        {
            Children.Add(Plain(line.Text, log));
            return;
        }
        WrapPanel? row = null;
        void NewRow()
        {
            row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            Children.Add(row);
        }
        NewRow();
        foreach (var p in parts)
        {
            if (p.Kind == ChatPartKind.Break) { NewRow(); continue; }
            if (p.Kind == ChatPartKind.Path && !string.IsNullOrEmpty(p.Path))
            {
                row!.Children.Add(PathBtn(p));
                continue;
            }
            if (!string.IsNullOrEmpty(p.Text))
                row!.Children.Add(Plain(p.Text, log));
        }
    }

    Button PathBtn(ChatPart p)
    {
        var btn = new Button
        {
            Content = p.Text,
            Tag = p.Path,
            ToolTip = (p.Path ?? "") + (string.IsNullOrEmpty(p.Note) ? "" : "\n" + p.Note),
            Margin = new Thickness(0, 2, 8, 2),
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x58)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x7E, 0xBF)),
            BorderThickness = new Thickness(1),
        };
        btn.Style = TryFindResource("GhostButton") as Style;
        btn.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x58));
        btn.Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0xFF));
        btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x7E, 0xBF));
        btn.Click += (_, _) =>
        {
            if (btn.Tag is string path)
                RaiseEvent(new PathClickEventArgs(PathClickEvent, this, path));
        };
        return btn;
    }

    static TextBlock Plain(string text, bool log)
        => new()
        {
            Text = text,
            FontSize = log ? 12 : 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(log ? 0xA8 : 0xF2),
        };

    static SolidColorBrush Brush(int g)
        => new(Color.FromRgb((byte)g, (byte)g, (byte)g));
}
