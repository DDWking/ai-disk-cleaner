using System.IO;
using System.Windows;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Crash(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Crash(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Crash(args.Exception);
            args.SetObserved();
        };

        Settings = AppSettings.Load();
        Loc.Lang = Settings.Lang;
        ThemeService.Apply(Settings.Theme);
        Resources["SearchHintText"] = Loc.SearchHint;
        base.OnStartup(e);
    }

    static void Crash(Exception ex)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DashaoHuo");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
        }
        catch { }
        try
        {
            if (Current?.MainWindow is MainWindow w)
                w.Dispatcher.BeginInvoke(() => w.ShowCrash(ex.Message));
        }
        catch { }
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
