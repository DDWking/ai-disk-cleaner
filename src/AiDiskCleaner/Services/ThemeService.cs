using System.Windows;
using System.Windows.Media;

namespace AiDiskCleaner.Services;

public sealed class ThemePalette
{
    public required Color Bg { get; init; }
    public required Color PanelLeft { get; init; }
    public required Color PanelRight { get; init; }
    public required Color CardSurface { get; init; }
    public required Color Border { get; init; }
    public required Color BorderStrong { get; init; }
    public required Color GridLine { get; init; }
    public required Color RowAlt { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentDim { get; init; }
    public required Color BrandAccent { get; init; }
    public required Color BrandAccentHover { get; init; }
    public required Color BrandAccentSoft { get; init; }
    public required Color BrandAccentFg { get; init; }
    public required Color Text { get; init; }
    public required Color TextDim { get; init; }
    public required Color TextMuted { get; init; }
    public required Color SelectBg { get; init; }
    public required Color HoverBg { get; init; }
    public required Color BarTrack { get; init; }
    public required Color CloseHoverBg { get; init; }
    public required Color CloseHoverFg { get; init; }
    public required Color DangerBg { get; init; }
    public required Color DangerHoverBg { get; init; }
    public required Color DangerFg { get; init; }
    public required Color Placeholder { get; init; }
    public required Color Overlay { get; init; }

    public required Color RingOrange { get; init; }
    public required Color RingCharcoal { get; init; }
    public required Color RingMidGray { get; init; }
    public required Color RingLightGray { get; init; }

    public required Color SectionSafeAccent { get; init; }
    public required Color SectionConfirmAccent { get; init; }
    public required Color SectionSafeSurface { get; init; }
    public required Color SectionConfirmSurface { get; init; }
    public required Color SidebarScrim { get; init; }

    public required Color ToneGreenAccent { get; init; }
    public required Color ToneGreenBg { get; init; }
    public required Color ToneGreenRowBg { get; init; }
    public required Color ToneYellowAccent { get; init; }
    public required Color ToneYellowBg { get; init; }
    public required Color ToneYellowRowBg { get; init; }
    public required Color ToneRedAccent { get; init; }
    public required Color ToneRedBg { get; init; }
    public required Color ToneRedRowBg { get; init; }

    public required Color JumpHighlightBg { get; init; }
    public required Color JumpHighlightBorder { get; init; }

    public required string Font { get; init; }
    /// <summary>等宽字体：只用于路径与技术信息，正文不用它。</summary>
    public required string MonoFont { get; init; }
}

public static class ThemeService
{
    private const string DefaultFont = "Microsoft YaHei UI, Segoe UI Variable Text, Segoe UI, sans-serif";
    private const string DefaultMonoFont = "Cascadia Mono, Consolas, Courier New";

    /// <summary>默认主题（方案 1）：瑞士纯白 + 亮橙。</summary>
    public static readonly ThemePalette SwissLight = new()
    {
        Bg = C(0xFA, 0xF9, 0xF7),
        PanelLeft = C(0xF7, 0xF6, 0xF4),
        PanelRight = C(0xFF, 0xFF, 0xFF),
        CardSurface = C(0xFF, 0xFF, 0xFF),
        Border = C(0xEB, 0xEA, 0xE6),
        BorderStrong = C(0xD4, 0xD2, 0xCC),
        GridLine = C(0xF0, 0xEF, 0xEA),
        RowAlt = C(0xFC, 0xFC, 0xFB),
        Accent = C(0x16, 0x16, 0x15),
        AccentDim = C(0x5A, 0x59, 0x55),
        BrandAccent = C(0xFF, 0x4D, 0x00),
        BrandAccentHover = C(0xE6, 0x45, 0x00),
        BrandAccentSoft = C(0xFF, 0xF1, 0xEB),
        BrandAccentFg = C(0xFF, 0xFF, 0xFF),
        Text = C(0x16, 0x16, 0x15),
        TextDim = C(0x6E, 0x6D, 0x68),
        TextMuted = C(0xA3, 0xA1, 0x9B),
        SelectBg = C(0xFF, 0xF2, 0xEC),
        HoverBg = C(0xF4, 0xF3, 0xEF),
        BarTrack = C(0xEA, 0xE9, 0xE4),
        CloseHoverBg = C(0xFF, 0x4D, 0x00),
        CloseHoverFg = C(0xFF, 0xFF, 0xFF),
        DangerBg = C(0xFF, 0x4D, 0x00),
        DangerHoverBg = C(0xE6, 0x45, 0x00),
        DangerFg = C(0xFF, 0xFF, 0xFF),
        Placeholder = C(0xA3, 0xA1, 0x9B),
        Overlay = Color.FromArgb(0x5C, 0x16, 0x16, 0x15),

        RingOrange = C(0xFF, 0x4D, 0x00),
        RingCharcoal = C(0x6E, 0x6D, 0x6A),
        RingMidGray = C(0xAB, 0xA9, 0xA2),
        RingLightGray = C(0xE0, 0xDF, 0xDA),

        SectionSafeAccent = C(0xFF, 0x4D, 0x00),
        SectionConfirmAccent = C(0x6E, 0x6D, 0x6A),
        SectionSafeSurface = C(0xFF, 0xF8, 0xF5),
        SectionConfirmSurface = C(0xF7, 0xF6, 0xF3),
        SidebarScrim = C(0xE8, 0xE6, 0xE0),

        ToneGreenAccent = C(0xFF, 0x4D, 0x00),
        ToneGreenBg = C(0xFF, 0xF1, 0xEB),
        ToneGreenRowBg = C(0xFF, 0xFF, 0xFF),

        ToneYellowAccent = C(0x68, 0x67, 0x64),
        ToneYellowBg = C(0xF2, 0xF1, 0xED),
        ToneYellowRowBg = C(0xFF, 0xFF, 0xFF),

        ToneRedAccent = C(0xA8, 0xA6, 0xA0),
        ToneRedBg = C(0xF2, 0xF1, 0xED),
        ToneRedRowBg = C(0xFF, 0xFF, 0xFF),

        JumpHighlightBg = C(0xFF, 0xF0, 0xE8),
        JumpHighlightBorder = C(0xFF, 0x4D, 0x00),
        Font = DefaultFont,
        MonoFont = DefaultMonoFont,
    };

