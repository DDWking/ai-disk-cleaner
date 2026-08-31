using System.Windows;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        Settings = AppSettings.Load();
        Loc.Lang = Settings.Lang;
        ThemeService.Apply(Settings.Theme);
        Resources["SearchHintText"] = Loc.SearchHint;
        base.OnStartup(e);
    }

    public static void SaveUi(AppTheme theme, AppLang lang)
    {
        Settings.Theme = theme;
        Settings.Lang = lang;
        Settings.Save();
        Loc.Lang = lang;
        ThemeService.Apply(theme);
        Current.Resources["SearchHintText"] = Loc.SearchHint;
        if (Current.MainWindow is MainWindow w)
            w.ApplyUi();
    }
}
