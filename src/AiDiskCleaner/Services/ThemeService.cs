using System.Windows;
using System.Windows.Media;

namespace AiDiskCleaner.Services;

public sealed class ThemePalette
{
    public required Color Bg { get; init; }
    public required Color PanelLeft { get; init; }
    public required Color PanelRight { get; init; }
    public required Color Border { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentDim { get; init; }
    public required Color Text { get; init; }
    public required Color TextDim { get; init; }
    public required Color TextMuted { get; init; }
    public required Color SelectBg { get; init; }
    public required Color HoverBg { get; init; }
    public required Color CloseHoverBg { get; init; }
    public required Color Placeholder { get; init; }
    public required Color Overlay { get; init; }
    public required string Font { get; init; }
}

public static class ThemeService
{
    public static readonly ThemePalette Mono = new()
    {
        Bg = C(0x00, 0x00, 0x00),
        PanelLeft = C(0x08, 0x08, 0x08),
        PanelRight = C(0x12, 0x12, 0x12),
        Border = C(0x3A, 0x3A, 0x3A),
        Accent = C(0xFF, 0xFF, 0xFF),
        AccentDim = C(0xC8, 0xC8, 0xC8),
        Text = C(0xF2, 0xF2, 0xF2),
        TextDim = C(0x8A, 0x8A, 0x8A),
        TextMuted = C(0x5A, 0x5A, 0x5A),
        SelectBg = C(0x28, 0x28, 0x28),
        HoverBg = C(0x1A, 0x1A, 0x1A),
        CloseHoverBg = C(0x4A, 0x00, 0x00),
        Placeholder = C(0x5A, 0x5A, 0x5A),
        Overlay = Color.FromArgb(0xD0, 0x00, 0x00, 0x00),
        Font = "Cascadia Mono, Consolas, Courier New",
    };

    public static ThemePalette Current { get; private set; } = Mono;

    public static void Apply()
    {
        Current = Mono;
        var p = Current;
        var app = Application.Current;
        if (app == null) return;
        Set(app, "Bg", p.Bg);
        Set(app, "PanelLeft", p.PanelLeft);
        Set(app, "PanelRight", p.PanelRight);
        Set(app, "Border", p.Border);
        Set(app, "Accent", p.Accent);
        Set(app, "AccentDim", p.AccentDim);
        Set(app, "Text", p.Text);
        Set(app, "TextDim", p.TextDim);
        Set(app, "TextMuted", p.TextMuted);
        Set(app, "SelectBg", p.SelectBg);
        Set(app, "HoverBg", p.HoverBg);
        Set(app, "CloseHoverBg", p.CloseHoverBg);
        Set(app, "Placeholder", p.Placeholder);
        Set(app, "Overlay", p.Overlay);
        app.Resources["AppFont"] = new FontFamily(p.Font);
        Set(app, "RowAlt", Darken(p.PanelRight, 8));
        Set(app, "GridLine", Lighten(p.PanelRight, 18));
    }

    public static SolidColorBrush Brush(string key)
        => Application.Current?.TryFindResource(key) as SolidColorBrush
           ?? new SolidColorBrush(Current.Accent);

    private static void Set(Application app, string key, Color c)
        => app.Resources[key] = new SolidColorBrush(c);

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static Color Darken(Color c, int d)
        => Color.FromRgb((byte)Math.Max(0, c.R - d), (byte)Math.Max(0, c.G - d), (byte)Math.Max(0, c.B - d));

    private static Color Lighten(Color c, int d)
        => Color.FromRgb((byte)Math.Min(255, c.R + d), (byte)Math.Min(255, c.G + d), (byte)Math.Min(255, c.B + d));
}
