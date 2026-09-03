using System.ComponentModel;
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

    ChatLine? _bound;
    TextBlock? _live;

    static void OnLine(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChatBubble)d).Bind(e.NewValue as ChatLine);

    void Bind(ChatLine? line)
    {
        if (_bound != null) _bound.PropertyChanged -= OnProp;
        _bound = line;
        if (_bound != null) _bound.PropertyChanged += OnProp;
        Rebuild(_bound);
    }

    void OnProp(object? sender, PropertyChangedEventArgs e)
    {
        if (_live != null && e.PropertyName == nameof(ChatLine.Text) && (_bound?.Parts.Count ?? 0) == 0)
        {
            _live.Text = _bound?.Text ?? "";
            return;
        }
        Rebuild(_bound);
    }

    void Rebuild(ChatLine? line)
    {
        Children.Clear();
        if (line == null) return;
        bool log = line.Log;
        Children.Add(new TextBlock
        {
            Text = line.Who,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = Brush(log ? 0x6A : 0x8A),
        });
        if (log || line.Parts.Count == 0)
        {
            _live = Plain(line.Text, log, wrap: true);
            Children.Add(_live);
            return;
        }
        _live = null;
        foreach (var p in line.Parts)
        {
            if (p.Kind == ChatPartKind.Break) continue;
            if (p.Kind == ChatPartKind.Heading)
            {
                Children.Add(new TextBlock
                {
                    Text = p.Text,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 10, 0, 6),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                });
                continue;
            }
            if (p.Kind == ChatPartKind.Path && !string.IsNullOrEmpty(p.Path))
            {
                Children.Add(PathRow(p));
                continue;
            }
            if (!string.IsNullOrWhiteSpace(p.Text))
                Children.Add(Plain(p.Text, false, wrap: true));
        }
    }

    Border PathRow(ChatPart p)
    {
        var row = new DockPanel { LastChildFill = true };
        var btn = new Button
        {
            Content = p.Text,
            Tag = p.Path,
            ToolTip = p.Path,
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x58)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x7E, 0xBF)),
            BorderThickness = new Thickness(1),
        };
        btn.Click += (_, _) =>
        {
            if (btn.Tag is string path)
                RaiseEvent(new PathClickEventArgs(PathClickEvent, this, path));
        };
        DockPanel.SetDock(btn, Dock.Left);
        row.Children.Add(btn);
        row.Children.Add(new TextBlock
        {
            Text = p.Note ?? "",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(0xC0),
        });
        return new Border
        {
            Child = row,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(0, 1, 0, 1),
        };
    }

    static TextBlock Plain(string text, bool log, bool wrap)
        => new()
        {
            Text = text,
            FontSize = log ? 12 : 13,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = Brush(log ? 0xA8 : 0xE8),
        };

    static SolidColorBrush Brush(int g)
        => new(Color.FromRgb((byte)g, (byte)g, (byte)g));
}