    /// <summary>深色主题：纯净黑曜碳灰 + 熔岩亮橙。</summary>
    public static readonly ThemePalette CarbonDark = new()
    {
        Bg = C(0x0A, 0x0A, 0x0B),
        PanelLeft = C(0x0E, 0x0E, 0x10),
        PanelRight = C(0x11, 0x11, 0x13),
        CardSurface = C(0x16, 0x16, 0x19),
        Border = C(0x22, 0x22, 0x26),
        BorderStrong = C(0x34, 0x34, 0x3B),
        GridLine = C(0x1B, 0x1B, 0x1F),
        RowAlt = C(0x14, 0x14, 0x17),
        Accent = C(0xF4, 0xF4, 0xF5),
        AccentDim = C(0xB4, 0xB4, 0xBC),
        BrandAccent = C(0xFF, 0x5C, 0x16),
        BrandAccentHover = C(0xFF, 0x70, 0x33),
        BrandAccentSoft = C(0x2B, 0x16, 0x0D),
        BrandAccentFg = C(0xFF, 0xFF, 0xFF),
        Text = C(0xF4, 0xF4, 0xF5),
        TextDim = C(0x9E, 0x9E, 0xA7),
        TextMuted = C(0x63, 0x63, 0x6B),
        SelectBg = C(0x24, 0x16, 0x10),
        HoverBg = C(0x19, 0x19, 0x1D),
        BarTrack = C(0x23, 0x23, 0x28),
        CloseHoverBg = C(0xFF, 0x5C, 0x16),
        CloseHoverFg = C(0xFF, 0xFF, 0xFF),
        DangerBg = C(0xFF, 0x5C, 0x16),
        DangerHoverBg = C(0xFF, 0x70, 0x33),
        DangerFg = C(0xFF, 0xFF, 0xFF),
        Placeholder = C(0x63, 0x63, 0x6B),
        Overlay = Color.FromArgb(0xB8, 0x06, 0x06, 0x07),

        RingOrange = C(0xFF, 0x5C, 0x16),
        RingCharcoal = C(0x68, 0x68, 0x72),
        RingMidGray = C(0x3D, 0x3D, 0x45),
        RingLightGray = C(0x22, 0x22, 0x27),

        SectionSafeAccent = C(0xFF, 0x5C, 0x16),
        SectionConfirmAccent = C(0x8E, 0x8E, 0x96),
        SectionSafeSurface = C(0x18, 0x13, 0x11),
        SectionConfirmSurface = C(0x15, 0x15, 0x18),
        SidebarScrim = C(0x0A, 0x0A, 0x0B),

        ToneGreenAccent = C(0xFF, 0x5C, 0x16),
        ToneGreenBg = C(0x2A, 0x15, 0x0C),
        ToneGreenRowBg = C(0x11, 0x11, 0x13),

        ToneYellowAccent = C(0xA1, 0xA1, 0xAA),
        ToneYellowBg = C(0x1F, 0x1F, 0x24),
        ToneYellowRowBg = C(0x11, 0x11, 0x13),

        ToneRedAccent = C(0x63, 0x63, 0x6B),
        ToneRedBg = C(0x1C, 0x1C, 0x20),
        ToneRedRowBg = C(0x11, 0x11, 0x13),

        JumpHighlightBg = C(0x2A, 0x17, 0x0E),
        JumpHighlightBorder = C(0xFF, 0x5C, 0x16),
        Font = DefaultFont,
        MonoFont = DefaultMonoFont,
    };

