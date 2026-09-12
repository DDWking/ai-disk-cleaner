using System.Diagnostics;
using System.IO;
using System.Text;

namespace AiDiskCleaner.Services;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>一条结构化日志。字段对齐「时间/级别/模块/operation ID/阶段/耗时/数量/异常类型/用户可见错误」。</summary>
public sealed record LogEntry(
    DateTime TimeUtc,
    LogLevel Level,
    string Module,
    string OperationId,
    string Stage,
    string Message,
    double? ElapsedMs = null,
    int? Count = null,
    string? ExceptionType = null,
    string? UserMessage = null)
{
    public string Format()
    {
        var sb = new StringBuilder(160);
        sb.Append(TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.Append(" [").Append(LevelTag(Level)).Append(']');
        sb.Append(" [").Append(Module).Append(']');
        if (!string.IsNullOrEmpty(OperationId)) sb.Append(" op=").Append(OperationId);
        if (!string.IsNullOrEmpty(Stage)) sb.Append(" stage=").Append(Stage);
        if (ElapsedMs is { } ms) sb.Append(" ms=").Append(ms.ToString("0"));
        if (Count is { } n) sb.Append(" n=").Append(n);
        if (!string.IsNullOrEmpty(ExceptionType)) sb.Append(" ex=").Append(ExceptionType);
        if (!string.IsNullOrEmpty(Message)) sb.Append(" | ").Append(Message);
        if (!string.IsNullOrEmpty(UserMessage)) sb.Append(" | user=").Append(UserMessage);
        return sb.ToString();
    }

    public static string LevelTag(LogLevel l) => l switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO ",
        LogLevel.Warn => "WARN ",
        _ => "ERROR",
    };
}

/// <summary>
/// 一次操作的日志作用域。自己带 operation ID 和计时，Done/Fail 时把耗时和数量写进去。
/// 用完即弃，不持有全局状态。
/// </summary>
public sealed class LogOperation : IDisposable
{
    private static int _seq;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private bool _closed;

    internal LogOperation(string module)
    {
        Module = module;
        Id = module.ToLowerInvariant()[..Math.Min(3, module.Length)]
             + "-" + DateTime.Now.ToString("HHmmss") + "-" + (++_seq % 1000).ToString("000");
    }

    public string Module { get; }
    public string Id { get; }
    public TimeSpan Elapsed => _sw.Elapsed;

    public void Stage(string stage, string? message = null, int? count = null)
        => AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Info, Module, Id, stage,
            LogRedactor.ScrubAll(message ?? ""), _sw.Elapsed.TotalMilliseconds, count));

    public void Note(string message)
        => AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, Module, Id, "", LogRedactor.ScrubAll(message)));

    public void Done(string? message = null, int? count = null)
    {
        _closed = true;
        AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Info, Module, Id, "done",
            LogRedactor.ScrubAll(message ?? "ok"), _sw.Elapsed.TotalMilliseconds, count));
    }

    public void Canceled(string? message = null)
    {
        _closed = true;
        AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Info, Module, Id, "canceled",
            LogRedactor.ScrubAll(message ?? "canceled by user"), _sw.Elapsed.TotalMilliseconds));
    }

    public void Fail(Exception ex, string? stage = null, string? userMessage = null)
    {
        _closed = true;
        var err = AppError.From(ex, Module + (stage == null ? "" : "/" + stage));
        // 取消不是错误：走 Info，避免界面上出现假故障
        var level = err.Kind == AppErrorKind.Canceled ? LogLevel.Info : LogLevel.Error;
        AppLog.Write(new LogEntry(DateTime.UtcNow, level, Module, Id, stage ?? "fail",
            err.Technical, _sw.Elapsed.TotalMilliseconds, null, ex.GetType().Name,
            userMessage ?? (err.Kind == AppErrorKind.Canceled ? null : err.UserMessage)));
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        AppLog.Write(new LogEntry(DateTime.UtcNow, LogLevel.Debug, Module, Id, "dispose",
            "scope closed without Done/Fail", _sw.Elapsed.TotalMilliseconds));
    }
}

/// <summary>
/// 应用诊断日志统一落在用户可写目录，不依赖源码 checkout 路径。
/// 写日志永远不会抛异常：日志失败不能影响扫描、删除或界面。
/// </summary>
public static class AppLog
{
    /// <summary>单个日志文件上限，超了滚动。避免长期使用后无限膨胀。</summary>
    public const long MaxFileBytes = 2 * 1024 * 1024;
    /// <summary>保留几个历史文件（app.log + app.1.log + app.2.log）。</summary>
    public const int KeepFiles = 3;

    static readonly object Gate = new();

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DashaoHuo");

