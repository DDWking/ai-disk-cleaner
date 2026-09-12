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
        ThemeService.Apply();
        Resources["SearchHintText"] = Loc.SearchHint;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 不关的话，用户退出大扫货后 sidecar 进程会残留
        try { SidecarClient.Stop(); } catch { }
        base.OnExit(e);
    }

    static void Crash(Exception ex)
    {
        // 统一日志（已脱敏）先写一笔，crash.log 是日志本身坏掉时的兜底。
        try { AppLog.Error("Crash", "unhandled exception", ex); }
        catch { /* 崩溃路径里日志失败不能再抛 */ }

        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DashaoHuo");
            Directory.CreateDirectory(dir);
            string log = Path.Combine(dir, "crash.log");
            try
            {
                var fi = new FileInfo(log);
                if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
            }
            catch
            {
                // 滚动失败就继续往原文件追加，别因为清理旧日志而丢掉这次崩溃记录。
            }
            File.AppendAllText(log,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            // 连兜底文件都写不了（磁盘满 / 无权限）：只能放弃记录，绝不能在这里再抛。
        }
        try
        {
            if (Current?.MainWindow is MainWindow w)
                w.Dispatcher.BeginInvoke(() => w.ShowCrash(ex.Message));
        }
        catch
        {
            // 主窗口已经没了（退出过程中崩溃）：没地方弹提示，忽略。
        }
    }

    public static void SaveUi(AppLang lang)
    {
        Settings.Lang = lang;
        Settings.Save();
        Loc.Lang = lang;
        ThemeService.Apply();
        Current.Resources["SearchHintText"] = Loc.SearchHint;
        if (Current.MainWindow is MainWindow w)
            w.ApplyUi();
    }
}