    public static readonly ThemePalette ObsidianDark = CarbonDark;
    public static readonly ThemePalette FluentLight = SwissLight;
    public static readonly ThemePalette Mono = SwissLight;

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;
    public static ThemePalette Current { get; private set; } = SwissLight;

    public static void Apply() => Apply(CurrentTheme);

    public static void Apply(AppTheme theme)
    {
        CurrentTheme = theme;
        Current = theme == AppTheme.Dark ? CarbonDark : SwissLight;
        var p = Current;
        var app = Application.Current;
        if (app == null) return;

        Set(app, "Bg", p.Bg);
        Set(app, "PanelLeft", p.PanelLeft);
        Set(app, "PanelRight", p.PanelRight);
        Set(app, "CardSurface", p.CardSurface);
        Set(app, "Border", p.Border);
        Set(app, "BorderStrong", p.BorderStrong);
        Set(app, "GridLine", p.GridLine);
        Set(app, "RowAlt", p.RowAlt);
        Set(app, "Accent", p.Accent);
        Set(app, "AccentDim", p.AccentDim);
        Set(app, "BrandAccent", p.BrandAccent);
        Set(app, "BrandAccentHover", p.BrandAccentHover);
        Set(app, "BrandAccentSoft", p.BrandAccentSoft);
        Set(app, "BrandAccentFg", p.BrandAccentFg);
        Set(app, "Text", p.Text);
        Set(app, "TextDim", p.TextDim);
        Set(app, "TextMuted", p.TextMuted);
        Set(app, "SelectBg", p.SelectBg);
        Set(app, "HoverBg", p.HoverBg);
        Set(app, "BarTrack", p.BarTrack);
        Set(app, "CloseHoverBg", p.CloseHoverBg);
        Set(app, "CloseHoverFg", p.CloseHoverFg);
        Set(app, "DangerBg", p.DangerBg);
        Set(app, "DangerHoverBg", p.DangerHoverBg);
        Set(app, "DangerFg", p.DangerFg);
        Set(app, "Placeholder", p.Placeholder);
        Set(app, "Overlay", p.Overlay);

        Set(app, "RingOrange", p.RingOrange);
        Set(app, "RingCharcoal", p.RingCharcoal);
        Set(app, "RingMidGray", p.RingMidGray);
        Set(app, "RingLightGray", p.RingLightGray);

        app.Resources["AppFont"] = new FontFamily(p.Font);
        app.Resources["MonoFont"] = new FontFamily(p.MonoFont);

        Set(app, "SectionSafeAccent", p.SectionSafeAccent);
        Set(app, "SectionConfirmAccent", p.SectionConfirmAccent);
        Set(app, "SectionSafeSurface", p.SectionSafeSurface);
        Set(app, "SectionConfirmSurface", p.SectionConfirmSurface);
        Set(app, "SidebarScrim", p.SidebarScrim);

        Set(app, "ToneGreenAccent", p.ToneGreenAccent);
        Set(app, "ToneGreenBg", p.ToneGreenBg);
        Set(app, "ToneGreenRowBg", p.ToneGreenRowBg);

        Set(app, "ToneYellowAccent", p.ToneYellowAccent);
        Set(app, "ToneYellowBg", p.ToneYellowBg);
        Set(app, "ToneYellowRowBg", p.ToneYellowRowBg);

        Set(app, "ToneRedAccent", p.ToneRedAccent);
        Set(app, "ToneRedBg", p.ToneRedBg);
        Set(app, "ToneRedRowBg", p.ToneRedRowBg);

        Set(app, "JumpHighlightBg", p.JumpHighlightBg);
        Set(app, "JumpHighlightBorder", p.JumpHighlightBorder);

        SetSystemBrush(app, SystemColors.HighlightBrushKey, p.SelectBg);
        SetSystemBrush(app, SystemColors.HighlightTextBrushKey, p.Text);
        SetSystemBrush(app, SystemColors.InactiveSelectionHighlightBrushKey, p.SelectBg);
        SetSystemBrush(app, SystemColors.InactiveSelectionHighlightTextBrushKey, p.Text);
        SetSystemBrush(app, SystemColors.ControlBrushKey, p.SelectBg);
    }

    public static SolidColorBrush Brush(string key)
        => Application.Current?.TryFindResource(key) as SolidColorBrush
           ?? new SolidColorBrush(Current.Accent);

    private static void Set(Application app, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        app.Resources[key] = b;
    }

    private static void SetSystemBrush(Application app, ResourceKey key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        app.Resources[key] = b;
    }

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
