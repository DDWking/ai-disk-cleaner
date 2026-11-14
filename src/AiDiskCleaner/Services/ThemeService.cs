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
    /// <summary>等宽字体：只用于路径与技术信息，正文不用它。</summary>
    public required string MonoFont { get; init; }
}

public static class ThemeService
{
    public static readonly ThemePalette Mono = new()
    {
        Bg = C(0x0D, 0x0F, 0x12),
        PanelLeft = C(0x11, 0x14, 0x18),
        PanelRight = C(0x15, 0x18, 0x1E),
        Border = C(0x28, 0x2E, 0x38),
        Accent = C(0xF5, 0xF7, 0xFA),
        AccentDim = C(0xC6, 0xCD, 0xD7),
        Text = C(0xEC, 0xEF, 0xF4),
        TextDim = C(0x94, 0x9C, 0xA8),
        TextMuted = C(0x64, 0x6C, 0x78),
        SelectBg = C(0x23, 0x2A, 0x35),
        HoverBg = C(0x1D, 0x22, 0x2B),
        CloseHoverBg = C(0x5E, 0x1C, 0x1C),
        Placeholder = C(0x64, 0x6C, 0x78),
        Overlay = Color.FromArgb(0xCC, 0x06, 0x08, 0x0B),
        // 正文用正常可读的界面字体（中文优先微软雅黑）；等宽只留给路径/技术信息。
        Font = "Microsoft YaHei UI, Segoe UI, Microsoft YaHei, sans-serif",
        MonoFont = "Cascadia Mono, Consolas, Courier New",
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
        app.Resources["MonoFont"] = new FontFamily(p.MonoFont);
        Set(app, "RowAlt", Darken(p.PanelRight, 4));
        Set(app, "GridLine", C(0x21, 0x26, 0x2F));

        // 风险分区的独立视觉区域。刻意克制：不铺大面积刺眼色，
        // 只用左侧色带 + 略微不同的背景层级 + 文字/图标共同表达（不单靠颜色）。
        Set(app, "SectionSafeAccent", C(0x3E, 0xB4, 0x89));       // 柔和绿松石
        Set(app, "SectionConfirmAccent", C(0xD9, 0x8A, 0x3C));    // 暖琥珀橙
        Set(app, "SectionSafeSurface", C(0x12, 0x18, 0x1A));
        Set(app, "SectionConfirmSurface", C(0x16, 0x16, 0x19));
        Set(app, "SidebarScrim", C(0x0A, 0x0C, 0x0F));
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
