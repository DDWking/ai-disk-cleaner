using System.IO;
using System.Windows;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>启动期致命异常的受控退出码（非零：脚本和用户都能看出这是失败）。</summary>
    public const int FatalStartupExitCode = 70;

    /// <summary>崩溃处理的判定结果（回归测试用）。</summary>
    public enum CrashOutcome { None, RuntimeHandled, FatalStartup, Suppressed }

    /// <summary>运行期提示最多弹几次（运行期异常风暴也要有界）。</summary>
    const int MaxRuntimeAlerts = 3;

    static int _crashGate;      // 1 = 正在处理崩溃（防止处理器自己再抛时递归）
    static int _fatalLatched;   // 1 = 已经处理过启动期致命异常（后续一律丢弃）
    static int _runtimeAlerts;

    /// <summary>
    /// 测试钩子：非 null 时接管崩溃提示（不弹真实对话框），便于断言"提示路径也不会再抛"。
    /// 生产代码不要设置它。
    /// </summary>
    public static Action<Exception, bool>? NoticeHookForTest { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            // 启动期致命异常不吞：HandleCrash 会记录原始原因并受控退出，
            // 绝不再"吞掉 + 继续跑"，那正是留下无窗口空转进程的原因。
            HandleCrash(args.Exception, UiReady(), fatal: true);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) HandleCrash(ex, UiReady(), fatal: true);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            // 未观察的任务异常通常是局部的：只记不退出，也不当作启动期致命
            HandleCrash(args.Exception, UiReady(), fatal: false);
            args.SetObserved();
        };

        Settings = AppSettings.Load();
        Loc.Lang = Settings.Lang;
        // 组合根：**在这里**把真实通道接上统一入口。
        //
        // 不能只靠 AiClient 的静态构造函数去接（它里面确实也调了一次 Register）——
        // 静态构造只在「有东西碰到 AiClient」时才跑。判定通道走的是 AiProtocols
        // （刻意做成不碰 AiClient，好让设置页读协议不触发它的静态构造），
        // 于是从没碰过 AiClient 的进程里 DecisionsSender 一直是 null，
        // 用户一点「识别用途」就报 "decisions channel is not registered"。
        AiGateways.Register();
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

    /// <summary>主窗口是否已经完成构造（所有 x:Name 控件都已赋值）。</summary>
    static bool UiReady() => Current?.MainWindow is MainWindow w && w.IsUiReady;

    static void HandleCrash(Exception? ex, bool uiReady, bool fatal)
        => _ = HandleCrashCore(ex, uiReady, fatal, exitOnFatal: true);

    /// <summary>回归测试入口：走同一条判定，但不真的退出进程。</summary>
    public static CrashOutcome HandleCrashForTest(Exception? ex, bool uiReady)
        => HandleCrashCore(ex, uiReady, fatal: true, exitOnFatal: false);

    /// <summary>回归测试入口：清空闸门与计数。</summary>
    public static void ResetCrashStateForTest()
    {
        Interlocked.Exchange(ref _crashGate, 0);
        Interlocked.Exchange(ref _fatalLatched, 0);
        Interlocked.Exchange(ref _runtimeAlerts, 0);
    }

    /// <summary>
    /// 统一的崩溃处理。三道防线：
    /// <list type="number">
    /// <item><b>启动期致命闩锁</b>：一旦判定过启动期致命，后续异常全部丢弃 ——
    /// 半初始化窗口 + 异步调度曾经在这里形成无限递归；</item>
    /// <item><b>重入闸</b>：处理器自己在记录/提示时再抛，直接返回，绝不递归；</item>
    /// <item><b>回调自身安全</b>：异步提示回调内部再包一层 try，
    /// 因为它一旦抛就会重新触发 DispatcherUnhandledException（形成死循环）。</item>
    /// </list>
    /// 启动期致命 ⇒ 记录原始原因 + 安全提示 + <b>受控非零退出</b>，
    /// 不留下"没有窗口但在空转"的进程。
    /// </summary>
    static CrashOutcome HandleCrashCore(Exception? ex, bool uiReady, bool fatal, bool exitOnFatal)
    {
        if (ex is null) return CrashOutcome.None;
        if (Volatile.Read(ref _fatalLatched) == 1) return CrashOutcome.Suppressed;
        if (Interlocked.CompareExchange(ref _crashGate, 1, 0) != 0) return CrashOutcome.Suppressed;

        try
        {
            // 先落第一现场：它独立成文件，不会被后面的日志风暴轮转掉
            CrashFirstRecord.Save(ex);
            try { AppLog.Error("Crash", "unhandled exception", ex); } catch { /* 已由 AppLog 自行兜底 */ }

            bool startupFatal = fatal && !uiReady;
            if (startupFatal)
            {
                Interlocked.Exchange(ref _fatalLatched, 1);   // 闩死：之后的异常不再处理
                Notify(ex, fatal: true);
                if (exitOnFatal) ExitControlled();
                return CrashOutcome.FatalStartup;
            }

            if (Interlocked.Increment(ref _runtimeAlerts) > MaxRuntimeAlerts)
                return CrashOutcome.Suppressed;

            var w = Current?.MainWindow as MainWindow;
            if (w is { IsUiReady: true })
            {
                try
                {
                    w.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { w.ShowCrash(ex.Message); }
                        catch { /* 提示失败绝不能变成第二轮崩溃 */ }
                    }));
                }
                catch
                {
                    Notify(ex, fatal: false);      // 调不动调度器就退回安全提示
                }
            }
            else
            {
                // 窗口半初始化：**绝不碰 MainWindow 的控件**（AlertText 等可能还是 null）
                Notify(ex, fatal: false);
            }
            return CrashOutcome.RuntimeHandled;
        }
        catch
        {
            return CrashOutcome.Suppressed;   // 崩溃路径里绝不能再抛
        }
        finally
        {
            Interlocked.Exchange(ref _crashGate, 0);
        }
    }

    /// <summary>
    /// 独立于 MainWindow 控件的安全提示（半初始化时唯一可用的通道）。
    /// 内容脱敏后再给用户看；这里不抛。
    /// </summary>
    static void Notify(Exception ex, bool fatal)
    {
        if (NoticeHookForTest is { } hook)
        {
            try { hook(ex, fatal); } catch { /* 钩子自己抛也不能影响崩溃路径 */ }
            return;
        }
        try
        {
            string msg = LogRedactor.ScrubAll(ex.Message ?? "");
            string text = fatal
                ? Loc.AppName + " 启动失败，程序将退出。" + Environment.NewLine + Environment.NewLine
                  + ex.GetType().Name + ": " + msg + Environment.NewLine + Environment.NewLine
                  + "原始原因已记录到：" + Environment.NewLine + CrashFirstRecord.FilePath
                : Loc.AppName + " 遇到一个问题。" + Environment.NewLine + Environment.NewLine
                  + ex.GetType().Name + ": " + msg;
            MessageBox.Show(text, Loc.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* 连提示都弹不出来时也不能抛 */ }
    }

    /// <summary>受控非零退出：不留空转进程。</summary>
    static void ExitControlled()
    {
        try { SidecarClient.Stop(); } catch { }
        try { Current?.Shutdown(FatalStartupExitCode); } catch { }
        Environment.Exit(FatalStartupExitCode);
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