    /// <summary>统一结构化日志。</summary>
    public static string LogPath => Path.Combine(DirectoryPath, "app.log");
    public static string ScanTimingPath => Path.Combine(DirectoryPath, "scan-timing.log");
    public static string UiTimingPath => Path.Combine(DirectoryPath, "ui-timing.log");

    public static void EnsureDirectory()
    {
        try { Directory.CreateDirectory(DirectoryPath); }
        catch { /* 诊断日志不能影响扫描或界面 */ }
    }

    /// <summary>开一个操作作用域，负责 operation ID 与耗时。</summary>
    public static LogOperation Begin(string module) => new(module);

    public static void Info(string module, string message, int? count = null)
        => Write(new LogEntry(DateTime.UtcNow, LogLevel.Info, module, "", "", LogRedactor.ScrubAll(message), null, count));

    public static void Warn(string module, string message)
        => Write(new LogEntry(DateTime.UtcNow, LogLevel.Warn, module, "", "", LogRedactor.ScrubAll(message)));

    public static void Error(string module, string message, Exception? ex = null)
        => Write(new LogEntry(DateTime.UtcNow, LogLevel.Error, module, "", "", LogRedactor.ScrubAll(message),
            null, null, ex?.GetType().Name, ex == null ? null : AppError.From(ex).UserMessage));

    /// <summary>把异常按分类记一笔，返回用户可读的一句话。调用方拿它去弹提示。</summary>
    public static string Record(string module, Exception ex, string? context = null)
    {
        var err = AppError.From(ex, context);
        Write(new LogEntry(DateTime.UtcNow,
            err.Kind == AppErrorKind.Canceled ? LogLevel.Info : LogLevel.Error,
            module, "", context ?? "", err.Technical, null, null,
            ex.GetType().Name, err.Kind == AppErrorKind.Canceled ? null : err.UserMessage));
        return err.UserMessage;
    }

    /// <summary>写一行。任何失败都吞掉——日志坏了不能拖垮主流程。</summary>
    public static void Write(LogEntry entry)
    {
        try
        {
            lock (Gate)
            {
                EnsureDirectory();
                RotateIfNeeded(LogPath);
                File.AppendAllText(LogPath, entry.Format() + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { }
    }

    /// <summary>追加到某个专用日志文件（scan-timing / ui-timing 这类既有文件保持兼容）。</summary>
    public static void AppendRaw(string path, string line)
    {
        try
        {
            lock (Gate)
            {
                EnsureDirectory();
                RotateIfNeeded(path);
                File.AppendAllText(path, LogRedactor.ScrubAll(line) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { }
    }

    static void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length < MaxFileBytes) return;
            for (int i = KeepFiles - 1; i >= 1; i--)
            {
                string from = i == 1 ? path : path + "." + (i - 1);
                string to = path + "." + i;
                if (File.Exists(from))
                {
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(from, to);
                }
            }
        }
        catch { /* 滚动失败就继续往原文件写，不阻断 */ }
    }

    /// <summary>
    /// 生成诊断包文本：环境信息 + 最近日志。全部经过脱敏，
    /// 不含 API Key，也不含未脱敏的用户路径。可直接交给用户贴到 issue 里。
    /// </summary>
    public static string BuildDiagnostics(int maxLogChars = 60000)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DashaoHuo diagnostics bundle");
        sb.AppendLine("generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        try
        {
            sb.AppendLine("os: " + Environment.OSVersion.VersionString);
            sb.AppendLine("64bit: " + Environment.Is64BitOperatingSystem);
            sb.AppendLine("clr: " + Environment.Version);
            sb.AppendLine("user: " + LogRedactor.Scrub(Environment.UserName));
            sb.AppendLine("machine: " + LogRedactor.Scrub(Environment.MachineName));
            sb.AppendLine("admin: " + IsElevated());
        }
        catch (Exception ex) { sb.AppendLine("env error: " + ex.GetType().Name); }

        sb.AppendLine();
        sb.AppendLine("--- log tail ---");
        sb.AppendLine(RedactedLogTail(maxLogChars));
        return sb.ToString();
    }

    /// <summary>把诊断包写到文件。返回是否成功。</summary>
    public static bool ExportDiagnostics(string targetPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(targetPath, BuildDiagnostics(), Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            Record("Diag", ex, "export");
            return false;
        }
    }

    /// <summary>读日志尾部并统一脱敏。</summary>
    public static string RedactedLogTail(int maxChars)
    {
        try
        {
            if (!File.Exists(LogPath)) return "(no log yet)";
            string text;
            using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long take = Math.Min(fs.Length, maxChars);
                fs.Seek(-take, SeekOrigin.End);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                text = reader.ReadToEnd();
            }
            return LogRedactor.ScrubPath(LogRedactor.Scrub(text));
        }
        catch (Exception ex)
        {
            return "(log unreadable: " + ex.GetType().Name + ")";
        }
    }

    static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
