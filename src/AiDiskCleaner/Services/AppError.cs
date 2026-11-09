using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Text.Json;

namespace AiDiskCleaner.Services;

/// <summary>错误大类。界面按这个给不同的提示，日志按这个分类聚合。</summary>
public enum AppErrorKind
{
    /// <summary>用户点了停止。不是故障，不能报成异常。</summary>
    Canceled,
    /// <summary>权限不足（UAC / ACL）。</summary>
    Permission,
    /// <summary>路径不存在、过长、非法。</summary>
    Path,
    /// <summary>网络连不上（判定端点不可达 / DNS / 超时）。</summary>
    Network,
    /// <summary>数据解析失败（端点返回的结构不符合约定）。</summary>
    Parse,
    /// <summary>
    /// 模型接口拒绝了密钥（401 / 403 / 欠费 / 密钥过期）。
    ///
    /// 单独一类，因为它的处置和别的都不一样：重试没有意义，
    /// 用户要做的是**去设置里换一把密钥**，而不是检查网络。
    /// 真机反馈：以前这种错会走「未知」，界面把服务商的原文糊上去 ——
    /// 用户看到的是「出了点问题：API key expired.」，既不知道该怎么办，也不确定是不是软件坏了。
    /// </summary>
    Auth,
    /// <summary>磁盘 IO 失败。</summary>
    Io,
    /// <summary>未知。</summary>
    Unknown,
}

/// <summary>
/// 把异常翻译成「用户能懂的一句话 + 技术细节 + 分类」。
/// 用户可见文案与技术细节分开，避免把栈或英文错误码糊到界面上。
/// </summary>
public sealed record AppError(AppErrorKind Kind, string UserMessage, string Technical, Exception? Exception)
{
    /// <summary>取消不算可恢复错误，它有自己的 UI 路径。</summary>
    public bool IsRecoverable => Kind != AppErrorKind.Canceled;

    public string LogLine => $"{Kind} | {Technical}";

    public static AppError From(Exception ex, string? context = null)
    {
        var kind = Classify(ex);
        string technical = Trim(context, ex);
        return new AppError(kind, UserMessageFor(kind, ex), technical, ex);
    }

    public static AppErrorKind Classify(Exception? ex)
    {
        if (ex == null) return AppErrorKind.Unknown;
        if (ex is OperationCanceledException) return AppErrorKind.Canceled;

        if (ex is UnauthorizedAccessException || ex is SecurityException)
            return AppErrorKind.Permission;

        // 判定通道拒绝密钥：**必须排在网络判断之前** —— HttpRequestException 会被
        // 下面那条网络分支先接走，401/403 就永远归不到 Auth 了（写这行的时候真踩了一次）。
        // 只看异常本身（状态码 / 文案），不去嗅探别的东西。
        if (ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
            return AppErrorKind.Auth;
        if (ex is InvalidOperationException && LooksLikeAuthRejection(ex.Message))
            return AppErrorKind.Auth;

        if (ex is HttpRequestException || ex is SocketException || ex is WebException)
            return AppErrorKind.Network;

        if (ex is JsonException || ex is FormatException)
            return AppErrorKind.Parse;

        if (ex is PathTooLongException || ex is DirectoryNotFoundException || ex is FileNotFoundException)
            return AppErrorKind.Path;

        if (ex is IOException io)
        {
            int code = io.HResult & 0xFFFF;
            return code switch
            {
                2 or 3 or 15 or 123 => AppErrorKind.Path,
                5 or 1314 => AppErrorKind.Permission,
                32 or 33 => AppErrorKind.Io, // 占用
                _ => AppErrorKind.Io,
            };
        }

        int hr = ex.HResult & 0xFFFF;
        if (hr is 5 or 1314) return AppErrorKind.Permission;
        if (hr is 2 or 3 or 15 or 123) return AppErrorKind.Path;

        return AppErrorKind.Unknown;
    }

    /// <summary>用户可读的一句话。取消单独表达，避免被当成故障。</summary>
    public static string UserMessageFor(AppErrorKind kind, Exception? ex = null) => kind switch
    {
        AppErrorKind.Canceled => "已停止。",
        AppErrorKind.Permission => "没有权限，试试用管理员身份运行。",
        AppErrorKind.Path => "路径读不到，可能已被移动或删除。",
        AppErrorKind.Network => "网络连不上，检查一下网络或本机的 AI 服务是否启动。",
        AppErrorKind.Auth => "判定通道拒绝了这把密钥（过期 / 无效 / 额度用完）。到设置里换一把再试 —— 重试没用。",
        AppErrorKind.Parse => "返回的数据看不懂，已保留本地规则的结果。",
        AppErrorKind.Io => "磁盘读写失败，可能被别的程序占用。",
        _ => "出了点问题：" + Short(ex?.Message),
    };

    /// <summary>
    /// 判定通道拒绝密钥时，端点返回的文案长什么样。
    ///
    /// 匹配的是**服务商真实的措辞**（OpenRouter / OpenAI 风格），不是我们自己的话：
    /// <c>API key expired.</c>（真机撞到的就是这个）、<c>Invalid API key</c>、
    /// <c>Incorrect API key provided</c>、<c>insufficient credits</c>、<c>quota exceeded</c>。
    /// 判不出来就退回「未知」——**宁可不说，也不猜错**：把网络故障说成密钥问题，
    /// 用户会去换一把好密钥，然后问题还在。
    /// </summary>
    internal static bool LooksLikeAuthRejection(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        string m = message.ToLowerInvariant();
        return m.Contains("api key")
               || m.Contains("api-key")
               || m.Contains("unauthorized")
               || m.Contains("invalid key")
               || m.Contains("no auth")
               || m.Contains("insufficient credit")
               || m.Contains("insufficient_quota")
               || m.Contains("quota")
               || m.Contains("out of credits");
    }

    public static string Short(string? s)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (s.Length == 0) return "未知原因";
        return s.Length <= 160 ? s : s[..157] + "…";
    }

    /// <summary>技术细节：上下文 + 异常类型 + HRESULT + 原始消息（已脱敏）。</summary>
    public static string Trim(string? context, Exception? ex)
    {
        if (ex == null) return context ?? "";
        string msg = LogRedactor.Scrub(ex.Message);
        string s = $"{context}{(string.IsNullOrEmpty(context) ? "" : " ")}{ex.GetType().Name} hr=0x{ex.HResult:X8} {msg}";
        return s.Length <= 900 ? s : s[..900] + "…";
    }

    /// <summary>取最内层异常的简短描述（原生错误码通常在里层）。</summary>
    public static string RootMessage(Exception ex)
    {
        var e = ex;
        while (e.InnerException != null) e = e.InnerException;
        return LogRedactor.Scrub(e.Message);
    }
}
